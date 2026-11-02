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
    internal static int Run(string[] args)
    {
        if (args.Length != 3 || args[1] is not ("spectrum" or "build"))
            throw new ArgumentException("Supply spectrum/build and a fresh output folder.");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (Application.Current == null)
        {
            var app = new App();
            app.GetType().GetMethod("InitializeComponent")?.Invoke(app, null);
        }
        FrameworkElement root = args[1] == "spectrum"
            ? SpectrumComparisonWindow.Create(Array.Empty<SpectrumComparisonRow>()) : new USearchLib();
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
        if (root is Window window) window.Close();
        File.WriteAllText(Path.Combine(output, "properties.json"), JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("PASS constructed native " + args[1] + " property snapshot: " + values.Count.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}
