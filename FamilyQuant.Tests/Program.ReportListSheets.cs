using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportListSheetCommands() =>
    [
        new("--test-report-list-sheets", 443, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-report-list-sheets", (args, _) =>
            {
                ReportListSheetTests(args.Length > 1 ? args[1] : null);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void ReportListSheetTests(string? output)
    {
        Section("Long-format report lists retain every item without oversized cells");
        var items = Enumerable.Range(0, 1000).Select(i => "PEPTIDE" + i.ToString("D5", CultureInfo.InvariantCulture) + new string('A', 30)).ToArray();
        string Row(params string[] cells) => string.Join(",", cells.Select(s => "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")) + "\n";
        string csv = Row("family_user_id", "family", "selected_protein_identifiers", "unexplained_sequences")
            + Row("11", "Fixture group", "P1; P2", string.Join("; ", items));
        string before = csv;
        var table = ReportCsvTable.Build("list-fixture", "List fixture", csv, "Native stored lists.");
        var document = new ReportDocument { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { table }) } };
        var sheets = new[] { new ReportSupplementSheet(table.Id, ReportSectionIds.Results, "Support", table) };
        Check("fixture exceeds the Excel per-cell limit", string.Join("; ", items).Length > 32767);
        byte[]? workbook = null;
        try { workbook = ReportSupplementWorkbook.BuildBytes(document, sheets); }
        catch (InvalidDataException) { }
        Check("a list longer than the Excel limit writes without truncation or refusal", workbook != null);
        if (workbook != null)
        {
            var book = ReadSupplement(workbook);
            var longSheets = book.Where(p => p.Value.Length > 0 && p.Value[0].Contains("Item")).ToArray();
            var retained = longSheets.SelectMany(p => p.Value.Skip(1).Select(r => r.Last())).ToArray();
            Check("every original list item has its own long-format row", items.All(retained.Contains) && retained.Count(v => items.Contains(v)) == items.Length);
            Check("the original cell contains a count and a list-sheet reference", !book["Support"].SelectMany(r => r).Any(v => v.Contains(items[0], StringComparison.Ordinal))
                && book["Support"].SelectMany(r => r).Any(v => v.Contains("1000", StringComparison.Ordinal) && longSheets.Any(s => v.Contains(s.Key, StringComparison.Ordinal))));
            if (output != null) { Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output, "long-lists.xlsx"), workbook); }
        }
        using var word = new ZipArchive(new MemoryStream(WordReportWriter.BuildBytes(document)));
        using var xml = word.GetEntry("word/document.xml")!.Open();
        string text = XDocument.Load(xml).Root!.Value;
        Check("Word prints list counts instead of the long joined list", !text.Contains(items[0], StringComparison.Ordinal) && text.Contains("1000", StringComparison.Ordinal));
        Check("native CSV bytes remain literal", csv == before);
        var structured = new ReportTable("structured", "Structured items", new[] { "family", "members", "note" },
            new[] { (IReadOnlyList<string>)new[] { "Owner", "P1; P2; ", "prose; remains scalar" } }, "")
        { ListColumns = new[] { new ReportListColumn(1, "members", new[] { (IReadOnlyList<string>)new[] { "P1; P2", "" } }) } };
        var structuredDoc = document with { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { structured }) } };
        var structuredSheets = new[] { new ReportSupplementSheet(structured.Id, ReportSectionIds.Results, "Structured", structured) };
        var exact = ReadSupplement(ReportSupplementWorkbook.BuildBytes(structuredDoc, structuredSheets));
        var exactList = exact.Single(p => p.Value.Length > 0 && p.Value[0].Contains("Item")).Value;
        Check("typed lists preserve delimiter text and empty items as distinct source items", exactList.Skip(1).Select(r => r.Last()).SequenceEqual(new[] { "P1; P2", "" })
            && exactList.Skip(1).All(r => r[4] == "Owner"));
        Check("scalar prose containing list punctuation remains byte-identical", exact["Structured"][1][2] == "prose; remains scalar");
        var excluded = ReadSupplement(ReportSupplementWorkbook.BuildBytes(structuredDoc, structuredSheets,
            new ReportSelection { ExcludedIds = new[] { structured.Id } }));
        Check("excluding a parent table excludes its item sheets and contents entries", excluded.Count == 1 && excluded["Contents"].Length == 1);
        string filteredCsv = Row("family_user_id", "family", "unexplained_sequences") + Row("11", "First", "AAA; BBB") + Row("22", "Hidden", "CCC");
        var allRows = ReportCsvTable.Build("filtered", "Filtered", filteredCsv, "");
        var partition = new FamilyReportThresholdResult(); partition.OtherFamilies.Add(new() { FamilyUserId = 22 });
        var filtered = ReportWordMembership.Filter(allRows, filteredCsv, partition);
        var filteredDoc = document with { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { filtered }) } };
        var filteredBook = ReadSupplement(ReportSupplementWorkbook.BuildBytes(filteredDoc, new[] { new ReportSupplementSheet(filtered.Id, ReportSectionIds.Results, "Filtered", filtered) }));
        var filteredItems = filteredBook.Single(p => p.Value.Length > 0 && p.Value[0].Contains("Item")).Value.Skip(1).ToArray();
        Check("J26 row projections keep list items aligned with the original family and row key", filteredItems.Select(r => r.Last()).SequenceEqual(new[] { "AAA", "BBB" })
            && filteredItems.All(r => r[2] == "1" && r[3] == "11"));
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); var pt = ReportSupplementWorkbook.BuildBytes(document, sheets);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("list workbook bytes are culture invariant", pt.SequenceEqual(ReportSupplementWorkbook.BuildBytes(document, sheets)));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }
}
