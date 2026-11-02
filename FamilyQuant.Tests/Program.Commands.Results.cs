using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsCommandTable() =>
    [
        new("--test-chart-themes", 440, TestCommandPhase.Raw, ChartThemesRequested, RunChartThemes),
    ];

    private static bool ChartThemesRequested(string[] args) => args.Contains("--test-chart-themes", StringComparer.Ordinal);

    private static int RunChartThemes(string[] args, string workDir)
    {
        string chartWork = args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.ChartThemes");
        ChartThemeTests(chartWork);
        UiChartPaletteTests(chartWork);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
