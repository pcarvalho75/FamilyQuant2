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
    private static IEnumerable<TestCommand> HtmlRingRemainderDisplayCommands() =>
    [new("--test-html-ring-remainder-display", 10732, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-ring-remainder-display", (args, _) =>
        {
            HtmlRingRemainderDisplayTests(args.Length == 2 ? args[1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
        })];

    private static void HtmlRingRemainderDisplayTests(string? output = null)
    {
        string folder = output ?? Path.Combine(Path.GetTempPath(), "html-ring-remainder-display"); Directory.CreateDirectory(folder);
        var fixture = HtmlSharePartsFixture();
        string before = JsonSerializer.Serialize(fixture.Context.StoredStudies);
        var payload = HtmlReportPayloadBuilder.Build(fixture.Document, fixture.Sheets, context: fixture.Context);
        Check("WD1 display projection leaves its stored inputs unchanged", before == JsonSerializer.Serialize(fixture.Context.StoredStudies));
        foreach (var run in fixture.Context.StoredStudies!.DerivedFractions) run.DetectedPeptideLikeArea *= 3;
        var positive = HtmlReportPayloadBuilder.Build(fixture.Document, fixture.Sheets, context: fixture.Context);
        foreach (var pair in new[] { (Payload: payload, Key: "tic"), (Payload: positive, Key: "detectorEnvelopes") })
        {
            var ring = pair.Payload.Composition.RingBases[pair.Key];
            double Value(string? id) => pair.Payload.Fields[id!].Value!.Value;
            Check("WD1 visible parts retain the original remainder " + pair.Key, ring.OutsidePeptide != null && ring.OutsideRest != null
                && Math.Abs(Value(ring.OutsidePeptide) + Value(ring.OutsideRest) - Value(ring.Outside)) < 1e-10);
        }
        HtmlReportPayload WithoutParts(bool partial)
        {
            var bases = payload.Composition.RingBases.ToDictionary(pair => pair.Key, pair => partial
                ? pair.Value with { OutsideRest = null } : pair.Value with { OutsidePeptide = null, OutsideRest = null });
            return payload with { Composition = payload.Composition with { RingBases = bases } };
        }
        HtmlReportBrowserChecks(Path.Combine(folder, "browser"), new[] { ("desktop", payload, 1366), ("phone", payload, 390),
            ("positive-desktop", positive, 1366), ("positive-phone", positive, 390), ("partial", WithoutParts(true), 390),
            ("legacy", WithoutParts(false), 390) }, """
            const checks={},p=JSON.parse(d.getElementById('fq-data').textContent),fields=p.fields;
            const labels=['other families, shared and unattributed','not explained by PSM and de novo'];
            const exists=id=>!!id&&Number.isFinite(fields[id]?.value);
            const displayed=id=>fields[id]?.short||id;
            for(const key of ['identifiedOnly','identifiedAndDeNovo','detectorEnvelopes','tic']){
              const choice=d.getElementById('basis-'+key);if(choice.disabled)continue;choice.click();
              const ring=p.composition.ringBases[key],parts=[ring.outsidePeptide,ring.outsideRest],split=parts.every(exists);
              const keys=[...d.querySelectorAll('.ring-outside-key')],lines=[...d.querySelectorAll('.ring-remainder-line')];
              checks[key+'Rows']=keys.length===(split?2:1)&&lines.length===(split?2:0);
              if(split){
                checks[key+'Names']=labels.every((label,index)=>keys[index].textContent.startsWith(label)&&lines[index].textContent.startsWith(label));
                checks[key+'Values']=parts.every((id,index)=>!!keys[index].querySelector('[data-fq-field="'+displayed(id)+'"]')
                  &&!!lines[index].querySelector('[data-fq-field="'+displayed(id)+'"]'));
                checks[key+'Sum']=Math.abs(fields[parts[0]].value+fields[parts[1]].value-fields[ring.outside].value)<1e-10;
                checks[key+'SeparateLines']=keys[1].getBoundingClientRect().top>=keys[0].getBoundingClientRect().bottom-1;
                keys[0].focus();keys[0].click();
                checks[key+'Exact']=!!d.querySelector('#dialog-body [data-fq-field="'+parts[0]+'"]')&&d.getElementById('dialog-title').textContent===labels[0];
                d.getElementById('dialog-close').click();
                checks[key+'Caption']=!d.getElementById('composition-plain-legend').textContent.includes('share outside the shown families');
              }else{
                checks[key+'Fallback']=keys[0].textContent.startsWith('Outside the shown families')
                  &&d.getElementById('composition-plain-legend').textContent.includes('share outside the shown families');
              }
              const arc=d.querySelector('#composition-ring [data-outside="true"]');
              checks[key+'OriginalArc']=!fields[ring.outside].value||arc?.dataset.share===ring.outside;
              checks[key+'Fits']=d.documentElement.scrollWidth<=w.innerWidth;
            }
            checks.sourceUnchanged=JSON.stringify(JSON.parse(d.getElementById('fq-data').textContent))===JSON.stringify(p);
            return checks;
            """);
        if (output != null)
        {
            foreach (string key in new[] { "tic", "identifiedAndDeNovo" })
                CaptureHtmlReportScreens(Path.Combine(folder, "screens-" + key), payload, "families",
                    "document.getElementById('basis-" + key + "').click();", "composition-title");
        }
    }
}
