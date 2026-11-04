using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlReportDataCommands() =>
    [new("--test-html-report-data", 10495, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-html-report-data", (_, _) =>
    {
        HtmlReportDataTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static (ReportDocument Document, HtmlReportContext Context) HtmlDataFixture()
    {
        const double mean = .385988123456;
        var table = new ReportTable("family-run-means", "Headline family composition: mean of per-run shares",
            new[] { "Family", "Mean share (%)", "Sample SD (pp)", "Whole measured (%)", "Whole-signal SD (pp)", "Proteins needed (greedy)", "Proteins with own peptide" },
            new IReadOnlyList<string>[] { new[] { "Cedar </script> ensemble", "38.5988", "1.10394", "32.34", "0.9", "225", "104" },
                new[] { "Others", "0.491774", "0.01", "0.4", "0.01", "", "" } }, "Stored fixture values")
        { SupplementSource = new("family_user_id,family,status,mean_pct\n7,Cedar </script> ensemble,Listed,38.5988\n,Others,Canonical grouped members,0.491774\n", new[] { "family", "mean_pct" }) };
        var document = new ReportDocument { Title = "Interactive fixture <&>", Authors = "Author <name>", Sections = new[]
        {
            new ReportSection(ReportSectionIds.Title, "Title", Array.Empty<ReportBlock>()),
            new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { table }),
        } };
        var context = new HtmlReportContext { StoredStudies = new QuantManifest { FamilyRunShares = new FamilyRunSharesResult
        { Families = new() { new() { FamilyUserId = 7, MeanShare = mean, StandardDeviation = .0110394, MeanShareOfMeasuredSignal = .3234 } } } } };
        return (document, context);
    }

    private static void HtmlReportDataTests()
    {
        Section("M1 source-bound graphical report data");
        var fixture = HtmlDataFixture();
        byte[] word = WordReportWriter.BuildBytes(fixture.Document);
        var model = HtmlReportPayloadBuilder.Build(fixture.Document, Array.Empty<ReportSupplementSheet>(), context: fixture.Context);
        var family = model.Composition.Families[0];
        Check("HTML headline uses the exact Word display and direct Word cell", model.Fields[family.Mean!].Text == "38.5988"
            && model.Fields[family.Mean!].Source is { Kind: "word-cell", Table: 1, Row: 2, Column: 2 });
        Check("HTML geometry prefers the typed stored value rather than rounded display text", model.Fields[family.Mean!].Value == .385988123456 * 100);
        Check("HTML names preserve literal user text before safe serialization", model.Fields[family.Name].Text == "Cedar </script> ensemble");
        Check("HTML retains canonical Others without introducing member names", model.Composition.Families.Count == 2
            && model.Composition.Families[1].IsOthers && model.Fields[model.Composition.Families[1].Name].Text == "Others");
        Check("HTML producer does not mutate the Word source", word.SequenceEqual(WordReportWriter.BuildBytes(fixture.Document)));
        var hidden = HtmlReportPayloadBuilder.Build(fixture.Document, Array.Empty<ReportSupplementSheet>(),
            new() { ExcludedIds = new[] { ReportSectionIds.Results } }, fixture.Context);
        Check("HTML producer omits unselected quantitative source fields", hidden.Composition.Families.Count == 0
            && hidden.Fields.Values.All(f => !f.Text.Contains("Cedar", StringComparison.Ordinal)));
        Check("HTML output links retain relative file identity safely", HtmlReportPayloadBuilder.Build(fixture.Document,
            Array.Empty<ReportSupplementSheet>(), wordFileName: "A & report.docx").Links.Word == "A%20%26%20report.docx");
        var mean = model.Fields[family.Mean!];
        Check("HTML chart share has a separate program-rounded short field", mean.Short != null && model.Fields[mean.Short].Text == "38.6"
            && model.Fields[mean.Short].Source is { Kind: "stored-value", Property: "/manifest/FamilyRunShares/Families/0/MeanShare", Scale: 100, Format: "G3" }
            && mean.Text == "38.5988");
        var bad = HtmlDataFixture();
        bad.Context.StoredStudies!.FamilyRunShares!.Families[0].MeanShare = .4;
        Check("HTML refuses a stored quantity that disagrees with the prepared Word value", Throws<System.IO.InvalidDataException>(() =>
            HtmlReportPayloadBuilder.Build(bad.Document, Array.Empty<ReportSupplementSheet>(), context: bad.Context)));
        HtmlReportSeriesTests();
        HtmlReportSummaryTests();
        HtmlRunLabelTests();
        HtmlDotMeasureTests();
    }

    private static void HtmlRunLabelTests()
    {
        var fixture = HtmlDataFixture();
        string[] names = { "acquisition_240min_01", "acquisition_240min_02", "acquisition_240min_03" };
        fixture.Context.StoredStudies!.FamilyRunShares!.Runs.AddRange(names.Select(name => new FamilyRunTotal { Run = name }));
        var table = new ReportTable("family-run-shares", "Runs", new[] { "family_user_id", "run", "assigned_signal_pct" },
            names.Select(name => (IReadOnlyList<string>)new[] { "7", name, "38.5988" }).ToArray(), "");
        var model = HtmlReportPayloadBuilder.Build(fixture.Document,
            new[] { new ReportSupplementSheet(table.Id, ReportSectionIds.Results, "Run names", table) }, context: fixture.Context);
        var labels = model.Composition.Families[0].Runs.Select(run => model.Fields[run.Name]).ToArray();
        Check("HTML run labels use shortest distinct endings while preserving full pop-up names", labels.Select(f => f.Text).SequenceEqual(names)
            && labels.Select(f => model.Fields[f.Short!].Text).SequenceEqual(new[] { "01", "02", "03" }));
        Check("HTML short run names are source-bound labels rather than new numeric results", labels.All(f => model.Fields[f.Short!].Value == null
            && model.Fields[f.Short!].Source is { Kind: "workbook-cell", Sheet: "Run names", Format: "run-suffix" }));
    }

    private static void HtmlReportSummaryTests()
    {
        var fixture = HtmlDataFixture();
        fixture.Context.StoredStudies!.FamilyRunShares!.Runs.Add(new() { Run = "run-a", AssignedSignal = 83.8674, MeasuredSignal = 100 });
        var findings = new ReportTable("findings-numbers", "Findings", new[] { "statement", "quantity", "value" },
            new IReadOnlyList<string>[] { new[] { "listed", "families above the threshold", "14" }, new[] { "replicates", "runs", "3" },
                new[] { "listed", "report threshold (%)", "0.1" }, new[] { "credited", "run-a credited share of measured (%)", "83.8674" } }, "");
        var totals = new ReportTable("family-run-totals", "Totals", new[] { "run", "assigned_family_signal", "measured_peptide_signal" },
            new IReadOnlyList<string>[] { new[] { "run-a", "83.8674", "100" } }, "");
        var gap = new ReportTable("database-gap-table", "Database gap", new[] { "Family or global row", "Fraction of measured signal" },
            new IReadOnlyList<string>[] { new[] { "Cedar", "0.123456789" } }, "");
        var document = fixture.Document with { Sections = fixture.Document.Sections.Concat(new[]
        {
            new ReportSection(ReportSectionIds.Findings, "Summary of findings", new ReportBlock[] { new ReportParagraph(findings.Id, "Companion data") }),
            new ReportSection(ReportSectionIds.DatabaseGap, "Database gap", new ReportBlock[] { gap }),
        }).ToArray() };
        var sheets = new[] { new ReportSupplementSheet(findings.Id, ReportSectionIds.Findings, "Findings", findings),
            new ReportSupplementSheet(totals.Id, ReportSectionIds.Results, "Totals", totals) };
        var model = HtmlReportPayloadBuilder.Build(document, sheets, context: fixture.Context);
        Check("HTML highlights copy printed summary values rather than counting family rows", model.Highlights.Count == 3
            && model.Fields[model.Highlights[0].Value].Text == "14" && model.Composition.Families.Count == 2);
        Check("HTML credited share uses the existing per-run report statistic", model.CreditedRuns.Count == 1
            && model.Fields[model.CreditedRuns[0].Share].Text == "83.8674");
        var fraction = model.Fields[model.DatabaseGap[0].Fraction];
        Check("HTML gap short label keeps the Word fraction unit and exact value for pop-ups", fraction.Text == "0.123456789" && fraction.Short != null
            && model.Fields[fraction.Short].Text == "0.123" && model.Fields[fraction.Short].Unit == "fraction" && model.Fields[fraction.Short].Source.Scale == 1);
        Check("D47 HTML producer emits no methods or quality prose", model.Methods.Count == 0 && model.Quality.Count == 0 && model.Interpretation == null);
        var repeated = document with { Sections = document.Sections.Select(s => s.Id == ReportSectionIds.Results
            ? s with { Blocks = s.Blocks.Concat(new ReportBlock[] { new ReportParagraph("first", "Observed 14 units."), new ReportParagraph("second", "Observed 14 units.") }).ToArray() } : s).ToArray() };
        var located = fixture.Context with { CoverageProvider = (id, add, _) => id != "7" ? Array.Empty<HtmlReportCoverage>() : new[]
        { new HtmlReportCoverage { Title = add("14", new() { Kind = "word-text", ParagraphText = "Observed 14 units.", Occurrence = 1 }, "", null) } } };
        var withLocator = HtmlReportPayloadBuilder.Build(repeated, sheets, context: located);
        string title = withLocator.Composition.Families[0].Coverage[0].Title;
        Check("word-text allocation preserves exact paragraph and duplicate occurrence without changing Word", withLocator.Fields[title].Source
            is { ParagraphText: "Observed 14 units.", Occurrence: 1 });
    }

    private static void HtmlReportSeriesTests()
    {
        var fixture = HtmlDataFixture();
        fixture.Context.StoredStudies!.FamilyRunShares!.Families[0].Runs.Add(new()
        { Run = "run-a", ShareOfAssignedSignal = .385988123456, ShareOfMeasuredSignal = .3234 });
        var runs = new ReportTable("family-run-shares", "Run values", new[] { "family_user_id", "family", "run", "assigned_signal_pct", "measured_peptide_signal_pct", "assigned_signal_fraction", "measured_peptide_signal_fraction" },
            new IReadOnlyList<string>[] { new[] { "7", "Cedar", "run-a", "38.5988", "32.34", "0.385988123456", "0.3234" } }, "");
        var parts = new ReportTable("subclass-parts", "Pooled parts", new[] { "family_user_id", "part", "within_family_pct", "within_family_fraction", "unresolved" },
            new IReadOnlyList<string>[] { new[] { "7", "Class A", "60", "0.6", "1" }, new[] { "7", "Not resolved", "40", "0.4", "0" } }, "");
        var counts = new ReportTable("protein-counts", "Counts", new[] { "family_user_id", "observed_peptides" }, new IReadOnlyList<string>[] { new[] { "7", "9" } }, "");
        var sheets = new[] { new ReportSupplementSheet(runs.Id, ReportSectionIds.Results, "Runs", runs),
            new ReportSupplementSheet(parts.Id, ReportSectionIds.Results, "Parts", parts), new ReportSupplementSheet(counts.Id, ReportSectionIds.Results, "Counts", counts) };
        var model = HtmlReportPayloadBuilder.Build(fixture.Document, sheets, context: fixture.Context);
        var family = model.Composition.Families[0];
        var run = model.Fields[family.Runs[0].Share!];
        Check("HTML run points keep their native exact label and unrounded geometry", family.Runs.Count == 1 && run.Text == "38.5988" && run.Value == .385988123456 * 100
            && run.Source is { Kind: "workbook-cell", Sheet: "Runs", Row: 2, Column: 4 });
        Check("HTML short run label binds to the native stored fraction", run.Short != null && model.Fields[run.Short].Text == "38.6"
            && model.Fields[run.Short].Source is { Sheet: "Runs", Column: 6, Scale: 100, Format: "G3" });
        Check("HTML preserves pooled within-family parts independently of the headline mean", family.Parts.Count == 2
            && model.Fields[family.Parts[0].Share!].Text == "60" && model.Fields[family.Parts[1].Share!].Text == "40");
        Check("HTML unresolved hatch follows the native flag rather than a part's display label", family.Parts[0].IsUnresolved && !family.Parts[1].IsUnresolved);
        Check("HTML peptide evidence count stays separate from the database-protein estimate", model.Fields[family.Peptides!].Text == "9" && model.Fields[family.Proteins!].Text == "225");
        var owned = fixture.Document with { Sections = fixture.Document.Sections.Append(
            new ReportSection("family:cedar", "Cedar", Array.Empty<ReportBlock>())).ToArray() };
        var context = fixture.Context with { FamilySections = new Dictionary<string, string> { ["7"] = "family:cedar" } };
        var omitted = HtmlReportPayloadBuilder.Build(owned, sheets, new() { ExcludedIds = new[] { "family:cedar" } }, context);
        var retained = omitted.Composition.Families[0];
        Check("unchecked family owner keeps the Results headline but omits all detail fields", retained.Mean != null
            && omitted.Fields[retained.Mean].Text == "38.5988" && retained.Parts.Count == 0 && retained.Runs.Count == 0
            && retained.Coverage.Count == 0 && retained.Peptides == null
            && omitted.Fields.Values.All(f => f.Text != "Class A" && f.Text != "run-a"));
    }
}
