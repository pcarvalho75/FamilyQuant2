using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> MeasuredRecoveryChartCommands() =>
    [new("--test-measured-recovery-chart", 100, TestCommandPhase.Raw,
        args => args[0] == "--test-measured-recovery-chart", (args, _) =>
        {
            string output = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "measured-recovery-chart");
            MeasuredRecoveryChartTests(output);
            RecoveryChartGeometryTests(output);
            ResultsTooltipTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void MeasuredRecoveryChartTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var fractions = new[] { (Run: "run01", Tic: 1000d), (Run: "run02", Tic: 2000d) }.Select(r => new NativeRunFraction
        {
            Run = r.Run, Tic = r.Tic, IdentifiedAreaM0 = r.Tic * .1, DeNovoAreaM0 = r.Tic * .05,
            IdentifiedEnvelope = r.Tic * .3, DeNovoEnvelope = r.Tic * .2,
            DeNovoEnvelopeReferenceCalibrated = r.Tic * .9,
            IdentifiedPeptides = 2, DeNovoPeptides = 1,
            DetectedPeptideLikeArea = r.Tic * .7, DetectedAreaMultiplyCharged = r.Tic * .6,
            DetectedAreaSinglyCharged = r.Tic * .1, DetectedEnvelopeCount = 3,
            DetectedCountMultiplyCharged = 2, DetectedCountSinglyCharged = 1,
        }).ToList();
        foreach (var variant in new[] { ChartPaletteVariant.Light, ChartPaletteVariant.Dark })
        {
            var chart = new RecoveryChart(fractions, isotopeCeiling: 6);
            string path = Path.Combine(output, "measured-recovery-" + variant + ".png");
            ChartImageExporter.Save(chart, path, layoutWidth: 1100, layoutHeight: 600, scale: 1, variant: variant);
            var rectangles = new List<System.Windows.Rect>();
            void Find(Drawing drawing)
            {
                if (drawing is DrawingGroup group) foreach (Drawing child in group.Children) Find(child);
                else if (drawing is GeometryDrawing { Brush: not null, Geometry: RectangleGeometry rectangle }
                    && rectangle.Rect.Top >= 46 && rectangle.Rect.Bottom < 300)
                    rectangles.Add(rectangle.Rect);
            }
            Find(VisualTreeHelper.GetDrawing(chart));
            Check("T4 " + variant + " render draws exactly two recovery readings for each run and the pooled row",
                rectangles.Count == 6, rectangles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Check("T4 " + variant + " rendered widths use M+0 and measured envelope despite legacy model fields",
                rectangles.Count == 6 && Enumerable.Range(0, 3).All(i =>
                    Math.Abs(rectangles[i * 2].Width / rectangles[i * 2 + 1].Width - .3) < 1e-10));
            Check("T4 " + variant + " legend gives each reading its own plain line and keeps the detector caveat",
                chart.LegendLines[0].StartsWith("M+0 only:", StringComparison.Ordinal)
                && chart.LegendLines[1].StartsWith("Measured envelope (M+0 to M+5):", StringComparison.Ordinal)
                && chart.LegendLines.Count == 4 && chart.ToolTip.ToString()!.Contains("not peptide-FDR calibrated", StringComparison.Ordinal));
        }
        Check("T4 the ceiling label describes the recorded window rather than the number of nonzero peaks",
            new RecoveryChart(fractions, isotopeCeiling: 3).EnvelopeLabel == "Measured envelope (M+0 to M+2)"
            && new RecoveryChart(fractions, isotopeCeiling: 1).EnvelopeLabel == "Measured envelope (M+0)"
            && new RecoveryChart(fractions).EnvelopeLabel.Contains("not recorded", StringComparison.Ordinal));

        var rows = new[] { new QuantRow { Run = "run01", Side = "identified", Scan = 1, Sequence = "PEPTIDEK",
            Charge = 2, PrecursorMz = 500, IsotopeAreas = new[] { 10d, 5d, 2d, 0d, 0d, 0d }, EnvelopeSum = 17 } };
        var manifest = new QuantManifest { DerivedMethod = NativeQuantFractions.Method, IsotopeCount = 3,
            PerRunTic = new() { ["run01"] = 1000 } };
        string project = Path.Combine(output, "three-isotopes.fqproj");
        ProjectStore.WriteFqproj(project, new FamilyClusterFile(), rows, manifest);
        var reopened = ProjectStore.ReadFqproj(project);
        var model = new AnalyticsViewModel(reopened.quant, reopened.manifest);
        Check("T4 Results carries the manifest ceiling through six-slot project storage",
            reopened.quant[0].IsotopeAreas.Length == 6 && model.Data!.IsotopeCeiling == 3);
        var figure = AnalyticsFigures.Build(model.Data!, null).Single(f => f.Chart is RecoveryChart);
        Check("T4 native Results figures receive the recorded ceiling", ((RecoveryChart)figure.Chart).IsotopeCeiling == 3);
        var data = AnalyticsBuilder.Build(rows, manifest.PerRunTic, 6);
        var plan = ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), null);
        var reportChart = (RecoveryChart)plan.Charts.Single(c => c.FileName == AnalyticsFigures.RecoveryFile).CreateChart();
        Check("T4 HTML recovery uses the manifest ceiling even when supplied analytics metadata differs",
            reportChart.IsotopeCeiling == 3 && reportChart.EnvelopeLabel.Contains("M+2", StringComparison.Ordinal));
        Check("T4 analytics without recorded metadata never infers a ceiling from padded areas",
            AnalyticsBuilder.Build(rows, manifest.PerRunTic).IsotopeCeiling == null);
    }
}
