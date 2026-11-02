using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssayBuildReportLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-build-report",
        new[] { "FamilyQuant/USearch.xaml.cs" },
        new[] { "FamilyQuant/USearch.Report.cs", "FamilyQuant.Core/IdentityScanRow.cs", "FamilyQuant.Core/GroupingScore.cs" },
        (output, rows, negative) => AssayBuildReportLedgerCases(output, rows, negative, Path.GetFullPath(args[1])),
        "Native identity-report unchanged prefix across empty/no-decoy/clean/mixed/all-mixed fixtures",
        new[] { "FamilyQuant.Tests/Program.AssayBuildReportLedger.cs", "FamilyQuant.Tests/AssayBuildReportSnapshot.cs", "FamilyQuant.Tests/LedgerMemberFactComparison.cs" });

    private static List<AssayRuntimeCase> AssayBuildReportLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative, string frozenRoot)
    {
        string root = FindLedgerRoot()!, host = Path.Combine(root, "FamilyQuant.Tests/ReferenceGoldenHost/bin/Release/net10.0-windows7.0");
        string frozen = Path.Combine(output, "frozen-report"), current = Path.Combine(output, "current-report");
        if (FeatureLedgerEvidence.Hash(Path.Combine(host, "FamilyQuant.dll")) != FeatureLedgerEvidence.Hash("D:/FamilyQuant-data/reference-1.5.0.6/app/FamilyQuant.dll"))
            throw new InvalidDataException("Frozen report host differs from the read-only reference.");
        var start = new ProcessStartInfo(Path.Combine(host, "ReferenceGoldenHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(AssayBuildReportSnapshot.Argument); start.ArgumentList.Add(frozen);
        using (var process = Process.Start(start)!)
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
            File.WriteAllText(Path.Combine(output, "frozen-report.log"), stdout + stderr);
            if (process.ExitCode != 0) throw new InvalidDataException("Frozen native report failed: " + stderr);
        }
        AssayBuildReportSnapshot.Run(new[] { AssayBuildReportSnapshot.Argument, current });
        foreach (string name in new[] { "FamilyQuant.dll", "ReferenceGoldenHost.dll", "ReferenceGoldenHost.exe" })
            File.Copy(Path.Combine(host, name), Path.Combine(output, "frozen-" + name));
        var before = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(frozen, "reports.json")))!;
        var after = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(current, "reports.json")))!;
        string oldSource = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/USearch.xaml.cs"));
        string newSource = File.ReadAllText(Path.Combine(root, "FamilyQuant/USearch.Report.cs"));
        int Body(string source, string method) { var match = Regex.Match(source, @"\b(?:private|public)\s+static\s+string\s+" + Regex.Escape(method) + @"\s*\([^)]*\)\s*\{"); return match.Success ? match.Index + match.Length - 1 : -1; }
        int oldStart = Body(oldSource, "FormatScanReport"), newStart = Body(newSource, "Format");
        // The last instruction names a different action after the approved screen split.
        // Compare the entire preceding source and native output, and leave that last instruction OPEN.
        int oldEnd = oldSource.LastIndexOf("sb.AppendLine(", StringComparison.Ordinal), newEnd = newSource.LastIndexOf("sb.AppendLine(", StringComparison.Ordinal);
        bool sourceAgrees = oldStart >= 0 && newStart >= 0 && oldEnd > oldStart && newEnd > newStart
            && oldSource[(oldStart + 1)..oldEnd].Replace("\r\n", "\n", StringComparison.Ordinal)
                == newSource[(newStart + 1)..newEnd].Replace("\r\n", "\n", StringComparison.Ordinal);
        string Prefix(string report) { string text = report.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'); int line = text.LastIndexOf('\n'); return line < 0 ? "" : text[..line]; }
        var prefixes = before.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
        { Fixture = pair.Key, Frozen = Prefix(pair.Value), Current = after.TryGetValue(pair.Key, out string? text) ? Prefix(text) : "<missing>" }).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "prefixes.json"), prefixes);
        bool nativeAgrees = before.Count == 4 && after.Count == 4 && prefixes.All(pair => pair.Frozen == pair.Current);
        var cases = new List<AssayRuntimeCase>();
        foreach (var row in rows.Where(row => row.Kind is "code fact" or "format/export"))
        {
            var owner = LedgerMemberFactComparison.Compare(row, oldSource, new Dictionary<string, string>());
            if (!owner.FrozenMember.Contains("::private static string FormatScanReport (", StringComparison.Ordinal)) continue;
            bool anchored = false;
            for (int at = oldSource.IndexOf(row.Detail, Math.Max(0, oldStart), StringComparison.Ordinal); at >= 0 && at < oldEnd;
                at = oldSource.IndexOf(row.Detail, at + row.Detail.Length, StringComparison.Ordinal))
                anchored |= row.Id == "FQ15-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Source + "|" + at.ToString(CultureInfo.InvariantCulture) + "|" + row.Kind)))[..12];
            if (anchored) cases.Add(new(row.Id, "complete unchanged report prefix, exact source and four native branch fixtures",
                "identical source prefix and native reports before the changed final navigation instruction", sourceAgrees + "/" + nativeAgrees,
                !negative && sourceAgrees && nativeAgrees));
        }
        return cases;
    }
}
