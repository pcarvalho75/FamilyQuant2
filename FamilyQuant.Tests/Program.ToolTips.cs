using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ToolTipCommandTable() =>
    [
        new("--test-tooltip-style", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-tooltip-style", (_, _) =>
        {
            ToolTipStyleTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-tooltips", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-tooltips", (_, _) =>
        {
            ToolTipCheckTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    /// <summary>Z12q1: one tooltip behaviour and look for every control (design rule 7, docs/TOOLTIPS.md).</summary>
    private static void ToolTipStyleTests()
    {
        Section("Z12q1 the shared tooltip style and defaults");
        EnsureApplication();
        var elements = new DependencyObject[] { new Button(), new TextBlock(), new MenuItem(), new CheckBox(), new TabItem(), new Expander(), new Hyperlink() };
        Check("Z12q1 every element opens its tooltip after 500 ms, keeps it 20 s and shows it when disabled",
            elements.All(e => ToolTipService.GetInitialShowDelay(e) == Themes.ToolTipDefaults.InitialDelayMilliseconds
                && ToolTipService.GetShowDuration(e) == Themes.ToolTipDefaults.ShowDurationMilliseconds && ToolTipService.GetShowOnDisabled(e)),
            string.Join(" ", elements.Select(e => e.GetType().Name + "=" + ToolTipService.GetInitialShowDelay(e) + "/" + ToolTipService.GetShowDuration(e) + "/" + ToolTipService.GetShowOnDisabled(e))));
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var tip = new ToolTip { Content = "One or two plain sentences." };
                var host = new Window { Content = new Button { Content = "Host", ToolTip = tip }, Width = 300, Height = 200, Left = -30000, Top = -30000,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    host.Show(); tip.ApplyTemplate(); PumpDispatcher();
                    var brushes = new[] { tip.Background, tip.Foreground, tip.BorderBrush }.OfType<SolidColorBrush>().ToArray();
                    Check("Z12q1 " + theme + " tooltips wrap at 380 px on the theme's surface, text and edge tokens",
                        tip.MaxWidth == Themes.ToolTipDefaults.MaximumWidth && brushes.Length == 3
                        && ReferenceEquals(tip.Background, Application.Current.Resources["SurfaceBrush"]) && ReferenceEquals(tip.Foreground, Application.Current.Resources["TextBrush"])
                        && ReferenceEquals(tip.BorderBrush, Application.Current.Resources["EdgeBrush"]), tip.MaxWidth + " " + string.Join(",", brushes.Select(b => b.Color)));
                    Check("Z12q1 " + theme + " tooltip colours carry no yellow or gold", brushes.All(b => !YellowOrGold(b.Color)), string.Join(",", brushes.Select(b => b.Color)));
                }
                finally { host.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }

        foreach (bool appearance in new[] { false, true })
        {
            var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { new { Name = "a" } } };
            var column = new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding("Name") };
            Themes.TableAppearance.SetHeaderToolTip(column, "The family name shown in tables and reports.");
            grid.Columns.Add(column);
            if (appearance) Themes.TableAppearance.SetEnabled(grid, true);
            var window = new Window { Content = grid, Width = 400, Height = 200, Left = -30000, Top = -30000,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show(); PumpDispatcher(); window.UpdateLayout(); PumpDispatcher();
                var header = WalkTree(window).OfType<DataGridColumnHeader>().FirstOrDefault(h => ReferenceEquals(h.Column, column));
                Check("Z12q1 a column header shows the one-attribute tooltip" + (appearance ? " under the table appearance" : ""),
                    header?.ToolTip as string == "The family name shown in tables and reports.", header?.ToolTip?.ToString()
                        ?? "no header among " + string.Join(",", WalkTree(window).OfType<DataGridColumnHeader>().Select(h => (h.Column?.Header ?? "null") + ":" + (h.ToolTip ?? "none"))));
            }
            finally { window.Close(); PumpDispatcher(); }
        }
    }

    /// <summary>Z12q6: rule 7 in every window the census opens. Each window passes when every required control has a
    /// tooltip that says more than its label, and when no tooltip has a dash, a colour word or a D21 word.</summary>
    private static void ToolTipCheckTests()
    {
        Section("Z12q6 every control explains itself on hover (rule 7)");
        EnsureApplication();
        var previous = Themes.ThemeManager.Current;
        List<ToolTipCensusTable> tables;
        try { Themes.ThemeManager.Apply(Themes.AppTheme.Dark); tables = ToolTipCensus(); }
        finally { Themes.ThemeManager.Apply(previous); }
        foreach (var table in tables)
        {
            string window = table.Window + " (" + table.Owner + ")";
            if (table.Note.StartsWith("OPEN FAILED", StringComparison.Ordinal) || table.Note.Contains("NOT OPENED", StringComparison.Ordinal))
            { Check(window + " opens for the tooltip check", false, table.Note); continue; }
            var missing = table.Rows.Where(r => r.Status != "OK" && !CensusInformationalKinds.Contains(r.Kind)).ToArray();
            Check(window + ": every control has a tooltip that says more than its label", missing.Length == 0,
                missing.Length + " controls: " + string.Join("; ", missing.Select(r => r.Status + " " + r.Kind + " \"" + r.Label + "\"" + (r.Name.Length > 0 ? " (" + r.Name + ")" : string.Empty))));
            var wording = table.Rows.Where(r => r.Wording.Length > 0).ToArray();
            Check(window + ": tooltips have no dash, colour word or D21 word", wording.Length == 0,
                string.Join("; ", wording.Select(r => r.Wording + " in " + r.Kind + " \"" + r.Label + "\"")));
        }
        var cellRows = ToolTipCensusCellProbe();
        Check("Z12q6 the census walks into table cells: one row per column template, NONE for a bare check box, OK with a tooltip",
            cellRows.Count(r => r.Kind == "check box") == 2
            && cellRows.SingleOrDefault(r => r.Kind == "check box" && r.Host.EndsWith("column \"Bare\"", StringComparison.Ordinal))?.Status == "NONE"
            && cellRows.SingleOrDefault(r => r.Kind == "check box" && r.Host.EndsWith("column \"Explained\"", StringComparison.Ordinal))?.Status == "OK",
            string.Join("; ", cellRows.Select(r => r.Kind + "|" + r.Host + "|" + r.Status)));
        Check("Z12q6 the check would notice a bare control and a tip that repeats its label",
            ToolTipCensusProbe(new Button { Content = "Save" }) == "NONE" && ToolTipCensusProbe(new Button { Content = "Save", ToolTip = "Save." }) == "REPEATS-LABEL"
            && ToolTipCensusProbe(new Button { Content = "Save", ToolTip = "Writes the project to its .fqproj file. Undo does not apply to files." }) == "OK");
    }

    private static bool YellowOrGold(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max - min < 0.15) return false;
        double hue = max == r ? 60 * (((g - b) / (max - min)) % 6) : max == g ? 60 * ((b - r) / (max - min) + 2) : 60 * ((r - g) / (max - min) + 4);
        if (hue < 0) hue += 360;
        return hue >= 38 && hue <= 68;
    }
}
