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
    private static IEnumerable<TestCommand> HtmlReportWordsCommands() =>
    [new("--test-html-report-words", 10534, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-report-words", (args, _) =>
        {
            HtmlReportWordsTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-report-words"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlReportWordsTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var (manifest, project, families) = HtmlFamilyNativeFixture();
        project.ReportThreshold = .001;
        foreach (var protein in project.MyExtendedFastaItems)
            protein.UserAnnotation = "Stored group " + protein.UserID + " OS=Header detail OX=123 GN=Label PE=1 SV=2";
        foreach (var family in families.Families)
            family.Description = project.MyExtendedFastaItems.Single(protein => protein.UserID == family.FamilyUserId).UserAnnotation;
        string before = JsonSerializer.Serialize(manifest);
        var export = ReportProjectExporter.Build(SyntheticAnalyticsRows(), manifest, project, Path.Combine(output, "figures"), familyResults: families);
        HtmlReportPayload Project(ReportProjectExport prepared) => HtmlReportFigures.Build(
            HtmlReportPayloadBuilder.Build(prepared.Document, prepared.SupplementSheets, context: prepared.HtmlContext), prepared.Document);
        var payload = Project(export);
        Check("U9 words preserve stored scientific inputs", before == JsonSerializer.Serialize(manifest));
        foreach (var measure in payload.DotMeasures.Where(measure => measure.Id is "mean" or "sd"))
            foreach (var (key, view) in measure.Bases)
            {
                Check("U9 dot axis names the selected base " + measure.Id + "/" + key,
                    payload.Fields[view.Label].Source is { Kind: "basis-axis-label" } && view.Ticks.Count >= 2);
                Check("U9 dot values reference native family statistics " + measure.Id + "/" + key,
                    payload.Composition.Families.Where(family => !family.IsOthers).All(family =>
                    {
                        var statistics = key switch { "identifiedOnly" => family.IdentifiedOnly, "identifiedAndDeNovo" => family.IdentifiedAndDeNovo,
                            "detectorEnvelopes" => family.DetectorEnvelopes, _ => family.Tic };
                        string? id = measure.Id == "mean" ? statistics.Mean : statistics.Sd;
                        return id == null ? !view.Values.ContainsKey(family.Id) : view.Values[family.Id] == id;
                    }));
            }
        Check("U9 native synthetic report includes every integrated chart tab", payload.SignalCharts.Count > 0
            && payload.FamilyFigures.Figures.Count > 0 && payload.MassQualityCharts != null && payload.CoverageCatalog?.Proteins.Count > 0);
        var empty = Project(ReportProjectExporter.Build(SyntheticAnalyticsRows(), new QuantManifest(), new FamilyClusterFile(), Path.Combine(output, "no-families")));
        Check("U9 native quantified project without curation has no family results", empty.Composition.Families.Count == 0
            && empty.Composition.ContributionRows.Count == 0 && empty.FamilyFigures.Figures.Count == 0);
        HtmlReportBrowserChecks(Path.Combine(output, "browser"), new[] { ("phone", payload, 390), ("desktop", payload, 1366), ("no-families", empty, 390) }, """
            const checks={}, p=JSON.parse(d.getElementById('fq-data').textContent), fields=p.fields;
            const help=d.getElementById('numbers-help'); help.click();
            checks.definitions=['TIC','Peptide feature','PSM','De novo','Isotope envelopes (measured)','Credited family signal'].every(word=>d.getElementById('dialog-body').textContent.includes(word));
            checks.featureUnit=d.getElementById('dialog-body').textContent.includes('One peptide at one charge in one elution peak, counted once.');
            d.getElementById('dialog-close').click();
            if(name==='no-families') {
              checks.guidance=d.getElementById('basis-compatibility').textContent==='No family results in this project yet. Curate families and run Quantify, then export again.';
              checks.disabled=[...d.querySelectorAll('input[name="normalization-basis"]')].every(choice=>choice.disabled&&choice.title.includes('Curate families'));
              checks.noOldVersion=!d.getElementById('basis-compatibility').textContent.includes('version');
              return checks;
            }
            const raw=p.composition.families.find(family=>!family.isOthers), full=fields[raw.name].text;
            checks.fixtureHeader=full.includes(' OS=');
            const short=full.split(/\s(?:OS|OX|GN|PE|SV)=/)[0];
            const rank=d.querySelector('#family-ranking [data-fq-field="'+raw.name+'"]');
            checks.shortLabel=rank.textContent===short&&rank.title===full;
            rank.closest('button').click();
            d.getElementById('family-info').click();
            checks.fullDetail=d.getElementById('dialog-body').textContent.includes(full);
            d.getElementById('dialog-close').click();
            const bases=['identifiedOnly','identifiedAndDeNovo','detectorEnvelopes','tic'];
            const paths=new Set();
            for(const [index,key] of bases.entries()) {
              d.querySelector('input[name="normalization-basis"][value="'+key+'"]').click();
              const baseNames={identifiedOnly:'PSM only',identifiedAndDeNovo:'PSM and de novo',detectorEnvelopes:'Isotope envelopes (measured)',tic:'TIC'};
              const captions={identifiedOnly:'Percent of PSM only',identifiedAndDeNovo:'Percent of PSM and de novo',detectorEnvelopes:'Percent of Isotope envelopes (measured)',tic:'Percent of TIC'};
              for(const [chart,caption] of [['family-ranking','ranking-note'],['family-contribution-chart','contribution-base-caption']]) {
                const heading=d.getElementById(caption);
                checks[key+chart+'BaseHeading']=heading.textContent===captions[key]&&heading.getBoundingClientRect().top<d.getElementById(chart).getBoundingClientRect().top;
                const percentages=[...d.querySelectorAll('#'+chart+' [data-percent-base]')];
                checks[key+chart+'CompactRows']=percentages.length>0&&percentages.every(element=>['none','normal','""'].includes(w.getComputedStyle(element,'::after').content)&&!element.textContent.includes(' of '));
                checks[key+chart+'HoverBase']=percentages.filter(element=>element.closest('[data-metric]')).every(element=>element.closest('[data-metric]').title.includes(baseNames[key]));
              }
              const view=p.dotMeasures.find(measure=>measure.id==='mean').bases[key];
              checks[key+'Axis']=d.getElementById('dot-chart').textContent.includes(fields[view.label].text);
              const mark=d.querySelector('.evidence-dot[data-family="'+raw.id+'"]');
              mark.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Enter',bubbles:true}));
              checks[key+'Value']=!!d.querySelector('#dot-tooltip [data-fq-field="'+view.values[raw.id]+'"]');
              paths.add([...d.querySelectorAll('.evidence-dot')].map(mark=>mark.getAttribute('transform')).join('|'));
              const labels=[...d.querySelectorAll('.dot-name-label')];
              checks[key+'FewLabels']=labels.length<=(name==='phone'?2:4)&&labels.every(label=>!label.childNodes[0].textContent.includes(' OS='));
              checks[key+'NoOverlap']=labels.every((label,i)=>labels.slice(i+1).every(other=>{
                const a=label.getBoundingClientRect(),b=other.getBoundingClientRect();
                return a.right<=b.left||b.right<=a.left||a.bottom<=b.top||b.bottom<=a.top;
              }));
            }
            checks.dotRedraws=paths.size>1;
            const threshold=p.highlights.find(highlight=>fields[highlight.name].text==='report threshold (%)');
            checks.thresholdFixture=!!threshold;
            if(threshold) {
              const value=d.querySelector('#headline [data-metric="'+threshold.value+'"]');
              checks.thresholdLabel=value.parentElement.querySelector('.headline-label').textContent==='Report threshold, mean share of credited family signal';
              checks.thresholdCompact=value.textContent==='0.1 %'&&['none','normal','""'].includes(w.getComputedStyle(value.firstElementChild,'::after').content);
            }
            for(const tab of d.querySelectorAll('[role="tab"]')) {
              tab.click();
              const percentages=[...d.querySelectorAll('[data-fq-field]')].filter(element=>fields[element.dataset.fqField]?.unit==='%'&&element.getClientRects().length&&element.tagName.toLowerCase()!=='title');
              checks[tab.id+'PercentBases']=percentages.every(element=>!!element.dataset.percentBase);
              checks[tab.id+'NativeLabels']=[...d.querySelectorAll('[data-family-label]')].filter(element=>element.getClientRects().length).every(element=>!element.textContent.includes(' OS=')&&!!element.title);
              checks[tab.id+'Fits']=d.documentElement.scrollWidth<=w.innerWidth;
            }
            d.getElementById('tab-coverage').click();
            checks.coverageRuler=d.querySelector('.coverage-ruler')?.textContent.includes('Residue 200');
            const choose=d.getElementById('coverage-family'),protein=d.getElementById('protein-select');
            checks.chooserStyles=w.getComputedStyle(choose).backgroundColor===w.getComputedStyle(protein).backgroundColor&&w.getComputedStyle(choose).borderRadius===w.getComputedStyle(protein).borderRadius;
            checks.placementList=[...d.querySelectorAll('#coverage-content button')].some(button=>button.textContent==='List placements and annotations');
            return checks;
            """);
        CaptureHtmlReportScreens(Path.Combine(output, "screens"), payload, "families");
        CaptureHtmlReportScreens(Path.Combine(output, "contribution-screens"), payload, "families", anchor: "family-contribution-section");
        CaptureHtmlReportScreens(Path.Combine(output, "threshold-screens"), payload, "families", anchor: "headline");
        CaptureHtmlReportScreens(Path.Combine(output, "coverage-screens"), payload, "coverage");
        byte[] bytes = HtmlReportWriter.BuildBytes(payload); File.WriteAllBytes(Path.Combine(output, "words.html"), bytes);
        Console.WriteLine("U9 synthetic HTML size: " + bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes.");
    }
}
