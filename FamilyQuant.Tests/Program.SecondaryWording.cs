using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string SecondaryWordingHeader = "lane\trow_id\told_text\tnew_text\treason";
    private static string SecondaryWordingCell(string value) => value.Replace("\r", "", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);

    // Decision list only. This never creates a proof, changes a ledger state or judges equivalence.
    private static void SecondaryWording(string output, string name, string source, IReadOnlyDictionary<int, string> values, bool includeUnchanged = false)
    {
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var lines = new List<string> { SecondaryWordingHeader };
        foreach (var row in inventory.Observations.Where(row => row.Source == source && values.ContainsKey(row.Line)).OrderBy(row => row.Id, StringComparer.Ordinal))
        {
            if (row.Kind is not ("code fact" or "format/export" or "option/copy")) continue;
            string old = row.Detail;
            if (old.StartsWith('"')) old = JsonSerializer.Deserialize<string>(old)!;
            else if (old.Contains(" = ", StringComparison.Ordinal))
            {
                int split = old.IndexOf(" = ", StringComparison.Ordinal);
                string property = old[..split];
                if (!property.EndsWith(".Content", StringComparison.Ordinal) && !property.EndsWith(".Text", StringComparison.Ordinal)) continue;
                old = old[(split + 3)..];
            }
            else continue;
            string actual = values[row.Line];
            if (old == actual && !includeUnchanged) continue;
            lines.Add(string.Join('\t', new[] { "FQFloater", row.Id, old, actual,
                old == actual ? "Current native text is identical; no semantic proof inferred. Listed as unproved copy for N6a or Paulo's ruling under roadmap v4."
                    : "Current native screen wording differs; Paulo ruling required under M44. No semantic proof or closure inferred." }.Select(SecondaryWordingCell)));
        }
        File.WriteAllLines(Path.Combine(output, "wording-" + name + ".tsv"), lines, new UTF8Encoding(false));
    }

    private static string SecondaryHeaderTip(System.Windows.Controls.DataGridColumn column) => column.HeaderStyle?.Setters
        .OfType<System.Windows.Setter>().FirstOrDefault(setter => setter.Property == System.Windows.FrameworkElement.ToolTipProperty)?.Value?.ToString() ?? "[No current header tooltip]";

    private static int SecondaryAppendWording(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Supply a native proof output folder and the shared Paulo wording TSV.");
        string folder = Path.GetFullPath(args[1]), target = Path.GetFullPath(args[2]);
        var proposed = Directory.GetFiles(folder, "wording-*.tsv").OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(path => File.ReadAllLines(path).Skip(1)).Where(line => line.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(line => line, StringComparer.Ordinal).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // A short exclusive append avoids interleaving another lane's row. Existing rows are never rewritten.
        using var stream = new FileStream(target, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        string previous = reader.ReadToEnd();
        if (previous.Length > 0 && previous.Split('\n')[0].TrimEnd('\r') != SecondaryWordingHeader) throw new InvalidDataException("Unexpected shared wording-list columns.");
        var existing = previous.Split('\n').Select(line => line.TrimEnd('\r')).ToHashSet(StringComparer.Ordinal);
        string[] added = proposed.Where(line => !existing.Contains(line)).ToArray();
        stream.Seek(0, SeekOrigin.End);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
        if (previous.Length == 0) writer.WriteLine(SecondaryWordingHeader);
        else if (!previous.EndsWith('\n')) writer.WriteLine();
        foreach (string line in added) writer.WriteLine(line);
        Console.WriteLine(FormattableString.Invariant($"Appended {added.Length} FQFloater wording rows; {proposed.Length} distinct native candidates. No ledger states changed."));
        return 0;
    }

    private static int SecondaryBoxAudit(string[] args)
    {
        if (args.Length != 5 || args[3] is not ("N6k" or "N6l")) throw new ArgumentException("Supply native ledger audit, shared wording TSV, N6k/N6l and output folder.");
        string[] sources = args[3] == "N6k" ? new[] { "FamilyQuant/ComputeXicWindow.xaml", "FamilyQuant/ComputeXicWindow.xaml.cs" }
            : new[] { "FamilyQuant/SuperClusterPreviewWindow.cs", "FamilyQuant/LockManagerWindow.cs", "FamilyQuant/IdentityScanWindow.cs", "FamilyQuant/IdentityScanChart.cs" };
        var rows = JsonSerializer.Deserialize<List<SecondaryLedgerAuditRow>>(File.ReadAllText(Path.Combine(args[1], "mapping.json")), FeatureLedgerEvidence.JsonOptions)!
            .Where(row => sources.Contains(row.Source, StringComparer.Ordinal) && row.State != "APPROVED_DROP").ToArray();
        var listed = File.ReadAllLines(args[2]).Skip(1).Select(line => line.Split('\t')).Where(fields => fields.Length == 5
            && fields[0] == "FQFloater" && fields[2].Length > 0 && fields[3].Length > 0 && fields[4].Length > 0)
            .Select(fields => fields[1]).ToHashSet(StringComparer.Ordinal);
        var missing = rows.Where(row => row.State != "VERIFIED" && !listed.Contains(row.Id)).OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        Directory.CreateDirectory(args[4]);
        FeatureLedgerEvidence.WriteJson(Path.Combine(args[4], args[3] + ".json"), new { Step = args[3], Total = rows.Length,
            Verified = rows.Count(row => row.State == "VERIFIED"), Wording = rows.Count(row => row.State != "VERIFIED" && listed.Contains(row.Id)), Missing = missing });
        Console.WriteLine(FormattableString.Invariant($"{args[3]}: {rows.Length} rows, {rows.Count(row => row.State == "VERIFIED")} verified, {rows.Count(row => row.State != "VERIFIED" && listed.Contains(row.Id))} listed, {missing.Length} missing."));
        foreach (var row in missing) Console.WriteLine(row.Id + " " + row.Source + ":" + row.Line + " " + row.Kind + " " + row.Detail);
        return missing.Length == 0 ? 0 : 1;
    }
}
