using FamilyQuant.Operations;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace FamilyQuant.Homology
{
    /// <summary>
    /// Maps de novo sequences onto the proteins of a family database by HOMOLOGY, and from there onto
    /// families.
    ///
    /// Why not exact matching: a de novo sequence is read off a fragment spectrum, so I and L are
    /// interchangeable by physics, single residues are misordered or miscalled, and the tag can be a
    /// real peptide of a protein that is merely a close relative of anything in the database. An exact
    /// lookup answers "is this string present", which for a snake venom family database is almost
    /// always no. This is the same two-stage answer PepExplorer gives - an identity prefilter, then
    /// PAM30MS over every window - assembled from ProgramEngine.AlignMAlign (:185-293).
    ///
    /// What differs from the ported orchestration, all of it deliberate:
    ///
    ///   - Bounded windows and no exceptions from bad characters (W1, W2; see
    ///     <see cref="HomologyScorer"/>).
    ///   - A seed index, so a query is only compared against proteins that can hold it
    ///     (<see cref="HomologyDatabase"/>). Used only where it provably cannot change the answer.
    ///   - Deterministic output. The source collects into a ConcurrentBag (ProgramEngine.cs:213) and
    ///     reports whatever order the threads produced; here the parallelism is over queries, each
    ///     writing its own slot, and hits are ordered by (score descending, identifier ordinal
    ///     ascending). Two runs of the same input are byte for byte the same run.
    ///   - Ordinal string comparison for the decoy label (ProgramEngine.cs:232 uses the
    ///     culture-sensitive StartsWith overload, which for a prefix like "Reverse_" can differ under
    ///     a non-invariant culture).
    ///   - Top hits are kept by rank, not only by exact tie with the best score
    ///     (ProgramEngine.cs:255-261 keeps every protein whose score EQUALS the maximum and nothing
    ///     else), because the second-best protein of a family is exactly what a curator needs to see.
    /// </summary>
    public static class HomologyMapper
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
                    : SearchNormalized(query.Raw, query.Normalized, database, opts, operation);
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
            HomologyOptions options, OperationContext? operation)
        {
            operation?.ThrowIfCancellationRequested();
            bool useIndex = ShouldUseSeedIndex(peptide, database, options);

            IReadOnlyList<int> candidates = useIndex
                ? database.CandidatesFor(peptide)
                : (IReadOnlyList<int>)new RangeList(database.Proteins.Count);

            char[] foldMap = HomologyScorer.BuildFoldMap(options);
            List<HomologyHit> hits = new List<HomologyHit>();
            int decoys = 0;

            foreach (int index in candidates)
            {
                operation?.ThrowIfCancellationRequested();
                HomologyProtein protein = database.Proteins[index];

                // The identity prefilter of ProgramEngine.cs:225-229. A protein shorter than the
                // peptide has no window at all, which is the source's null (Aligner.cs:218-221).
                int identities = HomologyScorer.BestIdentity(peptide, protein.Sequence, foldMap, out _);

                if (identities < 0)
                {
                    continue;
                }

                double gateIdentity = identities / (double)peptide.Length;

                if (gateIdentity < options.MinIdentity)
                {
                    continue;
                }

                int offset = HomologyScorer.BestWindow(peptide, protein.Sequence, out int score);
                double identityAtOffset =
                    HomologyScorer.IdentityAt(peptide, protein.Sequence, offset, foldMap) / (double)peptide.Length;

                bool isDecoy = protein.Id.StartsWith(options.DecoyLabel, StringComparison.Ordinal);

                if (isDecoy)
                {
                    decoys++;
                }

                hits.Add(new HomologyHit(protein.Id, offset, score, identityAtOffset, gateIdentity,
                    protein.Sequence.Substring(offset, peptide.Length), isDecoy));
            }

            // Explicit target-decoy competition. The best target must beat the best decoy strictly;
            // a tie is awarded to the decoy conservatively. Keeping every target whenever any target
            // exists would discard a better decoy and bias attribution toward biology.
            bool hasTargets = hits.Any(h => !h.IsDecoy);
            bool hasDecoys = decoys > 0;
            bool decoysSuppressed = false;
            bool targetsSuppressed = false;
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

            hits.Sort(CompareHits);

            if (options.BestScoreOnly && hits.Count > 0)
            {
                int best = hits[0].Score;
                hits.RemoveAll(h => h.Score != best);
            }

            if (hits.Count > options.TopHitsPerPeptide)
            {
                hits.RemoveRange(options.TopHitsPerPeptide, hits.Count - options.TopHitsPerPeptide);
            }

            return new HomologyResult(raw, peptide, null, hits, decoysSuppressed, targetsSuppressed,
                candidates.Count, useIndex);
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

        /// <summary>
        /// Whether this query may go through the seed index.
        ///
        /// <see cref="HomologySearchStrategy.Auto"/> requires all three: an index exists, the database
        /// is big enough for the index to be worth consulting, and the index is PROVABLY lossless for
        /// a query of this length at this identity - a window that keeps enough identities must
        /// contain a run of identities at least as long as a seed
        /// (<see cref="HomologyScorer.GuaranteedMatchRun"/>). Without that third condition the index
        /// would be a silent recall change dressed up as an optimisation.
        /// </summary>
        public static bool ShouldUseSeedIndex(string peptide, HomologyDatabase database, HomologyOptions options)
        {
            if (peptide == null)
            {
                throw new ArgumentNullException(nameof(peptide));
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Strategy == HomologySearchStrategy.BruteForce || !database.HasSeedIndex)
            {
                return false;
            }

            if (options.Strategy == HomologySearchStrategy.SeedIndex)
            {
                return true;
            }

            if (database.Proteins.Count < options.BruteForceProteinLimit || peptide.Length < database.SeedLength)
            {
                return false;
            }

            // The seeds fold I onto L. So does the identity gate by default, and when ConsiderILSame
            // is off the seeds are merely MORE permissive than the gate, which cannot lose a protein.
            // K/Q is the other way round: the seeds do not fold it, so with the option on a window can
            // pass the gate through K/Q pairs that no seed matches. The guarantee below does not cover
            // that, so the index is declined rather than quietly changing what the option finds.
            if (options.KQEquivalent)
            {
                return false;
            }

            return HomologyScorer.GuaranteedMatchRun(peptide.Length, options.MinIdentity) >= database.SeedLength;
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
