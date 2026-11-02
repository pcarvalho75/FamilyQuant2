using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record ShellWordingValue(string Id, string Before, string Current, string Reason);
    private const string NoCurrentEquivalent = "no current equivalent";

    private static string WordingCell(string value) => value.Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal)
        .Replace("\u2013", "\\u2013", StringComparison.Ordinal).Replace("\u2014", "\\u2014", StringComparison.Ordinal);

    private static void WriteShellWording(string path, IEnumerable<ShellWordingValue> rows)
    {
        var lines = new[] { "lane\trow_id\told_text\tnew_text\treason" }.Concat(rows.OrderBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => string.Join("\t", "FQShell", row.Id, WordingCell(row.Before), WordingCell(row.Current), WordingCell(row.Reason))));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static List<ShellWordingValue> CaptureShellWording(FeatureLedger.Observation[] rows,
        IReadOnlyDictionary<string, string>? priorMain = null)
    {
        EnsureApplication();
        var main = OffscreenProofWindow(new MainWindow());
        var curation = new ClusterGenerationControl(new FamilyClusterFile { MyExtendedFastaItems = new()
        { new() { SequenceIdentifier = "wording_capture_fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 } } });
        var report = OffscreenProofWindow(TextReportWindow.Create("Captured report title", "Captured report body"));
        var corpus = new Dictionary<string, List<(string Name, string Text)>>(StringComparer.Ordinal);
        void Add(string feature, string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!corpus.TryGetValue(feature, out var values)) corpus[feature] = values = new();
            foreach (string part in new[] { value }.Concat(value.Split(["\r\n\r\n", "\n\n", "\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)))
                if (part.Length > 0 && !values.Any(item => item.Text == part)) values.Add((name, part));
        }
        void Collect(string feature, FrameworkElement root)
        {
            root.Measure(new Size(1280, 900)); root.Arrange(new Rect(0, 0, 1280, 900)); root.UpdateLayout();
            foreach (var element in WalkTree(root))
            {
                if (element is TextBlock text) Add(feature, "compiled TextBlock.Text", text.Text);
                if (element is ContentControl { Content: string content }) Add(feature, "compiled control content", content);
                if (element is FrameworkElement { ToolTip: string tip }) Add(feature, "compiled tooltip", tip);
                if (element is Control control) Add(feature, "compiled FontFamily.Source", control.FontFamily?.Source);
                if (element is DataGrid grid)
                {
                    foreach (var column in grid.Columns)
                    {
                        if (column.Header is string header) Add(feature, "compiled column header", header);
                        for (Style? style = column.HeaderStyle; style != null; style = style.BasedOn)
                            foreach (var setter in style.Setters.OfType<Setter>())
                                if (setter.Property == FrameworkElement.ToolTipProperty && setter.Value is string headerTip)
                                    Add(feature, "compiled column tooltip", headerTip);
                    }
                    if (grid.ContextMenu is { } menu)
                        foreach (var item in menu.Items.OfType<MenuItem>())
                        { Add("grid", "compiled context-menu header", item.Header as string); Add("grid", "compiled context-menu tooltip", item.ToolTip as string); }
                }
            }
        }
        string? NativeProperty(FrameworkElement root, FeatureLedger.Observation row)
        {
            if (row.Kind == "control")
            {
                string name = row.Detail.Split(' ').Last();
                return ShellMetadataSnapshot.Resolve(root, name)?.GetType().Name is { } type ? type + " " + name : null;
            }
            var property = Regex.Match(row.Detail, @"^(\w+)\.(\w+) = ");
            if (!property.Success) return null;
            object? target = ShellMetadataSnapshot.Resolve(root, property.Groups[1].Value);
            var info = target == null ? null : ShellMetadataSnapshot.Property(target.GetType(), property.Groups[2].Value);
            if (info == null || info.GetIndexParameters().Length != 0) return null;
            object? value = info.GetValue(target);
            return value is string || value is bool || value?.GetType().IsEnum == true || value is IFormattable
                ? property.Groups[1].Value + "." + property.Groups[2].Value + " = " + Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }
        try
        {
            Collect("main", main); Collect("curation", curation); Collect("report", report);
            foreach (string method in new[] { "WorkflowText", "CurrentContextHelp", "ProjectStatusText" })
                Add("main", "compiled MainWindow." + method, typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(main, null) as string);
            foreach (var mode in Enum.GetValues<NormalizationMode>())
            foreach (var policy in Enum.GetValues<SharedPeptidePolicy>())
                Add("curation", "compiled HelpContent.Build(" + mode + "," + policy + ")", HelpContent.Build(null, mode, policy));
            var values = new List<ShellWordingValue>();
            foreach (var row in rows.OrderBy(row => row.Id, StringComparer.Ordinal))
            {
                string feature = row.Source.Contains("MainWindow", StringComparison.Ordinal) ? "main"
                    : row.Source.Contains("ClusterGeneration", StringComparison.Ordinal) ? "curation"
                    : row.Source.Contains("GridCopy", StringComparison.Ordinal) ? "grid" : "report";
                string? current = null;
                string reason = row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + ". ";
                if (row.Source.EndsWith(".xaml", StringComparison.Ordinal))
                {
                    current = NativeProperty(feature == "main" ? main : curation, row);
                    reason += current != null ? "Read from the compiled unshown default-fixture control/property. "
                        : "The original named owner/property has no captured compiled scalar in the current view. ";
                }
                else if (row.Source == "FamilyQuant/ReportView.cs")
                {
                    current = row.Line <= 47 ? ReportView.StatusLine(null, SharedPeptidePolicy.FamilyUnique, 1001, 2002)
                        : ReportView.StatusLine(NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique, 1001, 2002);
                    reason += "Compiled ReportView.StatusLine output; counts are labelled synthetic fixture values. ";
                }
                else if (row.Source == "FamilyQuant/TextReportWindow.cs" && row.Kind == "code control")
                {
                    current = WalkTree(report).OfType<TextBox>().FirstOrDefault()?.GetType().Name;
                    reason += "Compiled unshown report control type. ";
                }
                else if (priorMain != null && feature == "main" && priorMain.TryGetValue(row.Id, out string? captured))
                {
                    current = captured;
                    reason += "Retained native MainWindow report/property capture with matching executing product assemblies. ";
                }
                else
                {
                    string? old = null;
                    try { if (row.Detail.StartsWith('"')) old = JsonSerializer.Deserialize<string>(row.Detail); }
                    catch (JsonException) { }
                    if (old is { Length: >= 4 } && corpus.TryGetValue(feature, out var choices))
                    {
                        var match = choices.Where(value => value.Text.Contains(old, StringComparison.Ordinal)).OrderBy(value => value.Text.Length).FirstOrDefault();
                        if (match.Text != null) { current = match.Text; reason += match.Name + "; exact old fragment located inside this captured feature text. "; }
                    }
                    if (current == null) reason += row.Kind is "handler" or "handler definition"
                        ? "This handler's displayed path was not executed by the capture. "
                        : "No corresponding current user-facing text was found in the captured compiled feature values; unchanged/internal tokens are left to N6a. ";
                }
                if (row.Detail.Contains("ButtonHelp", StringComparison.Ordinal) || row.Detail.Contains("ButtonAlignSelected", StringComparison.Ordinal)
                    || row.Detail.Contains("ButtonManageLocks", StringComparison.Ordinal))
                    reason += "Window packet: FL-secondary-runtime, D:/FamilyQuant-data/out-refresh/20261102-070844-FamilyQuant-gate/secondary-runtime; M6 permits this listed boundary. ";
                values.Add(new(row.Id, row.Detail, current ?? NoCurrentEquivalent,
                    reason + "Tracking decision only: no semantic equivalence, disposition change or DROP."));
            }
            return values;
        }
        finally { report.Close(); main.Close(); }
    }

    private static int ShellReviewWordingCommand(string[] args)
    {
        if (args.Length != 6 || !DateTimeOffset.TryParse(args[5], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
            throw new ArgumentException("Supply review gaps, current shared list, retained native Main packet, fresh output and date.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Review wording output must be fresh.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        if (FeatureLedger.Fingerprint(inventory) != FeatureLedger.ReferenceInventorySha256) throw new InvalidDataException("Frozen inventory differs.");
        var ids = File.ReadLines(args[1]).Skip(1).Select(line => line.Split('\t')[2]).ToHashSet(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(args[2]).Skip(1))
        { var cells = line.Split('\t'); if (cells.Length >= 5 && cells[0] == "FQShell") ids.Add(cells[1]); }
        var rows = inventory.Observations.Where(row => ids.Contains(row.Id)).ToArray();
        if (rows.Length != ids.Count) throw new InvalidDataException("A review/list ID is outside the frozen inventory.");
        foreach (string assembly in new[] { typeof(MainWindow).Assembly.Location, typeof(FamilyClusterFile).Assembly.Location })
            if (FeatureLedgerEvidence.Hash(assembly) != FeatureLedgerEvidence.Hash(Path.Combine(args[3], "executed", Path.GetFileName(assembly))))
                throw new InvalidDataException("Retained native Main capture uses different product bytes.");
        var native = JsonSerializer.Deserialize<List<ShellMainComparison>>(File.ReadAllText(Path.Combine(args[3], "message-comparisons.json")), FeatureLedgerEvidence.JsonOptions)!;
        var prior = native.Where(row => row.Actual.Length > 0).ToDictionary(row => row.Id, row => row.Actual, StringComparer.Ordinal);
        Directory.CreateDirectory(output);
        var captured = CaptureShellWording(rows, prior);
        WriteShellWording(Path.Combine(output, "wording-for-paulo.tsv"), captured);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "captures.json"), captured);
        File.Copy(args[1], Path.Combine(output, "review-gaps.tsv"));
        Check("review wording captures every requested/listed frozen ID", captured.Count == ids.Count && captured.Select(row => row.Id).Distinct().Count() == ids.Count);
        Check("review wording contains compiled/native values or the exact explicit no-equivalent decision",
            captured.All(row => row.Current.Length > 0 && !row.Current.StartsWith('<') && row.Reason.Length > 0));
        Console.WriteLine("Review list: " + captured.Count + " rows; " + captured.Count(row => row.Current == NoCurrentEquivalent) + " explicit no current equivalent decisions.");
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int ShellReviewWordingCheck(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Supply review-gap TSV and wording TSV.");
        var rows = File.ReadLines(args[2]).Skip(1).Select(line => line.Split('\t')).Where(cells => cells.Length >= 5 && cells[0] == "FQShell")
            .GroupBy(cells => cells[1]).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var gaps = File.ReadLines(args[1]).Skip(1).Select(line => line.Split('\t')[2]).Distinct().ToArray();
        foreach (string id in gaps)
            Check("review-list row " + id + " has current text or exact no current equivalent with a reason", rows.TryGetValue(id, out var row)
                && row[2].Length > 0 && row[3].Length > 0 && !row[3].StartsWith('<') && row[4].Length > 0);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
