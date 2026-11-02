using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using System.Text.Json;
using System.Reflection;

namespace FamilyQuant.Tests;

// Linked into the frozen host: both snapshots construct the actual product view.
internal static class AssayViewSnapshot
{
    internal const string Argument = "--assay-view-snapshot";
    internal static void ConfigureOffscreen(Window window)
    {
        window.Left = -30000; window.Top = -30000;
        window.ShowActivated = false; window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
    }
    internal static int Run(string[] args)
    {
        if (args.Length != 3 || args[1] is not ("spectrum" or "spectrum-refusals" or "spectrum-details" or "build"))
            throw new ArgumentException("Supply spectrum/spectrum-refusals/spectrum-details/build and a fresh output folder.");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (Application.Current == null)
        {
            var app = new App();
            app.GetType().GetMethod("InitializeComponent")?.Invoke(app, null);
        }
        var rows = args[1] == "spectrum-details" ? Enumerable.Range(1, 1234).Select(scan => new SpectrumComparisonRow
            { FileName = "synthetic.raw", ScanNumber = scan }).ToArray() : Array.Empty<SpectrumComparisonRow>();
        FrameworkElement root = args[1].StartsWith("spectrum", StringComparison.Ordinal)
            ? SpectrumComparisonWindow.Create(rows, args[1] == "spectrum-details" ? "fixture/comparison.csv" : null) : new USearchLib();
        if (root is Window fixtureWindow) ConfigureOffscreen(fixtureWindow);
        root.Measure(new Size(1200, 900)); root.Arrange(new Rect(0, 0, 1200, 900)); root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string Text(object? value) => value switch
        {
            null => "<null>", TextBlock text => text.Text,
            IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture) ?? "<null>",
            _ => value.ToString() ?? "<null>"
        };
        var seen = new HashSet<DependencyObject>();
        void Walk(DependencyObject node)
        {
            if (!seen.Add(node)) return;
            if (node is FrameworkElement element && element.Name.Length > 0)
            {
                string key = "named/" + element.Name + "/";
                values.Add(key + "type", element.GetType().Name);
                values.Add(key + "Name", element.Name);
                values.Add(key + "tooltip", Text(element.ToolTip));
                values.Add(key + "enabled", element.IsEnabled.ToString());
                if (element is TextBlock block) values.Add(key + "Text", block.Text);
                if (element is TextBox box) values.Add(key + "Text", box.Text);
                if (element is ContentControl content && content.Content is string) values.Add(key + "Content", Text(content.Content));
                if (element is ToggleButton toggle) values.Add(key + "IsChecked", Text(toggle.IsChecked));
                foreach (string property in new[] { "Value", "Minimum", "Maximum", "Increment" })
                {
                    Type? type = element.GetType(); PropertyInfo? info = null;
                    while (type != null && info == null)
                    {
                        info = type.GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                        type = type.BaseType;
                    }
                    if (info != null) values.Add(key + property, Text(info.GetValue(element)));
                }
            }
            if (node is DataGrid grid)
            {
                values.Add("grid/columnTypes", string.Join("|", grid.Columns.Select(column => column.GetType().Name).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)));
                foreach (var column in grid.Columns.OfType<DataGridBoundColumn>())
                {
                    if (column.Binding is not Binding binding || binding.Path == null) continue;
                    string key = "columns/" + binding.Path.Path + "/";
                    values.Add(key + "header", Text(column.Header));
                    values.Add(key + "format", Text(binding.StringFormat));
                    var tips = column.HeaderStyle?.Setters.OfType<Setter>().Where(setter => setter.Property == FrameworkElement.ToolTipProperty).ToArray();
                    values.Add(key + "tooltip", tips?.Length == 1 ? Text(tips[0].Value) : "<missing-or-ambiguous>");
                }
            }
            foreach (object child in LogicalTreeHelper.GetChildren(node)) if (child is DependencyObject dependency) Walk(dependency);
        }
        Walk(root);
        if (root is Window shownWindow) values.Add("window/title", shownWindow.Title);
        if (args[1].StartsWith("spectrum", StringComparison.Ordinal))
        {
            var blocks = seen.OfType<TextBlock>().ToArray();
            var count = blocks.Where(block => System.Text.RegularExpressions.Regex.IsMatch(block.Text, @"^[0-9,]+ precursor-hypothesis row\(s\)$")).ToArray();
            if (count.Length == 1)
            { values.Add("view/count", count[0].Text); values.Add("view/countTooltip", Text(count[0].ToolTip)); }
            var path = blocks.Where(block => block.Text.StartsWith("CSV: ", StringComparison.Ordinal)
                || block.Text == "No automatic CSV path is available for this view.").ToArray();
            if (path.Length == 1)
            { values.Add("view/path", path[0].Text); values.Add("view/pathTooltip", Text(path[0].ToolTip)); }
            var explanations = blocks.Where(block => block.Text.StartsWith("One row per normalized file", StringComparison.Ordinal)).Select(block => block.Text).ToList();
            foreach (var button in seen.OfType<Button>())
            {
                object? store = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(button);
                if (store == null) continue;
                var handlers = store.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(store, new object[] { ButtonBase.ClickEvent }) as Array;
                if (handlers == null) continue;
                foreach (object handlerInfo in handlers)
                {
                    var handler = handlerInfo.GetType().GetProperty("Handler")?.GetValue(handlerInfo) as Delegate;
                    object? target = handler?.Target;
                    if (target?.GetType().GetField("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) is string help)
                        explanations.Add(help);
                }
            }
            if (explanations.Count == 1) values.Add("view/explanation", explanations[0]);
        }
        if (args[1] == "spectrum-refusals")
        {
            var textBlocks = seen.OfType<TextBlock>().ToArray();
            foreach (string name in new[] { "ButtonSaveSpectrumComparisonCsv", "ButtonSaveAcceptedScoreScatter" })
            {
                var button = seen.OfType<Button>().Single(item => item.Name == name);
                var before = textBlocks.ToDictionary(item => item, item => item.Text);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var changed = textBlocks.Where(item => item.Text != before[item]).ToArray();
                if (changed.Length != 1) throw new InvalidDataException("The actual routed click did not update one refusal status: " + name);
                values.Add("actions/" + name + "/refusal", changed[0].Text);
            }
        }
        if (root is Window window) window.Close();
        File.WriteAllText(Path.Combine(output, "properties.json"), JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("PASS constructed native " + args[1] + " property snapshot: " + values.Count.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}
