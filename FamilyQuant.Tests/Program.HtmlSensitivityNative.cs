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
    private static IEnumerable<TestCommand> HtmlSensitivityNativeCommands() =>
    [new("--test-html-sensitivity-native", 10549, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-html-sensitivity-native", (args, _) =>
        {
            HtmlSensitivityNativeTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "html-sensitivity-native"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlSensitivityNativeTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var (manifest, project, families) = HtmlFamilyNativeFixture();
        project.ClusteringIdentity = 80;
        manifest.PresentationIdentitySweep = new()
        {
            new() { Identity = 95, Groups = 60, TargetGroups = 30, NonContaminantTargetGroups = 28 },
            new() { Identity = 80, Groups = 42, TargetGroups = 21, NonContaminantTargetGroups = 20 },
            new() { Identity = 70, Groups = 20, TargetGroups = 10, NonContaminantTargetGroups = 9 },
            new() { Identity = 60, Groups = 10, TargetGroups = 6, NonContaminantTargetGroups = 5, MixedGroups = 2, Score = .2 },
            new() { Identity = 40, Groups = 3, TargetGroups = 2, NonContaminantTargetGroups = 2, MixedGroups = 1, Score = 1.0 / 3 },
            new() { Identity = 20, Groups = 4, TargetGroups = 3, NonContaminantTargetGroups = 3, MixedGroups = 2, Score = .5 },
        };
        var names = new[] { DeNovoSource.DenovoOnly.DisplayName, DeNovoSource.DeNovoAll.DisplayName, DeNovoSource.DeepNovo.DisplayName,
            DeNovoVariantComparison.OnlyPlusDeepNovoVariantName, DeNovoVariantComparison.AllPlusDeepNovoVariantName };
        manifest.PresentationDeNovoVariants = names.Select((name, index) => new DeNovoVariantSummary
        {
            Variant = name, Sources = index < 3 ? name : (index == 3 ? names[0] : names[1]) + " + " + names[2],
            GenerationId = "synthetic", GeneratedUtc = "2026-01-01T00:00:00Z", DatasetFingerprint = "synthetic",
            DeNovoInputFingerprint = name, QuantitationSettings = "synthetic", AppVersion = "synthetic", EnvelopeModelMethod = NativeQuantFractions.Method,
            ConfidenceCutoffNote = index == 2 ? "SCORE >= 0.8" : "ALC >= 80; caller-specific cutoff retained",
            IdentifiedFractionOfTic = .28, DeNovoModelEstimateFractionOfTic = .1 + index * .025,
            CombinedModelEstimateFractionOfTic = .28 + .1 + index * .025,
            SearchRepresentatives = 100, DeNovoRepresentatives = 20 + index, TotalRepresentatives = 120 + index,
            DistinctDeNovoSequencesNoXic = 75 + index * 20,
        }).ToList();
        string before = JsonSerializer.Serialize(new { manifest.PresentationIdentitySweep, manifest.PresentationDeNovoVariants });
        var export = ReportProjectExporter.Build(SyntheticAnalyticsRows(), manifest, project, Path.Combine(output, "figures"), familyResults: families);
        var payload = HtmlReportFigures.Build(HtmlReportPayloadBuilder.Build(export.Document, export.SupplementSheets, context: export.HtmlContext), export.Document);
        var figures = payload.SensitivityFigures.Figures;
        double Value(HtmlSensitivityNumber number) => payload.Fields[number.Exact].Value!.Value;
        var nativePlan = ReportFigureRenderer.Plan(AnalyticsBuilder.Build(SyntheticAnalyticsRows(), manifest.PerRunTic), manifest, project, families);
        Check("U3d four native kinds replace every Sensitivity image while Word keeps all four", figures.Select(figure => figure.Kind).Order()
            .SequenceEqual(new[] { "effects", "identity", "reference", "variants" }) && payload.Sensitivity.Count == 0
            && export.Document.Sections.Where(section => section.Id == ReportSectionIds.Sensitivity).SelectMany(section => section.Blocks)
                .OfType<ReportFigure>().Count(figure => HtmlSensitivityFigurePacket.Owns(figure.Id)) == 4);
        foreach (var planned in nativePlan.Charts.Where(chart => HtmlSensitivityFigurePacket.Owns("figure:" + chart.FileName)))
        {
            var figure = figures.Single(figure => figure.Id == "figure:" + planned.FileName);
            var native = planned.CreateChart();
            if (native is IdentityScanChart scan)
            {
                var rows = scan.Rows.OrderByDescending(row => row.Identity).ToArray();
                Check("U3d identity quantities equal native reported counts and stored mixing", figure.Panels[0].Rows.Select(row => Value(row.Parts[0].Value))
                    .SequenceEqual(rows.Select(row => (double)row.ReportedTargetGroups)) && figure.Panels[1].Rows.Select(row => Value(row.Parts[0].Value))
                    .SequenceEqual(rows.Select(row => row.Score * 100)) && figure.Panels[0].Rows.Select(row => Value(row.X!)).SequenceEqual(rows.Select(row => (double)row.Identity)));
                Check("U3d identity floor/current markers equal native rules and remain distinct", figure.Panels.All(panel =>
                    Value(panel.Markers.Single(marker => marker.Label == "Clean-range floor").Identity) == IdentityScanPlot.CleanFloor(rows)
                    && Value(panel.Markers.Single(marker => marker.Label == "This file").Identity) == scan.CurrentIdentity));
                var nativePoints = IdentityScanPlot.Map(rows, new System.Windows.Rect(0, 0, 1, 1), row => row.ReportedTargetGroups);
                Check("U3d identity positions use the native strict-to-loose zero-based scales", figure.Panels[0].Rows.Select((row, index) =>
                    Math.Abs(nativePoints[index].Y - (1 - Value(row.Parts[0].Value) / figure.Panels[0].MaximumY)) < 1e-12).All(equal => equal));
            }
            if (native is ReferenceIdentitySweepChart reference)
                Check("U3d reference counts use native inclusion and threshold filter", figure.Panels[0].Rows.Select(row => Value(row.X!)).SequenceEqual(reference.Rows.Select(row => (double)row.Identity))
                    && figure.Panels[0].Rows.All(row => Value(row.X!) >= 25)
                    && figure.Panels[0].Rows.Select(row => Value(row.Parts[0].Value)).SequenceEqual(reference.Rows.Select(row => (double)row.TargetGroups))
                    && figure.Panels[0].Rows.Select(row => Value(row.Parts[1].Value)).SequenceEqual(reference.Rows.Select(row => (double)row.ReportedTargetGroups)));
            if (native is DeNovoVariantComparisonChart variants)
                Check("U3d stored source bars equal each native identified/de novo/combined fraction", variants.Rows.Select((row, index) =>
                    Value(figure.Panels[0].Rows[index].Parts[0].Value) == row.IdentifiedFractionOfTic * 100
                    && Value(figure.Panels[0].Rows[index].Parts[1].Value) == row.DeNovoModelEstimateFractionOfTic * 100
                    && Value(figure.Panels[0].Rows[index].Total!) == row.CombinedModelEstimateFractionOfTic * 100).All(equal => equal));
            if (native is DeNovoTalkComparisonChart effects)
                Check("U3d source effects retain native sequence diversity and common baseline", effects.Rows.Select((row, index) =>
                    Value(figure.Panels[1].Rows[index].Parts[0].Value) == row.ObservedSearchAssignedFractionOfTic * 100
                    && Value(figure.Panels[1].Rows[index].Parts[1].Value) == row.DeNovoModelEstimateFractionOfTic * 100
                    && Value(figure.Panels[1].Rows[index].Total!) == row.CombinedAssignedFractionOfTic * 100
                    && (index == 0 || Value(figure.Panels[0].Rows[index - 1].Total!) == row.CarriedForwardDistinctSequences)).All(equal => equal));
        }
        Check("U3d reads stored studies without mutation", before == JsonSerializer.Serialize(new { manifest.PresentationIdentitySweep, manifest.PresentationDeNovoVariants }));
        Check("U3d every panel names its fixed base and legend explains Normalize to", figures.All(figure => figure.Legend.Contains("Changing Normalize to", StringComparison.Ordinal)
            && figure.Panels.All(panel => panel.Basis.Length > 0)));
        var omitted = HtmlReportPayloadBuilder.Build(export.Document, export.SupplementSheets,
            new() { ExcludedIds = new[] { figures[0].Id } }, export.HtmlContext);
        Check("U3d excludes unchecked figures and entire Sensitivity section", omitted.SensitivityFigures.Figures.Count == 3
            && HtmlReportPayloadBuilder.Build(export.Document, export.SupplementSheets,
                new() { ExcludedIds = new[] { ReportSectionIds.Sensitivity } }, export.HtmlContext).SensitivityFigures.Figures.Count == 0);
        Check("U3d image adapter also applies later figure and section exclusions", HtmlReportFigures.Build(payload, export.Document,
            new() { ExcludedIds = new[] { figures[0].Id } }).SensitivityFigures.Figures.Count == 3
            && HtmlReportFigures.Build(payload, export.Document, new() { ExcludedIds = new[] { ReportSectionIds.Sensitivity } }).SensitivityFigures.Figures.Count == 0);
        var plainRows = new List<IdentityScanRow> { new() { Identity = 90, Groups = 5, TargetGroups = 5 }, new() { Identity = 60, Groups = 2, TargetGroups = 2 } };
        var plainPlan = new ReportFigurePlan(new[] { new ReportChart(ReportSectionIds.Sensitivity, "identity_scan.png", "Identity scan", "", () => new IdentityScanChart(plainRows)) }, Array.Empty<ReportFigureAvailability>());
        var plainPacket = HtmlSensitivityFigurePacket.Prepare(plainPlan);
        int field = 0;
        var plain = plainPacket.Build((label, source, unit, value) => "test:" + (++field).ToString(CultureInfo.InvariantCulture), null);
        Check("U3d matches native no-decoy behavior without a false mixing curve or floor", plain.Figures.Single().Panels.Count == 1
            && plain.Figures.Single().Panels[0].Markers.Count == 0);
        string report = Path.Combine(output, "synthetic.docx"); ReportProjectExporter.Write(export, report);
        string html = HtmlReportWriter.PathForReport(report); var actual = ReadHtmlPayload(html);
        Check("U3d every native field traces to the actual workbook", ReportNumberTrace.RunHtmlBindings(actual, report,
            Path.Combine(output, "synthetic_supplement.xlsx"), project, manifest, Path.Combine(output, "trace.csv")) == 0);
        Console.WriteLine("U3d synthetic page: " + html + "; " + new FileInfo(html).Length.ToString(CultureInfo.InvariantCulture) + " bytes.");
        HtmlReportBrowserChecks(Path.Combine(output, "browser-check"), new[] { ("light-desktop", actual, 1366), ("dark-desktop", actual, 1366), ("light-phone", actual, 390), ("dark-phone", actual, 390) }, """
            const checks={};d.documentElement.dataset.theme=name.startsWith('dark')?'dark':'light';
            d.getElementById('tab-sensitivity').click();const host=d.getElementById('sensitivity-figures');
            checks.fourKinds=new Set([...host.querySelectorAll('[data-native-sensitivity-figure]')].map(el=>el.dataset.kind)).size===4;
            checks.noPictures=d.getElementById('panel-sensitivity').querySelectorAll('img').length===0;
            checks.nativeSvg=[...host.querySelectorAll('.u3d-figure')].every(el=>el.querySelector('svg'));
            checks.fixedBases=[...host.querySelectorAll('.u3d-panel')].every(el=>el.querySelector('.u3d-base')?.textContent);
            checks.legends=[...host.querySelectorAll('.u3d-figure')].every(el=>el.querySelector('figcaption')?.textContent.includes('Changing Normalize to'));
            checks.keyboard=[...host.querySelectorAll('.u3d-hit')].every(el=>el.tabIndex===0&&el.getAttribute('title')&&el.getAttribute('aria-label'));
            checks.noOverflow=d.documentElement.scrollWidth<=w.innerWidth+1;
            checks.linePanelsKeepScale=[...host.querySelectorAll('.u3d-svg[role="group"]')].every(plot=>Math.abs(plot.viewBox.baseVal.width-plot.getBoundingClientRect().width)<2);
            const snapshot=host.innerHTML;
            for(const key of ['identifiedOnly','identifiedAndDeNovo','detectorEnvelopes','tic']){
              const radio=d.querySelector('input[name="normalization-basis"][value="'+key+'"]');radio.checked=true;radio.dispatchEvent(new w.Event('change',{bubbles:true}));
            }
            checks.fixedUnderNormalization=host.innerHTML===snapshot;
            const target=host.querySelector('.u3d-hit');target.focus();target.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Enter',bubbles:true}));
            checks.exactKeyboardDialog=d.getElementById('detail-dialog').open&&d.getElementById('dialog-body').textContent.includes('Base');
            return checks;
            """);
    }
}
