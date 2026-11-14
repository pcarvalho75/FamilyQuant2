using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlSignalRecoveryCommands() =>
    [new("--test-html-signal-recovery", 10530, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-signal-recovery", (args, _) =>
        {
            HtmlSignalRecoveryTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-signal-recovery"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlSignalRecoveryTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var data = new AnalyticsData();
        data.Fractions.AddRange(new[]
        {
            new NativeRunFraction { Run = "run-a", Tic = 1000, IdentifiedAreaM0 = 100, DeNovoAreaM0 = 40,
                IdentifiedEnvelope = 200, DeNovoEnvelope = 100, DeNovoEnvelopeReferenceCalibrated = 100,
                DetectedPeptideLikeArea = 800, DetectedAreaMultiplyCharged = 600, DetectedAreaSinglyCharged = 200 },
            new NativeRunFraction { Run = "run-b", Tic = 4000, IdentifiedAreaM0 = 200, DeNovoAreaM0 = 300,
                IdentifiedEnvelope = 400, DeNovoEnvelope = 800, DeNovoEnvelopeReferenceCalibrated = 800,
                DetectedPeptideLikeArea = 2800, DetectedAreaMultiplyCharged = 2000, DetectedAreaSinglyCharged = 800 },
        });
        data.Grouped = NativeQuantFractions.Grouped(data.Fractions);
        var world = new FamilyWorldSummary { MinimumTagLength = FamilyWorldOptions.DefaultMinimumTagLength,
            CompositionRules = FamilyWorldErrors.CompositionRule, MeasuredIdentifiedEnvelope = 600, MeasuredDeNovoEnvelope = 900 };
        world.Composition.AddRange(new[]
        {
            new FamilyWorldCompositionRow { Family = "Illustrative family A", IdentifiedEnvelope = 100, DatabaseDeNovoEnvelope = 25, WorldDeNovoEnvelope = 75 },
            new FamilyWorldCompositionRow { Family = "Illustrative family B", IdentifiedEnvelope = 40, DatabaseDeNovoEnvelope = 10, WorldDeNovoEnvelope = 20 },
            new FamilyWorldCompositionRow { Family = "Illustrative family C", IdentifiedEnvelope = 450, DatabaseDeNovoEnvelope = 40, WorldDeNovoEnvelope = 10 },
            new FamilyWorldCompositionRow { Family = "Illustrative family D", IdentifiedEnvelope = 480, DatabaseDeNovoEnvelope = 15, WorldDeNovoEnvelope = 5 },
            new FamilyWorldCompositionRow { Family = "Illustrative family E" },
        });
        var manifest = new QuantManifest { IsotopeCount = 6, FamilyWorldResult = world, DerivedMethod = NativeQuantFractions.Method };
        var project = new FamilyClusterFile();
        string before = JsonSerializer.Serialize(data.Fractions), worldBefore = JsonSerializer.Serialize(world);
        var plan = ReportFigureRenderer.Plan(data, manifest, project, null);
        var signalPlans = plan.Charts.Where(chart => chart.SignalChart != null).ToArray();
        Check("U3a captures all four native chart kinds during the existing plan", signalPlans.Length == 4
            && signalPlans.Select(chart => chart.SignalChart!.Kind).OrderBy(kind => kind).SequenceEqual(new[] { "by-run", "gap", "recovery", "uplift" }));
        var recovery = signalPlans.Single(chart => chart.SignalChart!.Kind == "recovery");
        var nativeRecovery = (RecoveryChart)recovery.CreateChart();
        var recoveryData = recovery.SignalChart!;
        Check("U3a measured envelope label uses the exact recorded native ceiling", recoveryData.IsotopeLabel == nativeRecovery.EnvelopeLabel
            && recoveryData.Rows.Count == nativeRecovery.Rows.Count && recoveryData.Rows[^1].IsPooled);
        for (int index = 0; index < nativeRecovery.Rows.Count; index++)
        {
            var native = nativeRecovery.Rows[index]; var row = recoveryData.Rows[index];
            Check("U3a recovery row " + index + " equals every native fraction", row.Parts[0].Value == 100 * native.RecoveredFractionM0
                && row.Parts[1].Value == 100 * native.RecoveredFractionEnvelope
                && row.Parts[2].Value == 100 * native.DetectedFractionPeptideLike && row.Parts[3].Value == 100
                && row.Parts.All(value => value.Reference == native.Tic));
            Check("U3a recovery details retain the native envelope and detector charge parts " + index,
                row.Details[0].Value == native.IdentifiedFractionEnvelope * 100
                && row.Details[1].Value == native.DeNovoFractionEnvelope * 100
                && row.Details[2].Value == native.DetectedFractionMultiplyCharged * 100
                && row.Details[3].Value == native.DetectedFractionSinglyCharged * 100);
        }
        Check("U3a pooled recovery is the pooled native reading rather than the average of runs",
            Math.Abs(recoveryData.Rows[^1].Parts[0].Value!.Value - 12.8) < 1e-12
            && Math.Abs(recoveryData.Rows[^1].Parts[0].Value!.Value - recoveryData.Rows.Take(2).Average(row => row.Parts[0].Value!.Value)) > .1);
        var runs = signalPlans.Single(chart => chart.SignalChart!.Kind == "by-run");
        var nativeRuns = (ReportSignalByRunChart)runs.CreateChart();
        Check("U3a run parts and totals equal the native run chart", runs.SignalChart!.Rows.Select((row, index) =>
            row.Parts[0].Value == nativeRuns.Rows[index].IdentifiedFractionEnvelope * 100
            && row.Parts[1].Value == nativeRuns.Rows[index].DeNovoFractionReferenceCalibrated * 100
            && row.Total!.Value == nativeRuns.Rows[index].RecoveredFractionReferenceCalibrated * 100).All(same => same));
        var uplift = signalPlans.Single(chart => chart.SignalChart!.Kind == "uplift");
        var nativeUplift = (ReportSignalUpliftChart)uplift.CreateChart();
        Check("U3a uplift preserves the native TIC and identified-baseline denominators", uplift.SignalChart!.Rows[0].Parts[0].Value == data.Grouped.IdentifiedFractionEnvelope * 100
            && uplift.SignalChart.Rows[0].Parts[1].Value == data.Grouped.RecoveredFractionReferenceCalibrated * 100
            && uplift.SignalChart.Notes[0].Value == data.Grouped.DeNovoFractionReferenceCalibrated * 100
            && uplift.SignalChart.Notes[1].Value == ReportSignalUpliftChart.RelativeIncrease(nativeUplift.Grouped) * 100
            && uplift.SignalChart.Notes[1].Reference == data.Grouped.IdentifiedEnvelope);
        var gap = signalPlans.Single(chart => chart.SignalChart!.Kind == "gap");
        var nativeGap = (DatabaseGapChart)gap.CreateChart();
        Check("U5 gap order uses descending pooled totals and preserves native order for ties", gap.SignalChart!.Rows.Select(row => row.Name)
            .SequenceEqual(new[] { "Illustrative family C", "Illustrative family D", "Illustrative family A", "Illustrative family B", "Illustrative family E" })
            && gap.SignalChart.Rows.Select(row => row.Total!.Value).SequenceEqual(new double?[] { 500, 500, 200, 70, 0 }));
        Check("U5 gap ordering leaves the native Word chart order untouched", nativeGap.Rows.Select(row => row.Family)
            .SequenceEqual(new[] { "Illustrative family A", "Illustrative family B", "Illustrative family C", "Illustrative family D", "Illustrative family E" }));
        Check("U3a gap retains every native area and total after HTML ordering", gap.SignalChart.Rows.All(row =>
        {
            var native = nativeGap.Rows.Single(item => DatabaseGapDisplayRow.Label(item) == row.Name);
            return row.Parts[0].Value == native.IdentifiedEnvelope && row.Parts[1].Value == native.DatabaseDeNovoEnvelope
                && row.Parts[2].Value == native.WorldDeNovoEnvelope && row.Total!.Value == native.TotalEnvelope
                && row.Parts.All(value => value.Unit == "area");
        }));
        var noTic = new NativeRunFraction { Run = "missing-tic", IdentifiedEnvelope = 80, DeNovoEnvelope = 20 };
        var absent = HtmlSignalRecoveryData.Read(new RecoveryChart(new[] { noTic }, isotopeCeiling: null), "figure:recovery.png", "Recovery")!;
        Check("U3a missing TIC/detector/ceiling remain unavailable rather than zero", absent.Rows[0].Parts.All(value => value.Value == null)
            && absent.IsotopeLabel!.Contains("not recorded", StringComparison.Ordinal));
        var zero = new NativeRunFraction { Run = "zero-signal", Tic = 100 };
        var zeroData = HtmlSignalRecoveryData.Read(new RecoveryChart(new[] { zero }), "figure:recovery.png", "Recovery")!;
        Check("U3a a recorded zero signal remains zero while the absent detector stays unavailable", zeroData.Rows[0].Parts[0].Value == 0
            && zeroData.Rows[0].Parts[1].Value == 0 && zeroData.Rows[0].Parts[2].Value == null
            && zeroData.Rows[0].Parts[2].Area == null && zeroData.Rows[0].Parts[3].Value == 100);
        var noBaseline = HtmlSignalRecoveryData.Read(new ReportSignalUpliftChart(zero), "figure:signal_uplift.png", "Uplift")!;
        Check("U3a relative increase refuses an absent identified baseline", noBaseline.Notes[1].Value == null
            && noBaseline.Notes[1].UnavailableReason!.Contains("zero", StringComparison.Ordinal));
        var legacy = new NativeRunFraction { Run = "legacy-readings", Tic = 100, IdentifiedEnvelope = 10,
            DeNovoEnvelope = 5, DeNovoEnvelopeReferenceCalibrated = 8, DetectedPeptideLikeArea = 40 };
        var legacyData = HtmlSignalRecoveryData.Read(new RecoveryChart(new[] { legacy }), "figure:recovery.png", "Recovery")!;
        var legacyRuns = HtmlSignalRecoveryData.Read(new ReportSignalByRunChart(new[] { legacy }), "figure:signal_by_run.png", "Runs")!;
        var legacyUplift = HtmlSignalRecoveryData.Read(new ReportSignalUpliftChart(legacy), "figure:signal_uplift.png", "Uplift")!;
        Check("U3a all hover numerators follow the native measured envelope despite retired compatibility fields",
            legacyData.Rows[0].Parts[1].Value == legacy.RecoveredFractionEnvelope * 100
            && legacyRuns.Rows[0].Parts[1].Area == legacy.DeNovoEnvelope
            && legacyRuns.Rows[0].Total!.Area == legacy.IdentifiedEnvelope + legacy.DeNovoEnvelope
            && legacyUplift.Rows[0].Parts[1].Area == legacy.IdentifiedEnvelope + legacy.DeNovoEnvelope
            && legacyUplift.Notes.All(value => value.Area == legacy.DeNovoEnvelope));
        Check("U3a an old detector total does not invent an unrecorded charge split", legacyData.Rows[0].Parts[2].Value == 40
            && legacyData.Rows[0].Details.Skip(2).All(value => value.Value == null && value.UnavailableReason != null));
        var smallGap = HtmlSignalRecoveryData.Read(new DatabaseGapChart(new[]
            { new FamilyWorldCompositionRow { Family = "Small illustrative family", IdentifiedEnvelope = .2, WorldDeNovoEnvelope = .3 } }), "figure:database_gap.png", "Gap")!;
        Check("U3a area geometry retains a native maximum below one", smallGap.Maximum == .5);

        var rendered = ReportFigureRenderer.Render(plan, Path.Combine(output, "word-pictures"));
        var doc = new ReportDocument { Title = "Synthetic interactive signal report", Sections = rendered.GroupBy(figure => figure.SectionId)
            .Select(group => new ReportSection(group.Key, group.Key, group.Select(item => (ReportBlock)item.Figure).ToArray())).ToArray() };
        var sourceFixture = HtmlReportTabsFixture();
        var normalizationFixture = HtmlSharePartsFixture();
        var bases = HtmlReportPayloadBuilder.Build(normalizationFixture.Document, normalizationFixture.Sheets, context: normalizationFixture.Context);
        var sourceFields = sourceFixture.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var pair in bases.Fields) sourceFields[pair.Key] = pair.Value;
        sourceFixture = sourceFixture with { Fields = sourceFields, Composition = bases.Composition };
        var payload = HtmlReportFigures.Build(sourceFixture, doc);
        Check("U3a selected HTML contains data instead of recovery/gap pictures", payload.SignalCharts.Count == 4
            && payload.Results.Concat(payload.Quality).All(figure => !HtmlSignalRecoveryData.Owns(figure.SourceFigureId)));
        Check("U3a Word retains every original native signal picture and its snapshot", rendered.Where(figure => HtmlSignalRecoveryData.Owns(figure.Figure.Id))
            .All(figure => figure.Figure.PngBytes.Length > 1000 && figure.Figure.SignalChart != null));
        var filtered = HtmlReportFigures.Build(sourceFixture, doc, new() { ExcludedIds = new[] { recovery.SignalChart!.Id, "figure:signal_by_run.png" } });
        Check("U3a picture checkboxes still exclude the corresponding interactive chart", filtered.SignalCharts.Count == 2
            && filtered.SignalCharts.All(chart => chart.Kind is "gap" or "uplift"));
        var noQuality = HtmlReportFigures.Build(sourceFixture, doc, new() { ExcludedIds = new[] { ReportSectionIds.Quality } });
        Check("U3a section exclusion still removes the native recovery view", noQuality.SignalCharts.All(chart => chart.Kind != "recovery"));
        data.Fractions.AddRange(Enumerable.Range(3, 3).Select(index => new NativeRunFraction { Run = "run-" + index, Tic = 100, IdentifiedEnvelope = 10 }));
        var pages = ReportFigureRenderer.Plan(data, manifest, project, null).Charts.Where(chart => chart.SignalChart?.Kind == "by-run").ToArray();
        Check("U3a paged run figures retain only their own native page members", pages.Length == 2
            && pages[0].SignalChart!.Rows.Count == ReportSignalByRunChart.RunsPerPage
            && pages[1].SignalChart!.Rows.Count == 2 && pages[1].SignalChart!.Rows[0].Name.StartsWith("Run 4:", StringComparison.Ordinal));
        data.Fractions.RemoveRange(2, 3);
        Check("U3a snapshots and rendering leave the stored native inputs unchanged", before == JsonSerializer.Serialize(data.Fractions)
            && worldBefore == JsonSerializer.Serialize(world));
        string page = Path.Combine(output, "synthetic-signal-report.html");
        HtmlReportWriter.Write(page, payload);
        File.WriteAllText(Path.Combine(output, "native-signal-readings.json"), JsonSerializer.Serialize(payload.SignalCharts, new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
        Console.WriteLine("U3a synthetic HTML page size: " + new FileInfo(page).Length + " bytes; " + page);
        HtmlReportBrowserChecks(Path.Combine(output, "browser"), new[] { ("phone", payload, 390), ("desktop", payload, 1366),
            ("missing", payload with { SignalCharts = new[] { absent, noBaseline } }, 390) }, """
            const checks={}, p=JSON.parse(d.getElementById('fq-data').textContent), source=JSON.stringify(p.signalCharts);
            const panels=['recovery','gap'];
            checks.noPictures=panels.every(name=>!d.getElementById('panel-'+name).querySelector('img'));
            checks.nativeCards=d.querySelectorAll('[data-native-chart]').length===p.signalCharts.length;
            checks.nativeVectors=[...d.querySelectorAll('.u3a-card')].every(card=>card.querySelector('svg')||card.textContent.includes('No native readings'));
            checks.legends=[...d.querySelectorAll('.u3a-card')].every(card=>card.querySelector('.u3a-caption')?.textContent.includes('keeps its recorded base'));
            for(const kind of panels){ d.getElementById('tab-'+kind).click();
              checks[kind+'Fits']=d.documentElement.scrollWidth<=w.innerWidth+1&&[...d.getElementById('panel-'+kind).querySelectorAll('.u3a-card')].every(card=>{const r=card.getBoundingClientRect();return r.left>=0&&r.right<=w.innerWidth+1;}); }
            checks.gapHeading=[...d.getElementById('panel-gap').querySelectorAll('h2,h3')].filter(h=>h.textContent==='Database gap').length===1;
            const gapChart=p.signalCharts.find(chart=>chart.kind==='gap');
            checks.gapOrder=gapChart ? gapChart.rows.every((row,index)=>index===0||gapChart.rows[index-1].total.value>=row.total.value)
              &&[...d.querySelectorAll('.u3a-card.gap .u3a-run')].map(row=>row.dataset.nativeRun).join('|')===gapChart.rows.map(row=>row.name).join('|')
              : !d.querySelector('.u3a-card.gap');
            checks.gapPercent=d.getElementById('gap-panel').textContent.includes('25')&&!d.getElementById('gap-panel').textContent.includes('0.25')
              &&d.getElementById('gap-panel').textContent.includes('PSM and de novo');
            d.getElementById('tab-recovery').click();
            const first=d.querySelector('#panel-recovery .u3a-value'); first.focus();
            checks.focus=d.activeElement===first&&!d.getElementById('chart-hover').hidden;
            const expected=p.signalCharts.find(c=>c.kind==='recovery')?.rows[0].parts[0];
            checks.hover=d.getElementById('chart-hover').textContent.includes(expected?.value===null?expected.unavailableReason:expected.exact);
            first.click();checks.tap=d.getElementById('detail-dialog').open&&d.getElementById('dialog-body').textContent.includes(expected?.exact);
            d.getElementById('dialog-close').click();
            const mark=d.querySelector('#panel-recovery .u3a-mark');mark.focus();mark.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Enter',bubbles:true}));
            checks.keyboard=d.getElementById('detail-dialog').open;d.getElementById('dialog-close').click();
            const snapshot=[...d.querySelectorAll('.u3a-card')].map(card=>card.textContent).join('');
            checks.allBasesAvailable=d.querySelectorAll('input[name="normalization-basis"]:not(:disabled)').length===4;
            for(const choice of d.querySelectorAll('input[name="normalization-basis"]')) if(!choice.disabled)choice.click();
            checks.fixedBases=snapshot===[...d.querySelectorAll('.u3a-card')].map(card=>card.textContent).join('');
            w.dispatchEvent(new w.Event('beforeprint'));checks.print=[...d.querySelectorAll('.u3a-details')].every(details=>details.open);
            checks.printDetails=p.signalCharts.every(chart=>{
              const table=[...d.querySelectorAll('.u3a-card')].find(card=>card.dataset.nativeChart===chart.id)?.querySelector('.u3a-table');
              return !chart.rows.length||[...chart.rows.flatMap(row=>row.details||[]),...chart.notes].every(value=>table?.textContent.includes(value.label)&&table.textContent.includes(value.exact));
            });
            w.dispatchEvent(new w.Event('afterprint'));checks.restore=[...d.querySelectorAll('.u3a-details')].every(details=>!details.open);
            checks.sourceUntouched=source===JSON.stringify(JSON.parse(d.getElementById('fq-data').textContent).signalCharts);
            checks.tooltips=[...d.querySelectorAll('.u3a-card button,.u3a-card summary')].every(control=>control.title.length>20);
            if(name==='missing')checks.unavailable=!!d.querySelector('.u3a-missing')&&d.querySelector('.u3a-value').textContent==='not available';
            return checks;
            """);
    }
}
