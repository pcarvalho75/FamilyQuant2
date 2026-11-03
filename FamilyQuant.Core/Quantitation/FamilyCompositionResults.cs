using System.Collections.Generic;

namespace FamilyQuant
{
    /// <summary>Optional composition studies. Shares are fractions, not percentages. Signal uses the
    /// primary family basis: observed identified envelope plus reference-calibrated de novo envelope.
    /// Family IDs are current UserIDs. These records never replace the original family table.</summary>
    public sealed class FamilyRunSharesResult
    {
        public string Method { get; set; } = string.Empty;
        public SharedPeptidePolicy Policy { get; set; }
        public List<FamilyRunTotal> Runs { get; set; } = new();
        public List<FamilyRunShareSummary> Families { get; set; } = new();
    }

    public sealed class FamilyRunTotal
    {
        /// <summary>Normalized file name, matched case-insensitively, never by list position.</summary>
        public string Run { get; set; } = string.Empty;
        public double AssignedSignal { get; set; }
        public double MeasuredSignal { get; set; }
    }

    public sealed class FamilyRunShare
    {
        public string Run { get; set; } = string.Empty;
        public double Signal { get; set; }
        /// <summary>Null when this run has no assigned family signal.</summary>
        public double? ShareOfAssignedSignal { get; set; }
        /// <summary>Null when this run has no measured peptide signal. This is not a TIC share.</summary>
        public double? ShareOfMeasuredSignal { get; set; }
    }

    public sealed class FamilyRunShareSummary
    {
        public int FamilyUserId { get; set; }
        public List<FamilyRunShare> Runs { get; set; } = new();
        public int AvailableRunCount { get; set; }
        public double? MeanShare { get; set; }
        /// <summary>Sample SD (n-1); null for fewer than two available runs.</summary>
        public double? StandardDeviation { get; set; }
        /// <summary>Sample SD / mean, as a ratio; null when SD is unavailable or mean is zero.</summary>
        public double? CoefficientOfVariation { get; set; }
    }

    /// <summary>Database-protein diversity, not a gene or proteoform count. Greedy set cover is an
    /// approximation to the minimum. Ties use ordinal protein identifiers. Unexplained peptides stay visible.</summary>
    public sealed class FamilyProteinCountsResult
    {
        public string Method { get; set; } = string.Empty;
        public List<FamilyProteinCount> Families { get; set; } = new();
    }

    public sealed class FamilyProteinCount
    {
        public int FamilyUserId { get; set; }
        public int GreedyProteinCount { get; set; }
        public int UniquePeptideProteinCount { get; set; }
        public int ObservedPeptideCount { get; set; }
        public List<string> SelectedProteinIdentifiers { get; set; } = new();
        public List<string> UnexplainedPeptides { get; set; } = new();
    }

    /// <summary>A partition of credited family signal. A missing key with IsUnresolved=true denotes
    /// the explicit unresolved/outside part. Parts sum to FamilySignal; shares use that denominator.</summary>
    public sealed class FamilySignalPart
    {
        public string? PartKey { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool IsUnresolved { get; set; }
        public double Signal { get; set; }
        public double ShareOfFamily { get; set; }
    }

    public sealed class FamilySignalBreakdown
    {
        public int FamilyUserId { get; set; }
        public double FamilySignal { get; set; }
        public List<FamilySignalPart> Parts { get; set; } = new();
    }

    /// <summary>Peptides unique to one subclass are credited there; all others remain not resolved.
    /// Protein labels come from the panel's evidence assignment, never from fixed subclass names.</summary>
    public sealed class FamilySubclassPartsResult
    {
        public string Method { get; set; } = string.Empty;
        public SharedPeptidePolicy Policy { get; set; }
        public List<FamilySignalBreakdown> Families { get; set; } = new();
    }

    /// <summary>Separate partitions by domain and by named mature product. Positions are one-based,
    /// inclusive. Overlapping annotations require an explicit allocation method, recorded in Method.</summary>
    public sealed class FamilyDomainPartsResult
    {
        public string Method { get; set; } = string.Empty;
        public SharedPeptidePolicy Policy { get; set; }
        public List<FamilySignalBreakdown> Domains { get; set; } = new();
        public List<FamilySignalBreakdown> Products { get; set; } = new();
    }
}
