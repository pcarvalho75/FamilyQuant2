using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReviewTabGuideCommands() =>
    [new("--test-review-tab-guides", 74, TestCommandPhase.Raw, args => args.Contains("--test-review-tab-guides", StringComparer.Ordinal), (_, _) =>
    {
        ReviewTabGuideTests(); Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];
    private static void ReviewTabGuideTests()
    {
        EnsureApplication();
        var input = GroupingReviewScreens.Fixture(12) with { PanelLinks = Array.Empty<FamilyQuant.Evidence.PanelFamilyLinkProposal>() };
        var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null), input, null, evidenceMode: true)
        { Width = 1366, Height = 820, Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); PumpDispatcher();
            var modes = (TabControl)window.FindName("ReviewModeTabs"); var evidence = (TabControl)window.FindName("evidenceTabs");
            var tabs = modes.Items.OfType<TabItem>().Take(1).Concat(evidence.Items.OfType<TabItem>().Skip(1)).ToArray();
            var controls = new[] { "grid", "familyGrid", "evidenceGrid", "panelLinksGrid", "PanelChecklistGrid", "architectureReview", "unassignedGrid", "leftOutGrid" }
                .ToDictionary(name => name, name => window.FindName(name));
            var selection = ((GroupingReviewViewModel)window.DataContext).Evidence!.Proteins.Select(row => row.Selected).ToArray();
            window.ShowAllOptions = true; PumpDispatcher();
            foreach (var tab in tabs)
            {
                if (modes.Items.Contains(tab)) modes.SelectedItem = tab;
                else { modes.SelectedIndex = 1; evidence.SelectedItem = tab; }
                PumpDispatcher(); window.UpdateLayout();
                var panel = tab.Content as DockPanel; var line = panel?.Children.OfType<TextBlock>().FirstOrDefault();
                string text = GroupingReviewHelp.AdvancedTabGuides[(string)tab.Header];
                Check("RG9 " + tab.Header + " has one top purpose/use sentence shared with Help", panel?.Children.Count == 2
                    && ReferenceEquals(panel.Children[0], line) && line!.IsVisible && line.Text == text
                    && DockPanel.GetDock(line) == Dock.Top && text.EndsWith(".", StringComparison.Ordinal)
                    && GroupingReviewHelp.ForMode(true, true).Contains(text, StringComparison.Ordinal));
                if ((string)tab.Header == "Panel links")
                {
                    var links = (DataGrid)window.FindName("panelLinksGrid");
                    Check("Panel links choices, status and both examples fit the first viewport", links.Columns.Take(5).Select(column => (string)column.Header)
                        .SequenceEqual(new[] { "Source family", "User choice", "Status", "Example protein", "Example UniProt family" })
                        && links.Columns.Take(5).Sum(column => column.ActualWidth) <= links.ActualWidth - 16);
                }
            }
            Check("RG9 every revealed tab has a guide without moving actions or changing choices", tabs.Length == GroupingReviewHelp.AdvancedTabGuides.Count
                && controls.All(pair => pair.Value != null && ReferenceEquals(pair.Value, window.FindName(pair.Key)))
                && selection.SequenceEqual(((GroupingReviewViewModel)window.DataContext).Evidence!.Proteins.Select(row => row.Selected)));
        }
        finally { window.Close(); PumpDispatcher(); }
    }
}
