using FamilyQuant.Evidence;
using FamilyQuant.Panels;
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
    private static IEnumerable<TestCommand> ChecklistUnconfirmCommands() =>
    [
        new("--test-checklist-unconfirm", 74, TestCommandPhase.Raw, a => a.Contains("--test-checklist-unconfirm", StringComparer.Ordinal), (_, _) =>
        {
            ChecklistUnconfirmTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "checklist-unconfirm"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    /// <summary>IM10b: Family checklist un-confirms a row accepted from its sheet codes in the review draft; the proposal
    /// drops the matches that came only from the confirmation, Apply grouping keeps it, Undo restores it, Cancel keeps nothing.</summary>
    private static void ChecklistUnconfirmTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        FamilyClusterFile Coded()
        {
            static FastaItemExtended Protein(string id, string source, int userId, string domain) => new()
            {
                SequenceIdentifier = id, Description = "Synthetic protein " + userId, Sequence = "ACDEFGHIK" + new string('W', userId), UserID = userId, USearchID = userId,
                Evidence = new() { Accession = NearestReviewed.EntryAccession(id), Reviewed = id.StartsWith("sp|", StringComparison.Ordinal), Family = source,
                    Keywords = new() { "Action alpha" }, Pfam = new() { new() { Accession = domain } } },
            };
            var file = new FamilyClusterFile
            {
                FamilyPanel = new() { Families = new() { new() { Key = "coded", DisplayName = "Coded row", Pfam = new() { new() { Accession = "PF90501" } } } } },
                MyExtendedFastaItems = new()
                {
                    Protein("sp|P55001|SYNTHETIC", "Coded source family", 1, "PF90501"),
                    Protein("tr|P55002|SYNTHETIC", "Coded source family", 2, "PF90501"),
                    Protein("tr|P55003|SYNTHETIC", "Accepted source family", 3, "PF90599"),
                },
            };
            // As the import accepts a row from its sheet codes, with one more accepted label.
            file.FamilyPanel = PanelEvidenceMatching.WithConfirmed(file.FamilyPanel, "coded", PanelEvidenceMatching.Confirm(new[] { "Coded row" },
                new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Pfam, "PF90501"), new PanelEvidenceChoice(PanelEvidenceMatchKind.UniProtFamily, "Accepted source family") }));
            return file;
        }
        (SuperClusterPreviewWindow Review, GroupingReviewViewModel Model, EvidenceGroupingSession Session) Open(FamilyClusterFile file)
        {
            var session = new EvidenceGroupingSession(file, "un-confirm fixture");
            var input = session.Propose("Action alpha");
            var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
            var review = new SuperClusterPreviewWindow(preview, input, session.Propose, initialScope: "Action alpha", evidenceMode: true,
                addToFamily: session.AddToFamily, removeFromFamily: session.RemoveFromFamily, updateManualChoices: session.UpdateManualChoices,
                createMatchContext: session.CreatePanelMatchContext, confirmPanelMatches: session.ConfirmPanelEvidenceMatches)
                { Width = 1366, Height = 768, ShowInTaskbar = false, ShowActivated = false };
            review.ShowAllOptions = true; review.Show(); PumpDispatcher(); ((TabItem)review.FindName("PanelChecklistTab")).IsSelected = true;
            var model = (GroupingReviewViewModel)review.DataContext;
            model.Evidence!.SelectedChecklistFamily = model.Evidence.PanelChecklist.Single(row => row.Key == "coded"); PumpDispatcher();
            return (review, model, session);
        }

        var cancelled = Coded(); string before = cancelled.ToJsonText();
        var (review, model, _) = Open(cancelled);
        try
        {
            var button = (Button)review.FindName("UnconfirmPanelMatchesButton");
            var row = model.Evidence!.SelectedChecklistFamily!;
            Check("IM10b a row accepted from its sheet codes can be un-confirmed in Family checklist", row is { HasConfirmedMatches: true, Matches: 3 }
                && button.IsEnabled && (button.ToolTip as string ?? "").Contains("Pfam codes stay", StringComparison.Ordinal));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); review.UpdateLayout(); PumpDispatcher();
            row = model.Evidence!.SelectedChecklistFamily!;
            Check("IM10b un-confirming drops the matches that came only from the confirmation and keeps the row's Pfam codes", row is { Key: "coded", HasConfirmedMatches: false, Matches: 2 }
                && !row.MatchTypes.Contains("Confirmed match", StringComparison.Ordinal) && !button.IsEnabled
                && (button.ToolTip as string ?? "").Contains("Select a Family checklist row with Confirmed match", StringComparison.Ordinal));
            ScreenHarness.Save(review, Path.Combine(output, "unconfirmed-checklist.png"), 1366, 768);
        }
        finally { review.Close(); PumpDispatcher(); }
        Check("IM10b closing the review keeps nothing", cancelled.ToJsonText() == before);

        var applied = Coded(); before = applied.ToJsonText();
        (review, model, var session) = Open(applied);
        try
        {
            model.UnconfirmPanelMatches();
            var result = session.Apply(model.Evidence!.CaptureSelection());
            Check("IM10b Apply grouping keeps the un-confirmed row with its Pfam codes", applied.FamilyPanel!.ConfirmedEvidenceMatches?.ContainsKey("coded") != true
                && applied.FamilyPanel.Families.Single().Pfam.Single().Accession == "PF90501");
            result.Undo!.Restore(applied);
            Check("IM10b Undo restores the confirmed matches", applied.ToJsonText() == before);
        }
        finally { review.Close(); PumpDispatcher(); }
    }
}
