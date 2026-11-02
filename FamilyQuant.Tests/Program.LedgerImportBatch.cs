using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void LedgerImportBatchTests()
    {
        Section("Ordered atomic ledger import");
        string sourceRoot = FeatureLedgerEvidence.FindRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(sourceRoot, "docs/feature-ledger-scan.json")))!;
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.ImportBatch.Tests", Path.GetFileName(sourceRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        string ledger = Path.Combine(root, "docs/FEATURE-LEDGER.md");
        string original = FeatureLedger.Markdown(inventory);
        File.WriteAllText(ledger, original);
        File.Copy(Path.Combine(sourceRoot, "docs/feature-ledger-scan.json"), Path.Combine(root, "docs/feature-ledger-scan.json"), true);
        string receipts = Path.Combine(root, "docs/feature-ledger-evidence");
        Directory.CreateDirectory(receipts);
        string target = Path.Combine(receipts, "batch-fixture.json");
        string log = Path.Combine(root, "fixture.log");
        File.WriteAllText(log, "PASS synthetic batch validation fixture\n===== 1 passed, 0 failed, 0 skipped =====\n");
        string source = Path.Combine(root, "fixture.cs"); File.WriteAllText(source, "// Synthetic validator input only.\n");
        var observation = inventory.Observations.First(o => o.Kind == "option/copy" && !FeatureLedger.IsApprovedDrop(o));
        var row = new FeatureLedgerEvidence.Row(observation.Id,
            new() { new("fixture.cs", "synthetic", FeatureLedgerEvidence.Hash(source)) },
            new() { new("fixture", "test", nameof(LedgerImportBatchTests), "synthetic batch validation fixture", log, "") });
        var run = new FeatureLedgerEvidence.Run("fixture", "synthetic batch validation input, never closure", "2026-11-02 00:00:00 -0800", new string('a', 40),
            new() { new(log, FeatureLedgerEvidence.Hash(log)) });
        var register = new FeatureLedgerEvidence.Register(1, "batch-fixture", FeatureLedger.ReferenceCommit, new() { run }, new() { row });
        string first = Path.Combine(root, "first.json"), second = Path.Combine(root, "second.json"), bad = Path.Combine(root, "bad.json");
        FeatureLedgerEvidence.WriteJson(first, register);
        FeatureLedgerEvidence.WriteJson(second, register with { Rows = new() { row with { Proofs = new() } } });
        FeatureLedgerEvidence.WriteJson(bad, register with { Rows = new() { row with { Implementation = new() { row.Implementation[0] with { Sha256 = new string('0', 64) } } } } });
        string cwd = Environment.CurrentDirectory;
        Exception? Run(params string[] args)
        {
            try { int exit = FeatureLedgerEvidence.Command(args); return exit == 0 ? null : new InvalidOperationException("Import returned " + exit); }
            catch (Exception ex) { return ex; }
        }
        try
        {
            Environment.CurrentDirectory = root;
            var refused = Run("--feature-ledger-evidence-import", bad, Path.Combine(root, "single-refused"), "--replace-existing");
            var batchRefused = Run("--feature-ledger-evidence-import-batch", Path.Combine(root, "batch-refused"), first, bad, Path.Combine(root, "must-not-be-read.json"), "--replace-existing");
            Check("batch uses the same stale-proof refusal as a single import", refused is InvalidDataException && batchRefused?.Message == refused.Message);
            Check("a late invalid register leaves ledger and register unchanged", File.ReadAllText(ledger) == original && !File.Exists(target));
            var success = Run("--feature-ledger-evidence-import-batch", Path.Combine(root, "success"), first, second, "--replace-existing");
            Check("batch accepts ordered replacements in one process", success == null);
            Check("later replacement reopens its row while retaining the earlier planned check", success == null &&
                File.ReadAllText(ledger).Split('\n').Any(l => l.StartsWith("| " + observation.Id + " |", StringComparison.Ordinal)
                    && l.Contains("test:" + nameof(LedgerImportBatchTests), StringComparison.Ordinal) && l.Contains("| OPEN |", StringComparison.Ordinal)));
            Check("replacement preserves the previous register bytes", success == null &&
                File.ReadAllBytes(Path.Combine(root, "success/previous-batch-fixture.json")).SequenceEqual(File.ReadAllBytes(first)));
            string serialRoot = Path.Combine(root, "serial");
            Directory.CreateDirectory(Path.Combine(serialRoot, "docs"));
            File.Copy(Path.Combine(root, "docs/feature-ledger-scan.json"), Path.Combine(serialRoot, "docs/feature-ledger-scan.json"));
            File.Copy(source, Path.Combine(serialRoot, "fixture.cs"));
            File.WriteAllText(Path.Combine(serialRoot, "docs/FEATURE-LEDGER.md"), original);
            Environment.CurrentDirectory = serialRoot;
            var serialFirst = Run("--feature-ledger-evidence-import", first, Path.Combine(root, "serial-first"), "--replace-existing");
            var serialSecond = Run("--feature-ledger-evidence-import", second, Path.Combine(root, "serial-second"), "--replace-existing");
            Environment.CurrentDirectory = root;
            Check("ordered batch ledger is byte-identical to separate imports", success == null && serialFirst == null && serialSecond == null &&
                File.ReadAllBytes(ledger).SequenceEqual(File.ReadAllBytes(Path.Combine(serialRoot, "docs/FEATURE-LEDGER.md"))));
            string reports = Path.Combine(root, "success"), serialReports = Path.Combine(root, "serial-second");
            Check("one final batch report and backup match the final separate import byte for byte", success == null && serialSecond == null &&
                Directory.GetFiles(reports).Select(Path.GetFileName).Order().SequenceEqual(Directory.GetFiles(serialReports).Select(Path.GetFileName).Order()) &&
                Directory.GetFiles(reports).All(path => File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Path.Combine(serialReports, Path.GetFileName(path))))));
            string before = File.ReadAllText(ledger), stored = File.Exists(target) ? File.ReadAllText(target) : "";
            var lateFailure = Run("--feature-ledger-evidence-import-batch", Path.Combine(root, "late-refused"), first, bad, "--replace-existing");
            Check("late refusal preserves existing ledger, register and backup state", lateFailure?.Message == refused?.Message &&
                File.ReadAllText(ledger) == before && File.ReadAllText(target) == stored && !Directory.Exists(Path.Combine(root, "late-refused")));
            var replacement = Run("--feature-ledger-evidence-import-batch", Path.Combine(root, "replacement-refused"), first);
            Check("batch preserves explicit replacement permission and atomic refusal", replacement is InvalidOperationException && File.ReadAllText(ledger) == before
                && File.Exists(target) && File.ReadAllText(target) == stored);
        }
        finally { Environment.CurrentDirectory = cwd; }
    }
}
