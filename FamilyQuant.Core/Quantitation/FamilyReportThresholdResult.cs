using System.Collections.Generic;

namespace FamilyQuant
{
    /// <summary>Report partition based on the mean of available per-run assigned-signal shares.
    /// Shares and threshold are fractions. OtherFamilies retains the source rows for the supplement;
    /// unavailable means are separate and must not be treated as zero.</summary>
    public sealed class FamilyReportThresholdResult
    {
        public double Threshold { get; set; }
        public List<FamilyRunShareSummary> ListedFamilies { get; set; } = new();
        public List<FamilyRunShareSummary> OtherFamilies { get; set; } = new();
        public List<FamilyRunShareSummary> UnavailableFamilies { get; set; } = new();
        public List<FamilyRunShare> OthersRuns { get; set; } = new();
        public double? OthersMeanShare { get; set; }
    }
}
