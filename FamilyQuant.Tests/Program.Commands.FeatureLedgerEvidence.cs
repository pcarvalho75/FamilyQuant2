using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LedgerEvidenceCommandTable() =>
    [
        new("--feature-ledger-prove", 10010, TestCommandPhase.Raw, args => LedgerFlag(args, "--feature-ledger-prove"), (args, _) => LedgerPreservationCommand(args)),
        new("--feature-ledger-evidence", 10020, TestCommandPhase.Raw, args => LedgerFlag(args, "--feature-ledger-evidence"), (args, _) => FeatureLedgerEvidence.Command(args)),
        new("--feature-ledger-evidence-import-batch", 10029, TestCommandPhase.Raw, args => LedgerFlag(args, "--feature-ledger-evidence-import-batch"), (args, _) => FeatureLedgerEvidence.Command(args)),
        new("--feature-ledger-evidence-import", 10030, TestCommandPhase.Raw, args => LedgerFlag(args, "--feature-ledger-evidence-import"), (args, _) => FeatureLedgerEvidence.Command(args)),
        new("--test-ledger-import-batch", 10039, TestCommandPhase.Raw, args => LedgerFlag(args, "--test-ledger-import-batch"), (args, _) =>
        { LedgerImportBatchTests(); Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1; }),
        new("--test-ledger-evidence", 10040, TestCommandPhase.Raw, args => LedgerFlag(args, "--test-ledger-evidence"), RunLedgerEvidenceChecks),
        new("--test-ledger-receipt-fixtures", 10041, TestCommandPhase.Raw, args => LedgerFlag(args, "--test-ledger-receipt-fixtures"), (args, _) =>
        { FeatureLedgerReceiptFixtureTests(); Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1; }),
        new("--feature-ledger-kind-review", 10045, TestCommandPhase.Raw, args => LedgerFlag(args, "--feature-ledger-kind-review"), (args, _) => FeatureLedgerEvidence.ReviewCommand(args)),
    ];

    private static bool LedgerFlag(string[] args, string flag) => args.Length > 0 && args[0] == flag;
    private static int RunLedgerEvidenceChecks(string[] args, string workDir)
    {
        FeatureLedgerEvidenceTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
