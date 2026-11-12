using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

    internal static SuperClusterPreviewWindow ReviewLayoutWindow(int width = 1366, int height = 768, bool allOptions = true)
    {
        var input = ReviewLayoutFixture();
        return new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null,
            "", "", KeywordMatch.All, null, null), input, _ => input, evidenceMode: true,
            discoverTask: _ => Task.FromResult(new EvidenceTaskScopeInput("Supplied clade",
                Enumerable.Range(0, 5).Select(i => new ReviewTaskTaxon("Invented taxon " + i, 100 + i)).ToArray(),
                Enumerable.Range(0, 12).Select(i => new ReviewTaskKeyword("Supplied keyword " + i, 16 - i)).ToArray(),
                16, new[] { "Supplied function" }, "fixture-release", TargetProteins: 32)))
        {
            Width = width, Height = height, Left = -30000, Top = -30000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowAllOptions = allOptions
        };
    }

    internal static bool ReviewArchitectureFits(SuperClusterPreviewWindow window)
    {
        var architecture = (ArchitectureReviewControl)window.FindName("architectureReview");
        var groups = (DataGrid)architecture.FindName("groupGrid");
        var members = (DataGrid)architecture.FindName("memberGrid");
        return groups.ActualHeight >= 110 && members.ActualHeight >= 110
            && members.TranslatePoint(new Point(0, members.ActualHeight), architecture).Y <= architecture.ActualHeight + 1;
    }

    private static void EvidenceReviewFamilyLayoutTests(string? output = null)
    {
        EnsureApplication();
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (Width: 1024, Height: 768), (Width: 1366, Height: 768), (Width: 1920, Height: 1080) })
            foreach (bool allOptions in new[] { false, true })
            {
                Themes.ThemeManager.Apply(theme);
                var input = ReviewLayoutFixture();
                var window = ReviewLayoutWindow(size.Width, size.Height, allOptions);
                try
                {
                    window.Show(); PumpDispatcher(); window.UpdateLayout();
                    var grid = (DataGrid)window.FindName("familyGrid");
                    grid.ScrollIntoView(grid.Items[0]); PumpDispatcher(); window.UpdateLayout();
                    string label = theme + " " + size.Width + (allOptions ? " full" : " Simple");
                    var flags = (DataGrid)window.FindName("evidenceGrid");
                    var flagText = WalkTree(flags).OfType<TextBlock>().First(t => t.Text == "Longer than reviewed members");
                    Check(label + " hovering the flag text shows guidance and original evidence detail",
                        flagText.ToolTip is string tip && tip.Contains("Check the sequence length", StringComparison.Ordinal)
                        && tip.Contains("Supplied evidence detail.", StringComparison.Ordinal));
                    var text = WalkTree(grid).OfType<TextBlock>().FirstOrDefault(t => t.Text == input.Families[0].SuggestedName);
                    Check(label + " long family name wraps and exposes its full tooltip", text != null && text.TextWrapping == TextWrapping.Wrap
                        && text.ToolTip as string == input.Families[0].SuggestedName && text.ActualHeight >= text.FontSize * 1.1);
                    var scroll = WalkTree(grid).OfType<ScrollViewer>().First();
                    Check(label + " all family columns including Reviewed fit the viewport", scroll.ScrollableWidth <= 1
                        && grid.Columns.Single(c => (string)c.Header == "Reviewed").ActualWidth >= 90);
                    if (output != null)
                    {
                        string path = Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Width + "x" + size.Height, allOptions ? "families.png" : "families-simple.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!); ScreenHarness.Save(window, path, size.Width, size.Height);
                    }
                    var tabs = (TabControl)window.FindName("evidenceTabs");
                    window.ShowAllOptions = true;
                    tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Architecture");
                    PumpDispatcher(); window.UpdateLayout();
                    var architecture = (ArchitectureReviewControl)window.FindName("architectureReview");
                    var legend = architecture.FindName("architectureLegend") as TextBlock;
                    Check(label + " Architecture explains domain groups and selected-member split on Apply", legend?.Text ==
                        "Domain groups in this family; tick Split to give selected members their own family on Apply grouping."
                        && legend.TextWrapping == TextWrapping.NoWrap && legend.ActualHeight > 0);
                    var groups = (DataGrid)architecture.FindName("groupGrid");
                    var members = (DataGrid)architecture.FindName("memberGrid");
                    Check(label + " screens layout gate accepts visible Architecture tables", ReviewArchitectureFits(window));
                    if (size.Height == 768)
                    {
                        var scopeScroll = WalkTree(window).OfType<ScrollViewer>()
                            .Single(s => s.Content is StackPanel panel && panel.Children.OfType<Expander>().Any());
                        var layout = (Grid)scopeScroll.Parent;
                        int scopeRow = Grid.GetRow(scopeScroll);  // 2.1.13: a fixed title and Scope row sit above it
                        var top = layout.RowDefinitions[scopeRow]; var review = (RowDefinition)window.FindName("ReviewTablesRow");
                        var topHeight = top.Height; double topMax = top.MaxHeight, reviewMin = review.MinHeight;
                        try
                        {
                            top.Height = GridLength.Auto; top.MaxHeight = double.PositiveInfinity; review.MinHeight = 0;
                            window.UpdateLayout(); PumpDispatcher();
                            Check(label + " screens gate rejects the old unbounded scope layout", !ReviewArchitectureFits(window));
                        }
                        finally
                        {
                            top.Height = topHeight; top.MaxHeight = topMax; review.MinHeight = reviewMin;
                            window.UpdateLayout(); PumpDispatcher();
                        }
                    }
                    Check(label + " expanded scope leaves two usable Architecture tables", groups.ActualHeight >= 110 && members.ActualHeight >= 110,
                        "groups=" + groups.ActualHeight + "; members=" + members.ActualHeight);
                    Check(label + " Architecture has a row splitter between groups and members", WalkTree(architecture).OfType<GridSplitter>()
                        .Any(s => s.ResizeDirection == GridResizeDirection.Rows && s.ActualHeight >= 6 && s.Background != null
                            && Equals(s.Background, architecture.FindResource("EdgeBrush"))));
                    if (output != null)
                    {
                        string path = Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Width + "x" + size.Height, "architecture.png");
                        ScreenHarness.Save(window, path, size.Width, size.Height);
                    }
                }
                finally { window.Close(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
        Check("screens harness includes synthetic scope and Architecture states", ScreenHarness.Screens.Contains("15-evidence-scope")
            && ScreenHarness.Screens.Contains("16-evidence-architecture"));
    }
}
