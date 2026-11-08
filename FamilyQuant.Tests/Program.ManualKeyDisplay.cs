using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ManualKeyDisplayTests()
    {
        const string name = "Invented hand family";
        string key = ManualFamilyChoices.NewKey(name);
        var panel = new FamilyPanel { Name = "Synthetic key display", Families = new()
        {
            new() { Key = key, DisplayName = name, SourceDecision = "Added by hand" },
            new() { Key = "AA", DisplayName = "Ordinary family" },
            new() { Key = "AL", DisplayName = "Alias family", Decision = FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey = key },
            new() { Key = "manual:notes", DisplayName = "Ordinary prefixed key" },
        } };
        string before = JsonSerializer.Serialize(panel);
        var sections = ReportPanelSections.BuildFromResults(new FamilyClusterFile { FamilyPanel = panel }, new QuantManifest());
        var cells = sections.SelectMany(section => section.Blocks).OfType<ReportTable>().SelectMany(table => table.Rows).SelectMany(row => row).ToArray();
        Check("M65 report text hides generated keys and names a hand-made merge target",
            cells.All(cell => !cell.Contains(key, StringComparison.Ordinal)) && cells.Contains(name));
        Check("M65 ordinary keys and internal report identifiers remain unchanged",
            cells.Contains("AA") && cells.Contains("manual:notes") && sections.Any(section => section.Id == "family:" + key)
            && JsonSerializer.Serialize(panel) == before);
        var runs = new FamilyRunSharesResult { Runs = new() { new() { Run = "fixture", AssignedSignal = 100, MeasuredSignal = 200 } },
            Families = new() { new() { FamilyUserId = 1, MeanShare = .5, PooledShare = .5, AvailableRunCount = 1,
                Runs = new() { new() { Run = "fixture", Signal = 50, ShareOfAssignedSignal = .5, ShareOfMeasuredSignal = .25 } } } } };
        var project = new FamilyClusterFile { FamilyPanel = panel };
        var map = new PanelFamilyMap { IsCurrent = true, UserFamilies = new Dictionary<int, PanelFamilyUserMap>
            { [1] = new() { FamilyUserId = 1, HasConflict = true, PanelKeys = new[] { key, "AA" } } } };
        var grouping = FamilyReportThreshold.Build(runs, project, map);
        var manifest = new QuantManifest { FamilyRunShares = runs };
        var packet = ReportStoredResults.Apply(ReportCurrentResults.Build(new AnalyticsData(), manifest, null), manifest,
            new Dictionary<int, string> { [1] = "Synthetic conflicting group" }, grouping, project);
        var conflict = packet.Sections.Single(section => section.Id == ReportSectionIds.AssignmentConflicts).Blocks.OfType<ReportTable>().Single();
        Check("M65 conflict report text uses names while machine-readable key exports stay exact",
            conflict.Rows.SelectMany(row => row).All(cell => !cell.Contains(key, StringComparison.Ordinal))
            && conflict.Rows.Any(row => row.Any(cell => cell.Contains(name, StringComparison.Ordinal) && cell.Contains("AA", StringComparison.Ordinal)))
            && packet.CsvExports["assignment_conflicts.csv"].Contains(key, StringComparison.Ordinal));

        EnsureApplication();
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var window = new PanelEditorWindow(panel, pfamReview: new PanelPfamFixture())
                { Width = 1080, Height = 768, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    var grid = (DataGrid)window.FindName("FamilyGrid");
                    var hand = window.Draft.Families.Single(row => row.Key == key);
                    var alias = window.Draft.Families.Single(row => row.Key == "AL");
                    grid.SelectedItem = hand; grid.ScrollIntoView(hand, grid.Columns[0]); PumpDispatcher();
                    string Text(DataGridColumn column, PanelEditorRow row) => column.GetCellContent(row) switch
                    { TextBlock text => text.Text, ComboBox { SelectedItem: PanelEditorRow target } => target.Name, _ => "" };
                    Check("M65 " + theme + " editor identifies a hand-made key by its name", Text(grid.Columns[0], hand) == name);
                    grid.CurrentCell = new DataGridCellInfo(hand, grid.Columns[0]);
                    bool editing = grid.BeginEdit(); grid.CancelEdit();
                    Check("M65 " + theme + " internal generated keys are not edited through their displayed name", !editing && hand.Key == key);
                    grid.SelectedItem = alias; grid.ScrollIntoView(alias, grid.Columns[3]); PumpDispatcher();
                    Check("M65 " + theme + " merge target display uses the name while retaining its key",
                        Text(grid.Columns[3], alias) == name && window.Draft.Build().Families.Single(row => row.Key == "AL").MergeTargetFamilyKey == key);
                    hand.Name = "Renamed hand family"; PumpDispatcher();
                    Check("M65 " + theme + " renaming updates display text without regenerating the key",
                        Text(grid.Columns[0], hand) == hand.Name && hand.Key == key
                        && window.Draft.Build().Families.Single(row => row.Key == key).DisplayName == hand.Name);
                    var ordinary = window.Draft.Families.Single(row => row.Key == "AA");
                    grid.SelectedItem = ordinary; grid.ScrollIntoView(ordinary, grid.Columns[0]);
                    grid.CurrentCell = new DataGridCellInfo(ordinary, grid.Columns[0]); PumpDispatcher();
                    bool ordinaryEdit = grid.BeginEdit(); grid.CancelEdit();
                    Check("M65 " + theme + " ordinary keys remain editable and source panel bytes stay unchanged",
                        ordinaryEdit && ordinary.Key == "AA" && JsonSerializer.Serialize(panel) == before);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
