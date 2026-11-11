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
    private static IEnumerable<TestCommand> ReviewChecklistStateCommands() =>
    [new("--test-review-checklist-states", 74, TestCommandPhase.Raw, args => args.Contains("--test-review-checklist-states", StringComparer.Ordinal), (args, _) =>
    {
        ReviewChecklistStateTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "review-checklist-states"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];
    private static void ReviewChecklistStateTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var member = new PanelChecklistProtein(0, "P10000", PanelChecklistMatch.Pfam, true, true, "Supplied placement");
        var rows = new[]
        {
            new PanelChecklistFamily("a", "Proposed family", "Quantify", "a", new[] { member }),
            new PanelChecklistFamily("b", "Unresolved family", "Quantify", "b", Array.Empty<PanelChecklistProtein>(), HasPfamCode: false, NeedsLinkChoice: true),
            new PanelChecklistFamily("c", "Family without a code", "Quantify", "c", Array.Empty<PanelChecklistProtein>(), HasPfamCode: false),
            new PanelChecklistFamily("d", "Unmatched family", "Quantify", "d", Array.Empty<PanelChecklistProtein>()),
        };
        Check("RG6 every list row has one disjoint state", rows.Select(row => row.State).SequenceEqual(Enum.GetValues<PanelChecklistState>())
            && rows[0].StateText == "Has 1 protein" && rows[1].StateText == "Waits for a choice"
            && rows[2].StateText == "Has no Pfam code" && rows[3].StateText == "Matches no database protein");
        Check("RG6 outside matches wait for a scope or manual choice", (rows[3] with { Proteins = new[] { member with { Placed = false } } }).State == PanelChecklistState.NeedsChoice);
        var file = ChecklistFile(); string before = file.ToJsonText();
        var proposal = PanelEvidenceClassifier.Propose(file, new() { ScopeTerms = new[] { "selected" } });
        var actual = PanelFamilyChecklist.Build(file, proposal);
        Check("RG6 metadata comes from the imported panel without changing the document", actual.Count == file.FamilyPanel!.Families.Count
            && actual.All(row => row.HasPfamCode == (file.FamilyPanel.Families.Single(f => f.Key == row.Key).Pfam.Count > 0)) && file.ToJsonText() == before);
        var input = GroupingReviewScreens.Fixture(12) with { PanelChecklist = rows, PanelLinks = Array.Empty<PanelFamilyLinkProposal>() };
        var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null), input, null, evidenceMode: true)
        { Width = 1366, Height = 820, Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.RequestFamilyChecklist(); window.Show(); PumpDispatcher();
            var hint = (TextBlock)window.FindName("PanelChecklistHintText");
            Check("RG6 checklist shows complete list and state totals", hint.Text == "Your list: 4 families, 1 with proteins, 1 waiting for a choice, 1 with no Pfam code, 1 matching no database protein."
                && ((DataGrid)window.FindName("PanelChecklistGrid")).Items.Count == 4);
            ScreenHarness.Save(window, Path.Combine(output, "family-checklist.png"), 1366, 820);
        }
        finally { window.Close(); PumpDispatcher(); }
    }
}
