using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record LookStyleRole(string FrozenKey, string CurrentKey, string TargetType, string Role, string Consumer);
    private sealed record LookStyleRow(string Id, string Key, string Property, string Old, string Current);
    private sealed record LookStyleMap(string Source, List<LookStyleRole> Roles, List<LookStyleRow> Rows);
    private sealed record LookStyleResult(string Id, string Theme, bool Matched, string Role, string Property,
        string Frozen, string CurrentDeclaration, string Resolved, string TokenValue, string Consumer, string Assertion);

    private static int LookStyleRoleCommand(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Supply frozen root, explicit role mapping JSON, fresh output and date timestamp.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        var mapping = JsonSerializer.Deserialize<LookStyleMap>(File.ReadAllText(args[2]), FeatureLedgerEvidence.JsonOptions)!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        string frozenPath = Path.Combine(args[1], mapping.Source), currentPath = Path.Combine(root, mapping.Source);
        if (FeatureLedgerEvidence.Hash(frozenPath) != inventory.Files.Single(f => f.Path == mapping.Source).Sha256)
            throw new InvalidDataException("Frozen style source hash differs.");
        var frozen = XDocument.Load(frozenPath, LoadOptions.SetLineInfo);
        var current = XDocument.Load(currentPath, LoadOptions.SetLineInfo);
        if (mapping.Rows.Count != 24 || mapping.Rows.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != 24)
            throw new InvalidDataException("The assigned 24 style rows require unique explicit mappings.");
        var results = new List<LookStyleResult>();
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            EnsureApplication();
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var screen = new DeNovoControl();
                LayoutAt(screen, new Size(1366, 900), -1);
                foreach (var row in mapping.Rows)
                {
                    var observed = inventory.Observations.Single(o => o.Id == row.Id);
                    var role = mapping.Roles.Single(r => r.FrozenKey == row.Key);
                    XElement OldStyle = frozen.Descendants().Single(e => e.Name.LocalName == "Style" && StyleKey(e) == role.FrozenKey);
                    XElement NewStyle = current.Descendants().Single(e => e.Name.LocalName == "Style" && StyleKey(e) == role.CurrentKey);
                    var oldSite = row.Property == "Style.Key" ? OldStyle
                        : OldStyle.Elements().Single(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == row.Property);
                    string before = row.Property == "Style.Key" ? StyleKey(OldStyle)! : (string)oldSite.Attribute("Value")!;
                    var currentSite = row.Property == "Style.Key" ? NewStyle
                        : NewStyle.Elements().Single(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == row.Property);
                    string declaration = row.Property == "Style.Key" ? StyleKey(NewStyle)! : (string)currentSite.Attribute("Value")!;
                    bool frozenValid = observed.Source == mapping.Source && observed.Kind == "option/copy"
                        && observed.Line == ((IXmlLineInfo)oldSite).LineNumber && before == row.Old
                        && observed.Detail == (row.Property == "Style.Key" ? "Style.Key = " : "Setter.Value = ") + row.Old;
                    var style = (Style)screen.Resources[role.CurrentKey];
                    FrameworkElement consumer = role.Consumer.StartsWith("style:", StringComparison.Ordinal)
                        ? WalkTree(screen).OfType<FrameworkElement>().First(e => ReferenceEquals(e.Style, style))
                        : (FrameworkElement)screen.FindName(role.Consumer);
                    var probe = (FrameworkElement)Activator.CreateInstance(style.TargetType)!;
                    probe.Style = style;
                    string resolved = role.CurrentKey, token = "";
                    bool valueValid = true;
                    if (row.Property != "Style.Key")
                    {
                        var descriptor = TypeDescriptor.GetProperties(probe)[row.Property]!;
                        object actual = descriptor.GetValue(probe)!;
                        if (row.Current == "{DynamicResource TextBrush}")
                        {
                            var expected = (SolidColorBrush)Application.Current.FindResource("TextBrush");
                            resolved = (actual as SolidColorBrush)?.Color.ToString(CultureInfo.InvariantCulture) ?? actual.ToString()!;
                            token = expected.Color.ToString(CultureInfo.InvariantCulture);
                            valueValid = actual is SolidColorBrush brush && brush.Color == expected.Color;
                        }
                        else
                        {
                            object? expected = descriptor.Converter.ConvertFromInvariantString(row.Current);
                            valueValid = Equals(actual, expected);
                            resolved = descriptor.Converter.CanConvertTo(typeof(string))
                                ? descriptor.Converter.ConvertToInvariantString(actual)! : Convert.ToString(actual, CultureInfo.InvariantCulture)!;
                        }
                    }
                    bool matched = frozenValid && declaration == row.Current && style.TargetType.Name == role.TargetType
                        && (string?)OldStyle.Attribute("TargetType") == role.TargetType
                        && ReferenceEquals(consumer.Style, style) && valueValid;
                    string assertion = "style role " + row.Id + " / " + theme + ": " + row.Key + "." + row.Property;
                    Check(assertion, matched, "old=" + before + "; current=" + declaration + "; resolved=" + resolved + "; role=" + role.Role);
                    results.Add(new(row.Id, theme.ToString(), matched, role.Role, row.Property, before, declaration, resolved, token, role.Consumer, assertion));
                }
            }
            Themes.ThemeManager.Apply(Themes.AppTheme.Dark);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "proofs.log"), data = Path.Combine(output, "resolved-styles.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false)); FeatureLedgerEvidence.WriteJson(data, results);
        string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
        var artifacts = new List<string> { log, data };
        foreach (string path in new[] { mapping.Source, "FamilyQuant/Themes/ThemeManager.cs", "FamilyQuant/Themes/Tokens.xaml",
            "FamilyQuant.Tests/Program.LookStyleRoles.cs", "FamilyQuant.Tests/Program.Commands.LookLedger.cs" })
        {
            string target = Path.Combine(archive, path.Replace('/', '_')); File.Copy(Path.Combine(root, path), target); artifacts.Add(target);
        }
        foreach (string path in new[] { Path.GetFullPath(args[2]), typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(HelpContent).Assembly.Location })
        {
            string target = Path.Combine(archive, Path.GetFileName(path)); File.Copy(path, target); artifacts.Add(target);
        }
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
        { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record executing commit.");
        const string runId = "LK-style-roles";
        var run = new FeatureLedgerEvidence.Run(runId, Environment.CommandLine, args[4], commit,
            artifacts.Select(p => new FeatureLedgerEvidence.Artifact(p, FeatureLedgerEvidence.Hash(p))).ToList(), root);
        var rows = mapping.Rows.Select(row =>
        {
            var checks = results.Where(r => r.Id == row.Id).ToArray();
            var proofs = checks.All(r => r.Matched) && failed == 0
                ? checks.Select(r => new FeatureLedgerEvidence.Proof(runId, "test", nameof(LookStyleRoleCommand), r.Assertion, log, "")).ToList() : new();
            return new FeatureLedgerEvidence.Row(row.Id,
                new() { new(mapping.Source, "DeNovoControl style role " + row.Key + "." + row.Property, FeatureLedgerEvidence.Hash(currentPath)),
                    new("FamilyQuant/Themes/ThemeManager.cs", "ThemeManager.TextBrush", FeatureLedgerEvidence.Hash(Path.Combine(root, "FamilyQuant/Themes/ThemeManager.cs"))),
                    new("FamilyQuant/Themes/Tokens.xaml", "TextBrush", FeatureLedgerEvidence.Hash(Path.Combine(root, "FamilyQuant/Themes/Tokens.xaml"))) }, proofs,
                "Intentional visual role mapping; colors change with the theme and gold is removed. No numerical, handler or DPI claim.",
                null, "frozen " + row.Key + "." + row.Property + " = " + row.Old + " -> current " + row.Current + "; "
                    + string.Join("; ", checks.Select(c => c.Theme + " resolved " + c.Resolved + " via " + c.Consumer)));
        }).ToList();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "LK-style-roles.json"),
            new FeatureLedgerEvidence.Register(1, "LK-style-roles", FeatureLedger.ReferenceCommit, new() { run }, rows));
        return failed == 0 ? 0 : 1;
    }

    private static string? StyleKey(XElement element) => element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value;
}
