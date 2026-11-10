using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LedgerDrops2114Commands() =>
    [new("--test-ledger-drops-2114", 84, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ledger-drops-2114", (_, _) =>
        {
            LedgerDrops2114Tests();
            string? previous = Environment.GetEnvironmentVariable(FeatureLedger.ReceiptsVariable);
            try
            {
                Environment.SetEnvironmentVariable(FeatureLedger.ReceiptsVariable, FeatureLedger.ReceiptsReleaseOnlyValue);
                FeatureLedgerBaseTests();
                for (int mutation = 0; mutation < FeatureLedgerMutations; mutation++) FeatureLedgerMutationTest(mutation);
            }
            finally { Environment.SetEnvironmentVariable(FeatureLedger.ReceiptsVariable, previous); }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void LedgerDrops2114Tests()
    {
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        string markdown = File.ReadAllText(Path.Combine(root, "docs/FEATURE-LEDGER.md"));
        var lines = markdown.Split('\n');
        var rows = lines.Where(l => l.StartsWith("| FQ15-", StringComparison.Ordinal)).ToDictionary(l => l.Split('|')[1].Trim(), StringComparer.Ordinal);
        Check("L1 exactly 110 release IDs carry their own approved decision text", FeatureLedger.ApprovedDrops2114.Count == 110
            && FeatureLedger.ApprovedDrops2114.All(pair => rows[pair.Key].Split('|')[8].Trim() == pair.Value));
        Check("L1 listed 2.1.14 drops pass the ledger guard", !FeatureLedger.Validate(inventory, markdown, checkReceipts: false)
            .Any(error => error.StartsWith("Disposition requires Paulo:", StringComparison.Ordinal)));
        string Mutate(string id, int cell, string text)
        {
            string line = rows[id]; var cells = line.Split('|'); cells[cell] = " " + text + " ";
            return markdown.Replace(line, string.Join('|', cells), StringComparison.Ordinal);
        }
        string listed = FeatureLedger.ApprovedDrops2114.Keys.First();
        string otherDecision = FeatureLedger.ApprovedDrops2114.Values.First(text => text != FeatureLedger.ApprovedDrops2114[listed]);
        Check("L1 a listed ID with another approved decision text is refused", FeatureLedger.Validate(inventory,
            Mutate(listed, 8, otherDecision), checkReceipts: false).Contains("Disposition requires Paulo: " + listed));
        var unlisted = inventory.Observations.First(row => !FeatureLedger.IsApprovedDrop(row));
        string[] forged = rows[unlisted.Id].Split('|');
        forged[2] = " DROP "; forged[7] = " APPROVED_DROP "; forged[8] = " " + FeatureLedger.ApprovedDrops2114[listed] + " ";
        Check("L1 an unlisted DROP with valid-looking decision text is refused", FeatureLedger.Validate(inventory,
            markdown.Replace(rows[unlisted.Id], string.Join('|', forged), StringComparison.Ordinal), checkReceipts: false)
            .Contains("Disposition requires Paulo: " + unlisted.Id));
        var legacy = inventory.Observations.First(row => FeatureLedger.IsApprovedDrop(row) && !FeatureLedger.ApprovedDrops2114.ContainsKey(row.Id));
        Check("L1 the 11-01 approval still requires its original exact text", FeatureLedger.ApprovedDropText(legacy) == FeatureLedger.DropApproval
            && FeatureLedger.Validate(inventory, Mutate(legacy.Id, 8, otherDecision), checkReceipts: false)
                .Contains("Disposition requires Paulo: " + legacy.Id));
        var regenerated = FeatureLedger.Markdown(inventory).Split('\n').Where(line => line.StartsWith("| FQ15-", StringComparison.Ordinal))
            .Select(line => line.Split('|')).ToDictionary(cells => cells[1].Trim(), cells => cells[8].Trim(), StringComparer.Ordinal);
        Check("L1 regeneration preserves each release decision text", FeatureLedger.ApprovedDrops2114.All(pair => regenerated[pair.Key] == pair.Value));
        var deferred = FeatureLedger.LoadReleaseDeferrals(root);
        Check("L1 D90 OPEN deferrals contain no newly dropped IDs", !deferred.Ids.Any(FeatureLedger.ApprovedDrops2114.ContainsKey));
    }

    private static int FeatureLedgerReleaseGuardCheck()
    {
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        string markdown = File.ReadAllText(Path.Combine(root, "docs/FEATURE-LEDGER.md"));
        var deferrals = FeatureLedger.LoadReleaseDeferrals(root);
        var errors = FeatureLedger.Validate(inventory, markdown, requireClosed: true, checkReceipts: false, releaseDeferrals: deferrals);
        Console.WriteLine("Feature ledger guard-only: disposition errors=" + errors.Count(error => error.StartsWith("Disposition requires Paulo:", StringComparison.Ordinal))
            + "; D90 entries not OPEN=" + errors.Count(error => error.StartsWith("D90 list entry is not OPEN", StringComparison.Ordinal)) + ".");
        ReportFeatureLedgerValidation(errors);
        Console.WriteLine(FeatureLedger.ReleaseSummary(markdown, deferrals));
        Console.WriteLine("SKIP guard-only: receipt currency and native checks were not run; this is not a full release check.");
        return errors.Count == 0 ? 0 : 1;
    }
}
