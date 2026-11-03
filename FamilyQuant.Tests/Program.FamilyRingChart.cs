using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [LookExportCultureCase]
    private static IEnumerable<AnalyticsFigure> LookRingCultureCases() => new[]
    {
        FamilyCompositionFigures.BuildRing(LookCultureData(true), new FamilyRingGrouping(new[] { "a" }, 15.25))
    };

    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyRingCommands() => new[]
    {
        new TestCommand("--test-family-ring", 10000, TestCommandPhase.Normalized,
            args => args.Contains("--test-family-ring", StringComparer.Ordinal), (_, work) =>
            {
                FamilyRingTests(Path.Combine(work, "family-ring"));
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            })
    };

    private static void FamilyRingTests(string folder)
    {
        Section("Family/subclass ring - pooled closure and retained small families");
        EnsureApplication(); Directory.CreateDirectory(folder);
        var source = CompositionFixture();
        var pooled = new FamilyCompositionData(source.Families, "Share of credited family signal", false);
        var grouping = new FamilyRingGrouping(new[] { "c" }, 15);
        var ring = new FamilyRingChart(pooled, grouping);
        Check("both rings sum to 100 percent on the same pooled basis",
            Math.Abs(ring.Families.Sum(f => f.Percent) - 100) < 1e-9
            && Math.Abs(ring.Families.SelectMany(f => f.Parts).Sum(p => p.Percent) - 100) < 1e-9);
        Check("small families join Others and retain their original members",
            ring.Families.Count == 3 && ring.Families.Single(f => f.IsOthers).Members.Single().Key == "c"
            && ring.Families.Single(f => f.IsOthers).Percent == 10);
        Check("unresolved subclass signal stays explicitly unresolved",
            ring.Families[0].Parts.Any(p => p.Label == "Subclass not resolved" && p.Percent == 25));
        Check("ring obeys supplied membership even when pooled shares cross the threshold differently",
            new FamilyRingChart(pooled, new FamilyRingGrouping(new[] { "a" }, 15)).Families.Single(f => f.IsOthers).Percent == 60);
        Check("ring rejects non-closing family shares instead of normalizing them to 100",
            Throws<ArgumentException>(() => new FamilyRingChart(new FamilyCompositionData(
                new[] { source.Families[0] }, "Incomplete pooled data", false), new FamilyRingGrouping(Array.Empty<string>()))));
        Check("ring refuses mean-of-run shares, so it cannot mix bases",
            Throws<ArgumentException>(() => new FamilyRingChart(source, grouping)));
        CheckChartNative("family_subclass_ring.png", () => new FamilyRingChart(pooled, grouping), folder,
            FamilyRingChart.RecommendedHeight(pooled, grouping));
        var allGrouping = new FamilyRingGrouping(pooled.Families.Select(f => f.Key), 100);
        var allOthers = new FamilyRingChart(pooled, allGrouping);
        Check("a single full-circle Others group keeps all input signal and family membership",
            allOthers.Families.Count == 1 && allOthers.Families[0].Percent == 100
            && allOthers.Families[0].Members.Count == 3);
        CheckChartNative("all-others.png", () => new FamilyRingChart(pooled, allGrouping), folder,
            FamilyRingChart.RecommendedHeight(pooled, allGrouping));
        var fractional = new FamilyCompositionData(new[]
        {
            source.Families[0] with { MeanPercent = 60.125, Subclasses = new[]
                { new FamilySubclassValue("a1", "Subclass 1", 35.125), new FamilySubclassValue("", "Subclass not resolved", 25) } },
            source.Families[1] with { MeanPercent = 29.875, Subclasses = new[] { new FamilySubclassValue("b1", "Subclass 2", 29.875) } },
            source.Families[2]
        }, "Fractional pooled fixture", false);
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        byte[]? previous = null;
        try
        {
            foreach (string locale in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                byte[] bytes = FamilyCompositionFigures.RenderPng(FamilyCompositionFigures.BuildRing(fractional, grouping));
                File.WriteAllBytes(Path.Combine(folder, "report-" + locale + ".png"), bytes);
                if (previous != null) Check("report ring PNG bytes remain identical across decimal-point/comma cultures", previous.SequenceEqual(bytes));
                Check("report export restores the caller's display culture", CultureInfo.CurrentCulture.Name == locale && CultureInfo.CurrentUICulture.Name == locale);
                previous = bytes;
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; CultureInfo.CurrentUICulture = originalUiCulture; }
    }
}
