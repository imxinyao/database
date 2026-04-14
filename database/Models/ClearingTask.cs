using System;

namespace database.Models
{
    public class ClearingTask
    {
        public int TaskId { get; set; }
        public string TaskName { get; set; } = string.Empty;
        public int DataCount { get; set; }
        public string AlgorithmType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
