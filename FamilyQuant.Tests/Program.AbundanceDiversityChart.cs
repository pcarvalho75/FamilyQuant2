using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AbundanceDiversityCommands() => new[]
    {
        new TestCommand("--test-abundance-diversity", 10000, TestCommandPhase.Normalized,
            args => args.Contains("--test-abundance-diversity", StringComparer.Ordinal), (_, work) =>
            {
                AbundanceDiversityTests(Path.Combine(work, "abundance-diversity"));
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            })
    };

    private static void AbundanceDiversityTests(string folder)
    {
        Section("Abundance and diversity - same shares, independent protein count");
        EnsureApplication(); Directory.CreateDirectory(folder);
        var storedMeans = new FamilyRunSharesResult { Families = new()
        {
            new() { FamilyUserId = 1, MeanShare = .7, StandardDeviation = .1, AvailableRunCount = 3 },
            new() { FamilyUserId = 2, MeanShare = .3, StandardDeviation = .1, AvailableRunCount = 3 },
            new() { FamilyUserId = 3, MeanShare = null }
        } };
        var adapted = StoredCompositionChartData.FromRunShares(storedMeans, new FamilyProteinCountsResult
        {
            Families = new() { new() { FamilyUserId = 1, GreedyProteinCount = 5, UniquePeptideProteinCount = 2 } }
        });
        Check("stored FL fractional means and SD become percentage points without renormalization",
            adapted.Families[0].MeanPercent == 70 && adapted.Families[0].SdPercent == 10
            && adapted.Families[0].GreedyProteinCount == 5 && adapted.UnavailableFamilies.SequenceEqual(new[] { "Family 3" }));
        var manifest = new QuantManifest { FamilyRunShares = storedMeans };
        var results = new AnalyticsControl(SyntheticAnalyticsRows(), manifest);
        Check("stored composition is reachable through the actual Results screen",
            WalkTree(results).OfType<FamilyCompositionChart>().Count() == 1
            && WalkTree(results).OfType<FamilyAbundanceDiversityChart>().Count() == 1);
        string exported = Path.Combine(folder, "results-export");
        results.ViewModel.ExportKind = ResultsExportKind.Figures;
        results.ViewModel.Export(exported);
        Check("Results PNG export includes the same new composition and diversity charts",
            File.Exists(Path.Combine(exported, "family_composition.png"))
            && File.Exists(Path.Combine(exported, "abundance_diversity.png")));
        var pooled = new FamilySubclassPartsResult { Families = new()
        {
            new() { FamilyUserId = 1, FamilySignal = 60, Parts = new()
            { new() { PartKey = "a", Label = "Part", Signal = 30, ShareOfFamily = .5 },
              new() { IsUnresolved = true, Label = "Subclass not resolved", Signal = 30, ShareOfFamily = .5 } } },
            new() { FamilyUserId = 2, FamilySignal = 40, Parts = new()
            { new() { PartKey = "b", Label = "Part", Signal = 40, ShareOfFamily = 1 } } }
        } };
        var pooledData = StoredCompositionChartData.FromSubclassParts(pooled);
        Check("pooled subclass data preserves its own denominator rather than borrowing the run mean",
            pooledData.Families[0].MeanPercent == 60 && pooledData.Families[0].Subclasses[0].Percent == 30
            && !pooledData.IsMeanOfRuns && adapted.Families[0].MeanPercent == 70);
        pooled.Families[0].Parts[0].Signal = 29;
        Check("corrupt stored subclass partitions are refused instead of normalized into closure",
            Throws<ArgumentException>(() => StoredCompositionChartData.FromSubclassParts(pooled)));
        var data = CompositionFixture();
        var chart = new FamilyAbundanceDiversityChart(data);
        Check("scatter x values exactly equal the composition drawing/export values",
            chart.Points.All(p => p.SharePercent == data.Families.Single(r => r.Key == p.Key).MeanPercent));
        Check("scatter y uses minimum protein count rather than count of unique peptides",
            chart.Points.Select(p => p.ProteinCount).SequenceEqual(new[] { 4, 8 }));
        Check("unavailable count is not plotted as zero diversity",
            chart.UnavailableFamilies.Select(r => r.Key).SequenceEqual(new[] { "c" }));
        CheckChartNative("abundance_diversity.png", () => new FamilyAbundanceDiversityChart(data), folder, 540);
        var coincident = new FamilyCompositionData(Enumerable.Range(0, 30).Select(i =>
            new FamilyCompositionValue("f" + i, "Family " + i, 10, null, 1,
                Array.Empty<FamilySubclassValue>(), 3, 1)), "Synthetic coincident points");
        CheckChartNative("coincident.png", () => new FamilyAbundanceDiversityChart(coincident), folder,
            FamilyAbundanceDiversityChart.RecommendedHeight(coincident));
        Check("coincident families retain exact coordinates without jitter or silent dropping",
            new FamilyAbundanceDiversityChart(coincident).Points.Count == 30
            && new FamilyAbundanceDiversityChart(coincident).Points.All(p => p.SharePercent == 10 && p.ProteinCount == 3));
    }
}
