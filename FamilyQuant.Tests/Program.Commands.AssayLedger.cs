using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AssayLedgerCommandTable() =>
    [
        new("--ledger-assay-spectrum-properties", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-spectrum-properties", (args, _) => AssaySpectrumLedgerCommand(args)),
        new("--test-assay-spectrum-properties", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-assay-spectrum-properties", (args, _) =>
            { AssaySpectrumPropertyTests(); System.Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1; }),
        new(AssayViewSnapshot.Argument, 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == AssayViewSnapshot.Argument, (args, _) => AssayViewSnapshot.Run(args)),
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
