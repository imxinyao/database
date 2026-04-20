using database.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace database.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LinesController : ControllerBase
    {
        private readonly MetroDbContext _context;

        public LinesController(MetroDbContext context)
        {
            _context = context;
        }

        [HttpGet("topology")]
        public async Task<IActionResult> GetLineTopology()
        {
            var lines = await _context.LineInfos
                .AsNoTracking()
                .OrderBy(x => x.LineId)
                .Select(x => new
                {
                    x.LineId,
                    x.LineName
                })
                .ToListAsync();

            var sections = await _context.SectionInfos
                .AsNoTracking()
                .OrderBy(x => x.LineId)
                .ThenBy(x => x.SectionId)
                .Select(x => new
                {
                    x.LineId,
                    x.FromStationId,
                    x.ToStationId
                })
                .ToListAsync();

            var stations = await _context.StationInfos
                .AsNoTracking()
                .Select(x => new
                {
                    x.StationId,
                    x.StationCode,
                    x.StationName,
                    x.IsTransfer
                })
                .ToListAsync();

            var stationMap = stations.ToDictionary(x => x.StationId, x => x);

            var result = new List<object>();

            foreach (var line in lines)
            {
                var lineSections = sections
                    .Where(x => x.LineId == line.LineId)
                    .ToList();

                if (!lineSections.Any())
                {
                    result.Add(new
                    {
                        lineId = line.LineId,
                        lineName = line.LineName,
                        stations = new List<object>()
                    });
                    continue;
                }

                var nextMap = new Dictionary<long, long>();
                var fromSet = new HashSet<long>();
                var toSet = new HashSet<long>();

                foreach (var sec in lineSections)
                {
                    nextMap[sec.FromStationId] = sec.ToStationId;
                    fromSet.Add(sec.FromStationId);
                    toSet.Add(sec.ToStationId);
                }

                var firstStationId = fromSet.Except(toSet).FirstOrDefault();
                if (firstStationId == 0)
                {
                    firstStationId = lineSections.First().FromStationId;
                }

                var orderedStationIds = new List<long>();
                var visited = new HashSet<long>();
                var current = firstStationId;

                while (!visited.Contains(current))
                {
                    visited.Add(current);
                    orderedStationIds.Add(current);

                    if (!nextMap.ContainsKey(current))
                    {
                        break;
                    }

                    current = nextMap[current];
                }

                var orderedStations = orderedStationIds
                    .Where(stationId => stationMap.ContainsKey(stationId))
                    .Select((stationId, index) =>
                    {
                        var s = stationMap[stationId];
                        return new
                        {
                            stationId = s.StationId,
                            stationCode = s.StationCode,
                            stationName = s.StationName,
                            stationSeq = index + 1,
                            isTransfer = s.IsTransfer
                        };
                    })
                    .ToList();

                result.Add(new
                {
                    lineId = line.LineId,
                    lineName = line.LineName,
                    stations = orderedStations
                });
            }

            return Ok(result);
        }
    }
}