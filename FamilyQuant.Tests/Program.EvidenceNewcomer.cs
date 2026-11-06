using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void EvidenceNewcomerTests()
    {
        Section("Group by evidence - newcomer wording and next actions");
        EnsureApplication();
        var window = ReviewLayoutWindow();
        try
        {
            window.Show(); PumpDispatcher(); window.UpdateLayout();
            var coverage = (TextBlock)window.FindName("TextBlockQueryCoverageRule");
            Check("coverage uses protein length words and preserves the precise definition", coverage.Text == EvidenceProposalPresentation.QueryCoverageSummary
                && !coverage.Text.Contains("query", StringComparison.OrdinalIgnoreCase)
                && coverage.ToolTip as string == EvidenceProposalPresentation.QueryCoverageRule);
            var model = (GroupingReviewViewModel)window.DataContext;
            var hint = (TextBlock)window.FindName("applyHint");
            Check("changed scope has a visible next action beside Apply", hint.IsVisible && hint.Text == model.ApplyHint
                && model.ApplyHint.Contains("Update proposal", StringComparison.Ordinal) && !model.CanConfirm);
            FinishReviewWork(model.UpdateProposalAsync); PumpDispatcher();
            Check("a ready proposal explains applying and saving", model.CanConfirm && model.ApplyHint.Contains("File > Save", StringComparison.Ordinal));
            model.Evidence!.SetSelected(model.Evidence.Proteins, false);
            Check("empty selection explains the missing tick", !model.CanConfirm && model.ApplyHint.Contains("Tick at least one", StringComparison.Ordinal));
            model.Evidence.SetSelected(model.Evidence.Proteins, true);
            model.Evidence.Families[0].Name = " ";
            Check("missing family name explains the blocked Apply", !model.CanConfirm && model.ApplyHint.Contains("name for every selected family", StringComparison.Ordinal));
            model.Evidence.Families[0].Name = "Supplied family";
            model.MotifPattern = "C";
            Check("unchecked pattern explains checking or clearing it", !model.CanConfirm && model.ApplyHint.Contains("Check the motif, or clear it", StringComparison.Ordinal));
            Check("single-family headline uses the singular", model.Evidence.Summary.Contains("1 family.", StringComparison.Ordinal));
            var familyGrid = (DataGrid)window.FindName("familyGrid");
            Check("family counts explain total versus reviewed", familyGrid.Columns.Any(c => (string)c.Header == "Total"
                && c.HeaderStyle.Setters.OfType<Setter>().Any(s => s.Value is string t && t.Contains("including unticked and locked", StringComparison.Ordinal)))
                && familyGrid.Columns.Any(c => (string)c.Header == "Reviewed"));
            var proteins = (DataGrid)window.FindName("evidenceGrid");
            Check("protein headings use evidence and identifier words", new[] { "Evidence source", "Reviewed relative", "Scope words found", "Current family ID", "FASTA identifier" }
                .All(h => proteins.Columns.Any(c => (string)c.Header == h)));
            Check("protein filter is labelled and scope match retains its supplied fact", WalkTree(window).OfType<TextBlock>().Any(t => t.Text == "Find proteins")
                && model.Evidence.Proteins.All(p => p.ScopeWordsText == (p.Source.HasScopeAnnotation ? "Yes" : "No")));
            var tabs = (TabControl)window.FindName("evidenceTabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Architecture"); PumpDispatcher();
            var architecture = (ArchitectureReviewControl)window.FindName("architectureReview");
            var groupGrid = (DataGrid)architecture.FindName("groupGrid");
            Check("Architecture explains size-based placement and new family names", groupGrid.Columns.Any(c => (string)c.Header == "Placed by size")
                && groupGrid.Columns.Any(c => (string)c.Header == "Most supported domain")
                && groupGrid.Columns.Any(c => (string)c.Header == "New family name")
                && model.Evidence.SelectedArchitecture!.UndeterminedLabel.StartsWith("Not placed in a domain group", StringComparison.Ordinal));
            model.Evidence.SelectedFamily = null; PumpDispatcher();
            Check("missing Architecture data has a visible explanation instead of blank tables",
                ((TextBlock)architecture.FindName("architectureEmpty")).IsVisible
                && !((Grid)architecture.FindName("architectureContent")).IsVisible);
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Left out"); PumpDispatcher();
            var leftOut = (TextBlock)window.FindName("leftOutLegend");
            Check("Left out explains its proposal-wide list and retention", leftOut.IsVisible && leftOut.Text == model.Evidence.LeftOutSummary
                && leftOut.Text.StartsWith("0 marker carriers listed across the proposal", StringComparison.Ordinal)
                && leftOut.ToolTip is string explanation && explanation.Contains("never removes proteins from quantitation", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }
}
