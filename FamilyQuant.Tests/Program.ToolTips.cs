using System;
using System.Collections.Generic;
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

    private static bool YellowOrGold(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max - min < 0.15) return false;
        double hue = max == r ? 60 * (((g - b) / (max - min)) % 6) : max == g ? 60 * ((b - r) / (max - min) + 2) : 60 * ((r - g) / (max - min) + 4);
        if (hue < 0) hue += 360;
        return hue >= 38 && hue <= 68;
    }
}
