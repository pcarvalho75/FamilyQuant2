using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssayPartitionLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-partition-format",
        new[] { "FamilyQuant/DeNovo/ThreeWayPartition.cs" },
        new[] { "FamilyQuant.Core/DeNovo/ThreeWayPartition.cs", "FamilyQuant.Core/CsvFormatting.cs", "FamilyQuant.Core/DeNovo/SpreadsheetSafeCsv.cs" },
        (output, rows, negative) => AssayPartitionLedgerCases(output, rows, negative, Path.GetFullPath(args[1])),
        "Native ThreeWayPartition CSV quoting and round-trip formatting in en-US and pt-BR",
        new[] { "FamilyQuant.Tests/Program.AssayPartitionLedger.cs", "FamilyQuant.Tests/AssayPartitionSnapshot.cs", "FamilyQuant.Tests/LedgerMemberFactComparison.cs" });

    private static List<AssayRuntimeCase> AssayPartitionLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative, string frozenRoot)
    {
        string root = FindLedgerRoot()!, host = Path.Combine(root, "FamilyQuant.Tests/ReferenceGoldenHost/bin/Release/net10.0-windows7.0");
        string frozen = Path.Combine(output, "frozen-csv"), current = Path.Combine(output, "current-csv");
        if (FeatureLedgerEvidence.Hash(Path.Combine(host, "FamilyQuant.dll")) != FeatureLedgerEvidence.Hash("D:/FamilyQuant-data/reference-1.5.0.6/app/FamilyQuant.dll"))
            throw new InvalidDataException("Frozen CSV host differs from reference.");
        var start = new ProcessStartInfo(Path.Combine(host, "ReferenceGoldenHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(AssayPartitionSnapshot.Argument); start.ArgumentList.Add(frozen);
        using (var process = Process.Start(start)!)
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
            File.WriteAllText(Path.Combine(output, "frozen-csv.log"), stdout + stderr);
            if (process.ExitCode != 0) throw new InvalidDataException("Frozen native CSV failed: " + stderr);
        }
        AssayPartitionSnapshot.Run(new[] { AssayPartitionSnapshot.Argument, current });
        foreach (string name in new[] { "FamilyQuant.dll", "ReferenceGoldenHost.dll", "ReferenceGoldenHost.exe" })
            File.Copy(Path.Combine(host, name), Path.Combine(output, "frozen-" + name));
        bool same = new[] { "en-US.csv", "pt-BR.csv" }.All(name => File.ReadAllBytes(Path.Combine(frozen, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(current, name))));
        string source = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/DeNovo/ThreeWayPartition.cs"));
        var cases = new List<AssayRuntimeCase>();
        foreach (var row in rows)
        {
            var owner = LedgerMemberFactComparison.Compare(row, source, new Dictionary<string, string>());
            if (!owner.FrozenMember.Contains("::private static string Number (", StringComparison.Ordinal)
                && !owner.FrozenMember.Contains("::private static string Field (", StringComparison.Ordinal)) continue;
            cases.Add(new(row.Id, "complete native CSV bytes for quoted/comma/newline fields, full-precision and subnormal numeric values in two cultures",
                "identical frozen/current native CSV bytes", same ? "identical" : "different", same && !negative));
        }
        return cases;
    }
}
