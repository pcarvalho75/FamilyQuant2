using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReviewLayout3Commands() =>
    [new("--test-review-layout3", 74, TestCommandPhase.Raw, args => args.Contains("--test-review-layout3", StringComparer.Ordinal), (args, _) =>
    {
        ReviewLayout3Tests(args.Length == 2 ? args[1] : null);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];

    private static void ReviewLayout3Tests(string? output = null)
    {
        EnsureApplication();
        var previous = Themes.ThemeManager.Current;
        var input = ReviewLayoutFixture() with
        {
            PanelLinks = Enumerable.Range(0, 3).Select(i => new PanelFamilyLinkProposal
            { Key = "choice" + i, SourceFamily = "Supplied source " + i, CandidateFamilyKeys = new[] { "row0", "row1" } }).ToArray(),
            PanelChecklist = Enumerable.Range(0, 12).Select(i => new PanelChecklistFamily("row" + i,
                "Supplied family with a long descriptive name " + i, "Family", "row" + i,
                new[] { new PanelChecklistProtein(i, "fixture_" + i, PanelChecklistMatch.Pfam, true, false,
                    string.Join(" ", Enumerable.Repeat("Supplied reason for remaining outside.", 30)), new[] { "Supplied phrase" }) }, 1)).ToArray()
        };
        string before = JsonSerializer.Serialize(input);
        try
        {
            Themes.ThemeManager.Apply(Themes.AppTheme.Light);
            foreach (var size in new[] { (Width: 1366, Height: 820), (Width: 1180, Height: 740) })
            foreach (bool pending in new[] { false, true })
            {
                var source = pending ? input : input with { PanelLinks = Array.Empty<PanelFamilyLinkProposal>() };
                var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null),
                    source, _ => source, evidenceMode: true, hasFamilyList: true,
                    discoverTask: _ => Task.FromResult(new EvidenceTaskScopeInput("Supplied database", Array.Empty<ReviewTaskTaxon>(),
                        new[] { new ReviewTaskKeyword("Supplied phrase", 16) }, 16, new[] { "Supplied phrase" }, "synthetic")))
                { Width = size.Width, Height = size.Height, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
                    WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.Manual, ShowAllOptions = true };
                try
                {
                    window.Show(); PumpDispatcher(); window.UpdateLayout();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    FinishReviewWork(model.UpdateProposalAsync);
                    model.TaskScopeExpanded = false; PumpDispatcher(); window.UpdateLayout();
                    var expander = (Expander)window.FindName("ProteinDestinationsExpander");
                    var scopeScroll = WalkTree(window).OfType<ScrollViewer>().Single(viewer => viewer.Content is StackPanel panel
                        && panel.Children.OfType<Expander>().Any());
                    string label = "RG-layout3 " + size.Width + "x" + size.Height + (pending ? " pending choices" : " ready");
                    void Capture(string name)
                    {
                        if (output == null) return;
                        Directory.CreateDirectory(output);
                        ScreenHarness.Save(window, Path.Combine(output, size.Width + "x" + size.Height + (pending ? "-pending-" : "-ready-") + name + ".png"), size.Width, size.Height);
                    }
                    Check(label + " collapsed destinations and scope reserve no empty band", expander.ActualHeight <= 48
                        && scopeScroll.ActualHeight <= ((FrameworkElement)scopeScroll.Content).DesiredSize.Height + 1,
                        "destinations=" + expander.ActualHeight + "; scope=" + scopeScroll.ActualHeight + "; content=" + ((FrameworkElement)scopeScroll.Content).DesiredSize.Height);
                    Capture("collapsed");
                    expander.IsExpanded = true; PumpDispatcher(); window.UpdateLayout();
                    var lines = WalkTree(expander).OfType<TextBlock>().Where(text => text.Text == model.Evidence!.ListDestinationText
                        || text.Text == model.Evidence.SequenceDestinationText || text.Text == model.Evidence.OutsideScopeDestinationText
                        || text.Text.StartsWith("Proposal counts;", StringComparison.Ordinal)).ToArray();
                    Check(label + " expanded destinations show all three sentences and note", lines.Length == 4 && lines.All(text => ReviewLayout3Visible(text, window)),
                        string.Join("; ", lines.Select(text => text.Text + ":" + text.TranslatePoint(new Point(), expander).Y)));
                    Check(label + " scope controls and motif stay on screen with destinations expanded", ReviewLayout3Visible((TextBox)window.FindName("TaskScopeBox"), window)
                        && ReviewLayout3Visible((DockPanel)window.FindName("MotifRow"), window));
                    Capture("destinations");
                    expander.IsExpanded = false; PumpDispatcher();
                    var status = WalkTree(window).OfType<TextBlock>().Single(text => BindingOperations.GetBindingExpression(text, TextBlock.TextProperty)?.ParentBinding.Path?.Path == "Status");
                    var hint = WalkTree(window).OfType<TextBlock>().Single(text => text.Text == EvidenceTaskScopeInput.ScopeExpansionHint);
                    Check(label + " scope hint and proposal status are separate fully visible lines", ReviewLayout3Visible(status, window) && ReviewLayout3Visible(hint, window)
                        && status.Text == "Proposal updated. Review the ticks before applying."
                        && status.TranslatePoint(new Point(), window).Y >= hint.TranslatePoint(new Point(0, hint.ActualHeight), window).Y);
                    Check(label + " full protein table retains a visible row below its tools", ((DataGrid)window.FindName("evidenceGrid")).ActualHeight >= 64);
                    Capture("status");
                    var proteinTools = (ScrollViewer)window.FindName("ProteinToolsScroller");
                    proteinTools.ScrollToBottom(); PumpDispatcher();
                    var remove = (Button)window.FindName("removeFamilyButton");
                    remove.BringIntoView(); PumpDispatcher();
                    Check(label + " protein actions remain reachable in short full views", ReviewLayout3Visible(remove, window),
                        "tools=" + proteinTools.ActualHeight + "; viewport=" + proteinTools.ViewportHeight + "; extent=" + proteinTools.ExtentHeight
                        + "; offset=" + proteinTools.VerticalOffset + "; button top=" + remove.TranslatePoint(new Point(), proteinTools).Y + "; height=" + remove.ActualHeight);
                    Capture("protein-tools");
                    ((TabControl)window.FindName("evidenceTabs")).SelectedItem = window.FindName("PanelChecklistTab"); PumpDispatcher(); window.UpdateLayout();
                    var grid = (DataGrid)window.FindName("PanelChecklistGrid");
                    var rows = WalkTree(grid).OfType<DataGridRow>().Where(row => row.IsVisible && row.ActualHeight > 0
                        && ReviewLayout3Visible(row, window)).ToArray();
                    if (size.Width == 1366)
                        Check(label + " checklist shows six whole single-line rows", rows.Length >= 6
                            && rows.All(row => row.ActualHeight <= 34 && WalkTree(row).OfType<TextBlock>().All(text => text.TextWrapping == TextWrapping.NoWrap)),
                            "rows=" + rows.Length + "; table=" + grid.ActualHeight + "; heights=" + string.Join(",", rows.Select(row => row.ActualHeight)));
                    var tools = (ScrollViewer)window.FindName("ChecklistToolsScroller");
                    Check(label + " checklist reasons and actions scroll without hiding the motif", tools.ScrollableHeight > 0
                        && ReviewLayout3Visible(grid, window) && ReviewLayout3Visible((DockPanel)window.FindName("MotifRow"), window));
                    Capture("checklist");
                    tools.ScrollToBottom(); PumpDispatcher();
                    Check(label + " checklist matching action remains reachable", ReviewLayout3Visible((Button)window.FindName("MatchPanelNamesButton"), window));
                    ((TabControl)window.FindName("evidenceTabs")).SelectedIndex = 0; PumpDispatcher();
                    Check(label + " other tabs restore the proposed family table and legend", ReviewLayout3Visible((DataGrid)window.FindName("familyGrid"), window)
                        && ((TextBlock)window.FindName("familyTickLegend")).IsVisible);
                    model.ScopeText = ""; PumpDispatcher();
                    Check(label + " layout preserves the scope gate", !model.CanConfirm && !model.CanUpdateProposal);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
        Check("RG-layout3 changes no proposal data", before == JsonSerializer.Serialize(input));
    }

    private static bool ReviewLayout3Visible(FrameworkElement element, Window window)
    {
        if (!element.IsVisible || element.ActualHeight <= 0) return false;
        Rect bounds = new(new Point(), element.RenderSize);
        for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not FrameworkElement ancestor) continue;
            Rect actual = element.TransformToAncestor(ancestor).TransformBounds(bounds);
            if ((ancestor is ScrollContentPresenter || ancestor == window || ancestor is Expander)
                && (actual.Top < -1 || actual.Bottom > ancestor.ActualHeight + 1)) return false;
            var clip = LayoutInformation.GetLayoutClip(ancestor);
            if (clip != null && (actual.Top < clip.Bounds.Top - 1 || actual.Bottom > clip.Bounds.Bottom + 1)) return false;
        }
        return true;
    }
}
