using FamilyQuant.Evidence.Positions;
using System.Collections.Generic;

namespace FamilyQuant;

public enum PeptideLocalizationMatchKind { ExactContainment, Aligned }

/// <summary>One-based inclusive protein residues actually covered. Gapped alignments use several
/// intervals; the bounding span must never be substituted for these intervals.</summary>
public sealed class PeptideResidueInterval
{
    public int Start { get; set; }
    public int End { get; set; }
}

/// <summary>One admissible placement, supplied by the mapping stage. ProteinSequence and Positions
/// are inputs, not repeated in the persisted segment table. Aligned must never be relabeled exact.</summary>
public sealed class PeptideProteinPlacement
{
    public string ProteinIdentifier { get; set; } = string.Empty;
    public string ProteinSequence { get; set; } = string.Empty;
    public PeptideLocalizationMatchKind MatchKind { get; set; }
    public List<PeptideResidueInterval> Intervals { get; set; } = new();
    public ProteinPositionEvidence? Positions { get; set; }
}

/// <summary>All runs pooled, PTM-cleaned and I/L-folded sequence. Signal is a display allocation:
/// divided equally among candidate proteins and their placements, not an independent protein amount.</summary>
public sealed class LocalizedPeptideSegment
{
    public int FamilyUserId { get; set; }
    public string ProteinIdentifier { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string NormalizedSequence { get; set; } = string.Empty;
    /// <summary>One means family-unique, more than one shared, zero not recorded. Mapped evidence
    /// with zero credited signal is still retained for coverage inspection.</summary>
    public int FamilyMembershipCount { get; set; }
    public PeptideLocalizationMatchKind MatchKind { get; set; }
    public List<PeptideResidueInterval> Intervals { get; set; } = new();
    public double Signal { get; set; }
}

public sealed class FamilyRankedProtein
{
    public int Rank { get; set; }
    public string ProteinIdentifier { get; set; } = string.Empty;
    public int ProteinLength { get; set; }
    public string ProteinSequenceSha256 { get; set; } = string.Empty;
    public bool PositionEvidenceVerified { get; set; }
    public double CreditedSignal { get; set; }
    public double ExactSignal { get; set; }
    public double AlignedSignal { get; set; }
}

public sealed class FamilyProteinRanking
{
    public int FamilyUserId { get; set; }
    public double FamilySignal { get; set; }
    public double UnlocalizedSignal { get; set; }
    public List<FamilyRankedProtein> Proteins { get; set; } = new();
}

/// <summary>All runs pooled. Each segment retains its evidence side and exact/aligned status.
/// Rankings use additive allocated signal, descending, with ordinal protein-ID tie breaks.</summary>
public sealed class FamilyPeptideLocalizationResult : IFamilyMembershipStudy
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? MembershipSha256 { get; set; }
    public string Method { get; set; } = string.Empty;
    public SharedPeptidePolicy Policy { get; set; }
    public List<LocalizedPeptideSegment> Segments { get; set; } = new();
    public List<FamilyProteinRanking> Families { get; set; } = new();
}

public sealed class FamilyPositionStudiesResult
{
    public FamilyDomainPartsResult DomainParts { get; set; } = new();
    public FamilyPeptideLocalizationResult Localization { get; set; } = new();
}
