using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlReportFiguresCommands() =>
    [new("--test-html-report-figures", 10521, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-report-figures", (args, _) =>
        {
            HtmlReportFiguresTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-report-figures"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlReportFiguresTests(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        var rows = SyntheticAnalyticsRows();
        var data = AnalyticsBuilder.Build(rows, new Dictionary<string, double> { ["ui_01"] = 5e8, ["ui_02"] = 4e8 });
        var families = FamilyFixture(3, unattributedIdentified: 4000);
        var plan = ReportFigureRenderer.Plan(data, new(), new(), families);
        var screen = AnalyticsFigures.Build(data, families);
        Check("HTML native Results images use every Results screen chart class", screen.All(figure => plan.Charts
            .Single(chart => chart.FileName == figure.FileName).CreateChart().GetType() == figure.Chart.GetType()));
        var native = ReportFigureRenderer.Render(plan, Path.Combine(output, "native"));
        var document = new ReportDocument { Title = "Synthetic HTML figure fixture", Sections = native.GroupBy(figure => figure.SectionId)
            .Select(group => new ReportSection(group.Key, group.Key, group.Select(figure => (ReportBlock)figure.Figure).ToArray())).ToArray() };
        var payload = HtmlReportFigures.Build(new(), document);
        var imageNames = AnalyticsFigures.FileNames.Where(name => name != AnalyticsFigures.FamilyContributionFile).ToArray();
        Check("HTML includes every fixed Results figure", imageNames.All(name => payload.Results.Any(figure => figure.SourceFigureId == "figure:" + name)));
        Check("HTML contribution picture is replaced while Word keeps its native figure", payload.Results.All(figure => !figure.SourceFigureId.StartsWith("figure:family_contribution", StringComparison.Ordinal))
            && document.Sections.SelectMany(section => section.Blocks).OfType<ReportFigure>().Any(figure => figure.Id == "figure:" + AnalyticsFigures.FamilyContributionFile));
        Check("HTML figures retain the exact native Word PNG and title/caption bindings", payload.Results.Concat(payload.Quality).Concat(payload.Sensitivity).All(figure =>
        {
            var original = native.Single(item => item.Figure.Id == figure.SourceFigureId).Figure;
            return Convert.FromBase64String(figure.Png["data:image/png;base64,".Length..]).SequenceEqual(original.PngBytes)
                && payload.Fields[figure.Title].Text == original.Title && payload.Fields[figure.Caption].Text == original.Caption
                && payload.Fields[figure.Title].Source is { Kind: "figure-title" } title && title.Figure == original.Id;
        }));
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Check("native Word Results PNGs equal direct screen chart exports", screen.All(figure =>
            {
                string path = Path.Combine(output, "direct", figure.FileName);
                ChartImageExporter.Save(figure.Chart, path);
                return File.ReadAllBytes(path).SequenceEqual(native.Single(item => item.Figure.Id == "figure:" + figure.FileName).Figure.PngBytes);
            }));
        }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
        var omitFigure = HtmlReportFigures.Build(new(), document, new() { ExcludedIds = new[] { "figure:" + AnalyticsFigures.RecoveryFile } });
        Check("HTML omits unchecked figures and their fields", omitFigure.Results.All(figure => figure.SourceFigureId != "figure:" + AnalyticsFigures.RecoveryFile)
            && omitFigure.Fields.Values.All(field => field.Source.Figure != "figure:" + AnalyticsFigures.RecoveryFile));
        var omitQuality = HtmlReportFigures.Build(new(), document, new() { ExcludedIds = new[] { ReportSectionIds.Quality } });
        Check("HTML section exclusion includes its Results images", omitQuality.Quality.Count == 0
            && omitQuality.Results.All(figure => figure.SourceFigureId != "figure:" + AnalyticsFigures.XicByMassFile));

        var (manifest, project, _) = ReportStudyFigureFixture();
        project.FamilyPanel = null; project.PanelProteinAssignments = null; project.ProteinSubclassLabels = null;
        manifest.PresentationIdentitySweep = new() { new() { Identity = 80, Groups = 3, Score = 0 }, new() { Identity = 60, Groups = 2, Score = .1 } };
        manifest.DeNovoScoreDistribution = DeNovoScoreDistributionStudy.Build(Array.Empty<FamilyQuant.DeNovo.PeaksDeNovoRow>());
        var export = ReportProjectExporter.Build(rows, manifest, project, Path.Combine(output, "project-figures"), familyResults: families);
        string report = Path.Combine(output, "synthetic.docx");
        ReportProjectExporter.Write(export, report);
        string htmlPath = HtmlReportWriter.PathForReport(report);
        var actual = ReadHtmlPayload(htmlPath);
        Check("production HTML export includes all fixed Results images", imageNames.All(name => actual.Results.Any(figure => figure.SourceFigureId == "figure:" + name)));
        Check("production Word retains its contribution picture while HTML uses its interactive chart", export.Document.Sections.SelectMany(section => section.Blocks).OfType<ReportFigure>()
            .Any(figure => figure.Id == "figure:" + AnalyticsFigures.FamilyContributionFile) && actual.Results.All(figure => !figure.SourceFigureId.StartsWith("figure:family_contribution", StringComparison.Ordinal)));
        Check("production HTML export includes selected quality and sensitivity images", actual.Quality.Count > 0 && actual.Sensitivity.Count > 0);
        Check("coverage remains in the interactive maps rather than the figure grids", actual.Results.All(figure => !figure.SourceFigureId.StartsWith("figure:protein_coverage_", StringComparison.Ordinal))
            && actual.CoverageCatalog?.Proteins.Count > 0);
        Console.WriteLine("Synthetic project HTML size: " + new FileInfo(htmlPath).Length.ToString(CultureInfo.InvariantCulture) + " bytes; "
            + actual.Results.Count + " Results, " + actual.Quality.Count + " Quality, " + actual.Sensitivity.Count + " Sensitivity images.");
        HtmlReportBrowserChecks(Path.Combine(output, "page"), new[] { ("all-figures", actual, 390) }, """
            const checks={};
            for(const [name,host] of [['xic_by_mass.png','mass'],['isotopes_credited.png','mass'],['percent_ion_current_by_mass.png','mass'],['recovery.png','recovery']])
              checks[name]=!!d.getElementById(host+'-figures').querySelector('img[data-fq-figure="figure:'+name+'"]');
            checks.contribution=!!d.getElementById('family-contribution-chart')&&!d.querySelector('img[data-fq-figure^="figure:family_contribution"]');
            checks.quality=d.getElementById('quality-figures').querySelectorAll('img').length>0;
            checks.sensitivity=d.getElementById('sensitivity-figures').querySelectorAll('img').length>0;
            checks.inline=[...d.querySelectorAll('.figure-grid img')].every(img=>img.src.startsWith('data:image/png;base64,'));
            return checks;
            """);
    }
}
