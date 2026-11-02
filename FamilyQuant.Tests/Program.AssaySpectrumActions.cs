using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssaySpectrumActionsLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-spectrum-actions",
        new[] { "FamilyQuant/SpectrumComparisonWindow.cs" },
        Directory.GetFiles(Path.Combine(FindLedgerRoot()!, "FamilyQuant"), "SpectrumComparisonWindow*.cs")
            .Select(path => Path.GetRelativePath(FindLedgerRoot()!, path).Replace('\\', '/'))
            .Concat(new[] { "FamilyQuant/SecondaryWindowView.cs", "FamilyQuant/SecondaryWindows.ViewModels.cs" }).ToArray(),
        (output, rows, negative) => AssaySpectrumActionsLedgerCases(output, rows, negative, Path.GetFullPath(args[1])),
        "Constructed named spectrum controls/properties and actual routed disconnected-window refusals",
        new[] { "FamilyQuant.Tests/Program.AssaySpectrumActions.cs", "FamilyQuant.Tests/Program.AssaySpectrumLedger.cs",
            "FamilyQuant.Tests/AssaySpectrumActionComparison.cs", "FamilyQuant.Tests/AssaySpectrumPropertyComparison.cs", "FamilyQuant.Tests/AssayViewSnapshot.cs" });

    private static List<AssayRuntimeCase> AssaySpectrumActionsLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative, string frozenRoot)
    {
        var (before, actual) = AssayNativeViewPair(output, "spectrum-refusals");
        string source = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/SpectrumComparisonWindow.cs"));
        var comparisons = rows.Select(row => AssaySpectrumActionComparison.Compare(row, source, before, actual)).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "action-anchors.json"), comparisons);
        var exactIds = comparisons.Where(value => value.Passed).Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (negative)
        {
            if (exactIds.Count == 0) throw new InvalidDataException("Negative control has no eligible native action/property targets.");
            foreach (string key in actual.Keys.ToArray()) actual[key] += " changed";
        }
        return rows.Where(row => exactIds.Contains(row.Id)).Select(row =>
        {
            var value = AssaySpectrumActionComparison.Compare(row, source, before, actual);
            return new AssayRuntimeCase(value.Id, value.Key + ": " + value.Reason, value.Expected, value.Actual, value.Passed);
        }).ToList();
    }

    private static void AssaySpectrumActionTests()
    {
        const string source = "var save = new Button { Name = \"ButtonSaveSpectrumComparisonCsv\", Content = \"Save CSV\" };\n"
            + "save.Click += (_, __) => SaveCopy(save, status, rows);\n"
            + "private static void SaveCopy(Button button, TextBlock status) { if (PresentationSource.FromVisual(button) == null) { status.Text = \"No display.\" + \" Save refused.\"; return; } }";
        FeatureLedger.Observation Row(int at, string kind, string detail) => new("FQ15-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("FamilyQuant/SpectrumComparisonWindow.cs|" + at.ToString(CultureInfo.InvariantCulture) + "|" + kind)))[..12],
            "FamilyQuant/SpectrumComparisonWindow.cs", 1 + source[..at].Split('\n').Length - 1, kind, detail);
        var literal = Row(source.IndexOf("\"No display.\"", StringComparison.Ordinal), "code fact", "\"No display.\"");
        var handler = Row(source.IndexOf("save.Click", StringComparison.Ordinal), "handler", "save.Click += (_, __) => SaveCopy(save, status, rows);");
        const string key = "actions/ButtonSaveSpectrumComparisonCsv/refusal";
        var before = new Dictionary<string, string> { [key] = "No display. Save refused." };
        var current = new Dictionary<string, string>(before);
        Check("spectrum refusal literal binds to its complete executed native status",
            AssaySpectrumActionComparison.Compare(literal, source, before, current).Passed);
        Check("spectrum handler receipt requires the actual named routed click and frozen refusal",
            AssaySpectrumActionComparison.Compare(handler, source, before, current).Passed);
        current[key] = "Different refusal";
        Check("a changed routed-action result cannot close its frozen handler",
            !AssaySpectrumActionComparison.Compare(handler, source, before, current).Passed);
        current = new() { ["actions/OtherButton/refusal"] = before[key] };
        Check("another button cannot lend its matching refusal to a missing action",
            !AssaySpectrumActionComparison.Compare(literal, source, before, current).Passed);
        current = new(before);
        Check("a wrong frozen literal ID cannot borrow the method's runtime status",
            !AssaySpectrumActionComparison.Compare(literal with { Id = "FQ15-WRONG" }, source, before, current).Passed);
    }
}
