using System;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void ClusteringResultTests(string workDir)
        {
            Directory.CreateDirectory(workDir);
            const string sequence = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHRQ";
            string input = Path.Combine(workDir, "typed_cluster.fasta");
            File.WriteAllText(input, ">A\n" + sequence + "\n>B\n" + sequence + "\n");
            var result = USearchTools.Cluster(new FileInfo(input), 70, exhaustive: false);
            Check("typed clustering preserves the known single family of identical proteins",
                result.Document.MyExtendedFastaItems.Count == 2 && result.Document.GroupedFastaItems.Count == 1);
            Check("typed clustering reports requested and executed fast pass",
                !result.RequestedExhaustive && !result.UsedExhaustive && result.Warnings.Count == 0);

            string singleton = Path.Combine(workDir, "typed_single.fasta");
            File.WriteAllText(singleton, ">A\n" + sequence + "\n");
            var fallback = USearchTools.Cluster(new FileInfo(singleton), 70, exhaustive: true);
            Check("an empty all-pairs pass retains the known singleton family",
                fallback.Document.MyExtendedFastaItems.Count == 1 && fallback.Document.GroupedFastaItems.Count == 1);
            Check("typed clustering exposes an exhaustive fallback instead of claiming it ran",
                fallback.RequestedExhaustive && !fallback.UsedExhaustive && fallback.Warnings.Count == 1 &&
                fallback.Document.ClusteringMethod == USearchTools.FastClusteringMethod);

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
