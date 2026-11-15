using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

/// <summary>Classifies the complete target membership, independently of which members have signal.</summary>
public static class FamilyContaminants
{
    public static HashSet<int> OnlyIds(FamilyClusterFile project,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? sourceUserIds = null)
    {
        var targets = new Dictionary<int, bool>();
        foreach (var protein in project.MyExtendedFastaItems)
        {
            if (!protein.HasUserId || SequenceLabels.IsDecoy(protein.SequenceIdentifier)) continue;
            bool contaminant = SequenceLabels.IsContaminant(protein.SequenceIdentifier);
            targets[protein.UserID] = contaminant && (!targets.TryGetValue(protein.UserID, out bool previous) || previous);
        }
        var ids = targets.Where(pair => pair.Value).Select(pair => pair.Key).ToHashSet();
        if (sourceUserIds == null) return ids;
        var grouped = new HashSet<int>();
        foreach (var pair in sourceUserIds)
        {
            bool any = false, all = true;
            foreach (int id in pair.Value)
                if (targets.TryGetValue(id, out bool contaminant)) { any = true; all &= contaminant; }
            if (any && all) grouped.Add(pair.Key);
        }
        return grouped;
    }
}
