using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record AssayLedgerSelection(string Id, string CurrentPath, List<string>? CurrentPaths = null);

    private static int LedgerAssayCohortCommand(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("Supply frozen source root, selection plan, fresh output, step and timestamp from date.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), planPath = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Comparison output must be fresh.");
        if (!Regex.IsMatch(args[4], "^[A-Za-z0-9-]+$")) throw new ArgumentException("Invalid receipt step name.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var selections = JsonSerializer.Deserialize<List<AssayLedgerSelection>>(File.ReadAllText(planPath), FeatureLedgerEvidence.JsonOptions)!;
        if (selections.Count == 0 || selections.GroupBy(row => row.Id).Any(group => group.Count() != 1))
            throw new InvalidDataException("The selection plan must contain unique retained observation IDs.");
        var assigned = selections.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var cohort = inventory.Observations.Where(row => assigned.Contains(row.Id)).OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        if (cohort.Length != assigned.Count || cohort.Any(FeatureLedger.IsApprovedDrop)) throw new InvalidDataException("The selection plan differs from retained frozen IDs.");
        var frozen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
        {
            string path = Path.Combine(frozenRoot, file.Path);
            if (!File.Exists(path) || FeatureLedgerEvidence.Hash(path) != file.Sha256) throw new InvalidDataException("Frozen source hash differs: " + file.Path);
            frozen[file.Path] = File.ReadAllText(path);
        }
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string relative in selections.SelectMany(row => row.CurrentPaths ?? new() { row.CurrentPath }).Distinct(StringComparer.Ordinal))
        {
            string full = Path.GetFullPath(Path.Combine(root, relative));
            if (Path.IsPathFullyQualified(relative) || !full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.Split(Path.DirectorySeparatorChar).Any(part => part.Equals("Orion", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The current mapping leaves the permitted worktree.");
            if (File.Exists(full)) current[relative] = File.ReadAllText(full);
        }
        var empty = new Dictionary<string, string>();
        var results = new List<LedgerMemberFactComparison.Result>();
        var kinds = cohort.ToDictionary(row => row.Id, row => row.Kind, StringComparer.Ordinal);
        var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            Check("AS cohort contains every selected retained row exactly once", cohort.Length == selections.Count);
            foreach (var row in cohort)
            {
                var selected = selections.Single(item => item.Id == row.Id);
                var paths = selected.CurrentPaths ?? new() { selected.CurrentPath };
                var candidates = current.Where(file => paths.Contains(file.Key, StringComparer.Ordinal))
                    .ToDictionary(file => file.Key, file => file.Value, StringComparer.Ordinal);
                var anchor = LedgerMemberFactComparison.Compare(row, frozen[row.Source], empty);
                var result = anchor.FrozenMember.Length == 0 ? anchor
                    : LedgerMemberFactComparison.Compare(row, frozen[row.Source], candidates);
                if (row.Source.EndsWith(".xaml", StringComparison.Ordinal))
                {
                    var declaration = LedgerDeclarationComparison.Compare(row, XDocument.Parse(frozen[row.Source]),
                        candidates.ToDictionary(file => file.Key, file => XDocument.Parse(file.Value), StringComparer.Ordinal));
                    result = new(declaration.Id, declaration.Matched, declaration.Before, declaration.Actual,
                        declaration.CurrentPath, declaration.Symbol, declaration.Reason);
                }
                // This inspected utility relocation only widens visibility. Pin both complete type
                // headers; the shared comparer still checks every token in the selected member.
                if (row.Source == "FamilyQuant/DeNovo/SpreadsheetSafeCsv.cs" && result.CurrentPath.Length > 0
                    && LedgerMemberFactComparison.HasTypeDeclaration(frozen[row.Source], "FamilyQuant.DeNovo.SpreadsheetSafeCsv", "internal static class SpreadsheetSafeCsv")
                    && LedgerMemberFactComparison.HasTypeDeclaration(current[result.CurrentPath], "FamilyQuant.DeNovo.SpreadsheetSafeCsv", "public static class SpreadsheetSafeCsv"))
                {
                    result = LedgerMemberFactComparison.Compare(row, frozen[row.Source], candidates,
                        new(result.FrozenMember, result.CurrentPath, result.FrozenMember));
                    if (result.Matched) result = result with { Reason = result.Reason + " Parsed utility type visibility widened from internal to public; full member tokens still agree." };
                }
                if (row.Source == "FamilyQuant/SpectrumComparisonWindow.cs" && result.CurrentPath.Length > 0
                    && result.Reason == "The enclosing type declaration changed and needs an explicit reviewed relocation."
                    && LedgerMemberFactComparison.HasTypeDeclaration(frozen[row.Source], "FamilyQuant.SpectrumComparisonWindow", "public static class SpectrumComparisonWindow")
                    && candidates.Values.All(source => LedgerMemberFactComparison.HasTypeDeclaration(source,
                        "FamilyQuant.SpectrumComparisonWindow", "public static partial class SpectrumComparisonWindow")))
                {
                    result = LedgerMemberFactComparison.Compare(row, frozen[row.Source], candidates,
                        new(result.FrozenMember, result.CurrentPath, result.FrozenMember));
                    if (result.Matched) result = result with { Reason = result.Reason + " Parsed static type split into partial files; complete member tokens still agree." };
                }
                results.Add(result);
                if (result.Matched) Check("AS member " + row.Id + ": " + result.Symbol, true);
                else { skipped++; Console.WriteLine("  SKIP  " + row.Id + ": " + result.Reason); }
            }
            foreach (var group in results.Where(result => result.Matched).GroupBy(result => kinds[result.Id]).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string assertion = "AS member kind " + group.Key + ": every listed frozen/current member declaration agrees";
                Check(assertion, group.All(result => result.Matched)); assertions[group.Key] = assertion;
            }
            Check("AS comparisons retain every assigned observation exactly once", results.Count == cohort.Length
                && results.Select(result => result.Id).Distinct(StringComparer.Ordinal).Count() == cohort.Length);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "comparisons.log"), data = Path.Combine(output, "comparisons.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(data, results);
        string Escape(string value) => value.Replace("\t", "\\t", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        File.WriteAllLines(Path.Combine(output, "unproven.tsv"), new[] { "id\tkind\tsource\tcurrent_path\treason" }
            .Concat(results.Where(result => !result.Matched).Select(result => string.Join("\t", result.Id, kinds[result.Id],
                cohort.Single(row => row.Id == result.Id).Source, result.CurrentPath, Escape(result.Reason)))), new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(output, "counts-by-source.tsv"), new[] { "source\tmatched\topen" }
            .Concat(cohort.GroupBy(row => row.Source).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => group.Key + "\t"
                + results.Count(result => result.Matched && group.Any(row => row.Id == result.Id)).ToString(CultureInfo.InvariantCulture) + "\t"
                + results.Count(result => !result.Matched && group.Any(row => row.Id == result.Id)).ToString(CultureInfo.InvariantCulture))), new UTF8Encoding(false));
        string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
        var artifactPaths = new List<string> { log, data, Path.Combine(output, "unproven.tsv"), Path.Combine(output, "counts-by-source.tsv"), planPath };
        foreach (string relative in new[] { "FamilyQuant.Tests/Program.LedgerAssayCohort.cs", "FamilyQuant.Tests/Program.Commands.AssayLedger.cs", "FamilyQuant.Tests/LedgerMemberFactComparison.cs", "FamilyQuant.Tests/LedgerDeclarationComparison.cs" })
        {
            string retained = Path.Combine(archive, Path.GetFileName(relative));
            File.Copy(Path.Combine(root, relative), retained); artifactPaths.Add(retained);
        }
        string assembly = Path.Combine(archive, "FamilyQuant.Tests.dll");
        File.Copy(typeof(Program).Assembly.Location, assembly); artifactPaths.Add(assembly);
        var git = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        git.ArgumentList.Add("rev-parse"); git.ArgumentList.Add("HEAD");
        using var process = Process.Start(git)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record executing commit.");
        const string runId = "AS-member-comparison";
        string KindId(string kind) => "member-" + kind.Replace('/', '-').Replace(' ', '-');
        var rows = results.Select(result => new FeatureLedgerEvidence.Row(result.Id,
            result.CurrentPath.Length > 0 ? new() { new(result.CurrentPath, result.Symbol, FeatureLedgerEvidence.Hash(Path.Combine(root, result.CurrentPath))) } : new(),
            new(), result.Reason, result.Matched && failed == 0 ? KindId(kinds[result.Id]) : null,
            result.Before + " -> " + result.CurrentPath + ": " + result.Symbol + " = " + result.Actual)).ToList();
        var kindReceipts = assertions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new FeatureLedgerEvidence.KindReceipt(KindId(pair.Key), pair.Key,
                new() { new(runId, "test", nameof(LedgerAssayCohortCommand), pair.Value, log, "") })).ToList();
        var run = new FeatureLedgerEvidence.Run(runId, Environment.CommandLine, args[5], commit,
            artifactPaths.Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList(), root);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, args[4] + ".json"),
            new FeatureLedgerEvidence.Register(1, args[4], FeatureLedger.ReferenceCommit, new() { run }, rows, kindReceipts));
        return failed == 0 ? 0 : 1;
    }
}
