using System;
using System.ComponentModel.DataAnnotations;

namespace database.Models
{
    public class ClearingResult
    {
        [Key]
        public long ResultId { get; set; }

        public int TaskId { get; set; }

        public long TransactionId { get; set; }

        public long LineId { get; set; }

        public long OperatorId { get; set; }

        public decimal ClearingAmount { get; set; }

        public string? PathText { get; set; }

        public string? TransferText { get; set; }

        public string? PathGroup { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ClearingTask? Task { get; set; }

        public TicketTransaction? Transaction { get; set; }

        public LineInfo? Line { get; set; }
    }
}