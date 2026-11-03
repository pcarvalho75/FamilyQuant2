using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace FamilyQuant.Homology;

/// <summary>Experimental peptide mapping changes. None is enabled by default; callers name the
/// query role explicitly. Identified peptides retain the established containment/scoring rule.</summary>
public enum HomologyQueryRole { Unspecified, Identified, DeNovo }

public sealed class HomologyScienceOptions
{
    public HomologyQueryRole QueryRole { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DecoyIdentityGateOptions? DecoyGate { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TagErrorGateOptions? TagErrors { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HomologyScoreTierOptions? ScoreTier { get; set; }
    [JsonIgnore]
    public bool AnyEnabled => DecoyGate?.Enabled == true || TagErrors?.Enabled == true || ScoreTier?.Enabled == true;

    public HomologyScienceOptions Clone() => new()
    {
        QueryRole = QueryRole,
        DecoyGate = DecoyGate?.Clone(), TagErrors = TagErrors?.Clone(), ScoreTier = ScoreTier?.Clone(),
    };

    public void Validate()
    {
        if (!AnyEnabled) return;
        if (QueryRole == HomologyQueryRole.Unspecified || !Enum.IsDefined(QueryRole))
            throw new ArgumentException("An experimental search must explicitly identify de novo or identified queries.");
        DecoyGate?.Validate(); TagErrors?.Validate(); ScoreTier?.Validate();
    }
}

/// <summary>Experimental per-search, per-query-length identity competition. Its empirical decoy
/// estimate is reported with its denominator; it is not an independent FDR guarantee.</summary>
public sealed class DecoyIdentityGateOptions
{
    public bool Enabled { get; set; }
    public double MaximumDecoyRatio { get; set; } = 0.01;
    public double MinimumSearchIdentity { get; set; } = 0.8;
    public int MinimumLengthQueries { get; set; } = 100;
    public DecoyIdentityGateOptions Clone() => (DecoyIdentityGateOptions)MemberwiseClone();
    public void Validate()
    {
        if (!Enabled) return;
        if (!double.IsFinite(MaximumDecoyRatio) || MaximumDecoyRatio <= 0 || MaximumDecoyRatio >= 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumDecoyRatio));
        if (!double.IsFinite(MinimumSearchIdentity) || MinimumSearchIdentity < 0.8 || MinimumSearchIdentity > 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumSearchIdentity));
        if (MinimumLengthQueries < 1) throw new ArgumentOutOfRangeException(nameof(MinimumLengthQueries));
    }
}

/// <summary>Experimental identity gate only. PAM30MS continues to score the original query's full
/// windows. Local confidence values are percentages aligned to the normalized original sequence.</summary>
public sealed class TagErrorGateOptions
{
    public bool Enabled { get; set; }
    public bool AllowNAndGG { get; set; } = true;
    public bool AllowQAndAG { get; set; } = true;
    public bool AllowAdjacentOrderSwap { get; set; } = true;
    public bool AllowKAndQ { get; set; } = true;
    public int MaximumRewrites { get; set; } = 1;
    public bool WeightByLocalConfidence { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, double[]>? LocalConfidenceBySequence { get; set; }
    public TagErrorGateOptions Clone() => new()
    {
        Enabled = Enabled, AllowNAndGG = AllowNAndGG, AllowQAndAG = AllowQAndAG,
        AllowAdjacentOrderSwap = AllowAdjacentOrderSwap, AllowKAndQ = AllowKAndQ,
        MaximumRewrites = MaximumRewrites, WeightByLocalConfidence = WeightByLocalConfidence,
        LocalConfidenceBySequence = LocalConfidenceBySequence?.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal),
    };
    public void Validate()
    {
        if (!Enabled) return;
        if (MaximumRewrites < 0 || MaximumRewrites > 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumRewrites), "This experimental model supports zero or one rewrite.");
        foreach (var profile in LocalConfidenceBySequence ?? new())
            if (profile.Key.Length == 0 || profile.Value == null || profile.Value.Length != profile.Key.Length
                || profile.Value.Any(v => !double.IsFinite(v) || v < 0 || v > 100))
                throw new ArgumentException("Each local-confidence profile must match its normalized sequence and contain percentages from 0 to 100.");
    }
}

/// <summary>Experimental protein score tier after target-decoy competition. Zero keeps best-score
/// ties; a positive margin also keeps proteins within that many integer PAM30MS units.</summary>
public sealed class HomologyScoreTierOptions
{
    public bool Enabled { get; set; }
    public int MaximumPamScoreDrop { get; set; }
    public HomologyScoreTierOptions Clone() => (HomologyScoreTierOptions)MemberwiseClone();
    public void Validate()
    {
        if (Enabled && MaximumPamScoreDrop < 0) throw new ArgumentOutOfRangeException(nameof(MaximumPamScoreDrop));
    }
}

/// <summary>Query-level experimental audit. Protein multiplicities precede the caller's family
/// join; family multiplicity and shared-signal policy are measured by the attribution producer.</summary>
public sealed record HomologyScienceAudit
{
    public string Method { get; init; } = string.Empty;
    public HomologyQueryRole QueryRole { get; init; }
    public double NominalIdentityGate { get; init; }
    public double? AppliedIdentityGate { get; init; }
    public int GateTargetProteins { get; init; }
    public int GateDecoyProteins { get; init; }
    public double? BestTargetGateIdentity { get; init; }
    public double? BestDecoyGateIdentity { get; init; }
    public HomologyLengthGate? LengthCalibration { get; init; }
    public int ProteinsBeforeTier { get; init; }
    public int ProteinsAfterTier { get; init; }
    public int ProteinsAfterTopCut { get; init; }
    public int? MaximumPamScoreDrop { get; init; }
    public bool UsedLocalConfidence { get; init; }
    public string? GateVariant { get; init; }
    public int? GateVariantOffset { get; init; }
    public IReadOnlyList<HomologyGateAlignment>? GateAlignments { get; init; }
}
