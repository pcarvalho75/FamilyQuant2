using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    private void InitializeAdvancedTabGuides()
    {
        var tabs = ReviewModeTabs.Items.OfType<TabItem>().Take(1).Concat(evidenceTabs.Items.OfType<TabItem>().Skip(1));
        foreach (var tab in tabs)
        {
            var content = (UIElement)tab.Content;
            tab.Content = null;
            var guide = new TextBlock { Text = GroupingReviewHelp.AdvancedTabGuides[(string)tab.Header],
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            guide.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            DockPanel.SetDock(guide, Dock.Top);
            var panel = new DockPanel();
            panel.Children.Add(guide);
            panel.Children.Add(content);
            tab.Content = panel;
        }
    }
}
