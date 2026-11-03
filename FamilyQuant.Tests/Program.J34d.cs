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
        var families = FamilyFixture(3);
        families.SourceSequenceSummary.Families.Clear();
        families.SourceSequenceSummary.Families.Add(new()
        {
            FamilyUserId = families.Families[0].FamilyUserId, Description = "Synthetic search-only census row", SearchDistinctSequences = 17,
        });
        var sourcePlan = ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), families);
        Check("unavailable disjoint source sets do not occupy an empty source-census figure",
            !sourcePlan.Charts.Any(c => c.FileName.StartsWith("family_source_sequence_counts", StringComparison.Ordinal)));
        Check("source-set availability is explicit without claiming all de novo evidence is absent",
            sourcePlan.Unavailable.Any(item => item.Title == "Family source sequence counts"
                && item.Reason.Contains("PEAKS de-novo-only and DeepNovo", StringComparison.Ordinal)
                && item.Reason.Contains("Other de novo sources are separate", StringComparison.Ordinal)));
        Check("unavailable source graphic retains the original complete census and values in the workbook",
            ReportCurrentResults.Build(data, manifest, families).Sections.SelectMany(s => s.Blocks).OfType<ReportTable>()
                .Any(t => t.Id == "family-source-sequences" && t.SupplementOnly && t.Rows.Count == 1 && t.Rows[0].Contains("17")));
        families.SourceSequenceSummary.Families[0].DeNovoUnionDistinctSequences = 2;
        families.SourceSequenceSummary.Families[0].DeepNovoExclusiveDistinctSequences = 2;
        Check("nonempty source sets retain their existing source-census drawing",
            ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), families).Charts
                .Single(c => c.FileName == PresentationFigures.FamilySourceSequenceFile).CreateChart() is ReportFamilySourceSequenceChart);
    }
}
