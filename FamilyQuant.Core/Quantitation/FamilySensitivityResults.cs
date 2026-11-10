using System.Collections.Generic;
using FamilyQuant.DeNovo;

namespace FamilyQuant;

/// <summary>Comparison of actual computed cutoff variants. Missing variants are absent, never zero.
/// Shares use each variant's measured peptide signal on the measured envelope basis. Full counts
/// overlap and are not a normalized family composition. Family-world-only credits are excluded.</summary>
public sealed class FamilySensitivityResult
{
    public string Method { get; set; } = string.Empty;
    public string CurationFingerprint { get; set; } = string.Empty;
    public string InputCompatibilityFingerprint { get; set; } = string.Empty;
    public List<FamilySensitivityVariant> Variants { get; set; } = new();
}

public sealed class FamilySensitivityVariant
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>SHA256 of a loaded project, or of the stored current quant rows and source manifest facts.</summary>
    public string SourceSha256 { get; set; } = string.Empty;
    public List<DeNovoInputManifest> DeNovoInputs { get; set; } = new();
    public FamilyMappingProvenance? Mapping { get; set; }
    public List<FamilySensitivityPolicyResult> Policies { get; set; } = new();
}

public sealed class FamilySensitivityPolicyResult
{
    public SharedPeptidePolicy Policy { get; set; }
    public double MeasuredSignal { get; set; }
    public List<FamilySensitivityShare> Families { get; set; } = new();
    /// <summary>FL's per-run composition result for the two additive policies. Null for overlapping
    /// full counts, for which a normalized composition is undefined.</summary>
    public FamilyRunSharesResult? PerRunShares { get; set; }
}

public sealed class FamilySensitivityShare
{
    public int FamilyUserId { get; set; }
    public string Description { get; set; } = string.Empty;
    public double Signal { get; set; }
    public double? ShareOfMeasuredSignal { get; set; }
}
