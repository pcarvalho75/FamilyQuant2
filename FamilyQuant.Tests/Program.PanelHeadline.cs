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
    private static IEnumerable<TestCommand> PanelHeadlineCommands() =>
    [
        new("--test-panel-headline", 445, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-panel-headline", (args, _) =>
            {
                PanelHeadlineTests(args.Length > 1 ? args[1] : null);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void PanelHeadlineTests(string? output)
    {
        Section("Panel-family headline and composition figures use collapsed native membership");
        var (file, rows, hits, assigned, manifest) = EffectivePanelFixture();
        EffectivePanelFamilyStudy.Store(manifest, assigned, hits, file);
        var pooled = FamilyEnvelopeQuant.BuildFromAttributedPeptides(assigned);
        var labels = FamilyNaming.Names(file.MyExtendedFastaItems);
        var originalGrouping = FamilyReportThreshold.Build(manifest.FamilyRunShares!, file);
        string original = ReportStudyExports.Headline(originalGrouping, manifest.FamilyProteinCounts, labels);
        EnsureApplication();
        string directory = output ?? Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.PanelHeadline");
        var export = ReportProjectExporter.Build(rows, manifest, file, Path.Combine(directory, "figures"), familyResults: pooled);
        bool hasPanel = export.CsvExports.TryGetValue("panel_family_composition_headline.csv", out string? panel);
        Check("the report exports the new panel-unit headline separately", hasPanel);
        var table = hasPanel ? ReportCsvTable.Build("test", "Test", panel!, "") : null;
        Check("the shared-within-key peptide changes the headline to one fifty-percent family", table != null
            && table.Rows.Count(r => r[1] == "Main group" && r[4] == "50") == 1);
        Check("the original UserID headline stays byte-identical under its old export name", export.CsvExports["family_composition_headline.csv"] == original);
        Check("Word identifies the headline as the panel-family unit", export.Document.Sections.SelectMany(s => s.Blocks).OfType<ReportParagraph>()
            .Any(p => p.Text.Contains("headline", StringComparison.OrdinalIgnoreCase) && p.Text.Contains("effective panel-family totals", StringComparison.Ordinal)));
        var basis = ReportCompositionBasis.Select(file, manifest, labels);
        var plots = ReportStudyFigures.PlanForBasis(manifest, file, basis, reportGrouping: originalGrouping);
        var meanChart = (FamilyCompositionChart)plots.Figures.Single(p => p.Plot.Chart is FamilyCompositionChart).Plot.Chart;
        Check("the composition plot shares the native panel headline values and one-row identity", meanChart.Data.Families.Count(f => f.Label == "Main group" && f.MeanPercent == 50) == 1
            && meanChart.Data.Families.Count == 2);
        var subclassChart = (FamilySubclassChart)plots.Figures.Single(p => p.Plot.Chart is FamilySubclassChart).Plot.Chart;
        var ring = (FamilyRingChart)plots.Figures.Single(p => p.Plot.Chart is FamilyRingChart).Plot.Chart;
        var diversity = (FamilyAbundanceDiversityChart)plots.Figures.Single(p => p.Plot.Chart is FamilyAbundanceDiversityChart).Plot.Chart;
        Check("subclass, ring and abundance-diversity composition use the same collapsed family totals", new[] { subclassChart.Data, ring.Data, diversity.Data }
            .All(d => d.Families.Single(f => f.Label == "Main group").MeanPercent == 50));
        Check("source coverage diagnostics remain explicitly outside the primary composition unit", plots.Figures.Where(p => p.Plot.Chart is ProteinCoverageMapChart)
            .All(p => p.SectionId == ReportSectionIds.Supplements));
        string source = JsonSerializer.Serialize(manifest);
        _ = basis.Apply(ReportStoredResults.Apply(ReportCurrentResults.Build(AnalyticsBuilder.Build(rows, manifest.PerRunTic), manifest, pooled), manifest, labels, originalGrouping));
        Check("selecting a reporting basis never rewrites source studies", source == JsonSerializer.Serialize(manifest));
        var stored = manifest.EffectivePanelFamilies; manifest.EffectivePanelFamilies = null;
        bool refused = false;
        try { ReportCompositionBasis.Select(file, manifest); } catch (InvalidOperationException) { refused = true; }
        Check("an uncomputed multi-source panel study cannot silently fall back to the old unit", refused);
        manifest.EffectivePanelFamilies = stored;
        var single = EffectivePanelFixture();
        single.File.MyExtendedFastaItems[1].UserID = 11;
        single.File.PanelProteinAssignments![1].FamilyUserId = 11;
        single = PrepareEffectivePanelFixture(single.File, single.Rows);
        var singleLabels = FamilyNaming.Names(single.File.MyExtendedFastaItems);
        var before = ReportStudyExports.Headline(FamilyReportThreshold.Build(single.Manifest.FamilyRunShares!, single.File), single.Manifest.FamilyProteinCounts, singleLabels);
        var identical = ReportCompositionBasis.Select(single.File, single.Manifest, singleLabels);
        var after = ReportStudyExports.Headline(identical.Grouping!, identical.Studies.FamilyProteinCounts, identical.Labels);
        var beforeTable = ReportCsvTable.Build("before", "Before", before, "");
        var afterTable = ReportCsvTable.Build("after", "After", after, "");
        Check("one-to-one reporting reuses every stored row and denominator without arithmetic", identical.IsIdentity
            && ReferenceEquals(identical.Studies.FamilyRunShares, single.Manifest.FamilyRunShares)
            && beforeTable.Rows.Count == afterTable.Rows.Count && beforeTable.Rows.Zip(afterTable.Rows).All(p => p.First.Where((_, i) => i != 1).SequenceEqual(p.Second.Where((_, i) => i != 1))));
        string proofRoot = Path.Combine(directory, "proof-fixture");
        Directory.CreateDirectory(proofRoot);
        string proofProject = Path.Combine(proofRoot, "source.fqproj"), proofCache = Path.Combine(proofRoot, "cache");
        ProjectStore.WriteFqproj(proofProject, single.File, single.Rows, single.Manifest);
        _ = CurationHomologyCache.GetOrSearch(single.Rows, single.File, proofCache);
        Check("the one-to-one native proof verifies every non-label headline cell", PanelHeadlineProof(new[]
            { "--check-panel-headline", proofProject, Path.Combine(proofRoot, "identity") }) == 0);
        Check("the labelled counterfactual harness recomputes studies from verified retained hits", PanelHeadlineProof(new[]
            { "--check-panel-headline", proofProject, Path.Combine(proofRoot, "counterfactual"), proofCache, "--counterfactual" }) == 0);
        single.File.FamilyPanel = null; single.File.PanelProteinAssignments = null; single.Manifest.EffectivePanelFamilies = null;
        var noPanel = ReportCompositionBasis.Select(single.File, single.Manifest, singleLabels);
        Check("projects without a panel retain their original composition unit", !noPanel.UsesPanel && ReferenceEquals(noPanel.Studies, single.Manifest));
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "before-userid.csv"), original);
            if (hasPanel) File.WriteAllText(Path.Combine(output, "after-panel.csv"), panel);
        }
    }
}
