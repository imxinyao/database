using System.Globalization;
using database.Data;
using database.Dtos;
using database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace database.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TimetablesController : ControllerBase
    {
        private readonly MetroDbContext _context;

        public TimetablesController(MetroDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 时刻表列表
        /// GET /api/timetables
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetTimetables()
        {
            var list = await (
                from t in _context.TrainTimetables.AsNoTracking()
                join l in _context.LineInfos.AsNoTracking()
                    on t.LineId equals l.LineId
                orderby t.LineId, t.Direction, t.VersionNo
                select new
                {
                    timetableId = t.TimetableId,
                    timetableCode = t.TimetableCode,
                    timetableName = t.TimetableName,
                    lineId = t.LineId,
                    lineName = l.LineName,
                    direction = t.Direction,
                    directionText = t.Direction == 1 ? "上行" : t.Direction == 2 ? "下行" : "未知",
                    versionNo = t.VersionNo,
                    effectiveStartDate = t.EffectiveStartDate,
                    effectiveEndDate = t.EffectiveEndDate,
                    runCalendarType = t.RunCalendarType,
                    status = t.Status,
                    statusText = t.Status == 0 ? "草稿" : t.Status == 1 ? "已发布" : t.Status == 2 ? "停用" : "未知",
                    isActive = t.IsActive,
                    activeText = t.IsActive == 1 ? "当前生效" : "非当前版本",
                    remark = t.Remark,
                    createdAt = t.CreatedAt,
                    updatedAt = t.UpdatedAt
                })
                .ToListAsync();

            return Ok(list);
        }

        /// <summary>
        /// 时刻表详情
        /// GET /api/timetables/{id}/details
        /// </summary>
        [HttpGet("{id:long}/details")]
        public async Task<ActionResult<IEnumerable<object>>> GetTimetableDetails(long id)
        {
            var exists = await _context.TrainTimetables
                .AsNoTracking()
                .AnyAsync(t => t.TimetableId == id);

            if (!exists)
            {
                return NotFound(new
                {
                    success = false,
                    message = $"未找到 timetable_id = {id} 的时刻表。"
                });
            }

            var details = await (
                from d in _context.TrainTimetableDetails.AsNoTracking()
                join s in _context.StationInfos.AsNoTracking()
                    on d.StationId equals s.StationId
                where d.TimetableId == id
                orderby d.TrainNo, d.StationSeq
                select new
                {
                    detailId = d.DetailId,
                    timetableId = d.TimetableId,
                    trainNo = d.TrainNo,
                    stationId = d.StationId,
                    stationSeq = d.StationSeq,
                    stationCode = s.StationCode,
                    stationName = s.StationName,
                    arrivalTime = d.ArrivalTime,
                    departureTime = d.DepartureTime,
                    stopMinutes = d.StopMinutes,
                    isOriginStation = d.IsOriginStation,
                    isTerminalStation = d.IsTerminalStation,
                    originText = d.IsOriginStation == 1 ? "是" : "否",
                    terminalText = d.IsTerminalStation == 1 ? "是" : "否",
                    createdAt = d.CreatedAt
                })
                .ToListAsync();

            return Ok(details);
        }

        /// <summary>
        /// 导入时刻表
        /// POST /api/timetables/import
        /// FormData:
        /// timetableCode, timetableName, lineId, direction, versionNo,
        /// effectiveStartDate, effectiveEndDate, runCalendarType, remark, file
        /// </summary>
        [HttpPost("import")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> ImportTimetable([FromForm] TimetableImportRequest request)
        {
            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new { success = false, message = "请上传CSV文件" });
            }

            if (string.IsNullOrWhiteSpace(request.TimetableCode) ||
                string.IsNullOrWhiteSpace(request.TimetableName) ||
                string.IsNullOrWhiteSpace(request.VersionNo))
            {
                return BadRequest(new { success = false, message = "时刻表编号、名称、版本号不能为空" });
            }

            var stationMap = await _context.StationInfos
                .AsNoTracking()
                .ToDictionaryAsync(x => x.StationCode, x => x.StationId);

            var rows = new List<TrainTimetableDetail>();

            using var stream = request.File.OpenReadStream();
            using var reader = new StreamReader(stream);

            var header = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(header))
            {
                return BadRequest(new { success = false, message = "CSV文件内容为空" });
            }

            var expectedHeader = "train_no,station_code,station_seq,arrival_time,departure_time,stop_minutes,is_origin_station,is_terminal_station";
            if (!string.Equals(header.Trim(), expectedHeader, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    success = false,
                    message = $"CSV表头不正确，必须为：{expectedHeader}"
                });
            }

            var lineNo = 1;
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNo++;

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length < 8)
                {
                    return BadRequest(new { success = false, message = $"第 {lineNo} 行字段数不足" });
                }

                var trainNo = parts[0].Trim();
                var stationCode = parts[1].Trim();
                var stationSeqText = parts[2].Trim();
                var arrivalTimeText = parts[3].Trim();
                var departureTimeText = parts[4].Trim();
                var stopMinutesText = parts[5].Trim();
                var isOriginText = parts[6].Trim();
                var isTerminalText = parts[7].Trim();

                if (string.IsNullOrWhiteSpace(trainNo))
                {
                    return BadRequest(new { success = false, message = $"第 {lineNo} 行 train_no 不能为空" });
                }

                if (!stationMap.ContainsKey(stationCode))
                {
                    return BadRequest(new { success = false, message = $"第 {lineNo} 行站点编码不存在：{stationCode}" });
                }

                if (!int.TryParse(stationSeqText, out var stationSeq))
                {
                    return BadRequest(new { success = false, message = $"第 {lineNo} 行 station_seq 格式错误" });
                }

                int stopMinutes = 0;
                if (!string.IsNullOrWhiteSpace(stopMinutesText) && !int.TryParse(stopMinutesText, out stopMinutes))
                {
                    return BadRequest(new { success = false, message = $"第 {lineNo} 行 stop_minutes 格式错误" });
                }

                TimeSpan? arrivalTime = null;
                TimeSpan? departureTime = null;

                if (!string.IsNullOrWhiteSpace(arrivalTimeText))
                {
                    if (!TimeSpan.TryParse(arrivalTimeText, CultureInfo.InvariantCulture, out var arr))
                    {
                        return BadRequest(new { success = false, message = $"第 {lineNo} 行 arrival_time 格式错误" });
                    }

                    arrivalTime = arr;
                }

                if (!string.IsNullOrWhiteSpace(departureTimeText))
                {
                    if (!TimeSpan.TryParse(departureTimeText, CultureInfo.InvariantCulture, out var dep))
                    {
                        return BadRequest(new { success = false, message = $"第 {lineNo} 行 departure_time 格式错误" });
                    }

                    departureTime = dep;
                }

                int isOriginStation = (isOriginText == "1" || isOriginText.Equals("true", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
                int isTerminalStation = (isTerminalText == "1" || isTerminalText.Equals("true", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

                rows.Add(new TrainTimetableDetail
                {
                    TrainNo = trainNo,
                    StationId = stationMap[stationCode],
                    StationSeq = stationSeq,
                    ArrivalTime = arrivalTime,
                    DepartureTime = departureTime,
                    StopMinutes = stopMinutes,
                    IsOriginStation = isOriginStation,
                    IsTerminalStation = isTerminalStation,
                    CreatedAt = DateTime.Now
                });
            }

            if (!rows.Any())
            {
                return BadRequest(new { success = false, message = "未解析到有效明细数据" });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var existing = await _context.TrainTimetables
                    .FirstOrDefaultAsync(x =>
                        x.LineId == request.LineId &&
                        x.Direction == request.Direction &&
                        x.VersionNo == request.VersionNo);

                if (existing != null)
                {
                    var oldDetails = await _context.TrainTimetableDetails
                        .Where(x => x.TimetableId == existing.TimetableId)
                        .ToListAsync();

                    _context.TrainTimetableDetails.RemoveRange(oldDetails);
                    _context.TrainTimetables.Remove(existing);
                    await _context.SaveChangesAsync();
                }

                var timetable = new TrainTimetable
                {
                    TimetableCode = request.TimetableCode,
                    TimetableName = request.TimetableName,
                    LineId = request.LineId,
                    Direction = request.Direction,
                    VersionNo = request.VersionNo,
                    EffectiveStartDate = request.EffectiveStartDate,
                    EffectiveEndDate = request.EffectiveEndDate,
                    RunCalendarType = request.RunCalendarType,
                    Status = 0,
                    IsActive = 0,
                    Remark = request.Remark,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                _context.TrainTimetables.Add(timetable);
                await _context.SaveChangesAsync();

                foreach (var row in rows)
                {
                    row.TimetableId = timetable.TimetableId;
                }

                _context.TrainTimetableDetails.AddRange(rows);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "时刻表导入成功",
                    timetableId = timetable.TimetableId,
                    detailCount = rows.Count
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = "导入失败",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// 发布时刻表
        /// POST /api/timetables/{id}/publish
        /// 规则：
        /// 1. 当前记录 status = 1, is_active = 1
        /// 2. 同 line_id + direction 下其他版本 is_active = 0
        /// </summary>
        [HttpPost("{id:long}/publish")]
        public async Task<ActionResult<object>> PublishTimetable(long id)
        {
            var target = await _context.TrainTimetables
                .FirstOrDefaultAsync(t => t.TimetableId == id);

            if (target == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = $"未找到 timetable_id = {id} 的时刻表。"
                });
            }

            using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var sameGroup = await _context.TrainTimetables
                    .Where(t => t.LineId == target.LineId && t.Direction == target.Direction)
                    .ToListAsync();

                foreach (var item in sameGroup)
                {
                    item.IsActive = 0;
                    item.UpdatedAt = DateTime.Now;
                }

                target.Status = 1;
                target.IsActive = 1;
                target.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "时刻表发布成功。",
                    timetableId = target.TimetableId,
                    lineId = target.LineId,
                    direction = target.Direction,
                    versionNo = target.VersionNo
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new
                {
                    success = false,
                    message = $"发布失败：{ex.Message}"
                });
            }
        }

        /// <summary>
        /// 停用时刻表
        /// POST /api/timetables/{id}/disable
        /// 规则：
        /// 1. 当前记录 status = 2
        /// 2. 当前记录 is_active = 0
        /// </summary>
        [HttpPost("{id:long}/disable")]
        public async Task<ActionResult<object>> DisableTimetable(long id)
        {
            var target = await _context.TrainTimetables
                .FirstOrDefaultAsync(t => t.TimetableId == id);

            if (target == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = $"未找到 timetable_id = {id} 的时刻表。"
                });
            }

            try
            {
                target.Status = 2;
                target.IsActive = 0;
                target.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "时刻表停用成功。",
                    timetableId = target.TimetableId,
                    lineId = target.LineId,
                    direction = target.Direction,
                    versionNo = target.VersionNo
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"停用失败：{ex.Message}"
                });
            }
        }
        [HttpPost("{id:long}/delete")]
        public async Task<ActionResult<object>> DeleteTimetable(long id)
        {
            var target = await _context.TrainTimetables
                .FirstOrDefaultAsync(t => t.TimetableId == id);

            if (target == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = $"未找到 timetable_id = {id} 的时刻表。"
                });
            }

            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var details = await _context.TrainTimetableDetails
                    .Where(d => d.TimetableId == id)
                    .ToListAsync();

                if (details.Count > 0)
                {
                    _context.TrainTimetableDetails.RemoveRange(details);
                    await _context.SaveChangesAsync();
                }

                _context.TrainTimetables.Remove(target);
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "时刻表删除成功。",
                    timetableId = id
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = $"删除失败：{ex.Message}"
                });
            }
        }
    }
}