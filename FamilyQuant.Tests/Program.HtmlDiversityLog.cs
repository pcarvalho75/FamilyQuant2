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
    private static IEnumerable<TestCommand> HtmlDiversityLogCommands() =>
    [new("--test-html-diversity-log", 10732, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-diversity-log", (args, _) =>
        {
            HtmlDiversityLogTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-diversity-log"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlDiversityLogTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var values = new[] { (X: .1, Y: 1), (X: 1d, Y: 10), (X: 10d, Y: 100), (X: 0d, Y: 10), (X: 3d, Y: 0) };
        var rows = values.Select((value, index) => new FamilyCompositionValue((index + 1).ToString(CultureInfo.InvariantCulture),
            "Family " + (index + 1).ToString(CultureInfo.InvariantCulture), value.X, null, 2, Array.Empty<FamilySubclassValue>(), value.Y, 0)).ToArray();
        var manifest = new QuantManifest { FamilyRunShares = new() { Policy = SharedPeptidePolicy.FamilyUnique } };
        foreach (string name in new[] { "run-a", "run-b" })
            manifest.FamilyRunShares.Runs.Add(new() { Run = name, MeasuredSignal = 100, IdentifiedSignal = 100, TotalIonCurrent = 100, AssignedSignal = 100 });
        foreach (var row in rows)
            manifest.FamilyRunShares.Families.Add(new() { FamilyUserId = int.Parse(row.Key, CultureInfo.InvariantCulture),
                Runs = manifest.FamilyRunShares.Runs.Select(run => new FamilyRunShare { Run = run.Run, Signal = row.MeanPercent,
                    IdentifiedSignal = row.MeanPercent, ShareOfMeasuredSignal = row.MeanPercent / 100, ShareOfTic = row.MeanPercent / 100 }).ToList() });
        foreach (var run in manifest.FamilyRunShares.Runs) manifest.DerivedFractions.Add(new() { Run = run.Run, DetectedPeptideLikeArea = 100 });
        var basis = new ReportCompositionBasis(false, true, manifest, null, new Dictionary<int, string>(), null, new Dictionary<int, IReadOnlyList<int>>());
        var document = new ReportDocument { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", Array.Empty<ReportBlock>()) } };
        (HtmlReportPayload Payload, HtmlFamilyFigurePacket Packet) Build(IReadOnlyList<FamilyCompositionValue> supplied)
        {
            var data = new FamilyCompositionData(supplied, "Synthetic per-run family share");
            var plan = new ReportFigurePlan(new[] { new ReportChart(ReportSectionIds.Results, "abundance_diversity.png",
                "Abundance against database-protein diversity", "", () => new FamilyAbundanceDiversityChart(data)) }, Array.Empty<ReportFigureAvailability>());
            var studies = new ReportStudyFigurePacket(Array.Empty<ReportPlacedFigure>(), Array.Empty<ReportFigureAvailability>(),
                new Dictionary<string, FamilyQuant.Report.Captions.FigureCaptionInput>());
            var packet = HtmlFamilyFigurePacket.Prepare(studies, plan, basis, manifest);
            return (HtmlReportPayloadBuilder.Build(document, packet.Sheets, context: new() { StoredStudies = manifest,
                FamilyFiguresProvider = packet.Build }), packet);
        }
        var (payload, packet) = Build(rows);
        var figure = payload.FamilyFigures.Figures.Single();
        foreach (var view in figure.Views.Values)
        {
            Check("LG1 positive log bounds cover native values", view.MinimumX == .1 && view.MaximumX == 10 && view.MinimumY == 1 && view.MaximumY == 100);
            Check("LG1 native shares and counts stay exact including zero", view.Rows.Select(row => payload.Fields[row.X!.Exact].Value!.Value).SequenceEqual(values.Select(value => value.X))
                && view.Rows.Select(row => payload.Fields[row.Y!.Exact].Value!.Value).SequenceEqual(values.Select(value => (double)value.Y)));
            Check("LG1 log ticks use plain numbers", view.XTicks.Select(tick => tick.Label).SequenceEqual(new[] { "0.1%", "1%", "10%" })
                && view.YTicks.Select(tick => tick.Label).SequenceEqual(new[] { "1", "10", "100" }));
            Check("LG1 legend explains both log axes and zero marks", view.Legend.Contains("equal fold changes", StringComparison.Ordinal)
                && view.Legend.Contains("hollow diamonds", StringComparison.Ordinal) && view.Legend.Contains("no whiskers", StringComparison.Ordinal));
        }
        var zero = Build(rows.Select(row => row with { MeanPercent = 0, GreedyProteinCount = 0 }).ToArray()).Payload;
        var zeroView = zero.FamilyFigures.Figures.Single().Views["credited"];
        Check("LG1 all-zero native axes keep finite positive floors", zeroView.MinimumX == 1 && zeroView.MaximumX == 10 && zeroView.MinimumY == 1 && zeroView.MaximumY == 10);
        zero = zero with { FamilyFigures = zero.FamilyFigures with { Figures = new[] { zero.FamilyFigures.Figures.Single() with
            { UsesNormalization = false, Views = new Dictionary<string, HtmlFamilyFigureView> { ["fixed"] = zeroView } } } } };
        string word = Path.Combine(output, "diversity.docx"), workbook = ReportSupplementWorkbook.PathForReport(word);
        WordReportWriter.Write(word, document); ReportSupplementWorkbook.Write(workbook, document, packet.Sheets);
        Check("LG1 native values trace to workbook without transform mutation", ReportNumberTrace.RunHtmlBindings(payload, word, workbook, new(), manifest,
            Path.Combine(output, "trace.csv")) == 0);
        HtmlReportBrowserChecks(Path.Combine(output, "browser"), new[] { ("light-phone", payload, 390), ("dark-phone", payload, 390),
            ("light-desktop", payload, 1366), ("dark-desktop", payload, 1366), ("zero-phone", zero, 390) }, """
            const checks={};d.documentElement.dataset.theme=name.startsWith('dark')?'dark':'light';
            const card=d.querySelector('[data-kind="diversity"]'),canvas=card.querySelector('svg'),box=canvas.viewBox.baseVal;
            const point=key=>card.querySelector('[data-row-key="'+key+'"]'),xy=key=>{const c=point(key).querySelector('circle');return [+c.getAttribute('cx'),+c.getAttribute('cy')];};
            const a=xy('1'),b=xy('2'),c=xy('3'),zx=xy('4'),zy=xy('5'),near=(x,y)=>Math.abs(x-y)<1e-6;
            const allZero=name.startsWith('zero');
            checks.logX=allZero||near(b[0],(a[0]+c[0])/2)&&c[0]>a[0];
            checks.logY=allZero||near(b[1],(a[1]+c[1])/2)&&a[1]>c[1];
            checks.zeroFloors=allZero?[...card.querySelectorAll('.u3c-hit')].every(hit=>{const p=hit.querySelector('circle');return +p.getAttribute('cx')===52&&+p.getAttribute('cy')===268;}):near(zx[0],52)&&near(zy[1],268);
            checks.zeroMarks=point('4').querySelector('.u3c-zero-point')&&point('5').querySelector('.u3c-zero-point')&&(!allZero?!point('1').querySelector('.u3c-zero-point'):true);
            checks.finiteGeometry=[...canvas.querySelectorAll('circle')].every(el=>Number.isFinite(+el.getAttribute('cx'))&&Number.isFinite(+el.getAttribute('cy')));
            checks.plainTicks=[...canvas.querySelectorAll('text')].every(el=>!/[eE]|10\^/.test(el.textContent));
            checks.tickLabelsFit=[...canvas.querySelectorAll('text')].every(el=>{const r=el.getBBox();return r.x>=0&&r.x+r.width<=box.width+.5;});
            point('4').dispatchEvent(new w.Event('pointerenter'));checks.zeroXHover=d.getElementById('chart-hover').textContent.includes('0 %');
            point('5').focus();checks.zeroYHover=d.getElementById('chart-hover').textContent.includes(': 0 proteins');
            point('5').dispatchEvent(new w.KeyboardEvent('keydown',{key:'Enter',bubbles:true}));
            checks.zeroKeyboard=d.getElementById('detail-dialog').open&&d.getElementById('dialog-body').textContent.includes('0 proteins');d.getElementById('detail-dialog').close();
            checks.controlsExplain=[...card.querySelectorAll('.u3c-hit')].every(hit=>hit.getAttribute('tabindex')==='0'&&hit.getAttribute('title').includes('changes only this report view'));
            checks.noOverflow=d.documentElement.scrollWidth<=w.innerWidth+1;
            return Object.fromEntries(Object.entries(checks).map(([key,value])=>[key,!!value]));
            """);
        CaptureHtmlReportScreens(Path.Combine(output, "screens"), payload, "families", anchor: "family-figures");
    }
}
