using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsWorkbookLayoutCommandTable() =>
    [
        new("--test-report-layout", 10, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-report-layout", RunReportSupplementLayoutChecks),
    ];
    private static int RunReportSupplementLayoutChecks(string[] args, string workDir)
    {
        ReportSupplementChecks(args.Length > 1 ? args[1] : null);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
