using FamilyQuant.Report;
using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> J34dCommands() =>
    [new("--test-j34d", 10491, TestCommandPhase.Raw,
        args => args.Length == 1 && args[0] == "--test-j34d", (_, _) =>
        {
            J34dTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void J34dTests()
    {
        Section("J34d real report reader regressions");
        EnsureApplication();
        var data = AnalyticsBuilder.Build(SyntheticAnalyticsRows(), new Dictionary<string, double>());
        var manifest = new QuantManifest { IdentificationConcordance = new IdentificationConcordanceSummary { PatternLabPsmRows = 15 } };
        var plan = ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), null);
        Check("zero comparable pairs never render an affirmative concordance graphic",
            !plan.Charts.Any(c => c.FileName == PresentationFigures.DatabaseSearchConcordanceFile));
        Check("no-comparable-pairs state prints the recorded audit counts",
            plan.Unavailable.Any(item => item.Title == "Database search concordance"
                && item.Reason.Contains("No precursor-qualified comparable pairs", StringComparison.Ordinal)
                && item.Reason.Contains("15", StringComparison.Ordinal)
                && item.Reason.Contains("0", StringComparison.Ordinal)));
        manifest.IdentificationConcordance = new IdentificationConcordanceSummary
        {
            SharedDatabaseSearchScanKeys = 2, SameChargeDatabaseSearchScanKeys = 2,
            PrecursorQualifiedDatabaseSearchScanKeys = 2, PrecursorQualifiedDatabaseSearchPairs = 2,
            ExactPrecursorQualifiedDatabaseSearchPairs = 1, AdditionalIleLeuEquivalentPrecursorQualifiedPairs = 1,
        };
        Check("a supported concordance study retains its original native graphic",
            ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), null).Charts
                .Single(c => c.FileName == PresentationFigures.DatabaseSearchConcordanceFile).CreateChart() is DatabaseSearchConcordanceChart);
    }
}
