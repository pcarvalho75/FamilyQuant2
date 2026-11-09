using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SimpleGroupingCommands() =>
    [
        new("--test-simple-grouping", 70, TestCommandPhase.Raw,
            args => args.Contains("--test-simple-grouping", StringComparer.Ordinal), (args, _) =>
            {
                SimpleGroupingTests(args.Length == 2 ? args[1] : null);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            })
    ];

    private static void SimpleGroupingTests(string? output = null)
    {
        EnsureApplication();
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (Width: 1180, Height: 740), (Width: 1366, Height: 820) })
            {
                Themes.ThemeManager.Apply(theme);
                var input = ReviewLayoutFixture() with { PanelLinks = Array.Empty<PanelFamilyLinkProposal>() };
                var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null,
                    "", "", KeywordMatch.All, null, null), input, _ => input, evidenceMode: true, hasFamilyList: true,
                    discoverTask: _ => Task.FromResult(new EvidenceTaskScopeInput("Fixture clade", Array.Empty<ReviewTaskTaxon>(),
                        Array.Empty<ReviewTaskKeyword>(), 0, new[] { "Fixture scope" }, "fixture")))
                {
                    Width = size.Width, Height = size.Height, Left = -30000, Top = -30000,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize
                };
                try
                {
                    window.Show(); PumpDispatcher(); window.UpdateLayout();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    var grid = (DataGrid)window.FindName("evidenceGrid");
                    var tabs = (TabControl)window.FindName("evidenceTabs");
                    var switchView = (CheckBox)window.FindName("ShowAllOptionsCheckBox");
                    var checklist = (Button)window.FindName("OpenFamilyChecklistButton");
                    string label = theme + " " + size.Width + "x" + size.Height;
                    Check(label + " opens Simple with only the four requested protein columns", !window.ShowAllOptions
                        && switchView.IsChecked == false && model.ModeIndex == 1
                        && grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex)
                            .Select(c => c.Header as string).SequenceEqual(new[] { "Use", "Accession", "Description", "Flags" })
                        && tabs.Items.OfType<TabItem>().Count(t => t.Visibility == Visibility.Visible) == 1);
                    int visibleRows = WalkTree(grid).OfType<DataGridRow>().Count(row => row.IsVisible && row.ActualHeight > 0
                        && row.TranslatePoint(new Point(), grid).Y >= 0
                        && row.TranslatePoint(new Point(0, row.ActualHeight), grid).Y <= grid.ActualHeight - 1);
                    Check(label + " Simple shows at least eight whole protein rows and full details pane", visibleRows >= 8
                        && ((TextBox)window.FindName("proteinDetails")).ActualHeight >= 75, "rows=" + visibleRows + "; table=" + grid.ActualHeight);
                    Check(label + " Simple offers Family checklist in one click and says so in status", checklist.IsVisible
                        && ((TextBlock)window.FindName("applyHint")).Text.StartsWith("Family checklist is one click away.", StringComparison.Ordinal));
                    model.Evidence!.Proteins[1].Selected = false;
                    model.Evidence.Proteins[2].Selected = true;
                    model.Evidence.Families[0].Name = "Chosen family name";
                    string before = JsonSerializer.Serialize(model.Evidence.CaptureSelection());
                    string scope = model.ScopeText;
                    if (output != null)
                    {
                        Directory.CreateDirectory(output);
                        ScreenHarness.Save(window, Path.Combine(output, theme + "-" + size.Width + "x" + size.Height + "-simple.png"), size.Width, size.Height);
                    }
                    switchView.IsChecked = true; PumpDispatcher(); window.UpdateLayout();
                    string[] advancedTabs = { "Family checklist", "Panel links", "Architecture", "Left out", "Unassigned", "Markers", "Motif counts" };
                    Check(label + " Show all options restores every advanced tab and full columns", window.ShowAllOptions
                        && grid.Columns.All(c => c.Visibility == Visibility.Visible)
                        && advancedTabs.All(header => tabs.Items.OfType<TabItem>().Any(t => t.Header as string == header && t.Visibility == Visibility.Visible))
                        && ((TabControl)window.FindName("ReviewModeTabs")).Items.OfType<TabItem>().First().Visibility == Visibility.Visible);
                    var advancedButtons = WalkTree(window).OfType<Button>().Where(button => button.IsVisible && button.Content is string text
                        && text is "Untick flagged" or "Align selected" or "Add to family" or "Remove from family").ToArray();
                    Check(label + " advanced protein actions and motif remain reachable", advancedButtons.Length == 4
                        && advancedButtons.All(button => button.IsVisible) && ((TextBlock)window.FindName("MotifLabel")).IsVisible,
                        string.Join(", ", advancedButtons.Select(button => button.Content + ":" + button.IsVisible)) + "; motif=" + ((TextBlock)window.FindName("MotifLabel")).IsVisible);
                    tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => tab.Header as string == "Unassigned");
                    PumpDispatcher(); window.UpdateLayout();
                    Check(label + " full view keeps hand-placement actions reachable on Unassigned", ((Button)window.FindName("addUnassignedFamilyButton")).IsVisible);
                    tabs.SelectedIndex = 0; PumpDispatcher(); window.UpdateLayout();
                    if (output != null) ScreenHarness.Save(window, Path.Combine(output, theme + "-" + size.Width + "x" + size.Height + "-full.png"), size.Width, size.Height);
                    switchView.IsChecked = false; PumpDispatcher(); window.UpdateLayout();
                    Check(label + " returning to Simple hides extra actions and preserves all pending choices",
                        advancedButtons.All(button => !button.IsVisible) && model.ScopeText == scope
                        && JsonSerializer.Serialize(model.Evidence.CaptureSelection()) == before);
                    checklist.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher(); window.UpdateLayout();
                    Check(label + " checklist button opens the actual checklist tab without changing choices", window.ShowAllOptions
                        && tabs.SelectedItem is TabItem { Header: "Family checklist" }
                        && JsonSerializer.Serialize(model.Evidence.CaptureSelection()) == before);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }
}
