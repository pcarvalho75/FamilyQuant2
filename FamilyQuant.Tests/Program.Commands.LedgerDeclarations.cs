using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LedgerDeclarationsCommandTable() =>
    [
        new("--test-ledger-declarations", 10100, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-ledger-declarations", RunLedgerDeclarationChecks),
        new("--ledger-declaration-compare", 10110, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-declaration-compare", (args, _) => LedgerDeclarationCommand(args)),
        new("--ledger-shell-compare", 10115, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-shell-compare", (args, _) => ShellLedgerComparisonCommand(args)),
    ];

    private static int RunLedgerDeclarationChecks(string[] args, string workDir)
    {
        LedgerDeclarationComparisonTests();
        LedgerMemberFactComparisonTests();
        ShellLedgerComparisonTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
