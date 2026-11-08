using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void EvidenceProteinDetailsTests(string? output = null)
    {
        Section("Selected protein details - complete facts and tick consequences");
        EnsureApplication();
        var input = ReviewLayoutFixture();
        string reason = "Full invented reason: " + string.Join(" ", Enumerable.Repeat("recorded evidence detail", 35));
        string warning = "18 cysteines; reviewed range 8 to 12 (10 members: 3 in your file, 7 from the reference panel, all species).";
        var first = input.Proteins[0] with { Reason = reason, Flags = new[] { "coherence.cysteines.above: " + warning,
            "domain_compatibility: Supplied domain warning." }, Neighbour = "invented_relative; 82% identity; 91% of query residues (91/100)",
            Domains = "Invented domain A; Invented domain B", HasScopeAnnotation = true, SuggestedSelected = false };
        input = input with { Proteins = input.Proteins.Select(p => p.ProteinIndex == 0 ? first : p).ToArray() };
        string before = JsonSerializer.Serialize(input);
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (Width: 1366, Height: 768), (Width: 1920, Height: 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null), input, null, evidenceMode: true)
                {
                    Width = size.Width, Height = size.Height, Left = -30000, Top = -30000,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize
                };
                try
                {
                    window.Show(); PumpDispatcher(); window.UpdateLayout();
                    var model = ((GroupingReviewViewModel)window.DataContext).Evidence!;
                    var grid = (DataGrid)window.FindName("evidenceGrid");
                    grid.SelectedItem = model.Proteins[0]; PumpDispatcher();
                    var details = window.FindName("proteinDetails") as TextBox;
                    string prefix = theme + " " + size.Width;
                    Check(prefix + " selected detail keeps every supplied fact without trimming", details != null && details.IsReadOnly
                        && details.TextWrapping == TextWrapping.Wrap && details.VerticalScrollBarVisibility == ScrollBarVisibility.Auto
                        && details.Text.Contains(reason, StringComparison.Ordinal) && details.Text.Contains(warning, StringComparison.Ordinal)
                        && details.Text.Contains("Supplied domain warning.", StringComparison.Ordinal)
                        && details.Text.Contains(first.Neighbour, StringComparison.Ordinal) && details.Text.Contains(first.Domains, StringComparison.Ordinal)
                        && details.Text.Contains("Scope words found: Yes", StringComparison.Ordinal));
                    Check(prefix + " unticked detail retains current family and quantitation", details?.Text.Contains("Unticked: stays in its current family and is still quantified", StringComparison.Ordinal) == true);
                    model.Proteins[0].Selected = true; model.Families[0].Name = "Renamed invented family"; PumpDispatcher();
                    Check(prefix + " detail follows manual tick and edited family name", details?.Text.Contains("Ticked: joins Renamed invented family on Apply grouping", StringComparison.Ordinal) == true);
                    model.Proteins[0].Selected = false; PumpDispatcher();
                    Check(prefix + " table and details both have visible space", grid.ActualHeight >= 110 && details?.ActualHeight >= 65
                        && details.TranslatePoint(new Point(0, details.ActualHeight), window).Y < window.ActualHeight - 40);
                    if (output != null)
                    {
                        string path = Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Width + "x" + size.Height + ".png");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!); ScreenHarness.Save(window, path, size.Width, size.Height);
                    }
                    model.SearchText = "no_such_invented_protein"; PumpDispatcher();
                    Check(prefix + " filtering away the selected row clears stale details", details?.Text.StartsWith("Select a protein", StringComparison.Ordinal) == true);
                    Check(prefix + " review detail and ticks leave proposal source facts unchanged", JsonSerializer.Serialize(input) == before
                        && grid.SelectionMode == DataGridSelectionMode.Extended);
                }
                finally { window.Close(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }
}
