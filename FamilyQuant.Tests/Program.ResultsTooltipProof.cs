using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ResultsTooltipProof(string[] args)
    {
        if (args.Length is not (1 or 3)) throw new ArgumentException("Supply optional fresh output and date timestamp together.");
        if (args.Length == 1)
        {
            ResultsTooltipTests(); Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Proof output must be fresh.");
        Directory.CreateDirectory(output);
        var original = Console.Out;
        using var transcript = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            ResultsTooltipTests(); Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "proofs.log"); File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        string recovery = new RecoveryChart(Array.Empty<NativeRunFraction>()).ToolTip.ToString()!;
        string isotopes = new IsotopeCountChart(Array.Empty<AnalyticsPeptide>()).ToolTip.ToString()!;
        const string runId = "RS-C13-native-tooltips";
        var rows = inventory.Observations.Where(row => row.Kind == "code fact" &&
            (row.Source == "FamilyQuant/RecoveryChart.cs" && row.Line is >= 72 and <= 75
             || row.Source == "FamilyQuant/IsotopeCountChart.cs" && row.Line is >= 41 and <= 44)).Select(row =>
        {
            bool isRecovery = row.Source.EndsWith("RecoveryChart.cs", StringComparison.Ordinal);
            string actual = isRecovery ? recovery : isotopes;
            string name = isRecovery ? "recovery" : "isotope count";
            return new FeatureLedgerEvidence.Row(row.Id,
                new() { new(row.Source, name + " constructor / ToolTip", FeatureLedgerEvidence.Hash(Path.Combine(root, row.Source))) },
                failed == 0 ? new() { new(runId, "test", nameof(ResultsTooltipTests), name + " tooltip retains each required fact and its dynamic default", log, ""),
                    new(runId, "test", nameof(ResultsTooltipTests), name + " tooltip has at most two sentences", log, "") } : new(),
                "Explicit semantic wording comparison for C13, not an unchanged-literal declaration claim; independent review required.",
                Compared: row.Detail + " -> native ToolTip: " + actual);
        }).ToList();
        if (rows.Count != 7) throw new InvalidDataException("The seven frozen constructor-tooltip fragments changed.");
        string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
        var artifacts = new List<string> { log };
        foreach (string path in new[] { "FamilyQuant/ClusterGenerationControl.Presentation.cs", "FamilyQuant/RecoveryChart.cs", "FamilyQuant/IsotopeCountChart.cs",
            "FamilyQuant.Tests/Program.ResultsTooltips.cs", "FamilyQuant.Tests/Program.ResultsTooltipProof.cs", "FamilyQuant.Tests/Program.Commands.ResultsTooltips.cs" })
        {
            string target = Path.Combine(archive, path.Replace('/', '_')); File.Copy(Path.Combine(root, path), target); artifacts.Add(target);
        }
        foreach (string path in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(ProjectStore).Assembly.Location })
        {
            string target = Path.Combine(archive, Path.GetFileName(path)); File.Copy(path, target); artifacts.Add(target);
        }
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!; string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record source commit.");
        var run = new FeatureLedgerEvidence.Run(runId, Environment.CommandLine, args[2], commit,
            artifacts.Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList(), root);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "RS-C13-tooltips.json"),
            new FeatureLedgerEvidence.Register(1, "RS-C13-tooltips", FeatureLedger.ReferenceCommit, new() { run }, rows));
        return failed == 0 ? 0 : 1;
    }
}
