using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReviewDetailsHeightCommands() =>
    [new("--test-review-details-height", 74, TestCommandPhase.Raw, args => args.Contains("--test-review-details-height", StringComparer.Ordinal), (args, _) =>
    {
        ReviewDetailsHeightTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "review-details-height"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];
    private static void ReviewDetailsHeightTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var input = GroupingReviewScreens.Fixture(12);
        input = input with { Proteins = input.Proteins.Select((p, i) => p with { Flags = Array.Empty<string>(), Reason = i == 2
            ? string.Join(" ", Enumerable.Repeat("Long supplied evidence detail", 120)) : "Supplied reason", HasScopeAnnotation = true,
            Neighbour = "P10000; supplied comparison", Domains = "Supplied domain", SuggestedSelected = true }).ToArray() };
        var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null), input, null, evidenceMode: true)
        { Width = 1366, Height = 820, Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); PumpDispatcher(); var model = ((GroupingReviewViewModel)window.DataContext).Evidence!;
            var grid = (DataGrid)window.FindName("evidenceGrid"); var details = (TextBox)window.FindName("proteinDetails");
            foreach (int width in new[] { 1366, 1024 })
            {
                window.Width = width; window.Height = width == 1366 ? 820 : 768;
                grid.SelectedItem = model.Proteins[1]; PumpDispatcher(); window.UpdateLayout();
                details.ScrollToHome(); PumpDispatcher();
                var last = details.GetRectFromCharacterIndex(details.Text.Length - 1);
                Check("SF2 " + width + " short details show first through final line without clipping", details.LineCount >= 7
                    && details.ExtentHeight <= details.ViewportHeight + 1 && last.Bottom <= details.ActualHeight
                    && details.VerticalContentAlignment == VerticalAlignment.Top && details.Text.Contains("Scope words found: Yes", StringComparison.Ordinal)
                    && grid.ActualHeight >= 120);
                if (width == 1366) ScreenHarness.Save(window, Path.Combine(output, "complete-protein-details.png"), 1366, 820);
                double shortHeight = details.ActualHeight;
                model.SelectedFamily = null; grid.SelectedItem = model.Proteins[2]; PumpDispatcher(); window.UpdateLayout();
                Check("SF2 " + width + " long details cap at ten lines with visible scrollbar", details.MaxLines == 10 && details.ActualHeight >= shortHeight
                    && details.LineCount > 10 && details.ExtentHeight > details.ViewportHeight
                    && WalkTree(details).OfType<ScrollBar>().Any(bar => bar.Orientation == Orientation.Vertical && bar.IsVisible));
                details.ScrollToEnd(); PumpDispatcher();
                Check("SF2 " + width + " final consequence remains reachable", details.VerticalOffset > 0
                    && details.GetRectFromCharacterIndex(details.Text.Length - 1).Bottom <= details.ActualHeight);
            }
            window.ShowAllOptions = true; window.Width = 1366; window.Height = 768; PumpDispatcher(); window.UpdateLayout();
            Check("SF2 full view keeps ten-line details and table inside the window", grid.ActualHeight >= 120
                && details.TranslatePoint(new Point(0, details.ActualHeight), window).Y < window.ActualHeight - 40);
        }
        finally { window.Close(); PumpDispatcher(); }
    }
}
