using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ScopeReachabilityTests()
    {
        EnsureApplication();
        // 1180 x 740 is the window's own default size; 2.1.12 hid the Scope row there behind the tables' minimum height.
        foreach (var size in new[] { (1024, 768), (1180, 740), (1366, 820) })
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
                window.Show(); PumpDispatcher(); window.UpdateLayout();
                var model = (GroupingReviewViewModel)window.DataContext;
                var expander = (Expander)window.FindName("TaskCountsExpander");
                var scope = (TextBox)window.FindName("TaskScopeBox");
                var update = WalkTree(window).OfType<Button>().Single(button => button.Content as string == "Update proposal");
                var collapse = (Button)window.FindName("CollapseCountsToScopeButton");
                var motif = WalkTree(window).OfType<TextBlock>().First(text => text.Text == "Motif (optional)");
                var root = (FrameworkElement)window.Content;
                // Visible to a person: inside the window and inside the viewport of every scroll area around it
                // (a scroll area clips what lies below its viewport even when that is within the window).
                bool OnScreen(FrameworkElement element)
                {
                    if (!element.IsVisible) return false;
                    Rect bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                    if (bounds.Top < -1 || bounds.Bottom > root.ActualHeight + 1) return false;
                    for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
                        if (parent is ScrollViewer viewer && viewer.TemplatedParent is not TextBox)
                        {
                            Rect inViewer = element.TransformToAncestor(viewer).TransformBounds(new Rect(element.RenderSize));
                            if (inViewer.Top < -1 || inViewer.Bottom > viewer.ViewportHeight + 1) return false;
                        }
                    return true;
                }
                static bool Scrolls(DependencyObject element)
                {
                    for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
                        if (parent is ScrollViewer viewer && viewer.TemplatedParent is not TextBox) return true;
                    return false;
                }
                string label = "2.1.13 " + size.Item1 + "x" + size.Item2;
                Check(label + " expanded counts keep the Scope box, Update proposal and the motif row on screen",
                    model.TaskScopeExpanded && collapse.IsVisible && OnScreen(scope) && OnScreen(update) && OnScreen(motif) && !Scrolls(scope),
                    "scope_on_screen=" + OnScreen(scope) + " update_on_screen=" + OnScreen(update) + " motif_on_screen=" + OnScreen(motif));
                string previous = model.ScopeText;
                collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpDispatcher(); window.UpdateLayout(); PumpDispatcher();
                Check(label + " collapsing counts keeps scope and Update proposal usable",
                    !model.TaskScopeExpanded && OnScreen(scope) && OnScreen(update) && model.ScopeText == previous && scope.IsEnabled && update.IsEnabled);
                model.TaskScopeExpanded = true; PumpDispatcher();
                Check(label + " counts remain available after editing", expander.IsExpanded
                    && ReferenceEquals(model.TaskScope, facts) && collapse.IsVisible);
            }
            finally { window.Close(); PumpDispatcher(); }
        }
    }
}
