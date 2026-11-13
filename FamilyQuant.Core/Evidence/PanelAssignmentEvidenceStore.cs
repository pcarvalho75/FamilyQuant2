using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Evidence;

/// <summary>Assignment facts are available only while their target membership, panel rules,
/// evidence provenance and recipe still match. Text/display edits do not change family membership.</summary>
public static class PanelAssignmentEvidenceStore
{
    public const string RebuildMessage = "Family assignment evidence is missing or out of date. Open Curate families > Group by evidence, choose Update proposal, then Apply grouping; if locks block the proposal, use Unlock evidence families there.";
    public static IReadOnlyList<PanelProteinAssignment> RequireCurrent(FamilyClusterFile file) => Current(file)
        ?? throw new InvalidOperationException(HasMissingSequence(file) ? "A target has no stored sequence. Restore its sequence before assigning families."
            : RebuildMessage);

    private static bool HasMissingSequence(FamilyClusterFile file) => file.MyExtendedFastaItems
        .Any(p => !NearestReviewed.IsDecoy(p) && string.IsNullOrEmpty(p.Sequence));

    public static PanelFamilyMap GetCurrentFamilyMap(FamilyClusterFile file)
    {
        var current = Current(file);
        if (current == null)
        {
            // This is a diagnostic about stored labels, not permission to use stale assignments.
            int[] conflicts = (file.PanelProteinAssignments ?? new()).Where(r => r.IsApplied && r.PanelFamilyKey != null
                    && r.ProteinIndex >= 0 && r.ProteinIndex < file.MyExtendedFastaItems.Count
                    && r.Identifier == file.MyExtendedFastaItems[r.ProteinIndex].SequenceIdentifier)
                .GroupBy(r => file.MyExtendedFastaItems[r.ProteinIndex].UserID)
                .Where(g => g.Select(r => r.PanelFamilyKey).Distinct(StringComparer.Ordinal).Count() > 1)
                .Select(g => g.Key).Order().ToArray();
            return new() { Message = HasMissingSequence(file) ? "A target has no stored sequence. Restore its sequence before assigning families."
                : RebuildMessage, ConflictingUserIds = conflicts };
        }
        return BuildFamilyMap(file.FamilyPanel!, current);
    }

    /// <summary>Pure mapping of supplied current facts, also usable with a stand-in. Production
    /// consumers use GetCurrentFamilyMap, which verifies source/provenance first.</summary>
    public static PanelFamilyMap BuildFamilyMap(FamilyPanel panel, IReadOnlyList<PanelProteinAssignment> assignments)
    {
        FamilyPanelValidation.Validate(panel);
        var byId = assignments.Where(r => r.FamilyUserId.HasValue).GroupBy(r => r.FamilyUserId!.Value).ToDictionary(g => g.Key, g =>
        {
            // Excluded keys still carry a report decision. Only PanelRows' measured membership
            // drops them; erasing them here would turn an explicit exclusion into an unlinked row.
            string[] keys = g.Where(r => r.IsApplied && r.PanelFamilyKey != null)
                .Select(r => FamilyPanelValidation.ResolveFamilyKey(panel, r.PanelFamilyKey!) ?? r.PanelFamilyKey!)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            bool outside = g.Any(r => !r.IsApplied || r.PanelFamilyKey == null);
            bool conflict = keys.Length > 1 || keys.Length > 0 && outside;
            string? key = keys.Length == 1 && !conflict ? keys[0] : null;
            return new PanelFamilyUserMap { FamilyUserId = g.Key, PanelFamilyKey = key,
                Decision = key == null ? null : panel.Families.Single(f => f.Key == key).Decision,
                PanelKeys = keys, HasOutsideMembers = outside, HasConflict = conflict };
        });
        int[] conflicts = byId.Values.Where(g => g.HasConflict).Select(g => g.FamilyUserId).Order().ToArray();
        return new()
        {
            IsCurrent = true, UserFamilies = byId, ConflictingUserIds = conflicts,
            Message = conflicts.Length == 0 ? "Panel family mapping is current." : "A current family contains different panel keys or outside members. Review its grouping.",
            PanelRows = panel.Families.Select(f =>
            {
                string? effective = FamilyPanelValidation.ResolveFamilyKey(panel, f.Key);
                return new PanelFamilyRowMap { PanelRowKey = f.Key, EffectivePanelKey = effective, Decision = f.Decision,
                    MergeTargetFamilyKey = f.MergeTargetFamilyKey,
                    FamilyUserIds = effective == null ? Array.Empty<int>() : byId.Values.Where(g => g.PanelFamilyKey == effective && !g.HasConflict)
                        .Select(g => g.FamilyUserId).Order().ToArray() };
            }).ToArray(),
        };
    }

    /// <summary>EX2: call before a family change made in the program after Apply grouping. Null when the facts are not
    /// current, so a project whose database, list or evidence changed keeps its refusal.</summary>
    public static PanelMembershipEdit? BeginMembershipEdit(FamilyClusterFile file) => CurrentShape(file) == null ? null
        : new(file, file.MyExtendedFastaItems.Select(p => p.UserID).ToArray(), file.PanelProteinAssignmentsFingerprint!);

    public static IReadOnlyList<PanelProteinAssignment>? Current(FamilyClusterFile file) => CurrentShape(file) is { } rows
        && Fingerprint(file, rows, file.EvidenceRecipe!, file.EvidenceSource, null) == file.PanelProteinAssignmentsFingerprint ? rows : null;

    // Every check of Current but the fingerprint, which EX2 recomputes in the pass that records the changed state.
    private static List<PanelProteinAssignment>? CurrentShape(FamilyClusterFile file)
    {
        if (HasMissingSequence(file)) return null;
        var rows = file.PanelProteinAssignments;
        var recipe = file.EvidenceRecipe;
        if (rows == null || file.FamilyPanel == null || recipe?.PanelProteinAssignments == null
            || file.PanelProteinAssignmentsFingerprint == null
            || recipe.PanelProteinAssignmentsFingerprint != file.PanelProteinAssignmentsFingerprint) return null;
        if (PanelDerivedDataCurrentness.Check(file).FamilyAssignments != PanelDerivedDataStatus.Current) return null;
        var targets = file.MyExtendedFastaItems.Select((p, i) => (Protein: p, Index: i)).Where(r => !NearestReviewed.IsDecoy(r.Protein)).ToArray();
        if (rows.Count != targets.Length || rows.Select(r => r.ProteinIndex).Distinct().Count() != rows.Count
            || recipe.PanelProteinAssignments.Count != rows.Count) return null;
        var byIndex = rows.ToDictionary(r => r.ProteinIndex);
        foreach (var target in targets)
            if (!byIndex.TryGetValue(target.Index, out var row) || row.Identifier != target.Protein.SequenceIdentifier
                || row.FamilyUserId != target.Protein.UserID || row.Reason.Length == 0
                || row.IsApplied != (row.PanelFamilyKey != null)) return null;
        for (int index = 0; index < rows.Count; index++)
            if (JsonSerializer.Serialize(rows[index]) != JsonSerializer.Serialize(recipe.PanelProteinAssignments[index])) return null;
        return rows;
    }

    internal static List<PanelProteinAssignment> Stage(EvidenceProposal proposal, IReadOnlyList<EvidenceAssignment> applied)
    {
        var rows = EvidenceApply.Clone(proposal.PanelAssignments?.ToList()
            ?? throw new ArgumentException("The panel proposal has no per-target evidence facts.", nameof(proposal)));
        var byIndex = applied.ToDictionary(a => a.ProteinIndex);
        foreach (var row in rows)
        {
            if (byIndex.TryGetValue(row.ProteinIndex, out var selected))
            {
                row.PanelFamilyKey = selected.Family; row.FamilyUserId = selected.FamilyId;
                row.IsApplied = true; row.Reason = selected.Reason;
                if (selected.Tier == EvidenceTier.AddedByHand) row.EvidenceKind = PanelAssignmentEvidenceKind.AddedByHand;
            }
            else
            {
                if (row.PanelFamilyKey != null) row.Reason += " Proposed panel key " + row.PanelFamilyKey + " was not selected for application.";
                row.PanelFamilyKey = null; row.IsApplied = false;
            }
        }
        return rows;
    }

    internal static string Fingerprint(FamilyClusterFile file, IReadOnlyList<PanelProteinAssignment> rows,
        EvidenceRecipe recipe, EvidenceProvenance? source, IReadOnlyList<EvidenceAssignment>? projected)
    {
        var changed = projected?.ToDictionary(a => a.ProteinIndex);
        return Fingerprints(file, recipe, source, projected == null, (rows, index => changed != null && changed.TryGetValue(index, out var assignment)
            ? assignment.FamilyId : file.MyExtendedFastaItems[index].UserID))[0];
    }

    /// <summary>Fingerprints of states of one file that differ only in family numbers and fact rows, in one pass over its
    /// proteins and evidence: EX2 checks the state before a family change and records the state after it.</summary>
    internal static string[] Fingerprints(FamilyClusterFile file, EvidenceRecipe recipe, EvidenceProvenance? source, bool stored,
        params (IReadOnlyList<PanelProteinAssignment> Rows, Func<int, int> FamilyId)[] states)
    {
        var hashes = states.Select(_ => new LengthPrefixedSha256()).ToArray();
        try
        {
            void Append(string value) { foreach (var hash in hashes) hash.Append(value); }
            Append("Panel assignment facts v1");
            Append(file.SourceSha256); Append(file.SourceSequenceCount.ToString(CultureInfo.InvariantCulture));
            Append(recipe.FamilyPanel == null ? "" : PanelDerivedDataCurrentness.FamilyFingerprint(recipe.FamilyPanel));
            Append(JsonSerializer.Serialize(recipe.FamilyPanel?.EvidenceLinks));
            // On a stored file, use the current panel's definitions/links, not only its recipe snapshot.
            var currentPanel = stored ? file.FamilyPanel : recipe.FamilyPanel;
            Append(currentPanel == null ? "" : PanelDerivedDataCurrentness.FamilyFingerprint(currentPanel));
            Append(JsonSerializer.Serialize(currentPanel?.EvidenceLinks));
            for (int index = 0; index < file.MyExtendedFastaItems.Count; index++)
            {
                var protein = file.MyExtendedFastaItems[index];
                if (NearestReviewed.IsDecoy(protein)) continue;
                Append(index.ToString(CultureInfo.InvariantCulture)); Append(protein.SequenceIdentifier ?? ""); Append(protein.Sequence ?? "");
                for (int state = 0; state < hashes.Length; state++) hashes[state].Append(states[state].FamilyId(index).ToString(CultureInfo.InvariantCulture));
                Append(JsonSerializer.Serialize(protein.Evidence));
            }
            LengthPrefixedSha256.AppendJson(hashes, source); Append(JsonSerializer.Serialize(recipe.Options));
            Append(JsonSerializer.Serialize(recipe.PanelContext)); Append(JsonSerializer.Serialize(recipe.Assignments));
            if (recipe.RetainedPanelAssignments != null) Append(JsonSerializer.Serialize(recipe.RetainedPanelAssignments));
            for (int position = 0; position < states[0].Rows.Count; position++)
            {
                // A row the states share is serialized once.
                string first = JsonSerializer.Serialize(states[0].Rows[position]);
                hashes[0].Append(first);
                for (int state = 1; state < hashes.Length; state++)
                    hashes[state].Append(ReferenceEquals(states[state].Rows[position], states[0].Rows[position]) ? first : JsonSerializer.Serialize(states[state].Rows[position]));
            }
            return hashes.Select(hash => hash.Finish()).ToArray();
        }
        finally { foreach (var hash in hashes) hash.Dispose(); }
    }
}
