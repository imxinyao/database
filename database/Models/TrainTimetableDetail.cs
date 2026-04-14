using System.ComponentModel.DataAnnotations;

namespace database.Models
{
    public class TrainTimetableDetail
    {
        [Key]
        public long DetailId { get; set; }

        public long TimetableId { get; set; }

        public string TrainNo { get; set; } = string.Empty;

        public long StationId { get; set; }

        public int StationSeq { get; set; }

        public TimeSpan? ArrivalTime { get; set; }

        public TimeSpan? DepartureTime { get; set; }

        public int StopMinutes { get; set; } = 0;

        public int IsOriginStation { get; set; } = 0;

        public int IsTerminalStation { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}