using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportNumberTraceTestsCommandTable() =>
    [new("--test-report-number-trace", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-report-number-trace", (_, _) =>
        {
            ReportNumberTraceTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReportNumberTraceTests()
    {
        Section("J18 report number trace");
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.ReportNumberTrace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string data = Path.Combine(root, "fixture_data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "source.csv"), "name,value,other\r\n\"quoted, row\",\"1,234.50\",12.5\r\nsecond,0.125,42\r\n");
        string document = Path.Combine(root, "fixture.docx");
        string output = Path.Combine(root, "trace.csv");
        var report = new ReportDocument
        {
            Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[]
            {
                new ReportParagraph("body", "Observed 1,234.50; share 12.5%; fraction 0.125. Year 2024; run 3; page 8; SHA-256: 1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef."),
                new ReportTable("table", "Values", new[] { "Name", "Value" },
                    new[] { (IReadOnlyList<string>)new[] { "Example", "42" } }, "Caption value 12.5%."),
            }) },
        };
        WordReportWriter.Write(document, report);
        bool registered = TestCommandRegistry.Resolve(new[] { "--trace-report-numbers", document, data, output }, TestCommandPhase.Raw) != null;
        Check("J18 registers a standalone trace command", registered);
        if (!registered) return;
        int Run() { TestCommandRegistry.TryRun(new[] { "--trace-report-numbers", document, data, output }, TestCommandPhase.Raw, root, out int exit); return exit; }
        int first = Run();
        var trace = ReportCsvTable.Build("trace", "trace", File.ReadAllText(output), "");
        int value = trace.Columns.ToList().IndexOf("number"), status = trace.Columns.ToList().IndexOf("status");
        int location = trace.Columns.ToList().IndexOf("location"), csv = trace.Columns.ToList().IndexOf("csv"), cell = trace.Columns.ToList().IndexOf("cell");
        Check("J18 trace has documented number, status, location and CSV cell columns", new[] { value, status, location, csv, cell }.All(i => i >= 0));
        if (new[] { value, status, location, csv, cell }.Any(i => i < 0)) return;
        bool Has(string number, string expected) => trace.Rows.Any(r => r[value] == number && r[status] == expected);
        Check("J18 matching fixture succeeds without unexplained values", first == 0 && trace.Rows.All(r => r[status] != "NOT FOUND"));
        Check("J18 matches quoted thousands, percentages and decimal values", Has("1,234.50", "MATCH") && Has("12.5%", "MATCH") && Has("0.125", "MATCH"));
        Check("J18 names the CSV and one-based cell", trace.Rows.Any(r => r[value] == "1,234.50" && r[csv] == "source.csv" && r[cell] == "B2"));
        Check("J18 reports table and caption locations", trace.Rows.Any(r => r[value] == "42" && r[location].Contains("table", StringComparison.OrdinalIgnoreCase))
            && trace.Rows.Any(r => r[value] == "12.5%" && r[location].Contains("caption", StringComparison.OrdinalIgnoreCase)));
        Check("J18 classifies year, page, run index and hash independently", Has("2024", "YEAR") && Has("3", "RUN INDEX")
            && Has("8", "PAGE") && trace.Rows.Any(r => r[status] == "SHA-256"));

        File.WriteAllText(Path.Combine(data, "nested.csv"), "text,amount_fraction,number\r\n\"line one\nline two\",0.075,6.25e2\r\nother,11,22\r\nother,5,0\r\nother,1,\r\n");
        var edge = report with { Sections = new[] { report.Sections[0] with { Blocks = new ReportBlock[]
        {
            new ReportParagraph("edge", "Split 1,234.50; scaled 7.5%; scientific 625; sample 19; label 123abc; date 2024-11-02; rounded-to-5-decimals."),
            new ReportParagraph("tabbed", "Tab marker"),
        } } } };
        WordReportWriter.Write(document, edge);
        using (var word = WordprocessingDocument.Open(document, true))
        {
            var paragraph = word.MainDocumentPart!.Document!.Body!.Descendants<W.Paragraph>()
                .Single(p => p.InnerText.StartsWith("Split ", StringComparison.Ordinal));
            var text = paragraph.Descendants<W.Text>().Single();
            string remainder = text.Text.Substring("Split 1,".Length);
            text.Text = "Split 1,";
            text.Parent!.InsertAfterSelf(new W.Run(new W.Text(remainder) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
            var tabbed = word.MainDocumentPart.Document.Body.Descendants<W.Paragraph>().Single(p => p.InnerText == "Tab marker");
            tabbed.RemoveAllChildren<W.Run>();
            tabbed.Append(new W.Run(new W.Text("11"), new W.TabChar(), new W.Text("22")));
        }
        int edgeExit = Run();
        trace = ReportCsvTable.Build("edge", "edge", File.ReadAllText(output), "");
        Check("J18 joins a number split across Word runs", edgeExit == 0 && Has("1,234.50", "MATCH"));
        Check("J18 matches a percentage against an explicitly named fraction column", Has("7.5%", "MATCH")
            && trace.Rows.Any(r => r[value] == "7.5%" && r[csv] == "nested.csv" && r[cell] == "B2"));
        Check("J18 handles exponents and quoted multiline CSV cells", Has("625", "MATCH")
            && trace.Rows.Any(r => r[value] == "625" && r[cell] == "C2"));
        Check("J18 keeps numbers separated by Word tabs distinct", Has("11", "MATCH") && Has("22", "MATCH") && !Has("1122", "NOT FOUND"));
        Check("J18 classifies an alphanumeric identifier and an ISO date without dropping their digits",
            Has("123abc", "IDENTIFIER") && trace.Rows.Any(r => r[value] == "2024-11-02" && r[status] == "DATE"));
        Check("J18 retains a number embedded between hyphenated words", Has("5", "MATCH"));
        string sourceFile = Path.Combine(data, "source.csv");
        string original = File.ReadAllText(sourceFile);
        bool refused = false;
        try { ReportNumberTrace.Run(document, data, sourceFile); }
        catch (InvalidOperationException) { refused = true; }
        Check("J18 refuses to overwrite a source CSV", refused && File.ReadAllText(sourceFile) == original);

        WordReportWriter.Write(document, report with { Sections = new[] { report.Sections[0] with { Blocks = new ReportBlock[]
            { new ReportParagraph("fraction-mismatch", "Incorrect fraction 1%.") } } } });
        int fractionExit = Run();
        trace = ReportCsvTable.Build("fraction", "fraction", File.ReadAllText(output), "");
        Check("J18 does not mistake a fraction of one for one percent", fractionExit != 0 && Has("1%", "NOT FOUND"));

        WordReportWriter.Write(document, report with { Sections = new[] { report.Sections[0] with { Blocks = report.Sections[0].Blocks.Concat(new ReportBlock[] { new ReportParagraph("planted", "Planted 98765.4321 and 1234.5001.") }).ToArray() } } });
        int plantedExit = Run();
        trace = ReportCsvTable.Build("planted", "planted", File.ReadAllText(output), "");
        Check("J18 reports a planted number and returns failure", plantedExit != 0 && Has("98765.4321", "NOT FOUND"));
        Check("J18 does not treat a nearby value as equal", Has("1234.5001", "NOT FOUND"));
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            int cultureExit = Run();
            var localized = ReportCsvTable.Build("culture", "culture", File.ReadAllText(output), "");
            Check("J18 matches invariant values regardless of current culture", cultureExit == plantedExit && localized.Rows.SequenceEqual(trace.Rows, RowComparer.Instance));
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }

        string workbookDoc = Path.Combine(root, "workbook.docx");
        string missingFolder = Path.Combine(root, "workbook_data");
        string workbookOutput = Path.Combine(root, "workbook-trace.csv");
        var table = new ReportTable("book-table", "Workbook values", new[] { "Name", "Value" },
            new IReadOnlyList<string>[] { new[] { "First", "75.25" }, new[] { "Second", "3" } },
            "The table shows 2 data rows across 2 columns.");
        var bookReport = new ReportDocument { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { table }) } };
        WordReportWriter.Write(workbookDoc, bookReport);
        ReportSupplementWorkbook.Write(ReportSupplementWorkbook.PathForReport(workbookDoc), bookReport,
            new[] { new ReportSupplementSheet("book-sheet", ReportSectionIds.Results, "Values", table) });
        bool workbookRan = false;
        int workbookExit = -1;
        try { workbookExit = ReportNumberTrace.Run(workbookDoc, missingFolder, workbookOutput); workbookRan = true; }
        catch (DirectoryNotFoundException) { }
        Check("J18 reads the companion workbook without a sidecar folder", workbookRan && workbookExit == 0);
        if (!workbookRan) return;
        trace = ReportCsvTable.Build("book", "book", File.ReadAllText(workbookOutput), "");
        Check("J18 identifies the workbook sheet and cell", trace.Rows.Any(r => r[value] == "75.25" && r[status] == "MATCH"
            && r[csv].Contains("workbook_supplement.xlsx!Values", StringComparison.Ordinal) && r[cell] == "B2"));
        Check("J18 validates caption dimensions against their own Word table", Has("2", "TABLE ROWS") && Has("2", "TABLE COLUMNS"));
        var incorrect = table with { Caption = "The table shows 3 data rows across 2 columns." };
        WordReportWriter.Write(workbookDoc, bookReport with { Sections = new[] { bookReport.Sections[0] with { Blocks = new ReportBlock[] { incorrect } } } });
        int badSize = ReportNumberTrace.Run(workbookDoc, missingFolder, workbookOutput);
        trace = ReportCsvTable.Build("bad-size", "bad-size", File.ReadAllText(workbookOutput), "");
        Check("J18 refuses a wrong table size even when that number appears in a workbook cell", badSize != 0 && Has("3", "NOT FOUND"));

        string? savedVersion = Environment.GetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, "2.1.0.0");
            var versionReport = report with { ApplicationVersion = "2.1.0.0", Sections = new[]
            {
                new ReportSection(ReportSectionIds.Title, "Title", Array.Empty<ReportBlock>()),
                report.Sections[0] with { Blocks = new ReportBlock[]
                {
                    new ReportParagraph("stray-version-prefix", "Stray 2.1."),
                    new ReportParagraph("dotted-body", "Elsewhere 2.1.0.0."),
                    new ReportParagraph("other-version", "FamilyQuant 8.9.10.11"),
                } },
            } };
            WordReportWriter.Write(document, versionReport);
            int versionExit = Run();
            trace = ReportCsvTable.Build("version", "version", File.ReadAllText(output), "");
            int context = trace.Columns.ToList().IndexOf("context");
            Check("J18 classifies the complete current FamilyQuant cover version as VERSION",
                trace.Rows.Where(r => r[status] == "VERSION").Select(r => r[value]).SequenceEqual(new[] { "2.1.0.0" }));
            Check("J18 a stray body 2.1 stays NOT FOUND beside a classified cover",
                versionExit != 0 && trace.Rows.Any(r => r[value] == "2.1" && r[status] == "NOT FOUND" && r[context] == "Stray 2.1."));
            Check("J18 other dotted numbers and noncurrent FamilyQuant versions are not VERSION",
                trace.Rows.Where(r => r[context] is "Elsewhere 2.1.0.0." or "FamilyQuant 8.9.10.11").All(r => r[status] != "VERSION"));
            Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, "2.1.0.1");
            int noncurrentExit = Run();
            trace = ReportCsvTable.Build("noncurrent-version", "noncurrent-version", File.ReadAllText(output), "");
            Check("J18 a cover version differing from AppVersionInfo.Current is not exempt",
                noncurrentExit != 0 && trace.Rows.All(r => r[status] != "VERSION"));
        }
        finally { Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, savedVersion); }
    }

    private sealed class RowComparer : IEqualityComparer<IReadOnlyList<string>>
    {
        internal static readonly RowComparer Instance = new();
        public bool Equals(IReadOnlyList<string>? x, IReadOnlyList<string>? y) => x != null && y != null && x.SequenceEqual(y);
        public int GetHashCode(IReadOnlyList<string> obj) => obj.Aggregate(17, (hash, value) => HashCode.Combine(hash, value));
    }
}
