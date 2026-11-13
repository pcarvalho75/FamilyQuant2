using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyWorldOptionalCommands() =>
    [new("--test-family-world-optional", 100, TestCommandPhase.Raw, args => args is ["--test-family-world-optional"], (_, _) =>
    {
        FamilyWorldOptionalTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void FamilyWorldOptionalTests()
    {
        Section("PO2 optional family relatives after word grouping");
        EnsureApplication();
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "fq2", "FQDeNovo", "v2118", "PO2", Guid.NewGuid().ToString("N"));
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
                    var words = (Expander)control.FindName("WordGroupingActions");
                    var fetch = (Button)control.FindName("ButtonFetchFamilyWorld");
                    var quantify = (Button)control.FindName("ButtonComputeFullXic");
                    Check(theme + " PO2 optional family download follows words and precedes Quantify",
                        Equals(fetch.Content, "Fetch UniProt families (optional)")
                        && fetch.TranslatePoint(new Point(), window).Y >= words.TranslatePoint(new Point(), window).Y + words.ActualHeight
                        && quantify.TranslatePoint(new Point(), window).Y >= fetch.TranslatePoint(new Point(), window).Y + fetch.ActualHeight);
                    string tooltip = fetch.ToolTip.ToString()!;
                    Check(theme + " PO2 tooltip separates optional Database gap relatives from the own-family evidence",
                        TooltipSentenceCount(tooltip) <= 2 && tooltip.Contains("reviewed relatives", StringComparison.Ordinal)
                        && tooltip.Contains("Database gap estimate", StringComparison.Ordinal)
                        && tooltip.Contains("Grouping does not need", StringComparison.Ordinal)
                        && tooltip.Contains("own UniProt family already comes with Fetch evidence", StringComparison.Ordinal));
                    ScreenHarness.Save(window, Path.Combine(folder, theme.ToString().ToLowerInvariant() + ".png"), 1366, 768);
                    int offers = 0;
                    control.ConfirmFamilyWorldGroupingOfferOverride = () => { offers++; return false; };
                    fetch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    FinishReviewWork(() => control.FamilyWorldFetchCompletion);
                    Check(theme + " PO2 the moved button retains its task/scope offer and cancellation leaves stored values equal",
                        offers == 1 && file.ToJsonText() == before && !control.Operations.IsBusy);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
        string help = HelpContent.GroupingOrder;
        int wordsAt = help.IndexOf("b Group by words (optional)", StringComparison.Ordinal);
        int relativesAt = help.IndexOf("Fetch UniProt families (optional)", StringComparison.Ordinal);
        Check("PO2 Help names the optional download in working order and explains the own-family evidence",
            wordsAt >= 0 && relativesAt > wordsAt && help.IndexOf("\nQuantify:", StringComparison.Ordinal) > relativesAt
            && help.Contains("Grouping does not need this download", StringComparison.Ordinal)
            && help.Contains("each protein's own UniProt family", StringComparison.Ordinal));
        Console.WriteLine("PO2 renders: " + folder);
    }
}
