using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportWordOthersCommands() => new[]
    {
        new TestCommand("--test-report-word-others", 10, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-report-word-others", (args, _) =>
            {
                ReportWordOthersChecks(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "fq-word-others-" + Guid.NewGuid().ToString("N")));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            })
    };

    private static void ReportWordOthersChecks(string folder)
    {
        Section("J26 - canonical Others in every quantitative Word view");
        EnsureApplication();
        var (manifest, project, _) = ReportStudyFigureFixture();
        project.FamilyPanel!.Families = Enumerable.Range(1, 3).Select(i => new FamilyPanelFamily
        {
            Key = "row-" + i, DisplayName = "Reference row " + i, Decision = FamilyPanelDecision.Quantify,
            TextFields = new() { new() { Name = "Notes", Text = "Original reference text " + i } },
        }).ToList();
        foreach (var assignment in project.PanelProteinAssignments!) assignment.PanelFamilyKey = "row-" + assignment.FamilyUserId;
        foreach (var label in project.ProteinSubclassLabels!) label.PanelFamilyKey = "row-" + label.FamilyUserId;
        MakePanelAssignmentsCurrent(project, new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } });
        var native = FamilyFixture(3);
        foreach (var pair in native.Families.Select((row, index) => (row, index)))
        {
            pair.row.FamilyUserId = pair.index + 1;
            pair.row.Description = "Measured row " + (pair.index + 1);
        }
        foreach (var row in native.SourceSequenceSummary.Families.Select((value, index) => (value, index)))
        { row.value.FamilyUserId = row.index + 1; row.value.Description = "Measured row " + (row.index + 1); }
        var labels = native.Families.ToDictionary(f => f.FamilyUserId, f => f.Description);
        const string hidden = "Measured row 3";
        var world = manifest.FamilyWorldResult!;
        world.Composition = new()
        {
            new() { Family = labels[1], Kind = FamilyWorldCompositionRow.CuratedKind, CuratedUserIds = new() { 1 }, IdentifiedEnvelope = 20 },
            new() { Family = hidden, Kind = FamilyWorldCompositionRow.UniProtKind, CuratedUserIds = new() { 3 }, IdentifiedEnvelope = 30 },
        };
        world.Families = new() { new() { Family = hidden, CreditedEnvelope = 30, DecoyEnvelope = 1, DecoyRate = 1d / 30, KnownCredited = 2 } };
        var partition = FamilyReportThreshold.Build(manifest.FamilyRunShares!, project);
        Check("fixture membership uses the mean even though the hidden pooled amount is largest",
            partition.OtherFamilies.Single().FamilyUserId == 3
            && manifest.FamilySubclassParts!.Families.Single(f => f.FamilyUserId == 3).FamilySignal > manifest.FamilySubclassParts.Families.First().FamilySignal);
        string original = JsonSerializer.Serialize(manifest), originalNative = JsonSerializer.Serialize(native);
        string expectedHeadline = ReportStudyExports.Headline(partition, manifest.FamilyProteinCounts, labels);
        var old = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        ReportProjectExport? first = null; byte[]? firstWord = null, firstBook = null;
        try
        {
            foreach (string locale in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
                var export = ReportProjectExporter.Build(SyntheticAnalyticsRows(), manifest, project, Path.Combine(folder, locale), familyResults: native);
                byte[] word = WordReportWriter.BuildBytes(export.Document);
                byte[] book = ReportSupplementWorkbook.BuildBytes(export.Document, export.SupplementSheets);
                if (first == null) { first = export; firstWord = word; firstBook = book; }
                else
                {
                    Check("canonical-visibility Word is byte equal across locales", firstWord!.SequenceEqual(word));
                    Check("canonical-visibility workbook is byte equal across locales", firstBook!.SequenceEqual(book));
                }
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, locale + ".docx"), word);
                File.WriteAllBytes(Path.Combine(folder, locale + "_supplement.xlsx"), book);
            }
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
        var document = first!.Document;
        Check("no quantitative Word table names a canonical Others member", !document.Sections.SelectMany(s => s.Blocks).OfType<ReportTable>()
            .SelectMany(t => t.Rows).SelectMany(r => r).Any(cell => cell.Contains(hidden, StringComparison.Ordinal)));
        var hiddenSection = document.Sections.Single(s => s.Id == "family:row-3");
        Check("below-threshold family brief has no observed share or count", !hiddenSection.Blocks.OfType<ReportParagraph>()
            .Any(p => p.Text.StartsWith("Observed ", StringComparison.Ordinal)));
        Check("original panel reference text and section stay", hiddenSection.Title == "Reference row 3"
            && hiddenSection.Blocks.OfType<ReportParagraph>().Any(p => p.Text.Contains("Original reference text 3", StringComparison.Ordinal)));
        Check("canonical Others member remains in the supplement list", first.SupplementSheets.Where(s => s.Id == "others-members")
            .SelectMany(s => s.Table.Rows).Any(r => r.Contains(hidden)));
        Check("complete hidden family observations remain in the workbook", first.SupplementSheets.Where(s => s.OwnerSectionId == "family:row-3")
            .SelectMany(s => s.Table.Rows).Any(r => r[0] == "Observed family share" && r[2] == "Mean assigned share" && r[3] == "0.19999999999999998"));
        Check("headline export is unchanged by Word visibility", first.CsvExports["family_composition_headline.csv"] == expectedHeadline);
        Check("hidden native count and part exports remain complete", first.CsvExports["family_protein_counts.csv"].Contains(hidden, StringComparison.Ordinal)
            && first.CsvExports["family_subclass_parts.csv"].Contains(hidden, StringComparison.Ordinal));
        using (var images = new ZipArchive(new MemoryStream(firstWord!)))
        {
            var embedded = images.Entries.Where(e => e.FullName.EndsWith(".png", StringComparison.Ordinal)).Select(e =>
            { using var stream = e.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }).ToArray();
            Check("filtered report PNGs equal program sidecars and embedded bytes", first.Figures.All(f =>
                File.ReadAllBytes(Path.Combine(folder, "en-US", f.Figure.Id["figure:".Length..])).SequenceEqual(f.Figure.PngBytes)
                && embedded.Any(png => png.SequenceEqual(f.Figure.PngBytes))));
        }
        using (var zip = new ZipArchive(new MemoryStream(firstWord!)))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
            Check("serialized Word text contains no named hidden quantitative row", !reader.ReadToEnd().Contains(hidden, StringComparison.Ordinal));
        var data = AnalyticsBuilder.Build(SyntheticAnalyticsRows(), manifest.PerRunTic);
        var plan = ReportFigureRenderer.Plan(data, manifest, project, native);
        var legacy = (FamilyContributionChart)plan.Charts.Single(c => c.FileName == AnalyticsFigures.FamilyContributionFile).CreateChart();
        Check("legacy contribution PNG input obeys the same membership", !legacy.Bars.Any(b => b.FamilyUserId == 3));
        Check("legacy plot preserves the whole measured denominator", legacy.MeasuredEnvelope == native.MeasuredTotalEnvelopeReferenceCalibrated);
        Check("rate PNG input excludes hidden mapped reference families", plan.Charts.Where(c => c.FileName.StartsWith("family_world_rates_", StringComparison.Ordinal))
            .All(c => ((ReportFamilyWorldRatesChart)c.CreateChart()).Rows.All(r => r.Family != hidden)));
        Check("figure caption numbers never name a hidden member", first.Figures.All(f => !f.Figure.Caption.Contains(hidden, StringComparison.Ordinal)));
        var studyPlan = ReportStudyFigures.Plan(manifest, project);
        var ring = (FamilyRingChart)studyPlan.Figures.Single(f => f.Plot.Chart is FamilyRingChart).Plot.Chart;
        Check("Others ring legend withholds member and subclass names while preserving its amount",
            ring.Families.Single(f => f.IsOthers).Parts.All(p => !p.Label.Contains("Reference row 3", StringComparison.Ordinal))
            && Math.Abs(ring.Families.Single(f => f.IsOthers).Percent - 100d * 600 / 1020) < 1e-10);
        Check("visibility changes no stored results or native attribution", original == JsonSerializer.Serialize(manifest) && originalNative == JsonSerializer.Serialize(native));
        const string repeated = "family_user_id,family,value\n1,Repeated,11\n3,Repeated,33\n";
        var repeatedTable = ReportWordMembership.Filter(ReportCsvTable.Build("fixture-duplicates", "Fixture", repeated, "", "family", "value"), repeated, partition);
        Check("identical display labels are filtered by source UserID", repeatedTable.Rows.Count == 1 && repeatedTable.Rows[0][1] == "11");
        string sensitivityIds = repeated.Replace("family_user_id", "family_id", StringComparison.Ordinal);
        Check("sensitivity family-id rows obey the same canonical membership", ReportWordMembership.Filter(
            ReportCsvTable.Build("sensitivity-table", "Fixture", sensitivityIds, ""), sensitivityIds, partition).Rows.Count == 1);
        var explicitPanel = FamilyPanelValidation.Clone(project.FamilyPanel!);
        explicitPanel.Families[1].Decision = FamilyPanelDecision.ReportUnderOthers;
        var explicitPartition = FamilyReportThreshold.Build(manifest.FamilyRunShares!, new FamilyClusterFile { FamilyPanel = explicitPanel, ReportThreshold = project.ReportThreshold },
            Enumerable.Range(1, 3).ToDictionary(id => id, id => (IReadOnlyList<string>)new[] { "row-" + id }));
        string countsCsv = ReportStudyExports.ProteinCounts(manifest.FamilyProteinCounts!, labels);
        var explicitCounts = ReportWordMembership.Filter(ReportCsvTable.Build("protein-counts", "Fixture", countsCsv, "", "family"), countsCsv, explicitPartition);
        Check("explicit Others decision hides an above-threshold member too", explicitCounts.Rows.Count == 1 && explicitCounts.Rows[0][0] == labels[1]);
        var many = FamilyFixture(42);
        for (int i = 0; i < many.Families.Count; i++) many.Families[i].FamilyUserId = i + 1;
        var manyShares = new FamilyRunSharesResult { Families = many.Families.Select(f => new FamilyRunShareSummary
        { FamilyUserId = f.FamilyUserId, MeanShare = 1d / many.Families.Count }).ToList() };
        var manyPlan = ReportFigureRenderer.Plan(data, new QuantManifest { FamilyRunShares = manyShares }, new FamilyClusterFile(), many);
        var legacyPages = manyPlan.Charts.Where(c => c.CreateChart() is FamilyContributionChart).ToArray();
        var pageRows = legacyPages.SelectMany(c => ((FamilyContributionChart)c.CreateChart()).Bars).ToArray();
        Check("legacy canonical rows are paged without an oversized PNG or a second Others rule", legacyPages.Length > 1 && legacyPages.All(c => c.Height <= 840)
            && pageRows.Where(b => b.FamilyUserId.HasValue).Select(b => b.FamilyUserId).Distinct().Count() == 42
            && pageRows.All(b => b.Kind != FamilyContributionBarKind.OtherFamilies));
    }
}
