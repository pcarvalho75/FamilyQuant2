using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssaySpectrumExportLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-spectrum-exports",
        new[] { "FamilyQuant/SpectrumComparisonWindow.cs" },
        Directory.GetFiles(Path.Combine(FindLedgerRoot()!, "FamilyQuant"), "SpectrumComparisonWindow*.cs")
            .Select(path => Path.GetRelativePath(FindLedgerRoot()!, path).Replace('\\', '/'))
            .Concat(new[] { "FamilyQuant/SecondaryWindows.ViewModels.cs", "FamilyQuant/ShellDialogs.cs", "FamilyQuant/AppOperationFiles.cs",
                "FamilyQuant/AppOperationHost.cs", "FamilyQuant/PresentationFigures.cs", "FamilyQuant/ChartImageExporter.cs",
                "FamilyQuant.Core/DeNovo/SpectrumComparisonTable.cs", "FamilyQuant.Core/Quantitation/Studies/DeNovoAcceptedScoreScatterStudy.cs" }).ToArray(),
        (output, rows, negative) => AssaySpectrumExportLedgerCases(output, rows, negative, Path.GetFullPath(args[1])),
        "Actual synthetic native spectrum CSV/plot dialog, success, failure and cancellation paths",
        new[] { "FamilyQuant.Tests/Program.AssaySpectrumExport.cs", "FamilyQuant.Tests/Program.AssaySpectrumExportLedger.cs",
            "FamilyQuant.Tests/AssaySpectrumExportComparison.cs", "FamilyQuant.Tests/AssaySpectrumPropertyComparison.cs" });

    private static List<AssayRuntimeCase> AssaySpectrumExportLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative, string frozenRoot)
    {
        string folder = Path.Combine(output, "exports");
        if (AssaySpectrumExportProbeCommand(new[] { "--assay-spectrum-export-probe", folder }) != 0)
            throw new InvalidDataException("Actual native export probe failed; no row can close.");
        var actual = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(folder, "export-actuals.json")))!;
        string source = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/SpectrumComparisonWindow.cs"));
        var comparisons = rows.Select(row => AssaySpectrumExportComparison.Compare(row, source, actual)).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "export-anchors.json"), comparisons);
        var exactIds = comparisons.Where(value => value.Passed).Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (negative) foreach (string key in actual.Keys.ToArray()) if (!key.StartsWith("inputs/", StringComparison.Ordinal))
            actual[key] = key.EndsWith("/companion", StringComparison.Ordinal) ? actual[key] + ".changed" : "changed " + actual[key];
        return rows.Where(row => exactIds.Contains(row.Id)).Select(row =>
        {
            var value = AssaySpectrumExportComparison.Compare(row, source, actual);
            return new AssayRuntimeCase(value.Id, value.Key + ": " + value.Reason, value.Expected, value.Actual, value.Passed);
        }).ToList();
    }
}
