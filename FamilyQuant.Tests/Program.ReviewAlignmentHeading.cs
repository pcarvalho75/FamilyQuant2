using FamilyQuant.Evidence.Alignment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReviewAlignmentHeadingCommands() =>
    [new("--test-review-alignment-heading", 74, TestCommandPhase.Raw, args => args.Contains("--test-review-alignment-heading", StringComparer.Ordinal), (_, _) =>
    {
        ReviewAlignmentHeadingTests(); Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];
    private static void ReviewAlignmentHeadingTests()
    {
        EnsureApplication();
        var input = GroupingReviewScreens.Fixture(12);
        var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null), input, null, evidenceMode: true)
        { Width = 1366, Height = 820, Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        window.AlignmentSource = _ => new AlignmentCandidateSet(Enumerable.Range(0, 11).Select(i => new AlignmentCandidate("P100" + i.ToString("D2"),
            "P100" + i.ToString("D2"), "entry-" + i, i < 2 ? AlignmentOrigin.Highlighted : i % 2 == 0 ? AlignmentOrigin.File : AlignmentOrigin.Panel,
            "Example family", "ACDEFGHIK", true, false, i == 2, i < 9, Array.Empty<AlignmentDomain>(), "", new[] { "Example family" })).ToArray(),
            new[] { "Example family" }, "Example family", Array.Empty<string>());
        try
        {
            window.ShowAllOptions = true; window.Show(); PumpDispatcher();
            var model = ((GroupingReviewViewModel)window.DataContext).Evidence!; model.SelectedFamily = null; PumpDispatcher();
            var grid = (DataGrid)window.FindName("evidenceGrid"); grid.SelectedItems.Clear();
            grid.SelectedItems.Add(model.Proteins[0]); grid.SelectedItems.Add(model.Proteins[1]);
            var before = model.Proteins.Select(row => row.Selected).ToArray();
            ((Button)window.FindName("alignSelectedButton")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            var alignment = window.OpenAlignmentWindow!; PumpDispatcher();
            try
            {
                Check("SF3 heading explains two selected and seven included references", alignment.Title == "2 selected and 7 reviewed references"
                    && alignment.Model.Alignment!.Rows.Count == 9 && WalkTree(alignment).OfType<TextBlock>().Any(text => text.Text == alignment.Title));
                alignment.Model.Candidates[2].Ticked = false; PumpDispatcher();
                Check("SF3 heading follows comparison ticks without counting offered unticked references", alignment.Title == "2 selected and 6 reviewed references"
                    && alignment.Model.Alignment!.Rows.Count == 8 && alignment.Model.Candidates.Count == 11);
                Check("SF3 alignment leaves proposal ticks unchanged and Help names reference sources", before.SequenceEqual(model.Proteins.Select(row => row.Selected))
                    && GroupingReviewHelp.Text.Contains("stored nearest reviewed proteins", StringComparison.Ordinal)
                    && GroupingReviewHelp.Text.Contains("database or stored reference panel", StringComparison.Ordinal));
            }
            finally { alignment.Close(); }
        }
        finally { window.Close(); PumpDispatcher(); }
    }
}
