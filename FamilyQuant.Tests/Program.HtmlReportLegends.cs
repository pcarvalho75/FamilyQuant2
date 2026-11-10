using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlReportLegendsCommands() =>
    [new("--test-html-report-legends", 10522, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-report-legends", (args, _) =>
        {
            HtmlReportLegendTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-report-legends"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlReportLegendTests(string output)
    {
        var basis = HtmlReportTabsFixture();
        var source = new HtmlReportReference { Kind = "synthetic-ui-fixture" };
        var fields = basis.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        fields["sd"] = new("15", 15, "percentage points", source);
        fields["part-a"] = new("Part A", null, "", source); fields["part-b"] = new("Part B", null, "", source);
        fields["part-share-a"] = new("20", 20, "%", source); fields["part-share-b"] = new("80", 80, "%", source);
        fields["coverage-title"] = new("Stored coverage", null, "", source);
        fields["coverage-peptide"] = new("Observed peptide", null, "", source); fields["coverage-domain"] = new("Positioned region", null, "", source);
        var families = basis.Composition.Families.Select((family, index) =>
        {
            var runs = new List<HtmlFamilyRun>();
            for (int i = 0; i < 3; i++)
            {
                string name = "run-name-" + i, share = "run-share-" + family.Id + "-" + i;
                fields[name] = new("Run " + (i + 1), null, "", source);
                double value = index == 0 ? 10 + i * 15 : 90 - i * 15;
                fields[share] = new(value.ToString(System.Globalization.CultureInfo.InvariantCulture), value, "%", source);
                runs.Add(new(name, share, null));
            }
            return family with { Runs = runs, Sd = "sd", Parts = new[] { new HtmlFamilyPart("subclass", "part-a", "part-share-a"), new HtmlFamilyPart("subclass", "part-b", "part-share-b") },
                Coverage = index == 0 ? new[] { new HtmlReportCoverage { Title = "coverage-title", Length = 5, SourceFigureId = "figure:protein_coverage_fixture",
                    Peptides = new[] { new HtmlCoverageSpan(1, 2, "coverage-peptide", "peptide") }, Domains = new[] { new HtmlCoverageSpan(3, 5, "coverage-domain", "domain") } } }
                    : new[] { new HtmlReportCoverage { Title = "coverage-title", Png = basis.Quality[0].Png, SourceFigureId = "figure:protein_coverage_fallback" } } };
        }).ToArray();
        string[] files = { "xic_by_mass", "isotopes_credited", "percent_ion_current_by_mass", "recovery", "signal_by_run", "signal_uplift", "family_contribution",
            "family_composition", "family_subclasses", "family_subclass_ring", "abundance_diversity", "family_signal_vs_denovo_share", "family_signal_sources",
            "family_source_sequence_counts", "database_gap", "identity_sweep", "identity_scan", "de_novo_score_distribution", "de_novo_consolidation", "peptide_representatives",
            "family_world_rates_1", "de_novo_source_venn", "de_novo_accepted_score_agreement", "de_novo_peaks_deepnovo_agreement", "database_search_concordance", "de_novo_alternative_triage",
            "de_novo_variant_comparison", "de_novo_source_effect_comparison" };
        var figures = files.Select(file => new HtmlReportFigure("quality-title", "caption", basis.Quality[0].Png, "figure:" + file + ".png")).ToArray();
        var payload = basis with { Fields = fields, Composition = basis.Composition with { Families = families }, Results = figures };
        HtmlReportBrowserChecks(output, new[] { ("phone-legends", payload, 360), ("desktop-legends", payload, 1280) }, """
            const checks={}, sentences=s=>s.split(/[.!?]+/).filter(p=>p.trim()).length;
            const before=JSON.stringify(JSON.parse(d.getElementById('fq-data').textContent).fields);
            const figures=[...d.querySelectorAll('.figure-grid .report-figure')];
            checks.everyFigure=figures.length===25&&figures.every(f=>{const c=f.querySelector(':scope > figcaption.chart-legend');return c&&c.previousElementSibling?.tagName==='BUTTON'&&sentences(c.textContent)>=2&&sentences(c.textContent)<=4;});
            checks.specificLegends=figures.filter(f=>f.dataset.figure.endsWith('.png')).every(f=>!f.querySelector('figcaption').textContent.includes('full report caption'));
            checks.interactiveCharts=['composition-ring','family-ranking','dot-chart','family-contribution-chart'].every(id=>{const c=d.getElementById(id),legend=d.getElementById(c.getAttribute('aria-describedby'));return legend&&legend.classList.contains('chart-legend')&&sentences(legend.textContent)>=2&&sentences(legend.textContent)<=4;});
            checks.fixedBases=figures.every(f=>f.querySelector('figcaption').textContent.includes('keeps its recorded base when Normalize to changes'));
            const ranking=d.getElementById('ranking-legend').textContent;
            checks.meanThree=ranking.includes('mean of 3 runs')&&[...d.querySelectorAll('.rank-run-count')].every(c=>c.textContent==='mean of 3 runs');
            checks.baseName=ranking.includes('Percent of the chosen base: credited family signal.');
            checks.range=ranking.includes('lowest to highest run')&&ranking.includes('not a confidence interval');
            const expected='Sorts families by how much their share varies from run to run (sample SD in percentage points). It describes disagreement between runs, not a confidence interval.';
            const info=d.getElementById('spread-info');info.click();
            checks.spread=info.title.includes('opens help and changes no project values or files')&&d.getElementById('dialog-body').textContent===expected;
            d.getElementById('dialog-close').click();
            d.getElementById('tab-coverage').click();const choice=d.getElementById('coverage-family');choice.value='a';choice.dispatchEvent(new w.Event('change'));
            checks.coverage=d.getElementById('coverage-content').querySelector('.coverage-track')&&d.getElementById('coverage-content').querySelector('.chart-legend')?.textContent.includes('full protein length');
            checks.parts=[...d.querySelectorAll('.subclass-chart')].every(c=>c.querySelector('.subclass-bars')&&c.querySelector('.chart-legend')?.textContent.includes('all runs pooled'));
            choice.value='b';choice.dispatchEvent(new w.Event('change'));
            checks.fallbackCoverage=!!d.getElementById('coverage-content').querySelector('.report-figure > figcaption.chart-legend');
            checks.signal=d.getElementById('signal-panel').querySelector('.chart-legend')?.textContent.includes("that run's whole measured signal");
            checks.gap=d.getElementById('gap-panel').querySelector('.chart-legend')?.textContent.includes('reference total');
            checks.sd=d.querySelector('[data-figure="figure:family_composition.png"] figcaption').textContent.includes('one sample SD across runs');
            checks.retiredModelBounds=!d.querySelector('[data-figure="figure:de_novo_variant_comparison.png"] figcaption').textContent.includes('whiskers');
            d.getElementById('tab-mass').click();d.querySelector('[data-figure="figure:xic_by_mass.png"] > button').click();
            checks.popup=d.querySelector('#dialog-body .full-figure')?.nextElementSibling?.classList.contains('chart-legend')&&!!d.querySelector('#dialog-body [data-fq-field="caption"]');
            d.getElementById('dialog-close').click();
            w.dispatchEvent(new w.Event('beforeprint'));w.dispatchEvent(new w.Event('afterprint'));
            checks.printKeepsLegends=[...d.querySelectorAll('.figure-grid .report-figure')].every(f=>!!f.querySelector('figcaption.chart-legend'));
            checks.valuesUnchanged=before===JSON.stringify(JSON.parse(d.getElementById('fq-data').textContent).fields);
            return checks;
            """);
    }
}
