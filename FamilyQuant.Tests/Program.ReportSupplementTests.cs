using FamilyQuant.Report;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportSupplementCommandTable() =>
    [
        new("--test-report-supplement", 432, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-report-supplement", (args, _) => ReportSupplementTests(args)),
    ];

    private static (ReportDocument Document, ReportSupplementSheet[] Sheets) SupplementFixture()
    {
        var data = AnalyticsBuilder.Build(SyntheticAnalyticsRows(), new Dictionary<string, double> { ["ui_01"] = 5e8, ["ui_02"] = 4e8 });
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "sheet_target", Sequence = "PEPTIDEK", Description = "Sheet fixture", UserID = 1, USearchID = 1 }
        } };
        var csvs = new[]
        {
            ("proteins", "results", "Per protein", PanelAuditCsv.BuildCsv(file)),
            ("peptides", "results", "Per peptide", AnalyticsCsv.BuildPeptideCsv(data.Peptides)),
            ("runs", "results", "Per run", AnalyticsCsv.BuildSummaryCsv(data.Fractions, data.Grouped)),
            ("others", "supplements", "Others with reasons", "family,mean_share,threshold,reason\r\nFixture family,0.0004,0.001,Below the stored threshold\r\n"),
            ("panel-audit", "supplements", "Panel audit", PanelAuditCsv.BuildCsv(file)),
            ("literal", "supplements", "Literal cells", "identifier,numeric_text,formula_text,note,escape_text,blank\r\n00123,1.000,\"=SUM(1,2)\",\"a, b\"\"c\r\nline\",_x0041_,\r\n_x005F_x0041_,0.000,+123,'quoted,_x000D_,#N/A\r\n")
        };
        var sheets = csvs.Select(value => new ReportSupplementSheet(value.Item1, value.Item2, value.Item3,
            ReportCsvTable.Build(value.Item1, value.Item3, value.Item4, "Native CSV cells"))).ToArray();
        return (new ReportDocument { Sections = new[]
        {
            new ReportSection("results", "Results", Array.Empty<ReportBlock>()),
            new ReportSection("supplements", "Supplements", Array.Empty<ReportBlock>())
        } }, sheets);
    }

    private static string SpreadsheetText(string value) => Regex.Replace(value, "_x([0-9a-fA-F]{4})_",
        match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());

    private static Dictionary<string, string[][]> ReadSupplement(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var package = SpreadsheetDocument.Open(stream, false);
        var workbook = package.WorkbookPart!;
        return workbook.Workbook!.Sheets!.Elements<Sheet>().ToDictionary(sheet => sheet.Name!.Value!, sheet =>
        {
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
            return part.Worksheet!.GetFirstChild<SheetData>()!.Elements<Row>().Select(row => row.Elements<Cell>()
                .Select(cell => SpreadsheetText(cell.InlineString?.InnerText ?? cell.CellValue?.Text ?? "")).ToArray()).ToArray();
        }, StringComparer.Ordinal);
    }

    private static int ReportSupplementTests(string[] args)
    {
        if (args.Length > 2) throw new ArgumentException("Use --test-report-supplement [fresh-output].");
        var type = typeof(ReportDocument).Assembly.GetType("FamilyQuant.Report.ReportSupplementWorkbook");
        var method = type?.GetMethod("BuildBytes", new[] { typeof(ReportDocument), typeof(IReadOnlyList<ReportSupplementSheet>), typeof(ReportSelection) });
        Check("supplement writer exposes the agreed document/sheets/selection entry point", method != null);
        if (method == null) return 1;
        string output = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.Supplement.Tests", Guid.NewGuid().ToString("N"));
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a fresh supplement output directory.");
        Directory.CreateDirectory(output);
        var fixture = SupplementFixture();
        byte[] Build(IReadOnlyList<ReportSupplementSheet> sheets, ReportSelection? selection = null) =>
            (byte[])method.Invoke(null, new object?[] { fixture.Document, sheets, selection })!;
        byte[]? first = null;
        var culture = CultureInfo.CurrentCulture; var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string name in new[] { "pt-BR", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
                byte[] bytes = Build(fixture.Sheets);
                File.WriteAllBytes(Path.Combine(output, "synthetic_supplement-" + name + ".xlsx"), bytes);
                Check("supplement bytes do not depend on " + name, first == null || first.SequenceEqual(bytes));
                first ??= bytes;
                var contents = ReadSupplement(bytes);
                Check("one complete data sheet per supplied table plus Contents under " + name, contents.Count == fixture.Sheets.Length + 1 && contents.ContainsKey("Contents"));
                foreach (var sheet in fixture.Sheets)
                {
                    var expected = new[] { sheet.Table.Columns.ToArray() }.Concat(sheet.Table.Rows.Select(row => row.ToArray())).ToArray();
                    Check(sheet.Name + " cells equal their program CSV under " + name, contents[sheet.Name].Length == expected.Length
                        && contents[sheet.Name].Zip(expected).All(pair => pair.First.SequenceEqual(pair.Second)));
                }
            }
        }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
        using (var stream = new MemoryStream(first!))
        using (var package = SpreadsheetDocument.Open(stream, false))
        {
            var errors = new OpenXmlValidator().Validate(package).ToArray();
            Check("supplement workbook passes Open XML schema validation", errors.Length == 0,
                string.Join("; ", errors.Select(error => error.Path?.XPath + ": " + error.Description)));
            Check("source text is never interpreted as an Excel formula", package.WorkbookPart!.WorksheetParts.SelectMany(part => part.Worksheet!.Descendants<CellFormula>()).Count() == 0);
            Check("each data sheet has a frozen header and filters", package.WorkbookPart!.WorksheetParts.All(part => part.Worksheet!.Descendants<Pane>().Any(pane => pane.State?.Value == PaneStateValues.Frozen)
                && part.Worksheet!.GetFirstChild<AutoFilter>() != null));
        }
        var selected = ReadSupplement(Build(fixture.Sheets, new() { ExcludedIds = new[] { "results", "panel-audit" } }));
        Check("section and table exclusions govern which sheets exist", selected.Keys.ToHashSet(StringComparer.Ordinal)
            .SetEquals(new[] { "Contents", "Others with reasons", "Literal cells" }));
        Check("Contents cites only included data sheets", selected["Contents"].Skip(1).Select(row => row[0]).ToHashSet(StringComparer.Ordinal)
            .SetEquals(selected.Keys.Where(name => name != "Contents")));
        var none = ReadSupplement(Build(fixture.Sheets, new() { ExcludedIds = new[] { "results", "supplements" } }));
        Check("excluding every detail leaves an empty index and no fabricated result sheet", none.Count == 1 && none["Contents"].Length == 1);
        bool Refused(ReportSupplementSheet[] sheets)
        {
            try { Build(sheets); return false; }
            catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException or InvalidDataException) { return true; }
        }
        Check("illegal Excel names are refused without changing citation names", Refused(new[] { fixture.Sheets[0] with { Name = "bad/name" } }));
        Check("case-insensitive worksheet collisions are refused", Refused(new[] { fixture.Sheets[0], fixture.Sheets[1] with { Name = fixture.Sheets[0].Name.ToUpperInvariant() } }));
        Check("Contents and History names are reserved", Refused(new[] { fixture.Sheets[0] with { Name = "Contents" } }) && Refused(new[] { fixture.Sheets[0] with { Name = "History" } }));
        Check("unknown owning sections are refused", Refused(new[] { fixture.Sheets[0] with { OwnerSectionId = "absent" } }));
        var malformed = fixture.Sheets[0] with { Table = new("bad", "Bad", new[] { "a", "b" }, new[] { (IReadOnlyList<string>)new[] { "only one" } }, "") };
        Check("inconsistent row widths are refused", Refused(new[] { malformed }));
        ReportSupplementSheet Cell(string value) => fixture.Sheets[0] with
        { Table = new("cell", "Cell", new[] { "value" }, new[] { (IReadOnlyList<string>)new[] { value } }, "") };
        Check("Excel text limits refuse truncation", Refused(new[] { Cell(new string('x', 32768)) }));
        Check("invalid XML control text is refused", Refused(new[] { Cell("a\u0001b") }));
        var tooWide = fixture.Sheets[0] with { Table = new("wide", "Wide", Enumerable.Repeat("column", 16385).ToArray(), Array.Empty<IReadOnlyList<string>>(), "") };
        Check("Excel column limits refuse dropped columns", Refused(new[] { tooWide }));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        Console.WriteLine("Synthetic supplement workbook and CSV-parity fixture: " + output);
        return failed == 0 ? 0 : 1;
    }
}
