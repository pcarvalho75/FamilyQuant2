using System;
using System.Collections.Generic;

namespace FamilyQuant.Panels;

/// <summary>The user's original row, including exclusions and merge aliases. FamilyUserIds
/// describe its effective measured target; merged rows are aliases, not additional quantities.</summary>
public sealed class PanelFamilyRowMap
{
    public string PanelRowKey { get; init; } = string.Empty;
    public string? EffectivePanelKey { get; init; }
    public FamilyPanelDecision Decision { get; init; }
    public string MergeTargetFamilyKey { get; init; } = string.Empty;
    public IReadOnlyList<int> FamilyUserIds { get; init; } = Array.Empty<int>();
}

public sealed class PanelFamilyUserMap
{
    public int FamilyUserId { get; init; }
    /// <summary>Effective key, or the original excluded key so its report decision is retained.</summary>
    public string? PanelFamilyKey { get; init; }
    public FamilyPanelDecision? Decision { get; init; }
    public IReadOnlyList<string> PanelKeys { get; init; } = Array.Empty<string>();
    public bool HasOutsideMembers { get; init; }
    public bool HasConflict { get; init; }
}

public sealed class PanelFamilyMap
{
    public bool IsCurrent { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyDictionary<int, PanelFamilyUserMap> UserFamilies { get; init; } = new Dictionary<int, PanelFamilyUserMap>();
    public IReadOnlyList<PanelFamilyRowMap> PanelRows { get; init; } = Array.Empty<PanelFamilyRowMap>();
    public IReadOnlyList<int> ConflictingUserIds { get; init; } = Array.Empty<int>();
}
