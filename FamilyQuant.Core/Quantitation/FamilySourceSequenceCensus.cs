using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    public static partial class FamilyEnvelopeQuant
    {
        private static FamilySourceSequenceSummary BuildSourceSequenceSummary(
            IReadOnlyList<QuantRow> rows,
            Homology.HomologyDatabase database,
            List<FastaItemExtended> targets,
            Homology.HomologyOptions options,
            Homology.HomologyOptions identifiedOptions,
            IReadOnlyDictionary<int, string> descriptions, OperationContext? operation)
        {
            static string Clean(string value) =>
                DeNovoReconciler.NormalizeSequenceIgnoringPtm(value ?? string.Empty,
                    foldIsoleucineLeucine: false);

            var search = new HashSet<string>(StringComparer.Ordinal);
            var peaks = new HashSet<string>(StringComparer.Ordinal);
            var deep = new HashSet<string>(StringComparer.Ordinal);
            var unknown = new HashSet<string>(StringComparer.Ordinal);

            string SearchKey(QuantRow row, string sequence) =>
                DeNovoReconciler.NormalizeRunName(row.Run ?? string.Empty).ToUpperInvariant() + "\u001f" +
                row.Charge.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u001f" + sequence;
            var searchMzByKey = rows
                .Where(row => string.Equals(row.Side, NativeQuantFractions.SideIdentified,
                    StringComparison.Ordinal))
                .Select(row => new { Row = row, Sequence = Clean(row.Sequence) })
                .Where(x => x.Sequence.Length > 0)
                .GroupBy(x => SearchKey(x.Row, x.Sequence), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Row.PrecursorMz)
                    .Where(mz => mz > 0).OrderBy(mz => mz).ToList(), StringComparer.Ordinal);

            bool IsSearchEquivalent(QuantRow deNovoRow, string sequence)
            {
                if (!(deNovoRow.PrecursorMz > 0)) return false;
                if (!searchMzByKey.TryGetValue(SearchKey(deNovoRow, sequence),
                    out List<double>? candidates)) return false;
                return candidates.Any(mz => Math.Abs(deNovoRow.PrecursorMz - mz) / mz * 1e6 <=
                    DeNovoUnionResolver.PrecursorTolerancePpm);
            }

            foreach (QuantRow row in rows)
            {
                if (string.Equals(row.Side, NativeQuantFractions.SideIdentified,
                    StringComparison.Ordinal))
                {
                    string sequence = Clean(row.Sequence);
                    if (sequence.Length > 0) search.Add(sequence);
                    continue;
                }
                if (!string.Equals(row.Side, NativeQuantFractions.SideDeNovo,
                    StringComparison.Ordinal)) continue;

                List<DeNovoEvidence> evidence = row.DeNovoEvidence ?? new List<DeNovoEvidence>();
                if (evidence.Count == 0)
                {
                    string sequence = Clean(row.Sequence);
                    if (sequence.Length > 0) unknown.Add(sequence);
                    continue;
                }

                foreach (DeNovoEvidence claim in evidence)
                {
                    string sequence = Clean(claim.Sequence);
                    if (sequence.Length == 0) continue;
                    // Apply search-overlap suppression to EACH source claim, not only to the union
                    // winner.  Otherwise a DeepNovo alternate equal to search could be counted as added
                    // diversity merely because PEAKS supplied the primary label for the shared XIC.
                    if (IsSearchEquivalent(row, sequence)) continue;
                    if (string.Equals(claim.SourceId, DeNovoSource.DenovoOnly.Id,
                        StringComparison.OrdinalIgnoreCase)) peaks.Add(sequence);
                    else if (string.Equals(claim.SourceId, DeNovoSource.DeepNovo.Id,
                        StringComparison.OrdinalIgnoreCase)) deep.Add(sequence);
                    else unknown.Add(sequence);
                }
            }

            var shared = new HashSet<string>(peaks, StringComparer.Ordinal);
            shared.IntersectWith(deep);
            var peaksOnly = new HashSet<string>(peaks, StringComparer.Ordinal);
            peaksOnly.ExceptWith(deep);
            var deepOnly = new HashSet<string>(deep, StringComparer.Ordinal);
            deepOnly.ExceptWith(peaks);
            var deNovoUnion = new HashSet<string>(peaks, StringComparer.Ordinal);
            deNovoUnion.UnionWith(deep);

            // Each side under the rule the quantitation used for it (SearchCuration): an identified sequence
            // reaches the proteins containing it, a de novo tag every protein past the identity gate. Two
            // maps, because one sequence can be both a search identification and a de novo claim.
            Dictionary<string, IReadOnlyList<int>> FamiliesOf(IEnumerable<string> sequences,
                                                               Homology.HomologyOptions sideOptions) =>
                Homology.HomologyMapper.MapToFamilies(
                        Homology.HomologyMapper.Search(sequences, database, sideOptions), targets)
                    .ToDictionary(a => a.Peptide, a => a.FamilyIds, StringComparer.Ordinal);
            operation?.Report("report-census-identified-start", "Data step 2: source census 1 of 2, identified search");
            Dictionary<string, IReadOnlyList<int>> searchFamilies = FamiliesOf(search, identifiedOptions);
            operation?.Report("report-census-identified-end", "Data step 2: source census 1 of 2 complete");
            operation?.Report("report-census-denovo-start", "Data step 2: source census 2 of 2, de novo search");
            Dictionary<string, IReadOnlyList<int>> deNovoFamilies = FamiliesOf(deNovoUnion, options);
            operation?.Report("report-census-denovo-end", "Data step 2: source census 2 of 2 complete");

            var familyIds = new SortedSet<int>(searchFamilies.Values.Concat(deNovoFamilies.Values).SelectMany(ids => ids));
            var summary = new FamilySourceSequenceSummary
            {
                MappingMinimumIdentity = options.MinIdentity,
                SearchInputDistinctSequences = search.Count,
                PeaksDeNovoOnlyInputDistinctSequences = peaks.Count,
                DeepNovoInputDistinctSequences = deep.Count,
                UnknownSourceInputDistinctSequences = unknown.Count,
            };

            int CountIn(Dictionary<string, IReadOnlyList<int>> map, int family, HashSet<string> set) =>
                set.Count(sequence => map.TryGetValue(sequence, out IReadOnlyList<int>? ids) && ids.Contains(family));
            int CountFor(int family, HashSet<string> set) => CountIn(deNovoFamilies, family, set);

            foreach (int family in familyIds)
            {
                summary.Families.Add(new FamilySourceSequenceRow
                {
                    FamilyUserId = family,
                    Description = descriptions.TryGetValue(family, out string? description)
                        ? description : string.Empty,
                    SearchDistinctSequences = CountIn(searchFamilies, family, search),
                    PeaksDeNovoOnlyExclusiveDistinctSequences = CountFor(family, peaksOnly),
                    DeepNovoExclusiveDistinctSequences = CountFor(family, deepOnly),
                    SharedPeaksDeepNovoDistinctSequences = CountFor(family, shared),
                    PeaksDeNovoOnlyTotalDistinctSequences = CountFor(family, peaks),
                    DeepNovoTotalDistinctSequences = CountFor(family, deep),
                    DeNovoUnionDistinctSequences = CountFor(family, deNovoUnion),
                });
            }

            summary.Families.Sort((a, b) =>
            {
                int byUnion = b.DeNovoUnionDistinctSequences.CompareTo(a.DeNovoUnionDistinctSequences);
                if (byUnion != 0) return byUnion;
                int bySearch = b.SearchDistinctSequences.CompareTo(a.SearchDistinctSequences);
                return bySearch != 0 ? bySearch : a.FamilyUserId.CompareTo(b.FamilyUserId);
            });
            return summary;
        }
    }
}
