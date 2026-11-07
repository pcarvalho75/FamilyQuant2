using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void PanelLinkChoiceFilterTests()
    {
        Section("Z12p5h unresolved panel links remain findable");
        string output = Path.Combine(Path.GetTempPath(), "FamilyQuant.PanelLinkChoiceFilter");
        Directory.CreateDirectory(output);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var file = new FamilyClusterFile { MyExtendedFastaItems = new()
                {
                    new() { SequenceIdentifier = "fixture-entry", Description = "Synthetic protein", Sequence = "PEPTIDEK", UserID = 7, USearchID = 7 }
                } };
                string before = file.ToJsonText();
                var links = new List<PanelFamilyLinkProposal>();
                for (int index = 0; index < 45; index++)
                {
                    links.Add(new() { Key = "resolved-" + index, SourceFamily = "Resolved source " + index,
                        CandidateFamilyKeys = index % 2 == 0 ? new[] { "a" } : Array.Empty<string>(), SelectedFamilyKey = index % 2 == 0 ? "a" : null });
                    if (index is 5 or 22 or 40) links.Add(new() { Key = "choice-" + index, SourceFamily = "Unresolved source " + index,
                        CandidateFamilyKeys = new[] { "a", "b" } });
                }
                var input = new EvidenceReviewInput("synthetic-links",
                    new[] { new ReviewProteinInput(0, "fixture-entry", 7, "a", "fixture", "Synthetic proposal", true, "", Array.Empty<string>(), true) },
                    new[] { new ReviewFamilyInput("a", "Family a", 1, 1) }, Array.Empty<ReviewMarkerInput>(),
                    PanelLinks: links, PanelNames: new Dictionary<string, string> { ["a"] = "Family a", ["b"] = "Family b" },
                    PanelChecklist: new[] { new PanelChecklistFamily("a", "Family a", "Quantify", null, Array.Empty<PanelChecklistProtein>()) });
                EvidenceReviewInput Confirm(IReadOnlyList<PanelFamilyLinkSelection> choices)
                {
                    var selected = choices.ToDictionary(choice => choice.LinkKey, StringComparer.Ordinal);
                    input = input with { PanelLinks = input.PanelLinks!.Select(link => selected.TryGetValue(link.Key, out var choice)
                        ? new PanelFamilyLinkProposal { Key = link.Key, SourceFamily = link.SourceFamily, CandidateFamilyKeys = link.CandidateFamilyKeys,
                            SelectedFamilyKey = choice.PanelFamilyKey, IsConfirmed = true, SourceNote = choice.Source }
                        : link).ToArray() };
                    return input;
                }
                var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
                var window = new SuperClusterPreviewWindow(preview, input, _ => input, initialScope: "scope", evidenceMode: true,
                    confirmPanelLinks: Confirm, confirmPanelMatches: (_, _) => input)
                { Width = 1024, Height = 768, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    var tab = WalkTree(window).OfType<TabItem>().Single(item => item.Header as string == "Panel links");
                    tab.IsSelected = true; PumpDispatcher(); window.UpdateLayout();
                    var filter = (CheckBox)window.FindName("PanelLinksNeedsChoiceFilter");
                    var grid = (DataGrid)window.FindName("panelLinksGrid");
                    var match = (Button)window.FindName("MatchPanelNamesButton");
                    const string enabledHint = "Open stored evidence-label suggestions for the selected panel family, then review and confirm labels used by the checklist. Disabled without an editable current panel proposal and selected family; Update proposal after scope edits, select a family and wait for active operations.";
                    Check("Z12p5h " + theme + " all links start visible with an accurate unresolved count and unresolved links first",
                        filter.IsChecked == false && filter.Content as string == "Needs a choice (3)" && grid.Items.Count == links.Count
                        && grid.Items.Cast<PanelLinkReviewRow>().Take(3).All(row => row.Source.NeedsConfirmation)
                        && !model.CanConfirm && model.CanMatchPanelNames && match.ToolTip as string == enabledHint);
                    grid.Items.SortDescriptions.Add(new SortDescription("Source.NeedsConfirmation", ListSortDirection.Ascending)); PumpDispatcher();
                    bool resolvedFirst = !((PanelLinkReviewRow)grid.Items[0]).Source.NeedsConfirmation;
                    grid.Items.SortDescriptions.Clear();
                    grid.Items.SortDescriptions.Add(new SortDescription("Source.NeedsConfirmation", ListSortDirection.Descending)); PumpDispatcher();
                    Check("Z12p5h " + theme + " the native link view sorts unresolved status in either direction",
                        resolvedFirst && ((PanelLinkReviewRow)grid.Items[0]).Source.NeedsConfirmation);
                    grid.Items.SortDescriptions.Clear();
                    filter.SetCurrentValue(CheckBox.IsCheckedProperty, true); PumpDispatcher();
                    Check("Z12p5h " + theme + " native filter isolates every unresolved link among many resolved links without changing the project",
                        grid.Items.Count == 3 && grid.Items.Cast<PanelLinkReviewRow>().All(row => row.Source.NeedsConfirmation)
                        && model.Evidence!.PanelLinks.Count == links.Count && file.ToJsonText() == before);
                    Check("Z12p5h " + theme + " User choice offers sorting by unresolved status",
                        grid.Columns[1].CanUserSort && grid.Columns[1].SortMemberPath == "Source.NeedsConfirmation");
                    ScreenHarness.Save(window, Path.Combine(output, "needs-choice-" + theme + ".png"), 1024, 768);
                    var first = grid.Items.Cast<PanelLinkReviewRow>().First();
                    grid.ScrollIntoView(first); PumpDispatcher();
                    var choice = WalkTree(grid).OfType<ComboBox>().First(combo => ReferenceEquals(combo.DataContext, first));
                    choice.SelectedItem = first.Options.First(option => option.Key == "a"); PumpDispatcher();
                    Check("Z12p5h " + theme + " a staged native choice stays visible until Use link choices and explains disabled matching",
                        grid.Items.Count == 3 && filter.Content as string == "Needs a choice (3)" && !match.IsEnabled
                        && match.ToolTip is string hint && hint.Contains("panel link choices", StringComparison.Ordinal)
                        && hint.Contains("Use link choices", StringComparison.Ordinal) && !model.CanConfirm);
                    first.Note = "Synthetic review note";
                    foreach (var row in model.Evidence!.PanelLinks.Where(row => row.Source.NeedsConfirmation))
                        row.SelectedOption = row.Options.First(option => option.Key == "a");
                    FinishReviewWork(model.UpdatePanelLinksAsync); PumpDispatcher(); window.UpdateLayout();
                    Check("Z12p5h " + theme + " using choices updates the count, preserves the filter and re-enables matching and Apply",
                        filter.IsChecked == true && filter.Content as string == "Needs a choice (0)" && grid.Items.Count == 0
                        && match.IsEnabled && match.ToolTip as string == enabledHint && model.CanConfirm
                        && model.Evidence!.PanelLinks.Single(row => row.Source.Key == first.Source.Key).Note == "Synthetic review note"
                        && file.ToJsonText() == before);
                    ScreenHarness.Save(window, Path.Combine(output, "choices-used-" + theme + ".png"), 1024, 768);
                    filter.SetCurrentValue(CheckBox.IsCheckedProperty, false); PumpDispatcher();
                    Check("Z12p5h " + theme + " clearing the filter restores all resolved links and preserves their chosen family",
                        grid.Items.Count == links.Count && model.Evidence!.PanelLinks.Where(row => row.Source.IsConfirmed)
                            .All(row => row.SelectedOption?.Key == "a") && file.ToJsonText() == before);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
