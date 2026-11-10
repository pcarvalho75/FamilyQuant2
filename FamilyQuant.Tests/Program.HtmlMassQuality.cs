using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlMassQualityCommands() =>
    [new("--test-html-mass-quality", 10530, TestCommandPhase.Raw, args => args[0] == "--test-html-mass-quality", (args, _) =>
    {
        HtmlMassQualityTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "html-mass-quality"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static QuantManifest HtmlMassQualityManifest()
    {
        PeaksDeNovoRow Call(int scan, string sequence, DeNovoSource source, double score = 95) => new()
        {
            RunName = "synthetic_run", Scan = scan, Charge = 2, Mz = 500, Mass = 998, Peptide = sequence,
            Length = sequence.Length, ConfidenceValue = score, ConfidenceField = source.ConfidenceField,
            SourceId = source.Id, SourceDisplayName = source.DisplayName, LocalConfidence = Enumerable.Repeat(99, sequence.Length).ToArray(),
        };
        var all = new[] { Call(1, "ACDEFGHIK", DeNovoSource.DeNovoAll), Call(2, "PEPTIDER", DeNovoSource.DeNovoAll), Call(3, "VVVVVVVK", DeNovoSource.DeNovoAll, 60) };
        var only = new[] { Call(2, "PEPTIDER", DeNovoSource.DenovoOnly) };
        var deep = new[] { Call(1, "ACDEFGHIK", DeNovoSource.DeepNovo), Call(2, "PEPTLDER", DeNovoSource.DeepNovo), Call(4, "TTTTTTTK", DeNovoSource.DeepNovo, 70) };
        var psms = new[] { new PatternLabPsm { FileName = "synthetic_run.raw", ScanNumber = 1, ChargeState = 2, PrecursorMz = 500, Sequence = "ACDEFGHIK", XCorr = 5 } };
        var criteria = new DeNovoFilterCriteria();
        var accepted = all.Concat(only).Concat(deep).Where(r => r.ConfidenceValue >= 80).ToArray();
        var selections = new[] { new DeNovoSourceSelection(DeNovoSource.DeNovoAll, 80, 0), new DeNovoSourceSelection(DeNovoSource.DenovoOnly, 80, 1), new DeNovoSourceSelection(DeNovoSource.DeepNovo, 80, 2) };
        var union = DeNovoUnionResolver.Resolve(accepted, selections);
        var reconciled = DeNovoReconciler.Reconcile(union, psms);
        var scores = new[] { (Source: DeNovoSource.DeNovoAll, Rows: all), (Source: DeNovoSource.DenovoOnly, Rows: only), (Source: DeNovoSource.DeepNovo, Rows: deep) }
            .Select(s => DeNovoSourceScoreDistributionStudy.Build(s.Rows, new DeNovoSourceSelection(s.Source, 80))).ToList();
        var manifest = new QuantManifest
        {
            DerivedMethod = NativeQuantFractions.Method, IsotopeCount = 6,
            DeNovoInputs = scores.Select(s => new DeNovoInputManifest { SourceId = s.SourceId, SourceDisplayName = s.SourceDisplayName, RawRows = s.RawRows, MinimumConfidence = 80 }).ToList(),
            DeNovoSourceScoreDistributions = scores,
            DeNovoIdentification = DeNovoIdentificationStudy.Build(union, reconciled),
            DeNovoOverlap = DeNovoOverlapStudy.Build(all.Length + only.Length + deep.Length, reconciled, criteria),
            DeNovoCallerAgreement = DeNovoAgreementStudy.Build(all, deep, only),
            DeNovoSourceVenn = DeNovoSourceVennStudy.Build(only, deep, criteria, 80, 80, reconciled),
            IdentificationConcordance = new()
            {
                PatternLabPsmRows = 12, PeaksDatabasePsmRows = 11, PatternLabScanKeys = 10, PeaksDatabaseScanKeys = 9,
                SharedDatabaseSearchScanKeys = 8, ExactDatabaseSearchSequenceScanKeys = 7,
                AdditionalIleLeuEquivalentDatabaseSearchScanKeys = 1, SameChargeDatabaseSearchScanKeys = 8,
                ExactSameChargeDatabaseSearchScanKeys = 7, PrecursorQualifiedDatabaseSearchPairs = 8,
                ExactPrecursorQualifiedDatabaseSearchPairs = 7, AdditionalIleLeuEquivalentPrecursorQualifiedPairs = 1,
                PrecursorQualifiedDatabaseSearchScanKeys = 8, ExactPrecursorQualifiedDatabaseSearchScanKeys = 7,
                PatternLabPrecursorHypothesesInSharedScans = 8, PeaksDatabasePrecursorHypothesesInSharedScans = 8, PrecursorTolerancePpm = 20,
            },
        };
        manifest.DeNovoAcceptedScoreScatter = DeNovoAcceptedScoreScatterStudy.Build(
            SpectrumComparisonTable.Build(all.Concat(only), deep, psms, Array.Empty<QuantRow>(), acceptedDeNovoRows: accepted), scores);
        return manifest;
    }

    private static void HtmlMassQualityTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        const string residues = "ACDEFGHIKLMNPQRSTVWY";
        var rows = new List<QuantRow>();
        for (int run = 0; run < 2; run++)
        for (int i = 0; i < 128; i++)
        {
            string sequence = "ACDEFG" + residues[i % 20] + residues[i / 20];
            rows.Add(AnQr(i % 4 == 0 ? "denovo" : "identified", "synthetic_run_" + run, i + 1, 2 + i % 3,
                sequence, 300 + i * 6.1, Enumerable.Range(0, 6).Select(k => k <= i % 6 ? (1000d + i * 117 + run * 71) / (k + 1) : 0).ToArray()));
        }
        var data = AnalyticsBuilder.Build(rows, new Dictionary<string, double> { ["synthetic_run_0"] = 5e8, ["synthetic_run_1"] = 4e8 }, 6);
        foreach (var fraction in data.Fractions)
        {
            fraction.NeutralMassBinWidth = NeutralMassRanges.Width;
            fraction.DetectedAreaByNeutralMass = new double[NeutralMassRanges.Count];
            foreach (var peptide in data.Peptides.Where(p => p.Run == fraction.Run))
                fraction.DetectedAreaByNeutralMass[NeutralMassRanges.Index(peptide.Mass)] += peptide.Envelope * 2;
        }
        var manifest = HtmlMassQualityManifest();
        var plan = ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), null);
        var figures = ReportFigureRenderer.Render(plan, Path.Combine(output, "word-figures"));
        var document = new ReportDocument { Title = "Synthetic chart review", Sections = figures.GroupBy(f => f.SectionId)
            .Select(g => new ReportSection(g.Key, g.Key, g.Select(f => (ReportBlock)f.Figure).ToArray())).ToArray() };
        var payload = HtmlReportFigures.Build(HtmlReportPayloadBuilder.Build(document, Array.Empty<ReportSupplementSheet>(),
            context: new() { ChartAnalytics = data, StoredStudies = manifest }), document);
        var charts = payload.MassQualityCharts!;
        Check("U3b signal points equal native projection", JsonSerializer.Serialize(charts.SignalByMass) == JsonSerializer.Serialize(new XicMassChart(data.Peptides).Data));
        Check("U3b isotope bins equal native projection", JsonSerializer.Serialize(charts.Isotopes) == JsonSerializer.Serialize(new IsotopeCountChart(data.Peptides).Data)
            && charts.Isotopes!.Bins.Sum(b => b.Total) == data.Peptides.Count(p => p.IsotopesCredited >= 1));
        Check("U3b mass-recovery bins and bases equal native projection", JsonSerializer.Serialize(charts.IonCurrentByMass) == JsonSerializer.Serialize(new PercentIonCurrentChart(data.Peptides, data.Grouped!.Tic, recovery: MassRangeRecoveryData.Build(data)).Data));
        Check("U3b scores equal native source bins and cutoffs", JsonSerializer.Serialize(charts.Quality.Single(c => c.Scores.Count > 0).Scores)
            == JsonSerializer.Serialize(new DeNovoScoreDistributionChart(manifest.DeNovoSourceScoreDistributions).Distributions));
        Check("U3b caller audit retains observed floors and matching tolerances", charts.Quality.Single(c => c.Id == "de_novo_peaks_deepnovo_agreement.png").Legend
            .Contains("Observed confidence floors: PEAKS ALC", StringComparison.Ordinal)
            && charts.Quality.Single(c => c.Id == "de_novo_peaks_deepnovo_agreement.png").Legend.Contains("RT sensitivity tolerance:", StringComparison.Ordinal));
        var qualityIds = plan.Charts.Where(c => c.SectionId == ReportSectionIds.Quality && !AnalyticsFigures.FileNames.Contains(c.FileName)).Select(c => c.FileName).ToArray();
        Check("U3b every Quality picture has native data", qualityIds.All(id => charts.Quality.Any(c => c.Id == id)), string.Join(",", qualityIds));
        Check("U3b assigned pictures leave HTML while Word keeps every native figure", payload.Quality.Count == 0
            && payload.Results.All(f => !new[] { AnalyticsFigures.XicByMassFile, AnalyticsFigures.IsotopesCreditedFile, AnalyticsFigures.PercentIonCurrentFile }.Any(name => f.SourceFigureId == "figure:" + name))
            && document.Sections.SelectMany(s => s.Blocks).OfType<ReportFigure>().Count() == figures.Count);
        Check("U3b agreement denominator equals native qualified pairs", charts.Quality.Single(c => c.Id == PresentationFigures.DatabaseSearchConcordanceFile).Rows.Single(r => r.Label == "Strict agreement").Value
            == manifest.IdentificationConcordance!.PrecursorQualifiedStrictAgreement * 100);
        var omitted = HtmlReportPayloadBuilder.Build(document, Array.Empty<ReportSupplementSheet>(),
            new() { ExcludedIds = new[] { "figure:" + AnalyticsFigures.XicByMassFile, "figure:" + PresentationFigures.DeNovoScoreDistributionFile } }, new() { ChartAnalytics = data, StoredStudies = manifest });
        Check("U3b exclusions remove native data", omitted.MassQualityCharts!.SignalByMass == null && omitted.MassQualityCharts.Quality.All(c => c.Id != PresentationFigures.DeNovoScoreDistributionFile));
        File.WriteAllBytes(Path.Combine(output, "charts.html"), HtmlReportWriter.BuildBytes(payload));
        Console.WriteLine("U3b page size: " + new FileInfo(Path.Combine(output, "charts.html")).Length.ToString(CultureInfo.InvariantCulture) + " bytes.");
        HtmlReportBrowserChecks(Path.Combine(output, "browser"), new[] { ("phone", payload, 390), ("desktop", payload, 1366) }, """
            const checks = {}, p = JSON.parse(d.getElementById('fq-data').textContent);
            d.getElementById('tab-mass').click();
            const cards = [...d.querySelectorAll('#mass-figures [data-native-chart]')];
            checks.massNative = cards.length === 3 && !d.querySelector('#mass-figures img');
            checks.scatterPoints = d.querySelectorAll('[data-native-chart="xic_by_mass.png"] .u3b-mark').length === p.massQualityCharts.signalByMass.points.length;
            checks.massLegend = cards.every(c => c.querySelector('.chart-legend')?.textContent.length > 70);
            const recovery = d.querySelector('[data-native-chart="percent_ion_current_by_mass.png"]');
            checks.rangeRecovery = recovery.textContent.includes('Recovery by mass range') && recovery.querySelectorAll('details tr').length === p.massQualityCharts.ionCurrentByMass.bins.length + 1;
            checks.compactPercentages = [...recovery.querySelectorAll('.u3b-row-value')].every(n => /^(under 0\.1%|[0-9]+\.[0-9]%|Unavailable)$/.test(n.textContent));
            checks.scatterGrid = d.querySelectorAll('[data-native-chart="xic_by_mass.png"] .u3b-grid').length > 5;
            const fill = recovery.querySelector('.u3b-fill');
            checks.visibleBarTrack = !!fill && fill.getBoundingClientRect().height >= 8 && w.getComputedStyle(fill).backgroundColor !== 'rgba(0, 0, 0, 0)';
            const mark = d.querySelector('[data-native-chart="xic_by_mass.png"] .u3b-mark');
            mark.focus(); checks.focusValue = !d.getElementById('chart-hover').hidden && d.getElementById('chart-hover').textContent.includes('intensity x minutes');
            mark.dispatchEvent(new w.KeyboardEvent('keydown', {key:'ArrowRight',bubbles:true}));
            checks.arrows = d.activeElement !== mark && d.activeElement.classList.contains('u3b-mark');
            d.getElementById('tab-quality').click();
            checks.qualityNative = d.querySelectorAll('#quality-figures .u3b-chart').length >= p.massQualityCharts.quality.length && !d.querySelector('#quality-figures img');
            checks.scoreSources = d.querySelectorAll('[data-native-chart="de_novo_score_distribution.png"]').length === 3;
            checks.allLegends = [...d.querySelectorAll('.u3b-chart')].every(c => !!c.querySelector('.chart-legend')?.textContent);
            checks.singularUnits = ![...d.querySelectorAll('#quality-figures .u3b-row-value')].some(n => /\b1 (calls|pairs|scans)\b/.test(n.textContent));
            checks.noHorizontalPageScroll = d.documentElement.scrollWidth <= d.documentElement.clientWidth + 1;
            return checks;
            """);
        HtmlMassQualityScreens(output, payload);
    }

    private static void HtmlMassQualityScreens(string output, HtmlReportPayload payload)
    {
        string? edge = new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Microsoft", "Edge", "Application", "msedge.exe")).FirstOrDefault(File.Exists);
        if (edge == null) return;
        foreach (string theme in new[] { "light", "dark" })
        foreach (int width in new[] { 1366, 390 })
        foreach (string tab in new[] { "mass", "quality" })
        {
            string name = "U3b-" + tab + "-" + theme + "-" + width.ToString(CultureInfo.InvariantCulture);
            string page = Path.Combine(output, name + ".html"), screenshot = Path.Combine(output, name + ".png");
            string html = Encoding.UTF8.GetString(HtmlReportWriter.BuildBytes(payload)).Replace("$(\"app\").hidden = false;",
                "$(\"app\").hidden = false; document.documentElement.dataset.theme='" + theme + "'; requestAnimationFrame(()=>{$('tab-" + tab + "').click();});", StringComparison.Ordinal);
            File.WriteAllText(page, html, new UTF8Encoding(false));
            System.Threading.Tasks.Task.Run(() => CaptureMassQualityPage(edge, page, screenshot, width,
                Path.Combine(output, name + "-cdp-" + Guid.NewGuid().ToString("N")))).GetAwaiter().GetResult();
            Check("U3b emulated headless screen " + name, File.Exists(screenshot));
        }
    }
}
