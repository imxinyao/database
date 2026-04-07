using System.ComponentModel.DataAnnotations;

namespace database.Models
{
    public class TicketTransaction
    {
        [Key]
        public long TransactionId { get; set; }

        public string CardNo { get; set; } = string.Empty;

        public DateTime? EntryTime { get; set; }

        public long? EntryStationId { get; set; }

        public DateTime? ExitTime { get; set; }

        public long? ExitStationId { get; set; }

        public decimal PayAmount { get; set; }

        public string PaymentType { get; set; } = string.Empty;

        public string TransactionType { get; set; } = string.Empty;

        public string TransactionStatus { get; set; } = "NORMAL";

        public string? ExceptionType { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}