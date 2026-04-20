using Microsoft.AspNetCore.Http;
using System.Globalization;
using database.Data;
using database.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace database.Dtos
{
    public class TimetableImportRequest
    {
        public string TimetableCode { get; set; } = string.Empty;
        public string TimetableName { get; set; } = string.Empty;
        public long LineId { get; set; }
        public int Direction { get; set; }
        public string VersionNo { get; set; } = string.Empty;
        public DateTime EffectiveStartDate { get; set; }
        public DateTime EffectiveEndDate { get; set; }
        public string RunCalendarType { get; set; } = "DAILY";
        public string? Remark { get; set; }
        public IFormFile? File { get; set; }
    }
}