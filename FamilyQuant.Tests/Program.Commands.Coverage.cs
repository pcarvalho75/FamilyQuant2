using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CoverageCommandTable() =>
    [
        new("--evidence-coverage-check", 10010, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--evidence-coverage-check", (args, _) => EvidenceCoverageCommand(args)),
        new("--test-evidence-coverage", 10020, TestCommandPhase.Raw, args => args.Contains("--test-evidence-coverage", StringComparer.Ordinal), RunEvidenceCoverageTests),
    ];

    private static int RunEvidenceCoverageTests(string[] args, string workDir)
    {
        EvidenceCoverageTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
