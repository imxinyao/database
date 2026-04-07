using System.ComponentModel.DataAnnotations;

namespace database.Models
{
    public class SectionInfo
    {
        [Key]
        public long SectionId { get; set; }

        public long LineId { get; set; }

        public long FromStationId { get; set; }

        public long ToStationId { get; set; }

        public decimal DistanceKm { get; set; }

        public bool IsBidirectional { get; set; } = true;

        public LineInfo? Line { get; set; }
    }
}