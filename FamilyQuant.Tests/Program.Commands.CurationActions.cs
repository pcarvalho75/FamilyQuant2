using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationActionsCommandTable() =>
    [
        new("--prove-curation-actions", 10124, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-curation-actions", (args, _) => CurationActionsLedger(args)),
        new(CurationExportSnapshot.Argument, 10125, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == CurationExportSnapshot.Argument, (args, _) => CurationExportSnapshot.Run(args)),
        new("--shell-review-wording", 10126, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--shell-review-wording", (args, _) => ShellReviewWordingCommand(args)),
        new("--check-shell-review-list", 10127, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--check-shell-review-list", (args, _) => ShellReviewWordingCheck(args))
    ];
}
