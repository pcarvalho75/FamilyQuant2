using FamilyQuant.Report;
using FamilyQuant.Report.Captions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportCaptionCommands() => new[]
    {
        new TestCommand("--test-report-captions", 10000, TestCommandPhase.Normalized,
            args => args.Contains("--test-report-captions", StringComparer.Ordinal), (_, work) =>
            {
                ReportCaptionTests(Path.Combine(work, "report-captions"));
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            })
    };

    private static void ReportCaptionTests(string folder)
    {
        Section("Automatic captions - exact displayed data, general explanations and invariant files");
        EnsureApplication(); Directory.CreateDirectory(folder);
        var rows = new IReadOnlyList<string>[] { new[] { "Group A", "60.125", "4" }, new[] { "Group B", "39.875", "8" } };
        var table = new ReportTable("table", "Composition", new[] { "Family", "Mean share (%)", "Proteins" }, rows, "");
        var input = new FigureCaptionInput("Mean family shares", "Read bar lengths as shares and whiskers as sample SD",
            new[] { new CaptionNumber("Group A", 60.125, "%"), new CaptionNumber("Available runs", 3, "runs", "0") });
        string text = ReportCaptionBuilder.Figure(input);
        Check("figure caption states what, how and the input numbers", text.Contains("Mean family shares")
            && text.Contains("whiskers") && text.Contains("60.13%") && text.Contains("3 runs"));
        string tableText = ReportCaptionBuilder.Table(table);
        Check("table captions quote exact invariant cell strings rather than round them again",
            tableText.Contains("60.125") && tableText.Contains("Group A") && tableText.Contains("2 data rows"));
        Check("unavailable numbers cannot silently become zero in a caption",
            Throws<ArgumentException>(() => ReportCaptionBuilder.Figure(input with
            { Numbers = new[] { new CaptionNumber("Missing", double.NaN) } })));
        Check("small nonzero values are not described as zero", ReportCaptionBuilder.Number(.000001) == "1E-6");
        Check("all report figure topics have general reading guidance and retain their supplied facts",
            Enum.GetValues<ReportFigureTopic>().All(topic => ReportCaptionBuilder.Figure(
                ReportCaptionGuidance.Figure(topic, new[] { new CaptionNumber("Fixture value", 7.25, "%") })).Contains("7.25%")));
        Check("all report table topics have explicit reading guidance", Enum.GetValues<ReportTableTopic>()
            .All(topic => ReportCaptionGuidance.Table(topic).How.Length > 40));
        Check("input/provenance captions do not treat numeric-looking identifiers as measurements",
            ReportCaptionGuidance.Table(ReportTableTopic.Inputs).SummaryColumns is { Count: 0 });
        byte[] png = { 1, 2, 3 };
        var original = new ReportDocument { Sections = new[] { new ReportSection("s", "Results", new ReportBlock[]
        { table, new ReportFigure("figure", "Figure", png, "", 1, 1) }) } };
        var completed = ReportCaptionBuilder.WithCaptions(original, new Dictionary<string, FigureCaptionInput> { ["figure"] = input });
        Check("every figure/table gets a caption without changing its PNG or cells",
            ((ReportFigure)completed.Sections[0].Blocks[1]).Caption.Length > 0
            && ((ReportTable)completed.Sections[0].Blocks[0]).Caption.Length > 0
            && ReferenceEquals(((ReportFigure)completed.Sections[0].Blocks[1]).PngBytes, png)
            && original.Sections[0].Blocks.OfType<ReportFigure>().Single().Caption == "");
        Check("a figure without its data binding is refused rather than guessed from PNG bytes",
            Throws<InvalidOperationException>(() => ReportCaptionBuilder.WithCaptions(original, new Dictionary<string, FigureCaptionInput>())));
        var means = CompositionFixture();
        var meanFigure = FamilyCompositionFigures.Build(means).First();
        var description = CompositionChartCaptions.Describe(meanFigure);
        Check("composition caption numbers come from the exact rendered mean/SD snapshot",
            description.Numbers.Any(n => n.Value == 60 && n.Unit == "%")
            && description.Numbers.Any(n => n.Value == 5 && n.Unit == "percentage points"));
        var diversity = CompositionChartCaptions.Describe(FamilyCompositionFigures.BuildAbundanceDiversity(means));
        Check("diversity captions retain the greedy estimate definition",
            diversity.How.Contains("greedy estimate") && diversity.Numbers.Any(n => n.Value == 4 && n.Unit == "proteins"));
        var coverage = CompositionChartCaptions.Describe(ProteinCoverageFigures.Build(CoverageFixture()));
        Check("coverage caption describes drawn source blocks without claiming exact de novo sequence identification",
            coverage.How.Contains("aligned") && coverage.Numbers.Any(n => n.Label == "Protein length" && n.Value == 200));
        var oldCulture = CultureInfo.CurrentCulture; var oldUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string locale in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                Check(locale + " captions have the exact same invariant bytes", ReportCaptionBuilder.Figure(input) == text
                    && ReportCaptionBuilder.Table(table) == tableText);
                File.WriteAllText(Path.Combine(folder, locale + ".txt"), text + "\n" + tableText);
            }
        }
        finally { CultureInfo.CurrentCulture = oldCulture; CultureInfo.CurrentUICulture = oldUiCulture; }
    }
}
