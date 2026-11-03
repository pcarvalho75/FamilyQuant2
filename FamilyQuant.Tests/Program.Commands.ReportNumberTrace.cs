using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportNumberTraceCommandTable() =>
    [new("--trace-report-numbers", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--trace-report-numbers", (args, _) =>
        {
            if (args.Length != 4 && !(args.Length == 6 && args[4] == "--compare"))
                throw new ArgumentException("Use --trace-report-numbers <report.docx> <sidecar-folder> <output.csv> [--compare <before.csv>].");
            if (args.Length == 6) ReportNumberTraceComparison.ValidateSources(args[5], args[3], args[1], args[2]);
            int exit = ReportNumberTrace.Run(args[1], args[2], args[3]);
            if (args.Length == 6) ReportNumberTraceComparison.Write(args[5], args[3]);
            return exit;
        })];
}
