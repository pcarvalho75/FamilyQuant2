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
