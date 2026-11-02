using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssayBuildWorkflowLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-build-workflow",
        new[] { "FamilyQuant/USearch.xaml", "FamilyQuant/USearch.xaml.cs" },
        Directory.GetFiles(Path.Combine(FindLedgerRoot()!, "FamilyQuant"), "USearch*")
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(FindLedgerRoot()!, path).Replace('\\', '/'))
            .Concat(new[] { "FamilyQuant/ShellDialogs.cs", "FamilyQuant/AppOperationHost.cs", "FamilyQuant.Core/USearchTools.cs",
                "FamilyQuant.Core/RedundancyTools.cs", "FamilyQuant.Core/FamilyQuantFile.cs" }).ToArray(),
        (output, rows, negative) => AssayBuildWorkflowLedgerCases(output, rows, negative, Path.GetFullPath(args[1])),
        "Actual named USearch clicks, guarded workers, FCF/redundancy paths and frozen/current native output comparison",
        new[] { "FamilyQuant.Tests/Program.AssayBuildWorkflow.cs", "FamilyQuant.Tests/Program.AssayBuildWorkflowLedger.cs",
            "FamilyQuant.Tests/AssayBuildWorkflowComparison.cs", "FamilyQuant.Tests/AssayBuildOutputSnapshot.cs",
            "FamilyQuant.Tests/AssayViewSnapshot.cs", "FamilyQuant.Tests/AssaySpectrumPropertyComparison.cs" });

    private static List<AssayRuntimeCase> AssayBuildWorkflowLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative, string frozenRoot)
    {
        string root = FindLedgerRoot()!, host = Path.Combine(root, "FamilyQuant.Tests/ReferenceGoldenHost/bin/Release/net10.0-windows7.0");
        if (FeatureLedgerEvidence.Hash(Path.Combine(host, "FamilyQuant.dll")) != FeatureLedgerEvidence.Hash("D:/FamilyQuant-data/reference-1.5.0.6/app/FamilyQuant.dll"))
            throw new InvalidDataException("Frozen workflow host differs from the read-only reference.");
        string beforePath = Path.Combine(output, "frozen-native"), afterPath = Path.Combine(output, "current-native");
        var start = new ProcessStartInfo(Path.Combine(host, "ReferenceGoldenHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(AssayBuildOutputSnapshot.Argument); start.ArgumentList.Add(beforePath);
        using (var process = Process.Start(start)!)
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
            File.WriteAllText(Path.Combine(output, "frozen-native.log"), stdout + stderr);
            if (process.ExitCode != 0) throw new InvalidDataException("Frozen native workflow output failed: " + stderr);
        }
        foreach (string name in new[] { "FamilyQuant.dll", "ReferenceGoldenHost.dll", "ReferenceGoldenHost.exe" })
            File.Copy(Path.Combine(host, name), Path.Combine(output, "frozen-" + name));
        AssayBuildOutputSnapshot.Run(new[] { AssayBuildOutputSnapshot.Argument, afterPath });
        var before = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(beforePath, "native-values.json")))!;
        var after = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(afterPath, "native-values.json")))!;
        bool outputsMatch = before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? value) && pair.Value == value);
        Check("USearch frozen/current native clustering and redundancy outputs agree", outputsMatch);
        var actual = AssayBuildWorkflow(Path.Combine(output, "workflow"), before);
        actual["native/frozenCurrent"] = outputsMatch.ToString();
        var sources = rows.Select(row => row.Source).Distinct(StringComparer.Ordinal)
            .ToDictionary(path => path, path => File.ReadAllText(Path.Combine(frozenRoot, path)), StringComparer.Ordinal);
        var comparisons = rows.Select(row => AssayBuildWorkflowComparison.Compare(row, sources[row.Source], actual)).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "workflow-anchors.json"), comparisons);
        var eligible = comparisons.Where(value => value.Passed).Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (negative) foreach (string key in actual.Keys.ToArray()) actual[key] = "changed " + actual[key];
        return rows.Where(row => eligible.Contains(row.Id)).Select(row =>
        {
            var value = AssayBuildWorkflowComparison.Compare(row, sources[row.Source], actual);
            return new AssayRuntimeCase(value.Id, value.Key + ": " + value.Reason, value.Expected, value.Actual, value.Passed);
        }).ToList();
    }

    private static void AssayBuildWorkflowGuardTests()
    {
        const string source = "<UserControl xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Button x:Name='ButtonScanIdentities' Click='ButtonScanIdentities_Click'/></UserControl>";
        var row = new FeatureLedger.Observation("fixture", "FamilyQuant/USearch.xaml", 1, "handler", "ButtonScanIdentities.Click = ButtonScanIdentities_Click");
        var actual = new Dictionary<string, string> { ["workflow/ButtonScanIdentities/missing"] = "True", ["workflow/ButtonScanIdentities/failure"] = "True" };
        Check("Build handler proof requires its actual named native click branches", AssayBuildWorkflowComparison.Compare(row, source, actual).Passed);
        actual["workflow/ButtonScanIdentities/failure"] = "False";
        Check("Build failed native worker receipt stays OPEN", !AssayBuildWorkflowComparison.Compare(row, source, actual).Passed);
        actual = new() { ["workflow/ButtonDivideAndConquer/missing"] = "True", ["workflow/ButtonDivideAndConquer/failure"] = "True" };
        Check("another Build action cannot lend its successful native receipt", !AssayBuildWorkflowComparison.Compare(row, source, actual).Passed);
        actual["workflow/ButtonScanIdentities/missing"] = actual["workflow/ButtonScanIdentities/failure"] = "True";
        Check("a different frozen click binding cannot borrow the named native action",
            !AssayBuildWorkflowComparison.Compare(row, source.Replace("Click='ButtonScanIdentities_Click'", "Click='Other_Click'", StringComparison.Ordinal), actual).Passed);
        Check("the split select-and-build flow never claims the old one-click build handler",
            !AssayBuildWorkflowComparison.Compare(row with { Detail = "ButtonInput.Click = ButtonInput_Click" }, source, actual).Passed);
    }
}
