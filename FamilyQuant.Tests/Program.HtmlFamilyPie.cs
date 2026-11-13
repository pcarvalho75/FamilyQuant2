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
    private static IEnumerable<TestCommand> HtmlFamilyPieCommands() =>
    [new("--test-html-family-pie", 10721, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-family-pie", (args, _) =>
        {
            HtmlFamilyPieTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-family-pie"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static (FamilyClusterFile File, List<QuantRow> Rows, QuantManifest Manifest, FamilyEnvelopeQuantResult Families) FamilyPieFixture()
    {
        var file = new FamilyClusterFile();
        var families = new FamilyEnvelopeQuantResult { MeasuredIdentifiedEnvelope = 50, MeasuredDeNovoEnvelope = 50,
            SharedIdentifiedEnvelope = 5, SharedDeNovoEnvelope = 5, UnattributedIdentifiedEnvelope = 5, UnattributedDeNovoEnvelope = 5 };
        var shares = new FamilyRunSharesResult { Runs = new() { new() { Run = "pie-run", AssignedSignal = 80,
            IdentifiedSignal = 50, MeasuredSignal = 100, TotalIonCurrent = 200 } } };
        for (int id = 1; id <= 20; id++)
        {
            string name = "Stored family " + id;
            file.MyExtendedFastaItems.Add(new() { UserID = id, SequenceIdentifier = "P" + id, Sequence = "AAAAAAAKCCCCCCCK", UserAnnotation = name });
            families.Families.Add(new() { FamilyUserId = id, Description = name, IdentifiedEnvelope = 7, DeNovoEnvelope = 7,
                UniqueIdentifiedEnvelope = 2, UniqueDeNovoEnvelope = 2, SplitIdentifiedEnvelope = 2.25, SplitDeNovoEnvelope = 2.25 });
            shares.Families.Add(new() { FamilyUserId = id, MeanShare = .05, MeanShareOfMeasuredSignal = .04, AvailableRunCount = 1,
                PooledSignal = 4, PooledShare = .05, Runs = new() { new() { Run = "pie-run", Signal = 4, IdentifiedSignal = 2,
                    ShareOfAssignedSignal = .05, ShareOfMeasuredSignal = .04, ShareOfTic = .02 } } });
        }
        shares.MembershipSha256 = FamilyStudyMembership.Fingerprint(file);
        var manifest = new QuantManifest { DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method, FamilyRunShares = shares,
            PerRunTic = new() { ["pie-run"] = 200 }, DerivedFractions = new() { new() { Run = "pie-run", DetectedPeptideLikeArea = 90 } } };
        return (file, new() { Qr("identified", "pie-run", 1, 2, "AAAAAAAK", 50, 50), Qr("denovo", "pie-run", 2, 2, "CCCCCCCK", 50, 50) }, manifest, families);
    }

    private static void HtmlFamilyPieTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var fixture = FamilyPieFixture();
        string before = JsonSerializer.Serialize(fixture.Families), studies = JsonSerializer.Serialize(fixture.Manifest);
        foreach (int top in new[] { 1, 15, 0 })
            foreach (var policy in new[] { SharedPeptidePolicy.FamilyUnique, SharedPeptidePolicy.SplitEqually })
            {
                var bars = FamilyContributionPlot.Fold(fixture.Families, top, policy);
                var pie = FamilyPieData.Build(fixture.Families, top, policy);
                Check("PI1 pie closes to 100.0 " + top + "/" + policy, pie.Unavailable == null && Math.Abs(pie.Slices.Sum(slice => slice.Percent) - 100) < 1e-10);
                Check("PI1 every pie bucket equals its native bar " + top + "/" + policy, pie.Slices.Count == bars.Count
                    && pie.Slices.Zip(bars).All(pair => pair.First.Kind == pair.Second.Kind
                        && pair.First.Percent == 100 * pair.Second.Total / FamilyContributionPlot.MeasuredTotal(fixture.Families)
                        && pair.First.IdentifiedPercent == pair.Second.Identified && pair.First.DeNovoPercent == pair.Second.DeNovo));
            }
        Check("PI1 unavailable measured total does not invent shares", FamilyPieData.Build(null).Unavailable != null
            && FamilyPieData.Build(new()).Slices.Count == 0);
        var incomplete = new FamilyEnvelopeQuantResult { MeasuredIdentifiedEnvelope = 100, UnattributedIdentifiedEnvelope = 40 };
        Check("PI1 incomplete buckets have an explanation", FamilyPieData.Build(incomplete).Unavailable != null);
        Check("PI1 the data projection preserves its source objects", before == JsonSerializer.Serialize(fixture.Families) && studies == JsonSerializer.Serialize(fixture.Manifest));
        var export = ReportProjectExporter.Build(fixture.Rows, fixture.Manifest, fixture.File, Path.Combine(output, "figures"), familyResults: fixture.Families);
        string word = Path.Combine(output, "report.docx"); ReportProjectExporter.Write(export, word);
        var payload = ReadHtmlPayload(HtmlReportWriter.PathForReport(word));
        var figure = payload.FamilyFigures.Pie!.Figure!;
        var view = figure.Views["fixed"];
        Check("PI1 payload has shown families then other, shared and unattributed", view.Rows.Count == 18
            && view.Rows.Take(15).All(row => int.TryParse(row.Key, out _))
            && view.Rows.Skip(15).Select(row => row.Key).SequenceEqual(new[] { "OtherFamilies", "Shared", "Unattributed" }));
        Check("PI1 payload values close to 100.0 and use a fixed base", !figure.UsesNormalization
            && Math.Abs(view.Rows.Sum(row => payload.Fields[row.X!.Exact].Value!.Value) - 100) < 1e-10
            && view.Basis == FamilyPieData.Basis && view.Legend.Contains("fixed base when Normalize to changes", StringComparison.Ordinal));
        Check("PI1 every exact pie value is workbook-bound", ReportNumberTrace.RunHtmlBindings(payload, word,
            ReportSupplementWorkbook.PathForReport(word), fixture.File, fixture.Manifest, Path.Combine(output, "trace.csv")) == 0);
        var modified = payload.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        string id = view.Rows[0].X!.Exact; modified[id] = modified[id] with { Value = 99 };
        Check("PI1 trace catches a replaced slice value", ReportNumberTrace.RunHtmlBindings(payload with { Fields = modified }, word,
            ReportSupplementWorkbook.PathForReport(word), fixture.File, fixture.Manifest, Path.Combine(output, "negative-trace.csv")) != 0);
        foreach (string excluded in new[] { ReportSectionIds.Results, "figure:" + AnalyticsFigures.FamilyContributionFile })
        {
            var selected = HtmlReportPayloadBuilder.Build(export.Document, export.SupplementSheets, new() { ExcludedIds = new[] { excluded } }, export.HtmlContext);
            Check("PI1 exclusions also omit the pie " + excluded, selected.FamilyFigures.Pie?.Figure == null);
        }
        var named = EffectivePanelFixture();
        named.File.FamilyPanel!.Import = new() { ColumnHeaders = new() { [FamilyQuant.Panels.Import.PanelImportFields.OtherName] = "Other label" } };
        named.File.FamilyPanel.Families.Single(family => family.Key == "main").Names.Add(new() { Name = "Other label", Text = "Alternate main" });
        named = PrepareEffectivePanelFixture(named.File, named.Rows);
        EffectivePanelFamilyStudy.Store(named.Manifest, named.Assigned, named.Hits, named.File);
        named.File.ReportFamilyNames = ReportFamilyNameChoice.OtherLanguageName;
        var namedExport = ReportProjectExporter.Build(named.Rows, named.Manifest, named.File, Path.Combine(output, "named"),
            familyResults: FamilyEnvelopeQuant.BuildFromAttributedPeptides(named.Assigned));
        var namedPayload = HtmlReportPayloadBuilder.Build(namedExport.Document, namedExport.SupplementSheets, context: namedExport.HtmlContext);
        Check("PI1 pie follows NM1 names", namedPayload.FamilyFigures.Pie!.Figure!.Views["fixed"].Rows
            .Any(row => namedPayload.Fields[row.Name].Text.Contains("Alternate main", StringComparison.Ordinal)));
        HtmlReportBrowserChecks(Path.Combine(output, "browser"), new[] { ("light-phone", payload, 390), ("dark-phone", payload, 390),
            ("light-desktop", payload, 1366), ("dark-desktop", payload, 1366), ("named", namedPayload, 390),
            ("older", payload with { FamilyFigures = payload.FamilyFigures with { Pie = null } }, 390) }, """
            const checks={}, p=JSON.parse(d.getElementById('fq-data').textContent), fields=p.fields;
            if(name.startsWith('dark')) d.documentElement.dataset.theme='dark';
            const button=d.getElementById('contribution-pie');
            if(name==='older') { checks.unavailable=button.disabled&&button.title.includes('Quantify'); return checks; }
            const figure=p.familyFigures.pie.figure, rows=figure.views.fixed.rows;
            const barView=d.getElementById('contribution-bars-view'), pieView=d.getElementById('contribution-pie-view');
            checks.defaultBars=!barView.hidden&&pieView.hidden;
            const bars=d.getElementById('family-contribution-chart').innerHTML;
            button.focus();button.click();
            checks.choice=barView.hidden&&!pieView.hidden&&button.getAttribute('aria-pressed')==='true';
            checks.boundedFigure=pieView.querySelector('svg').getBoundingClientRect().width<=600;
            checks.slices=pieView.querySelectorAll('[data-pie-key]').length===rows.filter(row=>fields[row.x.exact].value>0).length;
            checks.values=[...pieView.querySelectorAll('[data-pie-key]')].every(mark=>rows.some(row=>row.key===mark.dataset.pieKey&&row.x.exact===mark.dataset.pieValue));
            checks.legend=pieView.querySelector('.chart-legend').textContent.includes('all measured peptide-feature signal')&&pieView.textContent.includes('fixed base when Normalize to changes');
            checks.exactTable=pieView.querySelectorAll('details tbody tr').length===rows.length;
            const mark=pieView.querySelector('[data-pie-key]');mark.focus();
            mark.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Enter',bubbles:true}));
            checks.keyboard=d.getElementById('dialog-body').textContent.includes(figure.views.fixed.basis);
            d.getElementById('dialog-close').click();
            const snapshot=()=>[...pieView.querySelectorAll('[data-pie-key]')].map(mark=>[mark.dataset.pieKey,mark.dataset.pieValue,mark.getAttribute('d')]).toString();
            const original=snapshot();
            for(const choice of d.querySelectorAll('input[name="normalization-basis"]')) {
              if(choice.disabled) continue;
              choice.click();
              checks[choice.value+'Fixed']=!pieView.hidden&&snapshot()===original&&pieView.textContent.includes('all measured peptide-feature signal');
            }
            d.getElementById('contribution-bars').click();
            checks.barReturn=!barView.hidden&&pieView.hidden;
            if(name!=='named') {
              d.getElementById('basis-identifiedAndDeNovo').click();
              checks.barsPreserved=d.getElementById('family-contribution-chart').innerHTML===bars;
            }
            button.click();
            checks.fits=d.documentElement.scrollWidth<=w.innerWidth;
            return checks;
            """);
        CaptureHtmlReportScreens(Path.Combine(output, "screens"), payload, "families", "document.getElementById('contribution-pie').click();", "family-contribution-section");
        File.WriteAllBytes(Path.Combine(output, "pie.html"), HtmlReportWriter.BuildBytes(payload));
        Console.WriteLine("PI1 synthetic HTML size: " + new FileInfo(Path.Combine(output, "pie.html")).Length + " bytes.");
    }
}
