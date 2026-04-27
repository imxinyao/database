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
                return BadRequest(new
                {
                    message = "清分模式只能是 shortest 或 multi。"
                });
            }

            if (request.TransferCoefficient <= 0)
            {
                return BadRequest(new
                {
                    message = "换乘贡献系数必须大于 0。"
                });
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

            var latestSuccessTask = await _context.ClearingTasks
                .AsNoTracking()
                .Where(x => x.Status == "SUCCESS" && x.AlgorithmType == algorithmType)
                .OrderByDescending(x => x.TaskId)
                .FirstOrDefaultAsync();

            var latestTransactionCreatedAt = await _context.TicketTransactions
                .AsNoTracking()
                .MaxAsync(x => (DateTime?)x.CreatedAt);

            var latestRuleUpdatedAt = await _context.ClearingRules
                .AsNoTracking()
                .Where(x => x.IsActive)
                .MaxAsync(x => (DateTime?)x.UpdatedAt);

            if (latestSuccessTask != null)
            {
                var taskEndTime = latestSuccessTask.EndTime ?? latestSuccessTask.CreatedAt;

                var transactionNotChanged =
                    latestTransactionCreatedAt == null || latestTransactionCreatedAt <= taskEndTime;

                var ruleNotChanged =
                    latestRuleUpdatedAt == null || latestRuleUpdatedAt <= taskEndTime;

                if (transactionNotChanged && ruleNotChanged)
                {
                    return await BuildClearingResponse(
                        latestSuccessTask.TaskId,
                        algorithmType,
                        transferCoefficient
                    );
                }
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

                var lineMap = lines.ToDictionary(x => x.LineId, x => x);
                var stationMap = stations.ToDictionary(x => x.StationId, x => x);

                var graph = BuildGraph(sections);

                var clearingResults = new List<ClearingResult>();
                var lineSummary = new Dictionary<string, decimal>();
                var tradeSummaries = new List<object>();

                foreach (var line in lines)
                {
                    lineSummary[line.LineName] = 0m;
                }

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
                        continue;
                    }

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
                            pathAmount = Math.Round(tx.PayAmount * CalculatePathWeight(path) / totalPathWeight, 2);
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
                                PathGroup = $"方案{pathIndex}",
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
                        lineAllocationCount = detailCount
                    });
                }

                if (clearingResults.Any())
                {
                    _context.ClearingResults.AddRange(clearingResults);
                }

                task.DataCount = normalTransactions.Count;
                task.Status = "SUCCESS";
                task.EndTime = DateTime.Now;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    taskId = task.TaskId,
                    clearingMode = algorithmType,
                    transferCoefficient = transferCoefficient,
                    normalTradeCount = normalTransactions.Count,
                    exceptionTradeCount = exceptionCount,
                    totalClearingAmount = normalTransactions.Sum(x => x.PayAmount),
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
                return NotFound(new
                {
                    message = "暂无历史清分结果。"
                });
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
            if (!graph.ContainsKey(startStationId) || !graph.ContainsKey(endStationId))
            {
                return null;
            }

            var distances = new Dictionary<long, decimal>();
            var previous = new Dictionary<long, (long PrevStationId, long LineId, decimal DistanceKm)>();
            var visited = new HashSet<long>();

            var allStations = graph.Keys
                .Union(graph.Values.SelectMany(x => x.Select(e => e.ToStationId)))
                .Distinct()
                .ToList();

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
                        previous[edge.ToStationId] = (currentStationId, edge.LineId, edge.DistanceKm);
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
                for (int i = 1; i < path.Segments.Count; i++)
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

            for (int i = 0; i < lineIds.Count; i++)
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

            for (int i = 1; i < segments.Count; i++)
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
            return string.Join(" -> ",
                stationIds
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
        }

        private class LineAllocation
        {
            public long LineId { get; set; }

            public long OperatorId { get; set; }

            public decimal Amount { get; set; }
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
                return NotFound(new
                {
                    message = "清分任务不存在。"
                });
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
                        LineAllocationCount = x.Count()
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
                    : 0
            }).ToList();

            var exceptionCount = await _context.TicketTransactions
                .AsNoTracking()
                .CountAsync(x => x.TransactionStatus == "EXCEPTION");

            var summary = details
                .GroupBy(x => x.lineName)
                .OrderBy(x => x.Key)
                .Select(x => new
                {
                    lineName = x.Key,
                    clearingAmount = Math.Round(x.Sum(d => d.ClearingAmount), 2)
                })
                .ToList();

            return Ok(new
            {
                taskId = task.TaskId,
                clearingMode = algorithmType,
                transferCoefficient = transferCoefficient,
                normalTradeCount = task.DataCount,
                exceptionTradeCount = exceptionCount,
                totalClearingAmount = tradeSummaries.Sum(x => x.payAmount),
                summary = summary,
                trades = tradeSummaries,
                details = details
            });
        }
    }
}