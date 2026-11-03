using FamilyQuant.Report;
using FamilyQuant.Report.Captions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ReportReaderWorkbookReferenceTests()
    {
        const string csv = "name,source_value,note\nGroup A,10,kept\nGroup B,20,kept\nshared between families,3,global\nnot attributed to any family,4,global\n";
        foreach (var (id, file, count, singular) in new[]
        {
            ("composition-assigned", "family_composition_credited.csv", 2, false),
            ("composition-measured", "family_composition_measured.csv", 2, false),
            ("protein-count-support", "family_protein_counts.csv", 2, false),
            ("synthetic-long-detail", "", 21, false),
            ("signal-summary", "signal_by_run.csv", 1, true),
        })
        {
            var rows = Enumerable.Range(1, count).Select(index => (IReadOnlyList<string>)(singular
                ? new[] { "42" } : new[] { "Group " + index.ToString(CultureInfo.InvariantCulture), index.ToString(CultureInfo.InvariantCulture) })).ToArray();
            var view = new ReportTable(id, "Synthetic projected detail", singular ? new[] { "Value" } : new[] { "Name", "Displayed value" }, rows, "Read each stored measure with its native units.");
            var displayed = new ReportTable("displayed-grid", "Displayed Word grid", new[] { "Name", "Value" },
                new IReadOnlyList<string>[] { new[] { "A", "1" }, new[] { "B", "2" } }, "Read across the displayed grid.");
            var source = new ReportDocument { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { view, displayed }) } };
            var captioned = ReportCaptionBuilder.WithCaptions(source, new Dictionary<string, FigureCaptionInput>(),
                new Dictionary<string, TableCaptionInput>
                {
                    [view.Id] = new(view.Title, view.Caption, SummaryColumns: Array.Empty<int>()),
                    [displayed.Id] = new(displayed.Title, displayed.Caption, SummaryColumns: Array.Empty<int>()),
                });
            string originalCaption = captioned.Sections.Single().Blocks.OfType<ReportTable>().Single(t => t.Id == id).Caption;
            var exports = file.Length == 0 ? new Dictionary<string, string>() : new Dictionary<string, string> { [file] = csv };
            var layout = ReportSupplementLayout.Apply(captioned, exports);
            var reference = layout.Document.Sections.Single().Blocks.OfType<ReportParagraph>().Single(p => p.Id == id);
            var sheet = layout.Sheets.Single(s => s.Id == id);
            Check(id + " workbook reference retains reading guidance and citation without claiming the projected grid size",
                reference.Text.Contains("sheet '" + sheet.Name + "'", StringComparison.Ordinal)
                && reference.Text.Contains("Read each stored measure", StringComparison.Ordinal)
                && !reference.Text.Contains("The table shows", StringComparison.Ordinal));
            var expected = file.Length == 0 ? view : ReportCsvTable.Build(id, view.Title, csv, originalCaption);
            Check(id + " native worksheet cells and original stored caption remain unchanged",
                sheet.Table.Columns.SequenceEqual(expected.Columns)
                && sheet.Table.Rows.Count == expected.Rows.Count
                && sheet.Table.Rows.Zip(expected.Rows).All(pair => pair.First.SequenceEqual(pair.Second))
                && sheet.Table.Caption == originalCaption);
            var grid = layout.Document.Sections.Single().Blocks.OfType<ReportTable>().Single(t => t.Id == displayed.Id);
            Check(id + " displayed Word table retains its own exact dimensions and cells",
                grid.Caption.Contains("The table shows 2 data rows across 2 columns.", StringComparison.Ordinal)
                && grid.Rows.Zip(displayed.Rows).All(pair => pair.First.SequenceEqual(pair.Second)));
        }
    }
}
