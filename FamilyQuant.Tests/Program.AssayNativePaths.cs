using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssayNativePathsLedgerCommand(string[] args) => RunAssayRuntimeLedger(args, "AS-native-paths",
        new[] { "FamilyQuant/USearchTools.cs" }, new[] { "FamilyQuant.Core/USearchTools.cs", "FamilyQuant.Core/ApplicationPaths.cs" },
        AssayNativePathsLedgerCases, "Actual bundled alignment/cluster/allpairs file suffixes and native no-output refusal",
        new[] { "FamilyQuant.Tests/Program.AssayNativePaths.cs" });

    private static List<AssayRuntimeCase> AssayNativePathsLedgerCases(string output, FeatureLedger.Observation[] rows, bool negative)
    {
        var observed = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        string scratch = ApplicationPaths.ScratchDirectory; Directory.CreateDirectory(scratch);
        using var scratchWatcher = new FileSystemWatcher(scratch) { NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true };
        using var outputWatcher = new FileSystemWatcher(output) { NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true };
        scratchWatcher.Created += (_, value) =>
        {
            string name = Path.GetFileName(value.FullPath), suffix = Path.GetExtension(value.FullPath);
            if (name.StartsWith("align_", StringComparison.Ordinal) && suffix is ".fasta" or ".aln") observed["align/" + suffix] = true;
            if (name.StartsWith("native-fixture_allpairs_", StringComparison.Ordinal) && suffix == ".tsv") observed["allpairs/" + suffix] = true;
        };
        outputWatcher.Created += (_, value) =>
        {
            if (Path.GetFileName(value.FullPath).StartsWith("native-fixture_85_", StringComparison.Ordinal) && Path.GetExtension(value.FullPath) == ".uc")
                observed["cluster/.uc"] = true;
        };
        const string sequence = "ACDEFGHIKLMNPQRSTVWYACDEFGHIKLMNPQRSTVWYACDEFGHIKLMNPQRSTVWY";
        var items = new List<FastaItemExtended>
        {
            new() { SequenceIdentifier = "fixture_a", Sequence = sequence },
            new() { SequenceIdentifier = "fixture_b", Sequence = sequence[..^1] + "F" }
        };
        string fasta = Path.Combine(output, "native-fixture.fasta");
        File.WriteAllText(fasta, string.Join("\n", items.Select(item => ">" + item.SequenceIdentifier + "\n" + item.Sequence)) + "\n");
        string? oldTool = Environment.GetEnvironmentVariable("FAMILYQUANT_USEARCH"), oldFixture = Environment.GetEnvironmentVariable("FQ_ASSAY_NOOUTPUT_NATIVE");
        string missing = "";
        try
        {
            Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", null);
            string alignment = USearchTools.Align(items);
            var clustered = USearchTools.Cluster(new FileInfo(fasta), 85, exhaustive: true);
            Check("actual bundled native path fixture returns alignment and both input records", alignment.Length > 0 && clustered.Document.MyExtendedFastaItems.Count == 2);
            var wait = Stopwatch.StartNew();
            while (observed.Count < 4 && wait.Elapsed < TimeSpan.FromSeconds(2)) Thread.Sleep(10);
            string executable = Path.Combine(FindLedgerRoot()!, "FamilyQuant.Tests/ReferenceGoldenHost/bin/Release/net10.0-windows7.0/ReferenceGoldenHost.exe");
            Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", executable);
            Environment.SetEnvironmentVariable("FQ_ASSAY_NOOUTPUT_NATIVE", "1");
            try { USearchTools.Cluster(new FileInfo(fasta), 85, exhaustive: false); }
            catch (InvalidOperationException error) { missing = error.Message; }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", oldTool);
            Environment.SetEnvironmentVariable("FQ_ASSAY_NOOUTPUT_NATIVE", oldFixture);
        }
        File.WriteAllLines(Path.Combine(output, "observed-suffixes.txt"), observed.Keys.OrderBy(value => value, StringComparer.Ordinal));
        File.WriteAllText(Path.Combine(output, "missing-cluster.txt"), missing);
        var cases = new List<AssayRuntimeCase>();
        foreach (var row in rows)
        {
            if (row.Detail is "\".fasta\"" or "\".aln\"" or "\".uc\"" or "\".tsv\"")
            {
                string expected = System.Text.Json.JsonSerializer.Deserialize<string>(row.Detail)!;
                string key = (expected is ".fasta" or ".aln" ? "align/" : expected == ".uc" ? "cluster/" : "allpairs/") + expected;
                cases.Add(new(row.Id, "actual native file creation observed at the recorded suffix", expected,
                    observed.ContainsKey(key) ? expected : "<missing>", !negative && observed.ContainsKey(key)));
            }
            else if (row.Detail == "\"USEARCH produced no cluster (.uc) file for \"")
            {
                string prefix = System.Text.Json.JsonSerializer.Deserialize<string>(row.Detail)!;
                string expected = prefix + Path.GetFileName(fasta) + ".";
                cases.Add(new(row.Id, "owned native no-output test process exercises the real missing-cluster refusal", expected, missing, !negative && missing == expected));
            }
        }
        return cases;
    }
}
