using System.ComponentModel.DataAnnotations;

namespace database.Models
{
    public class TrainTimetable
    {
        [Key]
        public long TimetableId { get; set; }

        public string TimetableCode { get; set; } = string.Empty;

        public string TimetableName { get; set; } = string.Empty;

        public long LineId { get; set; }

        public int Direction { get; set; }

        public string VersionNo { get; set; } = string.Empty;

        public DateTime EffectiveStartDate { get; set; }

        public DateTime EffectiveEndDate { get; set; }

        public string RunCalendarType { get; set; } = "DAILY";

        public int Status { get; set; } = 0;

        public int IsActive { get; set; } = 0;

        public string? Remark { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}