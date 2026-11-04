using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Homology;

public static partial class HomologySearch
{
    // If every selected disjoint block has a mismatch, its minimum position weight
    // must be lost. A sum greater than the allowed lost weight proves that at least
    // one block is exact. A positive-sum roundoff margin only widens candidates.
    private static bool TryWeightedSeeds(double[] weights, double gate, out int gram, out int[] offsets)
    {
        gram = 0; offsets = Array.Empty<int>();
        double total = weights.Sum();
        double margin = 16 * (Math.BitIncrement(1.0) - 1.0) * weights.Length * total;
        double allowedLoss = (1 - gate) * total + margin;
        if (!(total > 0) || !double.IsFinite(allowedLoss)) return false;
        for (int width = Math.Min(3, weights.Length); width >= 1; width--)
        {
            var best = new double[weights.Length + 1];
            var take = new bool[weights.Length];
            var cost = new double[weights.Length];
            for (int start = weights.Length - width; start >= 0; start--)
            {
                double minimum = weights[start];
                for (int k = 1; k < width; k++) minimum = Math.Min(minimum, weights[start + k]);
                cost[start] = minimum;
                double chosen = minimum + best[start + width];
                if (chosen > best[start + 1]) { best[start] = chosen; take[start] = true; }
                else best[start] = best[start + 1];
            }
            if (best[0] <= allowedLoss) continue;
            var selected = new List<int>();
            for (int start = 0; start <= weights.Length - width;)
                if (take[start]) { selected.Add(start); start += width; } else start++;
            var pruned = new List<int>(); double loss = 0;
            foreach (int start in selected.OrderByDescending(p => cost[p]).ThenBy(p => p))
            {
                pruned.Add(start); loss += cost[start];
                if (loss > allowedLoss) break;
            }
            if (loss <= allowedLoss) continue;
            gram = width; offsets = pruned.Order().ToArray(); return true;
        }
        return false;
    }

    private sealed partial class PositionalIndex
    {
        private Lazy<PostingBucket[]>? singlePostings, pairPostings;

        internal void PrepareWeightedSeeds(HomologyDatabase database, OperationContext? operation)
        {
            singlePostings = new(() => BuildShortPostings(database, 1, operation));
            pairPostings = new(() => BuildShortPostings(database, 2, operation));
        }

        private void ReleaseWeightedSeeds()
        {
            if (singlePostings?.IsValueCreated == true) Array.Clear(singlePostings.Value);
            if (pairPostings?.IsValueCreated == true) Array.Clear(pairPostings.Value);
        }

        private static uint PackShort(string sequence, int offset, int gram)
        {
            uint packed = 0;
            for (int k = 0; k < gram; k++)
            {
                char c = sequence[offset + k];
                packed = (packed << 5) | (c < Codes.Length ? Codes[c] : UnknownCode);
            }
            return packed;
        }

        private static PostingBucket[] BuildShortPostings(HomologyDatabase database, int gram, OperationContext? operation)
        {
            var result = new PostingBucket[1 << (5 * gram)];
            var counts = new int[result.Length];
            bool compact = database.Proteins.Count <= 65536 && database.Proteins.All(p => p.Sequence.Length <= 65536);
            foreach (var protein in database.Proteins)
            {
                operation?.ThrowIfCancellationRequested();
                for (int offset = 0; offset <= protein.Sequence.Length - gram; offset++)
                {
                    if ((offset & 4095) == 0) operation?.ThrowIfCancellationRequested();
                    counts[PackShort(protein.Sequence, offset, gram)]++;
                }
            }
            for (int key = 0; key < result.Length; key++)
                result[key] = compact ? new(counts[key] == 0 ? Array.Empty<uint>() : new uint[counts[key]], null)
                    : new(null, counts[key] == 0 ? Array.Empty<ulong>() : new ulong[counts[key]]);
            Array.Clear(counts);
            for (int protein = 0; protein < database.Proteins.Count; protein++)
            {
                operation?.ThrowIfCancellationRequested(); string sequence = database.Proteins[protein].Sequence;
                for (int offset = 0; offset <= sequence.Length - gram; offset++)
                {
                    if ((offset & 4095) == 0) operation?.ThrowIfCancellationRequested();
                    uint key = PackShort(sequence, offset, gram);
                    result[key].Store(counts[key]++, protein, offset);
                }
            }
            return result;
        }

        internal void VisitWeightedSeedWindows(string query, HomologyDatabase database, int gram, int[] offsets,
            OperationContext? operation, Action<int, int> visit)
        {
            PostingBucket[] source = gram == 3 ? postings : gram == 2 ? pairPostings!.Value : singlePostings!.Value;
            var buckets = new PostingBucket[offsets.Length];
            var merge = new PriorityQueue<PostingCursor, ulong>(offsets.Length);
            int visited = 0;
            try
            {
                for (int slot = 0; slot < offsets.Length; slot++)
                {
                    buckets[slot] = source[PackShort(query, offsets[slot], gram)];
                    Next(slot, 0);
                }
                while (merge.TryDequeue(out var cursor, out ulong diagonal))
                {
                    Next(cursor.QueryOffset, cursor.NextPosting);
                    while (merge.TryPeek(out _, out ulong next) && next == diagonal)
                    { merge.TryDequeue(out cursor, out _); Next(cursor.QueryOffset, cursor.NextPosting); }
                    visit((int)(diagonal >> 32), (int)(uint)diagonal);
                }
            }
            finally { Array.Clear(buckets); }

            void Next(int slot, int nextIndex)
            {
                PostingBucket bucket = buckets[slot];
                while (nextIndex < bucket.Length)
                {
                    if ((++visited & 4095) == 0) operation?.ThrowIfCancellationRequested();
                    ulong occurrence = bucket.Read(nextIndex++);
                    int protein = (int)(occurrence >> 32), start = (int)(uint)occurrence - offsets[slot];
                    if (start < 0 || start > database.Proteins[protein].Sequence.Length - query.Length) continue;
                    merge.Enqueue(new(slot, nextIndex), ((ulong)(uint)protein << 32) | (uint)start); return;
                }
            }
        }
    }
}
