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
    private static IEnumerable<TestCommand> GroupingPanelOrderCommands() =>
    [new("--test-grouping-panel-order", 100, TestCommandPhase.Raw, args => args is ["--test-grouping-panel-order"], (_, _) =>
    {
        GroupingPanelOrderTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void GroupingPanelOrderTests()
    {
        Section("PO1 Quantify follows both ways of grouping");
        EnsureApplication();
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "fq2", "FQDeNovo", "v2118", "PO1", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var file = ManualChoiceFile();
                var control = new ClusterGenerationControl(file);
                string before = file.ToJsonText();
                var window = new Window { Content = control, Width = 1366, Height = 768, Left = -30000, Top = -30000,
                    WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    string[] parts = { "TextBlockPartGroupByEvidence", "ButtonImportFamilyList", "ButtonFetchDomains", "ButtonEvidenceGrouping",
                        "TextBlockPartGroupByWords", "ButtonSuperCluster", "ButtonWordCluster", "ButtonComputeFullXic" };
                    static IEnumerable<FrameworkElement> Elements(DependencyObject node)
                    {
                        if (node is FrameworkElement element) yield return element;
                        foreach (object child in LogicalTreeHelper.GetChildren(node))
                            if (child is DependencyObject next) foreach (var inner in Elements(next)) yield return inner;
                    }
                    Check(theme + " PO1 evidence, optional words and Quantify are in working order",
                        Elements(control).Select(element => element.Name).Where(parts.Contains).SequenceEqual(parts)
                        && ((TextBlock)control.FindName("TextBlockPartGroupByWords")).Text == "b Group by words (optional)");
                    var quantify = (Button)control.FindName("ButtonComputeFullXic");
                    var words = (Expander)control.FindName("WordGroupingActions");
                    Check(theme + " PO1 Quantify keeps its command and is below both grouping sections",
                        quantify.Command == MainWindow.NavigateShellCommand && Equals(quantify.CommandParameter, ShellDestination.Quantify)
                        && quantify.TranslatePoint(new Point(), window).Y >= words.TranslatePoint(new Point(), window).Y + words.ActualHeight);
                    ScreenHarness.Save(window, Path.Combine(folder, theme.ToString().ToLowerInvariant() + ".png"), 1366, 768);
                    words.IsExpanded = true; PumpDispatcher();
                    var wordButton = (Button)control.FindName("ButtonWordCluster");
                    Check(theme + " PO1 expanded word settings and its action still precede Quantify",
                        wordButton.IsVisible && wordButton.TranslatePoint(new Point(), window).Y < quantify.TranslatePoint(new Point(), window).Y);
                    words.IsExpanded = false;
                    Check(theme + " PO1 viewing the reordered panel changes no stored values", file.ToJsonText() == before);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
        Check("PO1 Help puts optional word grouping before Quantify",
            HelpContent.GroupingOrder.IndexOf("b Group by words (optional)", StringComparison.Ordinal)
                < HelpContent.GroupingOrder.IndexOf("\nQuantify:", StringComparison.Ordinal));
        Console.WriteLine("PO1 renders: " + folder);
    }
}
