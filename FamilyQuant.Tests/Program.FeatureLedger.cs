using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    internal static int FeatureLedgerTests(bool release = false)
    {
        string? root = FindLedgerRoot();
        Check("feature ledger is present in the source checkout", root != null);
        if (root == null) return 1;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(
            Path.Combine(root, "docs", "feature-ledger-scan.json")))!;
        string markdown = File.ReadAllText(Path.Combine(root, "docs", "FEATURE-LEDGER.md"));
        var errors = FeatureLedger.Validate(inventory, markdown, release);
        Check("feature ledger scan equals rows and every row names a known check", errors.Count == 0,
            string.Join("; ", errors.Take(5)));
        Check("feature ledger includes both help pages", inventory.Observations.Count(o => o.Kind == "help page") == 2);
        Check("feature ledger records Paulo's two UI retirements and preserves homology and engines",
            inventory.Observations.Any(FeatureLedger.IsApprovedDrop) &&
            !FeatureLedger.IsApprovedDrop(new FeatureLedger.Observation("test", "FamilyQuant/DeNovoControl.xaml", 450, "control", "homology")) &&
            !FeatureLedger.IsApprovedDrop(new FeatureLedger.Observation("test", "FamilyQuant/DeNovoControl.xaml.cs", 1836, "code fact", "BuildPartition")));
        Check("feature ledger includes code windows, tooltips, handlers, shortcuts and file formats",
            new[] { "code control", "tooltip", "handler", "shortcut", "format/export", "column" }
                .All(kind => inventory.Observations.Any(o => o.Kind == kind)));
        string? first = markdown.Split('\n').FirstOrDefault(l => l.StartsWith("| FQ15-", StringComparison.Ordinal));
        if (first == null) return 1;
        string MutateCell(int column, string value)
        {
            string[] cells = first.Split('|');
            cells[column] = " " + value + " ";
            return markdown.Replace(first, string.Join("|", cells));
        }
        Check("feature ledger rejects a missing row", FeatureLedger.Validate(inventory, markdown.Replace(first + "\n", "")).Count > 0);
        Check("feature ledger rejects an invented check", FeatureLedger.Validate(inventory,
            MutateCell(6, "render:missing-screen")).Count > 0);
        Check("feature ledger rejects unapproved DROP", FeatureLedger.Validate(inventory,
            MutateCell(2, "DROP")).Count > 0);
        string noEvidence = MutateCell(7, "VERIFIED");
        string[] noEvidenceCells = noEvidence.Split('\n').First(l => l.StartsWith("| FQ15-", StringComparison.Ordinal)).Split('|');
        noEvidenceCells[8] = " pending ";
        noEvidence = noEvidence.Replace(noEvidence.Split('\n').First(l => l.StartsWith("| FQ15-", StringComparison.Ordinal)),
            string.Join("|", noEvidenceCells));
        Check("feature ledger rejects unevidenced closure", FeatureLedger.Validate(inventory,
            noEvidence).Count > 0);
        Check("feature ledger release gate rejects open rows", FeatureLedger.Validate(inventory,
            MutateCell(7, "OPEN"), true).Count > 0);
        Check("feature ledger rejects a changed inventory even if row counts are changed with it",
            FeatureLedger.Validate(inventory with { Scanner = "unapproved" }, markdown).Count > 0);
        FeatureLedgerEvidenceTests();
        ShellMetadataComparisonTests();
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
        return failed == 0 ? 0 : 1;
    }

    private static string? FindLedgerRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "docs", "feature-ledger-scan.json"))) return directory.FullName;
        return null;
    }
}
