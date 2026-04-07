using System.ComponentModel.DataAnnotations;

namespace database.Models
{
    public class LineInfo
    {
        [Key]
        public long LineId { get; set; }

        public string LineCode { get; set; } = string.Empty;

        public string LineName { get; set; } = string.Empty;

        public long OperatorId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public OperatorInfo? Operator { get; set; }
    }
}