using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssayCacheLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-cache-wire",
        new[] { "FamilyQuant/DeNovo/DeNovoXicCache.cs", "FamilyQuant/DeNovo/Ms1EnvelopeDetector.cs" },
        new[] { "FamilyQuant.Core/DeNovo/DeNovoXicCache.cs", "FamilyQuant.Core/DeNovo/Ms1EnvelopeDetector.cs",
            "FamilyQuant.Core/DeNovo/ExtractionCacheJson.cs" }, AssayCacheLedgerCases);

    private static List<AssayRuntimeCase> AssayCacheLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative)
    {
        string root = FindLedgerRoot()!;
        string hostFolder = Path.Combine(root, "FamilyQuant.Tests/ReferenceGoldenHost/bin/Release/net10.0-windows7.0");
        string current = Path.Combine(output, "current"), frozen = Path.Combine(output, "frozen");
        var start = new ProcessStartInfo(Path.Combine(hostFolder, "ReferenceGoldenHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(AssayCacheSnapshot.Argument); start.ArgumentList.Add(frozen);
        using (var process = Process.Start(start)!)
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            File.WriteAllText(Path.Combine(output, "frozen-host.log"), stdout + stderr);
            if (process.ExitCode != 0) throw new InvalidDataException("Frozen native cache snapshot failed: " + stderr);
        }
        AssayCacheSnapshot.Run(new[] { AssayCacheSnapshot.Argument, current });
        // Retain the actual frozen implementation and the host which invoked it.
        foreach (string name in new[] { "FamilyQuant.dll", "ReferenceGoldenHost.dll", "ReferenceGoldenHost.exe" })
            File.Copy(Path.Combine(hostFolder, name), Path.Combine(output, "frozen-" + name));
        if (FeatureLedgerEvidence.Hash(Path.Combine(hostFolder, "FamilyQuant.dll"))
            != FeatureLedgerEvidence.Hash("D:/FamilyQuant-data/reference-1.5.0.6/app/FamilyQuant.dll"))
            throw new InvalidDataException("Frozen host product assembly differs from the read-only reference.");
        var facts = new List<AssayRuntimeCase>();
        foreach (var row in rows)
        {
            string file = row.Source.EndsWith("/DeNovoXicCache.cs", StringComparison.Ordinal) ? "xic" : "ms1";
            if (row.Detail == "\".tmp\"")
            {
                using var before = JsonDocument.Parse(File.ReadAllText(Path.Combine(frozen, "staging.json")));
                using var after = JsonDocument.Parse(File.ReadAllText(Path.Combine(current, "staging.json")));
                string expected = before.RootElement.GetProperty(file + "Suffix").GetString()!;
                string actual = after.RootElement.GetProperty(file + "Suffix").GetString()!;
                facts.Add(new(row.Id, "blocked final rename retains complete native cache payload at the observed temporary suffix",
                    expected, actual, !negative && expected == ".tmp" && actual == expected));
                continue;
            }
            if (row.Kind != "export/storage fact") continue;
            string? key;
            try { key = JsonSerializer.Deserialize<string>(row.Detail); }
            catch (JsonException) { continue; }
            if (key == null) continue;
            string beforePath = Path.Combine(frozen, file + ".canonical.json"), afterPath = Path.Combine(current, file + ".canonical.json");
            using var expectedJson = JsonDocument.Parse(File.ReadAllText(beforePath));
            using var actualJson = JsonDocument.Parse(File.ReadAllText(afterPath));
            if (!expectedJson.RootElement.TryGetProperty(key, out var expectedValue)) continue;
            bool exists = actualJson.RootElement.TryGetProperty(key, out var actualValue);
            string expectedText = expectedValue.GetRawText(), actualText = exists ? actualValue.GetRawText() : "<missing>";
            facts.Add(new(row.Id, "native write/reopen compares top-level cache key " + key + " and its complete fixture value",
                expectedText, actualText, !negative && exists && actualText == expectedText
                    && File.ReadAllBytes(beforePath).SequenceEqual(File.ReadAllBytes(afterPath))));
        }
        return facts;
    }
}
