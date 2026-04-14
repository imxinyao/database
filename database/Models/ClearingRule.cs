using System;

namespace database.Models
{
    public class ClearingRule
    {
        public int RuleId { get; set; }
        public string RuleCode { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public string RuleType { get; set; } = string.Empty;
        public string? PricingMethod { get; set; }
        public decimal? StartDistance { get; set; }
        public decimal? EndDistance { get; set; }
        public decimal? Price { get; set; }
        public decimal? BaseShareRatio { get; set; }
        public decimal? TransferCoefficient { get; set; }
        public string? AlgorithmType { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
