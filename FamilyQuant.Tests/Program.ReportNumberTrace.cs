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
        ReportNumberTraceSubjectTests(root);
    }

    private static void ReportNumberTraceSubjectTests(string root)
    {
        Section("J49 subject-aware number citations");
        string document = Path.Combine(root, "subjects.docx"), output = Path.Combine(root, "subjects-trace.csv");
        var rows = Enumerable.Range(0, 50).Select(_ => (IReadOnlyList<string>)new[] { "", "", "" }).ToArray();
        rows[1] = new[] { "listed", "families above the threshold", "14" }; // C3
        rows[16] = new[] { "listed", "Cedar ensemble greedy protein estimate", "14" }; // C18
        rows[31] = new[] { "listed", "Maple ensemble greedy protein estimate", "2" }; // C33
        rows[34] = new[] { "listed", "Orchid ensemble greedy protein estimate", "2" }; // C36
        rows[37] = new[] { "listed", "Empty ensemble greedy protein estimate", "0" };
        rows[38] = new[] { "listed", "Vacant ensemble greedy protein estimate", "0" };
        rows[43] = new[] { "listed", "Hydrolase 2 greedy protein estimate", "1" };
        rows[44] = new[] { "listed", "Hydrolase greedy protein estimate", "9" }; // C46
        rows[48] = new[] { "listed", "below the threshold", "5" };
        rows[49] = new[] { "listed", "below the threshold and by panel decision", "5" }; // C51
        var numbers = new ReportTable("subject-numbers", "Numbers", new[] { "statement", "quantity", "value" }, rows, "");
        var runs = new ReportTable("subject-runs", "Run values", new[] { "run", "value" },
            new IReadOnlyList<string>[] { new[] { "run-a", "2" }, new[] { "run-b", "2" } }, "");
        var options = new ReportTable("subject-options", "Options", new[] { "option", "value" },
            new IReadOnlyList<string>[] { new[] { "Extraction tolerance", "2" }, new[] { "Minimum points", "2" },
                new[] { "Other setting", "37" }, new[] { "Only setting", "38" } }, "");
        var pointer = new ReportTable("subject-pointer", "Pointers", new[] { "Subject", "Source data row" },
            new IReadOnlyList<string>[] { new[] { "Other setting", "37" }, new[] { "Orphan measure", "17" } }, "");
        var single = new ReportTable("subject-single", "Single-word family", new[] { "Family", "Estimate" },
            new IReadOnlyList<string>[] { new[] { "Hydrolase", "9" } }, "");
        var roles = new ReportTable("subject-roles", "Equal fields", new[] { "Family", "family_id", "Protein estimate" },
            new IReadOnlyList<string>[] { new[] { "Elm ensemble", "2", "2" } }, "");
        var report = new ReportDocument { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[]
        {
            new ReportParagraph("families", "14 families are above the threshold. In headline order: Cedar ensemble (14 proteins); Maple ensemble (2 proteins); Orchid ensemble (2 proteins)."),
            new ReportParagraph("others", "5 families are below the threshold and by panel decision."),
            new ReportParagraph("run", "run-b has 2 units."),
            new ReportParagraph("option", "Minimum points: 2."),
            new ReportParagraph("cover-version", "FamilyQuant 2.0.0.0"),
            new ReportParagraph("unlabelled", "Unlabelled 14."),
            new ReportParagraph("paired", "Maple ensemble has 2 proteins and Orchid ensemble has 2 proteins."),
            new ReportParagraph("wrong-subject", "Only setting: 37."),
            new ReportParagraph("wrong-after", "Other setting is 37 and Only setting is 37."),
            new ReportParagraph("pointer", "Orphan measure: 17."),
            new ReportParagraph("single-word", "Hydrolase: 9 proteins."),
            new ReportParagraph("name-number", "Hydrolase 2: 1 protein."),
            new ReportParagraph("unknown", "Planted 98765.4321."),
            new ReportTable("printed-subjects", "Estimates", new[] { "Family", "Estimate" },
                new IReadOnlyList<string>[] { new[] { "Orchid ensemble", "2" } }, ""),
            new ReportTable("printed-roles", "Protein counts", new[] { "Family", "Protein estimate" },
                new IReadOnlyList<string>[] { new[] { "Elm ensemble", "2" } }, ""),
        }) } };
        WordReportWriter.Write(document, report);
        ReportSupplementWorkbook.Write(ReportSupplementWorkbook.PathForReport(document), report, new[]
        {
            new ReportSupplementSheet("subject-numbers", ReportSectionIds.Results, "Numbers", numbers),
            new ReportSupplementSheet("subject-runs", ReportSectionIds.Results, "Run values", runs),
            new ReportSupplementSheet("subject-options", ReportSectionIds.Results, "Options", options),
            new ReportSupplementSheet("subject-pointer", ReportSectionIds.Results, "Pointers", pointer),
            new ReportSupplementSheet("subject-single", ReportSectionIds.Results, "Single-word family", single),
            new ReportSupplementSheet("subject-roles", ReportSectionIds.Results, "Equal fields", roles),
        });
        int exit = ReportNumberTrace.Run(document, Path.Combine(root, "absent-subject-sidecars"), output);
        var trace = ReportCsvTable.Build("subjects", "subjects", File.ReadAllText(output), "");
        int Col(string name) => trace.Columns.ToList().IndexOf(name);
        var familyRows = trace.Rows.Where(r => r[Col("context")].StartsWith("14 families", StringComparison.Ordinal)).ToArray();
        Check("J49 count of families keeps its own C3 citation", familyRows[0][Col("cell")] == "C3");
        Check("J49 fourteen proteins cites its own family C18 rather than count of families C3", familyRows[1][Col("cell")] == "C18");
        Check("J49 two proteins cites the second family's C36 rather than the first family's C33", familyRows[3][Col("cell")] == "C36");
        Check("J49 panel-decision count cites its specific C51 rather than the threshold-only count", trace.Rows.Any(r => r[Col("context")].StartsWith("5 families", StringComparison.Ordinal) && r[Col("cell")] == "C51"));
        Check("J49 repeated run value cites the named run", trace.Rows.Any(r => r[Col("context")] == "run-b has 2 units." && r[Col("csv")].EndsWith("!Run values", StringComparison.Ordinal) && r[Col("cell")] == "B3"));
        Check("J49 option name before a colon selects that option", trace.Rows.Any(r => r[Col("context")] == "Minimum points: 2." && r[Col("csv")].EndsWith("!Options", StringComparison.Ordinal) && r[Col("cell")] == "B3"));
        Check("J49 cover version does not cite a protein estimate", trace.Rows.Where(r => r[Col("context")] == "FamilyQuant 2.0.0.0").All(r => (r[Col("status")] is "AMBIGUOUS" or "VERSION") && r[Col("csv")] == "" && r[Col("cell")] == ""));
        Check("J49 unrelated equal cells without a subject are explicitly ambiguous", trace.Rows.Any(r => r[Col("context")] == "Unlabelled 14." && r[Col("status")] == "AMBIGUOUS" && r[Col("cell")] == ""));
        Check("J49 table citations use the named Word row and its matching estimate column", trace.Rows.Any(r => r[Col("location")].Contains("table", StringComparison.Ordinal)
            && r[Col("number")] == "2" && r[Col("csv")].Contains("!Estimates_", StringComparison.Ordinal) && r[Col("cell")] == "B2"));
        var paired = trace.Rows.Where(r => r[Col("context")].StartsWith("Maple ensemble has", StringComparison.Ordinal)).ToArray();
        Check("J49 two subjects in one clause use the closest preceding name", paired.Length == 2 && paired[0][Col("cell")] == "C33" && paired[1][Col("cell")] == "C36");
        Check("J49 does not fall back when a named subject exists with a different value", trace.Rows.Any(r => r[Col("context")] == "Only setting: 37." && r[Col("status")] == "AMBIGUOUS" && r[Col("cell")] == ""));
        var wrongAfter = trace.Rows.Where(r => r[Col("context")].StartsWith("Other setting is", StringComparison.Ordinal)).ToArray();
        Check("J49 a preceding different subject cannot hide a mismatching later subject", wrongAfter.Length == 2 && wrongAfter[0][Col("cell")] == "B4" && wrongAfter[1][Col("status")] == "AMBIGUOUS");
        Check("J49 a structural source-row ordinal is not a measured-value citation", trace.Rows.Any(r => r[Col("context")] == "Orphan measure: 17." && r[Col("status")] == "AMBIGUOUS"));
        Check("J49 a known single-word family also binds its longer quantity label", trace.Rows.Any(r => r[Col("context")] == "Hydrolase: 9 proteins." && r[Col("cell")] == "C46"));
        Check("J49 a number inside the subject name does not borrow another numeric cell", trace.Rows.Any(r => r[Col("context")] == "Hydrolase 2: 1 protein." && r[Col("number")] == "2" && r[Col("status")] == "AMBIGUOUS")
            && trace.Rows.Any(r => r[Col("context")] == "Hydrolase 2: 1 protein." && r[Col("number")] == "1" && r[Col("cell")] == "C45"));
        Check("J49 the printed column selects the estimate instead of an equal ID for the same subject", trace.Rows.Any(r => r[Col("context")] == "2"
            && r[Col("csv")].EndsWith("!Equal fields", StringComparison.Ordinal) && r[Col("cell")] == "C2"));
        Check("J49 a missing numeric value retains NOT FOUND and failure exit", exit == 1 && trace.Rows.Any(r => r[Col("number")] == "98765.4321" && r[Col("status")] == "NOT FOUND"));
        byte[] invariant = File.ReadAllBytes(output);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            ReportNumberTrace.Run(document, Path.Combine(root, "absent-subject-sidecars"), output);
            Check("J49 subject and ambiguity decisions are byte-identical under pt-BR", invariant.SequenceEqual(File.ReadAllBytes(output)));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        WordReportWriter.Write(document, report with { Sections = new[] { report.Sections[0] with
            { Blocks = report.Sections[0].Blocks.Where(b => b.Id != "unknown").ToArray() } } });
        Check("J49 ambiguity alone retains the prior zero NOT FOUND exit convention", ReportNumberTrace.Run(document,
            Path.Combine(root, "absent-subject-sidecars"), output) == 0 && File.ReadAllText(output).Contains("AMBIGUOUS", StringComparison.Ordinal));
        string prior = Path.Combine(root, "prior-trace.csv"), changed = Path.Combine(root, "changed-trace.csv");
        File.WriteAllText(prior, "location,number,status,csv,cell,context\nparagraph,14,MATCH,Numbers,C3,Cedar ensemble\n");
        File.WriteAllText(changed, "location,number,status,csv,cell,context,subject\nparagraph,14,MATCH,Numbers,C18,Cedar ensemble,cedar ensemble\n");
        var comparison = ReportNumberTraceComparison.Write(prior, changed);
        Check("J49 comparison lists every changed citation with its prior and replacement cell", comparison.Tokens == 1 && comparison.ChangedCitations == 1
            && File.ReadAllText(comparison.Changes).Contains("C3,MATCH,Numbers,C18", StringComparison.Ordinal));
        Check("J49 comparison refuses to overwrite its input trace", Throws<InvalidOperationException>(() => ReportNumberTraceComparison.ValidatePaths(prior, prior)));
        Check("J49 comparison refuses a derived-output collision with another native source", Throws<InvalidOperationException>(() =>
            ReportNumberTraceComparison.ValidatePaths(prior, changed, new[] { Path.ChangeExtension(changed, ".changes.csv") })));
        File.WriteAllText(changed, File.ReadAllText(changed).Replace("paragraph,14", "paragraph,15", StringComparison.Ordinal));
        Check("J49 comparison rejects misaligned token identities", Throws<InvalidDataException>(() => ReportNumberTraceComparison.Write(prior, changed)));
    }

    private sealed class RowComparer : IEqualityComparer<IReadOnlyList<string>>
    {
        internal static readonly RowComparer Instance = new();
        public bool Equals(IReadOnlyList<string>? x, IReadOnlyList<string>? y) => x != null && y != null && x.SequenceEqual(y);
        public int GetHashCode(IReadOnlyList<string> obj) => obj.Aggregate(17, (hash, value) => HashCode.Combine(hash, value));
    }
}
