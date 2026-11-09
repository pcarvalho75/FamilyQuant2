using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CountOnceCommands() =>
    [new("--test-family-count-once", 84, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-family-count-once", (args, _) =>
        {
            EnsureApplication();
            FamilyCountOnceTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "count-once"));
            if (args.Contains("--legacy", StringComparer.Ordinal)) FamilyContributionChartTests(args[1]);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void FamilyCountOnceTests(string output)
    {
        Directory.CreateDirectory(output);
        var rows = new List<QuantRow>
        {
            AnQr("identified", "run_01", 1, 2, "AAAAAAAAAA", 700.5, new double[] { 60, 40 }),
            AnQr("identified", "run_01", 2, 2, "CCCCCCCCCC", 800.5, new double[] { 120, 80 }),
            AnQr("identified", "run_01", 3, 2, "DDDDDDDDDD", 900.5, new double[] { 30, 20 }),
            AnQr("denovo", "run_01", 4, 2, "EEEEEEEEEE", 1000.5, new double[] { 12, 8 }),
            AnQr("denovo", "run_01", 5, 2, "FFFFFFFFFF", 1100.5, new double[] { 24, 16 }),
            AnQr("denovo", "run_01", 6, 2, "GGGGGGGGGG", 1200.5, new double[] { 6, 4 }),
        };
        int[] Resolve(string sequence) => sequence[0] switch
        {
            'A' or 'E' => new[] { 1 },
            'C' or 'F' => new[] { 1, 2, 3 },
            _ => Array.Empty<int>(),
        };
        var table = FamilyEnvelopeQuant.Build(rows, Resolve, Resolve);
        Check("count-once fixture has identified and de novo signal in every bucket",
            table.SharedIdentifiedEnvelope > 0 && table.SharedDeNovoEnvelopePrimary > 0
            && table.UnattributedIdentifiedEnvelope > 0 && table.UnattributedDeNovoEnvelopePrimary > 0);
        foreach (var policy in new[] { SharedPeptidePolicy.FamilyUnique, SharedPeptidePolicy.SplitEqually })
        {
            var bars = FamilyContributionPlot.Fold(table, 1, policy);
            Check("count-once " + policy + " identified bars sum to measured identified signal",
                Math.Abs(bars.Sum(b => b.Identified) - table.MeasuredIdentifiedEnvelope) < 1e-9);
            Check("count-once " + policy + " de novo bars sum to measured de novo signal",
                Math.Abs(bars.Sum(b => b.DeNovo) - table.MeasuredDeNovoEnvelopePrimary) < 1e-9);
            Check("count-once " + policy + " percentages sum to 100",
                Math.Abs(bars.Sum(b => b.Total) / FamilyContributionPlot.MeasuredTotal(table) - 1) < 1e-9);
            Check("count-once " + policy + " keeps Top N and other families",
                bars.Count(b => b.Kind == FamilyContributionBarKind.Family) == 1
                && bars.Single(b => b.Kind == FamilyContributionBarKind.OtherFamilies).FamilyCount == 2);
            Check("count-once " + policy + " shared bucket follows the rule",
                bars.Any(b => b.Kind == FamilyContributionBarKind.Shared) == (policy == SharedPeptidePolicy.FamilyUnique));
            Check("count-once " + policy + " unattributed remains last",
                bars.Last().Kind == FamilyContributionBarKind.Unattributed);
            var fitted = FamilyContributionPlot.FitToHeight(FamilyContributionPlot.Fold(table, 0, policy), 3);
            Check("count-once " + policy + " height fold preserves every bucket and its signal",
                Math.Abs(fitted.Sum(b => b.Total) - table.MeasuredTotalEnvelopeReferenceCalibrated) < 1e-9
                && fitted.Count(b => b.Kind == FamilyContributionBarKind.Shared) == bars.Count(b => b.Kind == FamilyContributionBarKind.Shared));
            var chart = new FamilyContributionChart(table, 1, true, sharedPolicy: policy);
            Check("count-once heading states " + policy,
                chart.Heading.Contains(policy == SharedPeptidePolicy.SplitEqually ? "split equally" : "family-unique", StringComparison.Ordinal));
            ChartImageExporter.Save(chart, Path.Combine(output, "family-count-once-" + policy + ".png"), 1180, 420);
        }
    }
}
