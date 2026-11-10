using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsTopNCommands() =>
    [new("--test-results-topn-placement", 84, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-results-topn-placement", (args, _) =>
        {
            EnsureApplication();
            ResultsTopNPlacementTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "results-topn"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ResultsTopNPlacementTests(string output)
    {
        Directory.CreateDirectory(output);
        var control = new AnalyticsControl(SyntheticAnalyticsRows(), new QuantManifest
            { DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method,
                PerRunTic = new() { ["ui_01"] = 5e8, ["ui_02"] = 4e8 } }, BuildSyntheticFcf());
        var window = new Window { Content = control, Width = 1180, Height = 820 };
        try
        {
            ScreenHarness.Save(window, Path.Combine(output, "results-topn.png"), 1180, 820);
            var top = (ComboBox)control.FindName("ComboBoxFamilyTopN");
            var chart = WalkTree(control).OfType<FamilyContributionChart>().Single();
            Point topAt = top.TranslatePoint(new Point(0, 0), control);
            Point chartAt = chart.TranslatePoint(new Point(0, 0), control);
            Check("Top N sits immediately above its family chart", chartAt.Y >= topAt.Y + top.ActualHeight
                && chartAt.Y - topAt.Y - top.ActualHeight <= 12,
                $"Top N bottom={topAt.Y + top.ActualHeight}; chart top={chartAt.Y}");
            Check("the family chart is visible at the start of Results", chartAt.Y < 340
                && chartAt.Y + chart.ActualHeight <= 820);
            Check("other Results figures remain available", WalkTree(control).OfType<RecoveryChart>().Count() == 1
                && WalkTree(control).OfType<XicMassChart>().Count() == 1
                && WalkTree(control).OfType<IsotopeCountChart>().Count() == 1
                && WalkTree(control).OfType<PercentIonCurrentChart>().Count() == 1);
            var host = (StackPanel)control.FindName("PanelFamilyChart");
            top.SelectedIndex = 4;
            Check("changing Top N rebuilds the dedicated family chart host", host.Children.Count == 1
                && WalkTree(host).OfType<FamilyContributionChart>().Count() == 1 && control.FamilyTopN == 0);
        }
        finally { window.Close(); }
    }
}
