using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FeatureLedgerSyntheticModeCommands() =>
    [new("--test-ledger-synthetic-mode", 10532, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ledger-synthetic-mode", (_, _) =>
        {
            string? previous = Environment.GetEnvironmentVariable(FeatureLedger.ReceiptsVariable);
            try
            {
                Environment.SetEnvironmentVariable(FeatureLedger.ReceiptsVariable, FeatureLedger.ReceiptsReleaseOnlyValue);
                FeatureLedgerReceiptGuardTests();
            }
            finally { Environment.SetEnvironmentVariable(FeatureLedger.ReceiptsVariable, previous); }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static FeatureLedgerEvidence.Registry StructuralReceiptRegistry(bool release, Func<FeatureLedgerEvidence.Registry> load) =>
        LedgerReceiptsSkipped(release) ? new() : load();

    private static void FeatureLedgerSyntheticModeTests()
    {
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var observation = inventory.Observations.First(row => !FeatureLedger.IsApprovedDrop(row) && File.Exists(Path.Combine(root, row.Source)));
        string missing = Path.Combine(Path.GetTempPath(), "receipt-never-created-" + Guid.NewGuid().ToString("N"), "missing.log");
        string hash = new('0', 64);
        var run = new FeatureLedgerEvidence.Run("synthetic", "synthetic missing-path fixture", "2000-01-01T00:00:00+00:00",
            FeatureLedger.ReferenceCommit, new() { new(missing, hash) }, root);
        var row = new FeatureLedgerEvidence.Row(observation.Id,
            new() { new(observation.Source, "Synthetic receipt guard fixture", FeatureLedgerEvidence.Hash(Path.Combine(root, observation.Source))) },
            new() { new("synthetic", "test", nameof(FeatureLedgerSyntheticModeTests), "Synthetic missing-path proof", missing, "") }, "Synthetic fixture only");
        var register = new FeatureLedgerEvidence.Register(1, "synthetic-missing-paths", FeatureLedger.ReferenceCommit, new() { run }, new() { row });
        int loads = 0;
        FeatureLedgerEvidence.Registry Load()
        {
            loads++;
            return FeatureLedgerEvidence.Load(root, inventory, new[] { register }, suppliedReviews: Array.Empty<FeatureLedgerEvidence.KindReview>());
        }
        string? previous = Environment.GetEnvironmentVariable(FeatureLedger.ReceiptsVariable);
        try
        {
            Environment.SetEnvironmentVariable(FeatureLedger.ReceiptsVariable, FeatureLedger.ReceiptsReleaseOnlyValue);
            var synthetic = StructuralReceiptRegistry(false, Load);
            Check("S2 release-only mode passes without opening any missing receipt path", loads == 0
                && synthetic.FatalProblems.Count == 0 && synthetic.Entries.Count == 0);
            var full = StructuralReceiptRegistry(true, Load);
            Check("S2 explicit full mode still loads and refuses nonexistent receipt artifacts", loads == 1
                && (full.FatalProblems.Count > 0 || full.Entries.Values.Any(entry => !entry.Valid))
                && full.Entries.Values.SelectMany(entry => entry.Problems).Any(problem => problem.Contains("artifact", StringComparison.OrdinalIgnoreCase)));
            Environment.SetEnvironmentVariable(FeatureLedger.ReceiptsVariable, null);
            var ordinary = StructuralReceiptRegistry(false, Load);
            Check("S2 ordinary mode keeps the receipt loader and missing-path refusal", loads == 2
                && (ordinary.FatalProblems.Count > 0 || ordinary.Entries.Values.Any(entry => !entry.Valid)));
        }
        finally { Environment.SetEnvironmentVariable(FeatureLedger.ReceiptsVariable, previous); }
    }
}
