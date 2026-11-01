using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace FamilyQuant
{
    /// <summary>
    /// How a keyword super cluster decides that a family's description matches the keyword list.
    /// <see cref="All"/> requires every keyword to appear among the family's words; <see cref="Any"/>
    /// requires at least one. A single keyword behaves identically under either.
    /// </summary>
    public enum KeywordMatch
    {
        All,
        Any
    }

    /// <summary>What <see cref="WordCluster.MatchKeywords"/> found, before locks and before any merge.</summary>
    public sealed class KeywordFamilies
    {
        /// <summary>Families with a member carrying the keywords, exclusions NOT applied.</summary>
        public HashSet<int> Matched { get; } = new HashSet<int>();

        /// <summary>The proteins that carry the keywords themselves, as positions in the list the
        /// <see cref="WordCluster"/> was built from. A family joins on one of them and brings the rest.</summary>
        public HashSet<int> MatchedProteins { get; } = new HashSet<int>();

        /// <summary>Every exclude entry, lower-cased, with the families carrying it (possibly none).</summary>
        public Dictionary<string, HashSet<int>> ByExcludeEntry { get; } =
            new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        /// <summary>Every exclude entry with the proteins carrying it, as positions like
        /// <see cref="MatchedProteins"/>.</summary>
        public Dictionary<string, HashSet<int>> ProteinsByExcludeEntry { get; } =
            new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        /// <summary>Families carrying any exclude entry.</summary>
        public HashSet<int> Excluded => new HashSet<int>(ByExcludeEntry.Values.SelectMany(f => f));
    }

    public class WordCluster
    {
        List<FastaClusterElement> MyClusterElements { get; set; }
        Dictionary<string, int> WordIndex { get; set; }

        public WordCluster(List<FastaItemExtended> fastaItems, List<string> additionalWordsToExlcude)
        {
            var matcher = new DescriptionWordMatcher(additionalWordsToExlcude);
            var descriptions = fastaItems.Select(item => matcher.Read(item.Description)).ToList();
            var termsPerItem = descriptions.Select(d => d.Terms).ToList();

            WordIndex = GenerateWordIndex(termsPerItem);

            MyClusterElements = new List<FastaClusterElement>(fastaItems.Count);

            for (int i = 0; i < fastaItems.Count; i++)
            {
                string[] terms = termsPerItem[i];

                MyClusterElements.Add(new FastaClusterElement()
                {
                    SequenceIdentifier = fastaItems[i].SequenceIdentifier,
                    // The CURATED family, not the original USEARCH one. Reading USearchID here made every
                    // manual split invisible to reclustering: a family the user had separated by hand still
                    // arrived as a single group, and the operation then wrote that single group back over
                    // the curation. See UpdateClusterIds, which is the other half of the same defect.
                    FamilyId = fastaItems[i].UserID,
                    Words = terms.Select(t => WordIndex[t]).ToList(),
                    Description = string.Join(" ", terms),
                    RawTokens = descriptions[i].RawTokens,
                    MatchWords = descriptions[i].MatchWords,
                });
            }
        }

        /// <summary>Parses the shared keyword and exclusion boxes, preserving quoted phrases.</summary>
        public static List<string> ParseKeywords(string? text) => DescriptionWordMatcher.ParseKeywords(text);

        /// <summary>
        /// Groups the elements based on their similarity, represented as vectors,
        /// and merges clusters that exceed the specified similarity threshold.
        /// </summary>
        /// <param name="clusterThreshold">The similarity threshold for merging clusters.</param>
        /// <returns>A list of merged clusters, each represented as a list of curated family ids.</returns>
        public List<List<int>> GroupByWords(double clusterThreshold, ISet<int>? lockedFamilies = null)
        {
            var initialGroups = MyClusterElements.GroupBy(item => item.FamilyId).ToDictionary(g => g.Key, g => g.ToList());

            // Locked families still CONTRIBUTE to the document frequencies - the vocabulary of the
            // database does not change because the curator protected a family - but they are excluded
            // from the merge itself, so they neither absorb nor get absorbed. Removing them before
            // the vectors were built was the first draft here, and it was wrong in a quiet way:
            // shrinking the document count changes the IDF of every shared word, so locking one
            // family could have shifted how OTHER families cluster.
            var groupVectors = GenerateVectors(initialGroups);

            var locked = new List<int>();
            if (lockedFamilies != null && lockedFamilies.Count > 0)
            {
                locked.AddRange(groupVectors.Select(gv => gv.Item1).Where(lockedFamilies.Contains));
                groupVectors = groupVectors.Where(gv => !lockedFamilies.Contains(gv.Item1)).ToList();
            }

            var merged = MergeClusters(groupVectors, clusterThreshold);

            foreach (int id in locked) merged.Add(new List<int> { id });
            return merged;
        }

        /// <summary>
        /// Cluster ids for a reclustering result that a user can FOLLOW: every cluster keeps the
        /// smallest UserID among its members, so an untouched family keeps its exact number and a
        /// merge is visible as the other members adopting the survivor's number.
        ///
        /// The previous mapping renumbered every cluster densely from zero, which renumbered even
        /// families the operation never touched. The bench report was immediate: after one word
        /// reclustering the UserID column no longer matched anything, and a manual number like 500
        /// vanished even though its family had not merged with anything.
        /// </summary>
        public static Dictionary<int, int> BuildStableMapping(List<List<int>> clusters)
        {
            var mapping = new Dictionary<int, int>();
            foreach (var cluster in clusters)
            {
                int survivor = cluster.Min();
                foreach (int id in cluster) mapping[id] = survivor;
            }
            return mapping;
        }

        /// <summary>
        /// Brings the USearchID column back into step with the UserID column after an automatic
        /// grouping (word reclustering or a keyword super cluster) has renumbered UserID.
        ///
        /// For every item whose current UserID is NOT locked, USearchID is set equal to UserID. An
        /// automatic operation therefore keeps the two columns identical, which is the invariant: the
        /// USearch ID column shows the last automatic grouping, and it differs from the User ID column
        /// only where the user renumbered a family by hand. A manual renumbering is exactly a lock
        /// (see the cell-edit handler), so a locked family is left alone: its members did not move in
        /// the merge, which holds locked families out as singletons, and its frozen USearchID is the
        /// pre-manual-edit number the invariant wants preserved.
        ///
        /// Calling this on a file that carried a stale unlocked divergence from an older build heals
        /// it as a side effect: the next reclustering syncs those rows to their UserID.
        /// </summary>
        public static void SyncUnlockedIds(IEnumerable<FastaItemExtended> items, ISet<int> lockedUserIds)
        {
            foreach (var item in items)
            {
                if (!lockedUserIds.Contains(item.UserID))
                {
                    item.USearchID = item.UserID;
                }
            }
        }

        /// <summary>
        /// The items a reclustering should run over, given what is typed in the Search box.
        ///
        /// With no search text this is the whole universe, so an empty search reclusters exactly as
        /// before. With search text it is every protein of every family that has AT LEAST ONE member
        /// matching the text - the whole family, not only the matching proteins, so a family's word
        /// vector stays complete and applying the new number renumbers all of its members. The scope is
        /// taken from the SEARCH TEXT only (the hide-decoys / hide-contaminants / XIC checkboxes are NOT
        /// applied): those hide rows for display, and letting them shrink a recluster would silently drop
        /// families the curator can still see is there. Families outside this set are left untouched.
        /// </summary>
        public static List<FastaItemExtended> ScopeItems(IEnumerable<FastaItemExtended> allItems, string? searchText)
        {
            var all = allItems as List<FastaItemExtended> ?? allItems.ToList();

            if (string.IsNullOrWhiteSpace(searchText)) return all;

            var matched = FamilyFilter.Apply(all, searchText, false, false, false);
            var scopeIds = new HashSet<int>(matched.Select(i => i.UserID));

            return all.Where(i => scopeIds.Contains(i.UserID)).ToList();
        }

        /// <summary>
        /// Which families and proteins carry the keywords and each exclude entry, without merging anything:
        /// the one matching rule. The preview reads it, and the merge moves what the preview listed.
        ///
        /// An exclude entry is read as the words it stands for and looked up in the FULL description, so
        /// "domain-containing" and "domain containing" are the same two consecutive words, and a stop word
        /// or a word of the Recluster exclude box still counts. Whole words only: "adam" must not remove
        /// the adamalysin SVMPs. Only the text before " OS=" is read, so the species Crotalus adamanteus
        /// cannot trip it either.
        /// </summary>
        public KeywordFamilies MatchKeywords(List<string> keywords, KeywordMatch match = KeywordMatch.Any,
                                             List<string>? excludeWords = null) =>
            DescriptionWordMatcher.MatchKeywords(MyClusterElements, keywords, match, excludeWords);

        /// <summary>The preview's sentence on the exclude words: what each kept out, and which kept out
        /// nothing, the failure a user cannot otherwise see. Empty when none was typed.</summary>
        public static string DescribeExclusions(IReadOnlyDictionary<string, int> proteinsKeptOutByWord)
        {
            if (proteinsKeptOutByWord == null || proteinsKeptOutByWord.Count == 0) return string.Empty;

            List<string> keptOut = proteinsKeptOutByWord.Where(k => k.Value > 0)
                .OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal)
                .Select(k => k.Key + " " + k.Value.ToString("N0", CultureInfo.CurrentCulture)).ToList();
            List<string> nothing = proteinsKeptOutByWord.Where(k => k.Value == 0)
                .Select(k => k.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

            var parts = new List<string>();
            if (keptOut.Count > 0)
                parts.Add("Proteins kept out by each exclude word: " + string.Join(", ", keptOut) + ".");
            if (nothing.Count > 0)
                parts.Add((nothing.Count == 1 ? "This exclude word" : "These exclude words") + " kept out nothing: " +
                          string.Join(", ", nothing) + ". Matching is on whole words of the description before OS=, " +
                          "so those proteins may go by another name: look for them in the list below and untick them there.");
            return string.Join("\n", parts);
        }

        private List<(int, Dictionary<int, double>)> GenerateVectors(Dictionary<int, List<FastaClusterElement>> initialGroups)
        {
            int totalDocuments = initialGroups.Count;

            // Document frequency per word in a single pass over all groups.
            // (The old version was O(vocab x groups x items) because of a per-word List.Contains scan.)
            var docFreq = new Dictionary<int, int>();
            foreach (var group in initialGroups.Values)
            {
                var seen = new HashSet<int>();
                foreach (var item in group)
                {
                    foreach (var wordId in item.Words)
                    {
                        seen.Add(wordId);
                    }
                }

                foreach (var wordId in seen)
                {
                    docFreq[wordId] = docFreq.TryGetValue(wordId, out var c) ? c + 1 : 1;
                }
            }

            // Smoothed on BOTH sides, and shifted, so the weight is always positive.
            //
            // log(N / (1 + df)) is exactly zero when a term sits in N-1 of the N families and negative
            // when it sits in all of them. A family whose whole description is made of such words then
            // gets an all-zero vector; the "norm > 0" guard below leaves it unnormalised, every dot
            // product against it is zero, and it cannot merge with anything at any threshold, not even
            // with a family whose description is byte for byte identical. The negative branch was worse
            // in the other direction: two vectors sharing only a very common word both point along that
            // one negative axis, and their cosine comes out at 1, so a metalloproteinase merged with a
            // myoglobin at the strictest setting the interface offers because both said "snake".
            var idf = new Dictionary<int, double>(docFreq.Count);
            foreach (var kv in docFreq)
            {
                idf[kv.Key] = Math.Log((1.0 + totalDocuments) / (1.0 + kv.Value)) + 1.0;
            }

            // Build a sparse, L2-normalized TF-IDF vector per group (only non-zero word weights are
            // stored, instead of one dense double[] of length = full vocabulary per group).
            var groupVectors = new List<(int, Dictionary<int, double>)>(initialGroups.Count);
            foreach (var group in initialGroups)
            {
                var vector = new Dictionary<int, double>();
                foreach (var item in group.Value)
                {
                    foreach (var wordId in item.Words)
                    {
                        vector[wordId] = vector.TryGetValue(wordId, out var v) ? v + idf[wordId] : idf[wordId];
                    }
                }

                double norm = Math.Sqrt(vector.Values.Sum(x => x * x));
                if (norm > 0)
                {
                    foreach (var key in vector.Keys.ToList())
                    {
                        vector[key] /= norm;
                    }
                }

                groupVectors.Add((group.Key, vector));
            }

            return groupVectors;
        }

        private List<List<int>> MergeClusters(List<(int, Dictionary<int, double>)> groupVectors, double clusterThreshold)
        {
            // Seed clusters with the most descriptive groups first (most non-zero word weights).
            var sortedClusters = groupVectors
                .OrderByDescending(gv => gv.Item2.Count)
                .ToList();

            var mergedClusters = new List<List<int>>();
            var merged = new bool[sortedClusters.Count];
            var isMatch = new bool[sortedClusters.Count];

            for (int i = 0; i < sortedClusters.Count; i++)
            {
                if (merged[i]) continue;
                merged[i] = true;

                var seed = sortedClusters[i];
                var clusterGroup = new List<int> { seed.Item1 };

                // The parallel region only READS shared state (merged / sortedClusters) and each
                // iteration writes a single slot of isMatch that no other iteration touches, so there is
                // no race; every mutation of `merged` happens afterwards on this thread. This replaced an
                // earlier version where threads mutated a shared List and rebuilt a shared ConcurrentBag,
                // losing removals and emitting duplicated, overlapping clusters.
                Parallel.For(i + 1, sortedClusters.Count, j =>
                {
                    if (merged[j]) return;
                    if (DotProduct(seed.Item2, sortedClusters[j].Item2) >= clusterThreshold)
                    {
                        isMatch[j] = true;
                    }
                });

                // Ascending order, so a family's members are always listed the same way. Collecting the
                // matches in a ConcurrentBag made the membership order vary between identical runs.
                for (int j = i + 1; j < sortedClusters.Count; j++)
                {
                    if (!isMatch[j]) continue;

                    isMatch[j] = false;   // reset for the next seed; the buffer is reused across iterations
                    merged[j] = true;
                    clusterGroup.Add(sortedClusters[j].Item1);
                }

                mergedClusters.Add(clusterGroup);
            }

            return mergedClusters;
        }

        private double DotProduct(Dictionary<int, double> vectorA, Dictionary<int, double> vectorB)
        {
            // Iterate the smaller map; vectors are L2-normalized so this dot product is the cosine similarity.
            if (vectorA.Count > vectorB.Count)
            {
                (vectorA, vectorB) = (vectorB, vectorA);
            }

            double dotProduct = 0.0;
            foreach (var entry in vectorA)
            {
                if (vectorB.TryGetValue(entry.Key, out double b))
                {
                    dotProduct += entry.Value * b;
                }
            }

            return dotProduct;
        }

        /// <summary>
        /// Assigns every distinct term an id, in order of first appearance. The previous version counted
        /// term frequencies into a ConcurrentDictionary and then used only its keys, so the ids came out
        /// in a different order on every run for no benefit.
        /// </summary>
        private Dictionary<string, int> GenerateWordIndex(List<string[]> termsPerItem)
        {
            var wordIndex = new Dictionary<string, int>();

            foreach (string[] terms in termsPerItem)
            {
                foreach (string term in terms)
                {
                    if (!wordIndex.ContainsKey(term))
                    {
                        wordIndex.Add(term, wordIndex.Count);
                    }
                }
            }

            return wordIndex;
        }
    }

    public class FastaClusterElement
    {
        public string SequenceIdentifier { get; set; } = string.Empty;
        public List<int> Words { get; set; } = new List<int>();
        /// <summary>The curated family this protein currently belongs to (FastaItemExtended.UserID).</summary>
        public int FamilyId { get; set; }
        public string Description { get; set; } = string.Empty;

        /// <summary>Every word of the description before " OS=", lower-cased, stop words kept, for
        /// matching quoted keyword phrases.</summary>
        public string[] RawTokens { get; set; } = Array.Empty<string>();

        /// <summary><see cref="RawTokens"/> without the stop words and the words excluded from comparing:
        /// what a single-word keyword is looked up in.</summary>
        public string[] MatchWords { get; set; } = Array.Empty<string>();
    }

}
