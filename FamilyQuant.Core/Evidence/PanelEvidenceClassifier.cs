using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace FamilyQuant.Evidence;

public sealed class PanelFamilyLinkProposal
{
    public string Key { get; init; } = string.Empty;
    public string SourceFamily { get; init; } = string.Empty;
    public string ArchitectureKey { get; init; } = string.Empty;
    public IReadOnlyList<string> CandidateFamilyKeys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SupportingAccessions { get; init; } = Array.Empty<string>();
    public bool IsConfirmed { get; init; }
    public string? SelectedFamilyKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPfamCoverageResolved { get; init; }
    // Presentation only: the existing saved link Source remains the serialized note.
    [JsonIgnore] public string SourceNote { get; init; } = string.Empty;
    public bool NeedsConfirmation => CandidateFamilyKeys.Count > 1 && !IsConfirmed;
}

public sealed class PanelFamilyLinkSelection
{
    public string LinkKey { get; init; } = string.Empty;
    public string? PanelFamilyKey { get; init; }
    public string Source { get; init; } = string.Empty;
}

public sealed class PanelEvidenceContext
{
    public string SourcePanelFingerprint { get; init; } = string.Empty;
    public string AppliedPanelFingerprint { get; init; } = string.Empty;
    public string ReferenceFingerprint { get; init; } = string.Empty;
    public string SearchFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<PanelFamilyLinkProposal> Links { get; init; } = Array.Empty<PanelFamilyLinkProposal>();
}

public sealed partial class EvidenceRecipe
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PanelEvidenceContext? PanelContext { get; set; }
}
