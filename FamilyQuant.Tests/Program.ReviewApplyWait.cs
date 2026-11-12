using FamilyQuant.Evidence;
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
    private static IEnumerable<TestCommand> ReviewApplyWaitCommands() =>
    [new("--test-review-apply-wait", 74, TestCommandPhase.Raw, args => args.Contains("--test-review-apply-wait", StringComparer.Ordinal), (args, _) =>
    {
        ReviewApplyWaitTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "review-apply-wait"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];

    private static void ReviewApplyWaitTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var input = GroupingReviewScreens.Fixture(12) with { PanelLinks = Enumerable.Range(0, 3).Select(i => new PanelFamilyLinkProposal
        { Key = "link-" + i, SourceFamily = "Source " + i, CandidateFamilyKeys = new[] { "a", "b" } }).ToArray() };
        var preview = new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null);
        EvidenceReviewInput Confirm(IReadOnlyList<PanelFamilyLinkSelection> choices)
        {
            var selected = choices.ToDictionary(choice => choice.LinkKey);
            input = input with { PanelLinks = input.PanelLinks!.Select(link => selected.TryGetValue(link.Key, out var choice)
                ? new PanelFamilyLinkProposal { Key = link.Key, SourceFamily = link.SourceFamily, CandidateFamilyKeys = link.CandidateFamilyKeys,
                    SelectedFamilyKey = choice.PanelFamilyKey, IsConfirmed = true } : link).ToArray() };
            return input;
        }
        var window = new SuperClusterPreviewWindow(preview, input, null, evidenceMode: true, confirmPanelLinks: Confirm)
        { Width = 1024, Height = 768, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); PumpDispatcher(); var model = (GroupingReviewViewModel)window.DataContext;
            var waiting = (WrapPanel)window.FindName("PanelLinksWaitPanel");
            Check("RG1 disabled Apply explains the live unresolved count in Simple view", !model.CanConfirm && waiting.IsVisible
                && model.PanelLinksWaitText == "Waiting for 3 choices in Panel links");
            ScreenHarness.Save(window, Path.Combine(output, "apply-waits.png"), 1024, 768);
            ((Button)window.FindName("OpenPanelLinkChoicesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            Check("RG1 shortcut opens the Needs a choice filter", window.ShowAllOptions && model.OnlyPanelLinksNeedingChoice
                && ((TabItem)((TabControl)window.FindName("evidenceTabs")).SelectedItem).Header as string == "Panel links"
                && ((DataGrid)window.FindName("panelLinksGrid")).Items.Count == 3);
            foreach (var row in model.Evidence!.PanelLinks.Take(2)) row.SelectedOption = row.Options.First(option => option.Key == "a");
            FinishReviewWork(model.UpdatePanelLinksAsync); PumpDispatcher();
            Check("RG1 updated choices refresh the live count", model.PanelLinksWaitText == "Waiting for 1 choice in Panel links" && waiting.IsVisible);
            var last = model.Evidence!.PanelLinks.Single(row => row.Source.NeedsConfirmation); last.SelectedOption = last.Options.First(option => option.Key == "a");
            FinishReviewWork(model.UpdatePanelLinksAsync); PumpDispatcher();
            Check("RG1 no unresolved choice removes the waiting line and enables Apply", model.CanConfirm && !waiting.IsVisible);
        }
        finally { window.Close(); PumpDispatcher(); }
    }
}
