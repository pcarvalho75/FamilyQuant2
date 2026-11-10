using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ReviewTooltipCorrectionTests()
    {
        Section("Z12q9 review tooltip corrections follow the native workflow");
        string output = Path.Combine(Path.GetTempPath(), "FamilyQuant.ReviewTooltipCorrections");
        Directory.CreateDirectory(output);
        var originalTheme = Themes.ThemeManager.Current;
        List<FastaItemExtended> Items() => Enumerable.Range(0, 4).Select(index => new FastaItemExtended
        { SequenceIdentifier = "fixture-" + index, Sequence = "PEPTIDEK", Description = "signal remove", UserID = index / 2, USearchID = index / 2 }).ToList();
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var unopened = new SuperClusterPreviewWindow(new SuperClusterPreview(Items(), null, "signal", "", KeywordMatch.All, null, null));
                try { CheckTooltipCoverage("Z12h2c2 " + theme + " unopened grouping review", (FrameworkElement)unopened.Content); }
                finally { unopened.Close(); }
                int reads = 0;
                var facts = new EvidenceTaskScopeInput("Synthetic clade", Array.Empty<ReviewTaskTaxon>(), Array.Empty<ReviewTaskKeyword>(),
                    1, new[] { "Synthetic activity" }, "fixture");
                var preview = new SuperClusterPreview(Items(), null, "", "", KeywordMatch.All, null, null);
                var window = new SuperClusterPreviewWindow(preview, null, _ => new EvidenceReviewInput("fixture", Array.Empty<ReviewProteinInput>(),
                    Array.Empty<ReviewFamilyInput>(), Array.Empty<ReviewMarkerInput>()),
                    discoverTask: _ => { reads++; return Task.FromResult(facts); })
                { Width = 1024, Height = 768, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.ShowAllOptions = true;
                    window.Show(); PumpDispatcher(); window.UpdateLayout();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    TextBox Box(string path) => WalkTree(window).OfType<TextBox>().Single(box => box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path == path);
                    var keywords = Box("KeywordsText"); var exclude = Box("ExcludeText"); var scope = (TextBox)window.FindName("TaskScopeBox");
                    var rule = WalkTree(window).OfType<ComboBox>().Single(box => box.GetBindingExpression(ComboBox.SelectedIndexProperty)?.ParentBinding.Path?.Path == "MatchIndex");
                    Check("Z12q9 " + theme + " fresh keyword inputs and tips name empty defaults and the last applied review",
                        keywords.Text.Length == 0 && exclude.Text.Length == 0 && rule.SelectedIndex == 0
                        && new[] { keywords.ToolTip, exclude.ToolTip, rule.ToolTip }.OfType<string>().All(tip => tip.Contains("last applied", StringComparison.OrdinalIgnoreCase)
                            && !tip.Contains("setup", StringComparison.OrdinalIgnoreCase))
                        && ((string)keywords.ToolTip).Contains("empty", StringComparison.OrdinalIgnoreCase)
                        && ((string)rule.ToolTip).Contains("all words", StringComparison.OrdinalIgnoreCase));
                    Check("Z12q9 " + theme + " the empty scope tip names Read database and scope rather than nonexistent setup fields",
                        scope.Text.Length == 0 && scope.ToolTip is string scopeTip && scopeTip.Contains("empty", StringComparison.OrdinalIgnoreCase)
                        && scopeTip.Contains("Read database and scope", StringComparison.Ordinal) && !scopeTip.Contains("setup", StringComparison.OrdinalIgnoreCase));
                    var tabs = WalkTree(window).OfType<TabItem>().Where(tab => tab.Header as string is "Keywords" or "Evidence").ToArray();
                    var evidence = tabs.Single(tab => tab.Header as string == "Evidence");
                    Check("Z12q9 " + theme + " both mode tips explain the first Evidence switch can read UniProt over the internet",
                        evidence.IsEnabled && tabs.All(tab => tab.ToolTip is string tip && tip.Contains("internet", StringComparison.OrdinalIgnoreCase)
                            && tip.Contains("Read database and scope", StringComparison.Ordinal) && !tip.Contains("keyword-only", StringComparison.OrdinalIgnoreCase)));
                    var grid = (DataGrid)window.FindName("grid");
                    Check("Z12q9 " + theme + " each keyword header has one code-backed tooltip and no unused XAML copy",
                        grid.Columns.Count == 7 && grid.Columns.All(column => Themes.TableAppearance.GetHeaderToolTip(column) == null
                            && HeaderTip(column.HeaderStyle).Length > 0));
                    ScreenHarness.Save(window, Path.Combine(output, "keywords-" + theme + ".png"), 1024, 768);
                    Check("Z12q9 " + theme + " keyword mode does not read the scope before switching", reads == 0 && !model.HasTaskScope);
                    model.ModeIndex = 1; PumpDispatcher();
                    FinishReviewWork(async () =>
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(10);
                        while ((model.IsBusy || window.Operations.IsBusy) && DateTime.UtcNow < deadline) await Task.Delay(10);
                    });
                    window.UpdateLayout(); PumpDispatcher();
                    Check("Z12q9 " + theme + " the bound first Evidence switch reads the supplied scope once without applying grouping",
                        evidence.IsSelected && reads == 1 && model.HasTaskScope && model.ScopeText.Contains("Synthetic activity", StringComparison.Ordinal) && !window.Confirmed,
                        "reads=" + reads + "; busy=" + model.IsBusy + "; " + model.Status);
                    tabs.Single(tab => tab.Header as string == "Keywords").IsSelected = true; PumpDispatcher();
                    evidence.IsSelected = true; PumpDispatcher();
                    Check("Z12q9 " + theme + " returning to a loaded Evidence view does not repeat discovery", reads == 1);
                    ScreenHarness.Save(window, Path.Combine(output, "evidence-" + theme + ".png"), 1024, 768);
                }
                finally { window.Close(); PumpDispatcher(); }

                var cases = new (string Name, List<FastaItemExtended> Proteins, ISet<int>? Search, string Keywords, string Exclude, string Reason)[]
                {
                    ("no keywords", Items(), null, "", "", "Type at least one keyword"),
                    ("one whole family", Items().Where(row => row.UserID == 0).ToList(), null, "signal", "", "Only one whole family"),
                    ("Search hides matches", Items(), new HashSet<int> { 99 }, "signal", "", "hidden by the Search box"),
                    ("exclusions", Items(), null, "signal", "remove", "exclude words"),
                    ("no matching families", Items(), null, "unmatched", "", "No family matches")
                };
                foreach (var item in cases)
                {
                    var file = new FamilyClusterFile { MyExtendedFastaItems = item.Proteins };
                    string before = file.ToJsonText();
                    var candidate = new SuperClusterPreview(item.Proteins, item.Search, item.Keywords, item.Exclude, KeywordMatch.All, null, null);
                    var review = new SuperClusterPreviewWindow(candidate)
                    { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
                    try
                    {
                        review.ShowAllOptions = true;
                        review.Show(); PumpDispatcher();
                        var model = (GroupingReviewViewModel)review.DataContext;
                        var apply = (Button)review.FindName("createButton");
                        Check("Z12q9 " + theme + " disabled Apply explains " + item.Name + " using the actual keyword reason",
                            !apply.IsEnabled && model.Keyword.Headline.Contains(item.Reason, StringComparison.Ordinal)
                            && apply.ToolTip as string == model.Keyword.Headline && file.ToJsonText() == before, apply.ToolTip as string);
                    }
                    finally { review.Close(); PumpDispatcher(); }
                }
            }
        }
        finally { Themes.ThemeManager.Apply(originalTheme); }

        static string HeaderTip(Style? style)
        {
            for (var current = style; current != null; current = current.BasedOn)
                if (current.Setters.OfType<Setter>().FirstOrDefault(setter => setter.Property == FrameworkElement.ToolTipProperty)?.Value is string tip) return tip;
            return "";
        }
    }
}
