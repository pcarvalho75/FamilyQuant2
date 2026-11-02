using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Xml;

namespace FamilyQuant.Tests;

// Linked unchanged into ReferenceGoldenHost. Never shows a window, invokes a dialog,
// opens a project, fetches evidence, computes quantitation or writes an app profile.
internal static partial class ShellMetadataSnapshot
{
    internal const string Argument = "--shell-metadata-snapshot";
    internal sealed record Fact(string Id, string Source, int Line, string Kind, string Detail,
        string Anchor, string Property, string Type, string Value, string Reason, string Traits = "");
    internal sealed record Packet(string AssemblyName, string AssemblySha256, List<Fact> Facts);
    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly string[] Sources = ["FamilyQuant/MainWindow.xaml", "FamilyQuant/HomeControl.xaml",
        "FamilyQuant/ClusterGenerationControl.xaml", "FamilyQuant/App.xaml"];

    internal static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply frozen source, inventory and fresh output file.");
        if (File.Exists(args[3]) || Directory.Exists(args[3])) throw new IOException("Snapshot output must be fresh.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (Application.Current == null) { var app = new App(); app.InitializeComponent(); }
        using var inventory = JsonDocument.Parse(File.ReadAllText(args[2]));
        var observations = inventory.RootElement.GetProperty("Observations").EnumerateArray()
            .Where(row => Sources.Contains(row.GetProperty("Source").GetString(), StringComparer.Ordinal))
            .Where(row => row.GetProperty("Kind").GetString() is "control" or "option/copy" or "column")
            .ToArray();
        var documents = Sources.ToDictionary(source => source, source => XDocument.Load(Path.Combine(args[1], source), LoadOptions.SetLineInfo));
        foreach (string source in Sources.Append("FamilyQuant/HomeControl.xaml.cs"))
        {
            string expected = inventory.RootElement.GetProperty("Files").EnumerateArray()
                .Single(file => file.GetProperty("Path").GetString() == source).GetProperty("Sha256").GetString()!;
            if (Hash(Path.Combine(args[1], source)) != expected) throw new InvalidDataException("Frozen source hash differs: " + source);
        }
        var curation = new FamilyClusterFile { MyExtendedFastaItems = new() { new()
        { SequenceIdentifier = "shell_metadata_fixture", Description = "Synthetic shell metadata fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 } } };
        // New shells explicitly disable profile persistence outside their own entry assembly.
        // The frozen constructor only calls InitializeComponent, AppVersion and ShowHome.
        var main = new MainWindow { Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual };
        var home = new HomeControl();
        var families = new ClusterGenerationControl(curation);
        var views = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
        {
            [Sources[0]] = main, [Sources[1]] = home, [Sources[2]] = families
        };
        var facts = new List<Fact>();
        try
        {
            foreach (var view in views.Values)
            {
                view.Measure(new Size(1280, 900)); view.Arrange(new Rect(0, 0, 1280, 900)); view.UpdateLayout();
            }
            Application.Current!.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            foreach (var row in observations)
            {
                string id = row.GetProperty("Id").GetString()!, source = row.GetProperty("Source").GetString()!,
                    kind = row.GetProperty("Kind").GetString()!, detail = row.GetProperty("Detail").GetString()!;
                int line = row.GetProperty("Line").GetInt32();
                var fact = source == "FamilyQuant/App.xaml"
                    ? ObserveApp(id, source, line, kind, detail, documents[source])
                    : Observe(id, source, line, kind, detail, documents[source], views[source]);
                facts.Add(fact);
            }
            ObserveHomeInteractions(inventory.RootElement.GetProperty("Observations").EnumerateArray().ToArray(), home, facts);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
            File.WriteAllText(args[3], JsonSerializer.Serialize(new Packet(typeof(MainWindow).Assembly.GetName().Name!,
                Hash(typeof(MainWindow).Assembly.Location), facts.OrderBy(fact => fact.Id, StringComparer.Ordinal).ToList()), Options) + "\n");
            Console.WriteLine("Native shell metadata: " + facts.Count(fact => fact.Reason.Length == 0) + " observed, "
                + facts.Count(fact => fact.Reason.Length != 0) + " unresolved; windows remain unshown.");
            return 0;
        }
        finally { main.Close(); }
    }

    private static Fact Observe(string id, string source, int line, string kind, string detail, XDocument document, FrameworkElement view)
    {
        Fact No(string reason, string anchor = "", string property = "") => new(id, source, line, kind, detail, anchor, property, "", "", reason);
        int equals = detail.IndexOf(" = ", StringComparison.Ordinal);
        string anchor, property;
        if (kind == "control")
        {
            int space = detail.IndexOf(' ');
            if (space < 1) return No("No control anchor.");
            anchor = detail[(space + 1)..]; property = "$type";
        }
        else
        {
            if (equals < 0) return No("No property anchor.");
            string left = detail[..equals]; int dot = left.LastIndexOf('.');
            if (dot < 1) return No("No named property anchor.");
            anchor = left[..dot]; property = left[(dot + 1)..];
        }
        var nodes = document.Descendants().Where(node => Name(node) == anchor).ToArray();
        if (nodes.Length != 1) return No("Frozen anchor is unnamed or ambiguous.", anchor, property);
        if (kind == "control" && detail != nodes[0].Name.LocalName + " " + anchor)
            return No("Frozen control type differs from its recorded observation.", anchor, property);
        if (kind != "control" && nodes[0].Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == property)?.Value != detail[(equals + 3)..])
            return No("Frozen explicit property differs from its recorded observation.", anchor, property);
        var element = Resolve(view, anchor);
        if (element == null) return No("Current named object is missing or ambiguous in the assigned view.", anchor, property);
        if (!InContent(view, element)) return No("Object is not in the view's content, items or columns.", anchor, property);
        string type = element.GetType().FullName!;
        string traits = "";
        if (element.GetType().Name is "IntegerUpDown" or "DecimalUpDown")
            traits = string.Join(";", new[] { "Minimum", "Maximum", "Value" }.Select(name => name + "="
                + Convert.ToString(Property(element.GetType(), name)!.GetValue(element), CultureInfo.InvariantCulture)));
        if (property == "$type") return new(id, source, line, kind, detail, anchor, property, type, element.GetType().Name, "", traits);
        object? value;
        if (property == "Binding" && element is DataGridBoundColumn column)
        {
            if (column.Binding is not Binding binding) return No("Column does not have a single Binding.", anchor, property);
            value = "Path=" + binding.Path?.Path + ";Mode=" + binding.Mode + ";Format=" + binding.StringFormat
                + ";Converter=" + (binding.Converter?.GetType().FullName ?? "");
        }
        else
        {
            var info = Property(element.GetType(), property);
            if (info == null || info.GetIndexParameters().Length != 0) return No("Property cannot be read on the resolved object.", anchor, property);
            value = info.GetValue(element);
            if (value != null && value is not string && value is not bool && !value.GetType().IsEnum
                && value is not IFormattable) return No("Property value is not a supported scalar.", anchor, property);
        }
        return new(id, source, line, kind, detail, anchor, property, type, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>", "", traits);
    }

    internal static object? Resolve(FrameworkElement view, string name)
    {
        var found = new List<object>();
        foreach (var scope in Content(view).OfType<FrameworkElement>())
            if (scope.FindName(name) is { } match && !found.Any(item => ReferenceEquals(item, match))) found.Add(match);
        return found.Count == 1 ? found[0] : null;
    }

    internal static PropertyInfo? Property(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
            if (current.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } property) return property;
        return null;
    }

    private static Fact ObserveApp(string id, string source, int line, string kind, string detail, XDocument document)
    {
        Fact No(string reason) => new(id, source, line, kind, detail, "", "", "", "", reason);
        var nodes = document.Descendants().Where(node => ((IXmlLineInfo)node).LineNumber == line).ToArray();
        if (nodes.Length != 1) return No("Frozen style line is ambiguous.");
        var node = nodes[0];
        int equals = detail.IndexOf(" = ", StringComparison.Ordinal);
        if (equals < 0) return No("Not an App property observation.");
        string expected = detail[(equals + 3)..];
        if (node.Name.LocalName is "SolidColorBrush" or "Style" && detail.Contains(".Key = ", StringComparison.Ordinal))
        {
            if (node.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value != expected)
                return No("Frozen resource key differs from the inventory.");
            object? resource = Application.Current!.TryFindResource(expected);
            if (resource == null) return No("The frozen named resource has no current entry.");
            return new(id, source, line, kind, detail, expected, "resource key", resource.GetType().FullName!, expected, "");
        }
        if (node.Name.LocalName != "Setter" || node.Attribute("Value")?.Value != expected
            || node.Ancestors().Any(parent => parent.Name.LocalName.Contains("Trigger", StringComparison.Ordinal)
                || parent.Name.LocalName == "ControlTemplate")) return No("Template/trigger facts require their own state check or render.");
        var style = node.Ancestors().FirstOrDefault(parent => parent.Name.LocalName == "Style");
        string? target = style?.Attribute("TargetType")?.Value, property = node.Attribute("Property")?.Value;
        if (target == null || property == null || style!.Attributes().Any(attribute => attribute.Name.LocalName == "Key"))
            return No("No unique implicit control-style anchor.");
        Type? type = target switch
        {
            "Button" => typeof(Button), "CheckBox" => typeof(CheckBox), "ComboBox" => typeof(ComboBox),
            "ComboBoxItem" => typeof(ComboBoxItem), "TextBox" => typeof(TextBox), "MenuItem" => typeof(MenuItem), "ToolTip" => typeof(ToolTip), _ => null
        };
        if (type == null || Application.Current!.TryFindResource(type) is not Style currentStyle) return No("Implicit control style is unavailable.");
        var control = (FrameworkElement)Activator.CreateInstance(type)!;
        control.Style = currentStyle;
        control.Measure(new Size(400, 200)); control.Arrange(new Rect(0, 0, 400, 200)); control.ApplyTemplate();
        object? value;
        if (property.StartsWith("ToolTipService.", StringComparison.Ordinal))
        {
            string field = property[(property.IndexOf('.') + 1)..] + "Property";
            if (typeof(ToolTipService).GetField(field)?.GetValue(null) is not DependencyProperty dependencyProperty) return No("Attached property is unsupported.");
            value = control.GetValue(dependencyProperty);
        }
        else
        {
            var info = Property(type, property);
            if (info == null) return No("Style property is unsupported.");
            value = info.GetValue(control);
            if (value is Style) return No("A focus-visual object needs a focus-state proof.");
        }
        return new(id, source, line, kind, detail, target + " implicit style", property, type.FullName!,
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>", "");
    }

    private static bool InContent(FrameworkElement view, object target) => Content(view).Any(item => ReferenceEquals(item, target));
    private static IEnumerable<object> Content(object root)
    {
        var seen = new HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>(); pending.Push(root);
        while (pending.TryPop(out object? item))
        {
            if (!seen.Add(item)) continue;
            yield return item;
            if (item is DataGrid table) foreach (var column in table.Columns) pending.Push(column);
            if (item is ItemsControl items) foreach (object child in items.Items) pending.Push(child);
            if (item is DependencyObject node)
                foreach (object child in LogicalTreeHelper.GetChildren(node)) if (child is DependencyObject) pending.Push(child);
        }
    }
    private static string? Name(XElement node) => node.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value;
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}
