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
    internal static EvidenceReviewInput ReviewLayoutFixture()
    {
        const string family = "Supplied family with a long descriptive name that must remain readable during review";
        var proteins = Enumerable.Range(0, 32).Select(i => new ReviewProteinInput(i, "fixture_" + i, 1, family,
            "Reviewed neighbour", "Supplied evidence detail", true, "Supplied reviewed relative",
            i == 0 ? new[] { "coherence.length.above: Supplied evidence detail." } : Array.Empty<string>(), i != 0,
            "Invented review protein " + i, "Domain A", Reviewed: i % 2 == 0, Length: 120, Cysteines: 4, HasPfamCalls: true)).ToArray();
        return new EvidenceReviewInput("synthetic-layout", proteins, new[] { new ReviewFamilyInput(family, family, 32, 16) },
            Array.Empty<ReviewMarkerInput>(), new[] { new ReviewArchitectureInput(family, true,
                Enumerable.Range(0, 4).Select(i => new ReviewArchitectureGroupInput("group" + i, "Supplied split " + i,
                    "Domain A", new[] { "Domain A", "Domain " + i }, Enumerable.Range(i * 6, 6).ToArray(), 3)).ToArray(),
                Enumerable.Range(24, 8).Select(i => new ReviewArchitectureUndeterminedInput(i, "Supplied placement refusal")).ToArray()) });
    }

    private static void EvidenceReviewFamilyLayoutTests(string? output = null)
    {
        EnsureApplication();
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (Width: 1366, Height: 768), (Width: 1920, Height: 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var input = ReviewLayoutFixture();
                var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null,
                    "", "", KeywordMatch.All, null, null), input, null, evidenceMode: true)
                {
                    Width = size.Width, Height = size.Height, Left = -30000, Top = -30000,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize
                };
                try
                {
                    window.Show(); PumpDispatcher(); window.UpdateLayout();
                    var grid = (DataGrid)window.FindName("familyGrid");
                    grid.ScrollIntoView(grid.Items[0]); PumpDispatcher(); window.UpdateLayout();
                    string label = theme + " " + size.Width;
                    var text = WalkTree(grid).OfType<TextBlock>().FirstOrDefault(t => t.Text == input.Families[0].SuggestedName);
                    Check(label + " long family name wraps and exposes its full tooltip", text != null && text.TextWrapping == TextWrapping.Wrap
                        && text.ToolTip as string == input.Families[0].SuggestedName && text.ActualHeight >= text.FontSize * 1.1);
                    var scroll = WalkTree(grid).OfType<ScrollViewer>().First();
                    Check(label + " all family columns including Reviewed fit the viewport", scroll.ScrollableWidth <= 1
                        && grid.Columns.Single(c => (string)c.Header == "Reviewed").ActualWidth >= 90);
                    if (output != null)
                    {
                        string path = Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Width + "x" + size.Height, "families.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!); ScreenHarness.Save(window, path, size.Width, size.Height);
                    }
                }
                finally { window.Close(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }
}
