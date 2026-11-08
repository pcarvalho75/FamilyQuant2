using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ScopeReachabilityTests()
    {
        EnsureApplication();
        foreach (var size in new[] { (1024, 768), (1366, 820) })
        {
            var facts = new EvidenceTaskScopeInput("Fixture clade", Enumerable.Range(1, 40)
                .Select(id => new ReviewTaskTaxon("Fixture taxon " + id, id, 10, 1, 9)).ToArray(),
                new[] { new ReviewTaskKeyword("Fixture activity", 10, "Molecular function", Evidence.EvidenceTaskDiscovery.FunctionalScopeCategoryId) },
                40, new[] { "Fixture activity" }, "fixture", TargetProteins: 400);
            var preview = new SuperClusterPreview(new System.Collections.Generic.List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null);
            var window = new SuperClusterPreviewWindow(preview, GroupingReviewScreens.Fixture(12), _ => GroupingReviewScreens.Fixture(12),
                discoverTask: _ => Task.FromResult(facts)) { Width = size.Item1, Height = size.Item2, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                var expander = (Expander)window.FindName("TaskCountsExpander");
                var panel = (StackPanel)expander.Parent;
                ScrollViewer viewport;
                if (panel.Parent is ScrollViewer existing) viewport = existing;
                else
                {
                    var parent = (Panel)panel.Parent;
                    int index = parent.Children.IndexOf(panel);
                    parent.Children.Remove(panel);
                    viewport = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                    Grid.SetRow(viewport, Grid.GetRow(panel));
                    parent.Children.Insert(index, viewport);
                }
                viewport.Height = 210;
                window.Show(); PumpDispatcher(); window.UpdateLayout();
                var model = (GroupingReviewViewModel)window.DataContext;
                var scope = (TextBox)window.FindName("TaskScopeBox");
                var update = WalkTree(window).OfType<Button>().Single(button => button.Content as string == "Update proposal");
                var collapse = (Button)window.FindName("CollapseCountsToScopeButton");
                bool Inside(FrameworkElement element)
                {
                    Rect bounds = element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
                    return bounds.Top >= -1 && bounds.Bottom <= viewport.ActualHeight + 1;
                }
                Check("Z12c5 " + size.Item1 + " expanded counts expose a reachable collapse/edit action",
                    model.TaskScopeExpanded && collapse.IsVisible && Inside(collapse) && !Inside(scope));
                string previous = model.ScopeText;
                collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpDispatcher(); window.UpdateLayout(); PumpDispatcher();
                Check("Z12c5 " + size.Item1 + " collapsing counts brings scope and Update proposal into the bounded viewport",
                    !model.TaskScopeExpanded && Inside(scope) && Inside(update) && model.ScopeText == previous && scope.IsEnabled && update.IsEnabled);
                model.TaskScopeExpanded = true; PumpDispatcher();
                Check("Z12c5 " + size.Item1 + " counts remain available after editing", expander.IsExpanded
                    && ReferenceEquals(model.TaskScope, facts) && collapse.IsVisible);
            }
            finally { window.Close(); PumpDispatcher(); }
        }
    }
}
