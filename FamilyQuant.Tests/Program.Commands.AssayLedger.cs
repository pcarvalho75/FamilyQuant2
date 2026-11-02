using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AssayLedgerCommandTable() =>
    [
        new("--ledger-assay-compare", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-compare", (args, _) => LedgerAssayCompareCommand(args)),
    ];
}
