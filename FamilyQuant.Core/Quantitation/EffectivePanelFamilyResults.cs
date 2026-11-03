using System.Collections.Generic;

namespace FamilyQuant;

/// <summary>Optional native study of effective panel families. Nested native FamilyUserId fields
/// are deterministic study-local slot IDs, resolved by Families; they are never source UserIDs.
/// Original UserID studies remain separate and unchanged. Shares are fractions.</summary>
public sealed class EffectivePanelFamilyResult : IFamilyMembershipStudy
{
    public int SchemaVersion { get; set; } = 1;
    public string? MembershipSha256 { get; set; }
    public string PanelMapSha256 { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public SharedPeptidePolicy Policy { get; set; }
    public List<EffectivePanelFamilySlot> Families { get; set; } = new();
    public FamilyRunSharesResult RunShares { get; set; } = new();
    public FamilyProteinCountsResult ProteinCounts { get; set; } = new();
    public FamilySubclassPartsResult? SubclassParts { get; set; }
    public FamilyDomainPartsResult? DomainParts { get; set; }
    public FamilyPeptideLocalizationResult? Localization { get; set; }
    public List<EffectivePanelPartAvailability> Parts { get; set; } = new();
}

public enum EffectivePanelFamilyKind { PanelFamily, OutsidePanel, AssignmentConflict, Excluded }

public sealed class EffectivePanelFamilySlot
{
    public int StudyFamilyId { get; set; }
    public string? PanelFamilyKey { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public EffectivePanelFamilyKind Kind { get; set; }
    public List<int> SourceUserIds { get; set; } = new();
}

public enum EffectivePanelPartKind { Subclass, Domain, Product }
public enum EffectivePanelPartState { NotComputed, NotMeasured, Observed, NotResolved }

/// <summary>Names come from panel definitions or recorded position annotations. An unmeasured named
/// part has null signal/share plus an explicit reason; it is never filled with a guessed zero.</summary>
public sealed class EffectivePanelPartAvailability
{
    public string PanelFamilyKey { get; set; } = string.Empty;
    public EffectivePanelPartKind Kind { get; set; }
    public string? PartKey { get; set; }
    public string Label { get; set; } = string.Empty;
    public EffectivePanelPartState State { get; set; }
    public double? Signal { get; set; }
    public double? ShareOfFamily { get; set; }
    public string Reason { get; set; } = string.Empty;
}
