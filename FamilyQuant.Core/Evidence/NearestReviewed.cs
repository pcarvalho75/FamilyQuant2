using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using FamilyQuant.Operations;

namespace FamilyQuant.Evidence
{
    /// <summary>A protein a neighbour may be: reviewed, from the file or, later, from an outside panel.
    /// <see cref="Family"/> is copied as the source holds it, so an ambiguous membership stays empty.</summary>
    public sealed class NeighbourReference
    {
        public string Accession { get; init; } = string.Empty;

        /// <summary>The reference's sequence identifier in the file; empty for an outside entry.</summary>
        public string Identifier { get; init; } = string.Empty;
        public string Family { get; init; } = string.Empty;
        public string Sequence { get; init; } = string.Empty;

        /// <summary>The reference's own evidence (memberships, domains), when the caller has it.</summary>
        [JsonIgnore]
        public ProteinEvidence? Evidence { get; init; }
    }

    /// <summary>The nearest reviewed protein of one protein of the file, never the protein itself.</summary>
    public sealed class NearestNeighbour
    {
        /// <summary>The protein's <see cref="PatternTools.FastaTools.FastaItem.SequenceIdentifier"/>.</summary>
        public string Query { get; init; } = string.Empty;
        public string Accession { get; init; } = string.Empty;

        /// <summary>The neighbour's sequence identifier in the file; empty for an outside entry.</summary>
        public string Reference { get; init; } = string.Empty;
        public string Family { get; init; } = string.Empty;
        public double IdentityPercent { get; init; }
        public int AlignmentLength { get; init; }
        public int QueryLength { get; init; }
        public int ReferenceLength { get; init; }

        /// <summary>The alignment's length as a percentage of the query's, the quantity the gate reads.</summary>
        [JsonIgnore]
        public double QueryAlignedPercent => QueryLength == 0 ? 0 : 100.0 * AlignmentLength / QueryLength;

        /// <summary>Families of other neighbours at exactly the same identity, sorted; empty when none
        /// differs, and an empty entry is a reviewed protein with no single family. A tie across families
        /// is reported, never settled by the tie-break.</summary>
        public List<string> TiedFamilies { get; init; } = new List<string>();
    }

    /// <summary>One search as stored with the evidence: its parameters, what it searched, and the answers.</summary>
    public sealed class NeighbourSearch
    {
        public string Method { get; init; } = NearestReviewed.Method;
        public double MinIdentityPercent { get; init; }
        public int MinQueryAlignedPercent { get; init; }
        public int Queries { get; init; }
        public int References { get; init; }
        public string QuerySha256 { get; init; } = string.Empty;
        public string ReferenceSha256 { get; init; } = string.Empty;

        /// <summary>Proteins with a neighbour, in the order of the queries.</summary>
        public List<NearestNeighbour> Neighbours { get; init; } = new List<NearestNeighbour>();

        public Dictionary<string, NearestNeighbour> ByQuery() =>
            Neighbours.ToDictionary(n => n.Query, StringComparer.Ordinal);
    }

    public sealed partial class EvidenceProvenance
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public NeighbourSearch? NearestReviewed { get; set; }
    }

    /// <summary>
    /// The nearest reviewed protein of every target: every target aligned with every reviewed protein
    /// (<see cref="USearchTools.SearchGlobal"/>), gated on identity and on the share of the query the
    /// alignment spans. A protein is never its own neighbour: entries are compared by accession, isoforms folded,
    /// so a duplicate entry or an isoform of the same protein cannot lend it its own label.
    /// </summary>
    public static class NearestReviewed
    {
        public const string Method = "USEARCH search_global, every pair aligned, best hit by identity, never the same entry";
        public const double DefaultMinIdentityPercent = 50.0;
        public const int DefaultMinQueryAlignedPercent = 70;

        /// <summary>Proteins aligned per USEARCH run: the search reports progress and can stop between runs.
        /// The answer does not depend on it, since each protein's hits are its own.</summary>
        public const int DefaultQueriesPerRun = 2000;

        /// <summary>The stage name the search reports its progress under.</summary>
        public const string ProgressStage = "Nearest reviewed protein";
        private const double IdentityEpsilon = 1e-9;

        private static readonly ConcurrentDictionary<string, Lazy<List<NearestNeighbour>>> Memo =
            new ConcurrentDictionary<string, Lazy<List<NearestNeighbour>>>(StringComparer.Ordinal);

        private static long searchRuns;

        /// <summary>How many times USEARCH has actually been run here; a test seam for the memo.</summary>
        public static long SearchRunCount => Interlocked.Read(ref searchRuns);

        /// <summary>Test seam: forget every memoised search, so the next one runs USEARCH again.</summary>
        public static void ForgetMemo() => Memo.Clear();

        /// <summary>The entry a sequence identifier names, isoform folded; the whole identifier when it
        /// carries no UniProt accession, so a non-UniProt entry is still only ever itself.</summary>
        public static string EntryAccession(string? identifier)
        {
            string accession = FamilyNaming.Accession(identifier);
            return FamilyNaming.IsUniProtAccession(accession) ? accession.Split('-')[0] : accession;
        }

        public static bool IsDecoy(FastaItemExtended item) =>
            SequenceLabels.IsDecoy(item.SequenceIdentifier ?? string.Empty);

        /// <summary>The targets of the file, in file order.</summary>
        public static List<FastaItemExtended> Targets(FamilyClusterFile file) =>
            file.MyExtendedFastaItems.Where(i => !IsDecoy(i)).ToList();

        /// <summary>Every reviewed target of the file, in file order, optionally narrowed to a scope the
        /// caller chooses. Proteins without a single family stay in: their hit is reported with an empty
        /// family rather than dropped, so it cannot hide behind a weaker labelled one.</summary>
        public static List<NeighbourReference> ReviewedReferences(FamilyClusterFile file,
            Func<ProteinEvidence, bool>? inScope = null)
        {
            var references = new List<NeighbourReference>();
            foreach (FastaItemExtended item in Targets(file))
            {
                ProteinEvidence? evidence = item.Evidence;
                if (evidence?.Reviewed != true) continue;
                if (inScope != null && !inScope(evidence)) continue;
                if (string.IsNullOrEmpty(item.Sequence)) continue;
                references.Add(new NeighbourReference
                {
                    Accession = EntryAccession(item.SequenceIdentifier),
                    Identifier = item.SequenceIdentifier ?? string.Empty,
                    Family = evidence.Family ?? string.Empty,
                    Sequence = item.Sequence,
                    Evidence = evidence,
                });
            }
            return references;
        }

        /// <summary>Whether an alignment passes the gate: identity at least the minimum, and an alignment
        /// at least the given percentage of the query's length (integer arithmetic, so an alignment exactly
        /// on the line passes).</summary>
        public static bool PassesGate(double identityPercent, int alignmentLength, int queryLength,
            double minIdentityPercent, int minQueryAlignedPercent)
        {
            return identityPercent + IdentityEpsilon >= minIdentityPercent
                && queryLength > 0
                && 100L * alignmentLength >= (long)minQueryAlignedPercent * queryLength;
        }

        /// <summary>
        /// The neighbour among one query's hits, or null. A hit on the same entry or failing the gate is
        /// skipped. The best is the highest identity, then the longer alignment, then the smaller accession
        /// (ordinal), so the answer does not depend on the order USEARCH wrote the hits in.
        /// </summary>
        public static NearestNeighbour? Choose(string query, string queryAccession,
            IEnumerable<(NeighbourReference Reference, double IdentityPercent, int AlignmentLength, int QueryLength, int ReferenceLength)> hits,
            double minIdentityPercent = DefaultMinIdentityPercent, int minQueryAlignedPercent = DefaultMinQueryAlignedPercent)
        {
            var kept = hits
                .Where(h => !string.Equals(h.Reference.Accession, queryAccession, StringComparison.Ordinal))
                .Where(h => PassesGate(h.IdentityPercent, h.AlignmentLength, h.QueryLength,
                    minIdentityPercent, minQueryAlignedPercent))
                .OrderByDescending(h => h.IdentityPercent)
                .ThenByDescending(h => h.AlignmentLength)
                .ThenBy(h => h.Reference.Accession, StringComparer.Ordinal)
                .ThenBy(h => h.Reference.Family, StringComparer.Ordinal)
                .ToList();
            if (kept.Count == 0) return null;

            var best = kept[0];
            List<string> tied = kept
                .Where(h => Math.Abs(h.IdentityPercent - best.IdentityPercent) <= IdentityEpsilon)
                .Select(h => h.Reference.Family)
                .Where(f => !string.Equals(f, best.Reference.Family, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            return new NearestNeighbour
            {
                Query = query,
                Accession = best.Reference.Accession,
                Reference = best.Reference.Identifier,
                Family = best.Reference.Family,
                IdentityPercent = best.IdentityPercent,
                AlignmentLength = best.AlignmentLength,
                QueryLength = best.QueryLength,
                ReferenceLength = best.ReferenceLength,
                TiedFamilies = tied,
            };
        }

        /// <summary>
        /// The nearest reference of every query. Decoys and empty sequences are not searched. The answer
        /// is memoised on the content of both sets and the gate, so a repeated call in one session does not
        /// run USEARCH again; a failed run is never remembered. <paramref name="ownAccessions"/> names the
        /// entry of a query whose identifier does not carry it (a leave-one-out on stripped headers), so
        /// that query still never takes itself. With an <paramref name="operation"/>, progress is reported
        /// after every <paramref name="queriesPerRun"/> proteins, and a cancel stops USEARCH and remembers nothing.
        /// </summary>
        public static NeighbourSearch Search(IReadOnlyList<FastaItemExtended> queries,
            IReadOnlyList<NeighbourReference> references,
            double minIdentityPercent = DefaultMinIdentityPercent,
            int minQueryAlignedPercent = DefaultMinQueryAlignedPercent,
            IReadOnlyDictionary<string, string>? ownAccessions = null,
            OperationContext? operation = null, int queriesPerRun = DefaultQueriesPerRun)
        {
            if (minIdentityPercent <= 0 || minIdentityPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(minIdentityPercent));
            if (minQueryAlignedPercent < 0 || minQueryAlignedPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(minQueryAlignedPercent));
            if (queriesPerRun < 1) throw new ArgumentOutOfRangeException(nameof(queriesPerRun));

            List<FastaItemExtended> searched = queries
                .Where(q => !IsDecoy(q) && !string.IsNullOrEmpty(q.Sequence)).ToList();
            List<NeighbourReference> panel = references.Where(r => !string.IsNullOrEmpty(r.Sequence)).ToList();
            List<string> own = searched.Select(q => OwnAccession(q, ownAccessions)).ToList();
            string queryHash = QueryFingerprint(searched, own);
            string referenceHash = ReferenceFingerprint(panel);
            string key = string.Join("|", Method,
                minIdentityPercent.ToString("R", CultureInfo.InvariantCulture),
                minQueryAlignedPercent.ToString(CultureInfo.InvariantCulture), queryHash, referenceHash);

            Lazy<List<NearestNeighbour>> lazy = Memo.GetOrAdd(key, _ => new Lazy<List<NearestNeighbour>>(
                () => Run(searched, own, panel, minIdentityPercent, minQueryAlignedPercent, operation, queriesPerRun),
                LazyThreadSafetyMode.ExecutionAndPublication));
            List<NearestNeighbour> found;
            try { found = lazy.Value; }
            catch
            {
                Memo.TryRemove(new KeyValuePair<string, Lazy<List<NearestNeighbour>>>(key, lazy));
                throw;
            }

            return new NeighbourSearch
            {
                MinIdentityPercent = minIdentityPercent,
                MinQueryAlignedPercent = minQueryAlignedPercent,
                Queries = searched.Count,
                References = panel.Count,
                QuerySha256 = queryHash,
                ReferenceSha256 = referenceHash,
                Neighbours = new List<NearestNeighbour>(found),
            };
        }

        /// <summary>The file's reviewed proteins and the stored reference panel's entries that are not already
        /// among them, both in scope when a scope is given: a neighbour then always carries the user's words, so
        /// the nearest one in scope is found even when a closer protein outside the scope exists.</summary>
        public static List<NeighbourReference> References(FamilyClusterFile file, Func<ProteinEvidence, bool>? inScope = null)
        {
            List<NeighbourReference> references = ReviewedReferences(file, inScope);
            references.AddRange(ReferencePanelFetcher.References(file.EvidenceSource?.ReferencePanel,
                references.Select(r => r.Accession)).Where(r => inScope == null || r.Evidence != null && inScope(r.Evidence)));
            return references;
        }

        /// <summary>Searches every target of the file against its reviewed proteins (narrowed by
        /// <paramref name="inScope"/> when given) and its stored reference panel, and stores the answer
        /// with the file's evidence. With an <paramref name="operation"/> the answer is stored only by its
        /// final commit, so a cancel leaves the file as it was; run it through CoreOperation.Run for
        /// progress and Cancel.</summary>
        public static NeighbourSearch SearchAndStore(FamilyClusterFile file, Func<ProteinEvidence, bool>? inScope = null,
            OperationContext? operation = null, int queriesPerRun = DefaultQueriesPerRun)
        {
            NeighbourSearch search = Search(Targets(file), References(file, inScope), operation: operation, queriesPerRun: queriesPerRun);
            void Store()
            {
                file.EvidenceSource ??= new EvidenceProvenance();
                file.EvidenceSource.NearestReviewed = search;
            }
            if (operation == null) Store();
            else operation.Commit(Store);
            return search;
        }

        /// <summary>The evidence of a neighbour's reference protein: found in the file by its identifier,
        /// or in the stored reference panel by accession for an outside entry; null when there is none.</summary>
        public static Func<NearestNeighbour, ProteinEvidence?> ReferenceEvidence(FamilyClusterFile file)
        {
            var byIdentifier = new Dictionary<string, ProteinEvidence>(StringComparer.Ordinal);
            foreach (FastaItemExtended item in file.MyExtendedFastaItems)
                if (item.Evidence != null && !string.IsNullOrEmpty(item.SequenceIdentifier))
                    byIdentifier.TryAdd(item.SequenceIdentifier, item.Evidence);
            var byAccession = new Dictionary<string, ProteinEvidence>(StringComparer.Ordinal);
            foreach (PanelEntry entry in file.EvidenceSource?.ReferencePanel?.Entries ?? new List<PanelEntry>())
                byAccession.TryAdd(entry.Accession, entry.Evidence);
            return n => n.Reference.Length > 0
                ? (byIdentifier.TryGetValue(n.Reference, out ProteinEvidence? e) ? e : null)
                : (byAccession.TryGetValue(n.Accession, out ProteinEvidence? p) ? p : null);
        }

        /// <summary>Whether a stored search still answers for these targets and references: same
        /// method, same gate, same sequences and the same reference labels.</summary>
        public static bool IsCurrent(NeighbourSearch? stored, IReadOnlyList<FastaItemExtended> queries,
            IReadOnlyList<NeighbourReference> references, IReadOnlyDictionary<string, string>? ownAccessions = null)
        {
            if (stored == null || !string.Equals(stored.Method, Method, StringComparison.Ordinal)) return false;
            List<FastaItemExtended> searched = queries
                .Where(q => !IsDecoy(q) && !string.IsNullOrEmpty(q.Sequence)).ToList();
            List<NeighbourReference> panel = references.Where(r => !string.IsNullOrEmpty(r.Sequence)).ToList();
            List<string> own = searched.Select(q => OwnAccession(q, ownAccessions)).ToList();
            return string.Equals(stored.QuerySha256, QueryFingerprint(searched, own), StringComparison.Ordinal)
                && string.Equals(stored.ReferenceSha256, ReferenceFingerprint(panel), StringComparison.Ordinal);
        }

        private static string OwnAccession(FastaItemExtended query, IReadOnlyDictionary<string, string>? ownAccessions)
        {
            string id = query.SequenceIdentifier ?? string.Empty;
            return ownAccessions != null && ownAccessions.TryGetValue(id, out string? accession) && !string.IsNullOrEmpty(accession)
                ? accession : EntryAccession(id);
        }

        private static List<NearestNeighbour> Run(List<FastaItemExtended> queries, List<string> own,
            List<NeighbourReference> references, double minIdentityPercent, int minQueryAlignedPercent,
            OperationContext? operation, int queriesPerRun)
        {
            if (queries.Count == 0 || references.Count == 0) return new List<NearestNeighbour>();

            string dir = ApplicationPaths.ScratchDirectory;
            Directory.CreateDirectory(dir);
            string token = Guid.NewGuid().ToString("N");
            string queryFasta = Path.Combine(dir, "nearest_q_" + token + ".fasta");
            string referenceFasta = Path.Combine(dir, "nearest_r_" + token + ".fasta");
            try
            {
                // Index labels, so no header character can reach USEARCH's label parsing; the queries of
                // one run keep their position in the whole list.
                WriteFasta(referenceFasta, 'r', references.Select(r => r.Sequence));
                Interlocked.Increment(ref searchRuns);
                // Only hits past the gate and on another entry are kept, so a query with hundreds of
                // short fragment hits does not hold them all in memory.
                var hitsByQuery = new Dictionary<int, List<(NeighbourReference, double, int, int, int)>>();
                operation?.Report(ProgressStage, "Aligning " + Count(queries.Count) + " proteins with "
                    + Count(references.Count) + " reviewed proteins", 0, queries.Count);
                for (int start = 0; start < queries.Count; start += queriesPerRun)
                {
                    int end = Math.Min(queries.Count, start + queriesPerRun);
                    WriteFasta(queryFasta, 'q', queries.Skip(start).Take(end - start).Select(q => q.Sequence), start);
                    USearchTools.SearchGlobal(queryFasta, referenceFasta, minIdentityPercent / 100.0, edge =>
                    {
                        int q = LabelIndex(edge.QueryId, 'q', queries.Count);
                        int r = LabelIndex(edge.TargetId, 'r', references.Count);
                        if (!PassesGate(edge.IdentityPercent, edge.AlignmentLength, edge.QueryLength,
                                minIdentityPercent, minQueryAlignedPercent)) return;
                        if (!hitsByQuery.TryGetValue(q, out var list)) hitsByQuery[q] = list = new();
                        list.Add((references[r], edge.IdentityPercent, edge.AlignmentLength, edge.QueryLength, edge.TargetLength));
                    }, operation);
                    operation?.Report(ProgressStage, "Aligned " + Count(end) + " of " + Count(queries.Count) + " proteins",
                        end, queries.Count);
                }

                var found = new List<NearestNeighbour>();
                for (int q = 0; q < queries.Count; q++)
                {
                    if (!hitsByQuery.TryGetValue(q, out var hits)) continue;
                    string id = queries[q].SequenceIdentifier ?? string.Empty;
                    NearestNeighbour? best = Choose(id, own[q], hits,
                        minIdentityPercent, minQueryAlignedPercent);
                    if (best != null) found.Add(best);
                }
                return found;
            }
            finally
            {
                TryDelete(queryFasta);
                TryDelete(referenceFasta);
            }
        }

        private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static int LabelIndex(string label, char prefix, int count)
        {
            if (label.Length > 1 && label[0] == prefix
                && int.TryParse(label.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && index < count)
                return index;
            throw new InvalidDataException("USEARCH reported a label it was not given: \"" + label + "\".");
        }

        private static void WriteFasta(string path, char prefix, IEnumerable<string> sequences, int first = 0)
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
            int n = first;
            foreach (string sequence in sequences)
            {
                writer.WriteLine(">" + prefix + n.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(sequence);
                n++;
            }
        }

        private static string QueryFingerprint(List<FastaItemExtended> queries, List<string> own) =>
            Fingerprint(queries.Select((q, i) => new[] { q.SequenceIdentifier ?? string.Empty, own[i], q.Sequence }));

        private static string ReferenceFingerprint(IEnumerable<NeighbourReference> references) =>
            Fingerprint(references.Select(r => new[] { r.Accession, r.Identifier, r.Family, r.Sequence }));

        private static string Fingerprint(IEnumerable<string[]> rows)
        {
            using var hash = new LengthPrefixedSha256();
            foreach (string[] row in rows)
                foreach (string field in row) hash.Append(field);
            return hash.Finish().ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* a leftover scratch file is harmless */ }
        }
    }
}
