using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class ScreenHarness
{
    private static ScrollViewer EvidenceScopeScroller(SuperClusterPreviewWindow window) => Walk(window).OfType<ScrollViewer>()
        .Single(s => s.Content is StackPanel panel && panel.Children.OfType<Expander>().Any());

    private static void CaptureEvidenceReview(Action<string, Window> capture)
    {
        var scope = Program.ReviewLayoutWindow(); ShowOffScreen(scope);
        try { capture("15-evidence-scope", scope); }
        finally { scope.Close(); }
        var architecture = Program.ReviewLayoutWindow(); ShowOffScreen(architecture);
        try
        {
            var tabs = (TabControl)architecture.FindName("evidenceTabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Architecture");
            Pump(); capture("16-evidence-architecture", architecture);
        }
        finally { architecture.Close(); }
    }
}
