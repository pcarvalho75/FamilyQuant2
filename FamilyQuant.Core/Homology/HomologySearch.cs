using FamilyQuant.Operations;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace FamilyQuant.Homology
{
    /// <summary>Deterministic gapless homology search with a lossless positional q3 filter,
    /// mismatch-budget identity scan and matrix-proven BestScoreOnly containment shortcut.
    /// The frozen HomologyMapper remains the equivalence reference.</summary>
    public static class HomologySearch
    {
        /// <summary>
        /// Searches every distinct query against the database.
        ///
        /// Queries are deduplicated by their normalised sequence, as ProgramEngine.cs:201-203 groups
        /// its registries by CleanSequence: the same tag sequenced in fifty spectra is one alignment,
        /// not fifty. The returned list carries one result per distinct query, in the order each was
        /// first seen.
        /// </summary>
        public static List<HomologyResult> Search(IEnumerable<string> peptides, HomologyDatabase database,
            HomologyOptions? options = null, OperationContext? operation = null,
            string progressStage = "homology", string? progressLabel = null, int progressBatchSize = 1)
        {
            operation?.ThrowIfCancellationRequested();
            if (peptides == null)
            {
                throw new ArgumentNullException(nameof(peptides));
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            HomologyOptions opts = (options ?? new HomologyOptions()).Clone();
            opts.Validate();

            if (opts.Strategy == HomologySearchStrategy.SeedIndex && !database.HasSeedIndex)
            {
                throw new InvalidOperationException(
                    "The seed index was forced, but this database was built without one.");
            }

            if (database.HasSeedIndex && opts.SeedLength != database.SeedLength && opts.Strategy != HomologySearchStrategy.BruteForce)
            {
                throw new InvalidOperationException(
                    "The database was indexed with seeds of length " +
                    database.SeedLength.ToString(CultureInfo.InvariantCulture) +
                    " but the search asks for " + opts.SeedLength.ToString(CultureInfo.InvariantCulture) + ".");
            }

            List<(string Raw, string Normalized, string? Rejection)> queries =
                new List<(string, string, string?)>();
            HashSet<string> seenAccepted = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> seenRejected = new HashSet<string>(StringComparer.Ordinal);

            foreach (string peptide in peptides)
            {
                operation?.ThrowIfCancellationRequested();
                if (HomologyScorer.TryNormalizeQuery(peptide, out string normalized, out string rejection))
                {
                    if (seenAccepted.Add(normalized))
                    {
                        queries.Add((peptide, normalized, null));
                    }
                }
                else if (seenRejected.Add(peptide ?? string.Empty))
                {
                    queries.Add((peptide ?? string.Empty, string.Empty, rejection));
                }
            }

            PositionalIndex? positionalIndex = opts.Strategy != HomologySearchStrategy.SeedIndex
                && queries.Any(q => q.Rejection == null && ShouldUseSeedIndex(q.Normalized, database, opts))
                ? GetIndex(database, operation) : null;
            HomologyResult[] results = new HomologyResult[queries.Count];

            // Parallel over QUERIES, never inside one: each slot is written by exactly one task, so
            // the output order is the input order whatever the scheduler does. Scoring one peptide in
            // parallel would also have to merge partial argmaxes, and the merge is where a tie-break
            // silently becomes non-deterministic.
            int completed = 0;
            if (progressBatchSize < 1) throw new ArgumentOutOfRangeException(nameof(progressBatchSize));
            if (progressLabel != null) operation?.Report(progressStage, progressLabel, 0, queries.Count);
            object progressGate = new object();
            var parallelOptions = new ParallelOptions { CancellationToken = operation?.CancellationToken ?? CancellationToken.None };
            Parallel.For(0, queries.Count, parallelOptions, i =>
            {
                (string Raw, string Normalized, string? Rejection) query = queries[i];

                results[i] = query.Rejection != null
                    ? new HomologyResult(query.Raw, string.Empty, query.Rejection,
                        Array.Empty<HomologyHit>(), false, false, 0, false)
                    : SearchNormalized(query.Raw, query.Normalized, database, opts, operation, positionalIndex);
                if (operation != null)
                    lock (progressGate)
                    {
                        if (++completed % progressBatchSize == 0 || completed == queries.Count)
                            operation.Report(progressStage, progressLabel == null ? "Searched peptide queries" : progressLabel + " "
                                + completed.ToString(CultureInfo.InvariantCulture) + " of " + queries.Count.ToString(CultureInfo.InvariantCulture), completed, queries.Count);
                    }
            });

            return new List<HomologyResult>(results);
        }

        /// <summary>Searches a single query. Convenience for callers with one sequence in hand; the
        /// normalisation and the rejection path are identical to <see cref="Search"/>.</summary>
        public static HomologyResult SearchOne(string peptide, HomologyDatabase database,
            HomologyOptions? options = null, OperationContext? operation = null)
        {
            List<HomologyResult> results = Search(new[] { peptide }, database, options, operation);
            return results[0];
        }

        private static HomologyResult SearchNormalized(string raw, string peptide, HomologyDatabase database,
            HomologyOptions options, OperationContext? operation, PositionalIndex? positionalIndex)
        {
            operation?.ThrowIfCancellationRequested();
            int required = RequiredIdentities(peptide.Length, options.MinIdentity);
            int budget = peptide.Length - required;
            char[] foldMap = HomologyScorer.BuildFoldMap(options);
            char[] foldedQuery = peptide.Select(c => Fold(foldMap, c)).ToArray();
            bool useIndex = ShouldUseSeedIndex(peptide, database, options);
            var passed = new List<GateHit>();
            int candidatesExamined;

            if (useIndex && options.Strategy != HomologySearchStrategy.SeedIndex)
            {
                // A passing window has at most e mismatches. Each mismatch spoils at most three
                // q-grams, leaving at least L-2-3e matching query positions on this exact diagonal.
                // Folded indexing can add candidates (strict I/L or undefined FASTA symbols),
                // but the identity check below always uses the caller's actual fold.
                int threshold = (int)((long)peptide.Length - 2 - 3L * budget);
                List<ulong> windows = positionalIndex!.CandidateWindows(peptide, database, threshold, operation);
                candidatesExamined = 0;
                int cursor = 0;
                while (cursor < windows.Count)
                {
                    operation?.ThrowIfCancellationRequested();
                    int proteinIndex = (int)(windows[cursor] >> 32);
                    string sequence = database.Proteins[proteinIndex].Sequence;
                    int best = -1, exactOffset = -1;
                    candidatesExamined++;
                    do
                    {
                        int offset = (int)(uint)windows[cursor++];
                        int identity = IdentityWithinBudget(foldedQuery, sequence, offset, foldMap, budget);
                        if (identity > best) best = identity;
                        if (identity == peptide.Length && exactOffset < 0) exactOffset = offset;
                    }
                    while (cursor < windows.Count && (int)(windows[cursor] >> 32) == proteinIndex);
                    if (best >= required) passed.Add(new GateHit(proteinIndex, best, exactOffset));
                }
            }
            else
            {
                // A deliberately forced legacy seed strategy keeps its original candidate set,
                // including its documented lossy behavior. Auto uses the q3 proof or brute force.
                IReadOnlyList<int> candidates = useIndex
                    ? database.CandidatesFor(peptide) : new RangeList(database.Proteins.Count);
                candidatesExamined = candidates.Count;
                foreach (int proteinIndex in candidates)
                {
                    operation?.ThrowIfCancellationRequested();
                    string sequence = database.Proteins[proteinIndex].Sequence;
                    int best = -1, exactOffset = -1;
                    for (int offset = 0, last = sequence.Length - peptide.Length; offset <= last; offset++)
                    {
                        if ((offset & 4095) == 0) operation?.ThrowIfCancellationRequested();
                        int identity = IdentityWithinBudget(foldedQuery, sequence, offset, foldMap, budget);
                        if (identity > best) best = identity;
                        if (identity == peptide.Length)
                        {
                            exactOffset = offset;
                            break; // The maximum and its earliest exact offset are now known.
                        }
                    }
                    if (best >= required) passed.Add(new GateHit(proteinIndex, best, exactOffset));
                }
            }

            bool hasTargets = passed.Any(g => !IsDecoy(g.ProteinIndex));
            bool hasDecoys = passed.Any(g => IsDecoy(g.ProteinIndex));
            bool decoysSuppressed = false, targetsSuppressed = false;
            var hits = new List<HomologyHit>();
            bool containment = options.BestScoreOnly && passed.Any(g => g.ExactOffset >= 0)
                && TryContainmentScore(peptide, foldMap, out _)
                // List.Sort is not stable for duplicate identifiers with equal score/offset.
                // Keep the original complete hit list in that case, including its tie behavior.
                && passed.Select(g => database.Proteins[g.ProteinIndex].Id).Distinct(StringComparer.Ordinal).Count() == passed.Count;
            if (containment)
            {
                TryContainmentScore(peptide, foldMap, out int maximumScore);
                bool decoyWins = passed.Any(g => g.ExactOffset >= 0 && IsDecoy(g.ProteinIndex));
                // Lower-scoring opposing hits still determine the original suppression flags.
                // Gate every candidate first; skipping that work would silently change these fields.
                targetsSuppressed = hasTargets && hasDecoys && decoyWins;
                decoysSuppressed = hasTargets && hasDecoys && !decoyWins;
                foreach (GateHit gate in passed)
                    if (gate.ExactOffset >= 0 && IsDecoy(gate.ProteinIndex) == decoyWins)
                        AddHit(gate, gate.ExactOffset, maximumScore);
            }
            else
            {
                foreach (GateHit gate in passed)
                {
                    operation?.ThrowIfCancellationRequested();
                    int offset = HomologyScorer.BestWindow(peptide, database.Proteins[gate.ProteinIndex].Sequence, out int score);
                    AddHit(gate, offset, score);
                }
                if (hasTargets && hasDecoys)
                {
                    int bestTarget = hits.Where(h => !h.IsDecoy).Max(h => h.Score);
                    int bestDecoy = hits.Where(h => h.IsDecoy).Max(h => h.Score);
                    if (bestTarget > bestDecoy)
                    {
                        hits.RemoveAll(h => h.IsDecoy);
                        decoysSuppressed = true;
                    }
                    else
                    {
                        hits.RemoveAll(h => !h.IsDecoy);
                        targetsSuppressed = true;
                    }
                }
            }
            hits.Sort(CompareHits);
            if (options.BestScoreOnly && hits.Count > 0)
            {
                int best = hits[0].Score;
                hits.RemoveAll(h => h.Score != best);
            }
            if (hits.Count > options.TopHitsPerPeptide)
                hits.RemoveRange(options.TopHitsPerPeptide, hits.Count - options.TopHitsPerPeptide);
            return new HomologyResult(raw, peptide, null, hits, decoysSuppressed, targetsSuppressed,
                candidatesExamined, useIndex);

            bool IsDecoy(int index) => database.Proteins[index].Id.StartsWith(options.DecoyLabel, StringComparison.Ordinal);
            void AddHit(GateHit gate, int offset, int score)
            {
                HomologyProtein protein = database.Proteins[gate.ProteinIndex];
                hits.Add(new HomologyHit(protein.Id, offset, score,
                    HomologyScorer.IdentityAt(peptide, protein.Sequence, offset, foldMap) / (double)peptide.Length,
                    gate.Identities / (double)peptide.Length, protein.Sequence.Substring(offset, peptide.Length), IsDecoy(gate.ProteinIndex)));
            }
        }

        private readonly record struct GateHit(int ProteinIndex, int Identities, int ExactOffset);

        // Match the original comparison, including floating-point boundary rounding and NaN.
        // Multiplication/ceiling is not interchangeable with identities/(double)length.
        private static int RequiredIdentities(int length, double minimum)
        {
            int required = 0;
            while (required < length && required / (double)length < minimum) required++;
            return required;
        }

        private static char Fold(char[] map, char residue) => residue < map.Length ? map[residue] : residue;

        private static int IdentityWithinBudget(char[] query, string protein, int offset, char[] map, int budget)
        {
            int mismatches = 0;
            for (int i = 0; i < query.Length; i++)
                if (query[i] != Fold(map, protein[offset + i]) && ++mismatches > budget)
                    return -1; // This window cannot pass, and cannot attain a passing maximum.
            return query.Length - mismatches;
        }

        private static bool TryContainmentScore(string query, char[] fold, out int score)
        {
            long maximum = 0, minimum = 0;
            foreach (char residue in query)
            {
                int diagonal = PamMatrix.Score(residue, residue), rowMinimum = diagonal;
                foreach (char other in PamMatrix.Alphabet)
                {
                    int cell = PamMatrix.Score(residue, other);
                    // Row maximizers must be exactly this gate-equivalence class. This rejects
                    // wildcard rows, raw I under IL folding, L with strict IL, and K/Q folding.
                    if ((Fold(fold, residue) == Fold(fold, other)) != (cell == diagonal) || cell > diagonal)
                    {
                        score = 0;
                        return false;
                    }
                    rowMinimum = Math.Min(rowMinimum, cell);
                }
                maximum += diagonal;
                minimum += rowMinimum;
            }
            score = maximum <= int.MaxValue && minimum >= int.MinValue ? (int)maximum : 0;
            return maximum <= int.MaxValue && minimum >= int.MinValue;
        }

        /// <summary>Score descending, then identifier ordinal ascending. Total, so equal-scoring hits
        /// cannot swap between runs.</summary>
        private static int CompareHits(HomologyHit a, HomologyHit b)
        {
            int byScore = b.Score.CompareTo(a.Score);

            if (byScore != 0)
            {
                return byScore;
            }

            int byId = string.CompareOrdinal(a.ProteinId, b.ProteinId);

            if (byId != 0)
            {
                return byId;
            }

            return a.Offset.CompareTo(b.Offset);
        }

        /// <summary>Whether this query uses an index. Auto uses a positional q3 index only when
        /// the diagonal lemma has a positive threshold and its fold is lossless. Forced SeedIndex
        /// deliberately retains the reference's legacy k-mer candidate semantics.</summary>
        public static bool ShouldUseSeedIndex(string peptide, HomologyDatabase database, HomologyOptions options)
        {
            if (peptide == null) throw new ArgumentNullException(nameof(peptide));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Strategy == HomologySearchStrategy.BruteForce || !database.HasSeedIndex) return false;
            if (options.Strategy == HomologySearchStrategy.SeedIndex) return true;
            if (database.Proteins.Count < options.BruteForceProteinLimit || options.KQEquivalent) return false;
            int budget = peptide.Length - RequiredIdentities(peptide.Length, options.MinIdentity);
            return (long)peptide.Length - 2 - 3L * budget > 0;
        }

        /// <summary>
        /// Turns hits into family assignments against the curated family file.
        ///
        /// The join is on <see cref="PatternTools.FastaTools.FastaItem.SequenceIdentifier"/>, Ordinal,
        /// and the family is <see cref="FastaItemExtended.UserID"/> - the working grouping. USearchID
        /// is the frozen import record and using it here would undo every manual split, which is the
        /// exact regression CLAUDE.md documents for the word reclustering.
        ///
        /// One assignment per result, including rejected and unmatched queries, so the caller can join
        /// the two lists positionally or by peptide without wondering what went missing.
        /// </summary>
        public static List<FamilyAssignment> MapToFamilies(IEnumerable<HomologyResult> results,
            List<FastaItemExtended> database)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            Dictionary<string, FastaItemExtended> byIdentifier =
                new Dictionary<string, FastaItemExtended>(StringComparer.Ordinal);

            foreach (FastaItemExtended item in database)
            {
                if (item == null || string.IsNullOrEmpty(item.SequenceIdentifier))
                {
                    continue;
                }

                // A repeated identifier is refused at import (USearchTools.GenerateFCF), so first wins
                // rather than throwing here on a file that already exists.
                if (!byIdentifier.ContainsKey(item.SequenceIdentifier))
                {
                    byIdentifier.Add(item.SequenceIdentifier, item);
                }
            }

            List<FamilyAssignment> assignments = new List<FamilyAssignment>();

            foreach (HomologyResult result in results)
            {
                SortedSet<int> families = new SortedSet<int>();
                SortedSet<string> mapped = new SortedSet<string>(StringComparer.Ordinal);
                SortedSet<string> unknown = new SortedSet<string>(StringComparer.Ordinal);

                foreach (HomologyHit hit in result.Hits)
                {
                    if (byIdentifier.TryGetValue(hit.ProteinId, out FastaItemExtended? item))
                    {
                        families.Add(item.UserID);
                        mapped.Add(hit.ProteinId);
                    }
                    else
                    {
                        unknown.Add(hit.ProteinId);
                    }
                }

                assignments.Add(new FamilyAssignment(
                    result.IsRejected ? result.Peptide : result.NormalizedPeptide,
                    new List<int>(families),
                    new List<string>(mapped),
                    new List<string>(unknown)));
            }

            return assignments;
        }

        /// <summary>
        /// Counts the DISTINCT peptides each family was reached by, most peptides first.
        ///
        /// TARGET hits only: a decoy family reached by a decoy-only peptide is the control, not the
        /// biology. A peptide that reaches two families is counted under both, so the counts do not
        /// add up to the peptides searched and are not meant to.
        ///
        /// The family is <see cref="FastaItemExtended.UserID"/>, for the same reason as
        /// <see cref="MapToFamilies"/>. The description is the family's name over its TARGET members
        /// (<see cref="FamilyNaming"/>: the User Annotation, else the most frequent protein name), so a
        /// row names the biology rather than a reversed sequence; a family whose members are all decoys
        /// carries an empty description rather than a decoy's.
        ///
        /// One home for this aggregation on purpose. The screen used to hold its own copy and the
        /// suite a second one, so the census the suite pins proved only that the two copies agreed.
        /// </summary>
        /// <param name="decoyLabel">The decoy prefix, compared Ordinal, as everywhere else here.</param>
        public static List<DeNovoFamilyRow> CountByFamily(IEnumerable<HomologyResult> results,
            List<FastaItemExtended> database, string decoyLabel)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            string prefix = decoyLabel ?? string.Empty;

            Dictionary<string, FastaItemExtended> byIdentifier =
                new Dictionary<string, FastaItemExtended>(StringComparer.Ordinal);

            foreach (FastaItemExtended item in database)
            {
                // First wins, as in MapToFamilies: a repeated identifier is refused at import.
                if (item != null && !string.IsNullOrEmpty(item.SequenceIdentifier)
                    && !byIdentifier.ContainsKey(item.SequenceIdentifier))
                {
                    byIdentifier.Add(item.SequenceIdentifier, item);
                }
            }

            Dictionary<int, int> peptidesByFamily = new Dictionary<int, int>();

            foreach (HomologyResult result in results)
            {
                // Per RESULT, and each family at most once: one peptide reaching a family through
                // three of its proteins is one peptide, not three.
                SortedSet<int> families = new SortedSet<int>();

                foreach (HomologyHit hit in result.Hits)
                {
                    if (!hit.IsDecoy && byIdentifier.TryGetValue(hit.ProteinId, out FastaItemExtended? item))
                    {
                        families.Add(item.UserID);
                    }
                }

                foreach (int family in families)
                {
                    peptidesByFamily[family] =
                        peptidesByFamily.TryGetValue(family, out int n) ? n + 1 : 1;
                }
            }

            // The family's name over its TARGET members, by the rule every report uses (FamilyNaming).
            Dictionary<int, string> names = FamilyNaming.Names(database.Where(item =>
                item != null && !string.IsNullOrEmpty(item.SequenceIdentifier)
                && !item.SequenceIdentifier.StartsWith(prefix, StringComparison.Ordinal)));

            return peptidesByFamily
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Select(kv => new DeNovoFamilyRow
                {
                    FamilyId = kv.Key,
                    PeptideCount = kv.Value,
                    Description = names.TryGetValue(kv.Key, out string? name) ? name : string.Empty,
                })
                .ToList();
        }

        private static readonly ConditionalWeakTable<HomologyDatabase, IndexHolder> Indexes = new();
        private sealed class IndexHolder { internal PositionalIndex? Index; }

        private static PositionalIndex GetIndex(HomologyDatabase database, OperationContext? operation)
        {
            IndexHolder holder = Indexes.GetValue(database, _ => new IndexHolder());
            lock (holder)
            {
                operation?.ThrowIfCancellationRequested();
                // A cancelled build is never published or cached as a permanent failure.
                return holder.Index ??= new PositionalIndex(database, operation);
            }
        }

        private sealed class PositionalIndex
        {
            private readonly ulong[][] postings = new ulong[1 << 15][];
            private static readonly byte[] Codes = BuildCodes();
            private static readonly byte UnknownCode = (byte)PamMatrix.ScoringIndexOf(PamMatrix.UnknownSymbol);

            internal PositionalIndex(HomologyDatabase database, OperationContext? operation)
            {
                int[] counts = new int[postings.Length];
                foreach (HomologyProtein protein in database.Proteins)
                {
                    operation?.ThrowIfCancellationRequested();
                    for (int offset = 0; offset <= protein.Sequence.Length - 3; offset++)
                    {
                        if ((offset & 4095) == 0) operation?.ThrowIfCancellationRequested();
                        counts[Pack(protein.Sequence, offset)]++;
                    }
                }
                for (int key = 0; key < counts.Length; key++)
                    postings[key] = counts[key] == 0 ? Array.Empty<ulong>() : new ulong[counts[key]];
                Array.Clear(counts);
                for (int protein = 0; protein < database.Proteins.Count; protein++)
                {
                    operation?.ThrowIfCancellationRequested();
                    string sequence = database.Proteins[protein].Sequence;
                    for (int offset = 0; offset <= sequence.Length - 3; offset++)
                    {
                        if ((offset & 4095) == 0) operation?.ThrowIfCancellationRequested();
                        uint key = Pack(sequence, offset);
                        postings[key][counts[key]++] = ((ulong)(uint)protein << 32) | (uint)offset;
                    }
                }
            }

            internal List<ulong> CandidateWindows(string query, HomologyDatabase database, int threshold, OperationContext? operation)
            {
                var counts = new Dictionary<ulong, int>();
                int visited = 0;
                for (int queryOffset = 0; queryOffset <= query.Length - 3; queryOffset++)
                {
                    operation?.ThrowIfCancellationRequested();
                    foreach (ulong occurrence in postings[Pack(query, queryOffset)])
                    {
                        if ((++visited & 4095) == 0) operation?.ThrowIfCancellationRequested();
                        int protein = (int)(occurrence >> 32);
                        int start = (int)(uint)occurrence - queryOffset;
                        if (start < 0 || start > database.Proteins[protein].Sequence.Length - query.Length) continue;
                        ulong diagonal = ((ulong)(uint)protein << 32) | (uint)start;
                        counts.TryGetValue(diagonal, out int count);
                        counts[diagonal] = count + 1;
                    }
                }
                var windows = new List<ulong>();
                foreach (var diagonal in counts)
                    if (diagonal.Value >= threshold) windows.Add(diagonal.Key);
                windows.Sort(); // Protein order, then offset; no dictionary-order tie breaking.
                return windows;
            }

            private static byte[] BuildCodes()
            {
                var codes = new byte[128];
                for (int i = 0; i < codes.Length; i++)
                    codes[i] = (byte)PamMatrix.ScoringIndexOf(i == 'I' ? 'L' : (char)i);
                return codes;
            }

            private static uint Pack(string sequence, int offset)
            {
                uint code = 0;
                for (int i = 0; i < 3; i++)
                {
                    char c = sequence[offset + i];
                    code = (code << 5) | (c < Codes.Length ? Codes[c] : UnknownCode);
                }
                return code;
            }
        }

        /// <summary>The whole database as candidate indices, without materialising a list of them.
        /// The brute force path examines 0..Count-1 for every peptide, and on a real venom database
        /// that allocation would be the largest thing the search does.</summary>
        private sealed class RangeList : IReadOnlyList<int>
        {
            private readonly int count;

            internal RangeList(int count)
            {
                this.count = count;
            }

            public int Count
            {
                get { return count; }
            }

            public int this[int index]
            {
                get
                {
                    if (index < 0 || index >= count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    return index;
                }
            }

            public IEnumerator<int> GetEnumerator()
            {
                for (int i = 0; i < count; i++)
                {
                    yield return i;
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
