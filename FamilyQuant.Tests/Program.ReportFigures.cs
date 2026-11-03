using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ReportFigureTests(string output)
    {
        Section("Report figures - native Light PNGs, availability and stored rates");
        EnsureApplication();
        Directory.CreateDirectory(output);
        var data = AnalyticsBuilder.Build(SyntheticAnalyticsRows(), new Dictionary<string, double> { ["ui_01"] = 5e8, ["ui_02"] = 4e8 });
        var manifest = new QuantManifest();
        var curation = new FamilyClusterFile();
        var families = FamilyFixture(3, unattributedIdentified: 4000);
        var plan = ReportFigureRenderer.Plan(data, manifest, curation, families);
        Check("all five analytics figure exports are included", AnalyticsFigures.FileNames.All(name => plan.Charts.Any(c => c.FileName == name)));
        Check("uncomputed studies remain explicit", plan.Unavailable.Any(s => s.Title == "De novo score distributions")
            && plan.Unavailable.Any(s => s.Title == "Family-world error rates") && plan.Unavailable.Any(s => s.Title == "Database gap"));
        Check("figure ids and filenames do not collide", plan.Charts.Select(c => c.FileName).Distinct(StringComparer.Ordinal).Count() == plan.Charts.Count);
        manifest.PresentationIdentitySweep = new() { new() { Identity = 80, Groups = 3, Score = 0 }, new() { Identity = 60, Groups = 2, Score = .1 } };
        var withSweep = ReportFigureRenderer.Plan(data, manifest, curation, families);
        Check("presentation identity figure uses its own existing PNG chart", withSweep.Charts.Single(c => c.FileName == PresentationFigures.IdentitySweepFile).CreateChart() is ReferenceIdentitySweepChart);
        var oldCulture = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            var rendered = ReportFigureRenderer.Render(plan, Path.Combine(output, "pt"));
            Check("rendering restores both caller cultures", CultureInfo.CurrentCulture.Name == "pt-BR" && CultureInfo.CurrentUICulture.Name == "pt-BR");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            var english = ReportFigureRenderer.Render(plan, Path.Combine(output, "en"));
            Check("all report PNG bytes equal under pt-BR and en-US", rendered.Zip(english).All(pair => pair.First.Figure.PngBytes.SequenceEqual(pair.Second.Figure.PngBytes)));
            bool equal = true;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            foreach (var chart in plan.Charts)
            {
                string path = Path.Combine(output, "direct", chart.FileName);
                ChartImageExporter.Save(chart.CreateChart(), path, chart.Width, chart.Height, variant: ChartPaletteVariant.Light);
                equal &= File.ReadAllBytes(path).SequenceEqual(rendered.Single(f => f.Figure.Id == "figure:" + chart.FileName).Figure.PngBytes);
            }
            Check("every report figure equals the direct program PNG export", equal);
            var doc = new ReportDocument { Title = "Synthetic native figure fixture",
                Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", rendered.Select(f => (ReportBlock)f.Figure).ToArray()) } };
            byte[] bytes = WordReportWriter.BuildBytes(doc);
            File.WriteAllBytes(Path.Combine(output, "native-figures.docx"), bytes);
            using var zip = new ZipArchive(new MemoryStream(bytes));
            var embedded = zip.Entries.Where(e => e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).Select(e =>
            { using var image = e.Open(); using var memory = new MemoryStream(); image.CopyTo(memory); return memory.ToArray(); }).ToList();
            Check("every embedded image matches its native PNG byte for byte", embedded.Count == rendered.Count
                && rendered.All(f => embedded.Any(b => b.SequenceEqual(f.Figure.PngBytes))));
        }
        finally { CultureInfo.CurrentCulture = oldCulture; CultureInfo.CurrentUICulture = oldUi; }
        manifest.FamilyWorldResult = new FamilyWorldSummary { MinimumTagLength = FamilyWorldOptions.DefaultMinimumTagLength,
            Families = new() { new() { Family = "Synthetic stored family", DecoyRate = .05, RightRate = null, KnownCredited = 2 } } };
        var worldPlan = ReportFigureRenderer.Plan(data, manifest, curation, families);
        var rateChart = (ReportFamilyWorldRatesChart)worldPlan.Charts.Single(c => c.FileName == "family_world_rates_1.png").CreateChart();
        Check("world rate chart retains recorded values and null availability", rateChart.Rows.Single().DecoyRate == .05 && rateChart.Rows.Single().RightRate == null);
        oldCulture = CultureInfo.CurrentCulture; oldUi = CultureInfo.CurrentUICulture;
        try
        {
            var ratePlan = new ReportFigurePlan(worldPlan.Charts.Where(c => c.FileName.StartsWith("family_world_rates_", StringComparison.Ordinal)).ToArray(), Array.Empty<ReportFigureAvailability>());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            byte[] ratePt = ReportFigureRenderer.Render(ratePlan, Path.Combine(output, "rates-pt")).Single().Figure.PngBytes;
            string directPt = ChartImageExporter.Save(new ReportFamilyWorldRatesChart(rateChart.Rows), Path.Combine(output, "rates-direct-pt.png"), variant: ChartPaletteVariant.Light);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            byte[] rateEn = ReportFigureRenderer.Render(ratePlan, Path.Combine(output, "rates-en")).Single().Figure.PngBytes;
            string directEn = ChartImageExporter.Save(new ReportFamilyWorldRatesChart(rateChart.Rows), Path.Combine(output, "rates-direct-en.png"), variant: ChartPaletteVariant.Light);
            Check("actual stored-rate report PNG is byte-equal under pt-BR and en-US", ratePt.SequenceEqual(rateEn));
            Check("standalone stored-rate chart export is culture invariant", File.ReadAllBytes(directPt).SequenceEqual(File.ReadAllBytes(directEn)));
        }
        finally { CultureInfo.CurrentCulture = oldCulture; CultureInfo.CurrentUICulture = oldUi; }
        manifest.DeNovoSourceScoreDistributions.Add(new() { SourceId = "incomplete", RawRows = 1 });
        bool invalid = false;
        try { ReportFigureRenderer.Plan(data, manifest, curation, families); } catch (InvalidOperationException) { invalid = true; }
        Check("an invalid stored study fails instead of disappearing", invalid);
    }
}
