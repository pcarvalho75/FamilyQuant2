using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int LookHomeProbe(string[] args)
    {
        if (args.Length is not (2 or 3) || (args.Length == 3 && args[2] != "--negative-control"))
            throw new ArgumentException("Supply a fresh native snapshot output and optional --negative-control.");
        bool negative = args.Length == 3;
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output); EnsureApplication();
        var snapshots = new List<object>();
        HomeStartTests(output);
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        {
            Themes.ThemeManager.Apply(theme);
            var home = new HomeControl();
            var window = new Window { Content = home, Width = 1366, Height = 900, Left = -30000, Top = -30000,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                PumpDispatcher(); window.UpdateLayout();
                var elements = new[] { "ButtonHomeOpen", "ButtonHomeBuild", "ButtonHomeCurate", "ButtonHomeCompute", "ButtonHomeAnalytics", "ButtonHomeDeNovo", "TextBlockHomeProject" }
                    .Select(name => (Name: name, Element: (FrameworkElement)home.FindName(name))).ToArray();
                foreach (var item in elements)
                {
                    var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (string property in new[] { "Width", "ActualWidth", "MinWidth", "Margin", "Padding", "FontSize", "FontWeight", "Foreground", "Background", "Cursor", "Focusable", "IsTabStop", "HorizontalContentAlignment", "VerticalContentAlignment", "SnapsToDevicePixels", "FocusVisualStyle", "Content", "ToolTip" })
                    {
                        var descriptor = TypeDescriptor.GetProperties(item.Element)[property];
                        if (descriptor == null) continue;
                        object? value = descriptor.GetValue(item.Element);
                        values[property] = value is SolidColorBrush brush ? brush.Color.ToString(CultureInfo.InvariantCulture)
                            : value is Style style ? "Style target=" + style.TargetType.Name + "; setters=" + style.Setters.Count.ToString(CultureInfo.InvariantCulture)
                            : value == null ? "null" : descriptor.Converter.CanConvertTo(typeof(string))
                                ? descriptor.Converter.ConvertToInvariantString(value)! : value.ToString()!;
                    }
                    values["ToolTipService.ShowOnDisabled"] = ToolTipService.GetShowOnDisabled(item.Element).ToString(CultureInfo.InvariantCulture);
                    values["ToolTipService.ShowDuration"] = ToolTipService.GetShowDuration(item.Element).ToString(CultureInfo.InvariantCulture);
                    snapshots.Add(new { theme = theme.ToString(), item.Name, type = item.Element.GetType().Name, values });
                }
                snapshots.Add(new { theme = theme.ToString(), role = "Home text", text = WalkTree(home).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0).ToArray() });
                foreach (var button in WalkTree(home).OfType<Button>().Where(b => b.TemplatedParent == null))
                {
                    var visual = WalkTree(button).OfType<FrameworkElement>().Where(e => e.Name.Length > 0)
                        .Select(e => new { e.Name, type = e.GetType().Name }).ToArray();
                    snapshots.Add(new { theme = theme.ToString(), button = button.Name, visual });
                }
                string[] actions = { "ButtonHomeBuild", "ButtonHomeCurate", "ButtonHomeCompute", "ButtonHomeAnalytics", "ButtonHomeDeNovo" };
                var counts = new int[5];
                home.BuildFamiliesRequested += () => counts[0]++;
                home.CurateRequested += () => counts[1]++;
                home.ComputeXicRequested += () => counts[2]++;
                home.AnalyticsRequested += () => counts[3]++;
                home.DeNovoRequested += () => counts[4]++;
                home.SetProjectState(true, "Synthetic Home navigation fixture");
                // A planted lost-enablement regression must fail the whole-screen check.
                if (negative) ((Button)home.FindName("ButtonHomeCompute")).IsEnabled = false;
                bool actionsValid = true;
                foreach (int index in Enumerable.Range(0, actions.Length))
                {
                    Array.Clear(counts);
                    var button = (Button)home.FindName(actions[index]);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    bool correct = button.IsEnabled && button.Focusable && counts[index] == 1 && counts.Sum() == 1;
                    actionsValid &= correct;
                    snapshots.Add(new { theme = theme.ToString(), action = actions[index], button.IsEnabled, button.Focusable, counts = counts.ToArray(), correct });
                }
                Check(theme + " native Home screen retains all five independent navigation actions", actionsValid);
                home.SetProjectState(false, "No project open.");
                var disabled = new[] { "ButtonHomeCompute", "ButtonHomeAnalytics" }.Select(name => (Button)home.FindName(name)).ToArray();
                Check(theme + " native Home screen gates project actions with readable disabled explanations",
                    disabled.All(button => !button.IsEnabled && ToolTipService.GetShowOnDisabled(button)
                        && ToolTipService.GetShowDuration(button) == 60000 && button.ToolTip is string text && text.Contains("Disabled until a project is open.", StringComparison.Ordinal)));
                ScreenHarness.Save(window, Path.Combine(output, "home-" + theme + ".png"), 1366, 900);
            }
            finally { window.Close(); }
        }
        Themes.ThemeManager.Apply(Themes.AppTheme.Dark);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "native-home-values.json"), snapshots);
        Console.WriteLine("Native Home style/value probe retained: " + output);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
