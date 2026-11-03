using System;
using System.Collections.Generic;

namespace FamilyQuant
{
    [Flags]
    public enum FamilyReportOtherReason { None = 0, Threshold = 1, PanelDecision = 2 }

    public sealed class FamilyReportOtherMembership
    {
        public int FamilyUserId { get; set; }
        public FamilyReportOtherReason Reason { get; set; }
    }

    public sealed class FamilyReportMappingConflict
    {
        public FamilyRunShareSummary Family { get; set; } = new();
        public List<string> PanelFamilyKeys { get; set; } = new();
        public bool HasOutsideMembers { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>Statistics over available per-run shares. SD is sample SD; CV is SD divided by mean.</summary>
    public sealed class FamilyReportShareStatistics
    {
        public int AvailableRunCount { get; set; }
        public double? Mean { get; set; }
        public double? StandardDeviation { get; set; }
        public double? CoefficientOfVariation { get; set; }
    }

    /// <summary>Canonical report partition. Shares and threshold are fractions. OtherFamilies keeps
    /// source rows for the supplement. Unavailable and conflicting families remain separate.</summary>
    public sealed class FamilyReportThresholdResult
    {
        public double Threshold { get; set; }
        public List<FamilyRunShareSummary> ListedFamilies { get; set; } = new();
        public List<FamilyRunShareSummary> OtherFamilies { get; set; } = new();
        public List<FamilyReportOtherMembership> OtherMembership { get; set; } = new();
        public List<FamilyRunShareSummary> UnavailableFamilies { get; set; } = new();
        public List<FamilyRunShareSummary> ExcludedFamilies { get; set; } = new();
        public List<FamilyReportMappingConflict> ConflictFamilies { get; set; } = new();
        public List<FamilyRunShare> OthersRuns { get; set; } = new();
        public double? OthersMeanShare { get; set; }
        public FamilyReportShareStatistics OthersAssignedStatistics { get; set; } = new();
        public FamilyReportShareStatistics OthersMeasuredStatistics { get; set; } = new();
    }
}
