using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
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

    public static IReadOnlyList<PanelProteinAssignment>? Current(FamilyClusterFile file)
    {
        if (CurrentShape(file) is not { } rows) return null;
        string stored = file.PanelProteinAssignmentsFingerprint!;
        Func<int, int> family = index => file.MyExtendedFastaItems[index].UserID;
        bool digest = stored.StartsWith(DigestPrefix, StringComparison.Ordinal);
        // A full check also yields the same state in digest form, which a later family change can compare instead.
        string[] prints = digest ? Fingerprints(file, file.EvidenceRecipe!, file.EvidenceSource, true, false, (rows, family, true))
            : Fingerprints(file, file.EvidenceRecipe!, file.EvidenceSource, true, false, (rows, family, false), (rows, family, true));
        // XP2: written before 2.1.19, a fingerprint also hashed the stored UniProt family world.
        if (prints[0] != stored && file.EvidenceSource?.FamilyWorld != null)
            prints[0] = Fingerprints(file, file.EvidenceRecipe!, file.EvidenceSource, true, true, (rows, family, digest))[0];
        if (prints[0] != stored) return null;
        if (!digest) verified.AddOrUpdate(file, new(stored, prints[1]));
        return rows;
    }

    /// <summary>EX2c: the digest form of a fingerprint verified in full, while the facts keep that fingerprint.</summary>
    internal static string? VerifiedDigest(FamilyClusterFile file) => verified.TryGetValue(file, out var pair)
        && pair.Full == file.PanelProteinAssignmentsFingerprint ? pair.Digest : null;

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
        return Fingerprints(file, recipe, source, projected == null, false, (rows, index => changed != null && changed.TryGetValue(index, out var assignment)
            ? assignment.FamilyId : file.MyExtendedFastaItems[index].UserID, false))[0];
    }

    /// <summary>EX2c: a family change writes facts whose fingerprint hashes a digest of the evidence source instead of its
    /// JSON, which can run to hundreds of megabytes. The digest is remembered while the source and each of its parts are the
    /// same objects: the program replaces them and never edits them in place. Protein evidence is always hashed in full.</summary>
    internal const string DigestPrefix = "2:";
    private sealed record SourceDigest(object?[] Parts, string Hash, int Length);
    private sealed record VerifiedFingerprint(string Full, string Digest);
    private static readonly System.Reflection.PropertyInfo[] sourceParts = typeof(EvidenceProvenance).GetProperties()
        .Where(part => part.Name != nameof(EvidenceProvenance.FamilyWorld)).ToArray();
    private static readonly ConditionalWeakTable<EvidenceProvenance, SourceDigest> sourceDigests = new();
    private static readonly ConditionalWeakTable<FamilyClusterFile, VerifiedFingerprint> verified = new();

    /// <summary>Fingerprints of states of one file that differ only in family numbers and fact rows, in one pass over its
    /// proteins and evidence: EX2 checks the state before a family change and records the state after it.</summary>
    // XP2 (D130): the facts depend only on what grouping reads, so the source is hashed without the stored UniProt family
    // world, which only Quantify's Database gap reads; world keeps it, to verify a fingerprint written before 2.1.19.
    internal static string[] Fingerprints(FamilyClusterFile file, EvidenceRecipe recipe, EvidenceProvenance? source, bool stored, bool world,
        params (IReadOnlyList<PanelProteinAssignment> Rows, Func<int, int> FamilyId, bool Digest)[] states)
    {
        var hashed = world ? source : EvidenceApply.CurationSource(source);
        object?[] parts = source == null ? Array.Empty<object?>() : sourceParts.Select(part => part.GetValue(source)).ToArray();
        SourceDigest? known = !world && source != null && sourceDigests.TryGetValue(source, out var cached) ? cached : null;
        string? digest = known != null && known.Parts.Zip(parts).All(pair => ReferenceEquals(pair.First, pair.Second)
            || pair.First is string or ValueType && Equals(pair.First, pair.Second)) ? known.Hash : null;
        // A remembered length spares one serialization of the source; a wrong one costs a second pass, never a wrong fingerprint.
        return Pass(known?.Length) ?? Pass(null)!;
        string[]? Pass(int? sourceLength)
        {
            var hashes = states.Select(_ => new LengthPrefixedSha256()).ToArray();
            try
            {
                void Append(string value) { foreach (var hash in hashes) hash.Append(value); }
                for (int state = 0; state < hashes.Length; state++)
                    hashes[state].Append(states[state].Digest ? "Panel assignment facts v2" : "Panel assignment facts v1");
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
                if (digest == null || states.Any(state => !state.Digest))
                {
                    // The source JSON goes once to every full-form state and to its own digest.
                    using var own = new LengthPrefixedSha256();
                    int written = LengthPrefixedSha256.AppendJson(hashes.Where((_, state) => !states[state].Digest).Append(own).ToArray(), hashed, sourceLength);
                    if (written < 0) return null;
                    digest = own.Finish();
                    if (!world && source != null) sourceDigests.AddOrUpdate(source, new(parts, digest, written));
                }
                for (int state = 0; state < hashes.Length; state++) if (states[state].Digest) hashes[state].Append(digest);
                Append(JsonSerializer.Serialize(recipe.Options));
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
                return hashes.Select((hash, state) => (states[state].Digest ? DigestPrefix : "") + hash.Finish()).ToArray();
            }
            finally { foreach (var hash in hashes) hash.Dispose(); }
        }
    }
}
