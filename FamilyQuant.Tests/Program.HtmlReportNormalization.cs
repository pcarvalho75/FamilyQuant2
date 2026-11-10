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
    private static IEnumerable<TestCommand> HtmlReportNormalizationCommands() =>
    [new("--test-html-report-normalization", 10524, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-report-normalization", (args, _) =>
        {
            HtmlReportNormalizationTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-report-normalization"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlReportNormalizationTests(string output)
    {
        Directory.CreateDirectory(output);
        HtmlReportPayload Native(string name, Action<QuantManifest>? change = null, bool excludeResults = false)
        {
            var fixture = HtmlSharePartsFixture();
            var manifest = fixture.Context.StoredStudies!;
            change?.Invoke(manifest);
            string before = JsonSerializer.Serialize(manifest);
            var export = new ReportProjectExport(fixture.Document, new Dictionary<string, string>(), Array.Empty<ReportPlacedFigure>())
                { HtmlContext = fixture.Context, SupplementSheets = fixture.Sheets };
            string path = Path.Combine(output, name + ".docx");
            ReportProjectExporter.WriteHtml(export, path, new() { ExcludedIds = new[] { excludeResults ? ReportSectionIds.Results : "family-run-shares" } });
            Check("normalization export preserves the stored study " + name, before == JsonSerializer.Serialize(manifest));
            return ReadHtmlPayload(HtmlReportWriter.PathForReport(path));
        }
        var payload = Native("native");
        Check("contribution run count comes from the stored study when run details are excluded",
            payload.Composition.ContributionRunCount == 2 && payload.Composition.Families.All(f => f.Runs.Count == 0));
        Check("normalization starts with native canonical families and Others plus unavailable residual rows",
            payload.Composition.ContributionRows.Select(row => row.Kind).SequenceEqual(new[] { "family", "others", "shared", "unattributed" }));
        var noDetector = Native("no-detector", manifest => manifest.DerivedFractions.Clear());
        var noParts = Native("old-parts", manifest =>
        {
            foreach (var family in manifest.FamilyRunShares!.Families)
                foreach (var run in family.Runs) run.IdentifiedSignal = null;
        });
        var partial = Native("partial-parts", manifest => manifest.FamilyRunShares!.Families[0].Runs[0].IdentifiedSignal = null);
        var zero = Native("zero-psm", manifest =>
        {
            foreach (var run in manifest.FamilyRunShares!.Families[0].Runs) run.IdentifiedSignal = 0;
        });
        var excluded = Native("excluded", excludeResults: true);
        Check("excluding Results omits contribution rows and their run-count metadata", excluded.Composition.ContributionRows.Count == 0
            && excluded.Composition.ContributionRunCount == null);
        var chartOnly = payload with { Composition = payload.Composition with { Families = Array.Empty<HtmlReportFamily>() } };
        HtmlReportBrowserChecks(Path.Combine(output, "page"), new[] { ("phone", payload, 360), ("desktop", payload, 1280),
            ("no-detector", noDetector, 390), ("old-parts", noParts, 390), ("partial-parts", partial, 390),
            ("zero-psm", zero, 390), ("chart-only", chartOnly, 390), ("excluded", excluded, 390) }, """
            const checks = {}, p = JSON.parse(d.getElementById('fq-data').textContent);
            const fields = p.fields, sourceRows = p.composition.contributionRows;
            const bases = ['identifiedOnly', 'identifiedAndDeNovo', 'detectorEnvelopes', 'tic'];
            const labels = ['PSM only', 'PSM and de novo', 'Detector envelopes', 'TIC'];
            const choices = [...d.querySelectorAll('input[name="normalization-basis"]')];
            const valid = id => !!id && fields[id] && Number.isFinite(fields[id].value);
            const rows = () => [...d.querySelectorAll('#family-contribution-chart .contribution-row')];
            const sourceFields = JSON.stringify(fields), snapshots = new Set();
            checks.singleGroup = choices.length === 4 && d.querySelectorAll('#normalization-bases').length === 1
              && !d.getElementById('report-normalization').closest('[role="tabpanel"]');
            checks.default = name === 'excluded' ? choices.every(choice => choice.disabled)
              : choices[1].checked && !choices[1].disabled;
            checks.noPicture = !d.querySelector('img[data-fq-figure^="figure:family_contribution"]');
            if (name === 'excluded') {
              checks.excluded = rows().length === 0 && !d.getElementById('contribution-empty').hidden;
              return checks;
            }
            checks.unavailableDetector = name !== 'no-detector' || choices[2].disabled
              && !d.getElementById(choices[2].id + '-reason').hidden;
            checks.runCount = d.getElementById('contribution-legend').textContent.includes('mean of 2 runs');
            const tabs = [...d.querySelectorAll('[role="tab"]')];
            for (const [index, key] of bases.entries()) {
              if (choices[index].disabled) continue;
              tabs[1].click();
              choices[index].click();
              checks[key + 'HiddenRedraw'] = rows().every(row => row.dataset.basis === key);
              tabs[0].click();
              const expected = sourceRows.filter(row => !['shared', 'unattributed'].includes(row.kind) || valid(row[key]?.total));
              checks[key + 'Rows'] = rows().map(row => row.dataset.row).join(',') === expected.map(row => row.id).join(',');
              checks[key + 'BothLegends'] = d.getElementById('ranking-base-name').textContent === labels[index]
                && d.getElementById('contribution-base-name').textContent === labels[index];
              checks[key + 'Ranking'] = [...d.querySelectorAll('#family-ranking .rank-row')].every(row => {
                const family = p.composition.families.find(f => f.id === row.dataset.family), id = family[key]?.mean;
                const metric = row.querySelector('.rank-value');
                return valid(id) ? metric.dataset.metric === id : metric.textContent === 'not available';
              });
              const maximum = Math.max(0, ...expected.map(row => valid(row[key]?.total) ? fields[row[key].total].value : 0)) || 1;
              checks[key + 'Parts'] = rows().every(element => {
                const row = expected.find(row => row.id === element.dataset.row), parts = row[key] || {};
                const total = element.querySelector('.contribution-total');
                const complete = [parts.total, parts.identified, parts.deNovo].every(valid);
                const components = [...element.querySelectorAll('.contribution-values [data-metric]')].map(value => value.dataset.metric);
                const expectedIds = [parts.identified, parts.deNovo].filter(valid);
                if (valid(parts.total) ? total.querySelector('[data-metric]')?.dataset.metric !== parts.total : total.textContent !== 'not available') return false;
                if (components.join(',') !== expectedIds.join(',')) return false;
                const segments = element.querySelectorAll('.contribution-part');
                if (!complete) return segments.length === 0 && element.querySelectorAll('.contribution-values .unavailable').length > 0;
                const identified = element.querySelector('.identified'), deNovo = element.querySelector('.de-novo');
                return segments.length === 2 && Math.abs(parseFloat(identified.style.width) - fields[parts.identified].value / maximum * 100) < 0.0001
                  && Math.abs(parseFloat(deNovo.style.width) - fields[parts.deNovo].value / maximum * 100) < 0.0001
                  && Math.abs(parseFloat(deNovo.style.left) - fields[parts.identified].value / maximum * 100) < 0.0001;
              });
              checks[key + 'OmittedResiduals'] = !rows().some(row => ['shared', 'unattributed'].includes(row.dataset.kind))
                && d.getElementById('contribution-limit-legend').textContent.includes("program's Results")
                && d.getElementById('contribution-limit-legend').textContent.includes('per-run totals are unavailable');
              const metric = d.querySelector('#family-contribution-chart .contribution-values [data-metric]');
              if (metric) {
                metric.focus();
                checks[key + 'Keyboard'] = d.activeElement === metric && metric.title.includes('exact');
                metric.click();
                checks[key + 'ExactValue'] = !!d.querySelector('#dialog-body [data-fq-field="' + metric.dataset.metric + '"]');
                d.getElementById('dialog-close').click();
              }
              snapshots.add(JSON.stringify(rows().map(row => [row.querySelector('.contribution-total [data-metric]')?.dataset.metric,
                ...[...row.querySelectorAll('.contribution-part')].map(part => [part.style.width, part.style.left])])));
            }
            checks.redraws = snapshots.size > 1;
            checks.zeroPsm = name !== 'zero-psm' || (() => {
              choices[0].click();
              const zero = rows().find(row => row.dataset.kind === 'family');
              return zero.querySelectorAll('.contribution-part').length === 2
                && [...zero.querySelectorAll('.contribution-part')].every(part => parseFloat(part.style.width) === 0)
                && zero.querySelector('.contribution-total').textContent.trim() === '0 %';
            })();
            checks.sourceFields = sourceFields === JSON.stringify(JSON.parse(d.getElementById('fq-data').textContent).fields);
            checks.noOverflow = d.documentElement.scrollWidth <= w.innerWidth;
            const sentenceCount = d.getElementById('contribution-legend').textContent.split(/[.!?]+/).filter(s => s.trim()).length;
            checks.plainLegend = sentenceCount >= 2 && sentenceCount <= 4;
            return checks;
            """);
    }
}
