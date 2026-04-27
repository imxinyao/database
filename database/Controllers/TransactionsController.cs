using database.Data;
using database.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace database.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TransactionsController : ControllerBase
    {
        private readonly MetroDbContext _context;

        public TransactionsController(MetroDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TransactionListItemDto>>> GetTransactions()
        {
            var transactions = await (
                from t in _context.TicketTransactions.AsNoTracking()
                join entryStation in _context.StationInfos.AsNoTracking()
                    on t.EntryStationId equals entryStation.StationId into entryJoin
                from entryStation in entryJoin.DefaultIfEmpty()
                join exitStation in _context.StationInfos.AsNoTracking()
                    on t.ExitStationId equals exitStation.StationId into exitJoin
                from exitStation in exitJoin.DefaultIfEmpty()
                orderby t.TransactionId descending
                select new TransactionListItemDto
                {
                    TransactionId = t.TransactionId,
                    CardNo = t.CardNo,
                    EntryStationName = entryStation != null ? entryStation.StationName : "无进站记录",
                    ExitStationName = exitStation != null ? exitStation.StationName : "无出站记录",
                    PayAmount = t.PayAmount,
                    TransactionStatus = t.TransactionStatus,
                    PaymentType = t.PaymentType,
                    TransactionType = t.TransactionType,
                    EntryTime = t.EntryTime,
                    ExitTime = t.ExitTime
                }
            ).ToListAsync();

            return Ok(transactions);
        }
    }
}