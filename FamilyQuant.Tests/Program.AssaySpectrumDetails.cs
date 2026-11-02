using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssaySpectrumDetailsLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-spectrum-details",
        new[] { "FamilyQuant/SpectrumComparisonWindow.cs" },
        Directory.GetFiles(Path.Combine(FindLedgerRoot()!, "FamilyQuant"), "SpectrumComparisonWindow*.cs")
            .Select(path => Path.GetRelativePath(FindLedgerRoot()!, path).Replace('\\', '/'))
            .Concat(new[] { "FamilyQuant/SecondaryWindows.ViewModels.cs", "FamilyQuant/SecondaryWindowView.cs" }).ToArray(),
        (output, rows, negative) => AssaySpectrumDetailsLedgerCases(output, rows, negative, Path.GetFullPath(args[1])),
        "Actual empty/populated view count/path and explanation relocated into its live help callback",
        new[] { "FamilyQuant.Tests/Program.AssaySpectrumDetails.cs", "FamilyQuant.Tests/AssaySpectrumDetailComparison.cs",
            "FamilyQuant.Tests/Program.AssaySpectrumLedger.cs", "FamilyQuant.Tests/AssaySpectrumPropertyComparison.cs", "FamilyQuant.Tests/AssayViewSnapshot.cs" });

    private static List<AssayRuntimeCase> AssaySpectrumDetailsLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative, string frozenRoot)
    {
        var (emptyBefore, emptyAfter) = AssayNativeViewPair(Path.Combine(output, "empty"), "spectrum");
        var (fullBefore, fullAfter) = AssayNativeViewPair(Path.Combine(output, "populated"), "spectrum-details");
        string source = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/SpectrumComparisonWindow.cs"));
        var comparisons = rows.Select(row => AssaySpectrumDetailComparison.Compare(row, source, emptyBefore, fullBefore, emptyAfter, fullAfter)).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "detail-anchors.json"), comparisons);
        var exact = comparisons.Where(value => value.Passed).Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (negative) foreach (string key in fullAfter.Keys.ToArray()) fullAfter[key] = "changed " + fullAfter[key];
        return rows.Where(row => exact.Contains(row.Id)).Select(row =>
        {
            var value = AssaySpectrumDetailComparison.Compare(row, source, emptyBefore, fullBefore, emptyAfter, fullAfter);
            return new AssayRuntimeCase(value.Id, value.Key + ": " + value.Reason, value.Expected, value.Actual, value.Passed);
        }).ToList();
    }
}
