using database.Data;
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
            var list = await _context.TrainTimetables
                .AsNoTracking()
                .OrderBy(t => t.LineId)
                .ThenBy(t => t.Direction)
                .ThenBy(t => t.VersionNo)
                .Select(t => new
                {
                    timetableId = t.TimetableId,
                    timetableCode = t.TimetableCode,
                    timetableName = t.TimetableName,
                    lineId = t.LineId,
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
    }
}