using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.Tests
{
    // Linked into ReferenceGoldenHost to compare the same public operations against frozen 1.5.
    internal static class WordGroupingSnapshot
    {
        internal const string Argument = "--word-grouping-snapshot";

        internal static int Run(string[] args)
        {
            if (args.Length != 3) throw new ArgumentException("Supply an input .fcf/.fqproj and an output text file.");
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var document = Path.GetExtension(args[1]).Equals(".fqproj", StringComparison.OrdinalIgnoreCase)
                ? ProjectStore.ReadFqproj(args[1]).fcf : FamilyClusterFile.Load(args[1]);
            var text = new StringBuilder();
            text.AppendLine("proteins=" + document.MyExtendedFastaItems.Count);
            foreach (string search in new[] { "metalloprotease, SVMP", "phospholipase", "ribosomal protein" })
            {
                var scope = WordCluster.ScopeItems(document.MyExtendedFastaItems, search);
                text.AppendLine("scope=" + search + ":" + string.Join(",", scope.Select(i => i.SequenceIdentifier)));
                foreach (double threshold in new[] { 0.3, 0.8, 1.0 })
                {
                    var groups = new WordCluster(scope, new()).GroupByWords(threshold, document.LockedUserIds);
                    text.AppendLine("groups=" + threshold + ":" + string.Join(";", groups.Select(g => string.Join(",", g))));
                }
            }
            var matcher = new WordCluster(document.MyExtendedFastaItems, new() { "Fragment" });
            foreach (var mode in new[] { KeywordMatch.Any, KeywordMatch.All })
            {
                var result = matcher.MatchKeywords(WordCluster.ParseKeywords("metalloproteinase \"ribosomal protein\""),
                    mode, WordCluster.ParseKeywords("adam \"domain containing\" protein"));
                text.AppendLine("matched=" + mode + ":" + string.Join(",", result.Matched.OrderBy(i => i)));
                text.AppendLine("proteins=" + mode + ":" + string.Join(",", result.MatchedProteins.OrderBy(i => i)));
                foreach (var entry in result.ProteinsByExcludeEntry)
                    text.AppendLine("exclude=" + entry.Key + ":" + string.Join(",", entry.Value.OrderBy(i => i)));
            }
            File.WriteAllText(args[2], text.ToString());
            Console.WriteLine("Word grouping snapshot: " + args[2]);
            return 0;
        }
    }
}
