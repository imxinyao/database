using System;
using System.ComponentModel.DataAnnotations;

namespace database.Models
{
    public class ClearingUnmatchedTransaction
    {
        [Key]
        public long UnmatchedId { get; set; }

        public int TaskId { get; set; }

        public long TransactionId { get; set; }

        public string CardNo { get; set; } = string.Empty;

        public DateTime? EntryTime { get; set; }

        public long? EntryStationId { get; set; }

        public DateTime? ExitTime { get; set; }

        public long? ExitStationId { get; set; }

        public string ReasonCode { get; set; } = string.Empty;

        public string ReasonMessage { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ClearingTask? Task { get; set; }

        public TicketTransaction? Transaction { get; set; }
    }
}