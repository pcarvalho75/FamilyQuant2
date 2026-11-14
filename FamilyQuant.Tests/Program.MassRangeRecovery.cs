using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> MassRangeRecoveryCommands() =>
    [new("--test-mass-range-recovery", 10540, TestCommandPhase.Raw, args => args[0] == "--test-mass-range-recovery", (args, _) =>
    {
        string output = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "mass-range-recovery");
        MassRangeRecoveryTests(output);
        HtmlMassQualityTests(Path.Combine(output, "html"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void MassRangeRecoveryTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        double spacing = Ms1EnvelopeDetector.Carbon13Spacing;
        var scans = Enumerable.Range(0, 3).Select(i => Ms1Scan(i + 1, 10 + i * .1,
            (701.007276, 1000), (701.007276 + spacing, 400), (701.007276 + 2 * spacing, 150),
            (751.007276, 600), (751.007276 + spacing / 2, 300), (751.007276 + spacing, 100),
            (1001.007276, 800), (1001.007276 + spacing / 3, 300), (1001.007276 + 2 * spacing / 3, 100),
            (5001.007276, 500), (5001.007276 + spacing / 5, 200), (5001.007276 + 2 * spacing / 5, 50))).ToArray();
        var old = Ms1EnvelopeDetector.Detect("synthetic", scans, collectMassRanges: false);
        var current = Ms1EnvelopeDetector.Detect("synthetic", scans);
        Check("U6 detector charge areas are bit-identical with and without mass accumulation", old.DetectedAreaByCharge.Select(BitConverter.DoubleToInt64Bits)
            .SequenceEqual(current.DetectedAreaByCharge.Select(BitConverter.DoubleToInt64Bits)));
        Check("U6 detector counts, split areas and total retain every existing bit", old.DetectedCountByCharge.SequenceEqual(current.DetectedCountByCharge)
            && old.DetectedEnvelopeCount == current.DetectedEnvelopeCount && old.Ms1ScanCount == current.Ms1ScanCount
            && BitConverter.DoubleToInt64Bits(old.DetectedPeptideLikeArea) == BitConverter.DoubleToInt64Bits(current.DetectedPeptideLikeArea)
            && BitConverter.DoubleToInt64Bits(old.DetectedAreaSinglyCharged) == BitConverter.DoubleToInt64Bits(current.DetectedAreaSinglyCharged)
            && BitConverter.DoubleToInt64Bits(old.DetectedAreaMultiplyCharged) == BitConverter.DoubleToInt64Bits(current.DetectedAreaMultiplyCharged));
        Check("U6 detector integrates the known z1, z2, z3 and overflow envelope areas into neutral-mass ranges",
            current.DetectedAreaByNeutralMass is { } ranges && ranges[1] == 310 && ranges[3] == 200
            && ranges[6] == 240 && ranges[^1] == 150 && ranges.Sum() == 900 && current.NeutralMassBinWidth == 500);
        Check("U6 neutral-mass boundaries and open overflow range are explicit", NeutralMassRanges.Index(499.999) == 0
            && NeutralMassRanges.Index(500) == 1 && NeutralMassRanges.Index(19500) == 39 && NeutralMassRanges.Index(25000) == 39
            && NeutralMassRanges.Index(double.NaN) == -1 && NeutralMassRanges.Label(39) == "19,500 Da and above");

        var save = typeof(Ms1EnvelopeDetector).GetMethod("SaveCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        string legacyCache = Path.Combine(output, "legacy-detector.json"), currentCache = Path.Combine(output, "current-detector.json");
        save.Invoke(null, new object[] { legacyCache, old, "synthetic-fingerprint" });
        byte[] legacyBytes = File.ReadAllBytes(legacyCache);
        Check("U6 legacy detector cache shape has no new mass fields", !Encoding.UTF8.GetString(legacyBytes).Contains("neutralMass", StringComparison.OrdinalIgnoreCase));
        var cached = Ms1EnvelopeDetector.DetectRun(Path.Combine(output, "synthetic.raw"), legacyCache, sourceFingerprint: "synthetic-fingerprint");
        Check("U6 older cache opens without RAW and preserves exact old totals", cached.DetectedAreaByNeutralMass == null
            && cached.DetectedAreaByCharge.SequenceEqual(old.DetectedAreaByCharge) && cached.DetectedPeptideLikeArea == old.DetectedPeptideLikeArea);
        var fallback = Ms1EnvelopeDetector.DetectRun(Path.Combine(output, "synthetic.raw"), legacyCache,
            sourceFingerprint: "synthetic-fingerprint", requireMassRanges: true);
        Check("U6 missing RAW retains legacy totals and never rewrites a read-only old cache", fallback.DetectedAreaByNeutralMass == null
            && fallback.DetectedPeptideLikeArea == old.DetectedPeptideLikeArea && File.ReadAllBytes(legacyCache).SequenceEqual(legacyBytes));
        save.Invoke(null, new object[] { currentCache, current, "synthetic-fingerprint" });
        var cachedCurrent = Ms1EnvelopeDetector.DetectRun(Path.Combine(output, "synthetic.raw"), currentCache,
            sourceFingerprint: "synthetic-fingerprint", requireMassRanges: true);
        Check("U6 current cache persists every mass-range area exactly", cachedCurrent.DetectedAreaByNeutralMass!.Select(BitConverter.DoubleToInt64Bits)
            .SequenceEqual(current.DetectedAreaByNeutralMass!.Select(BitConverter.DoubleToInt64Bits)) && Ms1EnvelopeDetector.CacheVersion == 3);

        QuantRow Row(string side, string run, int scan, string sequence, double mass, params double[] areas) =>
            AnQr(side, run, scan, 2, sequence, mass / 2 + AnalyticsBuilder.ProtonMass, areas);
        var rows = new[]
        {
            Row("identified", "range_a", 1, "ACDIK", 1200, 60, 40),
            Row("denovo", "range_a", 2, "ACDLK", 1200 + spacing, 50, 40),
            Row("denovo", "range_a", 3, "VVVVK", 1400, 12, 8),
            Row("identified", "range_b", 4, "GGGGK", 1200, 20, 10),
            Row("denovo", "range_a", 5, "TTTTK", 2510, 30, 20),
            Row("identified", "range_a", 6, "AAAAK", 3510, 6, 4),
            Row("identified", "range_a", 7, "SSSSK", 500, 3, 2),
            Row("identified", "range_a", 8, "MMMMK", 25000, 3, 2),
        };
        var data = AnalyticsBuilder.Build(rows, new Dictionary<string, double> { ["range_a"] = 1e6, ["range_b"] = 2e6 }, 6);
        var manifest = new QuantManifest { AppVersion = "2.1.14", DerivedMethod = NativeQuantFractions.Method, IsotopeCount = 6,
            PerRunTic = new() { ["range_a"] = 1e6, ["range_b"] = 2e6 },
            DerivedFractions = data.Fractions.Select(f => new DerivedRunFraction { Run = f.Run, Tic = f.Tic,
                DetectedPeptideLikeArea = old.DetectedPeptideLikeArea, DetectedAreaByCharge = old.DetectedAreaByCharge,
                DetectedCountByCharge = old.DetectedCountByCharge }).ToList() };
        string legacyProject = Path.Combine(output, "synthetic-2.1.14.fqproj"), roundTrip = Path.Combine(output, "roundtrip.fqproj");
        ProjectStore.WriteFqproj(legacyProject, new FamilyClusterFile(), rows, manifest);
        using (var zip = ZipFile.OpenRead(legacyProject))
        using (var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open()))
            Check("U6 synthetic 2.1.14 project omits new optional fields", !reader.ReadToEnd().Contains("DetectedAreaByNeutralMass", StringComparison.OrdinalIgnoreCase));
        byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(legacyProject));
        var loaded = ProjectStore.ReadFqproj(legacyProject);
        ProjectStore.WriteFqproj(roundTrip, loaded.fcf, loaded.quant, loaded.manifest);
        var reloaded = ProjectStore.ReadFqproj(roundTrip);
        Check("U6 2.1.14 project opens and round-trips old quantitation and detector totals", reloaded.manifest.AppVersion == "2.1.14"
            && reloaded.quant.Count == rows.Length && reloaded.quant.Zip(rows).All(pair => Enumerable.Range(0, 6)
                .All(i => BitConverter.DoubleToInt64Bits(pair.First.IsotopeAreas.ElementAtOrDefault(i))
                    == BitConverter.DoubleToInt64Bits(pair.Second.IsotopeAreas.ElementAtOrDefault(i))))
            && reloaded.manifest.DerivedFractions.All(d => d.DetectedAreaByNeutralMass == null && d.NeutralMassBinWidth == null
                && d.DetectedPeptideLikeArea == old.DetectedPeptideLikeArea && d.DetectedAreaByCharge.SequenceEqual(old.DetectedAreaByCharge))
            && SHA256.HashData(File.ReadAllBytes(legacyProject)).SequenceEqual(sourceHash));
        NativeQuantFractions.ApplyDetectedToFractions(data.Fractions, data.Grouped, reloaded.manifest.DerivedFractions);
        var missing = MassRangeRecoveryData.Build(data);
        Check("U6 legacy native and HTML views show the exact recompute message, never invented zeros", !missing.Available && missing.Bins.Count == 0
            && new PercentIonCurrentChart(data.Peptides, recovery: missing).Data.Message == MassRangeRecoveryData.MissingMessage
            && Encoding.UTF8.GetString(HtmlReportWriter.BuildBytes(new HtmlReportPayload { MassQualityCharts = new() { IonCurrentByMass = missing } }))
                .Contains(MassRangeRecoveryData.MissingMessage, StringComparison.Ordinal));
        var legacyPayload = new HtmlReportPayload { MassQualityCharts = new() { IonCurrentByMass = missing } };
        HtmlReportBrowserChecks(Path.Combine(output, "legacy-html"), new[] { ("phone", legacyPayload, 390), ("desktop", legacyPayload, 1366) }, """
            const d = frame.contentDocument;
            d.getElementById('tab-mass').click();
            const chart = d.querySelector('[data-native-chart="percent_ion_current_by_mass.png"]');
            return { missingMessage: chart.textContent.includes('Recovery by mass range needs Quantify with this version: run Quantify again.'),
                noInventedBars: chart.querySelectorAll('.u3b-row').length === 0 };
            """);

        var a = new double[40]; a[1] = 10; a[2] = 200; a[5] = 20; a[39] = 50;
        var b = new double[40]; b[2] = 100;
        NativeQuantFractions.ApplyDetectedToManifest(manifest, new Dictionary<string, Ms1EnvelopeDetectionResult>
        {
            ["range_a"] = new() { DetectedAreaByNeutralMass = a, NeutralMassBinWidth = 500 },
            ["range_b"] = new() { DetectedAreaByNeutralMass = b, NeutralMassBinWidth = 500 },
        });
        NativeQuantFractions.ApplyDetectedToFractions(data.Fractions, data.Grouped, manifest.DerivedFractions);
        var recovery = MassRangeRecoveryData.Build(data);
        var byRange = recovery.Bins.ToDictionary(bin => bin.Index);
        Check("U6 range recovery pools numerator and denominator across both runs", recovery.Available && recovery.RunCount == 2
            && byRange[2].PeptideEnvelope == 150 && byRange[2].DetectedEnvelope == 300 && byRange[2].Recovery == .5);
        Check("U6 production I/L and isotope-offset equivalence suppress duplicate de novo signal", recovery.Bins.Sum(bin => bin.PeptideEnvelope)
            == data.Fractions.Sum(f => f.IdentifiedEnvelope + f.DeNovoEnvelope) && data.Fractions.Sum(f => f.DeNovoSuppressedCrossSideOverlaps) == 1);
        Check("U6 independent detector ratios above 100 percent remain visible", byRange[5].Recovery == 2.5 && recovery.AxisMaximum == 2.5);
        Check("U6 zero detector denominator is unavailable, distinct from zero recovery", byRange[7].Recovery == null
            && byRange[3].Recovery == null && byRange[1].Recovery == .5 && byRange[39].Recovery == .1);
        Check("U6 native chart and HTML projection share identical areas, ratios and bases", JsonSerializer.Serialize(recovery)
            == JsonSerializer.Serialize(new PercentIonCurrentChart(data.Peptides, recovery: recovery).Data));
        string caption = CompositionChartCaptions.Build(new AnalyticsFigure(AnalyticsFigures.PercentIonCurrentFile,
            new PercentIonCurrentChart(data.Peptides, recovery: recovery), 300), data);
        Check("U6 results and report caption describes detector recovery, not TIC-share bars",
            caption.Contains("Isotope envelopes (measured)", StringComparison.Ordinal)
            && caption.Contains("not a share of TIC", StringComparison.Ordinal)
            && !caption.Contains("outlined bars", StringComparison.Ordinal)
            && !caption.Contains("Represented share of recorded total ion current", StringComparison.Ordinal));
        data.Fractions[1].DetectedAreaByNeutralMass = null;
        Check("U6 one incomplete run blocks the pooled view", !MassRangeRecoveryData.Build(data).Available);
        data.Fractions[1].DetectedAreaByNeutralMass = b;
        ProjectStore.WriteFqproj(Path.Combine(output, "current-ranges.fqproj"), loaded.fcf, rows, manifest);
        var saved = ProjectStore.ReadFqproj(Path.Combine(output, "current-ranges.fqproj"));
        Check("U6 current project persists each run's mass bins bit for bit", saved.manifest.DerivedFractions[0].DetectedAreaByNeutralMass!.Select(BitConverter.DoubleToInt64Bits)
            .SequenceEqual(a.Select(BitConverter.DoubleToInt64Bits)) && saved.manifest.DerivedFractions[1].DetectedAreaByNeutralMass!.SequenceEqual(b));
        foreach (var theme in new[] { ChartPaletteVariant.Light, ChartPaletteVariant.Dark })
            WriteChartPng(ChartImageExporter.Render(new PercentIonCurrentChart(data.Peptides, recovery: recovery), 1100, 380, 1, theme),
                Path.Combine(output, "U6-native-" + theme + ".png"));
    }
}
