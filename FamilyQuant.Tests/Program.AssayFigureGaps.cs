using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssayFigureGapsLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-figure-gaps",
        new[] { "FamilyQuant/PresentationFigures.cs" },
        new[] { "FamilyQuant/PresentationFigures.cs", "FamilyQuant/ChartImageExporter.cs", "FamilyQuant/ChartPalette.cs",
            "FamilyQuant.Core/SequenceLabels.cs", "FamilyQuant.Core/FamilyQuantFile.cs" },
        (output, rows, negative) => AssayFigureGapLedgerCases(output, rows, negative, Path.GetFullPath(args[1])),
        "Native fixture reference-component prefix boundaries and dynamic rendered source-rule fields; no keyboard shortcut claim",
        new[] { "FamilyQuant.Tests/Program.AssayFigureGaps.cs", "FamilyQuant.Tests/AssayFigureGapSnapshot.cs", "FamilyQuant.Tests/AssayFigureGapComparison.cs" });

    private static List<AssayRuntimeCase> AssayFigureGapLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative, string frozenRoot)
    {
        string root = FindLedgerRoot()!, host = Path.Combine(root, "FamilyQuant.Tests/ReferenceGoldenHost/bin/Release/net10.0-windows7.0");
        if (FeatureLedgerEvidence.Hash(Path.Combine(host, "FamilyQuant.dll")) != FeatureLedgerEvidence.Hash("D:/FamilyQuant-data/reference-1.5.0.6/app/FamilyQuant.dll"))
            throw new InvalidDataException("Frozen figure host differs from the read-only reference.");
        string beforePath = Path.Combine(output, "frozen-figure"), afterPath = Path.Combine(output, "current-figure");
        var start = new ProcessStartInfo(Path.Combine(host, "ReferenceGoldenHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(AssayFigureGapSnapshot.Argument); start.ArgumentList.Add(beforePath);
        using (var process = Process.Start(start)!)
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
            File.WriteAllText(Path.Combine(output, "frozen-figure.log"), stdout + stderr);
            if (process.ExitCode != 0) throw new InvalidDataException("Frozen native figure fixture failed: " + stderr);
        }
        foreach (string name in new[] { "FamilyQuant.dll", "ReferenceGoldenHost.dll", "ReferenceGoldenHost.exe" })
            File.Copy(Path.Combine(host, name), Path.Combine(output, "frozen-" + name));
        AssayFigureGapSnapshot.Run(new[] { AssayFigureGapSnapshot.Argument, afterPath });
        var before = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(beforePath, "figure-values.json")))!;
        var after = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(afterPath, "figure-values.json")))!;
        string source = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/PresentationFigures.cs"));
        var anchors = rows.Select(row => new { Row = row, Key = AssayFigureGapComparison.Anchor(row, source) }).Where(value => value.Key != null).ToArray();
        Check("N6j has exactly five observation-specific native fixture anchors", anchors.Length == 5);
        Check("N6j native rendered field changes respond in both product assemblies",
            new[] { "SourceDisplayName", "ConfidenceField", "MinimumConfidence" }.All(key => AssayFigureGapComparison.Matches(key, before, after)));
        if (negative) foreach (string key in after.Keys.ToArray()) after[key] = "changed " + after[key];
        var cases = anchors.Select(value => new AssayRuntimeCase(value.Row.Id,
            value.Key == "reference" ? "native reference summary, empty/single-member/mixed prefix boundaries"
                : "actual exported chart drawing responds to manifest " + value.Key + "; scanner shortcut kind is a g.Key data-field false positive",
            "identical frozen/current native values and dynamic render response", value.Key + ": " + AssayFigureGapComparison.Matches(value.Key!, before, after),
            AssayFigureGapComparison.Matches(value.Key!, before, after))).ToList();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "figure-anchors.json"), anchors.Select(value => new { value.Row.Id, value.Row.Kind, value.Key }));
        return cases;
    }

    private static void AssayFigureGapGuardTests()
    {
        const string source = "public sealed class DeNovoConsolidationChart {\nvoid OnRender() {\nvar line = g.Key.SourceDisplayName;\n}\n}";
        int at = source.IndexOf("Key.SourceDisplayName", StringComparison.Ordinal);
        var row = new FeatureLedger.Observation("FQ15-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "FamilyQuant/PresentationFigures.cs|" + at.ToString(CultureInfo.InvariantCulture) + "|shortcut")))[..12],
            "FamilyQuant/PresentationFigures.cs", 3, "shortcut", "var line = g.Key.SourceDisplayName;");
        Check("figure data-field observation binds its exact frozen rendered consumer", AssayFigureGapComparison.Anchor(row, source) == "SourceDisplayName");
        Check("a different frozen observation ID cannot borrow the renderer", AssayFigureGapComparison.Anchor(row with { Id = "FQ15-WRONG" }, source) == null);
        var before = new Dictionary<string, string> { ["dynamic/SourceDisplayName"] = "True", ["render/base"] = "base", ["render/SourceDisplayName"] = "changed" };
        var after = new Dictionary<string, string>(before);
        Check("figure receipt requires native rendered baseline and mutation agreement", AssayFigureGapComparison.Matches("SourceDisplayName", before, after));
        after["render/SourceDisplayName"] = "borrowed";
        Check("changed rendered output keeps the field observation OPEN", !AssayFigureGapComparison.Matches("SourceDisplayName", before, after));
        before = new() { ["reference/empty"] = "empty", ["reference/boundaries"] = "summary", ["reference/one/fixture"] = "one" };
        after = new(before);
        Check("figure prefix receipt compares each native reference boundary fixture", AssayFigureGapComparison.Matches("reference", before, after));
        after["reference/one/fixture"] = "changed";
        Check("one changed prefix boundary cannot be hidden by matching pooled counts", !AssayFigureGapComparison.Matches("reference", before, after));
    }
}
