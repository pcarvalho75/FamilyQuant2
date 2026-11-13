using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Evidence;

/// <summary>EX2: a family change made in the program after Apply grouping (Edit selected proteins, a typed User ID,
/// Group by keywords, Recluster by words) moves the facts of the proteins it moved with them, so the evidence stays
/// current and the report exports. A moved protein keeps its own list row; one outside every row takes the row of the
/// family it joined when that family holds one row. A family left with two rows, or a row and outside proteins, is
/// reported without a link, as any conflict. Undo of the change restores the facts with the membership.</summary>
public sealed class PanelMembershipEdit
{
    public const string MovedByYou = "Moved by you";
    public const string RegroupedByWords = "Regrouped by words";
    private readonly FamilyClusterFile file;
    private readonly int[] before;
    private readonly string fingerprint;

    internal PanelMembershipEdit(FamilyClusterFile file, int[] before, string fingerprint)
    {
        this.file = file; this.before = before; this.fingerprint = fingerprint;
    }

    /// <summary>Call after the change, with the Undo step taken before it. Returns the number of moved proteins.</summary>
    public int Commit(string reason, GroupingSnapshot? undo)
    {
        var rows = file.PanelProteinAssignments;
        var recipe = file.EvidenceRecipe;
        if (rows == null || recipe?.PanelProteinAssignments == null || recipe.PanelProteinAssignments.Count != rows.Count
            || file.MyExtendedFastaItems.Count != before.Length || file.PanelProteinAssignmentsFingerprint != fingerprint) return 0;
        int[] moved = Enumerable.Range(0, rows.Count)
            .Where(i => file.MyExtendedFastaItems[rows[i].ProteinIndex].UserID != before[rows[i].ProteinIndex]).ToArray();
        if (moved.Length == 0) return 0;
        var after = rows.ToList();
        foreach (int i in moved)
        {
            after[i] = EvidenceApply.Clone(rows[i]);
            after[i].FamilyUserId = file.MyExtendedFastaItems[rows[i].ProteinIndex].UserID;
        }
        var held = after.Where(r => r.IsApplied && r.PanelFamilyKey != null && r.FamilyUserId.HasValue).GroupBy(r => r.FamilyUserId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(r => r.PanelFamilyKey!).Distinct(StringComparer.Ordinal).ToArray());
        var kind = reason == RegroupedByWords ? PanelAssignmentEvidenceKind.RegroupedByWords : PanelAssignmentEvidenceKind.MovedByYou;
        foreach (int i in moved)
        {
            var row = after[i];
            string previous = row.Reason;
            foreach (string prefix in new[] { MovedByYou + ". ", RegroupedByWords + ". " })
                if (previous.StartsWith(prefix, StringComparison.Ordinal)) previous = previous[prefix.Length..];
            if (!row.IsApplied && held.TryGetValue(row.FamilyUserId!.Value, out var keys) && keys.Length == 1)
            {
                row.PanelFamilyKey = keys[0]; row.IsApplied = true; row.EvidenceKind = kind;
                row.Reason = reason + " into a family of this list row.";
            }
            else row.Reason = reason + ". " + previous;
        }
        // The state before the change must still be the recorded one; a stale project keeps its refusal. A full fingerprint
        // verified earlier is compared in its digest form, so the change hashes the evidence source's digest, not its JSON.
        string expected = PanelAssignmentEvidenceStore.VerifiedDigest(file) ?? fingerprint;
        string[] prints = PanelAssignmentEvidenceStore.Fingerprints(file, recipe, file.EvidenceSource, true,
            (rows, index => before[index], expected.StartsWith(PanelAssignmentEvidenceStore.DigestPrefix, StringComparison.Ordinal)),
            (after, index => file.MyExtendedFastaItems[index].UserID, true));
        if (prints[0] != expected) return 0;
        undo?.KeepMembershipFacts(file, moved.ToDictionary(i => i, i => EvidenceApply.Clone(rows[i])));
        foreach (int i in moved) { rows[i] = after[i]; recipe.PanelProteinAssignments[i] = EvidenceApply.Clone(after[i]); }
        file.PanelProteinAssignmentsFingerprint = recipe.PanelProteinAssignmentsFingerprint = prints[1];
        return moved.Length;
    }
}
