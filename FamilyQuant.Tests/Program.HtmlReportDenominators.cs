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
    private static IEnumerable<TestCommand> HtmlReportDenominatorsCommands() =>
    [new("--test-html-report-denominators", 10523, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-report-denominators", (args, _) =>
        {
            HtmlReportDenominatorTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-report-denominators"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlReportDenominatorTests(string output)
    {
        var payload = HtmlRankingBasisFixture();
        var unavailable = payload with { Composition = payload.Composition with { Families = payload.Composition.Families.Select(family => family with
        {
            IdentifiedOnly = new(), DetectorEnvelopes = new(), Runs = family.Runs.Select(run => run with { IdentifiedOnly = null, DetectorEnvelopes = null }).ToArray(),
        }).ToArray() } };
        var missing = payload with { Composition = payload.Composition with { Families = payload.Composition.Families.Select(family => family.Id == "a"
            ? family with { IdentifiedAndDeNovo = new() } : family).ToArray() } };
        var zeroFields = payload.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var family in payload.Composition.Families)
            foreach (string? id in new[] { family.IdentifiedOnly.Mean, family.IdentifiedOnly.Sd }.Concat(family.Runs.Select(run => run.IdentifiedOnly)))
                if (id != null) zeroFields[id] = zeroFields[id] with { Text = "0", Value = 0 };
        var zero = payload with { Fields = zeroFields };
        HtmlReportBrowserChecks(output, new[] { ("phone", payload, 360), ("desktop", payload, 1280), ("older-project", unavailable, 390),
            ("missing-family-mean", missing, 390), ("zero-psm", zero, 390), ("one-run", HtmlRankingBasisFixture(1), 390),
            ("legacy-payload", HtmlReportTabsFixture(), 390) }, """
            const checks = {}, p = JSON.parse(d.getElementById('fq-data').textContent);
            const fields = p.fields, families = p.composition.families;
            const bases = ['identifiedOnly', 'identifiedAndDeNovo', 'detectorEnvelopes', 'tic'];
            const labels = ['PSM only', 'PSM and de novo', 'Detector envelopes', 'TIC'];
            const controls = [...d.querySelectorAll('input[name="ranking-basis"]')];
            const legacy = name === 'legacy-payload';
            const rows = () => [...d.querySelectorAll('#family-ranking .rank-row')];
            const available = id => id && fields[id] && Number.isFinite(fields[id].value);
            const value = id => available(id) ? fields[id].value : -Infinity;
            function sortedRows(key, statistic) {
              const values = rows().map(row => families.find(f => f.id === row.dataset.family))
                .filter(f => !f.isOthers).map(f => value(f[key]?.[statistic]));
              return values.every((number, index) => index === 0 || values[index - 1] >= number);
            }
            checks.order = controls.map(c => c.value).join(',') === bases.join(',')
              && controls.every((c, i) => c.closest('label').textContent.startsWith(labels[i]));
            checks.tooltips = controls.every(c => c.title.length > 30 && c.closest('label').title === c.title);
            checks.viewport = controls.every(c => {
              const bounds = c.closest('label').getBoundingClientRect();
              return bounds.left >= 0 && bounds.right <= w.innerWidth;
            });
            checks.default = legacy ? controls.every(c => c.disabled && !c.checked)
              : d.querySelector('input:checked[name="ranking-basis"]')?.value === 'identifiedAndDeNovo';
            checks.disabledReasons = controls.every(c => {
              const enabled = families.some(f => available(f[c.value]?.mean));
              const reason = d.getElementById(c.id + '-reason');
              return c.disabled === !enabled && reason.hidden === enabled
                && (enabled || c.getAttribute('aria-describedby') === reason.id);
            });
            checks.availability = name === 'older-project'
              ? controls[0].disabled && controls[2].disabled && !controls[1].disabled && !controls[3].disabled
              : legacy || controls.every(c => !c.disabled);
            if (legacy) {
              checks.legacy = d.getElementById('ranking-base-name').textContent === 'credited family signal'
                && !d.getElementById('basis-compatibility').hidden;
              return checks;
            }
            const originalRing = [...d.querySelectorAll('#composition-ring path[d]')]
              .map(path => path.getAttribute('d')).join('|');
            for (const [index, key] of bases.entries()) {
              const choice = controls[index];
              if (choice.disabled) {
                const before = d.getElementById('ranking-base-name').textContent;
                choice.click();
                checks[key + 'Disabled'] = d.getElementById('ranking-base-name').textContent === before;
                continue;
              }
              choice.focus();
              checks[key + 'Focus'] = d.activeElement === choice;
              const previousSpread = d.getElementById('rank-sort-spread').getAttribute('aria-pressed');
              choice.click();
              checks[key + 'PreservesSort'] = name === 'one-run' ? d.getElementById('rank-sort').hidden
                : d.getElementById('rank-sort-spread').getAttribute('aria-pressed') === previousSpread;
              d.getElementById('rank-sort-share').click();
              checks[key + 'Legend'] = d.getElementById('ranking-base-name').textContent === labels[index]
                && d.getElementById('ranking-legend').textContent.includes('mean of ' + (name === 'one-run' ? '1 run' : '2 runs'));
              checks[key + 'Sort'] = rows().length === families.length && sortedRows(key, 'mean');
              checks[key + 'Means'] = rows().every(row => {
                const family = families.find(f => f.id === row.dataset.family);
                const id = family[key]?.mean, metric = row.querySelector('.rank-value');
                return available(id) ? metric.dataset.metric === id && metric.textContent.trim() === fields[id].text + ' %'
                  : metric.textContent === 'not available' && row.querySelector('.rank-run-count').textContent === 'mean unavailable';
              });
              checks[key + 'Points'] = rows().every(row => {
                const family = families.find(f => f.id === row.dataset.family);
                const ids = (family.runs || []).map(run => run[key]).filter(available);
                const actual = [...row.querySelectorAll('.rank-point')].map(point =>
                  [...point.querySelectorAll('[data-fq-field]')].at(-1)?.dataset.fqField);
                return JSON.stringify(ids) === JSON.stringify(actual);
              });
              const maximum = Math.max(1, ...families.flatMap(f => [value(f[key]?.mean), ...(f.runs || []).map(run => value(run[key]))]));
              checks[key + 'Geometry'] = rows().every(row => {
                const family = families.find(f => f.id === row.dataset.family);
                const mean = family[key]?.mean, expected = available(mean) ? fields[mean].value / maximum * 100 : 0;
                const runValues = (family.runs || []).map(run => run[key]).filter(available);
                return Math.abs(parseFloat(row.querySelector('.rank-fill').style.width) - expected) < 0.0001
                  && [...row.querySelectorAll('.rank-point')].every((point, i) =>
                    Math.abs(parseFloat(point.style.left) - fields[runValues[i]].value / maximum * 100) < 0.0001);
              });
              checks[key + 'Summary'] = !!d.querySelector('#selected-summary [data-metric="' + families.find(f => f.id === 'b')[key].mean + '"]');
              const first = rows()[0], point = first.querySelector('.rank-point');
              if (point) {
                point.click();
                const family = families.find(f => f.id === first.dataset.family);
                const run = family.runs.find(r => available(r[key]));
                checks[key + 'RunPopup'] = !!d.querySelector('#dialog-body [data-fq-field="' + run[key] + '"]');
                d.getElementById('dialog-close').click();
              }
              d.getElementById('rank-sort-spread').click();
              if (name === 'one-run')
                checks.oneRunSpread = d.getElementById('rank-sort').hidden && sortedRows(key, 'mean');
              else checks[key + 'Spread'] = sortedRows(key, 'sd');
              checks[key + 'Others'] = rows().at(-1).dataset.family === 'others';
            }
            const first = rows()[0];
            first.querySelector('.rank-name').click();
            d.getElementById('family-info').click();
            const family = families.find(f => f.id === first.dataset.family);
            const dl = d.querySelector('#dialog-body .family-share-bases');
            checks.popupFour = !!dl && [...dl.querySelectorAll('dt')].map(t => t.textContent).join(',') === labels.join(',')
              && bases.every(key => {
                const cell = dl.querySelector('[data-basis="' + key + '"]'), id = family[key]?.mean;
                return available(id) ? !!cell.querySelector('[data-fq-field="' + id + '"]') : cell.textContent.includes('unavailable');
              });
            d.getElementById('dialog-close').click();
            checks.ringGeometry = originalRing === [...d.querySelectorAll('#composition-ring path[d]')]
              .map(path => path.getAttribute('d')).join('|');
            checks.compatibilityFields = JSON.parse(d.getElementById('fq-data').textContent).composition.families
              .every(f => fields[f.mean].value === (f.id === 'b' ? 75 : 25));
            return checks;
            """);
    }

    private static HtmlReportPayload HtmlRankingBasisFixture(int runCount = 2)
    {
        var payload = HtmlReportTabsFixture();
        var fields = payload.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        var manifest = new QuantManifest { FamilyRunShares = new() { Policy = SharedPeptidePolicy.FamilyUnique,
            Runs = new()
            {
                new() { Run = "run-a", AssignedSignal = 75, MeasuredSignal = 100, IdentifiedSignal = 80, TotalIonCurrent = 1000 },
                new() { Run = "run-b", AssignedSignal = 120, MeasuredSignal = 200, IdentifiedSignal = 160, TotalIonCurrent = 100000 },
            } }, DerivedFractions = new()
            { new() { Run = "RUN-B.raw", DetectedPeptideLikeArea = 100 }, new() { Run = "run-a.raw", DetectedPeptideLikeArea = 400 } } };
        var specifications = new[] { (Id: 7, Ui: "a", Signal: new double[] { 10, 90 }, Identified: new double[] { 10, 90 }),
            (Id: 8, Ui: "b", Signal: new double[] { 60, 20 }, Identified: new double[] { 20, 0 }),
            (Id: 9, Ui: "others", Signal: new double[] { 5, 10 }, Identified: new double[] { 5, 5 }) };
        foreach (var item in specifications)
            manifest.FamilyRunShares.Families.Add(new() { FamilyUserId = item.Id, Runs = Enumerable.Range(0, runCount).Select(i => new FamilyRunShare
                { Run = i == 0 ? "run-a" : "run-b", Signal = item.Signal[i], IdentifiedSignal = item.Identified[i] }).ToList() });
        manifest.FamilyRunShares.Runs = manifest.FamilyRunShares.Runs.Take(runCount).ToList();
        var families = new List<HtmlReportFamily>();
        foreach (var item in specifications)
        {
            var original = item.Ui == "others" ? payload.Composition.Families[0] with { Id = "others", IsOthers = true } : payload.Composition.Families.Single(f => f.Id == item.Ui);
            if (item.Ui == "others")
            {
                fields["others-name"] = new("Other families", null, "", new() { Kind = "synthetic-ui-fixture" });
                original = original with { Name = "others-name" };
            }
            string? Field(string basis, string? run = null, string? statistic = null)
            {
                var source = new HtmlReportReference { Kind = "family-basis", Family = item.Ui == "others" ? "others:9" : item.Id.ToString(CultureInfo.InvariantCulture),
                    Study = "source", Run = run, Property = basis + (statistic == null ? "" : "/" + statistic), Scale = 100 };
                double? number = HtmlReportPayloadBuilder.ReadFamilyShare(source, manifest) * 100;
                if (!number.HasValue) return null;
                string id = item.Ui + "/" + basis + "/" + (run ?? statistic);
                fields[id] = new(number.Value.ToString("G6", CultureInfo.InvariantCulture), number, statistic == "sd" ? "pp" : "%", source);
                return id;
            }
            HtmlFamilyShareStatistics Statistics(string basis) => new(Field(basis, statistic: "mean"), Field(basis, statistic: "sd"));
            var runs = Enumerable.Range(0, runCount).Select(i =>
            {
                string run = i == 0 ? "run-a" : "run-b", name = "name-" + run;
                fields[name] = new(run, null, "", new() { Kind = "synthetic-ui-fixture" });
                return new HtmlFamilyRun(name, original.Mean, null) { IdentifiedOnly = Field("IdentifiedOnly", run), IdentifiedAndDeNovo = Field("IdentifiedAndDeNovo", run),
                    DetectorEnvelopes = Field("DetectorEnvelopes", run), Tic = Field("Tic", run) };
            }).ToArray();
            families.Add(original with { Runs = runs, IdentifiedOnly = Statistics("IdentifiedOnly"), IdentifiedAndDeNovo = Statistics("IdentifiedAndDeNovo"),
                DetectorEnvelopes = Statistics("DetectorEnvelopes"), Tic = Statistics("Tic") });
        }
        return payload with { Fields = fields, Composition = payload.Composition with { Families = families } };
    }
}
