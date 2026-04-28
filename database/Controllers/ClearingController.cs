using database.Data;
using database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace database.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClearingController : ControllerBase
    {
        private readonly MetroDbContext _context;

        private const int FirstRideWaitMinutes = 30;
        private const int TransferWaitMinutes = 45;
        private const int ExitToleranceMinutes = 20;

        public ClearingController(MetroDbContext context)
        {
            _context = context;
        }

        [HttpGet("rules")]
        public async Task<IActionResult> GetRules()
        {
            var algorithmRule = await _context.ClearingRules
                .AsNoTracking()
                .Where(x => x.RuleType == "ALGORITHM" && x.IsActive)
                .OrderByDescending(x => x.RuleId)
                .FirstOrDefaultAsync();

            var shareRule = await _context.ClearingRules
                .AsNoTracking()
                .Where(x => x.RuleType == "SHARE" && x.IsActive)
                .OrderByDescending(x => x.RuleId)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                clearingMode = algorithmRule?.AlgorithmType ?? "shortest",
                transferCoefficient = shareRule?.TransferCoefficient ?? 1.1m
            });
        }

        [HttpPost("rules")]
        public async Task<IActionResult> SaveRules([FromBody] SaveClearingRulesRequest request)
        {
            var mode = string.IsNullOrWhiteSpace(request.ClearingMode)
                ? "shortest"
                : request.ClearingMode.Trim().ToLower();

            if (mode != "shortest" && mode != "multi")
            {
                return BadRequest(new { message = "清分模式只能是 shortest 或 multi。" });
            }

            if (request.TransferCoefficient <= 0)
            {
                return BadRequest(new { message = "换乘贡献系数必须大于 0。" });
            }

            var now = DateTime.Now;

            var algorithmRules = await _context.ClearingRules
                .Where(x => x.RuleType == "ALGORITHM")
                .ToListAsync();

            foreach (var rule in algorithmRules)
            {
                rule.IsActive = string.Equals(
                    rule.AlgorithmType,
                    mode,
                    StringComparison.OrdinalIgnoreCase
                );
                rule.UpdatedAt = now;
            }

            var shareRule = await _context.ClearingRules
                .Where(x => x.RuleType == "SHARE")
                .OrderByDescending(x => x.RuleId)
                .FirstOrDefaultAsync();

            if (shareRule == null)
            {
                shareRule = new ClearingRule
                {
                    RuleCode = "SHARE001",
                    RuleName = "换乘贡献系数",
                    RuleType = "SHARE",
                    TransferCoefficient = request.TransferCoefficient,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _context.ClearingRules.Add(shareRule);
            }
            else
            {
                shareRule.TransferCoefficient = request.TransferCoefficient;
                shareRule.IsActive = true;
                shareRule.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "规则保存成功",
                clearingMode = mode,
                transferCoefficient = request.TransferCoefficient
            });
        }

        [HttpPost("run")]
        public async Task<IActionResult> RunClearing()
        {
            var startTime = DateTime.Now;

            var algorithmRule = await _context.ClearingRules
                .AsNoTracking()
                .Where(x => x.IsActive && x.RuleType == "ALGORITHM")
                .OrderByDescending(x => x.RuleId)
                .FirstOrDefaultAsync();

            var shareRule = await _context.ClearingRules
                .AsNoTracking()
                .Where(x => x.IsActive && x.RuleType == "SHARE")
                .OrderByDescending(x => x.RuleId)
                .FirstOrDefaultAsync();

            var algorithmType = (algorithmRule?.AlgorithmType ?? "shortest").Trim().ToLower();
            var transferCoefficient = shareRule?.TransferCoefficient ?? 1.1m;

            if (algorithmType != "shortest" && algorithmType != "multi")
            {
                algorithmType = "shortest";
            }

            var task = new ClearingTask
            {
                TaskName = $"清分任务_{DateTime.Now:yyyyMMddHHmmss}",
                DataCount = 0,
                AlgorithmType = algorithmType,
                Status = "RUNNING",
                StartTime = startTime,
                CreatedAt = startTime
            };

            _context.ClearingTasks.Add(task);
            await _context.SaveChangesAsync();

            try
            {
                var normalTransactions = await (
                    from t in _context.TicketTransactions.AsNoTracking()
                    join entryStation in _context.StationInfos.AsNoTracking()
                        on t.EntryStationId equals entryStation.StationId into entryJoin
                    from entryStation in entryJoin.DefaultIfEmpty()
                    join exitStation in _context.StationInfos.AsNoTracking()
                        on t.ExitStationId equals exitStation.StationId into exitJoin
                    from exitStation in exitJoin.DefaultIfEmpty()
                    where t.TransactionStatus == "NORMAL"
                          && t.EntryStationId != null
                          && t.ExitStationId != null
                    orderby t.TransactionId
                    select new
                    {
                        t.TransactionId,
                        t.CardNo,
                        t.EntryTime,
                        t.ExitTime,
                        t.EntryStationId,
                        t.ExitStationId,
                        EntryStationName = entryStation != null ? entryStation.StationName : "无进站记录",
                        ExitStationName = exitStation != null ? exitStation.StationName : "无出站记录",
                        t.PayAmount
                    }
                ).ToListAsync();

                var exceptionCount = await _context.TicketTransactions
                    .AsNoTracking()
                    .CountAsync(x => x.TransactionStatus == "EXCEPTION");

                var lines = await _context.LineInfos
                    .AsNoTracking()
                    .ToListAsync();

                var stations = await _context.StationInfos
                    .AsNoTracking()
                    .ToListAsync();

                var sections = await _context.SectionInfos
                    .AsNoTracking()
                    .ToListAsync();

                var activeTimetables = await _context.TrainTimetables
                    .AsNoTracking()
                    .Where(x => x.Status == 1 && x.IsActive == 1)
                    .ToListAsync();

                var activeTimetableIds = activeTimetables
                    .Select(x => x.TimetableId)
                    .ToList();

                var timetableDetails = await _context.TrainTimetableDetails
                    .AsNoTracking()
                    .Where(x => activeTimetableIds.Contains(x.TimetableId))
                    .ToListAsync();

                var lineMap = lines.ToDictionary(x => x.LineId, x => x);
                var stationMap = stations.ToDictionary(x => x.StationId, x => x);
                var graph = BuildGraph(sections);

                var clearingResults = new List<ClearingResult>();
                var unmatchedTransactions = new List<ClearingUnmatchedTransaction>();
                var lineSummary = new Dictionary<string, decimal>();
                var tradeSummaries = new List<object>();

                foreach (var line in lines)
                {
                    lineSummary[line.LineName] = 0m;
                }

                var timetableMatchedTradeCount = 0;

                foreach (var tx in normalTransactions)
                {
                    if (tx.EntryStationId == null || tx.ExitStationId == null)
                    {
                        continue;
                    }

                    var selectedPaths = new List<PathResult>();

                    if (algorithmType == "multi")
                    {
                        selectedPaths = FindCandidatePaths(
                            graph,
                            tx.EntryStationId.Value,
                            tx.ExitStationId.Value,
                            maxExtraDepth: 3,
                            maxPaths: 12
                        );
                    }
                    else
                    {
                        var shortestPath = FindShortestPath(
                            graph,
                            tx.EntryStationId.Value,
                            tx.ExitStationId.Value
                        );

                        if (shortestPath != null)
                        {
                            selectedPaths.Add(shortestPath);
                        }
                    }

                    if (selectedPaths.Count == 0)
                    {
                        unmatchedTransactions.Add(CreateUnmatchedRecord(
                            task.TaskId,
                            tx.TransactionId,
                            tx.CardNo,
                            tx.EntryTime,
                            tx.EntryStationId,
                            tx.ExitTime,
                            tx.ExitStationId,
                            "NO_PATH",
                            "进站站点与出站站点之间未找到可用路径"
                        ));

                        continue;
                    }

                    var timetableFilterResult = FilterPathsByPublishedTimetableWithReason(
                        selectedPaths,
                        tx.EntryTime,
                        tx.ExitTime,
                        activeTimetables,
                        timetableDetails
                    );

                    if (timetableFilterResult.Paths.Count == 0)
                    {
                        unmatchedTransactions.Add(CreateUnmatchedRecord(
                            task.TaskId,
                            tx.TransactionId,
                            tx.CardNo,
                            tx.EntryTime,
                            tx.EntryStationId,
                            tx.ExitTime,
                            tx.ExitStationId,
                            timetableFilterResult.ReasonCode,
                            timetableFilterResult.ReasonMessage
                        ));

                        continue;
                    }

                    selectedPaths = timetableFilterResult.Paths;
                    var clearingBasis = "已发布时刻表匹配";
                    timetableMatchedTradeCount++;

                    var totalPathWeight = selectedPaths.Sum(CalculatePathWeight);

                    if (totalPathWeight <= 0)
                    {
                        continue;
                    }

                    var detailCount = 0;
                    var pathIndex = 1;
                    var transactionAllocated = 0m;

                    foreach (var path in selectedPaths)
                    {
                        decimal pathAmount;

                        if (pathIndex == selectedPaths.Count)
                        {
                            pathAmount = Math.Round(tx.PayAmount - transactionAllocated, 2);
                        }
                        else
                        {
                            pathAmount = Math.Round(
                                tx.PayAmount * CalculatePathWeight(path) / totalPathWeight,
                                2
                            );
                            transactionAllocated += pathAmount;
                        }

                        path.TotalFareAmount = pathAmount;

                        var allocations = AllocateByPath(
                            path,
                            lineMap,
                            algorithmType,
                            transferCoefficient
                        );

                        var pathText = BuildPathText(path.StationIds, stationMap);
                        var transferText = BuildTransferText(path.TransferStationIds, stationMap);

                        foreach (var allocation in allocations)
                        {
                            var result = new ClearingResult
                            {
                                TaskId = task.TaskId,
                                TransactionId = tx.TransactionId,
                                LineId = allocation.LineId,
                                OperatorId = allocation.OperatorId,
                                ClearingAmount = allocation.Amount,
                                PathText = pathText,
                                TransferText = transferText,
                                PathGroup = $"{clearingBasis}-方案{pathIndex}",
                                CreatedAt = DateTime.Now
                            };

                            clearingResults.Add(result);
                            detailCount++;

                            if (lineMap.TryGetValue(allocation.LineId, out var lineInfo))
                            {
                                if (!lineSummary.ContainsKey(lineInfo.LineName))
                                {
                                    lineSummary[lineInfo.LineName] = 0m;
                                }

                                lineSummary[lineInfo.LineName] += allocation.Amount;
                            }
                        }

                        pathIndex++;
                    }

                    tradeSummaries.Add(new
                    {
                        transactionId = tx.TransactionId,
                        cardNo = tx.CardNo,
                        entryStationName = tx.EntryStationName,
                        exitStationName = tx.ExitStationName,
                        payAmount = tx.PayAmount,
                        pathSchemeCount = selectedPaths.Count,
                        lineAllocationCount = detailCount,
                        clearingBasis = clearingBasis
                    });
                }

                if (clearingResults.Any())
                {
                    _context.ClearingResults.AddRange(clearingResults);
                }

                if (unmatchedTransactions.Any())
                {
                    _context.ClearingUnmatchedTransactions.AddRange(unmatchedTransactions);
                }

                task.DataCount = timetableMatchedTradeCount;
                task.Status = "SUCCESS";
                task.EndTime = DateTime.Now;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    taskId = task.TaskId,
                    clearingMode = algorithmType,
                    transferCoefficient = transferCoefficient,
                    normalTradeCount = timetableMatchedTradeCount,
                    exceptionTradeCount = exceptionCount,
                    timetableMatchedTradeCount = timetableMatchedTradeCount,
                    unmatchedTradeCount = unmatchedTransactions.Count,
                    totalClearingAmount = Math.Round(clearingResults.Sum(x => x.ClearingAmount), 2),
                    summary = lineSummary
                        .OrderBy(x => x.Key)
                        .Select(x => new
                        {
                            lineName = x.Key,
                            clearingAmount = Math.Round(x.Value, 2)
                        })
                        .ToList(),
                    trades = tradeSummaries,
                    details = clearingResults
                        .Select(x => new
                        {
                            x.TransactionId,
                            x.LineId,
                            lineName = lineMap.ContainsKey(x.LineId) ? lineMap[x.LineId].LineName : "",
                            x.ClearingAmount,
                            x.PathText,
                            x.TransferText,
                            x.PathGroup
                        })
                        .OrderBy(x => x.TransactionId)
                        .ThenBy(x => x.PathGroup)
                        .ThenBy(x => x.LineId)
                        .ToList(),
                    unmatchedTransactions = unmatchedTransactions
                        .Select(x => new
                        {
                            x.TransactionId,
                            x.CardNo,
                            x.EntryTime,
                            x.EntryStationId,
                            x.ExitTime,
                            x.ExitStationId,
                            x.ReasonCode,
                            x.ReasonMessage
                        })
                        .OrderBy(x => x.TransactionId)
                        .ToList()
                });
            }
            catch (Exception ex)
            {
                task.Status = "FAILED";
                task.EndTime = DateTime.Now;
                task.ErrorMessage = ex.ToString();

                await _context.SaveChangesAsync();

                return StatusCode(500, new
                {
                    message = "清分执行失败。",
                    error = ex.ToString()
                });
            }
        }

        [HttpGet("latest")]
        public async Task<IActionResult> GetLatestResult()
        {
            var latestTask = await _context.ClearingTasks
                .AsNoTracking()
                .Where(x => x.Status == "SUCCESS")
                .OrderByDescending(x => x.TaskId)
                .FirstOrDefaultAsync();

            if (latestTask == null)
            {
                return NotFound(new { message = "暂无历史清分结果。" });
            }

            var shareRule = await _context.ClearingRules
                .AsNoTracking()
                .Where(x => x.RuleType == "SHARE" && x.IsActive)
                .OrderByDescending(x => x.RuleId)
                .FirstOrDefaultAsync();

            return await BuildClearingResponse(
                latestTask.TaskId,
                latestTask.AlgorithmType,
                shareRule?.TransferCoefficient ?? 1.1m
            );
        }

        private static Dictionary<long, List<GraphEdge>> BuildGraph(List<SectionInfo> sections)
        {
            var graph = new Dictionary<long, List<GraphEdge>>();

            foreach (var sec in sections)
            {
                if (!graph.ContainsKey(sec.FromStationId))
                {
                    graph[sec.FromStationId] = new List<GraphEdge>();
                }

                graph[sec.FromStationId].Add(new GraphEdge
                {
                    ToStationId = sec.ToStationId,
                    DistanceKm = sec.DistanceKm,
                    LineId = sec.LineId
                });

                if (sec.IsBidirectional)
                {
                    if (!graph.ContainsKey(sec.ToStationId))
                    {
                        graph[sec.ToStationId] = new List<GraphEdge>();
                    }

                    graph[sec.ToStationId].Add(new GraphEdge
                    {
                        ToStationId = sec.FromStationId,
                        DistanceKm = sec.DistanceKm,
                        LineId = sec.LineId
                    });
                }
            }

            return graph;
        }

        private static PathResult? FindShortestPath(
            Dictionary<long, List<GraphEdge>> graph,
            long startStationId,
            long endStationId)
        {
            var allStations = graph.Keys
                .Union(graph.Values.SelectMany(x => x.Select(e => e.ToStationId)))
                .Distinct()
                .ToList();

            if (!allStations.Contains(startStationId) || !allStations.Contains(endStationId))
            {
                return null;
            }

            var distances = new Dictionary<long, decimal>();
            var previous = new Dictionary<long, (long PrevStationId, long LineId, decimal DistanceKm)>();
            var visited = new HashSet<long>();

            foreach (var stationId in allStations)
            {
                distances[stationId] = decimal.MaxValue;
            }

            distances[startStationId] = 0m;

            while (visited.Count < allStations.Count)
            {
                var currentCandidates = distances
                    .Where(x => !visited.Contains(x.Key))
                    .OrderBy(x => x.Value)
                    .ToList();

                if (currentCandidates.Count == 0)
                {
                    break;
                }

                var current = currentCandidates.First();

                if (current.Value == decimal.MaxValue)
                {
                    break;
                }

                var currentStationId = current.Key;
                visited.Add(currentStationId);

                if (currentStationId == endStationId)
                {
                    break;
                }

                if (!graph.ContainsKey(currentStationId))
                {
                    continue;
                }

                foreach (var edge in graph[currentStationId])
                {
                    if (visited.Contains(edge.ToStationId))
                    {
                        continue;
                    }

                    var newDistance = distances[currentStationId] + edge.DistanceKm;

                    if (!distances.ContainsKey(edge.ToStationId) || newDistance < distances[edge.ToStationId])
                    {
                        distances[edge.ToStationId] = newDistance;
                        previous[edge.ToStationId] = (
                            currentStationId,
                            edge.LineId,
                            edge.DistanceKm
                        );
                    }
                }
            }

            if (!distances.ContainsKey(endStationId) || distances[endStationId] == decimal.MaxValue)
            {
                return null;
            }

            var stationIds = new List<long>();
            var segments = new List<PathSegment>();
            var cursor = endStationId;

            stationIds.Add(cursor);

            while (cursor != startStationId)
            {
                if (!previous.ContainsKey(cursor))
                {
                    return null;
                }

                var prev = previous[cursor];

                segments.Add(new PathSegment
                {
                    FromStationId = prev.PrevStationId,
                    ToStationId = cursor,
                    LineId = prev.LineId,
                    DistanceKm = prev.DistanceKm
                });

                cursor = prev.PrevStationId;
                stationIds.Add(cursor);
            }

            stationIds.Reverse();
            segments.Reverse();

            return new PathResult
            {
                StationIds = stationIds,
                Segments = segments,
                TotalDistanceKm = segments.Sum(x => x.DistanceKm),
                TransferStationIds = GetTransferStationIds(segments)
            };
        }

        private static List<PathResult> FindCandidatePaths(
            Dictionary<long, List<GraphEdge>> graph,
            long startStationId,
            long endStationId,
            int maxExtraDepth = 3,
            int maxPaths = 12)
        {
            var shortest = FindShortestPath(graph, startStationId, endStationId);

            if (shortest == null)
            {
                return new List<PathResult>();
            }

            var maxDistance = shortest.TotalDistanceKm * 1.6m;
            var maxSegmentCount = shortest.Segments.Count + maxExtraDepth;
            var results = new List<PathResult>();
            var signatures = new HashSet<string>();

            void Dfs(
                long currentStationId,
                List<long> stationIds,
                List<PathSegment> segments,
                HashSet<long> visited,
                decimal currentDistance)
            {
                if (results.Count >= maxPaths)
                {
                    return;
                }

                if (currentDistance > maxDistance)
                {
                    return;
                }

                if (segments.Count > maxSegmentCount)
                {
                    return;
                }

                if (currentStationId == endStationId)
                {
                    var signature = string.Join("->", stationIds);

                    if (signatures.Contains(signature))
                    {
                        return;
                    }

                    signatures.Add(signature);

                    results.Add(new PathResult
                    {
                        StationIds = stationIds.ToList(),
                        Segments = segments.ToList(),
                        TotalDistanceKm = currentDistance,
                        TransferStationIds = GetTransferStationIds(segments)
                    });

                    return;
                }

                if (!graph.ContainsKey(currentStationId))
                {
                    return;
                }

                var edges = graph[currentStationId]
                    .OrderBy(x => x.DistanceKm)
                    .ToList();

                foreach (var edge in edges)
                {
                    if (visited.Contains(edge.ToStationId))
                    {
                        continue;
                    }

                    visited.Add(edge.ToStationId);
                    stationIds.Add(edge.ToStationId);

                    segments.Add(new PathSegment
                    {
                        FromStationId = currentStationId,
                        ToStationId = edge.ToStationId,
                        LineId = edge.LineId,
                        DistanceKm = edge.DistanceKm
                    });

                    Dfs(
                        edge.ToStationId,
                        stationIds,
                        segments,
                        visited,
                        currentDistance + edge.DistanceKm
                    );

                    segments.RemoveAt(segments.Count - 1);
                    stationIds.RemoveAt(stationIds.Count - 1);
                    visited.Remove(edge.ToStationId);
                }
            }

            Dfs(
                startStationId,
                new List<long> { startStationId },
                new List<PathSegment>(),
                new HashSet<long> { startStationId },
                0m
            );

            return results
                .OrderBy(x => x.TotalDistanceKm)
                .ThenBy(x => x.TransferStationIds.Count)
                .Take(maxPaths)
                .ToList();
        }

        private static List<PathResult> FilterPathsByPublishedTimetable(
            List<PathResult> candidatePaths,
            DateTime? entryTime,
            DateTime? exitTime,
            List<TrainTimetable> activeTimetables,
            List<TrainTimetableDetail> timetableDetails)
        {
            if (candidatePaths.Count == 0)
            {
                return new List<PathResult>();
            }

            if (entryTime == null)
            {
                return new List<PathResult>();
            }

            if (activeTimetables.Count == 0 || timetableDetails.Count == 0)
            {
                return new List<PathResult>();
            }

            var result = new List<PathResult>();

            foreach (var path in candidatePaths)
            {
                var matchResult = TryMatchPathByPublishedTimetable(
                    path,
                    entryTime.Value,
                    exitTime,
                    activeTimetables,
                    timetableDetails
                );

                if (matchResult.IsMatched)
                {
                    path.TimetableMatchText = matchResult.MatchText;
                    result.Add(path);
                }
            }

            return result;
        }

        private static TimetablePathMatchResult TryMatchPathByPublishedTimetable(
            PathResult path,
            DateTime entryTime,
            DateTime? exitTime,
            List<TrainTimetable> activeTimetables,
            List<TrainTimetableDetail> timetableDetails)
        {
            var lineGroups = SplitPathByLine(path);

            if (lineGroups.Count == 0)
            {
                return new TimetablePathMatchResult
                {
                    IsMatched = false,
                    ReasonCode = "NO_LINE_SEGMENT",
                    MatchText = "路径无有效线路区段"
                };
            }

            var currentTime = entryTime.TimeOfDay;
            var matchTexts = new List<string>();

            for (var i = 0; i < lineGroups.Count; i++)
            {
                var group = lineGroups[i];

                var validTimetables = activeTimetables
                    .Where(t =>
                        t.LineId == group.LineId &&
                        t.Status == 1 &&
                        t.IsActive == 1 &&
                        entryTime.Date >= t.EffectiveStartDate.Date &&
                        entryTime.Date <= t.EffectiveEndDate.Date)
                    .ToList();

                if (validTimetables.Count == 0)
                {
                    return new TimetablePathMatchResult
                    {
                        IsMatched = false,
                        ReasonCode = "NO_ACTIVE_TIMETABLE_FOR_LINE",
                        MatchText = $"线路{group.LineId}无已发布生效时刻表"
                    };
                }

                var timetableIds = validTimetables
                    .Select(t => t.TimetableId)
                    .ToHashSet();

                var details = timetableDetails
                    .Where(d => timetableIds.Contains(d.TimetableId))
                    .ToList();

                var waitMinutes = i == 0 ? FirstRideWaitMinutes : TransferWaitMinutes;

                var matchedTrain = FindMatchedTrain(
                    details,
                    group.FromStationId,
                    group.ToStationId,
                    currentTime,
                    waitMinutes
                );

                if (matchedTrain == null)
                {
                    return new TimetablePathMatchResult
                    {
                        IsMatched = false,
                        ReasonCode = "NO_MATCHED_TRAIN",
                        MatchText = $"线路{group.LineId}未匹配到可乘坐列车"
                    };
                }

                matchTexts.Add(
                    $"线路{group.LineId}-{matchedTrain.TrainNo}"
                );

                currentTime = matchedTrain.ArrivalTime
                              ?? matchedTrain.DepartureTime
                              ?? currentTime;
            }

            if (exitTime != null)
            {
                var latestAllowedExitTime = exitTime.Value.TimeOfDay.Add(
                    TimeSpan.FromMinutes(ExitToleranceMinutes)
                );

                if (currentTime > latestAllowedExitTime)
                {
                    return new TimetablePathMatchResult
                    {
                        IsMatched = false,
                        ReasonCode = "EXIT_TIME_TOLERANCE_EXCEEDED",
                        MatchText = "列车到达时间晚于出站时间容差"
                    };
                }
            }

            return new TimetablePathMatchResult
            {
                IsMatched = true,
                ReasonCode = "MATCHED",
                MatchText = string.Join("；", matchTexts)
            };
        }

        private static List<LinePathGroup> SplitPathByLine(PathResult path)
        {
            var groups = new List<LinePathGroup>();

            if (path.Segments.Count == 0)
            {
                return groups;
            }

            var currentLineId = path.Segments[0].LineId;
            var startStationId = path.Segments[0].FromStationId;
            var endStationId = path.Segments[0].ToStationId;

            for (var i = 1; i < path.Segments.Count; i++)
            {
                var segment = path.Segments[i];

                if (segment.LineId == currentLineId)
                {
                    endStationId = segment.ToStationId;
                }
                else
                {
                    groups.Add(new LinePathGroup
                    {
                        LineId = currentLineId,
                        FromStationId = startStationId,
                        ToStationId = endStationId
                    });

                    currentLineId = segment.LineId;
                    startStationId = segment.FromStationId;
                    endStationId = segment.ToStationId;
                }
            }

            groups.Add(new LinePathGroup
            {
                LineId = currentLineId,
                FromStationId = startStationId,
                ToStationId = endStationId
            });

            return groups;
        }

        private static MatchedTrainInfo? FindMatchedTrain(
            List<TrainTimetableDetail> details,
            long fromStationId,
            long toStationId,
            TimeSpan earliestDepartureTime,
            int maxWaitMinutes)
        {
            var groupedByTrain = details
                .GroupBy(d => new { d.TimetableId, d.TrainNo })
                .OrderBy(g => g.Key.TrainNo)
                .ToList();

            foreach (var trainGroup in groupedByTrain)
            {
                var from = trainGroup.FirstOrDefault(d => d.StationId == fromStationId);
                var to = trainGroup.FirstOrDefault(d => d.StationId == toStationId);

                if (from == null || to == null)
                {
                    continue;
                }

                if (from.StationSeq >= to.StationSeq)
                {
                    continue;
                }

                var departureTime = from.DepartureTime ?? from.ArrivalTime;
                var arrivalTime = to.ArrivalTime ?? to.DepartureTime;

                if (departureTime == null || arrivalTime == null)
                {
                    continue;
                }

                if (departureTime.Value < earliestDepartureTime)
                {
                    continue;
                }

                if (departureTime.Value > earliestDepartureTime.Add(TimeSpan.FromMinutes(maxWaitMinutes)))
                {
                    continue;
                }

                return new MatchedTrainInfo
                {
                    TimetableId = trainGroup.Key.TimetableId,
                    TrainNo = trainGroup.Key.TrainNo,
                    DepartureTime = departureTime,
                    ArrivalTime = arrivalTime
                };
            }

            return null;
        }

        private static decimal CalculatePathWeight(PathResult path)
        {
            if (path.TotalDistanceKm <= 0)
            {
                return 0m;
            }

            var transferCount = path.TransferStationIds.Count;
            var distanceWeight = 1m / path.TotalDistanceKm;
            var transferPenalty = 1m / (1m + transferCount * 0.3m);

            return distanceWeight * transferPenalty;
        }

        private static List<LineAllocation> AllocateByPath(
            PathResult path,
            Dictionary<long, LineInfo> lineMap,
            string algorithmType,
            decimal transferCoefficient)
        {
            var lineDistanceMap = new Dictionary<long, decimal>();

            foreach (var seg in path.Segments)
            {
                if (!lineDistanceMap.ContainsKey(seg.LineId))
                {
                    lineDistanceMap[seg.LineId] = 0m;
                }

                lineDistanceMap[seg.LineId] += seg.DistanceKm;
            }

            if (algorithmType == "shortest" && path.TransferStationIds.Count > 0 && transferCoefficient > 0)
            {
                for (var i = 1; i < path.Segments.Count; i++)
                {
                    if (path.Segments[i - 1].LineId != path.Segments[i].LineId)
                    {
                        var transferInLineId = path.Segments[i].LineId;

                        if (lineDistanceMap.ContainsKey(transferInLineId))
                        {
                            lineDistanceMap[transferInLineId] *= transferCoefficient;
                        }
                    }
                }
            }

            var totalWeight = lineDistanceMap.Values.Sum();

            if (totalWeight <= 0)
            {
                return new List<LineAllocation>();
            }

            var totalAmount = path.TotalFareAmount;
            var allocations = new List<LineAllocation>();
            decimal allocated = 0m;

            var lineIds = lineDistanceMap.Keys.OrderBy(x => x).ToList();

            for (var i = 0; i < lineIds.Count; i++)
            {
                var lineId = lineIds[i];
                decimal amount;

                if (i == lineIds.Count - 1)
                {
                    amount = Math.Round(totalAmount - allocated, 2);
                }
                else
                {
                    amount = Math.Round(totalAmount * lineDistanceMap[lineId] / totalWeight, 2);
                    allocated += amount;
                }

                allocations.Add(new LineAllocation
                {
                    LineId = lineId,
                    OperatorId = lineMap.ContainsKey(lineId) ? lineMap[lineId].OperatorId : 0,
                    Amount = amount
                });
            }

            return allocations;
        }

        private static List<long> GetTransferStationIds(List<PathSegment> segments)
        {
            var transferStationIds = new List<long>();

            for (var i = 1; i < segments.Count; i++)
            {
                if (segments[i - 1].LineId != segments[i].LineId)
                {
                    transferStationIds.Add(segments[i].FromStationId);
                }
            }

            return transferStationIds;
        }

        private static string BuildPathText(
            List<long> stationIds,
            Dictionary<long, StationInfo> stationMap)
        {
            return string.Join(" -> ", stationIds
                .Where(stationMap.ContainsKey)
                .Select(id => stationMap[id].StationName));
        }

        private static string BuildTransferText(
            List<long> transferStationIds,
            Dictionary<long, StationInfo> stationMap)
        {
            if (transferStationIds.Count == 0)
            {
                return "直达";
            }

            return string.Join("、", transferStationIds
                .Where(stationMap.ContainsKey)
                .Select(id => stationMap[id].StationName)) + "换乘";
        }

        private static ClearingUnmatchedTransaction CreateUnmatchedRecord(
    int taskId,
    long transactionId,
    string cardNo,
    DateTime? entryTime,
    long? entryStationId,
    DateTime? exitTime,
    long? exitStationId,
    string reasonCode,
    string reasonMessage)
        {
            return new ClearingUnmatchedTransaction
            {
                TaskId = taskId,
                TransactionId = transactionId,
                CardNo = cardNo,
                EntryTime = entryTime,
                EntryStationId = entryStationId,
                ExitTime = exitTime,
                ExitStationId = exitStationId,
                ReasonCode = reasonCode,
                ReasonMessage = reasonMessage,
                CreatedAt = DateTime.Now
            };
        }

        private static TimetableFilterResult FilterPathsByPublishedTimetableWithReason(
            List<PathResult> candidatePaths,
            DateTime? entryTime,
            DateTime? exitTime,
            List<TrainTimetable> activeTimetables,
            List<TrainTimetableDetail> timetableDetails)
        {
            var result = new TimetableFilterResult();

            if (candidatePaths.Count == 0)
            {
                result.ReasonCode = "NO_CANDIDATE_PATH";
                result.ReasonMessage = "未找到候选路径";
                return result;
            }

            if (entryTime == null)
            {
                result.ReasonCode = "NO_ENTRY_TIME";
                result.ReasonMessage = "交易缺少进站时间";
                return result;
            }

            if (activeTimetables.Count == 0)
            {
                result.ReasonCode = "NO_ACTIVE_TIMETABLE";
                result.ReasonMessage = "当前没有已发布且启用的时刻表";
                return result;
            }

            if (timetableDetails.Count == 0)
            {
                result.ReasonCode = "NO_TIMETABLE_DETAIL";
                result.ReasonMessage = "已发布时刻表没有明细数据";
                return result;
            }

            var lastReasonCode = "TIMETABLE_NOT_MATCHED";
            var lastReasonMessage = "候选路径未匹配到已发布时刻表";

            foreach (var path in candidatePaths)
            {
                var matchResult = TryMatchPathByPublishedTimetable(
                    path,
                    entryTime.Value,
                    exitTime,
                    activeTimetables,
                    timetableDetails
                );

                if (matchResult.IsMatched)
                {
                    path.TimetableMatchText = matchResult.MatchText;
                    result.Paths.Add(path);
                }
                else
                {
                    lastReasonCode = matchResult.ReasonCode;
                    lastReasonMessage = matchResult.MatchText;
                }
            }

            if (result.Paths.Count == 0)
            {
                result.ReasonCode = lastReasonCode;
                result.ReasonMessage = lastReasonMessage;
            }

            return result;
        }

        private async Task<IActionResult> BuildClearingResponse(
            int taskId,
            string algorithmType,
            decimal transferCoefficient)
        {
            var task = await _context.ClearingTasks
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TaskId == taskId);

            if (task == null)
            {
                return NotFound(new { message = "清分任务不存在。" });
            }

            var details = await (
                from r in _context.ClearingResults.AsNoTracking()
                join l in _context.LineInfos.AsNoTracking()
                    on r.LineId equals l.LineId
                where r.TaskId == taskId
                orderby r.TransactionId, r.PathGroup, r.LineId
                select new
                {
                    r.TransactionId,
                    r.LineId,
                    lineName = l.LineName,
                    r.ClearingAmount,
                    r.PathText,
                    r.TransferText,
                    r.PathGroup
                }
            ).ToListAsync();

            var transactionIds = details
                .Select(x => x.TransactionId)
                .Distinct()
                .ToList();

            var trades = await (
                from t in _context.TicketTransactions.AsNoTracking()
                join entryStation in _context.StationInfos.AsNoTracking()
                    on t.EntryStationId equals entryStation.StationId into entryJoin
                from entryStation in entryJoin.DefaultIfEmpty()
                join exitStation in _context.StationInfos.AsNoTracking()
                    on t.ExitStationId equals exitStation.StationId into exitJoin
                from exitStation in exitJoin.DefaultIfEmpty()
                where transactionIds.Contains(t.TransactionId)
                orderby t.TransactionId
                select new
                {
                    t.TransactionId,
                    t.CardNo,
                    EntryStationName = entryStation != null ? entryStation.StationName : "无进站记录",
                    ExitStationName = exitStation != null ? exitStation.StationName : "无出站记录",
                    t.PayAmount
                }
            ).ToListAsync();

            var detailGroups = details
                .GroupBy(x => x.TransactionId)
                .ToDictionary(
                    x => x.Key,
                    x => new
                    {
                        PathSchemeCount = x.Select(d => d.PathGroup).Distinct().Count(),
                        LineAllocationCount = x.Count(),
                        ClearingBasis = "已发布时刻表匹配"
                    }
                );

            var tradeSummaries = trades.Select(t => new
            {
                transactionId = t.TransactionId,
                cardNo = t.CardNo,
                entryStationName = t.EntryStationName,
                exitStationName = t.ExitStationName,
                payAmount = t.PayAmount,
                pathSchemeCount = detailGroups.ContainsKey(t.TransactionId)
                    ? detailGroups[t.TransactionId].PathSchemeCount
                    : 0,
                lineAllocationCount = detailGroups.ContainsKey(t.TransactionId)
                    ? detailGroups[t.TransactionId].LineAllocationCount
                    : 0,
                clearingBasis = detailGroups.ContainsKey(t.TransactionId)
                    ? detailGroups[t.TransactionId].ClearingBasis
                    : "未知"
            }).ToList();

            var exceptionCount = await _context.TicketTransactions
                .AsNoTracking()
                .CountAsync(x => x.TransactionStatus == "EXCEPTION");

            var unmatchedTransactions = await _context.ClearingUnmatchedTransactions
                .AsNoTracking()
                .Where(x => x.TaskId == taskId)
                .OrderBy(x => x.TransactionId)
                .Select(x => new
                {
                    x.TransactionId,
                    x.CardNo,
                    x.EntryTime,
                    x.EntryStationId,
                    x.ExitTime,
                    x.ExitStationId,
                    x.ReasonCode,
                    x.ReasonMessage
                })
                .ToListAsync();

            var summary = details
                .GroupBy(x => x.lineName)
                .OrderBy(x => x.Key)
                .Select(x => new
                {
                    lineName = x.Key,
                    clearingAmount = Math.Round(x.Sum(d => d.ClearingAmount), 2)
                })
                .ToList();

            var timetableMatchedTradeCount = tradeSummaries.Count;

            return Ok(new
            {
                taskId = task.TaskId,
                clearingMode = algorithmType,
                transferCoefficient = transferCoefficient,
                normalTradeCount = timetableMatchedTradeCount,
                exceptionTradeCount = exceptionCount,
                timetableMatchedTradeCount = timetableMatchedTradeCount,
                unmatchedTradeCount = unmatchedTransactions.Count,
                totalClearingAmount = Math.Round(details.Sum(x => x.ClearingAmount), 2),
                summary = summary,
                trades = tradeSummaries,
                details = details,
                unmatchedTransactions = unmatchedTransactions
            });
        }

        public class SaveClearingRulesRequest
        {
            public string ClearingMode { get; set; } = "shortest";
            public decimal TransferCoefficient { get; set; } = 1.1m;
        }

        private class GraphEdge
        {
            public long ToStationId { get; set; }
            public decimal DistanceKm { get; set; }
            public long LineId { get; set; }
        }

        private class PathSegment
        {
            public long FromStationId { get; set; }
            public long ToStationId { get; set; }
            public long LineId { get; set; }
            public decimal DistanceKm { get; set; }
        }

        private class PathResult
        {
            public List<long> StationIds { get; set; } = new();
            public List<PathSegment> Segments { get; set; } = new();
            public decimal TotalDistanceKm { get; set; }
            public List<long> TransferStationIds { get; set; } = new();
            public decimal TotalFareAmount { get; set; }
            public string? TimetableMatchText { get; set; }
        }

        private class LineAllocation
        {
            public long LineId { get; set; }
            public long OperatorId { get; set; }
            public decimal Amount { get; set; }
        }

        private class LinePathGroup
        {
            public long LineId { get; set; }
            public long FromStationId { get; set; }
            public long ToStationId { get; set; }
        }

        private class MatchedTrainInfo
        {
            public long TimetableId { get; set; }
            public string TrainNo { get; set; } = string.Empty;
            public TimeSpan? DepartureTime { get; set; }
            public TimeSpan? ArrivalTime { get; set; }
        }

        private class TimetablePathMatchResult
        {
            public bool IsMatched { get; set; }
            public string ReasonCode { get; set; } = string.Empty;
            public string MatchText { get; set; } = string.Empty;
        }
        private class TimetableFilterResult
        {
            public List<PathResult> Paths { get; set; } = new();
            public string ReasonCode { get; set; } = "UNKNOWN";
            public string ReasonMessage { get; set; } = "未知原因";
        }
    }
}