using FamilyQuant.Report;
using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> J34dCommands() =>
    [new("--test-j34d", 10491, TestCommandPhase.Raw,
        args => args.Length == 1 && args[0] == "--test-j34d", (_, _) =>
        {
            J34dTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void J34dTests()
    {
        Section("J34d real report reader regressions");
        EnsureApplication();
        var data = AnalyticsBuilder.Build(SyntheticAnalyticsRows(), new Dictionary<string, double>());
        var manifest = new QuantManifest { IdentificationConcordance = new IdentificationConcordanceSummary { PatternLabPsmRows = 15 } };
        var plan = ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), null);
        Check("zero comparable pairs never render an affirmative concordance graphic",
            !plan.Charts.Any(c => c.FileName == PresentationFigures.DatabaseSearchConcordanceFile));
        Check("no-comparable-pairs state prints the recorded audit counts",
            plan.Unavailable.Any(item => item.Title == "Database search concordance"
                && item.Reason.Contains("No precursor-qualified comparable pairs", StringComparison.Ordinal)
                && item.Reason.Contains("15", StringComparison.Ordinal)
                && item.Reason.Contains("0", StringComparison.Ordinal)));
        manifest.IdentificationConcordance = new IdentificationConcordanceSummary
        {
            SharedDatabaseSearchScanKeys = 2, SameChargeDatabaseSearchScanKeys = 2,
            PrecursorQualifiedDatabaseSearchScanKeys = 2, PrecursorQualifiedDatabaseSearchPairs = 2,
            ExactPrecursorQualifiedDatabaseSearchPairs = 1, AdditionalIleLeuEquivalentPrecursorQualifiedPairs = 1,
        };
        Check("a supported concordance study retains its original native graphic",
            ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), null).Charts
                .Single(c => c.FileName == PresentationFigures.DatabaseSearchConcordanceFile).CreateChart() is DatabaseSearchConcordanceChart);
        var families = FamilyFixture(3);
        families.SourceSequenceSummary.Families.Clear();
        families.SourceSequenceSummary.Families.Add(new()
        {
            FamilyUserId = families.Families[0].FamilyUserId, Description = "Synthetic search-only census row", SearchDistinctSequences = 17,
        });
        var sourcePlan = ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), families);
        Check("unavailable disjoint source sets do not occupy an empty source-census figure",
            !sourcePlan.Charts.Any(c => c.FileName.StartsWith("family_source_sequence_counts", StringComparison.Ordinal)));
        Check("source-set availability is explicit without claiming all de novo evidence is absent",
            sourcePlan.Unavailable.Any(item => item.Title == "Family source sequence counts"
                && item.Reason.Contains("PEAKS de-novo-only and DeepNovo", StringComparison.Ordinal)
                && item.Reason.Contains("Other de novo sources are separate", StringComparison.Ordinal)));
        Check("unavailable source graphic retains the original complete census and values in the workbook",
            ReportCurrentResults.Build(data, manifest, families).Sections.SelectMany(s => s.Blocks).OfType<ReportTable>()
                .Any(t => t.Id == "family-source-sequences" && t.SupplementOnly && t.Rows.Count == 1 && t.Rows[0].Contains("17")));
        families.SourceSequenceSummary.Families[0].DeNovoUnionDistinctSequences = 2;
        families.SourceSequenceSummary.Families[0].DeepNovoExclusiveDistinctSequences = 2;
        Check("nonempty source sets retain their existing source-census drawing",
            ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), families).Charts
                .Single(c => c.FileName == PresentationFigures.FamilySourceSequenceFile).CreateChart() is ReportFamilySourceSequenceChart);
        J34dConcordanceTraceTests(data);
        J34dRunChartTests(data);
    }

    private static void J34dConcordanceTraceTests(AnalyticsData data)
    {
        // Deliberately unique counts: incidental equality with another table cell cannot pass this regression.
        var manifest = new QuantManifest { IdentificationConcordance = new IdentificationConcordanceSummary
            { PatternLabPsmRows = 123457, PeaksDatabasePsmRows = 234569 } };
        var packet = ReportCurrentResults.Build(data, manifest, null);
        var audit = packet.Sections.SelectMany(s => s.Blocks).OfType<ReportTable>().Single(t => t.Id == "quality-concordance");
        Check("zero-pair audit retains both source PSM counts as native integer cells without rate denominators",
            audit.Rows.Any(r => r[1] == "patternlab_psm_rows" && r[2] == "123457" && r[3] == "")
            && audit.Rows.Any(r => r[1] == "peaks_database_psm_rows" && r[2] == "234569" && r[3] == ""));
        string csv = packet.CsvExports["database_search_concordance.csv"];
        Check("additional source audit counts preserve every original concordance CSV row byte for byte",
            csv.StartsWith(IdentificationConcordanceStudy.BuildCsv(manifest.IdentificationConcordance), StringComparison.Ordinal));
        var missing = ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), null).Unavailable.Single(x => x.Title == "Database search concordance");
        var document = new ReportDocument { Sections = new[] { new ReportSection(ReportSectionIds.Quality, "Quality",
            new ReportBlock[] { new ReportParagraph("no-comparable-pairs", missing.Reason) }) } };
        string folder = Path.Combine(Path.GetTempPath(), "j34d-concordance-trace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string word = Path.Combine(folder, "audit.docx"), output = Path.Combine(folder, "trace.csv");
        WordReportWriter.Write(word, document);
        ReportSupplementWorkbook.Write(ReportSupplementWorkbook.PathForReport(word), document,
            new[] { new ReportSupplementSheet(audit.Id, ReportSectionIds.Quality, "Concordance", audit) });
        File.WriteAllText(Path.Combine(folder, "database_search_concordance.csv"), csv);
        Check("the no-comparable-pairs paragraph passes the unchanged J18 tracer against its native audit cells",
            ReportNumberTrace.Run(word, folder, output) == 0);
    }

    private static void J34dRunChartTests(AnalyticsData data)
    {
        data.Fractions.Clear();
        for (int index = 0; index < 4; index++) data.Fractions.Add(new NativeRunFraction
        {
            Run = "synthetic_run_" + index, Tic = index == 3 ? 0 : 1000,
            IdentifiedEnvelope = 350, DeNovoEnvelope = 210, DeNovoEnvelopeReferenceCalibrated = 210,
        });
        var charts = ReportFigureRenderer.Plan(data, new QuantManifest(), new FamilyClusterFile(), null).Charts
            .Where(c => c.FileName.StartsWith("signal_by_run", StringComparison.Ordinal)).ToArray();
        Check("run chart pagination keeps all runs in their original order with room for labels", charts.Length == 2);
        foreach (var theme in new[] { ChartPaletteVariant.Light, ChartPaletteVariant.Dark })
        {
            var text = new List<string>();
            foreach (var chart in charts)
            {
                var native = chart.CreateChart();
                ChartImageExporter.Save(native, Path.Combine(Path.GetTempPath(), "j34d-run", theme.ToString(), chart.FileName),
                    chart.Width, chart.Height, variant: theme);
                var glyphs = ReaderGlyphs(VisualTreeHelper.GetDrawing(native)).ToArray();
                text.AddRange(glyphs.Select(g => new string(g.GlyphRun.Characters.ToArray())));
                double pointsPerDip = Math.Min(468 / chart.Width, 360 / chart.Height);
                Check(theme + " " + chart.FileName + " text remains readable and inside the drawing",
                    glyphs.Length > 0 && glyphs.All(g => g.GlyphRun.FontRenderingEmSize * pointsPerDip >= 10
                        && g.Bounds.Left >= 0 && g.Bounds.Top >= 0 && g.Bounds.Right <= chart.Width && g.Bounds.Bottom <= chart.Height));
                var totals = glyphs.Where(g => new string(g.GlyphRun.Characters.ToArray()).StartsWith("total ", StringComparison.Ordinal)).ToArray();
                var header = glyphs.Where(g => g.Bounds.Top < 100).ToArray();
                Check(theme + " " + chart.FileName + " run totals clear every header label",
                    totals.All(total => header.All(label => !total.Bounds.IntersectsWith(label.Bounds))));
            }
            Check(theme + " native run values and unavailable TIC are explicit",
                text.Any(t => t.Contains("56.00%", StringComparison.Ordinal))
                && text.Any(t => t.Contains("35.00%", StringComparison.Ordinal))
                && text.Any(t => t.Contains("21.00%", StringComparison.Ordinal))
                && text.Any(t => t.Contains("Run 4", StringComparison.Ordinal))
                && text.Any(t => t.Contains("TIC unavailable", StringComparison.Ordinal)));
        }
    }
}
