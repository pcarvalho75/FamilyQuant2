using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlNumberTraceCommands() =>
    [new("--test-html-number-trace", 10497, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-html-number-trace", (_, _) =>
    { HtmlNumberTraceTests(); Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1; }),
     new("--trace-html-report-numbers", 10497, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--trace-html-report-numbers", (args, _) =>
    {
        if (args.Length != 5) throw new ArgumentException("Use --trace-html-report-numbers <report.html> <source-folder> <project.fqproj> <output.csv>.");
        return ReportNumberTrace.RunHtml(args[1], args[2], args[4], args[3]);
    })];

    private static void HtmlNumberTraceTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.HtmlTrace", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string word = Path.Combine(root, "report.docx"), book = Path.Combine(root, "report_supplement.xlsx"), output = Path.Combine(root, "trace.csv");
        double stored = Enumerable.Range(1, 100).Select(i => (i + .23499999999) / 100).First(v =>
            (v * 100).ToString("G3", CultureInfo.InvariantCulture) != double.Parse((v * 100).ToString("G6", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture).ToString("G3", CultureInfo.InvariantCulture));
        string exactWord = (stored * 100).ToString("G6", CultureInfo.InvariantCulture);
        string incorrectlyRounded = double.Parse(exactWord, CultureInfo.InvariantCulture).ToString("G3", CultureInfo.InvariantCulture);
        var table = new ReportTable("values", "Values", new[] { "Family", "Mean" }, new IReadOnlyList<string>[] { new[] { "Cedar2", exactWord } }, "Values");
        const string paragraph = "Stored threshold 0.1% and 3 runs.";
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
        string sequenceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ACDEFG")));
        string coverageId = "figure:protein_coverage_7_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("protein1"))).ToLowerInvariant() + ".png";
        var nativeFigure = new ReportFigure(coverageId, "Coverage protein1", png, "Stored peptide 1-4", 1, 1);
        var doc = new ReportDocument { Title = "Sample2", Authors = "Author1", Sections = new[]
        { new ReportSection(ReportSectionIds.Title, "Title", Array.Empty<ReportBlock>()), new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[]
            { table, new ReportParagraph("note1", paragraph), new ReportParagraph("note2", paragraph), nativeFigure }) } };
        WordReportWriter.Write(word, doc);
        ReportSupplementWorkbook.Write(book, doc, new[] { new ReportSupplementSheet("native", ReportSectionIds.Results, "Data",
            new ReportTable("native-values", "Native", new[] { "Name", "Fraction" }, new IReadOnlyList<string>[]
            { new[] { "acquisition_01", "0.125" }, new[] { "acquisition_01", "0.25" } }, "Native")) });
        var project = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "protein1", Sequence = "ACDEFG", UserID = 7 } } };
        var manifest = new QuantManifest { FamilyRunShares = new FamilyRunSharesResult { Families = new()
        { new() { FamilyUserId = 7, MeanShare = stored } } } };
        manifest.FamilyPeptideLocalization = new() { Families = new() { new() { FamilyUserId = 7, Proteins = new()
            { new() { Rank = 1, ProteinIdentifier = "protein1", ProteinLength = 6, ProteinSequenceSha256 = sequenceHash } } } },
            Segments = new() { new() { FamilyUserId = 7, ProteinIdentifier = "protein1", NormalizedSequence = "ACDE", Side = "identified", Intervals = new() { new() { Start = 1, End = 4 } } } } };
        manifest.FamilyRunShares!.MembershipSha256 = manifest.FamilyPeptideLocalization.MembershipSha256 = FamilyStudyMembership.Fingerprint(project);
        var fields = new Dictionary<string, HtmlReportField>
        {
            ["title"] = new(doc.Title, null, "", new() { Kind = "word-property", Property = "Title" }),
            ["author"] = new(doc.Authors, null, "", new() { Kind = "word-property", Property = "Creator" }),
            ["name"] = new("Cedar2", null, "", new() { Kind = "word-cell", Table = 1, Row = 2, Column = 1 }),
            ["exact"] = new(exactWord, stored * 100, "%", new() { Kind = "word-cell", Table = 1, Row = 2, Column = 2 }) { Short = "short" },
            ["short"] = new((stored * 100).ToString("G3", CultureInfo.InvariantCulture), stored * 100, "%",
                new() { Kind = "stored-value", Property = "/manifest/FamilyRunShares/Families/0/MeanShare", Format = "G3", Scale = 100 }),
            ["hover"] = new("12.5", 12.5, "%", new() { Kind = "workbook-cell", Sheet = "Data", Row = 2, Column = 2, Scale = 100, Format = "G6" }),
            ["paragraph"] = new("0.1%", null, "%", new() { Kind = "word-text", ParagraphText = paragraph, Occurrence = 1 }),
            ["runName"] = new("acquisition_01", null, "", new() { Kind = "workbook-cell", Sheet = "Data", Row = 2, Column = 1 }) { Short = "runSuffix" },
            ["runSuffix"] = new("01", null, "", new() { Kind = "workbook-cell", Sheet = "Data", Row = 2, Column = 1, Format = "run-suffix" }),
            ["gap"] = new("0.125", .125, "fraction", new() { Kind = "workbook-cell", Sheet = "Data", Row = 2, Column = 2 }) { Short = "gapShort" },
            ["gapShort"] = new("0.125", .125, "fraction", new() { Kind = "workbook-cell", Sheet = "Data", Row = 2, Column = 2, Format = "G3" }),
        };
        string Add(string text, HtmlReportReference source, string unit, double? value)
        { string key = "coverage" + fields.Count; fields.Add(key, new(text, value, unit, source)); return key; }
        var coverage = HtmlReportCoverageBuilder.Build(doc, project, manifest, new[] { 7 }, Add).Single();
        var family = new HtmlReportFamily { Id = "7", Name = "name", Mean = "exact", Coverage = new[] { coverage } };
        var payload = new HtmlReportPayload { Title = "title", Authors = "author", Fields = fields,
            Composition = new() { Families = new[] { family with { Runs = new[] { new HtmlFamilyRun("runName", "hover", null) } } } },
            DatabaseGap = new[] { new HtmlGapRow("name", "gap") } };
        string beforeWord = FileFingerprint.Sha256(word), beforeBook = FileFingerprint.Sha256(book);
        int Run(HtmlReportPayload? other = null) => ReportNumberTrace.RunHtmlBindings(other ?? payload, word, book, project, manifest, output);
        Check("M1m exact Word/workbook/stored/short bindings have zero NOT FOUND", Run() == 0 && !File.ReadAllText(output).Contains("NOT FOUND", StringComparison.Ordinal));
        Check("M1m short label uses unrounded stored value across a rounding boundary", fields["short"].Text != incorrectlyRounded);
        var old = fields["short"]; fields["short"] = old with { Text = incorrectlyRounded };
        Check("M1m rejects short label rounded from the already-rounded Word cell", Run() == 1 && File.ReadAllText(output).Contains("NOT FOUND", StringComparison.Ordinal)); fields["short"] = old;
        fields["short"] = old with { Text = exactWord, Source = old.Source with { Format = "G6" } };
        Check("M1m enforces the program's G3 short-label rule", Run() == 1); fields["short"] = old;
        old = fields["exact"]; fields["exact"] = old with { Value = 12.5 };
        Check("M1m rejects geometry unrelated to its stored/native source", Run() == 1); fields["exact"] = old;
        old = fields["hover"]; fields["hover"] = old with { Source = old.Source with { Scale = 1 } };
        Check("M1m validates popup scaling instead of accepting an equal unrelated value", Run() == 1); fields["hover"] = old;
        Check("M1m rejects dangling rendered field references", Run(payload with { Title = "missing" }) == 1);
        old = fields["name"]; fields["name"] = old with { Source = old.Source with { Row = 99 } };
        Check("M1m rejects wrong source cell rather than searching for a same number elsewhere", Run() == 1); fields["name"] = old;
        old = fields["paragraph"]; fields["paragraph"] = old with { Source = old.Source with { Occurrence = 2 } };
        Check("M1m rejects an absent ordinal among identical Word paragraphs", Run() == 1); fields["paragraph"] = old;
        fields["paragraph"] = old with { Source = old.Source with { ParagraphText = "Unrelated 0.1%" } };
        Check("M1m requires the exact paragraph rather than an equal number", Run() == 1); fields["paragraph"] = old;
        HtmlReportPayload WithCoverage(HtmlReportCoverage changed) => payload with { Composition = payload.Composition with { Families = new[] { family with { Coverage = new[] { changed } } } } };
        Check("M1m rejects altered live stored endpoints", Run(WithCoverage(coverage with
            { Peptides = new[] { coverage.Peptides.Single() with { End = 5 } } })) == 1);
        Check("M1m rejects altered live sequence length", Run(WithCoverage(coverage with { Length = 7 })) == 1);
        var fallback = coverage with { Png = "data:image/png;base64," + Convert.ToBase64String(png), Length = null, LengthLabel = null,
            Protein = null, Peptides = Array.Empty<HtmlCoverageSpan>() };
        Check("M1m accepts byte-identical native fallback figure", Run(WithCoverage(fallback)) == 0);
        Check("M1m rejects changed native fallback PNG", Run(WithCoverage(fallback with { Png = "data:image/png;base64,AQID" })) == 1);
        Check("M1m reports malformed native PNG as NOT FOUND", Run(WithCoverage(fallback with { Png = "data:image/png;base64,invalid!" })) == 1);
        string html = Path.Combine(root, "report.html"); HtmlReportWriter.Write(html, payload);
        bool refused = false;
        try { ReportNumberTrace.RunHtml(html, root, html); } catch (IOException) { refused = true; }
        Check("M1m trace cannot overwrite its input HTML", refused);
        string projectPath = Path.Combine(root, "project.fqproj");
        bool projectRefused = false;
        try { ReportNumberTrace.RunHtml(html, root, projectPath, projectPath); } catch (IOException) { projectRefused = true; }
        Check("M1m trace refuses its source project as output before reading or writing it", projectRefused && !File.Exists(projectPath));
        old = fields["runSuffix"]; fields["runSuffix"] = old with { Text = "02" };
        Check("M157 rejects a short run label that is not a suffix", Run() == 1); fields["runSuffix"] = old;
        fields["runSuffix"] = old with { Value = 1 };
        Check("M157 treats a leading-zero run suffix as text, never as a numeric value", Run() == 1); fields["runSuffix"] = old;
        fields["runSuffix"] = old with { Source = old.Source with { Row = 3 } };
        Check("M157 suffix and full name share the exact source even when another cell is equal", Run() == 1); fields["runSuffix"] = old;
        old = fields["gapShort"]; fields["gapShort"] = old with { Text = "12.5", Value = 12.5, Unit = "%", Source = old.Source with { Scale = 100 } };
        Check("M157 gap short labels retain the native fraction unit and scale", Run() == 1); fields["gapShort"] = old;
        Check("M157 valid leading-zero suffix and same-unit gap label trace with zero missing", Run() == 0);
        Check("M1m trace does not rewrite either source package", beforeWord == FileFingerprint.Sha256(word) && beforeBook == FileFingerprint.Sha256(book));
        HtmlAxisNumberTraceTests();
    }
}
