using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    internal static int FeatureLedgerTests(bool release = false)
    {
        if (!FeatureLedgerInventoryTests(release)) return 1;
        FeatureLedgerEvidenceTests(release);
        ShellMetadataComparisonTests();
        ShellMessageFrameTests();
        FeatureLedgerLiveTests();
        return failed == 0 ? 0 : 1;
    }

    // The default suite runs these parts as separate entries, so shards share them. Each later part runs only
    // when the ledger the first part checks is present, as it did inside FeatureLedgerTests.
    private static bool FeatureLedgerReady()
    {
        string? root = FindLedgerRoot();
        return root != null && File.ReadAllText(Path.Combine(root, "docs", "FEATURE-LEDGER.md")).Split('\n')
            .Any(l => l.StartsWith("| FQ15-", StringComparison.Ordinal));
    }

    private static bool FeatureLedgerInventoryTests(bool release = false)
    {
        if (!FeatureLedgerBaseTests(release)) return false;
        for (int mutation = 0; mutation < FeatureLedgerMutations; mutation++) FeatureLedgerMutationTest(mutation, release);
        return true;
    }

    // A quick gate may leave receipt currency to full gates and the release check; that never reads as a pass.
    private static bool LedgerReceiptsSkipped(bool release) => !release && FeatureLedger.ReceiptsReleaseOnly;

    private static void SkipLedgerReceipts(string check)
    {
        skipped++;
        Console.WriteLine("  SKIP  " + check + " - " + FeatureLedger.ReceiptsVariable + "=" + FeatureLedger.ReceiptsReleaseOnlyValue
            + ": receipt currency is checked by full gates and the release check");
    }

    private static bool FeatureLedgerBaseTests(bool release = false)
    {
        string? root = FindLedgerRoot();
        Check("feature ledger is present in the source checkout", root != null);
        if (root == null) return false;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(
            Path.Combine(root, "docs", "feature-ledger-scan.json")))!;
        string markdown = File.ReadAllText(Path.Combine(root, "docs", "FEATURE-LEDGER.md"));
        bool receipts = !LedgerReceiptsSkipped(release);
        var errors = FeatureLedger.Validate(inventory, markdown, release, checkReceipts: receipts);
        ReportFeatureLedgerValidation(errors);
        if (!receipts) SkipLedgerReceipts("feature ledger VERIFIED rows carry current executed receipts");
        Check("feature ledger includes both help pages", inventory.Observations.Count(o => o.Kind == "help page") == 2);
        Check("feature ledger records Paulo's two UI retirements and preserves homology and engines",
            inventory.Observations.Any(FeatureLedger.IsApprovedDrop) &&
            !FeatureLedger.IsApprovedDrop(new FeatureLedger.Observation("test", "FamilyQuant/DeNovoControl.xaml", 450, "control", "homology")) &&
            !FeatureLedger.IsApprovedDrop(new FeatureLedger.Observation("test", "FamilyQuant/DeNovoControl.xaml.cs", 1836, "code fact", "BuildPartition")));
        Check("feature ledger includes code windows, tooltips, handlers, shortcuts and file formats",
            new[] { "code control", "tooltip", "handler", "shortcut", "format/export", "column" }
                .All(kind => inventory.Observations.Any(o => o.Kind == kind)));
        return markdown.Split('\n').Any(l => l.StartsWith("| FQ15-", StringComparison.Ordinal));
    }

    private const int FeatureLedgerMutations = 6;

    private static void ReportFeatureLedgerValidation(System.Collections.Generic.IReadOnlyList<string> errors) =>
        Check("feature ledger scan equals rows and every row names a known check", errors.Count == 0, LedgerDiagnostics.Capped(errors));

    /// <summary>One planted ledger change that validation must reject. Each validation reads every receipt,
    /// so the default suite gives each its own entry.</summary>
    private static void FeatureLedgerMutationTest(int mutation, bool release = false)
    {
        bool receipts = !LedgerReceiptsSkipped(release);
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(
            Path.Combine(root, "docs", "feature-ledger-scan.json")))!;
        string markdown = File.ReadAllText(Path.Combine(root, "docs", "FEATURE-LEDGER.md"));
        string first = markdown.Split('\n').First(l => l.StartsWith("| FQ15-", StringComparison.Ordinal));
        string MutateCell(int column, string value)
        {
            string[] cells = first.Split('|');
            cells[column] = " " + value + " ";
            return markdown.Replace(first, string.Join("|", cells));
        }
        switch (mutation)
        {
            case 0:
                Check("feature ledger rejects a missing row", FeatureLedger.Validate(inventory, markdown.Replace(first + "\n", ""), checkReceipts: receipts).Count > 0);
                break;
            case 1:
                Check("feature ledger rejects an invented check", FeatureLedger.Validate(inventory,
                    MutateCell(6, "render:missing-screen"), checkReceipts: receipts).Count > 0);
                break;
            case 2:
                Check("feature ledger rejects unapproved DROP", FeatureLedger.Validate(inventory,
                    MutateCell(2, "DROP"), checkReceipts: receipts).Count > 0);
                break;
            case 3:
                string noEvidence = MutateCell(7, "VERIFIED");
                string[] noEvidenceCells = noEvidence.Split('\n').First(l => l.StartsWith("| FQ15-", StringComparison.Ordinal)).Split('|');
                noEvidenceCells[8] = " pending ";
                noEvidence = noEvidence.Replace(noEvidence.Split('\n').First(l => l.StartsWith("| FQ15-", StringComparison.Ordinal)),
                    string.Join("|", noEvidenceCells));
                Check("feature ledger rejects unevidenced closure", FeatureLedger.Validate(inventory,
                    noEvidence, checkReceipts: receipts).Count > 0);
                break;
            case 4:
                Check("feature ledger release gate rejects open rows", FeatureLedger.Validate(inventory,
                    MutateCell(7, "OPEN"), true, checkReceipts: receipts).Count > 0);
                break;
            case 5:
                Check("feature ledger rejects a changed inventory even if row counts are changed with it",
                    FeatureLedger.Validate(inventory with { Scanner = "unapproved" }, markdown, checkReceipts: receipts).Count > 0);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private static void FeatureLedgerLiveTests()
    {
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(
            Path.Combine(root, "docs", "feature-ledger-scan.json")))!;
        string liveOutput = Path.Combine(Path.GetTempPath(), "FamilyQuant.Ledger.Live", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(liveOutput);
        var live = LedgerPreservationTests(root, liveOutput, inventory);
        string nativeRegister = Path.Combine(root, "docs/feature-ledger-evidence/N5.json");
        if (File.Exists(nativeRegister))
        {
            var register = JsonSerializer.Deserialize<FeatureLedgerEvidence.Register>(File.ReadAllText(nativeRegister), FeatureLedgerEvidence.JsonOptions)!;
            var registered = register.Rows.Where(row => row.Proofs.Any(proof => proof.Target == nameof(LedgerPreservationTests)))
                .Select(row => row.ObservationId).ToHashSet(StringComparer.Ordinal);
            Check("live preservation cases cover every registered native observation", registered.SetEquals(live.Select(proof => proof.ObservationId)));
        }
        string? reference = Environment.GetEnvironmentVariable("FAMILYQUANT_LEDGER_REFERENCE_SOURCE");
        if (!string.IsNullOrWhiteSpace(reference))
        {
            var rescanned = FeatureLedger.Scan(reference);
            Check("feature ledger reproduces frozen source hashes and every observation",
                JsonSerializer.Serialize(inventory) == JsonSerializer.Serialize(rescanned));
        }
        Console.WriteLine("Feature ledger inventory SHA256: " + FeatureLedger.Fingerprint(inventory));
    }

    private static string? FindLedgerRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "docs", "feature-ledger-scan.json"))) return directory.FullName;
        return null;
    }
}
