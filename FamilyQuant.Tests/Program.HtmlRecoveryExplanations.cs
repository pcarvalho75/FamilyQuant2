using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlRecoveryExplanationCommands() =>
    [new("--test-html-recovery-explanations", 10531, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-recovery-explanations", (args, _) =>
        {
            HtmlRecoveryExplanationTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-recovery-explanations"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlRecoveryExplanationTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var run = new NativeRunFraction { Run = "illustrative-run", Tic = 1000, IdentifiedAreaM0 = 100, DeNovoAreaM0 = 40,
            IdentifiedEnvelope = 200, DeNovoEnvelope = 100, DetectedPeptideLikeArea = 800,
            DetectedAreaMultiplyCharged = 600, DetectedAreaSinglyCharged = 200 };
        var grouped = NativeQuantFractions.Grouped(new[] { run });
        var gapRows = new[]
        {
            new FamilyWorldCompositionRow { Family = "Illustrative family", IdentifiedEnvelope = 100, DatabaseDeNovoEnvelope = 25, WorldDeNovoEnvelope = 75 },
            new FamilyWorldCompositionRow { Family = "Large illustrative family", IdentifiedEnvelope = 1.2e9, DatabaseDeNovoEnvelope = 3e8, WorldDeNovoEnvelope = 9e8 },
            new FamilyWorldCompositionRow { Family = "Recorded zero family" },
        };
        string before = JsonSerializer.Serialize(new { Run = run, GapRows = gapRows });
        var charts = new[]
        {
            HtmlSignalRecoveryData.Read(new RecoveryChart(new[] { run }, grouped, isotopeCeiling: 6), "figure:recovery.png", "Recovery and M+0 sensitivity")!,
            HtmlSignalRecoveryData.Read(new ReportSignalByRunChart(new[] { run }), "figure:signal_by_run.png", "Signal by run")!,
            HtmlSignalRecoveryData.Read(new ReportSignalUpliftChart(grouped), "figure:signal_uplift.png", "Signal recovery components")!,
            HtmlSignalRecoveryData.Read(new DatabaseGapChart(gapRows), "figure:database_gap.png", "Database gap")!,
        };
        var gap = charts[^1];
        for (int index = 0; index < gapRows.Length; index++)
        {
            var native = gapRows[index]; var row = gap.Rows[index];
            var amounts = new[] { native.IdentifiedEnvelope, native.DatabaseDeNovoEnvelope, native.WorldDeNovoEnvelope };
            Check("U5 gap areas retain native values and G5 program formatting " + index, row.Parts.Select((part, i) =>
                part.Value == amounts[i] && part.Text.StartsWith(amounts[i].ToString("G5", CultureInfo.InvariantCulture) + " area; ", StringComparison.Ordinal)).All(same => same));
            Check("U5 family percentages use that native family's own three-part total " + index, row.Parts.Select((part, i) =>
                part.FamilyShare is { } share && share.Value == (native.TotalEnvelope > 0 ? amounts[i] / native.TotalEnvelope * 100 : (double?)null)
                && share.Reference == native.TotalEnvelope && share.Basis == "this family's displayed total signal").All(same => same));
        }
        Check("U5 zero family totals stay unavailable instead of dividing by zero", gap.Rows[^1].Parts.All(part =>
            part.Value == 0 && part.FamilyShare is { Value: null, UnavailableReason: not null }));
        Check("U5 large areas use native scientific notation while exact areas stay unrounded", gap.Rows[1].Parts[0].Text.StartsWith("1.2E+09 area;", StringComparison.Ordinal)
            && gap.Rows[1].Parts[0].Exact == (1.2e9).ToString("G17", CultureInfo.InvariantCulture));
        var payload = HtmlReportTabsFixture() with { SignalCharts = charts };
        string path = Path.Combine(output, "synthetic-recovery-explanations.html");
        HtmlReportWriter.Write(path, payload);
        Check("U5 explanations and added family percentages leave stored native inputs unchanged", before == JsonSerializer.Serialize(new { Run = run, GapRows = gapRows }));
        var missing = HtmlSignalRecoveryData.Read(new RecoveryChart(new[] { new NativeRunFraction { Run = "unrecorded-tic" } }), "figure:recovery.png", "Recovery")!;
        Console.WriteLine("U5 synthetic HTML page size: " + new FileInfo(path).Length + " bytes; " + path);
        HtmlReportBrowserChecks(Path.Combine(output, "browser"), new[] { ("phone", payload, 390), ("desktop", payload, 1366),
            ("missing", payload with { SignalCharts = new[] { missing, gap } }, 390) }, """
            const checks={},p=JSON.parse(d.getElementById('fq-data').textContent),source=JSON.stringify(p.signalCharts);
            d.getElementById('tab-recovery').click();
            const definitions=d.getElementById('u5-readings'),gaps=d.getElementById('u5-gaps');
            checks.fourReadings=definitions.querySelectorAll('[data-reading]').length===4
              &&definitions.textContent.includes("one peptide at one charge in one elution peak, counted once")
              &&definitions.textContent.includes("that run's recorded total ion current");
            checks.measured=definitions.querySelector('[data-reading="measured"]').textContent.includes('recorded isotope limit')
              &&definitions.querySelector('[data-reading="m0"]').textContent.includes('Heavier isotope peaks');
            checks.detector=definitions.querySelector('[data-reading="detector"]').textContent.includes('with or without an accepted peptide sequence')
              &&definitions.textContent.includes('not calibrated to a peptide false discovery rate');
            checks.gapMeaning=gaps.querySelectorAll('[data-gap]').length===3&&gaps.textContent.includes('same peptide features')
              &&gaps.textContent.includes('does not measure a validated pool')&&gaps.textContent.includes('cannot classify the whole gap as noise')
              &&gaps.textContent.includes('not a biological partition of TIC');
            checks.bases=gaps.textContent.includes('summed signal by the summed TIC')&&gaps.textContent.includes('not the mean')
              &&gaps.textContent.includes('percentage points')&&gaps.textContent.includes('identified envelope as its baseline')
              &&gaps.textContent.includes('keep the fixed reference totals');
            const info=d.getElementById('signal-info');info.focus();info.click();
            checks.help=d.getElementById('detail-dialog').open&&d.getElementById('dialog-body').querySelectorAll('[data-reading]').length===4
              &&d.getElementById('dialog-body').textContent.includes('How to read the gaps')&&info.title.includes('without changing the project');
            checks.uniqueIds=d.querySelectorAll('#u5-readings').length===1&&d.querySelectorAll('#u5-gaps').length===1;
            d.getElementById('dialog-close').click();
            checks.guideFits=[definitions,gaps].every(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=w.innerWidth+1;})
              &&d.documentElement.scrollWidth<=w.innerWidth;
            const probe=d.createElement('span');probe.style.color='var(--edge)';d.body.append(probe);
            for(const theme of ['light','dark']){
              d.documentElement.dataset.theme=theme;
              const track=d.querySelector('.u3a-track'),segment=d.querySelector('.u3a-mark rect:not(.u3a-missing)'),tic=d.querySelector('.u3a-mark[data-native-key="tic"] rect');
              checks[theme+'Quiet']=w.getComputedStyle(track).stroke===w.getComputedStyle(probe).color
                &&w.getComputedStyle(track).vectorEffect==='non-scaling-stroke'&&w.getComputedStyle(segment).stroke==='none'
                &&parseFloat(w.getComputedStyle(tic).opacity)<.5;
            }
            probe.remove();d.getElementById('tab-gap').click();
            const gap=p.signalCharts.find(chart=>chart.kind==='gap'),card=d.querySelector('.u3a-card.gap');
            checks.familyDenominator=card.textContent.includes("that family's displayed total")&&card.textContent.includes('50.0% of family total')
              &&d.getElementById('gap-panel').textContent.includes('all measured peptide feature signal');
            checks.areaFormatting=card.textContent.includes('1.2E+09 area;')&&card.textContent.includes('family share not available');
            const value=card.querySelector('.u3a-value[data-native-key="identified"]');value.focus();
            checks.familyHover=d.getElementById('chart-hover').textContent.includes(gap.rows[0].parts[0].familyShare.exact)
              &&d.getElementById('chart-hover').textContent.includes("this family's displayed total signal");
            value.click();checks.familyExact=d.getElementById('dialog-body').textContent.includes("Share of this family's displayed total")
              &&d.getElementById('dialog-body').textContent.includes(gap.rows[0].parts[0].familyShare.exact);
            d.getElementById('dialog-close').click();
            checks.gapFits=d.documentElement.scrollWidth<=w.innerWidth;
            w.dispatchEvent(new w.Event('beforeprint'));
            checks.print=card.querySelector('.u3a-details').open&&card.querySelector('.u3a-table').textContent.includes("this family's displayed total signal");
            w.dispatchEvent(new w.Event('afterprint'));
            checks.sourceUnchanged=source===JSON.stringify(JSON.parse(d.getElementById('fq-data').textContent).signalCharts);
            checks.noPictures=!d.querySelector('#panel-recovery img,#panel-gap img');
            checks.tooltips=[...d.querySelectorAll('.u3a-card button,.u3a-card summary')].every(control=>
              control.title.length>20&&control.title.split(/\.\s+(?=[A-Z])/).length<=2);
            if(name==='missing')checks.unavailable=!!d.querySelector('.u3a-missing')&&d.querySelector('#panel-recovery .u3a-value').textContent==='not available';
            return checks;
            """);
    }
}
