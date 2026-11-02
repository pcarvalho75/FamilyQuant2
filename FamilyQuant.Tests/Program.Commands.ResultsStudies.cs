using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsStudyCommandTable() =>
    [
        new("--report-study-snapshot", 2, TestCommandPhase.Raw, ReportStudySnapshotRequested, RunReportStudySnapshot),
        new("--test-report-study-boundary", 3, TestCommandPhase.Raw, ReportStudyBoundaryRequested, RunReportStudyBoundary),
    ];

    private static bool ReportStudySnapshotRequested(string[] args) => args.Length == 2 && args[0] == "--report-study-snapshot";
    private static bool ReportStudyBoundaryRequested(string[] args) => args.Contains("--test-report-study-boundary", StringComparer.Ordinal);
    private static int RunReportStudySnapshot(string[] args, string workDir) => ReportStudySnapshot(args[1]);
    private static int RunReportStudyBoundary(string[] args, string workDir)
    {
        ReportStudyBoundaryTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
