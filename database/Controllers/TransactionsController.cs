using System;
using database.Data;
using database.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace database.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TransactionsController : ControllerBase
    {
        private readonly MetroDbContext _context;
        private readonly IConfiguration _configuration;

        public TransactionsController(MetroDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
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

        [HttpPost("import-demo")]
        public ActionResult<ImportDemoResultDto> ImportDemoData()
        {
            var result = new ImportDemoResultDto();
            string? connStr = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connStr))
            {
                result.Success = false;
                result.Message = "未找到数据库连接字符串 DefaultConnection。";
                return BadRequest(result);
            }

            try
            {
                using var conn = new MySqlConnection(connStr);
                conn.Open();

                using var transaction = conn.BeginTransaction();

                try
                {
                    ExecuteNonQuery(conn, transaction, "DELETE FROM ticket_transaction");
                    ExecuteNonQuery(conn, transaction, "DELETE FROM section_info");
                    ExecuteNonQuery(conn, transaction, "DELETE FROM station_info");
                    ExecuteNonQuery(conn, transaction, "DELETE FROM line_info");
                    ExecuteNonQuery(conn, transaction, "DELETE FROM operator_info");

                    ExecuteNonQuery(conn, transaction, "ALTER TABLE ticket_transaction AUTO_INCREMENT = 1");
                    ExecuteNonQuery(conn, transaction, "ALTER TABLE section_info AUTO_INCREMENT = 1");
                    ExecuteNonQuery(conn, transaction, "ALTER TABLE station_info AUTO_INCREMENT = 1");
                    ExecuteNonQuery(conn, transaction, "ALTER TABLE line_info AUTO_INCREMENT = 1");
                    ExecuteNonQuery(conn, transaction, "ALTER TABLE operator_info AUTO_INCREMENT = 1");

                    ExecuteNonQuery(conn, transaction, @"
                        INSERT INTO operator_info (operator_code, operator_name, contact_person, contact_phone, created_at)
                        VALUES
                        ('OP001', '地铁运营公司A', '张三', '13800000001', NOW()),
                        ('OP002', '地铁运营公司B', '李四', '13800000002', NOW())");

                    ExecuteNonQuery(conn, transaction, @"
                        INSERT INTO line_info (line_code, line_name, operator_id, created_at)
                        VALUES
                        ('L01', '1号线', 1, NOW()),
                        ('L02', '2号线', 1, NOW()),
                        ('L03', '3号线', 2, NOW())");

                    ExecuteNonQuery(conn, transaction, @"
                        INSERT INTO station_info (station_code, station_name, is_transfer, created_at)
                        VALUES
                        ('S001', '火车站', 1, NOW()),
                        ('S002', '人民广场', 1, NOW()),
                        ('S003', '大学城', 0, NOW()),
                        ('S004', '体育中心', 0, NOW()),
                        ('S005', '机场东', 0, NOW())");

                    ExecuteNonQuery(conn, transaction, @"
                        INSERT INTO section_info (line_id, from_station_id, to_station_id, distance_km, is_bidirectional)
                        VALUES
                        (1, 1, 2, 3.50, 1),
                        (1, 2, 3, 4.20, 1),
                        (2, 2, 4, 5.10, 1),
                        (3, 4, 5, 8.30, 1)");

                    ExecuteNonQuery(conn, transaction, @"
                        INSERT INTO ticket_transaction
                        (card_no, entry_time, entry_station_id, exit_time, exit_station_id, pay_amount, payment_type, transaction_type, transaction_status, exception_type, created_at)
                        VALUES
                        ('CARD001', '2026-04-06 08:00:00', 1, '2026-04-06 08:35:00', 3, 4.00, 'CARD', 'EXIT', 'NORMAL', NULL, NOW()),
                        ('CARD002', '2026-04-06 09:10:00', 2, '2026-04-06 09:40:00', 4, 3.00, 'QR', 'EXIT', 'NORMAL', NULL, NOW()),
                        ('CARD003', '2026-04-06 10:00:00', 4, '2026-04-06 10:50:00', 5, 5.00, 'CARD', 'EXIT', 'NORMAL', NULL, NOW())");

                    transaction.Commit();

                    result.Success = true;
                    result.Message = "测试数据导入成功。";
                    result.OperatorCount = GetCount(conn, "operator_info");
                    result.LineCount = GetCount(conn, "line_info");
                    result.StationCount = GetCount(conn, "station_info");
                    result.SectionCount = GetCount(conn, "section_info");
                    result.TransactionCount = GetCount(conn, "ticket_transaction");

                    return Ok(result);
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"导入失败：{ex.Message}";
                return StatusCode(500, result);
            }
        }

        private static void ExecuteNonQuery(MySqlConnection conn, MySqlTransaction transaction, string sql)
        {
            using var cmd = new MySqlCommand(sql, conn, transaction);
            cmd.ExecuteNonQuery();
        }

        private static int GetCount(MySqlConnection conn, string tableName)
        {
            using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM `{tableName}`", conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }
}