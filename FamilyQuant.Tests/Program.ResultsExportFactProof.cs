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
    private static int ResultsExportFactsCommand(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply frozen root, fresh output and date timestamp.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[2]), frozen = Path.Combine(args[1], "FamilyQuant/AnalyticsControl.xaml.cs");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Proof output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        if (FeatureLedgerEvidence.Hash(frozen) != inventory.Files.Single(file => file.Path == "FamilyQuant/AnalyticsControl.xaml.cs").Sha256)
            throw new InvalidDataException("Frozen source hash differs.");
        var cohort = inventory.Observations.Where(row => row.Source == "FamilyQuant/AnalyticsControl.xaml.cs"
            && (row.Line is >= 188 and <= 193 || row.Line >= 320)
            && row.Kind is "code fact" or "format/export" or "export/storage fact"
            && !FeatureLedger.IsApprovedDrop(row)).ToArray();
        if (cohort.Length != 57) throw new InvalidDataException("The 57-fact assigned cohort changed.");
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        List<ResultsExportFact> facts;
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            facts = ResultsExportFactTests(Path.Combine(output, "native"));
            foreach (var row in cohort)
                Check("RS export row " + row.Id + ": its own native fact checks pass", facts.Any(fact => fact.Line == row.Line)
                    && facts.Where(fact => fact.Line == row.Line).All(fact => fact.Passed));
            foreach (var group in cohort.GroupBy(row => row.Kind).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string assertion = "RS export kind " + group.Key + ": every listed observation has its own passing native fact check";
                Check(assertion, group.All(row => facts.Any(fact => fact.Line == row.Line)
                    && facts.Where(fact => fact.Line == row.Line).All(fact => fact.Passed))); kinds[group.Key] = assertion;
            }
            Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "proofs.log"), values = Path.Combine(output, "actual-facts.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false)); FeatureLedgerEvidence.WriteJson(values, facts);
        string[] implementations = { "FamilyQuant/AnalyticsControl.xaml", "FamilyQuant/AnalyticsControl.xaml.cs", "FamilyQuant/AnalyticsViewModel.cs" };
        void Retain(string source, string name)
        {
            string target = Path.Combine(output, "executed", name); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, false);
        }
        foreach (string path in implementations.Concat(new[] { "FamilyQuant.Tests/Program.ResultsExportFacts.cs", "FamilyQuant.Tests/Program.ResultsExportFactProof.cs",
            "FamilyQuant.Tests/Program.Commands.ResultsExportFacts.cs" })) Retain(Path.Combine(root, path), path);
        Retain(frozen, "frozen/AnalyticsControl.xaml.cs");
        foreach (string path in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(ProjectStore).Assembly.Location })
            Retain(path, Path.GetFileName(path));
        var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("rev-parse"); info.ArgumentList.Add("HEAD"); using var process = Process.Start(info)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); if (process.ExitCode != 0) throw new IOException("Cannot record source commit.");
        var run = new FeatureLedgerEvidence.Run("native", Environment.CommandLine, args[3], commit,
            Directory.GetFiles(output, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList(), root);
        var rows = cohort.Select(row =>
        {
            var checks = facts.Where(fact => fact.Line == row.Line).ToArray(); bool passes = checks.Length > 0 && checks.All(fact => fact.Passed);
            return new FeatureLedgerEvidence.Row(row.Id, implementations.Select(path => new FeatureLedgerEvidence.Implementation(path,
                "Results empty view and native export messages", FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList(), new(),
                "Semantic fact preservation in current wording, not old literal equality. Native complete pack success and refused/incomplete pack are separate checks.",
                passes ? "native-" + row.Kind.Replace('/', '-').Replace(' ', '-') : null,
                passes ? row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + " " + row.Detail + " -> "
                    + string.Join("; ", checks.Select(fact => fact.Fact + ": " + fact.Actual)) : null);
        }).ToList();
        var receipts = kinds.Select(pair => new FeatureLedgerEvidence.KindReceipt("native-" + pair.Key.Replace('/', '-').Replace(' ', '-'), pair.Key,
            new() { new("native", "test", nameof(ResultsExportFactsCommand), pair.Value, log, "") })).ToList();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "RS-export-facts.json"),
            new FeatureLedgerEvidence.Register(1, "RS-export-facts", FeatureLedger.ReferenceCommit, new() { run }, rows, receipts));
        return failed == 0 ? 0 : 1;
    }
}
