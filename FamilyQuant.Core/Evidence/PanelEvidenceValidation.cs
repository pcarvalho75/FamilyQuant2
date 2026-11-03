using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FamilyQuant.Evidence;

public enum PanelLeaveOneOutOutcome { Right, Wrong, Abstained, Unscorable }
public sealed class PanelLeaveOneOutRow
{
    public int ProteinIndex { get; init; }
    public string Identifier { get; init; } = "";
    public IReadOnlyList<string> SourceFamilies { get; init; } = Array.Empty<string>();
    public bool ExpectedKnown { get; init; }
    public string? ExpectedPanelKey { get; init; }
    public string? PredictedPanelKey { get; init; }
    public string ExpectedReason { get; init; } = "";
    public string Reason { get; init; } = "";
    public string? NeighbourAccession { get; init; }
    public double? IdentityPercent { get; init; }
    public int TrainingReferences { get; init; }
    public PanelLeaveOneOutOutcome Outcome { get; init; }
}

public sealed class PanelLeaveOneOutResult
{
    public int ReviewedProteins { get; init; }
    public IReadOnlyList<PanelLeaveOneOutRow> Rows { get; init; } = Array.Empty<PanelLeaveOneOutRow>();
    public int ReviewedEntries => Rows.Count;
    public int Right => Rows.Count(r => r.Outcome == PanelLeaveOneOutOutcome.Right);
    public int Wrong => Rows.Count(r => r.Outcome == PanelLeaveOneOutOutcome.Wrong);
    public int Abstained => Rows.Count(r => r.Outcome == PanelLeaveOneOutOutcome.Abstained);
    public int Unscorable => Rows.Count(r => r.Outcome == PanelLeaveOneOutOutcome.Unscorable);
    public int Scorable => ReviewedEntries - Unscorable;
    public double? RightRate => Scorable == 0 ? null : (double)Right / Scorable;
    public double? RightOverAllReviewed => ReviewedEntries == 0 ? null : (double)Right / ReviewedEntries;
}

public sealed class PanelDraftDifference
{
    public string Identifier { get; init; } = "";
    public string DraftLabel { get; init; } = "";
    public bool DraftMappingKnown { get; init; }
    public string? DraftPanelKey { get; init; }
    public string? CurrentPanelKey { get; init; }
    public string Kind { get; init; } = "";
    public string Reason { get; init; } = "";
}

/// <summary>Measures the actual panel rule. Expectations are source annotations interpreted through
/// the panel, not independent biological truth. Every fold masks its source label and removes its
/// entry/isoforms from learned reference support; abstentions and unknown expectations stay visible.</summary>
public static class PanelEvidenceValidation
{
    public static PanelLeaveOneOutResult LeaveOneOut(FamilyClusterFile file, EvidenceClassificationOptions options,
        NeighbourSearch search, Action<int, int>? progress = null)
    {
        var panel = file.FamilyPanel ?? throw new InvalidOperationException("A panel is required.");
        var references = PanelEvidenceClassifier.References(file, options);
        if (search.TieMethod != NearestReviewed.ReferenceTieMethod || search.MinQueryAlignedPercent != options.MinQueryAlignedPercent)
            throw new InvalidOperationException("Run a current tie-aware search at the selected coverage gate.");
        EvidenceApply.ValidateSearch(file, search, references, options, null, allowPanelReferences: true);
        var full = new PanelEvidenceClassifier.ClassificationContext(panel, PanelEvidenceClassifier.BuildLinks(panel, references, options));
        var reviewed = file.MyExtendedFastaItems.Select((p, i) => (Protein: p, Index: i))
            .Where(r => !NearestReviewed.IsDecoy(r.Protein) && !SequenceLabels.IsContaminant(r.Protein.SequenceIdentifier)
                && r.Protein.Evidence?.Reviewed == true && EvidenceClassifier.IsInScope(r.Protein.Evidence, options.ScopeTerms)).ToArray();
        var entries = reviewed.GroupBy(r => Entry(r.Protein), StringComparer.Ordinal).ToArray();
        var neighbours = search.ByQuery();
        var rows = new List<PanelLeaveOneOutRow>();
        for (int position = 0; position < entries.Length; position++)
        {
            var query = entries[position].First();
            var own = entries[position].SelectMany(r => new[] { NearestReviewed.EntryAccession(r.Protein.SequenceIdentifier),
                    NearestReviewed.EntryAccession(r.Protein.Evidence?.Accession) }).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
            bool IsOwn(NeighbourReference r) => own.Contains(NearestReviewed.EntryAccession(r.Accession))
                || own.Contains(NearestReviewed.EntryAccession(r.Identifier))
                || r.Evidence?.Accession.Length > 0 && own.Contains(NearestReviewed.EntryAccession(r.Evidence.Accession));
            var training = references.Where(r => !IsOwn(r)).ToArray();
            var fold = new PanelEvidenceClassifier.ClassificationContext(panel, PanelEvidenceClassifier.BuildLinks(panel, training, options));
            neighbours.TryGetValue(query.Protein.SequenceIdentifier, out var neighbour);
            var prediction = PanelEvidenceClassifier.Classify(query.Protein, query.Index, options, fold, neighbour, training,
                searched: true, maskSourceLabel: true);
            var expected = full.Resolve(query.Protein.Evidence);
            var families = PanelEvidenceClassifier.SourceFamilies(query.Protein.Evidence);
            bool known = families.Length > 0 && (expected.Family != null || expected.Code.Length == 0);
            string? predicted = prediction.Family.Length == 0 ? null : prediction.Family;
            rows.Add(new PanelLeaveOneOutRow
            {
                ProteinIndex = query.Index, Identifier = query.Protein.SequenceIdentifier, SourceFamilies = families,
                ExpectedKnown = known, ExpectedPanelKey = expected.Family, PredictedPanelKey = predicted,
                ExpectedReason = known ? expected.Reason : "The source annotation has no resolved panel expectation. " + expected.Reason,
                Reason = prediction.Reason, NeighbourAccession = neighbour?.Accession, IdentityPercent = neighbour?.IdentityPercent,
                TrainingReferences = training.Length,
                Outcome = !known ? PanelLeaveOneOutOutcome.Unscorable : predicted == expected.Family ? PanelLeaveOneOutOutcome.Right
                    : predicted == null ? PanelLeaveOneOutOutcome.Abstained : PanelLeaveOneOutOutcome.Wrong,
            });
            progress?.Invoke(position + 1, entries.Length);
        }
        return new() { ReviewedProteins = reviewed.Length, Rows = rows };
    }

    private static string Entry(FastaItemExtended protein) => NearestReviewed.EntryAccession(
        string.IsNullOrEmpty(protein.Evidence?.Accession) ? protein.SequenceIdentifier : protein.Evidence.Accession);

    public static IReadOnlyList<PanelDraftDifference> CompareDraft(FamilyClusterFile file, EvidenceProposal proposal,
        FamilyClusterFile draft, IReadOnlyDictionary<string, string?>? explicitMap = null)
    {
        if (EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems) != proposal.SourceFingerprint)
            throw new InvalidOperationException("The proposal is not the current program output.");
        var panel = file.FamilyPanel ?? throw new InvalidOperationException("A panel is required.");
        var prior = draft.MyExtendedFastaItems.Where(p => !NearestReviewed.IsDecoy(p)).ToArray();
        if (prior.Select(p => p.SequenceIdentifier).Distinct(StringComparer.Ordinal).Count() != prior.Length)
            throw new InvalidOperationException("Draft target identifiers are not unique.");
        var byId = prior.ToDictionary(p => p.SequenceIdentifier, StringComparer.Ordinal);
        var groupNames = prior.GroupBy(p => p.UserID).ToDictionary(g => g.Key, g => g.Select(p => p.UserAnnotation ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray());
        string Label(FastaItemExtended p) => !string.IsNullOrWhiteSpace(p.UserAnnotation) ? p.UserAnnotation.Trim()
            : groupNames[p.UserID].Length == 1 ? groupNames[p.UserID][0].Trim() : "";
        (bool Known, string? Key) Map(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return (true, null);
            if (explicitMap != null && explicitMap.TryGetValue(label, out string? supplied))
                return (true, supplied == null ? null : FamilyPanelValidation.ResolveFamilyKey(panel, supplied));
            var keys = panel.Families.Where(f => string.Equals(f.Key, label, StringComparison.Ordinal)
                    || string.Equals(f.DisplayName, label, StringComparison.OrdinalIgnoreCase)
                    || f.Names.Any(n => string.Equals(n.Text, label, StringComparison.OrdinalIgnoreCase)))
                .Select(f => FamilyPanelValidation.ResolveFamilyKey(panel, f.Key)).Distinct(StringComparer.Ordinal).ToArray();
            return keys.Length == 1 ? (true, keys[0]) : (false, null);
        }
        var differences = new List<PanelDraftDifference>();
        foreach (var row in proposal.Proteins)
        {
            bool existed = byId.TryGetValue(row.Identifier, out var old);
            string label = existed ? Label(old!) : "";
            var mapped = existed ? Map(label) : (Known: true, Key: (string?)null);
            string? current = row.Family.Length == 0 ? null : row.Family;
            if (mapped.Known && mapped.Key == current) continue;
            differences.Add(new()
            {
                Identifier = row.Identifier, DraftLabel = label, DraftMappingKnown = mapped.Known,
                DraftPanelKey = mapped.Key, CurrentPanelKey = current,
                Kind = !mapped.Known ? "Draft label unresolved" : mapped.Key == null ? "Added" : current == null ? "Removed" : "Changed",
                Reason = (!mapped.Known ? "The draft label has no unique declared panel mapping. " : "") + row.Reason,
            });
        }
        var currentIds = proposal.Proteins.Select(p => p.Identifier).ToHashSet(StringComparer.Ordinal);
        foreach (var old in prior.Where(p => !currentIds.Contains(p.SequenceIdentifier)))
        {
            string label = Label(old); var mapped = Map(label);
            differences.Add(new() { Identifier = old.SequenceIdentifier, DraftLabel = label, DraftMappingKnown = mapped.Known,
                DraftPanelKey = mapped.Key, Kind = "Absent from current source", Reason = "The draft entry is absent from the verified current source database." });
        }
        return differences.OrderBy(r => r.Identifier, StringComparer.Ordinal).ToArray();
    }

    public static string BuildLeaveOneOutCsv(PanelLeaveOneOutResult result)
    {
        var csv = new StringBuilder("Identifier,Source families,Expected known,Expected panel key,Predicted panel key,Outcome,Neighbour,Identity percent,Training references,Expected reason,Reason\r\n");
        foreach (var r in result.Rows)
            csv.Append(string.Join(",", new[] { r.Identifier, string.Join("; ", r.SourceFamilies), r.ExpectedKnown ? "yes" : "no", r.ExpectedPanelKey,
                r.PredictedPanelKey, r.Outcome.ToString(), r.NeighbourAccession, r.IdentityPercent?.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                r.TrainingReferences.ToString(System.Globalization.CultureInfo.InvariantCulture), r.ExpectedReason, r.Reason }.Select(CsvFormatting.Guarded))).Append("\r\n");
        return csv.ToString();
    }

    public static string BuildDraftCsv(IReadOnlyList<PanelDraftDifference> rows)
    {
        var csv = new StringBuilder("Identifier,Draft label,Draft mapping known,Draft panel key,Current panel key,Kind,Reason\r\n");
        foreach (var r in rows) csv.Append(string.Join(",", new[] { r.Identifier, r.DraftLabel, r.DraftMappingKnown ? "yes" : "no", r.DraftPanelKey,
            r.CurrentPanelKey, r.Kind, r.Reason }.Select(CsvFormatting.Guarded))).Append("\r\n");
        return csv.ToString();
    }
}
