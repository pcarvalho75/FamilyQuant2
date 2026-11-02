using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FamilyQuant.Operations;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void ClusteringResultTests(string workDir)
        {
            ClusteringImprovementGuards(workDir);
            var cache = typeof(USearchTools).GetField("PairwiseEdgeCache", System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic)!;
            Check("X14: exhaustive memo stores no edge list", !cache.FieldType.ToString().Contains("List`1", StringComparison.Ordinal));
            Directory.CreateDirectory(workDir);
            string table = Path.Combine(workDir, "streamed_edges.tsv");
            File.WriteAllText(table, "A description\tB\t0.9\t90\t100\t100\ninvalid\nB\tC\t1\t100\t100\t100\n");
            var fractional = USearchTools.ReadPairwiseEdgesForTest(table).ToArray();
            Check("X14: streamed fractional table preserves labels, scale and malformed-row handling", fractional.Length == 2 &&
                fractional[0].QueryId == "A" && fractional[0].IdentityPercent == 90 && fractional[1].IdentityPercent == 100);
            File.WriteAllText(table, "A\tB\t0.9\t90\t100\t100\nB\tC\t80\t90\t100\t100\n");
            Check("X14: scale is decided from the whole table before merging", USearchTools.ReadPairwiseEdgesForTest(table)
                .Select(edge => edge.IdentityPercent).SequenceEqual(new[] { 0.9, 80.0 }));
            using var cancellation = new CancellationTokenSource();
            IEnumerable<USearchTools.PairwiseEdge> CancelDuringEnumeration()
            {
                yield return new USearchTools.PairwiseEdge("A", "B", 100, 100, 100, 100);
                cancellation.Cancel();
                yield return new USearchTools.PairwiseEdge("B", "C", 100, 100, 100, 100);
            }
            var cancelled = CoreOperation.Run("streamed merge", context => USearchTools.MergeFirstPassClusters(
                new Dictionary<string, int> { ["A"] = 1, ["B"] = 2, ["C"] = 3 }, CancelDuringEnumeration(), 85, context),
                cancellation.Token);
            Check("X14: cancellation is checked while consuming edges", cancelled.Status == OperationStatus.Cancelled);
            const string sequence = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHRQ";
            string input = Path.Combine(workDir, "typed_cluster.fasta");
            File.WriteAllText(input, ">A\n" + sequence + "\n>B\n" + sequence + "\n");
            var result = USearchTools.Cluster(new FileInfo(input), 70, exhaustive: false);
            Check("typed clustering preserves the known single family of identical proteins",
                result.Document.MyExtendedFastaItems.Count == 2 && result.Document.GroupedFastaItems.Count == 1);
            Check("typed clustering reports requested and executed fast pass",
                !result.RequestedExhaustive && !result.UsedExhaustive && result.Warnings.Count == 0);

            long beforeConcurrent = USearchTools.PairwiseAlignmentRunCount;
            IReadOnlyList<USearchTools.PairwiseEdge>? left = null, right = null;
            Parallel.Invoke(() => left = USearchTools.GetPairwiseEdgesForTest(new FileInfo(input)),
                () => right = USearchTools.GetPairwiseEdgesForTest(new FileInfo(input)));
            Check("X14: concurrent readers share one native alignment", left?.Count == 1 && right?.Count == 1 &&
                USearchTools.PairwiseAlignmentRunCount == beforeConcurrent + 1);

            string singleton = Path.Combine(workDir, "typed_single.fasta");
            File.WriteAllText(singleton, ">A\n" + sequence + "\n");
            var fallback = USearchTools.Cluster(new FileInfo(singleton), 70, exhaustive: true);
            Check("an empty all-pairs pass retains the known singleton family",
                fallback.Document.MyExtendedFastaItems.Count == 1 && fallback.Document.GroupedFastaItems.Count == 1);
            Check("typed clustering exposes an exhaustive fallback instead of claiming it ran",
                fallback.RequestedExhaustive && !fallback.UsedExhaustive && fallback.Warnings.Count == 1 &&
                fallback.Document.ClusteringMethod == USearchTools.FastClusteringMethod);
            long beforeEmptyRetry = USearchTools.PairwiseAlignmentRunCount;
            Check("X14: an empty alignment is retried rather than memoised",
                USearchTools.GetPairwiseEdgesForTest(new FileInfo(singleton)) == null &&
                USearchTools.PairwiseAlignmentRunCount == beforeEmptyRetry + 1);

            string output = Path.Combine(workDir, "typed_nonredundant.fasta");
            var nonredundant = RedundancyTools.RemoveRedundancyDetailed(new FileInfo(input), 100, output);
            Check("typed redundancy result retains the known before/after and output path",
                nonredundant.Before == 2 && nonredundant.After == 1 && nonredundant.OutputFile == output &&
                nonredundant.IdentityPercent == 100);
            Check("typed redundancy retains the existing duplicate survivor rule",
                File.ReadLines(output).First().StartsWith(">B", StringComparison.Ordinal));
        }
    }
}
