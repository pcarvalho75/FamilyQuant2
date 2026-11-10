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
    private static IEnumerable<TestCommand> HtmlFamilyNativeCommands() =>
    [new("--test-html-family-native", 10531, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-html-family-native", (args, _) =>
        {
            HtmlFamilyNativeTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "html-family-native"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static (QuantManifest Manifest, FamilyClusterFile Project, FamilyEnvelopeQuantResult Families) HtmlFamilyNativeFixture()
    {
        var (manifest, project) = StoredCompositionFixture();
        string fingerprint = FamilyStudyMembership.Fingerprint(project);
        manifest.FamilyProteinCounts = new() { MembershipSha256 = fingerprint, Families = Enumerable.Range(1, 3).Select(id =>
            new FamilyProteinCount { FamilyUserId = id, GreedyProteinCount = id + 2, UniquePeptideProteinCount = 1,
                SelectedProteinIdentifiers = Enumerable.Range(1, id + 2).Select(index => "P" + id + "-" + index).ToList() }).ToList() };
        foreach (var total in manifest.FamilyRunShares!.Runs)
        {
            total.IdentifiedSignal = total.MeasuredSignal * .6;
            total.TotalIonCurrent = total.MeasuredSignal * 2;
            manifest.PerRunTic[total.Run] = total.TotalIonCurrent.Value;
            manifest.DerivedFractions.Add(new() { Run = total.Run, DetectedPeptideLikeArea = total.MeasuredSignal * .8 });
        }
        const double areaScale = 1_234_567.89;
        var families = new FamilyEnvelopeQuantResult
        {
            MeasuredIdentifiedEnvelope = manifest.FamilyRunShares.Runs.Sum(run => run.IdentifiedSignal!.Value) * areaScale,
            MeasuredDeNovoEnvelope = manifest.FamilyRunShares.Runs.Sum(run => run.MeasuredSignal - run.IdentifiedSignal!.Value) * areaScale,
        };
        foreach (var family in manifest.FamilyRunShares.Families)
        {
            foreach (var run in family.Runs)
            {
                run.IdentifiedSignal = run.Signal * (run.Run == "heavy" ? .8 : .4);
                var total = manifest.FamilyRunShares.Runs.Single(total => total.Run == run.Run);
                run.ShareOfTic = run.Signal / total.TotalIonCurrent;
            }
            double identified = family.Runs.Sum(run => run.IdentifiedSignal!.Value) * areaScale, deNovo = family.Runs.Sum(run => run.Signal - run.IdentifiedSignal!.Value) * areaScale;
            families.Families.Add(new() { FamilyUserId = family.FamilyUserId, Description = "Family " + family.FamilyUserId,
                IdentifiedEnvelope = identified, DeNovoEnvelope = deNovo, UniqueIdentifiedEnvelope = identified, UniqueDeNovoEnvelope = deNovo,
                SplitIdentifiedEnvelope = identified, SplitDeNovoEnvelope = deNovo });
            int id = family.FamilyUserId;
            families.SourceSequenceSummary.Families.Add(new() { FamilyUserId = id, Description = "Family " + id,
                PeaksDeNovoOnlyExclusiveDistinctSequences = id + 2, SharedPeaksDeepNovoDistinctSequences = 1, DeepNovoExclusiveDistinctSequences = id + 3,
                PeaksDeNovoOnlyTotalDistinctSequences = id + 3, DeepNovoTotalDistinctSequences = id + 4, DeNovoUnionDistinctSequences = id * 2 + 6,
                SearchDistinctSequences = id + 5 });
        }
        families.UnattributedIdentifiedEnvelope = families.MeasuredIdentifiedEnvelope - families.Families.Sum(family => family.IdentifiedEnvelope);
        families.UnattributedDeNovoEnvelope = families.MeasuredDeNovoEnvelope - families.Families.Sum(family => family.DeNovoEnvelope);
        manifest.FamilyWorldResult = new() { MinimumTagLength = FamilyWorldOptions.DefaultMinimumTagLength, CompositionRules = FamilyWorldErrors.CompositionRule,
            Composition = Enumerable.Range(1, 3).Select(id => new FamilyWorldCompositionRow { Family = "Family " + id,
                CuratedUserIds = new() { id }, IdentifiedEnvelope = (100 * id + .123456789) * 1e9, DatabaseDeNovoEnvelope = 20 * id * 1e9, WorldDeNovoEnvelope = 5 * id * 1e9 }).ToList() };
        return (manifest, project, families);
    }

    private static void HtmlFamilyNativeTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var (manifest, project, families) = HtmlFamilyNativeFixture();
        var export = ReportProjectExporter.Build(SyntheticAnalyticsRows(), manifest, project, Path.Combine(output, "figures"), familyResults: families);
        var payload = HtmlReportPayloadBuilder.Build(export.Document, export.SupplementSheets, context: export.HtmlContext);
        payload = HtmlReportFigures.Build(payload, export.Document);
        var figures = payload.FamilyFigures.Figures;
        Check("U3c replaces all seven Families picture types", new[] { "composition", "sources", "scatter", "ring", "subclasses", "diversity", "counts" }
            .All(kind => figures.Any(figure => figure.Kind == kind)));
        Check("U3c HTML omits every Families picture while Word retains them", payload.Results.All(figure => !HtmlFamilyFigurePacket.Owns(figure.SourceFigureId))
            && export.Document.Sections.SelectMany(section => section.Blocks).OfType<ReportFigure>().Count(figure => HtmlFamilyFigurePacket.Owns(figure.Id)) == figures.Count);
        double? Value(HtmlFamilyFigureNumber? number) => number == null ? null : payload.Fields[number.Exact].Value;
        var native = ReportStudyFigures.Plan(manifest, project);
        foreach (var chart in native.Figures)
        {
            var figure = figures.SingleOrDefault(figure => figure.Id == "figure:" + chart.Plot.FileName);
            if (figure == null) continue;
            var fixedRows = figure.Views[figure.UsesNormalization ? "credited" : "fixed"].Rows;
            if (chart.Plot.Chart is FamilyCompositionChart composition)
                Check("U3c composition exact means and SD endpoints equal native", composition.Rows.All(row =>
                {
                    var html = fixedRows.Single(html => html.Key == row.Key);
                    return Value(html.X) == row.MeanPercent && Value(html.Lower) == row.MeanPercent - row.SdPercent && Value(html.Upper) == row.MeanPercent + row.SdPercent;
                }));
            if (chart.Plot.Chart is FamilySubclassChart subclasses)
                Check("U3c subclass amounts equal native pooled parts", subclasses.Rows.All(row =>
                {
                    var html = fixedRows.Single(html => html.Key == row.Key);
                    return Value(html.X) == row.MeanPercent && html.Parts.Select(part => Value(part.Value)).SequenceEqual(row.Subclasses.Select(part => (double?)part.Percent));
                }));
            if (chart.Plot.Chart is FamilyRingChart ring)
                Check("U3c ring arcs equal native canonical groups and parts", ring.Families.Select(group => group.Percent).SequenceEqual(fixedRows.Select(row => Value(row.X)!.Value))
                    && ring.Families.SelectMany(group => group.Parts).Select(part => part.Percent).SequenceEqual(fixedRows.SelectMany(row => row.Parts).Select(part => Value(part.Value)!.Value)));
            if (chart.Plot.Chart is FamilySignalOriginChart sources)
                Check("U3c source areas equal native stored amounts", sources.Rows.Select(row => row.TotalEnvelope).SequenceEqual(fixedRows.Select(row => Value(row.X)!.Value))
                    && sources.Rows.SelectMany(row => new[] { row.IdentifiedEnvelope, row.DatabaseDeNovoEnvelope, row.WorldDeNovoEnvelope })
                        .SequenceEqual(fixedRows.SelectMany(row => row.Parts).Select(part => Value(part.Value)!.Value)));
            if (chart.Plot.Chart is FamilyAbundanceDiversityChart diversity)
                Check("U3c diversity exact points equal native", diversity.Points.All(point => fixedRows.Any(row => row.Key == point.Key
                    && Value(row.X) == point.SharePercent && Value(row.Y) == point.ProteinCount)));
        }
        var scatter = figures.Single(figure => figure.Kind == "scatter").Views["fixed"].Rows;
        var plan = ReportFigureRenderer.Plan(AnalyticsBuilder.Build(SyntheticAnalyticsRows(), manifest.PerRunTic), manifest, project, families,
            FamilyReportThreshold.Build(manifest.FamilyRunShares!, project));
        var nativeScatter = (FamilySignalShareChart)plan.Charts.Single(figure => figure.FileName == PresentationFigures.FamilySignalShareFile).CreateChart();
        Check("U3c scatter uses the native point reader without a second formula", nativeScatter.Rows.All(row =>
        {
            var value = FamilySignalShareChart.Value(row);
            var html = scatter.Single(html => html.Key == row.FamilyUserId.ToString(CultureInfo.InvariantCulture));
            return Value(html.X) == value.Signal && Value(html.Y) == value.DeNovoShare * 100;
        }));
        var counts = figures.Where(figure => figure.Kind == "counts").SelectMany(figure => figure.Views["fixed"].Rows).ToArray();
        Check("U3c source sequence counts equal native disjoint census", counts.All(row => row.Parts.Sum(part => Value(part.Value)!.Value) == Value(row.X))
            && counts.Length > 0 && counts.All(row => row.Y != null));
        foreach (var figure in figures.Where(figure => figure.UsesNormalization))
            foreach (var (key, sourceBasis) in new[] { ("identifiedOnly", "IdentifiedOnly"), ("identifiedAndDeNovo", "IdentifiedAndDeNovo"), ("detectorEnvelopes", "DetectorEnvelopes"), ("tic", "Tic") })
                Check("U3c " + figure.Kind + " " + key + " equals stored native share reader", figure.Views[key].Rows.All(row =>
                {
                    string family = row.Key == "others" ? "others:3" : row.Key;
                    double? expected = HtmlReportPayloadBuilder.ReadFamilyShare(new() { Kind = "family-basis", Family = family, Study = "source", Property = sourceBasis + "/mean" }, manifest) * 100;
                    return Value(row.X) == expected && row.RunText.Contains("3 runs", StringComparison.Ordinal);
                }));
        var sourceRows = figures.Single(figure => figure.Kind == "sources").Views["fixed"].Rows;
        Check("U3c source hovers carry program-computed within-family percentages", sourceRows.All(row => row.Parts.All(part =>
            Value(part.FamilyPercent) == 100 * Value(part.Value) / Value(row.X))));
        Check("U3c raw areas use the native G5 formatter and retain exact values", figures.Where(figure => figure.Kind is "sources" or "scatter")
            .SelectMany(figure => figure.Views["fixed"].Rows).All(row => payload.Fields[row.X!.Display].Source.Format == "G5"
                && payload.Fields[row.X.Display].Text == Value(row.X)!.Value.ToString("G5", CultureInfo.InvariantCulture)
                && payload.Fields[row.X.Exact].Source.Format == "R") && sourceRows.Any(row => Value(row.X) > 1e9));
        Check("U3c spread keeps native means with a readable negative-to-positive percent axis", figures.Where(figure => figure.Kind == "composition")
            .All(figure => figure.Title.StartsWith("Run-to-run spread of family shares", StringComparison.Ordinal)
                && figure.Views.Values.All(view => view.XTicks.Any(tick => tick.Position == 0 && tick.Label == "0%")
                    && view.XTicks.All(tick => tick.Label.EndsWith('%'))))
            && figures.Single(figure => figure.Kind == "composition").Views["identifiedAndDeNovo"].XTicks.Any(tick => tick.Position < 0));
        Check("U3c fixed plots explicitly name their own pooling/base", figures.Where(figure => !figure.UsesNormalization).All(figure =>
            figure.Views["fixed"].Legend.Contains("All runs pooled", StringComparison.Ordinal) && figure.Views["fixed"].Legend.Contains("Normalize to", StringComparison.Ordinal)));
        var omit = HtmlReportPayloadBuilder.Build(export.Document, export.SupplementSheets,
            new() { ExcludedIds = new[] { figures[0].Id } }, export.HtmlContext);
        Check("U3c honors existing figure selection", omit.FamilyFigures.Figures.All(figure => figure.Id != figures[0].Id));
        string report = Path.Combine(output, "synthetic.docx");
        ReportProjectExporter.Write(export, report);
        string htmlPath = HtmlReportWriter.PathForReport(report);
        var actual = ReadHtmlPayload(htmlPath);
        Check("U3c exported native values bind to actual native worksheets", ReportNumberTrace.RunHtmlBindings(actual, report,
            Path.Combine(output, "synthetic_supplement.xlsx"), project, manifest, Path.Combine(output, "trace.csv")) == 0);
        Console.WriteLine("U3c synthetic page: " + htmlPath + "; " + new FileInfo(htmlPath).Length.ToString(CultureInfo.InvariantCulture) + " bytes.");
        HtmlReportBrowserChecks(Path.Combine(output, "browser-check"), new[] { ("light-desktop", actual, 1366), ("dark-desktop", actual, 1366), ("light-phone", actual, 390), ("dark-phone", actual, 390) }, """
            const checks={};d.documentElement.dataset.theme=name.startsWith('dark')?'dark':'light';
            const host=d.getElementById('family-figures');
            checks.sevenNativeKinds=new Set([...host.querySelectorAll('[data-native-family-figure]')].map(el=>el.dataset.kind)).size===7;
            checks.noFamiliesPictures=d.getElementById('panel-families').querySelectorAll('img').length===0;
            checks.nativeSvg=[...host.querySelectorAll('.u3c-figure')].every(el=>el.querySelector('svg'));
            checks.legends=[...host.querySelectorAll('.u3c-figure')].every(el=>el.querySelector('.u3c-base')?.textContent&&el.querySelector('figcaption')?.textContent);
            checks.keyboard=[...host.querySelectorAll('.u3c-hit')].every(el=>el.getAttribute('tabindex')==='0'&&el.getAttribute('title')&&el.getAttribute('aria-label'));
            checks.noPageOverflow=d.documentElement.scrollWidth<=w.innerWidth+1;
            const sections=[...host.children];
            checks.spreadTitle=sections[0]?.dataset.kind==='composition'&&sections[0].querySelector('h3')?.textContent.startsWith('Run-to-run spread of family shares');
            checks.detailOrder=sections[1]?.dataset.kind==='diversity'&&sections[2]?.classList.contains('u3c-pooled');
            const pooled=host.querySelector('.u3c-pooled');
            checks.pooledGroup=pooled?.querySelector('h2')?.textContent==='Pooled details'&&pooled.querySelector('p').textContent.includes('Changing Normalize to does not change')
              &&[...pooled.querySelectorAll('.u3c-figure')].map(el=>el.dataset.kind).join(',')==='subclasses,ring,sources,scatter,counts';
            const spread=host.querySelector('[data-kind="composition"]');
            const labels=[...spread.querySelectorAll('.u3c-scale text')].map(el=>el.textContent);
            checks.spreadAxis=labels.includes('0%')&&labels.some(label=>label.startsWith('-')&&label.endsWith('%'))&&spread.querySelectorAll('.u3c-zero').length>=3;
            const source=host.querySelector('[data-kind="sources"] .u3c-hit');source.dispatchEvent(new w.Event('pointerenter'));
            const tooltip=d.getElementById('chart-hover').textContent;
            checks.sourcePartHovers=(tooltip.match(/% of this family's total/g)||[]).length===3&&tooltip.includes('E+');source.dispatchEvent(new w.Event('pointerleave'));
            checks.nativeAreaLabels=host.querySelector('[data-kind="sources"] .u3c-row-value').textContent.includes('E+');
            for(const key of ['identifiedOnly','identifiedAndDeNovo','detectorEnvelopes','tic']){
              const radio=d.querySelector('input[name="normalization-basis"][value="'+key+'"]');radio.checked=true;radio.dispatchEvent(new w.Event('change',{bubbles:true}));
              checks['base-'+key]=[...host.querySelectorAll('[data-kind="composition"],[data-kind="diversity"]')].every(el=>el.dataset.base===key&&el.querySelector('svg'));
            }
            const target=host.querySelector('.u3c-hit');target.focus();target.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Enter',bubbles:true}));
            checks.exactValueDialog=d.getElementById('detail-dialog').open&&d.getElementById('dialog-body').textContent.includes('Base');
            return checks;
            """);
    }
}
