using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> K16LayoutCommands() =>
    [new("--test-k16-layout", 10490, TestCommandPhase.Raw,
        args => args.Length == 1 && args[0] == "--test-k16-layout", (_, _) =>
        {
            K16LayoutTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void K16LayoutTests()
    {
        Section("K16 observed report layout regressions");
        EnsureApplication();
        var (manifest, project, map) = ReportStudyFigureFixture();
        for (int index = 0; index < 500; index++)
            manifest.FamilyPeptideLocalization!.Families.Add(new() { FamilyUserId = 1000 + index });
        var plan = ReportStudyFigures.Plan(manifest, project, panelMap: map);
        var unplaced = plan.Unavailable.Where(item => item.Title.StartsWith("Protein coverage: group ", StringComparison.Ordinal)).ToArray();
        Check("unplaced coverage diagnostics stay complete without becoming Word paragraphs",
            unplaced.Length >= 500 && unplaced.All(item => item.SupplementOnly));
        var blocks = new List<ReportBlock>();
        ReportFigureAvailabilityBlocks.Append(blocks, ReportSectionIds.Results, plan.Unavailable);
        Check("Word does not repeat per-group coverage diagnostic messages",
            !blocks.OfType<ReportParagraph>().Any(p => p.Text.StartsWith("Protein coverage: group ", StringComparison.Ordinal)));
        var table = blocks.OfType<ReportTable>().SingleOrDefault(t => t.Id == "results:figure-availability");
        Check("every original group and availability reason reaches the supplementary detail table",
            table is { SupplementOnly: true } && table.Rows.Count == unplaced.Length + 1
            && unplaced.All(item => table.Rows.Any(row => row.SequenceEqual(new[] { "Group", item.Title, item.Reason }))));
        string count = unplaced.Length.ToString(CultureInfo.InvariantCulture);
        Check("the printed group count has its own native workbook value",
            table != null && table.Rows[0].SequenceEqual(new[] { "Summary", "Unplaced groups", count }));
        var layout = ReportSupplementLayout.Apply(new ReportDocument { Sections = new[]
        {
            new ReportSection(ReportSectionIds.Results, "Results", blocks),
        } }, new Dictionary<string, string>());
        var references = layout.Document.Sections.Single().Blocks.OfType<ReportParagraph>()
            .Where(p => p.Id == "results:figure-availability").ToArray();
        Check("Word receives one counted coverage notice with the reason and a workbook citation",
            references.Length == 1 && references[0].Text.Contains("for " + count + " groups", StringComparison.Ordinal)
            && references[0].Text.Contains("outside the panel or conflicting", StringComparison.Ordinal)
            && references[0].Text.Contains("companion supplement workbook", StringComparison.Ordinal)
            && layout.Sheets.Single().Table.Rows.Count == unplaced.Length + 1);
    }
}
