using System.ComponentModel.DataAnnotations;

namespace database.Models
{
    public class OperatorInfo
    {
        [Key]
        public long OperatorId { get; set; }

        public string OperatorCode { get; set; } = string.Empty;

        public string OperatorName { get; set; } = string.Empty;

        public string? ContactPerson { get; set; }

        public string? ContactPhone { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}