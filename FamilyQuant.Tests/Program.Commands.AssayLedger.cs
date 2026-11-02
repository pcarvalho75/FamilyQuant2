using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AssayLedgerCommandTable() =>
    [
        new("--ledger-assay-cache", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-cache", (args, _) => AssayCacheLedgerCommand(args)),
        new("--ledger-assay-cohort", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-cohort", (args, _) => LedgerAssayCohortCommand(args)),
        new(AssayCacheSnapshot.Argument, 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == AssayCacheSnapshot.Argument, (args, _) => AssayCacheSnapshot.Run(args)),
        new("--ledger-assay-compare", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-compare", (args, _) => LedgerAssayCompareCommand(args)),
    ];
}
