using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportFigureCommandTable() =>
    [
        new("--test-report-figures", 10, TestCommandPhase.Raw,
            args => args.Contains("--test-report-figures", StringComparer.Ordinal), RunReportFigureChecks),
    ];

    private static int RunReportFigureChecks(string[] args, string workDir)
    {
        ReportFigureTests(args.Length > 1 ? args[1] : System.IO.Path.Combine(workDir, "report-figures"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
