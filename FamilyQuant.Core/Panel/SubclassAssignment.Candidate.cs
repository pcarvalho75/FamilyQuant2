using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;

namespace FamilyQuant.Panels;

/// <summary>A calculated candidate, not a current stored assignment. Its inputs must be verified before publication.</summary>
public sealed class SubclassAssignmentCandidate
{
    public IReadOnlyList<ProteinSubclassLabel> Labels { get; internal init; } = Array.Empty<ProteinSubclassLabel>();
    public string PanelFingerprint { get; internal init; } = string.Empty;
    public string SourceFingerprint { get; internal init; } = string.Empty;
    internal string ContentFingerprint { get; init; } = string.Empty;
    internal bool UsesCurrentResolver { get; init; }
}

public static partial class SubclassAssignment
{
    /// <summary>Calculate a candidate from explicit applied-family stand-ins. This method does not store results
    /// or assert that the family assignments are current; the J2a provenance check must precede publication.</summary>
    public static SubclassAssignmentCandidate BuildCandidate(FamilyClusterFile file, EvidenceClassificationOptions options) =>
        BuildCandidate(file, options, null);

    public static SubclassAssignmentCandidate BuildCandidate(FamilyClusterFile file, EvidenceClassificationOptions options,
        OperationContext? operation) => BuildCandidateCore(file, options, operation, null, null);

    private static SubclassAssignmentCandidate BuildCandidateCore(FamilyClusterFile file, EvidenceClassificationOptions options,
        OperationContext? operation, ISet<int>? heldOut, PanelFamilyResolver? resolver)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);
        FamilyPanel panel = file.FamilyPanel ?? throw new InvalidOperationException("A family panel is required.");
        FamilyPanelValidation.Validate(panel);
        string panelFingerprint = PanelDerivedDataCurrentness.Fingerprint(panel);
        string familyFingerprint = PanelDerivedDataCurrentness.FamilyFingerprint(panel);
        if (file.PanelProteinAssignments == null || file.PanelProteinAssignmentsPanelFingerprint != familyFingerprint)
            throw new InvalidOperationException("Panel family assignments are missing or do not match the current panel rules.");
        if (resolver != null && !resolver.IsCurrent(file))
            throw new InvalidOperationException("The panel-family resolver is out of date.");
        string sourceBefore = SourceFingerprint(file, options, panelFingerprint);
        var proteins = file.MyExtendedFastaItems;
        var targets = Enumerable.Range(0, proteins.Count).Where(i => !NearestReviewed.IsDecoy(proteins[i])).ToArray();
        if (targets.Select(i => proteins[i].SequenceIdentifier).Distinct(StringComparer.Ordinal).Count() != targets.Length)
            throw new InvalidOperationException("Target protein identifiers must be unique.");
        var assignments = new Dictionary<int, PanelProteinAssignment>();
        foreach (var row in file.PanelProteinAssignments)
        {
            if (row == null || row.ProteinIndex < 0 || row.ProteinIndex >= proteins.Count || NearestReviewed.IsDecoy(proteins[row.ProteinIndex])
                || !assignments.TryAdd(row.ProteinIndex, row) || row.Identifier != proteins[row.ProteinIndex].SequenceIdentifier
                || row.FamilyUserId != proteins[row.ProteinIndex].UserID)
                throw new InvalidOperationException("Panel family assignments do not match current target proteins.");
        }
        if (assignments.Count != targets.Length)
            throw new InvalidOperationException("Panel family assignments do not cover all target proteins.");
        var familyMap = PanelAssignmentEvidenceStore.BuildFamilyMap(panel, file.PanelProteinAssignments);
        var conflictingIds = familyMap.ConflictingUserIds.ToHashSet();
        var families = panel.Families.ToDictionary(f => f.Key, StringComparer.Ordinal);
        var selected = new Dictionary<int, FamilyPanelFamily>();
        foreach (int index in targets)
        {
            operation?.ThrowIfCancellationRequested();
            var assignment = assignments[index];
            if (conflictingIds.Contains(proteins[index].UserID)) continue;
            if (!assignment.IsApplied || string.IsNullOrEmpty(assignment.PanelFamilyKey)) continue;
            string? key = FamilyPanelValidation.ResolveFamilyKey(panel, assignment.PanelFamilyKey);
            if (key != null) selected.Add(index, families[key]);
        }
        var labels = new Dictionary<int, ProteinSubclassLabel>();
        foreach (int index in targets)
        {
            var protein = proteins[index];
            ProteinSubclassLabel label = selected.TryGetValue(index, out var family)
                ? HasExecutableDefinitions(family) ? ClassifyDirect(family, protein.Evidence)
                    : Unknown("No subclass definitions are available for this panel family.")
                : conflictingIds.Contains(protein.UserID)
                    ? Unknown("The current User ID has conflicting panel family or outside members.")
                    : Unknown("No applied panel family is available for this target.");
            label.ProteinIndex = index;
            label.Identifier = protein.SequenceIdentifier ?? string.Empty;
            label.FamilyUserId = protein.UserID;
            label.PanelFamilyKey = family?.Key;
            labels.Add(index, label);
        }

        SubclassAssignmentCandidate Finish()
        {
            var resultLabels = targets.Select(i => labels[i]).ToArray();
            string source = SourceFingerprint(file, options, panelFingerprint);
            if (source != sourceBefore || PanelDerivedDataCurrentness.Fingerprint(panel) != panelFingerprint
                || (resolver != null && !resolver.IsCurrent(file)))
                throw new InvalidOperationException("Subclass inputs changed during computation. Rebuild the subclass candidate.");
            return new SubclassAssignmentCandidate { Labels = resultLabels, PanelFingerprint = panelFingerprint,
                SourceFingerprint = source, ContentFingerprint = LabelFingerprint(source, resultLabels), UsesCurrentResolver = resolver != null };
        }
        if (!panel.Families.Any(HasExecutableDefinitions))
        {
            foreach (var label in labels.Values)
                label.Reason = "No subclass definitions are available in this panel."
                    + (label.FamilyUserId.HasValue && conflictingIds.Contains(label.FamilyUserId.Value)
                        ? " The current User ID has conflicting panel family or outside members." : "");
            return Finish();
        }

        var byIdentifier = targets.ToDictionary(i => proteins[i].SequenceIdentifier, i => i, StringComparer.Ordinal);
        var originalReferences = NearestReviewed.References(file);
        var references = new List<NeighbourReference>();
        var scopedOriginals = new List<NeighbourReference>();
        var referenceLabels = new Dictionary<string, ProteinSubclassLabel>(StringComparer.Ordinal);
        static string ReferenceKey(string accession, string identifier) => JsonSerializer.Serialize(new[] { accession, identifier });
        foreach (var reference in originalReferences)
        {
            string encoded = string.Empty;
            ProteinSubclassLabel? referenceLabel = null;
            FamilyPanelFamily? referenceFamily = null;
            if (reference.Identifier.Length > 0 && byIdentifier.TryGetValue(reference.Identifier, out int index)
                && proteins[index].Evidence?.Reviewed == true && selected.TryGetValue(index, out var localFamily))
            {
                referenceFamily = localFamily;
                if (labels[index].Status == ProteinSubclassStatus.Assigned) referenceLabel = labels[index];
            }
            else if (reference.Identifier.Length == 0 && reference.Evidence?.Reviewed == true && resolver != null)
            {
                string? key = resolver.PanelFamilyFor(reference.Evidence);
                if (key != null && families.TryGetValue(key, out var externalFamily))
                {
                    referenceFamily = externalFamily;
                    var direct = ClassifyDirect(externalFamily, reference.Evidence);
                    if (direct.Status == ProteinSubclassStatus.Assigned)
                    {
                        direct.PanelFamilyKey = key;
                        referenceLabel = direct;
                    }
                }
            }
            if (referenceFamily == null || !HasExecutableDefinitions(referenceFamily)) continue;
            scopedOriginals.Add(reference);
            if (referenceLabel?.DefinitionKey == null) referenceLabel = null;
            if (referenceLabel != null)
            {
                // Preserve the original cross-family tie gate, and also distinguish source rules:
                // rules reporting the same subclass can impose different absent-domain constraints.
                encoded = JsonSerializer.Serialize(new[] { reference.Family, referenceLabel.PanelFamilyKey, referenceLabel.SubclassKey, referenceLabel.DefinitionKey });
                referenceLabels[ReferenceKey(reference.Accession, reference.Identifier)] = referenceLabel;
            }
            references.Add(new NeighbourReference { Accession = reference.Accession, Identifier = reference.Identifier,
                Sequence = reference.Sequence, Family = encoded, Evidence = reference.Evidence });
        }
        if (heldOut != null)
            foreach (int index in heldOut)
            {
                if (!labels.TryGetValue(index, out var original)) throw new ArgumentException("A held-out target is missing.", nameof(heldOut));
                labels[index] = new ProteinSubclassLabel { ProteinIndex = original.ProteinIndex, Identifier = original.Identifier,
                    FamilyUserId = original.FamilyUserId, PanelFamilyKey = original.PanelFamilyKey,
                    Status = ProteinSubclassStatus.Unknown, Reason = "The target's subclass evidence is held out." };
            }
        var queryIndices = targets.Where(i => selected.TryGetValue(i, out var queryFamily) && HasExecutableDefinitions(queryFamily)
            && (heldOut != null ? heldOut.Contains(i) : proteins[i].Evidence?.Reviewed != true)
            && labels[i].Status == ProteinSubclassStatus.Unknown && labels[i].Reason != ConflictReason
            && !string.IsNullOrEmpty(proteins[i].Sequence)).ToArray();
        if (queryIndices.Length > 0 && referenceLabels.Count == 0)
        {
            foreach (int index in queryIndices) labels[index].Reason = "No reviewed subclass reference is available for this panel.";
        }
        else if (queryIndices.Length > 0)
        {
            var queries = queryIndices.Select(i => proteins[i]).ToList();
            var search = NearestReviewed.Search(queries, references,
                Math.Min(options.AnnotatedIdentityPercent, options.UnannotatedIdentityPercent), options.MinQueryAlignedPercent,
                operation: operation);
            var originals = scopedOriginals.GroupBy(r => ReferenceKey(r.Accession, r.Identifier), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var hits = new Dictionary<string, NearestNeighbour>(StringComparer.Ordinal);
            foreach (var hit in search.Neighbours)
            {
                if (!originals.TryGetValue(ReferenceKey(hit.Accession, hit.Reference), out var original) || original.Evidence?.Reviewed != true) continue;
                hits.Add(hit.Query, new NearestNeighbour { Query = hit.Query, Reference = hit.Reference, Accession = hit.Accession,
                    Family = original.Evidence.Family, IdentityPercent = hit.IdentityPercent, AlignmentLength = hit.AlignmentLength,
                    QueryLength = hit.QueryLength, ReferenceLength = hit.ReferenceLength, QueryAlignedResidues = hit.QueryAlignedResidues,
                    TiedFamilies = hit.TiedFamilies.ToList() });
            }
            var masked = targets.Where(i => proteins[i].Evidence?.Reviewed != true).ToHashSet();
            var evidenceOf = NearestReviewed.ReferenceEvidence(file);
            var sharedProposal = heldOut == null ? EvidenceClassifier.Propose(proteins, options, hits, evidenceOf, masked) : null;
            var sharedRows = sharedProposal?.Proteins.ToDictionary(p => p.ProteinIndex);
            foreach (int index in queryIndices)
            {
                operation?.ThrowIfCancellationRequested();
                EvidenceProteinProposal row;
                if (heldOut != null)
                {
                    var oneMasked = new HashSet<int>(masked) { index };
                    var proposal = EvidenceClassifier.Propose(proteins, options, hits, evidenceOf, oneMasked);
                    row = proposal.Proteins.Single(p => p.ProteinIndex == index);
                }
                else row = sharedRows![index];
                if ((row.Tier != EvidenceTier.ReviewedNeighbour && row.Tier != EvidenceTier.UnannotatedNeighbour)
                    || row.Neighbour == null || row.Neighbour.TiedFamilies.Count > 0
                    || (proteins[index].Evidence?.Accession is { Length: > 0 } accession
                        && string.Equals(NearestReviewed.EntryAccession(accession), NearestReviewed.EntryAccession(row.Neighbour.Accession), StringComparison.Ordinal))
                    || !referenceLabels.TryGetValue(ReferenceKey(row.Neighbour.Accession, row.Neighbour.Reference), out var reference)
                    || reference.PanelFamilyKey != labels[index].PanelFamilyKey)
                {
                    labels[index].Reason = "No unambiguous qualifying reviewed subclass supports this target. " + row.Reason;
                    continue;
                }
                FamilyPanelSubclass? rule = selected[index].Subclasses.FirstOrDefault(s => s.Key == reference.DefinitionKey);
                var targetEvidence = proteins[index].Evidence;
                if (rule == null || (rule.AbsentPfam.Count > 0 && targetEvidence?.Fragment != false)
                    || (targetEvidence?.Pfam ?? new List<EvidenceDomain>()).Any(d => rule.AbsentPfam.Contains(d.Accession, StringComparer.Ordinal)))
                {
                    labels[index].Reason = "The target's available evidence does not support the reviewed reference's subclass rule.";
                    continue;
                }
                labels[index].Status = ProteinSubclassStatus.Assigned;
                labels[index].SubclassKey = reference.SubclassKey;
                labels[index].DefinitionKey = reference.DefinitionKey;
                labels[index].Reason = "Subclass carried from reviewed reference " + row.Neighbour.Accession + ". " + row.Reason;
            }
        }
        return Finish();
    }

    private static bool HasExecutableDefinitions(FamilyPanelFamily family) => family.Subclasses.Any(s =>
        s.RequiredPfam.Count > 0 || s.AbsentPfam.Count > 0 || s.UniProtSubfamilies.Any(t => !string.IsNullOrWhiteSpace(t)));

    private static string SourceFingerprint(FamilyClusterFile file, EvidenceClassificationOptions options, string panelFingerprint) =>
        Hash(JsonSerializer.Serialize(new { Algorithm = "Panel subclass assignment v1", NearestReviewed.Method, NearestReviewed.QueryCoverageMethod, NearestReviewed.ReferenceTieMethod,
            Panel = panelFingerprint, Proteins = EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems),
            file.PanelProteinAssignments, file.PanelProteinAssignmentsFingerprint, Options = options,
            ReferencePanel = file.EvidenceSource?.ReferencePanel }));

    private static string LabelFingerprint(string source, IReadOnlyList<ProteinSubclassLabel> labels) =>
        Hash(JsonSerializer.Serialize(new { Source = source, Labels = labels }));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
