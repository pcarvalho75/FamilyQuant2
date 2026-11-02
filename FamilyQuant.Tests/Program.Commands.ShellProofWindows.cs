using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellProofWindowCommands() =>
    [
        new("--test-grid-copy", 10127, TestCommandPhase.Raw,
            args => args.Length == 1 && args[0] == "--test-grid-copy", (_, _) =>
            {
                EnsureApplication();
                GridCopyTests();
                GridCopyRowNumberTests();
                System.Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
        new("--test-shell-proof-windows", 10126, TestCommandPhase.Raw,
            args => args.Length == 1 && args[0] == "--test-shell-proof-windows", ShellProofWindowChecks)
    ];
}
