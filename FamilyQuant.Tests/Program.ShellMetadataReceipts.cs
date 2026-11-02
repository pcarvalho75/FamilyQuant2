using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ShellMetadataReceipts(string[] args)
    {
        if (args.Length is not (5 or 6) || !DateTimeOffset.TryParse(args[4], CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out _)) throw new ArgumentException("Supply frozen source, frozen host, fresh output and date timestamp.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[3]), host = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Receipt output must be fresh.");
        Directory.CreateDirectory(output);
        string inventoryPath = Path.Combine(root, "docs/feature-ledger-scan.json");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(inventoryPath))!;
        if (inventory.Reference != FeatureLedger.ReferenceCommit || FeatureLedger.Fingerprint(inventory) != FeatureLedger.ReferenceInventorySha256)
            throw new InvalidDataException("Frozen inventory identity differs.");
        HashSet<string>? c1Scope = null;
        if (args.Length == 6)
        {
            var scope = JsonSerializer.Deserialize<FeatureLedgerEvidence.Register>(File.ReadAllText(args[5]), FeatureLedgerEvidence.JsonOptions)!;
            if (scope.Step != "C1-runtime" || scope.FrozenCommit != FeatureLedger.ReferenceCommit
                || scope.Rows.Any(row => !inventory.Observations.Any(observation => observation.Id == row.ObservationId
                    && observation.Source.StartsWith("FamilyQuant/ClusterGenerationControl.xaml", StringComparison.Ordinal))))
                throw new InvalidDataException("The bounded C1 cohort differs from its original source assignment.");
            c1Scope = scope.Rows.Select(row => row.ObservationId).ToHashSet(StringComparer.Ordinal);
        }
        string frozenPacket = Path.Combine(output, "frozen.json"), currentPacket = Path.Combine(output, "current.json");
        var start = new ProcessStartInfo(host) { UseShellExecute = false, WorkingDirectory = root,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in new[] { ShellMetadataSnapshot.Argument, args[1], inventoryPath, frozenPacket }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start)!)
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            File.WriteAllText(Path.Combine(output, "frozen.log"), stdout + stderr, new UTF8Encoding(false));
            if (process.ExitCode != 0) throw new InvalidDataException("Frozen native snapshot failed: " + stdout + stderr);
        }
        ShellMetadataSnapshot.Run([ShellMetadataSnapshot.Argument, args[1], inventoryPath, currentPacket]);
        var frozen = JsonSerializer.Deserialize<ShellMetadataSnapshot.Packet>(File.ReadAllText(frozenPacket), ShellMetadataSnapshot.Options)!;
        var current = JsonSerializer.Deserialize<ShellMetadataSnapshot.Packet>(File.ReadAllText(currentPacket), ShellMetadataSnapshot.Options)!;
        string frozenAssembly = Path.Combine(Path.GetDirectoryName(host)!, "FamilyQuant.dll");
        if (frozen.AssemblySha256 != FeatureLedgerEvidence.Hash(frozenAssembly)
            || frozen.AssemblySha256 != FeatureLedgerEvidence.Hash("D:/FamilyQuant-data/reference-1.5.0.6/app/FamilyQuant.dll")
            || current.AssemblySha256 != FeatureLedgerEvidence.Hash(typeof(MainWindow).Assembly.Location))
            throw new InvalidDataException("Native packet does not identify its executing application assembly.");
        var cohort = inventory.Observations.Where(row => frozen.Facts.Any(fact => fact.Id == row.Id))
            .Where(row => c1Scope == null || !row.Source.Contains("ClusterGenerationControl", StringComparison.Ordinal) || c1Scope.Contains(row.Id))
            .Where(row => !row.Detail.Contains("Help", StringComparison.Ordinal) && !row.Detail.Contains("HowToRead", StringComparison.Ordinal)
                && !row.Detail.Contains("WhereAmI", StringComparison.Ordinal)).OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        var compared = cohort.Select(row => ShellMetadataComparison.Compare(row, frozen.Facts, current.Facts)).ToArray();
        var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        string log = Path.Combine(output, "comparisons.log");
        var original = Console.Out; using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            foreach (var result in compared)
            {
                if (result.Matched) Check("native shell metadata " + result.Id + ": " + result.Before, true);
                else { skipped++; Console.WriteLine("  SKIP  " + result.Id + ": " + result.Reason); }
            }
            foreach (var kind in cohort.Where(row => compared.Single(result => result.Id == row.Id).Matched).GroupBy(row => row.Kind))
            {
                string assertion = "native shell kind " + kind.Key + ": every listed frozen/current observation has equal native metadata or executed event outcome";
                Check(assertion, kind.All(row => compared.Single(result => result.Id == row.Id).Matched));
                assertions[kind.Key] = assertion;
            }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        string comparisonPath = Path.Combine(output, "comparisons.json"); FeatureLedgerEvidence.WriteJson(comparisonPath, compared);
        var artifacts = new List<FeatureLedgerEvidence.Artifact>();
        void Artifact(string path) => artifacts.Add(new(path, FeatureLedgerEvidence.Hash(path)));
        foreach (string path in new[] { log, frozenPacket, currentPacket, comparisonPath, Path.Combine(output, "frozen.log") }) Artifact(path);
        if (args.Length == 6) Artifact(Path.GetFullPath(args[5]));
        string executed = Path.Combine(output, "executed"); Directory.CreateDirectory(executed);
        foreach (string relative in new[] { "FamilyQuant.Tests/ShellMetadataSnapshot.cs", "FamilyQuant.Tests/ShellMetadataSnapshot.Home.cs", "FamilyQuant.Tests/ShellMetadataComparison.cs",
            "FamilyQuant.Tests/Program.ShellMetadata.cs", "FamilyQuant.Tests/Program.ShellMetadataReceipts.cs", "FamilyQuant.Tests/Program.Commands.ShellMetadata.cs",
            "FamilyQuant.Tests/ReferenceGoldenHost/Program.cs", "FamilyQuant.Tests/ReferenceGoldenHost/ReferenceGoldenHost.csproj" })
        {
            string target = Path.Combine(executed, relative.Replace('/', '_')); File.Copy(Path.Combine(root, relative), target); Artifact(target);
        }
        foreach (string path in new[] { typeof(Program).Assembly.Location, typeof(MainWindow).Assembly.Location,
            typeof(FamilyClusterFile).Assembly.Location, host, Path.ChangeExtension(host, ".dll"), frozenAssembly })
        {
            string target = Path.Combine(executed, Path.GetFileName(path));
            File.Copy(path, target); Artifact(target);
        }
        var git = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        git.ArgumentList.Add("rev-parse"); git.ArgumentList.Add("HEAD");
        using var head = Process.Start(git)!; string commit = head.StandardOutput.ReadToEnd().Trim(); head.WaitForExit();
        if (head.ExitCode != 0) throw new IOException("Could not record executing head.");
        var run = new FeatureLedgerEvidence.Run("native-shell-metadata", Environment.CommandLine, args[4], commit, artifacts, root);
        foreach (string step in new[] { "SH-runtime", "C1-runtime" })
        {
            bool curation = step.StartsWith("C1", StringComparison.Ordinal);
            var selected = cohort.Where(row => row.Source.Contains("ClusterGenerationControl", StringComparison.Ordinal) == curation)
                .Where(row => !curation || compared.Single(result => result.Id == row.Id).Matched).ToArray();
            var rows = selected.Select(row =>
            {
                var result = compared.Single(result => result.Id == row.Id);
                var mappings = result.Matched ? MetadataSources(root, row.Source).Select(path => new FeatureLedgerEvidence.Implementation(path,
                    "compiled native metadata for " + row.Detail, FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList() : new();
                return new FeatureLedgerEvidence.Row(row.Id, mappings, new(), result.Reason,
                    result.Matched ? "native-" + row.Kind.Replace('/', '-').Replace(' ', '-') : null,
                    result.Matched ? row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + " " + row.Detail
                        + "; frozen compiled " + result.Before + " -> current compiled " + result.Actual : null);
            }).ToList();
            var receipts = selected.Select(row => row.Kind).Distinct(StringComparer.Ordinal).Where(assertions.ContainsKey)
                .Select(kind => new FeatureLedgerEvidence.KindReceipt("native-" + kind.Replace('/', '-').Replace(' ', '-'), kind,
                    new() { new(run.Id, "test", nameof(ShellMetadataReceipts), assertions[kind], log, "") })).ToList();
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"), new FeatureLedgerEvidence.Register(1, step,
                FeatureLedger.ReferenceCommit, new() { run }, rows, receipts));
            Console.WriteLine(step + ": " + selected.Count(row => compared.Single(result => result.Id == row.Id).Matched) + " matched native observations.");
        }
        return failed == 0 ? 0 : 1;
    }

    private static string[] MetadataSources(string root, string source)
    {
        string name = Path.GetFileNameWithoutExtension(source);
        var names = source switch
        {
            "FamilyQuant/ClusterGenerationControl.xaml" => new[] { "ClusterGenerationControl", "CurationViewModel", "CurationQuantitationPanel", "CurationQuantitationViewModel" },
            "FamilyQuant/HomeControl.xaml" or "FamilyQuant/HomeControl.xaml.cs" => new[] { "HomeControl", "HomeViewModel" },
            "FamilyQuant/MainWindow.xaml" => new[] { "MainWindow", "ShellViewModel", "ShellStatusViewModel", "ShellDocumentState", "ShellSessionState" },
            _ => new[] { name }
        };
        return Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => names.Any(prefix => Path.GetFileName(path).StartsWith(prefix + ".", StringComparison.Ordinal)))
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant/Themes"), "*.xaml"))
            .Concat(new[] { Path.Combine(root, "FamilyQuant/App.xaml"), Path.Combine(root, "FamilyQuant.Tests/ShellMetadataSnapshot.cs"),
                Path.Combine(root, "FamilyQuant.Tests/ShellMetadataSnapshot.Home.cs"),
                Path.Combine(root, "FamilyQuant.Tests/ShellMetadataComparison.cs") })
            .Concat(new[] { "FamilyQuantFile.cs", "FamilyNaming.cs", "NormalizationMode.cs", "SharedPeptidePolicy.cs", "QuantitationProfile.cs" }
                .Select(file => Path.Combine(root, "FamilyQuant.Core", file)).Where(File.Exists))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }
}
