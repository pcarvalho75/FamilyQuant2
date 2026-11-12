using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace FamilyQuant.Evidence;

public sealed partial class EvidenceRecipe
{
    /// <summary>Already applied, locked memberships revalidated against the current panel.
    /// They refresh row names and provenance without becoming new user selections.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<EvidenceAssignment>? RetainedPanelAssignments { get; set; }
}

public static partial class EvidenceApply
{
    private static List<EvidenceAssignment>? RetainPanelAssignments(FamilyClusterFile file, EvidenceProposal proposal,
        IReadOnlyDictionary<string, string> names, IReadOnlyList<EvidenceFamilyIdentity> identities)
    {
        if (proposal.PanelContext == null || file.EvidenceRecipe?.PanelContext == null) return null;
        var retained = new List<EvidenceAssignment>();
        foreach (var group in proposal.Proteins.Where(row => file.LockedUserIds.Contains(row.PreviousFamilyId))
            .GroupBy(row => row.PreviousFamilyId))
        {
            var anchors = identities.Where(identity => identity.FamilyId == group.Key).ToArray();
            if (anchors.Length != 1 || !anchors[0].PanelFamily
                || anchors[0].GroupKey != GroupKey(anchors[0].Family, string.Empty)
                || !anchors[0].Identifiers.ToHashSet(StringComparer.Ordinal).SetEquals(group.Select(row => row.Identifier))
                || group.Any(row => row.Family != anchors[0].Family || !row.SuggestedSelected || row.Tier == EvidenceTier.AddedByHand)
                || !names.TryGetValue(anchors[0].Family, out string? name)) continue;
            foreach (var row in group.OrderBy(row => row.ProteinIndex))
            {
                var protein = file.MyExtendedFastaItems[row.ProteinIndex];
                retained.Add(new EvidenceAssignment
                {
                    ProteinIndex = row.ProteinIndex, Identifier = row.Identifier, Family = row.Family,
                    GroupKey = anchors[0].GroupKey, Name = name, FamilyId = protein.UserID,
                    PreviousFamilyId = protein.UserID, PreviousSearchId = protein.USearchID,
                    Tier = row.Tier, Reason = row.Reason,
                    AutomaticEvidence = row.AutomaticEvidence == null ? null : Clone(row.AutomaticEvidence),
                });
            }
        }
        return retained.Count == 0 ? null : retained.OrderBy(row => row.ProteinIndex).ToList();
    }
}
