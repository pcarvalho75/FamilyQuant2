using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsSimplerCommands() =>
    [new("--test-results-percent-only", 84, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-results-percent-only", (args, _) =>
        {
            EnsureApplication();
            ResultsPercentOnlyTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "results-percent-only"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ResultsPercentOnlyTests(string output)
    {
        Directory.CreateDirectory(output);
        var model = new AnalyticsViewModel(SyntheticAnalyticsRows(), new QuantManifest
            { PerRunTic = new() { ["ui_01"] = 5e8, ["ui_02"] = 4e8 } }, BuildSyntheticFcf());
        var control = new AnalyticsControl(model);
        Check("Results removes the percent checkbox", control.FindName("CheckBoxFamilyPercent") == null);
        Check("Results always uses percent even through the retained compatibility property", model.FamilyAsPercent);
        model.FamilyAsPercent = false;
        var chart = WalkTree(control).OfType<FamilyContributionChart>().Single();
        Check("Results chart stays in percent mode", control.FamilyAsPercent && chart.IsPercentOfTotal);
        var top = (ComboBox)control.FindName("ComboBoxFamilyTopN");
        top.SelectedIndex = 0;
        Check("Top N changes keep percent mode", model.FamilyTopN == 10
            && WalkTree(control).OfType<FamilyContributionChart>().Single().IsPercentOfTotal);
        var small = new FamilyContributionBar { Identified = 0.1, DeNovo = 0.02, Label = "small family" };
        var tinyChart = new FamilyContributionChart(new[] { small }, true, 100);
        string label = tinyChart.BarValueLabel(small);
        Check("a small bar prints both identified and de novo percentages beside it",
            label.Contains("ID ", StringComparison.Ordinal) && label.Contains("de novo ", StringComparison.Ordinal)
            && label.Contains((small.Identified / 100).ToString("P1", CultureInfo.CurrentCulture), StringComparison.Ordinal)
            && label.Contains((small.DeNovo / 100).ToString("P1", CultureInfo.CurrentCulture), StringComparison.Ordinal));
        var zero = new FamilyContributionBar { Identified = 0, DeNovo = 5 };
        Check("zero identified signal still has its percentage", tinyChart.BarValueLabel(zero).Contains(
            "ID " + 0d.ToString("P1", CultureInfo.CurrentCulture), StringComparison.Ordinal));
        var window = new Window { Content = control, Width = 1180, Height = 820 };
        try { ScreenHarness.Save(window, Path.Combine(output, "results-percent-only.png"), 1180, 820); }
        finally { window.Close(); }
        ChartImageExporter.Save(tinyChart, Path.Combine(output, "small-parts.png"), 1180, 420);
    }
}
