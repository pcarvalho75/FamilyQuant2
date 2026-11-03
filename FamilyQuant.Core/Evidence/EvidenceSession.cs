using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Evidence
{
    public static partial class EvidenceApply
    {
        private sealed record PreparedSession(NeighbourSearch? Search, List<NeighbourReference>? References,
            List<MotifPattern>? Motifs, List<MotifResult>? Results);

        private static PreparedSession PrepareSession(FamilyClusterFile file, EvidenceProposal proposal, EvidenceApplySelection selection,
            Operations.OperationContext? operation)
        {
            List<NeighbourReference>? references = ValidateSearch(file, selection.SessionSearch, selection.SessionReferences, proposal.Options, operation,
                allowPanelReferences: proposal.PanelContext != null);
            if (selection.SessionSearch != null)
            {
                var byQuery = selection.SessionSearch.ByQuery();
                foreach (EvidenceProteinProposal row in proposal.Proteins)
                {
                    operation?.ThrowIfCancellationRequested();
                    byQuery.TryGetValue(row.Identifier, out NearestNeighbour? found);
                    if (JsonSerializer.Serialize(found) != JsonSerializer.Serialize(row.Neighbour))
                        throw new InvalidOperationException("The proposal does not use this session's reviewed neighbours.");
                }
            }
            List<MotifPattern>? motifs = selection.CheckedMotifs?.ToList();
            if (motifs?.Any(m => m == null) == true) throw new ArgumentException("A checked motif is missing.", nameof(selection));
            IEnumerable<(string Family, FastaItemExtended Protein)> Members()
            {
                foreach (EvidenceProteinProposal row in proposal.Proteins.Where(p => p.Family.Length > 0))
                {
                    operation?.ThrowIfCancellationRequested();
                    yield return (row.Family, file.MyExtendedFastaItems[row.ProteinIndex]);
                }
            }
            List<MotifResult>? results = motifs?.Select(m => MotifCheck.Count(m, Members())).ToList();
            return new PreparedSession(selection.SessionSearch, references, motifs, results);
        }

        internal static List<NeighbourReference>? ValidateSearch(FamilyClusterFile file, NeighbourSearch? search,
            IReadOnlyList<NeighbourReference>? suppliedReferences, EvidenceClassificationOptions options, Operations.OperationContext? operation,
            bool allowLegacyCoverage = false, bool allowPanelReferences = false)
        {
            if (search == null)
            {
                if (suppliedReferences != null) throw new ArgumentException("Session references require a session search.");
                return null;
            }
            List<FastaItemExtended> queries = NearestReviewed.Targets(file).Where(p => !string.IsNullOrEmpty(p.Sequence)).ToList();
            List<NeighbourReference> current = NearestReviewed.References(file);
            List<NeighbourReference>? panel = allowPanelReferences ? PanelEvidenceClassifier.References(file, options) : null;
            List<NeighbourReference>? references = suppliedReferences?.ToList();
            if (references == null)
            {
                var candidates = new List<List<NeighbourReference>>
                {
                    current,
                    NearestReviewed.References(file, e => EvidenceClassifier.IsInScope(e, options.ScopeTerms)),
                    NearestReviewed.References(file, e => e.FamilyAnnotation.Length > 0 && EvidenceClassifier.IsInScope(e, options.ScopeTerms)),
                };
                if (panel != null) candidates.Add(panel);
                references = candidates.FirstOrDefault(r => NearestReviewed.IsCurrent(search, queries, r, allowLegacyCoverage: allowLegacyCoverage));
            }
            var known = current.Select(r => Hash(r)).ToHashSet(StringComparer.Ordinal);
            var panelKnown = panel?.Select(r => Hash(r)).ToHashSet(StringComparer.Ordinal);
            bool fromCurrent = references != null && references.All(r => r != null && known.Contains(Hash(r)));
            bool fromPanel = references != null && panelKnown != null && references.All(r => r != null && panelKnown.Contains(Hash(r)));
            if (references == null || !(fromCurrent || fromPanel)
                || references.Select(r => Hash(r)).Distinct(StringComparer.Ordinal).Count() != references.Count
                || !NearestReviewed.IsCurrent(search, queries, references, allowLegacyCoverage: allowLegacyCoverage) || search.Queries != queries.Count || search.References != references.Count)
                throw new InvalidOperationException("The session search no longer matches the current queries and reviewed references.");
            if (!double.IsFinite(search.MinIdentityPercent) || search.MinIdentityPercent <= 0 || search.MinIdentityPercent > 100
                || search.MinIdentityPercent > Math.Min(options.AnnotatedIdentityPercent, options.UnannotatedIdentityPercent)
                || search.MinQueryAlignedPercent < 0 || search.MinQueryAlignedPercent > options.MinQueryAlignedPercent)
                throw new InvalidOperationException("The session search omits alignments needed by the proposal's thresholds.");
            var queryById = queries.ToDictionary(q => q.SequenceIdentifier, StringComparer.Ordinal);
            var referenceByKey = references.GroupBy(r => ReferenceKey(r.Accession, r.Identifier, r.Family), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var families = references.Select(r => r.Family).ToHashSet(StringComparer.Ordinal);
            bool legacyCoverage = allowLegacyCoverage && search.CoverageMethod == null;
            foreach (NearestNeighbour neighbour in search.Neighbours)
            {
                operation?.ThrowIfCancellationRequested();
                if (!seen.Add(neighbour.Query) || !queryById.TryGetValue(neighbour.Query, out FastaItemExtended? query)
                    || !referenceByKey.TryGetValue(ReferenceKey(neighbour.Accession, neighbour.Reference, neighbour.Family), out var matches)
                    || neighbour.Accession == NearestReviewed.EntryAccession(query.SequenceIdentifier)
                    || neighbour.QueryLength != query.Sequence.Length || !matches.Any(r => r.Sequence.Length == neighbour.ReferenceLength)
                    || !double.IsFinite(neighbour.IdentityPercent) || neighbour.IdentityPercent > 100 || neighbour.AlignmentLength <= 0
                    || !(legacyCoverage
                        ? NearestReviewed.PassesLegacyColumnsGate(neighbour.IdentityPercent, neighbour.AlignmentLength, neighbour.QueryLength,
                            search.MinIdentityPercent, search.MinQueryAlignedPercent)
                        : neighbour.QueryAlignedResidues is int covered && NearestReviewed.PassesGate(neighbour.IdentityPercent, covered, neighbour.QueryLength,
                            search.MinIdentityPercent, search.MinQueryAlignedPercent)) || neighbour.TiedFamilies.Any(f => !families.Contains(f)))
                    throw new InvalidOperationException("A session neighbour is inconsistent with its current query, reference or search gate.");
                if (search.TieMethod == NearestReviewed.ReferenceTieMethod)
                {
                    var alternatives = neighbour.TiedReferences ?? new List<NearestNeighbourTie>();
                    var keys = new HashSet<string>(StringComparer.Ordinal) { ReferenceKey(neighbour.Accession, neighbour.Reference, neighbour.Family) };
                    foreach (var alternative in alternatives)
                        if (!keys.Add(ReferenceKey(alternative.Accession, alternative.Reference, alternative.Family))
                            || !referenceByKey.TryGetValue(ReferenceKey(alternative.Accession, alternative.Reference, alternative.Family), out var tiedMatches)
                            || alternative.Accession == NearestReviewed.EntryAccession(query.SequenceIdentifier)
                            || alternative.AlignmentLength <= 0 || !tiedMatches.Any(r => r.Sequence.Length == alternative.ReferenceLength)
                            || alternative.QueryAlignedResidues is not int tiedCovered
                            || !NearestReviewed.PassesGate(neighbour.IdentityPercent, tiedCovered, neighbour.QueryLength,
                                search.MinIdentityPercent, search.MinQueryAlignedPercent))
                            throw new InvalidOperationException("An equally close reference is inconsistent with the search evidence.");
                    string[] expectedTies = alternatives.Select(a => a.Family).Where(f => f != neighbour.Family)
                        .Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToArray();
                    if (!expectedTies.SequenceEqual(neighbour.TiedFamilies))
                        throw new InvalidOperationException("The tied source-family labels do not match the equally close references.");
                }
            }
            return references;
        }

        private static string ReferenceKey(string accession, string identifier, string family) => JsonSerializer.Serialize(new[] { accession, identifier, family });

        private static EvidenceProvenance? SessionSource(FamilyClusterFile file, NeighbourSearch? search)
        {
            if (search == null) return file.EvidenceSource;
            EvidenceProvenance source = file.EvidenceSource == null ? new EvidenceProvenance() : Clone(file.EvidenceSource);
            source.NearestReviewed = Clone(search);
            return source;
        }
    }
}
