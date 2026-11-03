using FamilyQuant.Operations;
using PatternTools.FastaTools;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FamilyQuant.Homology
{
    /// <summary>One protein as the search sees it: an identifier to report and an uppercased
    /// sequence to scan.</summary>
    public sealed class HomologyProtein
    {
        internal HomologyProtein(string id, string sequence)
        {
            Id = id;
            Sequence = sequence;
        }

        /// <summary>The FASTA identifier, exactly as it arrived - this is the key
        /// <see cref="HomologySearch.MapToFamilies"/> joins on, so it is never rewritten.</summary>
        public string Id { get; }

        /// <summary>The sequence, uppercased. Residues the matrix does not define are left in place
        /// and scored as X (see <see cref="PamMatrix.ScoringIndexOf"/>) rather than removed, so every
        /// reported offset still indexes the real sequence.</summary>
        public string Sequence { get; }
    }

    /// <summary>
    /// The protein database, plus the k-mer index that keeps a search from touching proteins that
    /// cannot possibly hold the query.
    ///
    /// PepExplorer scores EVERY protein for EVERY distinct de novo sequence
    /// (<c>database.FindAll(...)</c>, ProgramEngine.cs:225-229), so the work is
    /// peptides x proteins x residues - which is why it is wrapped in a Parallel.ForEach and still
    /// takes what it takes. The index here is the standard seed-and-extend prefilter: every position
    /// of every protein contributes its k-mer, folded I to L so the seeds carry the same ambiguity the
    /// identity prefilter does, and a query is only compared against proteins that share at least one
    /// of its own k-mers.
    ///
    /// The index stores PROTEIN indices, not (protein, offset) pairs. The prefilter it feeds is at
    /// protein granularity - the identity scan then examines every window of that protein anyway - so
    /// offsets would cost one entry per occurrence instead of one per protein and buy nothing.
    ///
    /// Whether the index can be used WITHOUT changing the answer is decided per query, not per
    /// database: see <see cref="HomologyScorer.GuaranteedMatchRun"/> and
    /// <see cref="HomologySearchStrategy"/>.
    /// </summary>
    public sealed class HomologyDatabase
    {
        private readonly List<HomologyProtein> proteins;
        private readonly Dictionary<uint, List<int>>? seedIndex;

        private HomologyDatabase(List<HomologyProtein> proteins, Dictionary<uint, List<int>>? seedIndex,
            int seedLength, int skippedEntries)
        {
            this.proteins = proteins;
            this.seedIndex = seedIndex;
            SeedLength = seedLength;
            SkippedEntries = skippedEntries;
        }

        /// <summary>The proteins, in the order they were supplied.</summary>
        public IReadOnlyList<HomologyProtein> Proteins
        {
            get { return proteins; }
        }

        /// <summary>The k the index was built with.</summary>
        public int SeedLength { get; }

        /// <summary>True when an index was built. A database built without one can still be searched;
        /// it just always scans.</summary>
        public bool HasSeedIndex
        {
            get { return seedIndex != null; }
        }

        /// <summary>Entries that carried no sequence and are therefore not searchable. They are
        /// counted rather than silently forgotten.</summary>
        public int SkippedEntries { get; }

        /// <summary>Number of distinct seeds in the index, or 0 when there is none.</summary>
        public int SeedCount
        {
            get { return seedIndex == null ? 0 : seedIndex.Count; }
        }

        /// <summary>
        /// Builds from the repo's own FASTA type. <see cref="FastaItemExtended"/> derives from
        /// <see cref="FastaItem"/>, so the same call serves the curated family file.
        /// </summary>
        public static HomologyDatabase Build(IEnumerable<FastaItem> items,
            int seedLength = HomologyOptions.DefaultSeedLength, bool buildSeedIndex = true, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            List<(string Id, string Sequence)> entries = new List<(string, string)>();

            foreach (FastaItem item in items)
            {
                operation?.ThrowIfCancellationRequested();
                if (item == null)
                {
                    continue;
                }

                entries.Add((item.SequenceIdentifier, item.Sequence));
            }

            return Build(entries, seedLength, buildSeedIndex, operation);
        }

        /// <summary>Builds from bare (identifier, sequence) pairs, so the core can be exercised
        /// without a FASTA file.</summary>
        public static HomologyDatabase Build(IEnumerable<(string Id, string Sequence)> entries,
            int seedLength = HomologyOptions.DefaultSeedLength, bool buildSeedIndex = true, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (seedLength < 2 || seedLength > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(seedLength),
                    "SeedLength must be between 2 and 6 (5 bits per residue in a 32-bit key), was " +
                    seedLength.ToString(CultureInfo.InvariantCulture) + ".");
            }

            List<HomologyProtein> parsed = new List<HomologyProtein>();
            int skipped = 0;

            foreach ((string Id, string Sequence) entry in entries)
            {
                operation?.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Sequence))
                {
                    skipped++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    // An unidentified protein could be hit but never attributed to a family, which
                    // would silently lose the peptide. USearchTools refuses a database with repeated
                    // identifiers for the same reason.
                    throw new ArgumentException(
                        "A database entry has no identifier; its sequence starts '" +
                        entry.Sequence.Substring(0, Math.Min(12, entry.Sequence.Length)) + "'.", nameof(entries));
                }

                parsed.Add(new HomologyProtein(entry.Id, entry.Sequence.Trim().ToUpperInvariant()));
            }

            Dictionary<uint, List<int>>? index = buildSeedIndex ? BuildSeedIndex(parsed, seedLength, operation) : null;

            return new HomologyDatabase(parsed, index, seedLength, skipped);
        }

        private static Dictionary<uint, List<int>> BuildSeedIndex(List<HomologyProtein> proteins, int seedLength, OperationContext? operation)
        {
            Dictionary<uint, List<int>> index = new Dictionary<uint, List<int>>();

            for (int p = 0; p < proteins.Count; p++)
            {
                operation?.ThrowIfCancellationRequested();
                string sequence = proteins[p].Sequence;
                int last = sequence.Length - seedLength;

                for (int offset = 0; offset <= last; offset++)
                {
                    operation?.ThrowIfCancellationRequested();
                    uint seed = Pack(sequence, offset, seedLength);

                    if (!index.TryGetValue(seed, out List<int>? bucket))
                    {
                        index[seed] = bucket = new List<int>();
                    }

                    // Proteins arrive in order, so the same protein repeating a seed is always the
                    // bucket's last entry: one comparison deduplicates.
                    if (bucket.Count == 0 || bucket[bucket.Count - 1] != p)
                    {
                        bucket.Add(p);
                    }
                }
            }

            return index;
        }

        /// <summary>
        /// A k-mer as a 32-bit key: 5 bits per residue over the 26 matrix symbols, with I folded onto
        /// L so a seed carries the same I/L ambiguity the identity prefilter does, and undefined
        /// residues folded onto X so a real FASTA cannot produce a key that is out of range.
        /// </summary>
        private static uint Pack(string sequence, int start, int length)
        {
            uint packed = 0;

            for (int i = 0; i < length; i++)
            {
                char c = sequence[start + i];

                if (c == 'I')
                {
                    c = 'L';
                }

                packed = (packed << 5) | (uint)PamMatrix.ScoringIndexOf(c);
            }

            return packed;
        }

        /// <summary>
        /// The proteins sharing at least one seed with the query, as ascending indices into
        /// <see cref="Proteins"/>. Ascending so the candidate set does not depend on the order the
        /// query's seeds happen to hash.
        /// </summary>
        internal List<int> CandidatesFor(string peptide)
        {
            List<int> candidates = new List<int>();

            if (seedIndex == null || peptide.Length < SeedLength)
            {
                return candidates;
            }

            HashSet<int> seen = new HashSet<int>();
            int last = peptide.Length - SeedLength;

            for (int offset = 0; offset <= last; offset++)
            {
                if (!seedIndex.TryGetValue(Pack(peptide, offset, SeedLength), out List<int>? bucket))
                {
                    continue;
                }

                foreach (int p in bucket)
                {
                    if (seen.Add(p))
                    {
                        candidates.Add(p);
                    }
                }
            }

            candidates.Sort();
            return candidates;
        }
    }
}
