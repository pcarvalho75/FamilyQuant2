using FamilyQuant.Panels;
using FamilyQuant.Evidence;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportStudyFigureCommands() => new[]
    {
        new TestCommand("--test-report-study-figures", 10000, TestCommandPhase.Normalized,
            args => args.Contains("--test-report-study-figures", StringComparer.Ordinal), (_, work) =>
            {
                ReportStudyFigureTests(Path.Combine(work, "report-study-figures"));
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            })
    };

    private static void ReportStudyFigureTests(string folder)
    {
        Section("Report study figures - native PNG/caption/Word parity from saved results");
        EnsureApplication(); Directory.CreateDirectory(folder);
        var (manifest, project, map) = ReportStudyFigureFixture();
        var plan = ReportStudyFigures.Plan(manifest, project, panelMap: map);
        Check("default production call refuses stale panel evidence rather than inferring subclass-label membership",
            Throws<InvalidOperationException>(() => ReportStudyFigures.Plan(manifest, project)));
        Check("stored report plan includes composition, subclasses, ring, diversity, source signal and coverage",
            plan.Figures.Count == 6 && plan.Figures.Any(f => f.Plot.Chart is FamilyCompositionChart)
            && plan.Figures.Any(f => f.Plot.Chart is FamilySubclassChart)
            && plan.Figures.Any(f => f.Plot.Chart is FamilyRingChart)
            && plan.Figures.Any(f => f.Plot.Chart is FamilyAbundanceDiversityChart)
            && plan.Figures.Any(f => f.Plot.Chart is FamilySignalOriginChart)
            && plan.Figures.Any(f => f.Plot.Chart is ProteinCoverageMapChart));
        var maps = plan.Figures.Where(f => f.Plot.Chart is ProteinCoverageMapChart).ToArray();
        Check("coverage selection is top per panel family rather than one per User ID",
            maps.Length == 1 && maps[0].SectionId == "family:alpha"
            && ((ProteinCoverageMapChart)maps[0].Plot.Chart).Data.ProteinId == "P1");
        Check("panel families without localized proteins remain explicitly unavailable",
            plan.Unavailable.Any(f => f.SectionId == "family:beta" && f.Reason.Length > 0));
        var ring = (FamilyRingChart)plan.Figures.Single(f => f.Plot.Chart is FamilyRingChart).Plot.Chart;
        Check("report ring preserves central Others membership and pooled angles",
            ring.Families.Single(f => f.IsOthers).Members.Single().Key == "3"
            && Math.Abs(ring.Families.Single(f => f.IsOthers).Percent - 100.0 * 600 / 1020) < 1e-10);
        Check("every caption's numeric binding is the exact chart binding",
            plan.Figures.All(f => f.Caption.Numbers.SequenceEqual(CompositionChartCaptions.Describe(f.Plot).Numbers)));
        var sourcePlot = plan.Figures.Single(f => f.Plot.Chart is FamilySignalOriginChart);
        var sourceRows = ((FamilySignalOriginChart)sourcePlot.Plot.Chart).Rows;
        Check("source caption uses stored component amounts and leaves world-only signal separate",
            sourcePlot.Caption.Numbers.Any(n => n.Label == "Family-world-only de novo signal" && n.Value == sourceRows.Sum(r => r.WorldDeNovoEnvelope))
            && sourcePlot.Caption.How.Contains("starting database"));

        var oldCulture = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        byte[]? firstDocument = null;
        try
        {
            foreach (string locale in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                string pngFolder = Path.Combine(folder, locale);
                var packet = ReportStudyFigures.Build(manifest, project, pngFolder, panelMap: map);
                Check(locale + " report builder restores caller cultures", CultureInfo.CurrentCulture.Name == locale && CultureInfo.CurrentUICulture.Name == locale);
                Check(locale + " every placed figure equals the independent native PNG exporter",
                    plan.Figures.All(f =>
                    {
                        byte[] native = f.Plot.Chart is FamilySignalOriginChart origin
                            ? FamilySignalOriginChart.RenderReportPng(origin.Rows) : FamilyCompositionFigures.RenderPng(f.Plot);
                        return native.SequenceEqual(File.ReadAllBytes(Path.Combine(pngFolder, f.Plot.FileName)))
                            && native.SequenceEqual(packet.Figures.Single(p => p.Figure.Id == "figure:" + f.Plot.FileName).Figure.PngBytes);
                    }));
                var doc = new ReportDocument { Title = "Stored study figure fixture", Sections = packet.Figures.GroupBy(f => f.SectionId)
                    .Select(g => new ReportSection(g.Key, g.Key, g.Select(f => (ReportBlock)f.Figure).ToArray())).ToArray() };
                doc = FamilyQuant.Report.Captions.ReportCaptionBuilder.WithCaptions(doc, packet.CaptionBindings);
                byte[] bytes = WordReportWriter.BuildBytes(doc);
                File.WriteAllBytes(Path.Combine(folder, "study-figures-" + locale + ".docx"), bytes);
                using var zip = new ZipArchive(new MemoryStream(bytes));
                using var word = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(bytes), false);
                var main = word.MainDocumentPart!;
                var images = main.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Select(blip =>
                { using var input = main.GetPartById(blip.Embed!.Value!).GetStream(); using var output = new MemoryStream();
                    input.CopyTo(output); return output.ToArray(); }).ToArray();
                Check(locale + " every Word image is byte-equal to its exported native PNG", images.Length == packet.Figures.Count
                    && packet.Figures.All(f => images.Any(image => image.SequenceEqual(f.Figure.PngBytes))));
                using var xmlInput = zip.GetEntry("word/document.xml")!.Open();
                var xml = XDocument.Load(xmlInput); XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                string body = string.Concat(xml.Descendants(w + "t").Select(t => t.Value));
                Check(locale + " Word contains every automatic caption verbatim", packet.Figures.All(f => body.Contains(f.Figure.Caption)));
                if (firstDocument != null) Check("Word report bytes match across en-US and pt-BR", firstDocument.SequenceEqual(bytes));
                firstDocument = bytes;
            }
        }
        finally { CultureInfo.CurrentCulture = oldCulture; CultureInfo.CurrentUICulture = oldUi; }
        double sourceBefore = sourceRows.Sum(r => r.WorldDeNovoEnvelope);
        manifest.FamilyWorldResult!.Composition[0].WorldDeNovoEnvelope += 99;
        Check("planned source figure and caption retain their immutable stored-data snapshot",
            sourceRows.Sum(r => r.WorldDeNovoEnvelope) == sourceBefore
            && sourcePlot.Caption.Numbers.Single(n => n.Label == "Family-world-only de novo signal").Value == sourceBefore);
        manifest.FamilyWorldResult.Composition[0].WorldDeNovoEnvelope -= 99;
        var many = new QuantManifest { FamilyRunShares = new FamilyRunSharesResult(), FamilySubclassParts = new FamilySubclassPartsResult() };
        many.FamilyRunShares.Runs.Add(new FamilyRunTotal { Run = "r", AssignedSignal = 100, MeasuredSignal = 100 });
        foreach (int id in Enumerable.Range(1, 23))
        {
            many.FamilyRunShares.Families.Add(new FamilyRunShareSummary { FamilyUserId = id, MeanShare = 1d / 23, AvailableRunCount = 1,
                Runs = new() { new() { Run = "r", Signal = 100d / 23, ShareOfAssignedSignal = 1d / 23 } } });
            many.FamilySubclassParts.Families.Add(new FamilySignalBreakdown { FamilyUserId = id, FamilySignal = 1,
                Parts = new() { new() { PartKey = "part", Label = "Part", Signal = 1, ShareOfFamily = 1 } } });
        }
        var paged = ReportStudyFigures.Plan(many, new FamilyClusterFile());
        var compositionPages = paged.Figures.Where(f => f.Plot.Chart is FamilyCompositionChart).ToArray();
        Check("long family composition is paged without changing any stored mean share",
            compositionPages.Length == 3 && compositionPages.Sum(p => ((FamilyCompositionChart)p.Plot.Chart).Rows.Count) == 23
            && compositionPages.All(p => p.Plot.ScreenHeight <= 710
                && ((FamilyCompositionChart)p.Plot.Chart).Rows.All(r => Math.Abs(r.MeanPercent - 100d / 23) < 1e-12)));
        Check("subclass pages preserve global pooled fractions instead of renormalizing each page",
            paged.Figures.Where(f => f.Plot.Chart is FamilySubclassChart).All(p => p.Plot.ScreenHeight <= 710
                && ((FamilySubclassChart)p.Plot.Chart).Rows.All(r => Math.Abs(r.Subclasses.Single().Percent - 100d / 23) < 1e-12)));
        var empty = ReportStudyFigures.Plan(new QuantManifest(), project, panelMap: map);
        Check("missing stored studies produce reasons instead of invented zero-valued figures", empty.Figures.Count == 0 && empty.Unavailable.Count >= 5);
        manifest.FamilySubclassParts!.Families[0].Parts[0].Signal -= 1;
        string refused = Path.Combine(folder, "invalid-refused");
        Check("invalid present study fails before the report output folder is written",
            Throws<ArgumentException>(() => ReportStudyFigures.Build(manifest, project, refused, panelMap: map)) && !Directory.Exists(refused));
    }

    private static (QuantManifest, FamilyClusterFile, PanelFamilyMap) ReportStudyFigureFixture()
    {
        var (manifest, project) = StoredCompositionFixture();
        project.FamilyPanel = new FamilyPanel { Name = "Fixture panel", Families = new()
        {
            new() { Key = "alpha", DisplayName = "Panel Alpha", Decision = FamilyPanelDecision.Quantify },
            new() { Key = "beta", DisplayName = "Panel Beta", Decision = FamilyPanelDecision.Quantify }
        } };
        project.ProteinSubclassLabels = project.MyExtendedFastaItems.Select((p, i) => new ProteinSubclassLabel
        { ProteinIndex = i, Identifier = p.SequenceIdentifier, FamilyUserId = p.UserID,
            PanelFamilyKey = p.UserID < 3 ? "alpha" : "beta", Status = ProteinSubclassStatus.Assigned }).ToList();
        project.PanelProteinAssignments = project.MyExtendedFastaItems.Select((p, i) => new PanelProteinAssignment
        { ProteinIndex = i, Identifier = p.SequenceIdentifier, FamilyUserId = p.UserID,
            PanelFamilyKey = p.UserID < 3 ? "alpha" : "beta", IsApplied = true, Reason = "Fixture verified assignment" }).ToList();
        var map = PanelAssignmentEvidenceStore.BuildFamilyMap(project.FamilyPanel, project.PanelProteinAssignments);
        manifest.FamilyProteinCounts = new FamilyProteinCountsResult { Families = new()
        { new() { FamilyUserId = 1, GreedyProteinCount = 1, UniquePeptideProteinCount = 1 },
            new() { FamilyUserId = 2, GreedyProteinCount = 2, UniquePeptideProteinCount = 1 },
            new() { FamilyUserId = 3, GreedyProteinCount = 3, UniquePeptideProteinCount = 2 } } };
        var second = manifest.FamilyPeptideLocalization!.Families.Single(f => f.FamilyUserId == 2);
        second.UnlocalizedSignal = 1;
        second.Proteins.Add(new FamilyRankedProtein { Rank = 1, ProteinIdentifier = "P2", ProteinLength = 200,
            ProteinSequenceSha256 = ProteinCoverageMapData.SequenceHash(project.MyExtendedFastaItems[1].Sequence), CreditedSignal = 8, ExactSignal = 8 });
        manifest.FamilyPeptideLocalization.Segments.Add(new LocalizedPeptideSegment { FamilyUserId = 2, ProteinIdentifier = "P2",
            Side = "identified", NormalizedSequence = new string('C', 11), Signal = 8,
            MatchKind = PeptideLocalizationMatchKind.ExactContainment, Intervals = new() { new() { Start = 15, End = 25 } } });
        manifest.FamilyWorldResult = new FamilyWorldSummary { MinimumTagLength = FamilyWorldOptions.DefaultMinimumTagLength,
            CompositionRules = FamilyWorldErrors.CompositionRule, Composition = new()
            {
                new() { Family = "Panel Alpha", IdentifiedEnvelope = 125.25, DatabaseDeNovoEnvelope = 30.125, WorldDeNovoEnvelope = 8.5 },
                new() { Family = "Panel Beta", IdentifiedEnvelope = .5, DatabaseDeNovoEnvelope = 100.125, WorldDeNovoEnvelope = .25 }
            } };
        return (manifest, project, map);
    }
}
