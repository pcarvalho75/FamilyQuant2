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
    private sealed record CoreRuntimeCase(string Id, string Operation, string Expected, string Actual, bool Passed);

    private static int StorageRuntimeLedgerCommand(string[] args) => RunCoreRuntimeLedger(args, "Core-X7-runtime",
        new[] { "FamilyQuant/ProjectStore.cs" },
        new[] { "FamilyQuant.Core/ProjectStore.cs" }.Concat(Directory.GetFiles(
            Path.Combine(FindLedgerRoot()!, "FamilyQuant.Core/Storage"), "*.cs")
            .Select(path => Path.GetRelativePath(FindLedgerRoot()!, path).Replace('\\', '/'))).ToArray(),
        StorageRuntimeLedgerCases);

    private static int RunCoreRuntimeLedger(string[] args, string step, string[] sources, string[] implementations,
        Func<string, FeatureLedger.Observation[], bool, List<CoreRuntimeCase>> execute)
    {
        if (args.Length is not (4 or 5) || args.Length == 5 && args[4] != "--negative-control")
            throw new ArgumentException("Supply frozen source root, fresh output, date timestamp and optional --negative-control.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Native proof output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(
            Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        foreach (string source in sources)
            if (FeatureLedgerEvidence.Hash(Path.Combine(frozenRoot, source))
                != inventory.Files.Single(file => file.Path == source).Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + source);
        var registry = FeatureLedgerEvidence.Load(root, inventory);
        var alreadyProved = registry.Entries.Values.Where(entry => entry.Valid && entry.Step != step)
            .Select(entry => entry.Row.ObservationId).ToHashSet(StringComparer.Ordinal);
        var all = inventory.Observations.Where(row => sources.Contains(row.Source, StringComparer.Ordinal)
            && !FeatureLedger.IsApprovedDrop(row)).ToArray();
        var cohort = all.Where(row => !alreadyProved.Contains(row.Id)).OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        TextWriter original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        List<CoreRuntimeCase> cases;
        var kindAssertions = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            var executed = execute(output, all, args.Length == 5);
            if (executed.GroupBy(fact => fact.Id).Any(group => group.Count() != 1))
                throw new InvalidDataException("A native observation was recorded more than once.");
            cases = executed.Where(fact => cohort.Any(row => row.Id == fact.Id)).OrderBy(fact => fact.Id, StringComparer.Ordinal).ToList();
            foreach (var fact in cases) Check(step + " " + fact.Id + ": " + fact.Operation, fact.Passed, fact.Actual);
            foreach (var row in cohort.Where(row => cases.All(fact => fact.Id != row.Id)))
            {
                skipped++;
                Console.WriteLine("  SKIP  " + row.Id + ": no observation-specific native value or branch check.");
            }
            foreach (var group in cases.GroupBy(fact => all.Single(row => row.Id == fact.Id).Kind)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string assertion = step + " kind " + group.Key + ": every listed native value and branch check passed";
                Check(assertion, group.All(fact => fact.Passed));
                kindAssertions[group.Key] = assertion;
            }
            Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "proofs.log"), values = Path.Combine(output, "actual-values.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(values, cases);
        var artifacts = new List<FeatureLedgerEvidence.Artifact>
            { new(log, FeatureLedgerEvidence.Hash(log)), new(values, FeatureLedgerEvidence.Hash(values)) };
        void Retain(string source, string relative)
        {
            string destination = Path.Combine(output, "executed", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, false);
            artifacts.Add(new(destination, FeatureLedgerEvidence.Hash(destination)));
        }
        foreach (string path in implementations.Concat(new[]
        {
            "FamilyQuant.Tests/Program.CoreRuntimeLedger.cs", "FamilyQuant.Tests/Program.StorageRuntimeLedger.cs",
            "FamilyQuant.Tests/Program.Commands.CoreRuntimeLedger.cs", "FamilyQuant.Tests/FeatureLedgerEvidence.cs",
            "FamilyQuant.Tests/FeatureLedgerEvidence.Reporting.cs",
        }).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
            Retain(Path.Combine(root, path), path);
        foreach (string source in sources) Retain(Path.Combine(frozenRoot, source), Path.Combine("frozen", source));
        Retain(typeof(Program).Assembly.Location, "FamilyQuant.Tests.dll");
        Retain(typeof(ProjectStore).Assembly.Location, "FamilyQuant.Core.dll");
        foreach (string path in Directory.GetFiles(output, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path != log && path != values).OrderBy(path => path, StringComparer.Ordinal))
            artifacts.Add(new(path, FeatureLedgerEvidence.Hash(path)));
        var mappings = implementations.OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new FeatureLedgerEvidence.Implementation(path, "ProjectStore public reader/writer and split codecs",
                FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList();
        var rows = cohort.Select(row =>
        {
            var fact = cases.SingleOrDefault(fact => fact.Id == row.Id);
            return new FeatureLedgerEvidence.Row(row.Id, mappings, new(),
                fact == null ? "No observation-specific native check; remains OPEN."
                    : fact.Passed ? "Executed native value/refusal check, with the specific comparison recorded below."
                    : "Executed native check failed; remains OPEN.",
                fact?.Passed == true ? "native-" + row.Kind.Replace('/', '-').Replace(' ', '-') : null,
                fact?.Passed == true ? row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + " " + row.Detail
                    + "; " + fact.Operation + "; expected " + fact.Expected + "; actual " + fact.Actual : null);
        }).ToList();
        var receipts = kindAssertions.Select(pair => new FeatureLedgerEvidence.KindReceipt(
            "native-" + pair.Key.Replace('/', '-').Replace(' ', '-'), pair.Key,
            new() { new("native", "test", nameof(RunCoreRuntimeLedger), pair.Value, log, "") })).ToList();
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record source commit.");
        var run = new FeatureLedgerEvidence.Run("native", Environment.CommandLine, args[3], commit, artifacts, root);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"),
            new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { run }, rows, receipts));
        return failed == 0 ? 0 : 1;
    }
}
