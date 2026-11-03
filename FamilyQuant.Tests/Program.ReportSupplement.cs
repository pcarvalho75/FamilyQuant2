using FamilyQuant.Report;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ReportSupplementChecks(string? output)
    {
        Section("Report supplement - compact Word, native workbook and shared selections");
        var source = new ReportDocument { Title = "Synthetic supplement fixture", Sections = new[]
        {
            new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[]
            {
                new ReportTable("composition-measured", "Full current grouping", new[] { "Family", "Share" },
                    new[] { (IReadOnlyList<string>)new[] { "Synthetic α", "0.125" } }, "All runs pooled."),
                new ReportTable("family-run-means", "Headline", new[] { "Family", "Mean (%)" },
                    new[] { (IReadOnlyList<string>)new[] { "Synthetic α", "12.5" } }, "Mean of per-run shares."),
            }),
            new ReportSection(ReportSectionIds.FamilyPrefix + "key", "Synthetic α", new ReportBlock[]
            {
                new ReportTable("panel-block:key:text", "User panel text", new[] { "Field", "Text" },
                    new[] { (IReadOnlyList<string>)new[] { "Original notes", new string('x', 800) } }, "User data."),
                new ReportParagraph("panel-block:key:observation-status", "No observation was stored."),
                new ReportFigure("figure:family-map-fixture", "Synthetic family map",
                    Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6MpIAAAAASUVORK5CYII="),
                    "Synthetic map caption.", 1, 1),
            }),
            new ReportSection(ReportSectionIds.Supplements, "Supplements", Array.Empty<ReportBlock>()),
        } };
        var csv = new Dictionary<string, string> { ["peptide_representatives.csv"] = "sequence,area\nSYNTHETIC,123.5\n" };
        var layout = ReportSupplementLayout.Apply(source, csv);
        Check("full current grouping leaves Word and gets an exact named-sheet citation", layout.Document.Sections[0].Blocks.Single(b => b.Id == "composition-measured") is ReportParagraph p
            && p.Text.Contains(layout.Sheets.Single(s => s.Id == "composition-measured").Name, StringComparison.Ordinal));
        Check("headline composition stays in Word", layout.Document.Sections[0].Blocks.Single(b => b.Id == "family-run-means") is ReportTable);
        Check("complete panel text is retained in its family-owned workbook sheet", layout.Sheets.Single(s => s.OwnerSectionId == "family:key").Table.Rows.Any(r => r.Contains(new string('x', 800))));
        Check("family brief marks excerpts and cites full panel text", layout.Document.Sections[1].Blocks.OfType<ReportParagraph>().Any(p => p.Text.StartsWith("Panel text excerpt:", StringComparison.Ordinal))
            && layout.Document.Sections[1].Blocks.OfType<ReportParagraph>().Any(p => p.Text.Contains("Full panel text", StringComparison.Ordinal)));
        Check("compact family section preserves its native coverage figure and caption", layout.Document.Sections[1].Blocks.OfType<ReportFigure>().Single().Id == "figure:family-map-fixture"
            && layout.Document.Sections[1].Blocks.OfType<ReportFigure>().Single().Caption == "Synthetic map caption.");
        Check("workbook sheet names are deterministic, unique and valid length", layout.Sheets.All(s => s.Name.Length <= 31)
            && layout.Sheets.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == layout.Sheets.Count
            && layout.Sheets.Select(s => s.Name).SequenceEqual(ReportSupplementLayout.Apply(source, csv).Sheets.Select(s => s.Name)));
        var writer = typeof(ReportDocument).Assembly.GetType("FamilyQuant.Report.ReportSupplementWorkbook");
        Check("the native supplement writer is available", writer != null);
        if (writer == null) return;
        byte[] Bytes(ReportSelection? selection = null) => ReportSupplementWorkbook.BuildBytes(layout.Document, layout.Sheets, selection);
        var old = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        byte[] workbook;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            byte[] pt = Bytes();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            workbook = Bytes();
            Check("entire workbook bytes equal under pt-BR and en-US", pt.SequenceEqual(workbook));
            Check("repeat workbook export bytes equal", workbook.SequenceEqual(Bytes()));
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
        using (var package = SpreadsheetDocument.Open(new MemoryStream(workbook), false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(package).ToList();
            Check("supplement passes Open XML schema validation", errors.Count == 0, string.Join("; ", errors.Select(e => e.Description)));
            var main = package.WorkbookPart ?? throw new InvalidDataException("Workbook part is missing.");
            var book = main.Workbook ?? throw new InvalidDataException("Workbook root is missing.");
            var sheets = (book.Sheets ?? throw new InvalidDataException("Workbook sheets are missing.")).Elements<S.Sheet>().ToList();
            var tableIds = layout.Sheets.Select(s => s.Id).Concat(layout.Document.Sections.SelectMany(s => s.Blocks.OfType<ReportTable>()).Select(t => t.Id));
            Check("one worksheet per supplied or printed table plus its index", sheets.Count == tableIds.Distinct(StringComparer.Ordinal).Count() + 1 && sheets.Any(s => s.Name == "Contents"));
            var cells = main.WorksheetParts.SelectMany(part => (part.Worksheet ?? throw new InvalidDataException("Worksheet root is missing.")).Descendants<S.Cell>()).ToList();
            string Value(S.Cell cell) => cell.InlineString?.InnerText ?? cell.CellValue?.Text ?? string.Empty;
            Check("stored numeric lexemes are unchanged", cells.Any(c => Value(c) == "0.125") && cells.Any(c => Value(c) == "123.5"));
            Check("snapshot workbook has no formulas or error cells", !main.WorksheetParts.Any(part => (part.Worksheet ?? throw new InvalidDataException("Worksheet root is missing.")).Descendants<S.CellFormula>().Any())
                && cells.All(c => c.DataType?.Value != S.CellValues.Error));
        }
        using (var zip = new ZipArchive(new MemoryStream(workbook)))
            Check("supplement ZIP timestamps are fixed", zip.Entries.All(e => e.LastWriteTime.Year == 2000));
        var excluded = new ReportSelection { ExcludedIds = new[] { ReportSectionIds.Results, "family:key" } };
        using (var package = SpreadsheetDocument.Open(new MemoryStream(Bytes(excluded)), false))
        {
            var main = package.WorkbookPart ?? throw new InvalidDataException("Workbook part is missing.");
            var book = main.Workbook ?? throw new InvalidDataException("Workbook root is missing.");
            var names = (book.Sheets ?? throw new InvalidDataException("Workbook sheets are missing.")).Elements<S.Sheet>().Select(s => s.Name!.Value!).ToList();
            Check("excluded sections remove exactly their workbook detail", names.Count == 2 && names.Contains("Contents")
                && names.Contains(layout.Sheets.Single(s => s.Id == "export:peptide_representatives.csv").Name));
        }
        ReportThresholdDocumentChecks();
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "synthetic_supplement.xlsx"), workbook);
            WordReportWriter.Write(Path.Combine(output, "synthetic-layout.docx"), layout.Document);
        }
    }

    private static void ReportThresholdDocumentChecks()
    {
        var shares = new FamilyRunSharesResult { Runs = new() { new() { Run = "r", AssignedSignal = 100, MeasuredSignal = 200 } }, Families = new()
        {
            new() { FamilyUserId = 1, MeanShare = .0015, AvailableRunCount = 1, Runs = new() { new() { Run = "r", Signal = .15, ShareOfAssignedSignal = .0015, ShareOfMeasuredSignal = .00075 } } },
            new() { FamilyUserId = 2, MeanShare = .9985, AvailableRunCount = 1, Runs = new() { new() { Run = "r", Signal = 99.85, ShareOfAssignedSignal = .9985, ShareOfMeasuredSignal = .49925 } } },
        } };
        var project = new FamilyClusterFile(); var labels = new Dictionary<int, string> { [1] = "Small source family", [2] = "Large source family" };
        var manifest = new QuantManifest { FamilyRunShares = shares };
        var basic = ReportCurrentResults.Build(new AnalyticsData(), manifest, null);
        var low = ReportStoredResults.Apply(basic, manifest, labels, FamilyReportThreshold.Build(shares, project));
        project.ReportThreshold = .002;
        var high = ReportStoredResults.Apply(basic, manifest, labels, FamilyReportThreshold.Build(shares, project));
        ReportTable Head(ReportResultPacket value) => value.Sections.SelectMany(s => s.Blocks).OfType<ReportTable>().Single(t => t.Id == "family-run-means");
        Check("stored 0.2 percent threshold regroups the headline through J5c", Head(low).Rows.Any(r => r[0] == labels[1])
            && !Head(high).Rows.Any(r => r[0] == labels[1]) && Head(high).Rows.Any(r => r[0] == "Others"));
        var members = ReportCsvTable.Build("members", "Members", high.CsvExports["family_others_members.csv"], "");
        Check("regrouped member and canonical reason survive in workbook source", members.Rows.Single()[0] == "1" && members.Rows.Single()[2].Contains("Threshold", StringComparison.Ordinal));
        Check("obsolete threshold-not-computed scaffold is removed", !high.Sections.SelectMany(s => s.Blocks).Any(b => b.Id == "others-unavailable"));
    }
}
