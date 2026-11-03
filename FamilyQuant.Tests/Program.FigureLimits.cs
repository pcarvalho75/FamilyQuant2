using FamilyQuant.Report;
using FamilyQuant.Report.Captions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.IO.Compression;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FigureLimitCommands() => new[]
    {
        new TestCommand("--test-figure-limits", 10479, TestCommandPhase.Normalized,
            args => args.Contains("--test-figure-limits", StringComparer.Ordinal), (_, work) =>
            {
                FigureLimitTests(Path.Combine(work, "figure-limits"));
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            }),
        new TestCommand("--test-project-figure-export", 10480, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-project-figure-export", (args, _) =>
            {
                if (args.Length is not (3 or 4)) throw new ArgumentException(
                    "Use --test-project-figure-export <copied-project.fqproj> <output-folder> [--plan-only|--report-figures-only].");
                if (args.Length == 4 && args[3] is not ("--plan-only" or "--report-figures-only")) throw new ArgumentException("Unknown figure check mode.");
                ProjectFigureExportTests(args[1], args[2], args.Length == 4 && args[3] == "--plan-only",
                    args.Length == 4 && args[3] == "--report-figures-only");
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            })
    };

    private static void FigureLimitTests(string folder)
    {
        EnsureApplication(); Directory.CreateDirectory(folder);
        var rows = Enumerable.Range(1, 1200).Select(i => new FamilyCompositionValue(i.ToString(CultureInfo.InvariantCulture),
            "Family " + i, 100d / 1200, null, 1, new[] { new FamilySubclassValue("part", "Subclass", 100d / 1200) }, 1, 1)).ToArray();
        var mean = new FamilyCompositionData(rows.Select(r => r with { Subclasses = Array.Empty<FamilySubclassValue>() }), "Mean per-run credited share");
        var pooled = new FamilyCompositionData(rows, "Credited signal", false);
        var figures = FamilyCompositionFigures.Build(mean, pooled);
        figures.Add(FamilyCompositionFigures.BuildAbundanceDiversity(mean));
        figures.Add(FamilyCompositionFigures.BuildRing(pooled, new FamilyRingGrouping(new[] { "1200" })));
        string sequence = new('A', 200);
        var coverage = new ProteinCoverageMapData(1, "P", "Protein", sequence, null, ProteinCoverageMapData.SequenceHash(sequence),
            Enumerable.Range(0, 160).Select(i => new CoveragePeptidePlacement(i.ToString(CultureInfo.InvariantCulture), "Peptide " + i,
                i % 2 == 0 ? CoveragePeptideSource.Identified : CoveragePeptideSource.DeNovo, CoveragePlacementKind.Exact,
                new[] { new CoverageInterval(10, 30) })), 100);
        figures.Add(ProteinCoverageFigures.Build(coverage));
        var manyParts = new FamilyCompositionData(new[] { new FamilyCompositionValue("one", "One family", 100, null, 0,
            Enumerable.Range(0, 2000).Select(i => new FamilySubclassValue("part" + i, "Part " + i, .05)).ToArray(), null, null) }, "Pooled original denominator", false);
        figures.Add(new("one-large-subclass.png", new FamilySubclassChart(manyParts), FamilySubclassChart.RecommendedHeight(manyParts)));
        foreach (var figure in figures)
            Check(figure.Chart.GetType().Name + " bounds the image height on a large stored result", figure.ScreenHeight <= 710);
        if (figures.All(f => f.ScreenHeight <= 710))
        {
            foreach (var figure in figures)
            {
                File.WriteAllBytes(Path.Combine(folder, figure.FileName), FamilyCompositionFigures.RenderPng(figure));
                CheckChartNative(figure.FileName, () => figure.Chart switch
                {
                    FamilyCompositionChart c => new FamilyCompositionChart(c.Data),
                    FamilySubclassChart c => new FamilySubclassChart(c.Data),
                    FamilyAbundanceDiversityChart c => new FamilyAbundanceDiversityChart(c.Data),
                    FamilyRingChart c => new FamilyRingChart(c.Data, new FamilyRingGrouping(new[] { "1200" })),
                    ProteinCoverageMapChart c => new ProteinCoverageMapChart(c.Data),
                    _ => throw new InvalidOperationException()
                }, folder, figure.ScreenHeight);
            }
        }
        var meanChart = (FamilyCompositionChart)figures[0].Chart;
        Check("bounded family overview retains original shares without renormalization", meanChart.Rows.Count < rows.Length
            && meanChart.Rows.All(r => r.MeanPercent == 100d / 1200) && meanChart.Data.Families.Count == 1200);
        var ring = figures.Select(f => f.Chart).OfType<FamilyRingChart>().Single();
        Check("bounded ring reference retains every original arc and canonical Others member", ring.Reference.Count < ring.Families.Count
            && Math.Abs(ring.Families.Sum(g => g.Percent) - 100) < 1e-9 && ring.Families.Single(g => g.IsOthers).Members.Single().Key == "1200");
        var coverageChart = figures.Select(f => f.Chart).OfType<ProteinCoverageMapChart>().Single();
        Check("bounded coverage retains both sources and leaves full positions available", coverageChart.VisiblePeptides.Count < coverage.Peptides.Count
            && coverageChart.VisiblePeptides.Any(p => p.Source == CoveragePeptideSource.Identified)
            && coverageChart.VisiblePeptides.Any(p => p.Source == CoveragePeptideSource.DeNovo) && coverage.Peptides.Count == 160);
        Check("every bounded overview explains its displayed limit and full data reference", figures.All(f =>
            f.Chart is IFigureDetails details && details.DetailNotice.Contains(FigureLayout.DetailFileName)
                && ReportCaptionBuilder.Figure(CompositionChartCaptions.Describe(f)).Contains(details.DetailNotice)));
        Check("coverage caption counts only placements actually drawn", CompositionChartCaptions.Describe(figures[4]).Numbers
            .Single(n => n.Label == "Identified placements drawn").Value == coverageChart.VisiblePeptides.Count(p => p.Source == CoveragePeptideSource.Identified));
        string csv = FigureDetailsCsv.Build(figures);
        File.WriteAllText(Path.Combine(folder, FigureLayout.DetailFileName), csv);
        var table = ReportCsvTable.Build("details", "Figure data", csv, "");
        Check("figure supplement retains all 1200 family shares and all 2000 subclass references",
            table.Rows.Count(r => r[0] == "family_composition.png" && r[1] == "family") == 1200
            && table.Rows.Count(r => r[0] == "one-large-subclass.png" && r[1] == "subclass") == 2000);
        Check("figure supplement retains every original localized interval", table.Rows.Count(r => r[1] == "peptide") == 160
            && table.Rows.Where(r => r[1] == "peptide").All(r => r[13] == "10" && r[14] == "30"));
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); Check("complete figure data are byte-stable across locales", csv == FigureDetailsCsv.Build(figures)); }
        finally { CultureInfo.CurrentCulture = culture; }
        var manifest = new QuantManifest { FamilyRunShares = new() { Runs = new() { new() { Run = "r", AssignedSignal = 100 } },
            Families = new() { new() { FamilyUserId = 1, MeanShare = 1, AvailableRunCount = 1, Runs = new() { new() { Run = "r", Signal = 100, ShareOfAssignedSignal = 1 } } } } },
            FamilySubclassParts = new() { Families = new() { new() { FamilyUserId = 1, FamilySignal = 100,
                Parts = Enumerable.Range(0, 2000).Select(i => new FamilySignalPart { PartKey = "p" + i, Label = "Part " + i, Signal = .05, ShareOfFamily = .0005 }).ToList() } } } };
        var project = new FamilyClusterFile(); var plan = ReportStudyFigures.Plan(manifest, project);
        Check("Word plan bounds a single family with thousands of subclass references", plan.Figures.All(f => f.Plot.ScreenHeight <= 710));
        var packet = ReportStudyFigures.Build(manifest, project, Path.Combine(folder, "report"));
        Check("bounded report references have complete detail sheets owned by the actual figure", packet.FigureDetailSheets.Count == 2
            && packet.FigureDetailSheets.All(s => packet.Figures.Any(f => f.Figure.Id == s.Id && f.SectionId == s.OwnerSectionId))
            && packet.FigureDetailSheets.All(s => s.Table.Rows.Count(r => r[1] == "subclass") == 2000));
        var document = new ReportDocument { Title = "Large figure references", Sections = new[] {
            new ReportSection(ReportSectionIds.Results, "Results", packet.Figures.Select(f => (ReportBlock)f.Figure).ToArray()) } };
        byte[] word = WordReportWriter.BuildBytes(document); File.WriteAllBytes(Path.Combine(folder, "large-figures.docx"), word);
        using var opened = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(word), false);
        var main = opened.MainDocumentPart!;
        var images = main.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Select(b =>
        { using var input = main.GetPartById(b.Embed!.Value!).GetStream(); using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray(); }).ToArray();
        Check("bounded Word images remain byte-equal to the program's own PNG exports", packet.Figures.All(f => images.Any(b => b.SequenceEqual(f.Figure.PngBytes))));
        File.WriteAllBytes(Path.Combine(folder, "large-figures_supplement.xlsx"), ReportSupplementWorkbook.BuildBytes(document, packet.FigureDetailSheets));
        string excludedFigure = packet.FigureDetailSheets[0].Id;
        byte[] selectedBook = ReportSupplementWorkbook.BuildBytes(document, packet.FigureDetailSheets,
            new ReportSelection { ExcludedIds = new[] { excludedFigure } });
        using var selected = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(new MemoryStream(selectedBook), false);
        Check("excluding a bounded figure also excludes its complete detail sheet", selected.WorkbookPart?.Workbook?.Sheets?.ChildElements.Count
            == packet.FigureDetailSheets.Count);
        manifest.FamilyRunShares!.MembershipSha256 = manifest.FamilySubclassParts!.MembershipSha256 = FamilyStudyMembership.Fingerprint(project);
        var assembled = ReportProjectExporter.Build(SyntheticAnalyticsRows(), manifest, project,
            Path.Combine(folder, "assembled"), familyResults: FamilyFixture(3));
        bool uniqueIds = assembled.SupplementSheets.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() == assembled.SupplementSheets.Count;
        Check("assembled large report combines figure details and caption trace under one unique owner id", uniqueIds);
        Check("assembled figure sheet retains both exact caption values and every full reference row",
            assembled.SupplementSheets.Any(s => s.Table.Columns.Contains("Displayed value") && s.Table.Columns.Contains("Data record_type")
                && s.Table.Rows.Count > 2000));
        if (uniqueIds) File.WriteAllBytes(Path.Combine(folder, "assembled_supplement.xlsx"),
            ReportSupplementWorkbook.BuildBytes(assembled.Document, assembled.SupplementSheets));
    }

    private static void ProjectFigureExportTests(string projectPath, string folder, bool planOnly, bool reportFiguresOnly)
    {
        EnsureApplication(); Directory.CreateDirectory(folder);
        void Stage(string message) { Console.WriteLine(message); File.AppendAllText(Path.Combine(folder, "progress.log"), message + Environment.NewLine); }
        string originalHash = TestFileHash.Upper(projectPath);
        Stage("Opening copied saved project.");
        var (project, rows, manifest) = ProjectStore.ReadFqproj(projectPath);
        Stage("Saved project loaded; planning stored figures.");
        IReadOnlyList<AnalyticsFigure> figures = reportFiguresOnly ? Array.Empty<AnalyticsFigure>() : StoredCompositionFigures.Build(manifest, project: project);
        var report = ReportStudyFigures.Plan(manifest, project);
        string PlanLine(string route, AnalyticsFigure f) => route + "\t" + f.FileName + "\t" + f.Chart.GetType().Name
            + "\t" + f.ScreenHeight.ToString("R", CultureInfo.InvariantCulture);
        File.WriteAllLines(Path.Combine(folder, "figure-plan.tsv"), new[] { "Route\tFile\tChart\tLayoutHeight" }
            .Concat(figures.Select(f => PlanLine("Results", f))).Concat(report.Figures.Select(f => PlanLine("Report", f.Plot))));
        Console.WriteLine("Results figures: " + figures.Count + "; report study figures: " + report.Figures.Count);
        foreach (var f in figures.OrderByDescending(f => f.ScreenHeight).Take(5)) Console.WriteLine(PlanLine("Largest Results", f));
        foreach (var f in report.Figures.OrderByDescending(f => f.Plot.ScreenHeight).Take(5)) Console.WriteLine(PlanLine("Largest report", f.Plot));
        if (planOnly) return;
        if (reportFiguresOnly)
        {
            Stage("Rendering the saved project's native report study figure packet.");
            var packet = ReportStudyFigures.Build(manifest, project, Path.Combine(folder, "report"));
            Check("real saved-project report figures fit bounded native dimensions", packet.Figures.Count == report.Figures.Count
                && report.Figures.All(f => f.Plot.ScreenHeight <= FigureLayout.MaximumUncappedHeight));
            var doc = new ReportDocument { Title = "Saved-project report figures", Sections = packet.Figures.GroupBy(f => f.SectionId)
                .Select(g => new ReportSection(g.Key, g.Key, g.Select(f => (ReportBlock)f.Figure).ToArray())).ToArray() };
            var sheets = ReportStudyFigures.CombineFigureSupplements(packet.FigureDetailSheets, ReportCaptionTrace.Build(doc, packet.CaptionBindings));
            byte[] docBytes = WordReportWriter.BuildBytes(doc);
            File.WriteAllBytes(Path.Combine(folder, "report-figures.docx"), docBytes);
            File.WriteAllBytes(Path.Combine(folder, "report-figures_supplement.xlsx"), ReportSupplementWorkbook.BuildBytes(doc, sheets));
            using var opened = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(docBytes), false);
            var mainPart = opened.MainDocumentPart!;
            var embedded = mainPart.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Select(b =>
            { using var input = mainPart.GetPartById(b.Embed!.Value!).GetStream(); using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray(); }).ToArray();
            Check("all real report-study PNG bytes are embedded verbatim in Word", embedded.Length == packet.Figures.Count
                && packet.Figures.All(f => embedded.Any(b => b.SequenceEqual(f.Figure.PngBytes))));
            Check("real figure workbook has unique ownership and retains full data with caption facts", sheets.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() == sheets.Count
                && sheets.Any(s => s.Table.Columns.Contains("Displayed value") && s.Table.Columns.Contains("Data record_type")));
            Check("copied project stays byte-identical during report figure export", originalHash == TestFileHash.Upper(projectPath));
            Stage("Saved-project Word figure and workbook checks finished.");
            return;
        }
        Stage("Preparing the actual Results export model and its current family attribution.");
        var model = new AnalyticsViewModel(rows, manifest, project) { ExportKind = ResultsExportKind.Figures };
        Check("real Results model retains its current family result", model.Families != null);
        string resultFolder = Path.Combine(folder, "results");
        Stage("Exporting all Results PNGs through the native asynchronous export writer.");
        var anchor = new System.Windows.Controls.Border();
        var operations = AppOperationHost.For(anchor);
        WaitWorldWalk(AppOperationHost.OnUi(anchor, () => model.ExportAsync(resultFolder, operations)), TimeSpan.FromMinutes(60));
        Stage(model.Status);
        Check("copied real project exports every Results figure", model.Status.StartsWith("Written to ", StringComparison.Ordinal)
            && Directory.Exists(resultFolder) && Directory.GetFiles(resultFolder, "*.png").Length == AnalyticsFigures.FileNames.Count + figures.Count);
        Check("complete Results figure data are exported beside the bounded PNGs", File.Exists(Path.Combine(resultFolder, FigureLayout.DetailFileName)));
        Stage("Building the actual complete Word report and its figure packet.");
        var exported = ReportProjectExporter.Build(rows, manifest, project, Path.Combine(folder, "report"), familyResults: model.Families);
        string wordPath = Path.Combine(folder, "report.docx");
        ReportProjectExporter.Write(exported, wordPath);
        using var word = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(wordPath, false);
        var main = word.MainDocumentPart!;
        var images = main.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Select(b =>
        { using var input = main.GetPartById(b.Embed!.Value!).GetStream(); using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray(); }).ToArray();
        Check("actual Word images equal the native program PNG exports", images.Length > 0
            && images.All(b => exported.Figures.Any(f => f.Figure.PngBytes.SequenceEqual(b))));
        Check("actual report has the complete figure reference supplement", exported.SupplementSheets.Any(s => s.Name.StartsWith("Figure data ", StringComparison.Ordinal))
            && File.Exists(Path.Combine(folder, "report_supplement.xlsx")));
        Check("copied input project remains byte-identical", originalHash == TestFileHash.Upper(projectPath));
        Stage("Copied-project Results and complete Word exports finished.");
    }
}
