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
            && model.Fields[mean.Short].Source is { Kind: "stored-family", Property: "mean", Family: "7", Scale: 100, Format: "G3" }
            && mean.Text == "38.5988");
        var bad = HtmlDataFixture();
        bad.Context.StoredStudies!.FamilyRunShares!.Families[0].MeanShare = .4;
        Check("HTML refuses a stored quantity that disagrees with the prepared Word value", Throws<System.IO.InvalidDataException>(() =>
            HtmlReportPayloadBuilder.Build(bad.Document, Array.Empty<ReportSupplementSheet>(), context: bad.Context)));
        HtmlReportSeriesTests();
    }

    private static void HtmlReportSeriesTests()
    {
        var fixture = HtmlDataFixture();
        fixture.Context.StoredStudies!.FamilyRunShares!.Families[0].Runs.Add(new()
        { Run = "run-a", ShareOfAssignedSignal = .385988123456, ShareOfMeasuredSignal = .3234 });
        var runs = new ReportTable("family-run-shares", "Run values", new[] { "family_user_id", "family", "run", "assigned_signal_pct", "measured_peptide_signal_pct", "assigned_signal_fraction", "measured_peptide_signal_fraction" },
            new IReadOnlyList<string>[] { new[] { "7", "Cedar", "run-a", "38.5988", "32.34", "0.385988123456", "0.3234" } }, "");
        var parts = new ReportTable("subclass-parts", "Pooled parts", new[] { "family_user_id", "part", "within_family_pct", "within_family_fraction" },
            new IReadOnlyList<string>[] { new[] { "7", "Class A", "60", "0.6" }, new[] { "7", "Not resolved", "40", "0.4" } }, "");
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
        Check("HTML peptide evidence count stays separate from the database-protein estimate", model.Fields[family.Peptides!].Text == "9" && model.Fields[family.Proteins!].Text == "225");
    }
}
