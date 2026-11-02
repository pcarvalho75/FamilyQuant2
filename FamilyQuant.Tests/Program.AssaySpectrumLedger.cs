using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssaySpectrumLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-spectrum-properties",
        new[] { "FamilyQuant/SpectrumComparisonWindow.cs" },
        Directory.GetFiles(Path.Combine(FindLedgerRoot()!, "FamilyQuant"), "SpectrumComparisonWindow*.cs")
            .Select(path => Path.GetRelativePath(FindLedgerRoot()!, path).Replace('\\', '/'))
            .Concat(new[] { "FamilyQuant/SecondaryWindowView.cs", "FamilyQuant/SecondaryWindows.ViewModels.cs" }).ToArray(),
        (output, rows, negative) => AssaySpectrumLedgerCases(output, rows, negative, Path.GetFullPath(args[1])),
        "Constructed SpectrumComparisonWindow: exact column binding, header, tooltip setter and format",
        new[] { "FamilyQuant.Tests/Program.AssaySpectrumLedger.cs", "FamilyQuant.Tests/AssaySpectrumPropertyComparison.cs", "FamilyQuant.Tests/AssayViewSnapshot.cs" });

    private static List<AssayRuntimeCase> AssaySpectrumLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative, string frozenRoot)
    {
        string root = FindLedgerRoot()!, frozen = Path.Combine(output, "frozen-view"), current = Path.Combine(output, "current-view");
        string host = Path.Combine(root, "FamilyQuant.Tests/ReferenceGoldenHost/bin/Release/net10.0-windows7.0");
        if (FeatureLedgerEvidence.Hash(Path.Combine(host, "FamilyQuant.dll")) != FeatureLedgerEvidence.Hash("D:/FamilyQuant-data/reference-1.5.0.6/app/FamilyQuant.dll"))
            throw new InvalidDataException("Frozen host product assembly differs from the read-only reference.");
        var start = new ProcessStartInfo(Path.Combine(host, "ReferenceGoldenHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { AssayViewSnapshot.Argument, "spectrum", frozen }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start)!)
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
            File.WriteAllText(Path.Combine(output, "frozen-view.log"), stdout + stderr);
            if (process.ExitCode != 0) throw new InvalidDataException("Frozen native view snapshot failed: " + stderr);
        }
        AssayViewSnapshot.Run(new[] { AssayViewSnapshot.Argument, "spectrum", current });
        foreach (string name in new[] { "FamilyQuant.dll", "ReferenceGoldenHost.dll", "ReferenceGoldenHost.exe" })
            File.Copy(Path.Combine(host, name), Path.Combine(output, "frozen-" + name));
        var before = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(frozen, "properties.json")))!;
        var actual = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(current, "properties.json")))!;
        if (negative) foreach (string key in actual.Keys.ToArray()) actual[key] += " changed";
        string source = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/SpectrumComparisonWindow.cs"));
        var comparisons = rows.Select(row => AssaySpectrumPropertyComparison.Compare(row, source, before, actual)).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "property-anchors.json"), comparisons);
        return comparisons.Where(value => value.Key.Length > 0).Select(value => new AssayRuntimeCase(value.Id,
            value.Key + ": frozen source argument equals frozen native property and current native property",
            value.Expected, value.Actual, value.Passed)).ToList();
    }
}
