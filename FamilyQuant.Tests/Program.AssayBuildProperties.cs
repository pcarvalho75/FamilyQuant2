using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssayBuildPropertiesLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-build-properties",
        new[] { "FamilyQuant/USearch.xaml" },
        Directory.GetFiles(Path.Combine(FindLedgerRoot()!, "FamilyQuant"), "USearch*")
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(FindLedgerRoot()!, path).Replace('\\', '/'))
            .Concat(new[] { "FamilyQuant/NumericUpDown.cs" }).ToArray(),
        (output, rows, negative) => AssayBuildPropertiesLedgerCases(output, rows, negative, Path.GetFullPath(args[1])),
        "Actual constructed named build controls and bound scalar defaults/limits",
        new[] { "FamilyQuant.Tests/Program.AssayBuildProperties.cs", "FamilyQuant.Tests/AssayBuildPropertyComparison.cs",
            "FamilyQuant.Tests/Program.AssaySpectrumLedger.cs", "FamilyQuant.Tests/AssaySpectrumPropertyComparison.cs", "FamilyQuant.Tests/AssayViewSnapshot.cs" });

    private static List<AssayRuntimeCase> AssayBuildPropertiesLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative, string frozenRoot)
    {
        var (before, actual) = AssayNativeViewPair(output, "build");
        string source = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/USearch.xaml"));
        var comparisons = rows.Select(row => AssayBuildPropertyComparison.Compare(row, source, before, actual)).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "build-anchors.json"), comparisons);
        var exactIds = comparisons.Where(value => value.Passed).Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (negative) foreach (string key in actual.Keys.ToArray()) actual[key] += " changed";
        return rows.Where(row => exactIds.Contains(row.Id)).Select(row =>
        {
            var value = AssayBuildPropertyComparison.Compare(row, source, before, actual);
            return new AssayRuntimeCase(value.Id, value.Key + ": " + value.Reason, value.Expected, value.Actual, value.Passed);
        }).ToList();
    }

    private static void AssayBuildPropertyTests()
    {
        const string source = "<UserControl xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><IntegerUpDown x:Name='Identity' Value='85' Minimum='1' Maximum='100'/></UserControl>";
        var scalar = new FeatureLedger.Observation("fixture", "FamilyQuant/USearch.xaml", 1, "option/copy", "Identity.Value = 85");
        var control = scalar with { Kind = "control", Detail = "IntegerUpDown Identity" };
        var before = new Dictionary<string, string> { ["named/Identity/Value"] = "85", ["named/Identity/type"] = "IntegerUpDown" };
        var current = new Dictionary<string, string>(before);
        Check("build scalar receipt compares the frozen explicit value with both native controls",
            AssayBuildPropertyComparison.Compare(scalar, source, before, current).Passed);
        Check("build control receipt requires actual named materialization in both product trees",
            AssayBuildPropertyComparison.Compare(control, source, before, current).Passed);
        current["named/Identity/Value"] = "84";
        Check("a changed bound build default keeps its frozen row OPEN",
            !AssayBuildPropertyComparison.Compare(scalar, source, before, current).Passed);
        current = new() { ["named/Other/Value"] = "85", ["named/Other/type"] = "IntegerUpDown" };
        Check("a different numeric control cannot lend its value or type",
            !AssayBuildPropertyComparison.Compare(scalar, source, before, current).Passed
            && !AssayBuildPropertyComparison.Compare(control, source, before, current).Passed);
        Check("build property construction never claims a handler executed",
            !AssayBuildPropertyComparison.Compare(scalar with { Kind = "handler" }, source, before, before).Passed);
    }
}
