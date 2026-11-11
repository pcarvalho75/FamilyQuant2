using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReviewDestinationCommands() =>
    [new("--test-review-destinations", 74, TestCommandPhase.Raw, args => args.Contains("--test-review-destinations", StringComparer.Ordinal), (args, _) =>
    {
        ReviewDestinationTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "review-destinations"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];
    private static void ReviewDestinationTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        ReviewProteinInput Row(int i, string family, bool scope, string? identifier = null, bool locked = false) =>
            new(i, identifier ?? "sp|P1000" + i + "|ENTRY", i, family, "UniProt family", "Supplied reason", scope, "", Array.Empty<string>(), true, "Example protein", IsLocked: locked);
        var input = new EvidenceReviewInput("synthetic-destinations", new[]
        {
            Row(0, "a", true), Row(1, "", true), Row(2, "", false), Row(3, "a", false, locked: true),
            Row(4, "", true, "contaminant_sp|P10004|ENTRY"), Row(5, "", true, "Reverse_sp|P10005|ENTRY"),
        }, new[] { new ReviewFamilyInput("a", "Family a", 2, 1) }, Array.Empty<ReviewMarkerInput>(), PanelLinks: Array.Empty<PanelFamilyLinkProposal>());
        string before = JsonSerializer.Serialize(input);
        var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null), input, null, evidenceMode: true)
        { Width = 1366, Height = 820, Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); PumpDispatcher(); var model = ((GroupingReviewViewModel)window.DataContext).Evidence!;
            Check("RG7 proposal targets form three disjoint destinations", model.ProteinsInList == 2 && model.InScopeProteinsOutsideList == 1
                && model.ProteinsOutsideScope == 1 && ((StackPanel)window.FindName("ProteinDestinationsPanel")).IsVisible);
            model.SearchText = "no visible match"; model.Proteins[0].Selected = false;
            Check("RG7 view filters and ticks leave proposal counts unchanged and retain lock semantics", model.ProteinsInList == 2
                && model.InScopeProteinsOutsideList == 1 && model.ProteinsOutsideScope == 1 && !model.Proteins[3].CanSelect
                && EvidenceReviewViewModel.DestinationHelp.Contains("Locked or unticked proteins keep their current family", StringComparison.Ordinal));
            Check("RG7 Help explains sequence families and measurement without a data example", GroupingReviewHelp.Text.Contains("named after its proteins", StringComparison.Ordinal)
                && GroupingReviewHelp.Text.Contains("measured together in that family", StringComparison.Ordinal)
                && GroupingReviewHelp.Text.Contains("Decoys and contaminants are excluded", StringComparison.Ordinal));
            model.SearchText = "";
            ScreenHarness.Save(window, Path.Combine(output, "protein-destinations.png"), 1366, 820);
            Check("RG7 destination display leaves supplied scientific facts unchanged", JsonSerializer.Serialize(input) == before);
        }
        finally { window.Close(); PumpDispatcher(); }
    }
}
