using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportStoredCommandTable() =>
    [
        new("--test-report-stored", 10, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-report-stored", RunReportStoredChecks),
    ];

    private static int RunReportStoredChecks(string[] args, string workDir)
    {
        ReportStoredChecks();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
