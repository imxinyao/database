namespace database.DTOs
{
    public class TransactionListItemDto
    {
        public long TransactionId { get; set; }
        public string CardNo { get; set; } = string.Empty;

        public long? EntryStationId { get; set; }
        public long? ExitStationId { get; set; }

        public string EntryStationName { get; set; } = string.Empty;
        public string ExitStationName { get; set; } = string.Empty;

        public decimal PayAmount { get; set; }
        public string TransactionStatus { get; set; } = string.Empty;
        public string PaymentType { get; set; } = string.Empty;
        public string TransactionType { get; set; } = string.Empty;
        public DateTime? EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }
    }
}