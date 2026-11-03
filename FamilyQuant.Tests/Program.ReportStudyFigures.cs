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
        Check("canonical Others without localized proteins remains explicitly unavailable",
            plan.Unavailable.Any(f => f.Title == "Protein coverage: Others" && f.Reason.Length > 0));
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
        ReportStudyCanonicalMembershipTests(folder);
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

    private static void ReportStudyCanonicalMembershipTests(string folder)
    {
        var project = new FamilyClusterFile { ReportThreshold = .2, FamilyPanel = new FamilyPanel { Families = new()
        {
            new() { Key = "a", DisplayName = "Panel A", Decision = FamilyPanelDecision.Quantify },
            new() { Key = "b", DisplayName = "Panel B", Decision = FamilyPanelDecision.Quantify },
            new() { Key = "x", DisplayName = "Excluded", Decision = FamilyPanelDecision.NotAFamily }
        } } };
        var assignments = Enumerable.Range(1, 4).Select(id => new PanelProteinAssignment
        { FamilyUserId = id, PanelFamilyKey = "a", IsApplied = true }).ToList();
        assignments.Add(new() { FamilyUserId = 4, PanelFamilyKey = "b", IsApplied = true });
        assignments.Add(new() { FamilyUserId = 5, PanelFamilyKey = "x", IsApplied = true });
        var map = PanelAssignmentEvidenceStore.BuildFamilyMap(project.FamilyPanel, assignments);
        var shares = new FamilyRunSharesResult { Runs = new() {
            new() { Run = "r1", AssignedSignal = 100 }, new() { Run = "r2", AssignedSignal = 1000 } } };
        var parts = new FamilySubclassPartsResult();
        var counts = new FamilyProteinCountsResult();
        double[][] values = { new[] { .4, .3 }, new[] { .05, .25 }, new[] { .15, .05 }, new[] { .3, .3 }, new[] { .1, .1 } };
        for (int i = 0; i < values.Length; i++)
        {
            int id = i + 1; double mean = values[i].Average();
            var runs = values[i].Select((v, r) => new FamilyRunShare { Run = "r" + (r + 1),
                Signal = v * shares.Runs[r].AssignedSignal, ShareOfAssignedSignal = v }).ToList();
            shares.Families.Add(new() { FamilyUserId = id, Runs = runs, MeanShare = mean, AvailableRunCount = 2,
                StandardDeviation = Math.Sqrt(values[i].Sum(v => Math.Pow(v - mean, 2))), PooledSignal = runs.Sum(r => r.Signal) });
            parts.Families.Add(new() { FamilyUserId = id, FamilySignal = runs.Sum(r => r.Signal), Parts = new() {
                new() { PartKey = "s", Label = "Part", Signal = runs.Sum(r => r.Signal), ShareOfFamily = 1 } } });
            counts.Families.Add(new() { FamilyUserId = id, GreedyProteinCount = id, UniquePeptideProteinCount = 1,
                SelectedProteinIdentifiers = id == 2 ? new() { "shared", "P2" } : id == 3 ? new() { "shared", "P3", "P4" } : new() });
        }
        var manifest = new QuantManifest { FamilyRunShares = shares, FamilySubclassParts = parts, FamilyProteinCounts = counts,
            FamilyWorldResult = new FamilyWorldSummary { MinimumTagLength = FamilyWorldOptions.DefaultMinimumTagLength,
                CompositionRules = FamilyWorldErrors.CompositionRule, Composition = Enumerable.Range(1, 5).Select(id => new FamilyWorldCompositionRow
                { Family = "Source " + id, CuratedUserIds = new() { id }, IdentifiedEnvelope = id, WorldDeNovoEnvelope = id * 2 }).ToList() } };
        var threshold = FamilyReportThreshold.Build(shares, project, map);
        var plan = ReportStudyFigures.Plan(manifest, project, panelMap: map);
        var meanRows = plan.Figures.Where(f => f.Plot.Chart is FamilyCompositionChart)
            .SelectMany(f => ((FamilyCompositionChart)f.Plot.Chart).Rows).ToArray();
        var other = meanRows.SingleOrDefault(r => r.Key == "others");
        Check("report means use one canonical Others row and omit every conflicting group", meanRows.Length == 2
            && meanRows.Any(r => r.Key == "1") && other != null && !meanRows.Any(r => r.Key is "2" or "3" or "4"));
        Check("Others mean and sample SD come from canonical combined run statistics", other != null
            && Math.Abs(other.MeanPercent - threshold.OthersAssignedStatistics.Mean!.Value * 100) < 1e-12
            && Math.Abs(other.SdPercent!.Value - threshold.OthersAssignedStatistics.StandardDeviation!.Value * 100) < 1e-12
            && other.RunCount == threshold.OthersAssignedStatistics.AvailableRunCount);
        var subclassRows = plan.Figures.Where(f => f.Plot.Chart is FamilySubclassChart)
            .SelectMany(f => ((FamilySubclassChart)f.Plot.Chart).Rows).ToArray();
        Check("pooled Others keeps the original full denominator and member subclass parts", subclassRows.Length == 2
            && subclassRows.SingleOrDefault(r => r.Key == "others") is { } pooledOther
            && Math.Abs(pooledOther.MeanPercent - 100d * 320 / 1100) < 1e-12
            && pooledOther.Subclasses.Count == 2 && Math.Abs(subclassRows.Sum(r => r.MeanPercent) - 60) < 1e-12);
        var scatter = (FamilyAbundanceDiversityChart)plan.Figures.Single(f => f.Plot.Chart is FamilyAbundanceDiversityChart).Plot.Chart;
        Check("diversity uses canonical Others and excludes conflicts", scatter.Data.Families.Count == 2
            && scatter.Data.Families.Any(r => r.Key == "others") && scatter.Points.All(p => p.Key is not "2" and not "3" and not "4" and not "5"));
        Check("Others diversity unions stored greedy selections instead of summing overlapping counts",
            scatter.Points.SingleOrDefault(p => p.Key == "others")?.ProteinCount == 4);
        var source = (FamilySignalOriginChart)plan.Figures.Single(f => f.Plot.Chart is FamilySignalOriginChart).Plot.Chart;
        Check("source figures combine canonical Others and refuse conflicted or excluded family rows", source.Rows.Count == 2
            && source.Rows.Single(r => r.Family == "Others").IdentifiedEnvelope == 5
            && source.Rows.All(r => !r.CuratedUserIds.Contains(4) && !r.CuratedUserIds.Contains(5))
            && plan.Unavailable.Count(f => f.Title.StartsWith("Family signal source:", StringComparison.Ordinal)) == 2);
        var ring = (FamilyRingChart)plan.Figures.Single(f => f.Plot.Chart is FamilyRingChart).Plot.Chart;
        Check("ring retains conflicts and excluded signal as separate grey categories on the full denominator",
            ring.Families.SingleOrDefault(f => f.Label == "Conflicting families") is { IsUnassigned: true, Percent: 30 }
            && ring.Families.SingleOrDefault(f => f.Label == "Not a panel family") is { IsUnassigned: true, Percent: 10 }
            && ring.Families.Where(f => !f.IsUnassigned).SelectMany(f => f.Members).All(r => r.Key is not "4" and not "5")
            && Math.Abs(ring.Families.Sum(f => f.Percent) - 100) < 1e-12
            && Math.Abs(ring.Families.SelectMany(f => f.Parts).Sum(p => p.Percent) - 100) < 1e-12);
        var grouping = FamilyRingGrouping.FromReportThreshold(threshold);
        CheckChartNative("canonical-membership-ring.png", () => new FamilyRingChart(ring.Data, grouping), folder,
            FamilyRingChart.RecommendedHeight(ring.Data, grouping));
        var packet = ReportStudyFigures.Build(manifest, project, Path.Combine(folder, "canonical-png"), panelMap: map);
        var ringCaption = packet.CaptionBindings.Single(p => p.Key.Contains("ring", StringComparison.Ordinal)).Value;
        Check("ring caption binds the exact separate unassigned pooled shares", ringCaption.Numbers.Any(n => n.Label == "Conflicting families pooled share" && n.Value == 30)
            && ringCaption.Numbers.Any(n => n.Label == "Not a panel family pooled share" && n.Value == 10));
        var document = new ReportDocument { Title = "Canonical membership fixture", Sections = new[] {
            new ReportSection(ReportSectionIds.Results, "Results", packet.Figures.Select(f => (ReportBlock)f.Figure).ToArray()) } };
        byte[] wordBytes = WordReportWriter.BuildBytes(document);
        File.WriteAllBytes(Path.Combine(folder, "canonical-membership.docx"), wordBytes);
        using (var word = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(wordBytes), false))
        {
            var main = word.MainDocumentPart!;
            var images = main.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Select(blip =>
            { using var input = main.GetPartById(blip.Embed!.Value!).GetStream(); using var output = new MemoryStream();
                input.CopyTo(output); return output.ToArray(); }).ToArray();
            Check("canonical Others/conflict figures remain byte-equal from native export through Word", plan.Figures.All(f =>
                images.Any(bytes => bytes.SequenceEqual(ReportStudyFigures.ExportPng(f)))));
        }
        counts.Families.Single(f => f.FamilyUserId == 3).SelectedProteinIdentifiers.Clear();
        var missingCounts = ReportStudyFigures.Plan(manifest, project, panelMap: map);
        Check("Others diversity remains unavailable when stored selected identifiers cannot support the union",
            ((FamilyAbundanceDiversityChart)missingCounts.Figures.Single(f => f.Plot.Chart is FamilyAbundanceDiversityChart).Plot.Chart)
                .UnavailableFamilies.Any(r => r.Key == "others"));
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
