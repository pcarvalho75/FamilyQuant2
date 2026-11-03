using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> WordReportCommandTable() =>
    [
        new("--test-word-report", 10, TestCommandPhase.Raw,
            args => args.Contains("--test-word-report", StringComparer.Ordinal), RunWordReportChecks),
    ];

    private static int RunWordReportChecks(string[] args, string workDir)
    {
        WordReportTests(args.Length > 1 ? args[1] : null);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
