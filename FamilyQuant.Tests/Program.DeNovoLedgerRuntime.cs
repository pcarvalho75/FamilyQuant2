using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int DeNovoLedgerRuntimeCommand(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Supply frozen source root, frozen app folder, fresh output and a timestamp from date.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row)
            && row.Source is "FamilyQuant/DeNovoControl.xaml" or "FamilyQuant/DeNovoControl.xaml.cs").ToArray();
        foreach (var file in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
            if (FeatureLedgerEvidence.Hash(Path.Combine(frozenRoot, file.Path)) != file.Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + file.Path);
        Directory.CreateDirectory(output);
        string legacyPath = Path.Combine(Path.GetFullPath(args[2]), "FamilyQuant.dll");
        var legacyAssembly = Assembly.LoadFrom(legacyPath);
        EnsureApplication();
        string oldSource = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/DeNovoControl.xaml.cs"));
        var columnStatements = Regex.Matches(oldSource, @"(?m)^\s*(?<field>Column\w+)\.HeaderStyle\s*=\s*HeaderTip\([\s\S]*?\);")
            .Cast<Match>().Select(match => new { Field = match.Groups["field"].Value,
                First = 1 + oldSource.AsSpan(0, match.Index).Count('\n'),
                Last = 1 + oldSource.AsSpan(0, match.Index + match.Length).Count('\n') }).ToArray();
        var facts = new Dictionary<string, (string Path, string Symbol, string Property)>(StringComparer.Ordinal);
        foreach (var row in cohort)
        {
            if (row.Source.EndsWith(".xaml", StringComparison.Ordinal))
            {
                if (row.Kind == "control")
                {
                    string[] pieces = row.Detail.Split(' ');
                    if (pieces.Length == 2 && pieces[0] != pieces[1]) facts[row.Id] = ("FamilyQuant/DeNovoControl.xaml", pieces[1], "control");
                }
                else if (row.Kind is "tooltip" or "option/copy" or "column")
                {
                    int separator = row.Detail.IndexOf(" = ", StringComparison.Ordinal), dot = row.Detail.IndexOf('.');
                    if (dot > 0 && separator > dot)
                    {
                        string name = row.Detail[..dot], property = row.Detail[(dot + 1)..separator];
                        if (property is not ("Binding" or "ItemsSource" or "ClipboardContentBinding"))
                            facts[row.Id] = ("FamilyQuant/DeNovoControl.xaml", name, property);
                    }
                }
            }
            else if (row.Kind == "code fact")
            {
                var statements = columnStatements.Where(statement => row.Line >= statement.First && row.Line <= statement.Last).ToArray();
                if (statements.Length == 1) facts[row.Id] = ("FamilyQuant/DeNovoControl.Tables.cs", statements[0].Field, "HeaderStyle.ToolTip");
            }
        }
        var before = facts.Keys.ToDictionary(id => id, _ => new SortedDictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var after = facts.Keys.ToDictionary(id => id, _ => new SortedDictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var results = new List<DeNovoDeclarationResult>();
        var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            string provenance = legacyAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            Check("De novo runtime comparison uses the program built from the exact frozen source commit",
                provenance.EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal));
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var legacy = (UserControl)Activator.CreateInstance(legacyAssembly.GetType("FamilyQuant.DeNovoControl", true)!)!;
                var current = new DeNovoControl();
                var oldWindow = LedgerDeNovoWindow(legacy); var newWindow = LedgerDeNovoWindow(current);
                try
                {
                    oldWindow.Show(); newWindow.Show(); PumpDispatcher();
                    foreach (var expander in WalkTree(legacy).Concat(WalkTree(current)).OfType<Expander>()) expander.IsExpanded = true;
                    PumpDispatcher();
                    Capture(theme + "/initial-source");
                    foreach (string source in new[] { "peaks-denovo-only", "peaks-denovo-all", "deepnovo" })
                    {
                        foreach (var control in new[] { legacy, current })
                        {
                            var combo = (ComboBox)control.FindName("ComboBoxDeNovoSource");
                            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == source);
                        }
                        PumpDispatcher();
                        Capture(theme + "/" + source);
                    }
                    void Capture(string key)
                    {
                        foreach (var fact in facts)
                        {
                            if (LedgerDeNovoValue(legacy, fact.Value.Symbol, fact.Value.Property) is string oldValue) before[fact.Key][key] = oldValue;
                            if (LedgerDeNovoValue(current, fact.Value.Symbol, fact.Value.Property) is string newValue) after[fact.Key][key] = newValue;
                        }
                    }
                }
                finally { newWindow.Close(); oldWindow.Close(); }
            }
            foreach (var row in cohort)
            {
                bool mapped = facts.TryGetValue(row.Id, out var fact);
                string oldValue = mapped ? JsonSerializer.Serialize(before[row.Id]) : "", newValue = mapped ? JsonSerializer.Serialize(after[row.Id]) : "";
                bool matched = mapped && before[row.Id].Count == 8 && after[row.Id].Count == 8 && oldValue == newValue;
                string reason = matched ? "Native frozen/current control or resolved property agrees in both themes, initially and in all three legacy source modes."
                    : mapped ? "A named runtime value is missing or differs; no closure." : "This row needs another behaviour check or an explicit reviewed mapping.";
                results.Add(new(row.Id, row.Kind, matched, oldValue, newValue, mapped ? fact.Path : "",
                    mapped ? "FamilyQuant.DeNovoControl." + fact.Symbol + "." + fact.Property : "",
                    row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + " " + row.Detail + " -> "
                    + (mapped ? fact.Symbol + "." + fact.Property : "unmapped") + "; frozen=" + oldValue + "; current=" + newValue, reason));
                if (matched) Check("De novo runtime " + row.Id + ": " + fact.Symbol + "." + fact.Property, true);
                else { skipped++; Console.WriteLine("  SKIP  " + row.Id + ": " + reason); }
            }
            foreach (var group in results.Where(result => result.Matched).GroupBy(result => result.Kind))
            {
                string assertion = "De novo runtime kind " + group.Key + ": every listed value agrees initially and across both themes and all legacy sources";
                Check(assertion, group.All(result => result.Before == result.Actual)); assertions[group.Key] = assertion;
            }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); Themes.ThemeManager.Apply(Themes.AppTheme.Dark); }
        var sources = Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "DeNovo*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*DeNovo*.xaml"))
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core/DeNovo"), "*.cs"))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        return CompleteDeNovoLedgerProof(root, frozenRoot, output, args[4], results, assertions, transcript.ToString(),
            sources, "EV-denovo-runtime", nameof(DeNovoLedgerRuntimeCommand),
            "Native control/property comparison only, not handler execution or numerical export validation.",
            new[] { legacyPath }, sources);
    }

    private static Window LedgerDeNovoWindow(UserControl control) => new()
    { Content = control, Width = 1024, Height = 768, Left = -30000, Top = -30000, ShowInTaskbar = false,
        ShowActivated = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };

    private static string? LedgerDeNovoValue(UserControl control, string name, string property)
    {
        object? target = control.FindName(name);
        if (target == null) return null;
        if (property == "control")
        {
            bool visible = target is FrameworkElement element ? element.IsVisible
                : target is DataGridColumn column && column.Visibility == Visibility.Visible
                    && WalkTree(control).OfType<DataGrid>().Any(grid => grid.IsVisible && grid.Columns.Contains(column));
            return visible && PresentationSource.FromVisual(control) != null ? target.GetType().Name + ": visible in hosted view" : null;
        }
        object? value;
        if (property == "HeaderStyle.ToolTip") value = (target as DataGridColumn)?.HeaderStyle?.Setters.OfType<Setter>()
            .LastOrDefault(setter => setter.Property == FrameworkElement.ToolTipProperty)?.Value;
        else value = target.GetType().GetProperty(property)?.GetValue(target);
        return value switch
        {
            string text => text,
            bool flag => flag ? "true" : "false",
            int number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            Enum enumeration => enumeration.ToString(),
            Thickness thickness => string.Join(",", new[] { thickness.Left, thickness.Top, thickness.Right, thickness.Bottom }
                .Select(number => number.ToString("R", CultureInfo.InvariantCulture))),
            _ => null,
        };
    }
}
