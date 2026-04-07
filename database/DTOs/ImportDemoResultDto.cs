namespace database.DTOs
{
    public class ImportDemoResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public int OperatorCount { get; set; }
        public int LineCount { get; set; }
        public int StationCount { get; set; }
        public int SectionCount { get; set; }
        public int TransactionCount { get; set; }
    }
}