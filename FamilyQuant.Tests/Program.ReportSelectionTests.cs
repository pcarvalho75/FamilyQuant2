using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportSelectionCommandTable() =>
    [
        new("--test-report-selection", 427, TestCommandPhase.Raw,
            args => args.Contains("--test-report-selection", StringComparer.Ordinal), (_, _) => RunReportSelectionTests()),
    ];

    private static int RunReportSelectionTests()
    {
        var document = new ReportDocument { Sections = new[]
        {
            new ReportSection("section-one", "First section", new ReportBlock[]
            {
                new ReportParagraph("text-one", "Text"),
                new ReportFigure("figure-one", "First figure", Array.Empty<byte>(), "Caption", 10, 10),
                new ReportFigure("figure-two", "Second figure", Array.Empty<byte>(), "Caption", 10, 10)
            }),
            new ReportSection("section-two", "Second section", Array.Empty<ReportBlock>())
        } };
        var model = new ReportExportViewModel(document, null);
        Check("every section and figure has one checkbox, selected by default", model.Choices.Count == 4 && model.Choices.All(row => row.IsIncluded));
        if (model.Choices.Count != 4) return 1;
        model.Choices.Single(row => row.Id == "figure-one").IsIncluded = false;
        Check("unticking one figure excludes only its id", model.Selection().ExcludedIds!.SequenceEqual(new[] { "figure-one" }));
        model.Choices.Single(row => row.Id == "section-two").IsIncluded = false;
        Check("section and figure choices are independent", model.Selection().ExcludedIds!.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "figure-one", "section-two" }));
        var reopened = new ReportExportViewModel(document, new ReportSelection { ExcludedIds = new[] { "future-figure", "figure-one" } });
        Check("stored choices restore each checkbox and retain unavailable ids", !reopened.Choices.Single(row => row.Id == "figure-one").IsIncluded
            && reopened.Choices.Where(row => row.Id != "figure-one").All(row => row.IsIncluded)
            && reopened.Selection().ExcludedIds!.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "future-figure", "figure-one" }));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
