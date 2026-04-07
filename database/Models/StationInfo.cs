using System.ComponentModel.DataAnnotations;

namespace database.Models
{
    public class StationInfo
    {
        [Key]
        public long StationId { get; set; }

        public string StationCode { get; set; } = string.Empty;

        public string StationName { get; set; } = string.Empty;

        public bool IsTransfer { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}