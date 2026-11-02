using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LookLedgerCommandTable() =>
    [
        new("--test-look-copy-preservation", 10200, TestCommandPhase.Raw,
            args => args.Length == 1 && args[0] == "--test-look-copy-preservation", (_, _) =>
            {
                LookCopyComparisonTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
        new("--prove-look-copy", 10210, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-look-copy", (args, _) => LookCopyReceiptCommand(args)),
        new("--prove-look-style-roles", 10220, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-look-style-roles", (args, _) => LookStyleRoleCommand(args)),
        new("--probe-look-home-styles", 10230, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--probe-look-home-styles", (args, _) => LookHomeProbe(args)),
        new("--prove-look-app-tokens", 10240, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-look-app-tokens", (args, _) => LookAppTokenCommand(args)),
    ];
}
