using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AssayLedgerCommandTable() =>
    [
        new("--ledger-assay-figure-gaps", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-figure-gaps", (args, _) => AssayFigureGapsLedgerCommand(args)),
        new(AssayFigureGapSnapshot.Argument, 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == AssayFigureGapSnapshot.Argument, (args, _) => AssayFigureGapSnapshot.Run(args)),
        new("--test-assay-figure-gaps", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-assay-figure-gaps", (args, _) =>
            { AssayFigureGapGuardTests(); System.Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1; }),
        new("--ledger-assay-build-workflow", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-build-workflow", (args, _) => AssayBuildWorkflowLedgerCommand(args)),
        new("--test-assay-build-workflow", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-assay-build-workflow", (args, _) =>
            { AssayBuildWorkflowGuardTests(); System.Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1; }),
        new(AssayBuildOutputSnapshot.Argument, 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == AssayBuildOutputSnapshot.Argument, (args, _) => AssayBuildOutputSnapshot.Run(args)),
        new("--ledger-assay-partition-format", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-partition-format", (args, _) => AssayPartitionLedgerCommand(args)),
        new(AssayPartitionSnapshot.Argument, 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == AssayPartitionSnapshot.Argument, (args, _) => AssayPartitionSnapshot.Run(args)),
        new("--ledger-assay-native-paths", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-native-paths", (args, _) => AssayNativePathsLedgerCommand(args)),
        new("--ledger-assay-spectrum-details", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-spectrum-details", (args, _) => AssaySpectrumDetailsLedgerCommand(args)),
        new("--ledger-assay-build-report", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-build-report", (args, _) => AssayBuildReportLedgerCommand(args)),
        new(AssayBuildReportSnapshot.Argument, 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == AssayBuildReportSnapshot.Argument, (args, _) => AssayBuildReportSnapshot.Run(args)),
        new("--ledger-assay-spectrum-exports", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-spectrum-exports", (args, _) => AssaySpectrumExportLedgerCommand(args)),
        new("--assay-spectrum-export-probe", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--assay-spectrum-export-probe", (args, _) => AssaySpectrumExportProbeCommand(args)),
        new("--ledger-assay-build-properties", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-build-properties", (args, _) => AssayBuildPropertiesLedgerCommand(args)),
        new("--ledger-assay-spectrum-actions", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-spectrum-actions", (args, _) => AssaySpectrumActionsLedgerCommand(args)),
        new("--ledger-assay-spectrum-properties", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-assay-spectrum-properties", (args, _) => AssaySpectrumLedgerCommand(args)),
        new("--test-assay-spectrum-properties", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-assay-spectrum-properties", (args, _) =>
            { AssaySpectrumPropertyTests(); AssaySpectrumActionTests(); AssayBuildPropertyTests(); AssaySpectrumExportGuardTests(); System.Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1; }),
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
