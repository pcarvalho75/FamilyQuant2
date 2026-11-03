using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FamilyQuant.Panels;

namespace FamilyQuant;

/// <summary>Coordinates panel edits with the separate, undoable assignment operations.</summary>
public static class PanelSubclassRefresh
{
    public static bool DefinitionsChanged(FamilyPanel? before, FamilyPanel? after) => Snapshot(before) != Snapshot(after);

    private static string Snapshot(FamilyPanel? panel) => JsonSerializer.Serialize(
        (panel?.Families ?? new List<FamilyPanelFamily>()).Where(f => f.Subclasses.Count > 0)
            .OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new
            {
                f.Key, Subclasses = f.Subclasses.OrderBy(s => s.Key, StringComparer.Ordinal).ToArray()
            }).ToArray());

    public static async Task<bool> RecomputeIfCurrentAsync(bool changed, Func<bool> familyCurrent, Func<Task<bool>> recompute)
    {
        ArgumentNullException.ThrowIfNull(familyCurrent);
        ArgumentNullException.ThrowIfNull(recompute);
        return changed && familyCurrent() && await recompute();
    }

    /// <summary>When family assignments are stale, offer their review before subclass recomputation.
    /// If review performs its own subclass refresh, report that result without running it again.</summary>
    public static async Task<bool> ReviewThenRecomputeAsync(bool familyStale, bool subclassesChanged, Func<bool> familyCurrent,
        Func<bool> confirmReview, Func<Task>? reviewFamilies, Func<Task<bool>> recompute, Func<bool>? subclassesCurrent = null)
    {
        ArgumentNullException.ThrowIfNull(familyCurrent);
        ArgumentNullException.ThrowIfNull(confirmReview);
        ArgumentNullException.ThrowIfNull(recompute);
        if (familyStale || (subclassesChanged && !familyCurrent()))
        {
            if (reviewFamilies == null || !confirmReview()) return false;
            await reviewFamilies();
            if (!familyCurrent()) return false;
            if (subclassesCurrent != null) return subclassesChanged && subclassesCurrent();
        }
        return await RecomputeIfCurrentAsync(subclassesChanged, familyCurrent, recompute);
    }
}
