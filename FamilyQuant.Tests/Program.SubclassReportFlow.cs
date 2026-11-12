using FamilyQuant.DeNovo;
using FamilyQuant.Panels.Import;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SubclassReportFlowCommands() =>
    [new("--test-subclass-report-flow", 449, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--test-subclass-report-flow", (_, _) =>
    {
        SubclassReportFlowTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void SubclassReportFlowTests()
    {
        EnsureApplication();
        string root = Path.Combine(Path.GetTempPath(), "subclass-report-flow", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string[] classes = { "First class", "Second class", "Third class", "Fourth class", "Fifth class" };
        string[] sequences = { "ACDEFGHIK", "LMNPQRSTK", "WYACDEFGK", "HIKLMNPQK", "RSTVWYAAK" };
        var file = new FamilyClusterFile
        {
            ReportThreshold = .9,
            MyExtendedFastaItems = Enumerable.Range(0, 5).Select(index => new FastaItemExtended
            {
                SequenceIdentifier = "sp|P6400" + index + "|FIXTURE", Description = classes[index] + " synthetic protein", Sequence = sequences[index],
                UserID = index + 1, USearchID = index + 1,
                Evidence = new() { Accession = "P6400" + index, Reviewed = true, Family = classes[index] + " source family",
                    FamilyAnnotation = classes[index] + " source family", Keywords = new() { "selected" },
                    Pfam = new() { new() { Accession = index < 2 ? "PF11111" : "PF22222" } } },
            }).ToList(),
            EvidenceRecipe = new() { Options = new() { ScopeTerms = new[] { "selected" } } },
        };
        string project = Path.Combine(root, "input.fqproj"); ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        string workbook = Path.Combine(root, "families.xlsx"); PanelImportTemplate.Write(workbook);
        FillTemplate(workbook, new[] { (Key: "A", Name: "First parent family", Domain: "PF11111"), (Key: "B", Name: "Second parent family", Domain: "PF22222") }
            .Select(value => PanelImportTemplate.Columns.Select(column => column.Role switch
            { PanelImportFields.FamilyKey => value.Key, PanelImportFields.DisplayName => value.Name, PanelImportFields.Pfam => value.Domain, _ => "" }).ToArray()).ToArray());
        FillSubclassTemplate(workbook, classes.Select((name, index) => new[] { index < 2 ? "A" : "B", name, name + " source family", "", "" }).ToArray());
        string walk = Path.Combine(root, "walk");
        int walked = FamilyListWalkCommand(new[] { "--family-list-walk", project, workbook, walk });
        Check("SF5 synthetic IM9 workbook imports and applies two families with two and three subclasses", walked == 0
            && File.Exists(Path.Combine(walk, "walked.fqproj")));
        if (walked != 0 || !File.Exists(Path.Combine(walk, "walked.fqproj"))) return;
        string raw = Path.Combine(root, "raw"), source = Path.Combine(root, "source"), cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(raw); Directory.CreateDirectory(source); Directory.CreateDirectory(cache);
        string acquisition = Path.Combine(raw, "run01.raw"); File.WriteAllText(acquisition, "Synthetic validated-cache placeholder, never an acquisition.");
        string fingerprint = FileFingerprint.Sha256(acquisition); var storage = new FullXicStoragePaths(raw, cache);
        var extraction = DeNovoXicCache.Load(storage.Extraction("denovo", "run01").WritePath, "run01", DeNovoXicExtractor.ProductionPpm,
            DeNovoXicExtractor.ProductionMs1Tolerance, 6, 5, fingerprint);
        var rows = new List<string> { "Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,local confidence (%)" };
        for (int index = 0; index < sequences.Length; index++)
        {
            rows.Add((index + 1) + "," + sequences[index] + ",99,9," + (500 + index) + ",2,1,100,99 99 99 99 99 99 99 99 99");
            extraction.Put(new() { RunName = "run01", ScanNoMS2 = index + 1, PrecursorMz = 500 + index, Charge = 2,
                IsotopeAreas = new double[] { 10 * (index + 1), 5, 2, 0, 0, 0 } });
        }
        extraction.Save(); File.WriteAllLines(Path.Combine(source, "run01.raw.denovo.csv"), rows);
        typeof(Ms1EnvelopeDetector).GetMethod("SaveCache", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
            new object[] { storage.Detector("run01").WritePath, new Ms1EnvelopeDetectionResult { RunName = "run01", Ms1ScanCount = 10,
                DetectedAreaByNeutralMass = new double[NeutralMassRanges.Count], NeutralMassBinWidth = NeutralMassRanges.Width }, fingerprint });
        string package = Path.Combine(root, "fixture.sepr2"); CreateShellSeproFixture(Array.Empty<(string, int)>()).Save(package);
        string output = Path.Combine(root, "quantified"), config = Path.Combine(root, "compute.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new QuantifyScreenConfiguration(Path.Combine(walk, "walked.fqproj"), raw, package, source, cache, output,
            SaveProject: true, IsotopeCeiling: 6, MinimumPoints: 5, PeaksMinimumAlc: 90, DeNovoMode: "peaks-all")));
        int computed = QuantifyScreenRun(config, options => typeof(FullXicOptions).GetProperty("ReadTicArea", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(options, (Func<string, double>)(_ => 1000)));
        Check("SF5 full synthetic Quantify completes", computed == 0);
        if (computed != 0) return;
        var result = ProjectStore.ReadFqproj(Path.Combine(output, "computed.fqproj"));
        var stored = result.manifest.FamilySubclassParts?.Families.SelectMany(family => family.Parts).ToArray() ?? Array.Empty<FamilySignalPart>();
        Check("SF5 computed subclass labels are stored with positive signal", result.fcf.FamilyPanel!.Families.Select(family => family.Subclasses.Count).Order().SequenceEqual(new[] { 2, 3 })
            && classes.All(name => stored.Any(part => part.Label == name && part.Signal > 0)));
        string report = Path.Combine(root, "report.docx");
        var export = ReportProjectExporter.Build(result.quant, result.manifest, result.fcf, Path.Combine(root, "figures"));
        ReportProjectExporter.WriteHtml(export, report);
        var payload = ReadHtmlPayload(HtmlReportWriter.PathForReport(report));
        string[] htmlLabels = payload.Composition.Families.SelectMany(family => family.Parts)
            .Concat(payload.CoverageCatalog?.Families.SelectMany(family => family.Parts) ?? Array.Empty<HtmlFamilyPart>()).Where(part => part.Kind == "Subclass")
            .Select(part => payload.Fields[part.Name].Text).ToArray();
        bool complete = classes.All(name => htmlLabels.Contains(name, StringComparer.Ordinal));
        Check("SF5 exported HTML Families details hold every computed subclass label", complete,
            "Stored: " + string.Join(", ", stored.Select(part => part.Label)) + "; HTML: " + string.Join(", ", htmlLabels));
        var excluded = HtmlReportPayloadBuilder.Build(export.Document, export.SupplementSheets,
            new ReportSelection { ExcludedIds = new[] { "subclass-parts" } }, export.HtmlContext);
        Check("SF5 subclass-detail exclusions also apply to below-threshold family entries", excluded.CoverageCatalog!.Families
            .All(family => family.Parts.All(part => part.Kind != "Subclass")));
        var native = StoredCompositionFigures.Build(result.manifest, project: result.fcf);
        string[] nativeLabels = native.Select(figure => figure.Chart).OfType<FamilySubclassChart>()
            .SelectMany(chart => chart.Rows).SelectMany(row => row.Subclasses).Select(part => part.Label).ToArray();
        Check("SF5 native Results keeps named parts and Word retains its canonical subclass figure", classes.All(name => nativeLabels.Contains(name, StringComparer.Ordinal))
            && export.Document.Sections.SelectMany(section => section.Blocks).OfType<ReportFigure>().Any(figure => figure.Id.StartsWith("figure:family_subclasses", StringComparison.Ordinal)));
        if (complete)
            HtmlReportBrowserChecks(Path.Combine(root, "browser"), new[] { ("subclasses", payload, 1366) }, """
                const checks={},labels=[];d.getElementById('tab-coverage').click();
                const select=d.getElementById('coverage-family');let segments=0;
                for(const option of select.options){select.value=option.value;select.dispatchEvent(new w.Event('change'));labels.push(d.getElementById('subclasses').textContent);segments+=d.querySelectorAll('#subclasses .subclass-segment').length;}
                checks.everySubclassDrawn=['First class','Second class','Third class','Fourth class','Fifth class'].every(label=>labels.some(text=>text.includes(label)));
                checks.stackedBarsDrawn=segments>=5;return checks;
                """);
        Console.WriteLine("SF5 synthetic report: " + HtmlReportWriter.PathForReport(report));
    }
}
