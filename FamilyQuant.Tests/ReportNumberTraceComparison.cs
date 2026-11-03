using FamilyQuant.Report;
using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

/// <summary>Native audit of citation changes, never a new calculation of report results.</summary>
internal static class ReportNumberTraceComparison
{
    internal sealed record Summary(int Tokens, int ChangedCitations, int Ambiguous, int NotFound, string Changes, string Ambiguities);

    internal static void ValidateSources(string before, string after, string document, string folder)
    {
        var inputs = new List<string> { document, ReportSupplementWorkbook.PathForReport(document) };
        if (Directory.Exists(folder)) inputs.AddRange(Directory.GetFiles(folder, "*.csv", SearchOption.AllDirectories));
        ValidatePaths(before, after, inputs);
    }

    internal static void ValidatePaths(string before, string after, IEnumerable<string>? protectedInputs = null)
    {
        before = Path.GetFullPath(before); after = Path.GetFullPath(after);
        if (!File.Exists(before)) throw new FileNotFoundException("The prior trace was not found.", before);
        var inputs = (protectedInputs ?? Array.Empty<string>()).Append(before).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (new[] { after, Path.ChangeExtension(after, ".changes.csv"), Path.ChangeExtension(after, ".ambiguous.csv"), Path.ChangeExtension(after, ".comparison.json") }.Any(inputs.Contains))
            throw new InvalidOperationException("Comparison outputs must not overwrite the prior trace or a report source.");
    }

    internal static Summary Write(string before, string after)
    {
        ValidatePaths(before, after);
        var old = Read(before); var current = Read(after);
        int Old(string column) => old.Columns.ToList().IndexOf(column);
        int New(string column) => current.Columns.ToList().IndexOf(column);
        string[] required = { "location", "number", "status", "csv", "cell", "context" };
        if (required.Any(name => Old(name) < 0 || New(name) < 0) || current.Rows.Count != old.Rows.Count)
            throw new InvalidDataException("Traces do not have matching token rows and required columns.");
        var changes = new List<IReadOnlyList<string>> { new[] { "trace_row", "location", "number", "before_status", "before_csv", "before_cell",
            "after_status", "after_csv", "after_cell", "subject", "match_kind", "candidate_count", "context" } };
        var ambiguous = new List<IReadOnlyList<string>> { new[] { "trace_row" }.Concat(current.Columns).ToArray() };
        for (int i = 0; i < current.Rows.Count; i++)
        {
            var was = old.Rows[i]; var now = current.Rows[i];
            string Value(string column) => New(column) < 0 ? "" : now[New(column)];
            if (new[] { "location", "number", "context" }.Any(name => was[Old(name)] != Value(name)))
                throw new InvalidDataException("Trace token identity changed at CSV row " + (i + 2).ToString(CultureInfo.InvariantCulture));
            string row = (i + 2).ToString(CultureInfo.InvariantCulture);
            if (new[] { "status", "csv", "cell" }.Any(name => was[Old(name)] != Value(name)))
                changes.Add(new[] { row, Value("location"), Value("number"), was[Old("status")], was[Old("csv")], was[Old("cell")],
                    Value("status"), Value("csv"), Value("cell"), Value("subject"), Value("match_kind"), Value("candidate_count"), Value("context") });
            if (Value("status") == "AMBIGUOUS") ambiguous.Add(new[] { row }.Concat(now).ToArray());
        }
        string changesPath = Path.ChangeExtension(after, ".changes.csv"), ambiguousPath = Path.ChangeExtension(after, ".ambiguous.csv");
        Csv(changesPath, changes); Csv(ambiguousPath, ambiguous);
        var result = new Summary(current.Rows.Count, changes.Count - 1, ambiguous.Count - 1,
            current.Rows.Count(row => row[New("status")] == "NOT FOUND"), changesPath, ambiguousPath);
        File.WriteAllText(Path.ChangeExtension(after, ".comparison.json"), JsonSerializer.Serialize(new { Result = result,
            BeforeSha256 = DeliveryWorkspace.Sha256(before), AfterSha256 = DeliveryWorkspace.Sha256(after) }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine("Trace citation comparison: " + result.ChangedCitations.ToString(CultureInfo.InvariantCulture)
            + " changed, " + result.Ambiguous.ToString(CultureInfo.InvariantCulture) + " ambiguous; " + changesPath);
        return result;
    }

    private static ReportTable Read(string path) => ReportCsvTable.Build("trace-comparison", "Trace", File.ReadAllText(path), "");
    private static void Csv(string path, IEnumerable<IReadOnlyList<string>> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        foreach (var row in rows) writer.WriteLine(string.Join(",", row.Select(value => SpreadsheetSafeCsv.Encode(value))));
    }
}
