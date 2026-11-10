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
    private static IEnumerable<TestCommand> HtmlRingBasesCommands() =>
    [new("--test-html-ring-bases", 10526, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-ring-bases", (args, _) =>
        {
            HtmlRingBasesTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-ring-bases"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlRingBasesTests(string output)
    {
        Directory.CreateDirectory(output);
        HtmlReportPayload Build(Action<QuantManifest>? change = null)
        {
            var fixture = HtmlSharePartsFixture();
            change?.Invoke(fixture.Context.StoredStudies!);
            string before = JsonSerializer.Serialize(fixture.Context.StoredStudies);
            var result = HtmlReportPayloadBuilder.Build(fixture.Document, fixture.Sheets, context: fixture.Context);
            Check("U8 ring projection preserves the stored study", before == JsonSerializer.Serialize(fixture.Context.StoredStudies));
            return result;
        }
        var payload = Build();
        var precise = Build(manifest =>
        {
            foreach (var run in manifest.FamilyRunShares!.Runs)
            {
                run.IdentifiedSignal *= 53.75 / 69.1734;
                run.TotalIonCurrent *= 7 / 36.0917;
            }
            foreach (var family in manifest.FamilyRunShares.Families)
                foreach (var run in family.Runs)
                    run.ShareOfTic = run.Signal / manifest.FamilyRunShares.Runs.Single(total => total.Run == run.Run).TotalIonCurrent;
        });
        string Display(HtmlReportPayload data, string id) => data.Fields[data.Fields[id].Short!].Text;
        Check("U9-polish ring shares use the existing G3 display and keep exact values", precise.Composition.RingBases["identifiedOnly"] is { Total: { } total, Outside: { } outside }
            && precise.Fields[total].Text == "69.1734" && Display(precise, total) == "69.2"
            && precise.Fields[outside].Text == "30.8266" && Display(precise, outside) == "30.8"
            && Display(precise, precise.Composition.RingBases["tic"].Total!) == "36.1");
        double Value(HtmlReportPayload data, string? id) => id == null ? double.NaN : data.Fields[id].Value!.Value;
        foreach (var item in new[] { (Key: "identifiedOnly", Total: 53.75), (Key: "identifiedAndDeNovo", Total: 47.5),
            (Key: "detectorEnvelopes", Total: 76.25), (Key: "tic", Total: 7d) })
        {
            var ring = payload.Composition.RingBases[item.Key];
            Check("U8 native total and outside match stored means " + item.Key, ring.CanDraw && ring.RunCount == 2
                && Math.Abs(Value(payload, ring.Total) - item.Total) < 1e-10 && Math.Abs(Value(payload, ring.Outside) - (100 - item.Total)) < 1e-10);
        }
        var excessive = Build(manifest => { foreach (var run in manifest.DerivedFractions) run.DetectedPeptideLikeArea = 1; });
        Check("U8 detector totals over a full circle remain available as values with an unavailable-ring explanation",
            !excessive.Composition.RingBases["detectorEnvelopes"].CanDraw
            && Value(excessive, excessive.Composition.RingBases["detectorEnvelopes"].Total) > 100
            && excessive.Composition.RingBases["detectorEnvelopes"].Outside == null);
        var missing = Build(manifest => manifest.FamilyRunShares!.Families[0].Runs[0].IdentifiedSignal = null);
        Check("U8 incomplete shown-family shares never become an outside percentage", !missing.Composition.RingBases["identifiedOnly"].CanDraw
            && missing.Composition.RingBases["identifiedOnly"].Total == null && missing.Composition.RingBases["identifiedOnly"].Outside == null);
        var zero = Build(manifest => { foreach (var family in manifest.FamilyRunShares!.Families) foreach (var run in family.Runs) run.IdentifiedSignal = 0; });
        Check("U8 zero shown PSM share leaves a full outside segment", zero.Composition.RingBases["identifiedOnly"].CanDraw
            && Value(zero, zero.Composition.RingBases["identifiedOnly"].Total) == 0 && Value(zero, zero.Composition.RingBases["identifiedOnly"].Outside) == 100);
        var full = Build(manifest =>
        {
            foreach (var total in manifest.FamilyRunShares!.Runs) total.TotalIonCurrent = total.AssignedSignal;
            foreach (var family in manifest.FamilyRunShares.Families)
                foreach (var run in family.Runs) run.ShareOfTic = run.Signal / manifest.FamilyRunShares.Runs.Single(total => total.Run == run.Run).AssignedSignal;
        });
        Check("U8 a full shown share preserves a recorded zero outside", full.Composition.RingBases["tic"].CanDraw
            && Math.Abs(Value(full, full.Composition.RingBases["tic"].Outside)) < 1e-10);
        var fixture = HtmlSharePartsFixture();
        string word = Path.Combine(output, "ring.docx");
        WordReportWriter.Write(word, fixture.Document);
        ReportSupplementWorkbook.Write(ReportSupplementWorkbook.PathForReport(word), fixture.Document, fixture.Sheets);
        Check("U8 totals and outside trace to the stored canonical family means", ReportNumberTrace.RunHtmlBindings(payload, word,
            ReportSupplementWorkbook.PathForReport(word), new(), fixture.Context.StoredStudies!, Path.Combine(output, "trace.csv")) == 0);
        var fields = payload.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        string totalId = payload.Composition.RingBases["tic"].Total!;
        fields[totalId] = fields[totalId] with { Value = 99 };
        Check("U8 trace refuses a substituted total", ReportNumberTrace.RunHtmlBindings(payload with { Fields = fields }, word,
            ReportSupplementWorkbook.PathForReport(word), new(), fixture.Context.StoredStudies!, Path.Combine(output, "negative-trace.csv")) != 0);
        fields = payload.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        var source = new HtmlReportReference { Kind = "synthetic-ui-fixture" };
        fields["part-a"] = new("Part A", null, "", source); fields["part-b"] = new("Part B", null, "", source);
        fields["share-a"] = new("40", 40, "%", source); fields["share-b"] = new("60", 60, "%", source);
        var withParts = payload with { Fields = fields, Composition = payload.Composition with { Families = payload.Composition.Families.Select(family => family.IsOthers ? family
            : family with { Parts = new[] { new HtmlFamilyPart("subclass", "part-a", "share-a"), new HtmlFamilyPart("subclass", "part-b", "share-b") } }).ToArray() } };
        HtmlReportBrowserChecks(Path.Combine(output, "page"), new[] { ("phone", withParts, 390), ("desktop", withParts, 1366),
            ("excessive", excessive, 390), ("missing", missing, 390), ("zero", zero, 390), ("full", full, 390),
            ("precision-phone", precise, 390), ("precision-desktop", precise, 1366) }, """
            const checks = {}, p = JSON.parse(d.getElementById('fq-data').textContent), fields = p.fields;
            const bases = ['identifiedOnly','identifiedAndDeNovo','detectorEnvelopes','tic'];
            const choices = [...d.querySelectorAll('input[name="normalization-basis"]')], paths = new Set();
            for (const [index,key] of bases.entries()) {
              if (choices[index].disabled) continue;
              choices[index].click();
              const ring = p.composition.ringBases[key];
              checks[key+'Centre'] = ring.total ? !!d.querySelector('#ring-value [data-metric="'+ring.total+'"]') : d.getElementById('ring-value').textContent==='Unavailable';
              if (ring.total) {
                const metric=d.querySelector('#ring-value [data-metric="'+ring.total+'"]'), short=fields[ring.total].short;
                checks[key+'CompactCentre']=metric.textContent===fields[short].text+' %'&&metric.title.includes(fields[ring.total].text)&&w.getComputedStyle(metric).whiteSpace==='nowrap';
              }
              checks[key+'Base'] = d.getElementById('ring-name').textContent.includes(key==='tic'?'TIC':key==='detectorEnvelopes'?'detector':key==='identifiedOnly'?'identified':'peptide-feature');
              if (!ring.canDraw) {
                checks[key+'Unavailable'] = !d.getElementById('ring-unavailable').hidden && !d.querySelector('#composition-ring .ring-slice');
                continue;
              }
              checks[key+'Slices'] = [...d.querySelectorAll('#composition-ring .ring-slice[data-family]')].every(path => {
                const family=p.composition.families.find(f=>f.id===path.dataset.family);
                return path.dataset.share===family[key].mean;
              });
              const outside=d.querySelector('.ring-outside-key');
              checks[key+'Outside'] = !!outside&&outside.textContent.includes('Outside the shown families')&&outside.getClientRects().length>0;
              checks[key+'CompactOutside']=outside.textContent.includes(fields[fields[ring.outside].short].text+' %')&&outside.title.includes(fields[ring.outside].text);
              outside.focus();outside.click();
              checks[key+'OutsideValue'] = !!d.querySelector('#dialog-body [data-fq-field="'+ring.outside+'"]');
              d.getElementById('dialog-close').click();
              paths.add([...d.querySelectorAll('#composition-ring .ring-slice')].map(path=>path.getAttribute('d')).join('|'));
              const selected=p.composition.families.find(f=>!f.isOthers);
              checks[key+'Card'] = !!d.querySelector('#selected-summary [data-metric="'+selected[key].mean+'"]');
            }
            checks.redraws = paths.size>1;
            if (name==='phone'||name==='desktop') {
              choices[1].click();d.getElementById('reset-composition').click();
              checks.parts = d.getElementById('composition-basis').textContent.includes('Pooled within-family');
              choices[3].click();
              checks.partsReturn = !!d.querySelector('#composition-ring [data-outside="true"]')&&d.getElementById('composition-basis').textContent.includes('TIC');
            }
            checks.noPicture = !d.querySelector('#composition-ring image');
            checks.noOverflow = d.documentElement.scrollWidth<=w.innerWidth;
            return checks;
            """);
        CaptureHtmlReportScreens(Path.Combine(output, "screens"), precise, "families", "document.getElementById('basis-identifiedOnly').click();");
        File.WriteAllBytes(Path.Combine(output, "ring.html"), HtmlReportWriter.BuildBytes(withParts));
        Console.WriteLine("U8 synthetic HTML size: " + new FileInfo(Path.Combine(output, "ring.html")).Length.ToString(CultureInfo.InvariantCulture) + " bytes.");
    }
}
