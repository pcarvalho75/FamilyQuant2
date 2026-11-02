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
    private static int ResultsMemberComparisonCommand(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply frozen root, fresh output and a timestamp from date.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Comparison output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        // These are the source modules assigned to RS and RS/C5, not a scientific classification rule.
        string[] assigned = { "AnalyticsControl.xaml.cs", "AnalyticsFigures.cs", "AnalyticsModel.cs", "ChartImageExporter.cs",
            "ChartPalette.cs", "FamilyContributionChart.cs", "IdentityScanChart.cs", "IsotopeCountChart.cs", "NormalizationMode.cs",
            "PercentIonCurrentChart.cs", "QuantitationProfile.cs", "RecoveryChart.cs", "SharedPeptidePolicy.cs", "XicMassChart.cs" };
        var cohort = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row)
            && assigned.Contains(Path.GetFileName(row.Source), StringComparer.Ordinal)).ToArray();
        var snapshots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
        {
            string before = Path.Combine(frozenRoot, file.Path);
            if (!File.Exists(before) || FeatureLedgerEvidence.Hash(before) != file.Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + file.Path);
            snapshots[file.Path] = File.ReadAllText(before);
        }
        var candidates = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string source in cohort.Select(row => row.Source).Distinct(StringComparer.Ordinal))
        {
            string current = FeatureLedgerEvidence.CurrentPath(root, source);
            string[] paths = source == "FamilyQuant/AnalyticsControl.xaml.cs"
                ? new[] { current, "FamilyQuant/AnalyticsViewModel.cs" }
                : new[] { current };
            candidates[source] = paths.Where(p => File.Exists(Path.Combine(root, p)))
                .ToDictionary(p => p, p => File.ReadAllText(Path.Combine(root, p)), StringComparer.Ordinal);
        }
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var results = new List<LedgerMemberFactComparison.Result>();
        var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        var kinds = cohort.ToDictionary(row => row.Id, row => row.Kind, StringComparer.Ordinal);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            foreach (var row in cohort)
            {
                var result = LedgerMemberFactComparison.Compare(row, snapshots[row.Source], candidates[row.Source]);
                // Reviewed source moves: these six classes now inherit ChartDrawingBase instead of
                // FrameworkElement, retaining their owners. Two private display helpers moved to the
                // view model. Explicit mapping still requires exact complete member tokens.
                string[] chartMoves = { "FamilyContributionChart", "IdentityScanChart", "IsotopeCountChart",
                    "PercentIonCurrentChart", "RecoveryChart", "XicMassChart" };
                string sourceType = Path.GetFileNameWithoutExtension(row.Source);
                if (chartMoves.Contains(sourceType, StringComparer.Ordinal)
                    && result.FrozenMember.StartsWith("FamilyQuant." + sourceType + "::", StringComparison.Ordinal))
                    result = LedgerMemberFactComparison.Compare(row, snapshots[row.Source], candidates[row.Source],
                        new(result.FrozenMember, FeatureLedgerEvidence.CurrentPath(root, row.Source), result.FrozenMember));
                else if (row.Source == "FamilyQuant/AnalyticsControl.xaml.cs"
                    && (result.FrozenMember == "FamilyQuant.AnalyticsControl::private static string DeNovoSourceClause ( QuantManifest ? manifest )"
                        || result.FrozenMember == "FamilyQuant.AnalyticsControl::private static string IdentificationCountingClause ( AnalyticsData d , QuantManifest ? manifest )"))
                    result = LedgerMemberFactComparison.Compare(row, snapshots[row.Source], candidates[row.Source],
                        new(result.FrozenMember, "FamilyQuant/AnalyticsViewModel.cs",
                            result.FrozenMember.Replace("FamilyQuant.AnalyticsControl::", "FamilyQuant.AnalyticsViewModel::", StringComparison.Ordinal)));
                results.Add(result);
                if (result.Matched) Check("member declaration " + row.Id + ": " + result.Symbol, true);
                else { skipped++; Console.WriteLine("  SKIP  " + row.Id + ": " + result.Reason); }
            }
            foreach (var group in results.Where(r => r.Matched).GroupBy(r => kinds[r.Id]).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                string assertion = "RS declaration kind " + group.Key + ": every listed literal retains its complete containing member";
                Check(assertion, group.All(r => r.FrozenMemberSha256 == r.CurrentMemberSha256 && r.FrozenOccurrences == r.CurrentOccurrences));
                assertions[group.Key] = assertion;
            }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "comparisons.log"), data = Path.Combine(output, "comparisons.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false)); FeatureLedgerEvidence.WriteJson(data, results);
        string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
        var artifacts = new List<string> { log, data };
        foreach (string path in candidates.Values.SelectMany(c => c.Keys).Distinct(StringComparer.Ordinal).Concat(new[] {
            "FamilyQuant.Tests/Program.ResultsDeclarations.cs", "FamilyQuant.Tests/LedgerMemberFactComparison.cs",
            "FamilyQuant.Tests/Program.Commands.ResultsPreservation.cs" }))
        {
            string target = Path.Combine(archive, path.Replace('/', '_')); File.Copy(Path.Combine(root, path), target); artifacts.Add(target);
        }
        foreach (string path in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(ProjectStore).Assembly.Location })
        {
            string target = Path.Combine(archive, Path.GetFileName(path)); File.Copy(path, target); artifacts.Add(target);
        }
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true,
            UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record executing commit.");
        const string runId = "RS-member-declarations";
        var run = new FeatureLedgerEvidence.Run(runId, Environment.CommandLine, args[3], commit,
            artifacts.Select(p => new FeatureLedgerEvidence.Artifact(p, FeatureLedgerEvidence.Hash(p))).ToList(), root);
        var byId = cohort.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var rows = results.Select(result =>
        {
            string current = result.CurrentPath.Length > 0 ? result.CurrentPath : FeatureLedgerEvidence.CurrentPath(root, byId[result.Id].Source);
            var mapping = File.Exists(Path.Combine(root, current))
                ? new List<FeatureLedgerEvidence.Implementation> { new(current, result.Symbol.Length > 0 ? result.Symbol : "source module; member unresolved",
                    FeatureLedgerEvidence.Hash(Path.Combine(root, current))) } : new();
            return new FeatureLedgerEvidence.Row(result.Id, mapping, new(),
                result.Reason + " Declaration comparison does not prove runtime behavior.",
                result.Matched ? "member-" + kinds[result.Id].Replace('/', '-').Replace(' ', '-') : null,
                result.Matched ? result.FrozenMember + ": " + result.Before + " -> " + result.CurrentPath + ": " + result.CurrentMember
                    + "; exact tokens SHA256 " + result.CurrentMemberSha256 + "; occurrences " + result.CurrentOccurrences.ToString(CultureInfo.InvariantCulture) : null);
        }).ToList();
        var receipts = assertions.Select(pair => new FeatureLedgerEvidence.KindReceipt("member-" + pair.Key.Replace('/', '-').Replace(' ', '-'), pair.Key,
            new() { new(runId, "test", nameof(ResultsMemberComparisonCommand), pair.Value, log, "") })).ToList();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "RS-members.json"),
            new FeatureLedgerEvidence.Register(1, "RS-members", FeatureLedger.ReferenceCommit, new() { run }, rows, receipts));
        return failed == 0 ? 0 : 1;
    }
}
