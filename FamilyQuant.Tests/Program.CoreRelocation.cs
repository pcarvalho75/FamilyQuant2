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
    private static int CoreRelocationCommand(string[] args)
    {
        if (args.Length is not (5 or 6) || args[3] is not ("X5" or "X7"))
            throw new ArgumentException("Supply frozen source root, fresh output, X5/X7 cohort, date timestamp and optional explicit member mapping JSON.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Relocation output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        string step = "Core-" + args[3];
        var existing = FeatureLedgerEvidence.Load(root, inventory);
        var alreadyProved = existing.Entries.Values.Where(entry => entry.Valid && entry.Step != step)
            .Select(entry => entry.Row.ObservationId).ToHashSet(StringComparer.Ordinal);
        string[] sources = args[3] == "X5"
            ? new[] { "FamilyQuant/FullXicComputer.cs", "FamilyQuant/FamilyEnvelopeQuant.cs" }
            : new[] { "FamilyQuant/ProjectStore.cs" };
        var cohort = inventory.Observations.Where(row => sources.Contains(row.Source, StringComparer.Ordinal)
            && !FeatureLedger.IsApprovedDrop(row)).OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        var frozen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string source in sources)
        {
            string path = Path.Combine(frozenRoot, source);
            string expected = inventory.Files.Single(file => file.Path == source).Sha256;
            if (!File.Exists(path) || FeatureLedgerEvidence.Hash(path) != expected)
                throw new InvalidDataException("Frozen source hash differs: " + source);
            frozen[source] = File.ReadAllText(path);
        }
        // Deliberate destination sets: another subsystem cannot lend a matching member or literal.
        string[] files = args[3] == "X5"
            ? new[] { "FamilyQuant.Core/FullXicComputer.cs", "FamilyQuant.Core/FamilyEnvelopeQuant.cs" }
                .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core/Quantitation"), "*.cs", SearchOption.TopDirectoryOnly)
                    .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))).ToArray()
            : new[] { "FamilyQuant.Core/ProjectStore.cs" }
                .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core/Storage"), "*.cs", SearchOption.TopDirectoryOnly)
                    .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))).ToArray();
        var current = files.OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => path, path => File.ReadAllText(Path.Combine(root, path)), StringComparer.Ordinal);
        var mappings = args.Length == 6
            ? JsonSerializer.Deserialize<List<LedgerMemberFactComparison.MemberMapping>>(File.ReadAllText(args[5]), FeatureLedgerEvidence.JsonOptions)!
            : new();
        if (mappings.GroupBy(m => m.FrozenMember, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("Explicit member mappings must be unique.");
        var results = new List<LedgerMemberFactComparison.Result>();
        var kinds = cohort.ToDictionary(row => row.Id, row => row.Kind, StringComparer.Ordinal);
        var kindAssertions = new Dictionary<string, string>(StringComparer.Ordinal);
        TextWriter original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            foreach (var observation in cohort)
            {
                var initial = LedgerMemberFactComparison.Compare(observation, frozen[observation.Source], current);
                var mapping = mappings.SingleOrDefault(m => m.FrozenMember == initial.FrozenMember);
                var result = mapping == null ? initial
                    : LedgerMemberFactComparison.Compare(observation, frozen[observation.Source], current, mapping);
                results.Add(result);
                if (result.Matched) Check("Core member " + result.Id + ": " + result.Symbol, true);
                else { skipped++; Console.WriteLine("  SKIP  " + result.Id + ": " + result.Reason); }
            }
            foreach (var group in results.Where(r => r.Matched).GroupBy(r => kinds[r.Id]).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                string assertion = "Core member kind " + group.Key + ": all listed complete declarations retain exact literal syntax and multiplicity";
                Check(assertion, group.All(r => r.Matched));
                kindAssertions[group.Key] = assertion;
            }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "comparisons.log"), data = Path.Combine(output, "comparisons.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(data, results);
        var rows = results.Select(result => new FeatureLedgerEvidence.Row(result.Id,
            result.Matched ? new() { new(result.CurrentPath, result.Symbol, FeatureLedgerEvidence.Hash(Path.Combine(root, result.CurrentPath))) } : new(),
            new(), alreadyProved.Contains(result.Id) ? "An existing independent receipt remains authoritative; declaration comparison is retained in the report only."
                : result.Matched ? "Complete member declaration only; no runtime/reachability claim." : result.Reason,
            result.Matched && !alreadyProved.Contains(result.Id) ? "member-" + kinds[result.Id].Replace('/', '-').Replace(' ', '-') : null,
            result.Matched && !alreadyProved.Contains(result.Id) ? result.Before + " -> " + result.CurrentPath + ": " + result.Symbol
                + "; frozen/current member SHA256 " + result.FrozenMemberSha256 + "/" + result.CurrentMemberSha256 : null)).ToList();
        var kindReceipts = kindAssertions.Select(pair => new FeatureLedgerEvidence.KindReceipt(
            "member-" + pair.Key.Replace('/', '-').Replace(' ', '-'), pair.Key,
            new() { new("core-members", "test", nameof(CoreRelocationCommand), pair.Value, log, "") })).ToList();
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record executing commit.");
        var artifacts = new List<FeatureLedgerEvidence.Artifact> { new(log, FeatureLedgerEvidence.Hash(log)), new(data, FeatureLedgerEvidence.Hash(data)) };
        if (args.Length == 6) artifacts.Add(new(Path.GetFullPath(args[5]), FeatureLedgerEvidence.Hash(args[5])));
        // A pre-commit run retains the source and assemblies that produced its assertions.
        void Retain(string source, string relative)
        {
            string destination = Path.Combine(output, "executed", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, false);
            artifacts.Add(new(destination, FeatureLedgerEvidence.Hash(destination)));
        }
        foreach (string source in current.Keys.Concat(new[]
        {
            "FamilyQuant.Tests/Program.CoreRelocation.cs", "FamilyQuant.Tests/Program.Commands.CoreRelocation.cs",
            "FamilyQuant.Tests/LedgerMemberFactComparison.cs", "FamilyQuant.Tests/FeatureLedgerEvidence.cs",
            "FamilyQuant.Tests/FeatureLedgerEvidence.Reporting.cs",
        }).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
            Retain(Path.Combine(root, source), source);
        foreach (string source in sources)
            Retain(Path.Combine(frozenRoot, source), Path.Combine("frozen", source));
        Retain(typeof(Program).Assembly.Location, "FamilyQuant.Tests.dll");
        Retain(typeof(ProjectStore).Assembly.Location, "FamilyQuant.Core.dll");
        var run = new FeatureLedgerEvidence.Run("core-members", Environment.CommandLine, args[4], commit, artifacts, root);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "Core-" + args[3] + ".json"),
            new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { run }, rows, kindReceipts));
        Console.WriteLine(args[3] + " member cohort: " + cohort.Length.ToString(CultureInfo.InvariantCulture)
            + " observations; " + results.Count(r => r.Matched).ToString(CultureInfo.InvariantCulture) + " exact declarations; "
            + results.Count(r => !r.Matched).ToString(CultureInfo.InvariantCulture) + " remain unproved.");
        return failed == 0 ? 0 : 1;
    }
}
