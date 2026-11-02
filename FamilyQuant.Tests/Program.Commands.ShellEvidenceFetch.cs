using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellEvidenceFetchCommandTable() =>
    [
        new("--test-shell-evidence-cancel", 10160, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-shell-evidence-cancel", RunShellEvidenceFetchChecks),
    ];

    private static int RunShellEvidenceFetchChecks(string[] args, string workDir)
    {
        ShellEvidenceFetchCancellationTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
