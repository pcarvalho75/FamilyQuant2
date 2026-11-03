using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportReaderCommandTable() =>
    [new("--test-report-reader", 440, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-report-reader", (_, _) =>
        {
            ReportReaderTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReportReaderTests()
    {
        Section("Report reader - labelled counting grains and print layout");
        EnsureApplication();
        var data = AnalyticsBuilder.Build(SyntheticAnalyticsRows(), new Dictionary<string, double>());
        // Deliberately different populations: copying AnalyticsData.DeNovoCount is incorrect here.
        data.Grouped = new NativeRunFraction { IsGrouped = true, DeNovoPeptides = 7, IdentifiedPeptides = 11 };
        var manifest = new QuantManifest { DeNovoIdentification = new DeNovoIdentificationSummary() };
        var plan = ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), null);
        var figure = plan.Charts.Single(c => c.FileName == PresentationFigures.PeptideEvidenceFile);
        var facts = figure.CaptionInput!.Numbers;
        Check("representative caption names the same credited count as its figure",
            facts.Any(n => n.Label.Contains("Credited de novo", StringComparison.Ordinal) && n.Value == 7)
            && figure.Caption.Contains("after cross-side suppression", StringComparison.Ordinal));
        Check("pre-suppression representatives are separately named",
            facts.Any(n => n.Label.Contains("before cross-side suppression", StringComparison.Ordinal)
                && n.Label.Contains("de novo", StringComparison.Ordinal) && n.Value == data.DeNovoCount));
    }
}
