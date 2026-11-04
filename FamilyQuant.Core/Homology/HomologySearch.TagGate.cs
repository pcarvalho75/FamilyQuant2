using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Homology;

public static partial class HomologySearch
{
    private static List<GateHit> TagGateProteins(string original, HomologyDatabase database, HomologyOptions options,
        OperationContext? operation, PositionalIndex? index, out int candidatesExamined, out bool usedIndex, out bool usedConfidence)
    {
        var variants = HomologyTagErrorModel.Variants(original, options.Science!.TagErrors!);
        usedConfidence = variants[0].UsedLocalConfidence;
        char[] fold = HomologyScorer.BuildFoldMap(options);
        var chosen = new Dictionary<int, GateHit>();
        var considered = new HashSet<int>();
        bool indexed = false;
        foreach (HomologyTagVariant variant in variants)
        {
            operation?.ThrowIfCancellationRequested();
            double total = variant.Weights.Sum();
            int required = HomologyTagErrorModel.MinimumMatchingResidues(variant.Weights, options.MinIdentity);
            long threshold = (long)variant.Sequence.Length - 2 - 3L * (variant.Sequence.Length - required);
            if (index != null && options.Strategy == HomologySearchStrategy.Auto && !options.KQEquivalent && threshold > 0)
            {
                indexed = true;
                index.VisitCandidateWindows(variant.Sequence, database, (int)threshold, operation, Evaluate);
            }
            else if (index != null && options.Strategy == HomologySearchStrategy.Auto && !options.KQEquivalent
                && TryWeightedSeeds(variant.Weights, options.MinIdentity, out int gram, out int[] offsets))
            {
                indexed = true;
                index.VisitWeightedSeedWindows(variant.Sequence, database, gram, offsets, operation, Evaluate);
            }
            else
            {
                for (int protein = 0; protein < database.Proteins.Count; protein++)
                {
                    operation?.ThrowIfCancellationRequested();
                    string sequence = database.Proteins[protein].Sequence;
                    if (sequence.Length < original.Length) continue; // The original PAM window must fit.
                    for (int start = 0, last = sequence.Length - variant.Sequence.Length; start <= last; start++)
                    {
                        if ((start & 4095) == 0) operation?.ThrowIfCancellationRequested();
                        Evaluate(protein, start);
                    }
                }
            }

            void Evaluate(int protein, int start)
            {
                HomologyProtein target = database.Proteins[protein];
                if (target.Sequence.Length < original.Length) return;
                considered.Add(protein);
                double matched = 0;
                int identities = 0;
                for (int i = 0; i < variant.Sequence.Length; i++)
                    if (Fold(fold, variant.Sequence[i]) == Fold(fold, target.Sequence[start + i]))
                    { matched += variant.Weights[i]; identities++; }
                double fraction = matched / total;
                if (fraction < options.MinIdentity) return;
                var alignment = new HomologyGateAlignment(target.Id, variant.Sequence, variant.Rule, start,
                    variant.Sequence.Length, fraction, variant.UsedLocalConfidence);
                if (chosen.TryGetValue(protein, out var previous))
                {
                    if (fraction < previous.WeightedIdentity) return;
                    if (fraction == previous.WeightedIdentity)
                    {
                        var old = previous.Alignment!;
                        int oldRewrites = old.Rule == "Original" ? 0 : 1;
                        int byRewrites = variant.Rewrites.CompareTo(oldRewrites);
                        int bySequence = string.CompareOrdinal(variant.Sequence, old.Variant);
                        if (byRewrites > 0 || byRewrites == 0 && (bySequence > 0 || bySequence == 0 && start >= old.Offset)) return;
                    }
                }
                chosen[protein] = new GateHit(protein, identities, -1, fraction, alignment);
            }
        }
        candidatesExamined = considered.Count; usedIndex = indexed;
        return chosen.OrderBy(p => p.Key).Select(p => p.Value).ToList();
    }

    private sealed partial class PositionalIndex
    {
        // Same sorted diagonal lemma as the default path; the caller supplies a conservative
        // matching-residue lower bound for the weighted gate, then verifies its exact ordered sum.
        internal void VisitCandidateWindows(string query, HomologyDatabase database, int threshold,
            OperationContext? operation, Action<int, int> visit)
        {
            int grams = query.Length - 2;
            var buckets = new PostingBucket[grams];
            var merge = new PriorityQueue<PostingCursor, ulong>(grams);
            int visited = 0;
            try
            {
                for (int offset = 0; offset < grams; offset++)
                {
                    buckets[offset] = postings[Pack(query, offset)];
                    EnqueueNext(offset, 0);
                }
                while (merge.TryDequeue(out PostingCursor cursor, out ulong diagonal))
                {
                    int votes = 1;
                    EnqueueNext(cursor.QueryOffset, cursor.NextPosting);
                    while (merge.TryPeek(out _, out ulong next) && next == diagonal)
                    {
                        merge.TryDequeue(out cursor, out _); votes++;
                        EnqueueNext(cursor.QueryOffset, cursor.NextPosting);
                    }
                    if (votes >= threshold) visit((int)(diagonal >> 32), (int)(uint)diagonal);
                }
            }
            finally { Array.Clear(buckets); }

            void EnqueueNext(int queryOffset, int nextIndex)
            {
                PostingBucket bucket = buckets[queryOffset];
                while (nextIndex < bucket.Length)
                {
                    if ((++visited & 4095) == 0) operation?.ThrowIfCancellationRequested();
                    ulong occurrence = bucket.Read(nextIndex++);
                    int protein = (int)(occurrence >> 32);
                    int start = (int)(uint)occurrence - queryOffset;
                    if (start < 0 || start > database.Proteins[protein].Sequence.Length - query.Length) continue;
                    merge.Enqueue(new PostingCursor(queryOffset, nextIndex), ((ulong)(uint)protein << 32) | (uint)start);
                    return;
                }
            }
        }
    }
}
