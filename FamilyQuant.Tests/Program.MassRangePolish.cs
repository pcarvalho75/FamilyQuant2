using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> MassRangePolishCommands() =>
    [new("--test-mass-range-polish", 10550, TestCommandPhase.Raw, args => args[0] == "--test-mass-range-polish", (args, _) =>
    {
        MassRangePolishTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "mass-range-polish"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void MassRangePolishTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        QuantRow Row(int scan, string sequence, double mass, double area) =>
            AnQr("identified", "polish_run", scan, 2, sequence, mass / 2 + AnalyticsBuilder.ProtonMass, new[] { area });
        var rows = new[]
        {
            Row(1, "AAAAK", 1200, 100), Row(2, "VVVVK", 2200, 30), Row(3, "GGGGK", 3200, 125),
            Row(4, "TTTTK", 650, 10), Row(5, "SSSSK", 4200, 10), Row(6, "DDDDK", 2700, 10),
        };
        var data = AnalyticsBuilder.Build(rows, new Dictionary<string, double> { ["polish_run"] = 10000 }, 6);
        var areas = new double[NeutralMassRanges.Count]; areas[2] = 200; areas[3] = 10; areas[4] = 20; areas[6] = 50;
        data.Fractions[0].DetectedAreaByNeutralMass = areas;
        data.Fractions[0].NeutralMassBinWidth = NeutralMassRanges.Width;
        var view = MassRangeRecoveryData.Build(data);
        Check("U6-polish native and HTML span only the first through last detector ranges", view.Bins.Select(b => b.Index).SequenceEqual(new[] { 2, 3, 4, 5, 6 })
            && view.Bins[0].Label == "1,000 to under 1,500 Da" && view.Bins[^1].Label == "3,000 to under 3,500 Da");
        Check("U6-polish distinguishes zero peptide signal from an internal detector gap", view.Bins[1].DetectedEnvelope == 10
            && view.Bins[1].Recovery == 0 && view.Bins[3].PeptideEnvelope == 10 && view.Bins[3].Recovery == null);
        Check("U6-polish range trimming does not change any measured area or defined ratio", view.Bins[0].PeptideEnvelope == 100
            && view.Bins[0].DetectedEnvelope == 200 && view.Bins[0].Recovery == .5
            && view.Bins[2].PeptideEnvelope == 30 && view.Bins[2].DetectedEnvelope == 20 && view.Bins[2].Recovery == 1.5
            && view.Bins[4].PeptideEnvelope == 125 && view.Bins[4].DetectedEnvelope == 50 && view.Bins[4].Recovery == 2.5);
        Check("U6-polish axis uses 0, 50, 100, 150, 200 and 250 percent", view.TickStep == .5 && view.AxisMaximum == 2.5
            && Enumerable.Range(0, 6).Select(i => 100 * i * view.TickStep).SequenceEqual(new double[] { 0, 50, 100, 150, 200, 250 }));
        Check("U6-polish explains detector gaps once in the caption", view.Legend.Split("blank ranges have no detector signal", StringSplitOptions.None).Length == 2);
        var native = new PercentIonCurrentChart(data.Peptides, recovery: view);
        Check("U6-polish native chart uses the shared trimmed ranges and axis", JsonSerializer.Serialize(native.Data) == JsonSerializer.Serialize(view));
        foreach (var theme in new[] { ChartPaletteVariant.Light, ChartPaletteVariant.Dark })
        foreach (int width in new[] { 1366, 390 })
        {
            var chart = new PercentIonCurrentChart(data.Peptides, recovery: view);
            WriteChartPng(ChartImageExporter.Render(chart, width, 400, 1, theme), Path.Combine(output, "U6-polish-native-" + theme + "-" + width + ".png"));
            var drawing = VisualTreeHelper.GetDrawing(chart);
            var text = new List<string>();
            void Read(Drawing item)
            {
                if (item is GlyphRunDrawing glyphs && glyphs.GlyphRun.Characters != null) text.Add(new string(glyphs.GlyphRun.Characters.ToArray()));
                if (item is DrawingGroup group) foreach (Drawing child in group.Children) Read(child);
            }
            if (drawing != null) Read(drawing);
            string painted = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", text), @"\s+", " ").Trim();
            File.WriteAllText(Path.Combine(output, "U6-polish-native-" + theme + "-" + width + ".txt"), painted);
            // Glyph runs can split a word, so layout whitespace is not part of the text contract.
            string characters = painted.Replace(" ", string.Empty, StringComparison.Ordinal);
            Check("U6-polish " + theme + " " + width + " paints round ticks, one gap explanation and no per-bar n/a", painted.Contains("50%", StringComparison.Ordinal)
                && !painted.Contains("62.5%", StringComparison.Ordinal) && !painted.Contains("187.5%", StringComparison.Ordinal)
                && !painted.Contains("n/a", StringComparison.Ordinal) && characters.Contains("Blankrangeshavenodetectorsignal;", StringComparison.Ordinal)
                && characters.Contains("baselinemarksshowzeropeptidesignal.", StringComparison.Ordinal), painted);
        }
        var payload = new HtmlReportPayload { MassQualityCharts = new() { IonCurrentByMass = view } };
        HtmlReportBrowserChecks(Path.Combine(output, "browser"), new[] { ("phone", payload, 390), ("desktop", payload, 1366) }, """
            const d = frame.contentDocument;
            d.getElementById('tab-mass').click();
            const chart = d.querySelector('[data-native-chart="percent_ion_current_by_mass.png"]');
            const labels = [...chart.querySelectorAll('.u3b-row-name')].map(n => n.textContent);
            return { sameRangeBounds: labels.length === 5 && labels[0] === '1,000 to under 1,500 Da' && labels[4] === '3,000 to under 3,500 Da',
                sameDefinedRatios: [...chart.querySelectorAll('.u3b-row-value')].map(n => n.textContent).join('|') === '50.0%|under 0.1%|150.0%|Unavailable|250.0%',
                gapCaption: chart.textContent.includes('blank ranges have no detector signal'),
                fitsViewport: d.documentElement.scrollWidth <= d.documentElement.clientWidth + 1 };
            """);
        Array.Clear(areas);
        var empty = MassRangeRecoveryData.Build(data);
        Check("U6-polish zero detector signal has an explicit empty state, not forty undefined ranges", empty.Available && empty.Bins.Count == 0
            && empty.Message == "No detector envelope signal was recorded.");
    }
}
