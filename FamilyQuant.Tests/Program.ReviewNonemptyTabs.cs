using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReviewNonemptyTabCommands() =>
    [new("--test-review-nonempty-tabs", 74, TestCommandPhase.Raw, args => args.Contains("--test-review-nonempty-tabs", StringComparer.Ordinal), (args, _) =>
    {
        ReviewNonemptyTabTests(args.Length == 2 ? args[1] : null);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];

    private static void ReviewNonemptyTabTests(string? output = null)
    {
        EnsureApplication();
        var filled = GroupingReviewScreens.Fixture(12);
        filled = filled with { SourceLabel = "Synthetic review fixture.", PanelLinks = Array.Empty<PanelFamilyLinkProposal>(),
            Proteins = filled.Proteins.Select(row => row with { Description = "Synthetic protein " + row.ProteinIndex }).ToArray(),
            Architectures = filled.Architectures!.Take(1).ToArray() };
        var empty = filled with { Proteins = filled.Proteins.Where(row => row.Family.Length > 0).ToArray(),
            Markers = Array.Empty<ReviewMarkerInput>(), LeftOut = Array.Empty<ReviewLeftOutInput>(),
            Architectures = new[] { new ReviewArchitectureInput(filled.Families[0].Family, false, Array.Empty<ReviewArchitectureGroupInput>()) } };
        string sourceBefore = JsonSerializer.Serialize(filled);
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var current = empty;
                var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null),
                    current, _ => current, motif: _ => current.Families.Count == 0 ? "Pattern: C\nFamily\tMatching proteins\tProteins counted\n" : "Supplied motif count row.",
                    initialScope: "Synthetic scope", evidenceMode: true)
                { Width = 1366, Height = 820, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
                    WindowStyle = WindowStyle.None, ShowAllOptions = true };
                try
                {
                    window.Show(); PumpDispatcher(); window.UpdateLayout();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    var tabs = (TabControl)window.FindName("evidenceTabs");
                    TabItem Tab(string name) => tabs.Items.OfType<TabItem>().Single(tab => tab.Header as string == name);
                    string[] optional = { "Architecture", "Left out", "Unassigned", "Markers", "Motif counts" };
                    string[] basic = { "Proteins", "Panel links", "Family checklist" };
                    Check(theme + " RG11 hides tabs without project rows, including an empty architecture holder",
                        optional.All(name => Tab(name).Visibility == Visibility.Collapsed));
                    Check(theme + " RG11 keeps the three basic tabs under Show all options", basic.All(name => Tab(name).Visibility == Visibility.Visible));
                    void Capture(string name)
                    {
                        if (output == null) return;
                        Directory.CreateDirectory(output);
                        ScreenHarness.Save(window, Path.Combine(output, theme + "-" + name + ".png"), 1366, 820);
                    }
                    Capture("empty");
                    current = filled; FinishReviewWork(model.UpdateProposalAsync); PumpDispatcher();
                    Check(theme + " RG11 proposal rows immediately restore their tabs", optional.Take(4).All(name => Tab(name).Visibility == Visibility.Visible)
                        && Tab("Motif counts").Visibility == Visibility.Collapsed);
                    model.MotifPattern = "C"; FinishReviewWork(model.CheckMotifAsync); PumpDispatcher();
                    Check(theme + " RG11 a checked report restores Motif counts", Tab("Motif counts").Visibility == Visibility.Visible);
                    Capture("filled");
                    model.Evidence!.SearchText = "no matching visible protein";
                    model.Evidence.SelectedFamily = model.Evidence.Families.Last(); PumpDispatcher();
                    Check(theme + " RG11 search and a family without architecture do not hide other project rows", model.Evidence.SelectedArchitecture == null
                        && optional.All(name => Tab(name).Visibility == Visibility.Visible));
                    model.Evidence.Proteins[1].Selected = false; model.Evidence.Families[0].Name = "Chosen output name";
                    string choices = JsonSerializer.Serialize(model.Evidence.CaptureSelection());
                    window.ShowAllOptions = false; PumpDispatcher();
                    Check(theme + " RG11 Simple retains only Proteins", tabs.Items.OfType<TabItem>().Count(tab => tab.Visibility == Visibility.Visible) == 1
                        && Tab("Proteins").Visibility == Visibility.Visible);
                    window.ShowAllOptions = true; PumpDispatcher();
                    Check(theme + " RG11 switching views keeps pending choices and populated tabs", choices == JsonSerializer.Serialize(model.Evidence.CaptureSelection())
                        && optional.All(name => Tab(name).Visibility == Visibility.Visible));
                    tabs.SelectedItem = Tab("Markers"); current = empty;
                    FinishReviewWork(model.UpdateProposalAsync); PumpDispatcher();
                    Check(theme + " RG11 removing rows hides the tabs and retires a stale motif report", optional.All(name => Tab(name).Visibility == Visibility.Collapsed)
                        && tabs.SelectedItem is TabItem { Visibility: Visibility.Visible } && model.MotifReport.Length == 0);
                    current = empty with { PanelLinks = null }; FinishReviewWork(model.UpdateProposalAsync); PumpDispatcher();
                    Check(theme + " RG11 keeps basic tabs even without a panel", basic.All(name => Tab(name).Visibility == Visibility.Visible));
                    current = empty with { Architectures = new[] { new ReviewArchitectureInput(empty.Families[0].Family, false,
                        Array.Empty<ReviewArchitectureGroupInput>(), new[] { new ReviewArchitectureUndeterminedInput(empty.Proteins[0].ProteinIndex, "Supplied placement reason.") }) } };
                    FinishReviewWork(model.UpdateProposalAsync); PumpDispatcher();
                    Check(theme + " RG11 undetermined architecture members also count as rows", Tab("Architecture").Visibility == Visibility.Visible);
                    current = filled with { Proteins = filled.Proteins.Where(row => row.Family.Length == 0).ToArray(), Families = Array.Empty<ReviewFamilyInput>(),
                        Architectures = Array.Empty<ReviewArchitectureInput>(), Markers = Array.Empty<ReviewMarkerInput>(), LeftOut = Array.Empty<ReviewLeftOutInput>() };
                    FinishReviewWork(model.UpdateProposalAsync); FinishReviewWork(model.CheckMotifAsync); PumpDispatcher();
                    Check(theme + " RG11 a header-only report with no family members stays hidden", model.MotifReport.Length > 0
                        && Tab("Motif counts").Visibility == Visibility.Collapsed);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
        Check("RG11 tab visibility changes no supplied proposal data", sourceBefore == JsonSerializer.Serialize(filled));
    }
}
