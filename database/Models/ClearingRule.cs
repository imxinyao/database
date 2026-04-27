using System;
using System.ComponentModel.DataAnnotations;

namespace database.Models
{
    public class ClearingRule
    {
        [Key]
        public int RuleId { get; set; }

        public string RuleCode { get; set; } = string.Empty;

        public string RuleName { get; set; } = string.Empty;

        public string RuleType { get; set; } = "FARE";

        public string? PricingMethod { get; set; }

        public decimal? StartDistance { get; set; }

        public decimal? EndDistance { get; set; }

        public decimal? Price { get; set; }

        public decimal? BaseShareRatio { get; set; }

        public decimal? TransferCoefficient { get; set; }

        public string? AlgorithmType { get; set; } = "shortest";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }
    }
}