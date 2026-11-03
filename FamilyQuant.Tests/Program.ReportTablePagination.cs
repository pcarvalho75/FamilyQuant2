using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportTablePaginationCommands() =>
    [new("--test-report-table-pagination", 442, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-report-table-pagination", (args, _) =>
        {
            ReportTablePaginationTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "report-table-pagination"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReportTablePaginationTests(string output)
    {
        Directory.CreateDirectory(output);
        string caption = "Pagination caption anchor. " + string.Join(" ", Enumerable.Repeat(
            "Exact source values remain unchanged while the table spans pages. The caption belongs with the table and the column names repeat.", 6))
            + "\nCaption continuation anchor. The final stored row remains present.";
        var rows = Enumerable.Range(1, 28).Select(index => (IReadOnlyList<string>)new[]
        {
            "Synthetic recorded family with a deliberately long descriptive label " + index.ToString(CultureInfo.InvariantCulture),
            "Position not resolved", "26151419061.633663", "26151419061.633663", "100",
        }).ToArray();
        var table = new ReportTable("spanning-table", "Spanning table", new[]
            { "Family", "Part", "Pooled family signal", "Pooled part signal", "Within-family share (%)" }, rows, caption);
        var document = new ReportDocument { Sections = new[] { new ReportSection("pagination", "Pagination", new ReportBlock[]
            { table, new ReportParagraph("following", "Following content anchor.") }) } };
        CultureInfo original = CultureInfo.CurrentCulture;
        byte[]? previous = null;
        try
        {
            foreach (string language in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                byte[] bytes = WordReportWriter.BuildBytes(document);
                File.WriteAllBytes(Path.Combine(output, language + ".docx"), bytes);
                using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
                Check("J34f spanning fixture satisfies the Word schema " + language, !new OpenXmlValidator().Validate(package).Any());
                XDocument xml = XDocument.Parse(package.MainDocumentPart!.Document!.OuterXml);
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var printed = xml.Descendants(w + "tbl").Single();
                var printedRows = printed.Elements(w + "tr").ToArray();
                Check("J34f contiguous first header is explicitly repeatable " + language,
                    printedRows[0].Element(w + "trPr")?.Element(w + "tblHeader") != null
                    && printedRows.Skip(1).All(row => row.Element(w + "trPr")?.Element(w + "tblHeader") == null));
                Check("J34f every original row stays together without manual page breaks " + language,
                    printedRows.All(row => row.Element(w + "trPr")?.Element(w + "cantSplit") != null)
                    && !printed.Descendants(w + "br").Any());
                var captionLines = caption.Split('\n');
                var captions = xml.Descendants(w + "p").Where(paragraph => captionLines.Contains(
                    string.Concat(paragraph.Descendants(w + "t").Select(text => text.Value)), StringComparer.Ordinal)).ToArray();
                Check("J34f caption chain precedes its table and cannot start a page alone " + language,
                    captions.Length == captionLines.Length && captions.All(paragraph => paragraph.IsBefore(printed)
                        && paragraph.Element(w + "pPr")?.Element(w + "keepNext") != null));
                Check("J34f caption remains adjacent to its table " + language,
                    captions.Length > 0 && ReferenceEquals(captions[^1].NextNode, printed));
                Check("J34f all original cell strings and row order stay exact " + language,
                    printedRows.Skip(1).Select(row => row.Elements(w + "tc").Select(cell =>
                        string.Concat(cell.Descendants(w + "t").Select(text => text.Value))).ToArray())
                    .Zip(rows).All(pair => pair.First.SequenceEqual(pair.Second)) && printedRows.Length == rows.Length + 1);
                Check("J34f caption and following text remain exact " + language,
                    captions.Select(paragraph => string.Concat(paragraph.Descendants(w + "t").Select(text => text.Value))).SequenceEqual(captionLines)
                    && xml.Descendants(w + "t").Count(text => text.Value == "Following content anchor.") == 1);
                if (previous != null) Check("J34f spanning fixture repeats across cultures", previous.SequenceEqual(bytes));
                previous = bytes;
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
