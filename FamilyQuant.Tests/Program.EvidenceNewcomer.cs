using System;
using System.Collections.Generic;
using FamilyQuant.Evidence;
using FamilyQuant.DeNovo;
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
                && leftOut.Text.Contains("not deleted", StringComparison.OrdinalIgnoreCase)
                && leftOut.Text.Contains("still quantified", StringComparison.Ordinal)
                && leftOut.Text.Contains("current or assigned family", StringComparison.Ordinal)
                && leftOut.Text.Contains("0 listed", StringComparison.Ordinal));
            var assurance = new TextBlock { Text = leftOut.Text, FontFamily = leftOut.FontFamily, FontSize = leftOut.FontSize };
            assurance.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check("Left out assurance fits the visible line without ellipsis", assurance.DesiredSize.Width <= leftOut.ActualWidth + 1);
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Markers"); PumpDispatcher();
            var markers = (DataGrid)window.FindName("markerGrid");
            Check("marker support is visible before its secondary count columns", markers.Columns.ToList().FindIndex(c => (string)c.Header == "Support")
                < markers.Columns.ToList().FindIndex(c => (string)c.Header == "Family votes")
                && ((TextBlock)window.FindName("markerLegend")).Text.Contains("not abundance", StringComparison.Ordinal)
                && ((TextBlock)window.FindName("markerLegend")).Text.Contains("family votes / labelled scope votes", StringComparison.Ordinal));
            var marker = new ReviewMarkerInput("invented", "Invented domain", "Invented family", 18, 26, 20, .9);
            Check("support preserves the supplied fraction and explains its denominator", marker.SupportText == .9.ToString("P1")
                && marker.SupportHelp.Contains("18 of 20", StringComparison.Ordinal) && marker.SupportHelp.Contains("26 domain carriers", StringComparison.Ordinal)
                && (marker with { LabelledCarriers = 0 }).SupportText == "Not supplied");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Motif counts"); PumpDispatcher();
            var motif = (TextBlock)window.FindName("motifLegend");
            Check("blank Motif counts gives an entry action and explains unticked members", motif.IsVisible
                && motif.Text.Contains("Enter a pattern, then Check motif", StringComparison.Ordinal)
                && motif.Text.Contains("include unticked members", StringComparison.Ordinal)
                && motif.Text.Contains("exclude known fragments", StringComparison.Ordinal));
            var sequences = new[] {
                new FastaItemExtended { SequenceIdentifier = "motif_one", Sequence = "ACCC" },
                new FastaItemExtended { SequenceIdentifier = "motif_two", Sequence = "AAAA" },
                new FastaItemExtended { SequenceIdentifier = "motif_fragment", Sequence = "CCCC", Evidence = new ProteinEvidence { Fragment = true } } };
            var proposal = new EvidenceProposal { SourceFingerprint = EvidenceClassifier.Fingerprint(sequences), Proteins = sequences.Select((p, i) => new EvidenceProteinProposal
                { ProteinIndex = i, Identifier = p.SequenceIdentifier, Family = "Invented family", Tier = EvidenceTier.UniProtFamily,
                    Flags = i == 0 ? new[] { new EvidenceFlag { Code = "supplied", Message = "Starts unticked" } } : Array.Empty<EvidenceFlag>() }).ToArray() };
            string report = EvidenceProposalPresentation.CountMotif(proposal, sequences, "C");
            Check("motif denominator includes unticked proposals and excludes known fragments", !proposal.Proteins[0].SuggestedSelected
                && report.Contains("Matching proteins\tProteins counted", StringComparison.Ordinal)
                && report.Contains("Invented family\t1\t2", StringComparison.Ordinal));
        }
        finally { window.Close(); }
        var input = ReviewLayoutFixture();
        var orphan = input.Proteins[0] with { ProteinIndex = 32, Identifier = "invented_unassigned", Family = "", SuggestedSelected = false,
            Flags = new[] { "domain_compatibility: Supplied incompatible domain detail." } };
        var unassignedWindow = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null),
            input with { Proteins = input.Proteins.Append(orphan).ToArray() }, null, evidenceMode: true)
            { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            unassignedWindow.Show(); PumpDispatcher();
            var tabs = (TabControl)unassignedWindow.FindName("evidenceTabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Unassigned"); PumpDispatcher();
            var grid = (DataGrid)unassignedWindow.FindName("unassignedGrid");
            var flag = WalkTree(grid).OfType<TextBlock>().First(t => t.Text == "Domain compatibility");
            Check("Unassigned warning text retains check guidance and original detail", flag.ToolTip is string help
                && help.Contains("Check whether observed domains", StringComparison.Ordinal) && help.Contains("Supplied incompatible domain detail.", StringComparison.Ordinal));
            var legend = (TextBlock)unassignedWindow.FindName("unassignedLegend");
            Check("Unassigned visibly retains current families and quantitation", legend.IsVisible && legend.Text.Contains("keep their current families", StringComparison.Ordinal)
                && legend.Text.Contains("available for quantitation", StringComparison.Ordinal) && grid.IsReadOnly);
        }
        finally { unassignedWindow.Close(); }
        var panelInput = input with { Architectures = Array.Empty<ReviewArchitectureInput>(), PanelLinks = new[] { new PanelFamilyLinkProposal
            { Key = "invented-link", SourceFamily = "Invented source family", ArchitectureKey = "[\"PF-invented\"]",
                CandidateFamilyKeys = new[] { "invented-a", "invented-b" }, SupportingAccessions = new[] { "invented_relative" } } } };
        var panelWindow = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null),
            panelInput, null, evidenceMode: true)
            { Width = 1366, Height = 768, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            panelWindow.Show(); PumpDispatcher();
            var tabs = (TabControl)panelWindow.FindName("evidenceTabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Panel links"); PumpDispatcher();
            var grid = (DataGrid)panelWindow.FindName("panelLinksGrid");
            var choice = WalkTree(grid).OfType<ComboBox>().First();
            var updateLine = WalkTree(panelWindow).OfType<TextBlock>().FirstOrDefault(t => t.Text == "Use link choices updates the family proposal.");
            Check("panel update effect is explicit in visible text", updateLine?.IsVisible == true);
            Check("panel choice and status precede provenance and fit the first viewport", (string)grid.Columns[1].Header == "User choice"
                && (string)grid.Columns[2].Header == "Status" && grid.Columns.Any(c => (string)c.Header == "Candidate families")
                && choice.TranslatePoint(new Point(choice.ActualWidth, 0), grid).X < grid.ActualWidth - 16);
            choice.SelectedIndex = 0; PumpDispatcher();
            var model = (GroupingReviewViewModel)panelWindow.DataContext;
            Check("visible choice retains its exact supplied family key", model.Evidence!.PanelLinks[0].SelectedOption?.Key == "invented-a"
                && model.Evidence.HasUnappliedPanelLinkChoices && !model.CanConfirm);
        }
        finally { panelWindow.Close(); }
        string evidenceHelp = GroupingReviewHelp.ForMode(true);
        Check("Evidence Help opens with the scope workflow and plain review definitions", evidenceHelp.StartsWith("GROUPING BY EVIDENCE", StringComparison.Ordinal)
            && evidenceHelp.Contains(EvidenceScopeHelp.Build(), StringComparison.Ordinal) && evidenceHelp.Contains(EvidenceReviewTerms.Text, StringComparison.Ordinal)
            && GroupingReviewHelp.ForMode(true, true).Contains(EvidenceReviewTerms.Text, StringComparison.Ordinal)
            && GroupingReviewHelp.ForMode(false).StartsWith(GroupingReviewHelp.Text, StringComparison.Ordinal));
        Check("Help distinguishes warnings from unassigned and defines counting", !evidenceHelp.Contains("stays unassigned and flagged", StringComparison.Ordinal)
            && evidenceHelp.Contains("warning flag appears only when the analysis supplies one", StringComparison.Ordinal)
            && evidenceHelp.Contains("entries without a fragment annotation are counted", StringComparison.Ordinal)
            && evidenceHelp.Contains("unticked proteins keep their current family and are still quantified", StringComparison.Ordinal));
        string generalHelp = HelpContent.BuildDeNovo();
        Check("Help distinguishes scan m/z clustering from peptidoform neutral-mass suppression", generalHelp.Contains("same run, scan and charge by reported precursor m/z", StringComparison.Ordinal)
            && generalHelp.Contains("canonical peptidoform and charge, then compares neutral precursor masses", StringComparison.Ordinal)
            && generalHelp.Contains("one- or two-isotope selection correction", StringComparison.Ordinal)
            && GroupingReviewHelp.Text.Contains("Align selected opens a read-only alignment", StringComparison.Ordinal));
        var identified = new NativePeptideQuant { Run = "invented_run", Sequence = "ACDI", Charge = 2, PrecursorMz = 500 };
        var deNovo = new NativePeptideQuant { Run = "invented_run", Sequence = "ACDL", Charge = 2,
            PrecursorMz = 500 + PeptidoformIdentity.Carbon13SpacingDa / 2 };
        Check("the documented isotope offset is accepted by both production precursor helpers", PeptidoformIdentity.PrecursorMzEquivalent(identified.PrecursorMz,
            deNovo.PrecursorMz, 2, out _) && NativeQuantFractions.IsCrossSideEquivalent(identified, deNovo));
        deNovo.Sequence = "ACD[15.99]L";
        Check("suppression still requires canonical modified-peptide equivalence", !NativeQuantFractions.IsCrossSideEquivalent(identified, deNovo));
    }
}
