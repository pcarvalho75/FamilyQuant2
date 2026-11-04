using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FamilyQuant;

/// <summary>Read-only database population, not peptide support or signal. Null panel counts
/// mean conflicting membership; zero means no eligible stored entries.</summary>
public sealed record FamilyDatabaseEntryCountView(IReadOnlyDictionary<int, int> ByUserId,
    IReadOnlyDictionary<string, int?> ByPanelKey, bool PanelMappingCurrent);

public static class FamilyDatabaseEntryCounts
{
    public static FamilyDatabaseEntryCountView Read(FamilyClusterFile curation)
    {
        ArgumentNullException.ThrowIfNull(curation);
        var assigned = curation.MyExtendedFastaItems.Where(p => p != null && p.HasUserId).ToArray();
        var targets = assigned.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier ?? string.Empty)
            && !SequenceLabels.IsContaminant(p.SequenceIdentifier)).ToArray();
        if (targets.Any(p => string.IsNullOrWhiteSpace(p.SequenceIdentifier)))
            throw new ArgumentException("Database entries require recorded identifiers.", nameof(curation));
        // Each stored record is one entry. Equal sequences are not the same database entry.
        var users = new SortedDictionary<int, int>();
        foreach (int id in assigned.Select(p => p.UserID).Distinct().Order()) users.Add(id, 0);
        foreach (var entry in targets) users[entry.UserID]++;
        var panels = new SortedDictionary<string, int?>(StringComparer.Ordinal);
        FamilyDatabaseEntryCountView Result(bool current) => new(
            new ReadOnlyDictionary<int, int>(users), new ReadOnlyDictionary<string, int?>(panels), current);
        if (curation.FamilyPanel == null) return Result(true);
        var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(curation);
        if (!map.IsCurrent) return Result(false);
        var conflicts = map.UserFamilies.Values.Where(f => f.HasConflict).SelectMany(f => f.PanelKeys)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string key in map.PanelRows.Where(r => r.EffectivePanelKey != null
            && r.Decision is FamilyPanelDecision.Quantify or FamilyPanelDecision.ReportUnderOthers)
            .Select(r => r.EffectivePanelKey!).Distinct(StringComparer.Ordinal))
            panels.Add(key, conflicts.Contains(key) ? null : 0);
        // Reuse the native collapse, including aliases, outside groups and conflicts. Do not
        // infer membership from names, or add stored summary counts from different families.
        foreach (var slot in EffectivePanelFamilyStudy.Slots(curation, map))
        {
            if (slot.Kind != EffectivePanelFamilyKind.PanelFamily || slot.PanelFamilyKey is not string key
                || !panels.TryGetValue(key, out int? available) || !available.HasValue) continue;
            var sourceIds = slot.SourceUserIds.ToHashSet();
            panels[key] = targets.Count(p => sourceIds.Contains(p.UserID));
        }
        return Result(true);
    }
}
