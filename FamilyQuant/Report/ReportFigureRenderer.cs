using FamilyQuant.DeNovo;
using FamilyQuant.Report.Captions;
using System;
using FamilyQuant.Operations;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace FamilyQuant.Report;

public sealed record ReportChart(string SectionId, string FileName, string Title, string Caption,
    Func<FrameworkElement> CreateChart, double Width = ChartImageExporter.DefaultLayoutWidth,
    double Height = ChartImageExporter.DefaultLayoutHeight, FigureCaptionInput? CaptionInput = null);
public sealed record ReportFigureAvailability(string SectionId, string Title, string Reason)
{
    public bool SupplementOnly { get; init; }
}
public sealed record ReportFigurePlan(IReadOnlyList<ReportChart> Charts, IReadOnlyList<ReportFigureAvailability> Unavailable);
public sealed record ReportPlacedFigure(string SectionId, ReportFigure Figure);

/// <summary>Reuses the program's chart classes and PNG exporter. Call rendering on an STA thread.
/// An absent stored study is reported; a present but invalid study fails rather than being hidden.</summary>
public static class ReportFigureRenderer
{
    public static ReportFigurePlan Plan(AnalyticsData data, QuantManifest manifest,
        FamilyClusterFile curation, FamilyEnvelopeQuantResult? families, FamilyReportThresholdResult? reportGrouping = null)
    {
        reportGrouping ??= manifest.FamilyRunShares == null ? null : FamilyReportThreshold.Build(manifest.FamilyRunShares, curation);
        var visibleFamilies = families == null ? null : ReportFigureMembership.Visible(families, reportGrouping);
        var charts = new List<ReportChart>();
        var unavailable = new List<ReportFigureAvailability>();
        void Add(string section, string file, string title, string guidance, Func<FrameworkElement> factory,
            params CaptionNumber[] facts)
        {
            var native = factory();
            FigureCaptionInput input;
            if (native is XicMassChart or IsotopeCountChart or PercentIonCurrentChart or RecoveryChart or FamilyContributionChart)
                input = CompositionChartCaptions.Describe(new AnalyticsFigure(file, native, 540), data);
            else
            {
                if (facts.Length == 0) throw new InvalidOperationException("Numeric caption data are missing for " + file);
                input = new FigureCaptionInput(title, guidance + " Read each labelled axis and legend using its stated units.", facts);
            }
            if (native is FamilyContributionChart or FamilySignalShareChart)
                input = input with { Notes = (input.Notes ?? Array.Empty<string>()).Append(
                    "Family labels may be abbreviated. Match each printed family id to its complete name in the Family composition tables and companion workbook").ToArray() };
            if (reportGrouping != null && native is FamilyContributionChart or FamilySignalShareChart or ReportFamilySourceSequenceChart or DatabaseGapChart or ReportFamilyWorldRatesChart)
                input = input with { Notes = (input.Notes ?? Array.Empty<string>()).Append(
                    "Only canonical report members are shown individually. Others member details remain in the supplement; stored values and original denominators are unchanged").ToArray() };
            charts.Add(new(section, file, title, ReportCaptionBuilder.Figure(input), factory, CaptionInput: input));
        }
        void Missing(string section, string title, string reason = "Not computed in this project.") => unavailable.Add(new(section, title, reason));
        Add(ReportSectionIds.Quality, AnalyticsFigures.XicByMassFile, "Extracted signal by mass",
            "Peptide representatives grouped by mass, with identified and de novo signal shown on the chart's stated basis.", () => new XicMassChart(data.Peptides));
        Add(ReportSectionIds.Quality, AnalyticsFigures.IsotopesCreditedFile, "Isotopes credited",
            "Number of isotope windows carrying signal in the stored representatives. An absent isotope receives no credit.", () => new IsotopeCountChart(data.Peptides));
        Add(ReportSectionIds.Quality, AnalyticsFigures.PercentIonCurrentFile, "Ion current by mass",
            "Signal in each mass interval divided by the recorded RAW total ion current. Missing TIC is unavailable.", () => new PercentIonCurrentChart(data.Peptides, data.Grouped?.Tic ?? 0));
        Add(ReportSectionIds.Quality, AnalyticsFigures.RecoveryFile, "Recovery and M+0 sensitivity",
            "The chart separates the primary reference-calibrated estimate, M+0 sensitivity and directly observed diagnostic bases.", () => new RecoveryChart(data.Fractions, data.Grouped));
        Add(ReportSectionIds.Results, PresentationFigures.SignalByRunFile, "Signal by run",
            "Stored run-level native fractions, with the denominator and signal bases stated in the chart.", () => new SignalByRunChart(data.Fractions),
            new CaptionNumber("Acquisition runs", data.Fractions.Count, "runs", "0"));
        var pooledFacts = data.Grouped is { Tic: > 0 } pooled
            ? new[] { new CaptionNumber("Pooled primary recovery estimate", 100 * pooled.RecoveredFractionReferenceCalibrated, "%"),
                new CaptionNumber("Pooled M+0 recovery", 100 * pooled.RecoveredFractionM0, "%") }
            : new[] { new CaptionNumber("Pooled records available", data.Grouped == null ? 0 : 1, "records", "0") };
        Add(ReportSectionIds.Results, PresentationFigures.SignalUpliftFile, "Signal recovery components",
            "Grouped primary signal estimate and its components. The de novo estimate is distinct from directly observed isotope-window signal.", () => new ReportSignalUpliftChart(data.Grouped),
            pooledFacts);
        charts[^1] = charts[^1] with { Width = ReportPrintChart.LayoutWidth, Height = ReportPrintChart.LayoutHeight };
        if (visibleFamilies != null)
        {
            var bars = FamilyContributionPlot.Fold(visibleFamilies, reportGrouping == null ? FamilyContributionPlot.DefaultTopN : 0);
            var pages = reportGrouping == null ? new[] { bars.ToArray() } : bars.Chunk(12).DefaultIfEmpty(Array.Empty<FamilyContributionBar>()).ToArray();
            for (int page = 0; page < pages.Length; page++)
            {
                var pageBars = pages[page];
                string file = page == 0 ? AnalyticsFigures.FamilyContributionFile : Path.GetFileNameWithoutExtension(AnalyticsFigures.FamilyContributionFile)
                    + "_" + (page + 1).ToString(CultureInfo.InvariantCulture) + ".png";
                Add(ReportSectionIds.Results, file, "Family signal contributions" + (pages.Length > 1 ? " - page " + (page + 1).ToString(CultureInfo.InvariantCulture) : ""),
                    "Current curated family contributions on the chart's stated shared-peptide basis. Shared and unattributed signal remain explicit.",
                    () => reportGrouping == null ? new FamilyContributionChart(visibleFamilies)
                        : new FamilyContributionChart(pageBars, measuredEnvelope: visibleFamilies.MeasuredTotalEnvelopeReferenceCalibrated, identityThresholdFraction: visibleFamilies.IdentityThreshold));
                if (reportGrouping != null) charts[^1] = charts[^1] with { Height = Math.Max(540, 240 + pageBars.Length * 50) };
            }
            Add(ReportSectionIds.Results, PresentationFigures.FamilySignalShareFile, "Family signal and de novo share",
                "Family signal against its de novo contribution; these axes describe abundance and evidence source, not diversity.", () => new FamilySignalShareChart(visibleFamilies, 10),
                new CaptionNumber("Family rows shown from the stored attribution", visibleFamilies.Families.Count, "families", "0"));
            var census = visibleFamilies.SourceSequenceSummary;
            var sourcePages = ReportFamilySourceSequenceChart.Pages(census);
            int selected = sourcePages.Sum(page => page.Count);
            if (selected == 0)
                Missing(ReportSectionIds.Results, "Family source sequence counts",
                    "The stored census has no source-resolved family sequences for the PEAKS de-novo-only and DeepNovo disjoint sets. "
                    + "Other de novo sources are separate; this does not establish that all de novo evidence is absent. "
                    + "The complete stored census remains in the companion workbook's Complete family source sequence census table.");
            for (int page = 0; page < sourcePages.Count; page++)
            {
                var pageRows = sourcePages[page];
                string file = page == 0 ? PresentationFigures.FamilySourceSequenceFile
                    : Path.GetFileNameWithoutExtension(PresentationFigures.FamilySourceSequenceFile) + "_" + (page + 1).ToString(CultureInfo.InvariantCulture) + ".png";
                Add(ReportSectionIds.Results, file, "Family source sequence counts - page " + (page + 1).ToString(CultureInfo.InvariantCulture),
                    "The highest-count families with de novo sequences are selected by descending union count, ties by family id, and split across pages. "
                        + "Sequence counts are not gene or proteoform counts. The complete stored census and full names are retained in the companion workbook's Complete family source sequence census table; match by family id.",
                    () => new ReportFamilySourceSequenceChart(pageRows),
                    new("Selected highest-count families across pages", selected, "families", "0"),
                    new("Family rows in the visible stored sequence census", census.Families.Count, "families", "0"),
                    new("Eligible families with de novo sequences", census.Families.Count(row => row.DeNovoUnionDistinctSequences > 0), "families", "0"),
                    new("Input identified distinct sequences", census.SearchInputDistinctSequences, "sequences", "0"),
                    new("Family rows on this page", pageRows.Count, "families", "0"));
                charts[^1] = charts[^1] with { Width = ReportPrintChart.LayoutWidth, Height = ReportFamilySourceSequenceChart.RequiredHeight(pageRows) };
            }
        }
        else Missing(ReportSectionIds.Results, "Family figures", "Family attribution is unavailable in this project.");
        if (manifest.DeNovoSourceScoreDistributions.Count > 0)
        {
            foreach (var distribution in manifest.DeNovoSourceScoreDistributions) distribution.Validate();
            var inputs = manifest.DeNovoInputs.Select(i => i.SourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sources = manifest.DeNovoSourceScoreDistributions.Select(i => i.SourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!inputs.SetEquals(sources)) throw new InvalidDataException("Stored de novo score distributions do not match the recorded input sources.");
            Add(ReportSectionIds.Quality, PresentationFigures.DeNovoScoreDistributionFile, "De novo source scores",
                "Each source uses its own native score scale and recorded cutoff; the scores are not pooled onto one scale.", () => new DeNovoScoreDistributionChart(manifest.DeNovoSourceScoreDistributions),
                new("Stored source distributions", manifest.DeNovoSourceScoreDistributions.Count, "sources", "0"),
                new("Raw rows across those sources", manifest.DeNovoSourceScoreDistributions.Sum(s => s.RawRows), "rows", "0"));
        }
        else if (manifest.DeNovoScoreDistribution is { } scores)
        {
            scores.Validate();
            if (manifest.DeNovoInputs.Select(i => i.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                throw new InvalidDataException("A multi-source project requires every source's stored score distribution.");
            Add(ReportSectionIds.Quality, PresentationFigures.DeNovoScoreDistributionFile, "De novo scores",
                "Recorded confidence distribution and filtering stages for the stored source.", () => new DeNovoScoreDistributionChart(scores),
                new("Raw source rows", scores.RawRows, "rows", "0"), new("Rows passing all stored criteria", scores.RowsPassingAllCriteria, "rows", "0"));
        }
        else Missing(ReportSectionIds.Quality, "De novo score distributions");
        if (manifest.DeNovoIdentification is { } identification)
        {
            identification.Validate();
            Add(ReportSectionIds.Quality, PresentationFigures.DeNovoConsolidationFile, "De novo consolidation",
                "Imported calls, accepted calls, pooled sequences and credited representatives have different counting grains.", () => new DeNovoConsolidationChart(data, manifest),
                new("Filtered spectrum calls", identification.FilteredSpectrumCalls, "calls", "0"),
                new("Carried-forward spectrum calls", identification.CarriedForwardSpectrumCalls, "calls", "0"));
            Add(ReportSectionIds.Quality, PresentationFigures.PeptideEvidenceFile, "Peptide representatives",
                "Credited de novo representatives retain run, canonical peptidoform, charge and precursor mass after cross-side suppression. "
                    + "Stored representatives before cross-side suppression are a separate population; neither count is sequence diversity.",
                () => new PeptideRepresentativesChart(data, identification),
                new("Credited de novo representatives after cross-side suppression", data.Grouped?.DeNovoPeptides
                    ?? data.Peptides.Select(p => p.Run).Distinct(StringComparer.OrdinalIgnoreCase).Sum(run => data.Fractions.FirstOrDefault(f =>
                        !f.IsGrouped && string.Equals(f.Run, run, StringComparison.OrdinalIgnoreCase))?.DeNovoPeptides ?? 0), "representatives", "0"),
                new("Stored identified representatives before cross-side suppression", data.IdentifiedCount, "representatives", "0"),
                new("Stored de novo representatives before cross-side suppression", data.DeNovoCount, "representatives", "0"));
        }
        else Missing(ReportSectionIds.Quality, "De novo consolidation and representatives");
        if (manifest.IdentificationConcordance is { } concordance)
        {
            concordance.Validate();
            if (concordance.PrecursorQualifiedDatabaseSearchPairs > 0)
                Add(ReportSectionIds.Quality, PresentationFigures.DatabaseSearchConcordanceFile, "Database search concordance",
                    "Sequence agreement among precursor-qualified one-to-one PSM pairs. Raw scan overlap is not its denominator.", () => new DatabaseSearchConcordanceChart(concordance),
                    new("Precursor-qualified database-search pairs", concordance.PrecursorQualifiedDatabaseSearchPairs, "pairs", "0"),
                    new("Exact pairs", concordance.ExactPrecursorQualifiedDatabaseSearchPairs, "pairs", "0"));
            else Missing(ReportSectionIds.Quality, "Database search concordance",
                "No precursor-qualified comparable pairs were recorded. Stored audit counts: PatternLab PSM rows "
                + concordance.PatternLabPsmRows.ToString(CultureInfo.InvariantCulture) + "; PEAKS database PSM rows "
                + concordance.PeaksDatabasePsmRows.ToString(CultureInfo.InvariantCulture) + "; comparable pairs "
                + concordance.PrecursorQualifiedDatabaseSearchPairs.ToString(CultureInfo.InvariantCulture) + "; exact pairs "
                + concordance.ExactPrecursorQualifiedDatabaseSearchPairs.ToString(CultureInfo.InvariantCulture)
                + ". No agreement rate is estimated. The complete audit remains in the table and companion workbook.");
            if (manifest.DeNovoOverlap is { HasScanLevelSequenceComparison: true } overlap)
            {
                overlap.Validate();
                Add(ReportSectionIds.Quality, PresentationFigures.DeNovoSearchCrosscheckFile, "De novo alternative triage",
                    "Stored overlap and sequence-comparison stages, with scan and precursor denominators kept distinct. "
                        + "Source scope: " + overlap.SourceScopeLabel + ". Contextual search overlap is not a caller-accuracy estimate. "
                        + (overlap.IsStrictDeNovoOnlyScope ? "De-novo-only export calls are alternative hypotheses, not failed database searches. " : "")
                        + "PEAKS database sequence context is compared at the same scan and charge.", () => new ReportDeNovoTriageChart(overlap),
                    new CaptionNumber("Filtered de novo rows", overlap.FilteredDeNovoRows, "rows", "0"),
                    new CaptionNumber("Precursor-matched rows", overlap.SamePrecursorWithinTolerance, "rows", "0"),
                    new CaptionNumber("Same-charge PEAKS database context", concordance.DeNovoRowsSharingPeaksDatabaseScanAndCharge, "rows", "0"),
                    new CaptionNumber("Exact PEAKS database sequence in that context", concordance.DeNovoRowsExactToPeaksDatabaseAtSameCharge, "rows", "0"));
                charts[^1] = charts[^1] with { Width = ReportPrintChart.LayoutWidth, Height = ReportPrintChart.LayoutHeight };
            }
            else Missing(ReportSectionIds.Quality, "De novo alternative triage");
        }
        else Missing(ReportSectionIds.Quality, "Database search concordance and alternative triage");
        if (manifest.ReferenceEnvelopeModelDiagnostics is { } diagnostics)
        {
            diagnostics.Validate();
            Add(ReportSectionIds.Quality, PresentationFigures.ReferenceEnvelopeDiagnosticsFile, "Reference envelope diagnostics",
                "Stored model support and error diagnostics for the reference-calibrated envelope estimate.", () => new ReferenceEnvelopeDiagnosticsChart(diagnostics),
                new("Identified reference peptides", diagnostics.IdentifiedPeptides, "peptides", "0"),
                new("Cross-validated peptides", diagnostics.CrossValidatedPeptides, "peptides", "0"));
        }
        else Missing(ReportSectionIds.Quality, "Reference envelope diagnostics");
        if (manifest.PresentationIdentitySweep.Count > 0)
        {
            int components = ReferenceComponentSummary.FromCuration(curation).AllComponents;
            int identity = manifest.PresentationIdentitySweep.OrderBy(r => Math.Abs(r.Groups - components))
                .ThenByDescending(r => r.Identity).First().Identity;
            Add(ReportSectionIds.Sensitivity, PresentationFigures.IdentitySweepFile, "Identity sensitivity",
                "The identity scan recorded in this project, without recomputing clustering.", () => new ReferenceIdentitySweepChart(manifest.PresentationIdentitySweep, identity),
                new("Stored identity thresholds", manifest.PresentationIdentitySweep.Count, "thresholds", "0"), new("Highlighted identity", identity, "%"));
            var scanFacts = new List<CaptionNumber> { new("Stored identity thresholds", manifest.PresentationIdentitySweep.Count, "thresholds", "0") };
            if (curation.ClusteringIdentity > 0) scanFacts.Add(new("This file's clustering identity", curation.ClusteringIdentity, "%"));
            bool hasDecoys = manifest.PresentationIdentitySweep.Any(r => r.Groups > r.TargetGroups || r.MixedGroups > 0);
            int? floor = hasDecoys && manifest.PresentationIdentitySweep.Max(r => r.Score) > 0
                ? IdentityScanPlot.CleanFloor(manifest.PresentationIdentitySweep) : null;
            if (floor.HasValue) scanFacts.Add(new("Recorded clean-range floor", floor.Value, "%"));
            Add(ReportSectionIds.Sensitivity, "identity_scan.png", "Identity scan",
                "Horizontal position runs from stricter to looser identity. The left axis counts non-contaminant target connected components; "
                    + "the right axis is the percentage of families mixing target and decoy. Family mixing is not an identification FDR. "
                    + "A marked floor is the lowest identity in the uninterrupted clean range from the strictest recorded threshold, not an optimum or a guarantee against target-target fusions. "
                    + (floor.HasValue ? "The recorded file identity and the clean-range floor are separate reference points." : "No clean-range floor is drawn for these data."),
                () => new IdentityScanChart(manifest.PresentationIdentitySweep,
                    curation.ClusteringIdentity > 0 ? curation.ClusteringIdentity : null),
                scanFacts.ToArray());
        }
        else Missing(ReportSectionIds.Sensitivity, "Identity sensitivity");
        if (manifest.DeNovoCallerAgreement is { } agreement)
        {
            agreement.Validate();
            Add(ReportSectionIds.Quality, PresentationFigures.DeNovoAgreementFile, "De novo caller agreement",
                "Population: " + agreement.PopulationDefinition + ". Observed source floors describe the imported exports, not configured acceptance cutoffs. "
                    + "This comparison is separate from the jointly accepted source-score population. Pairing: " + agreement.PrimaryPairGate,
                () => new DeNovoAgreementChart(agreement),
                new("Precursor-qualified comparisons", agreement.Total, "comparisons", "0"), new("Identical comparisons", agreement.Identical, "comparisons", "0"),
                new("Observed PEAKS confidence floor", agreement.PeaksObservedMinimumConfidence),
                new("Observed DeepNovo confidence floor", agreement.DeepNovoObservedMinimumConfidence));
        }
        else Missing(ReportSectionIds.Quality, "De novo caller agreement");
        if (manifest.DeNovoSourceVenn is { } venn)
        {
            venn.Validate();
            Add(ReportSectionIds.Quality, PresentationFigures.DeNovoSourceVennFile, "De novo source sets",
                "Stored sequence-set regions for the recorded sources, with their stated normalization.", () => new DeNovoSourceVennChart(venn),
                new("Filtered pooled sequence union", venn.FilteredBeforeUnion.Union, "sequences", "0"),
                new("Carried-forward pooled claim union", venn.CarriedForwardClaimProvenance.Union, "sequences", "0"));
        }
        else Missing(ReportSectionIds.Quality, "De novo source sets");
        if (manifest.DeNovoAcceptedScoreScatter is { Total: > 0 } scatter)
        {
            scatter.Validate();
            var acceptedFacts = new List<CaptionNumber> { new("Accepted source-pair comparisons", scatter.Total, "comparisons", "0"),
                new("Identical PTM-cleaned accepted pairs", scatter.Matches, "comparisons", "0"),
                new("Identical share of accepted pairs", 100 * scatter.MatchFraction, "%") };
            if (scatter.PeaksMinimumAcceptedScore is double peaksCutoff) acceptedFacts.Add(new("Recorded PEAKS acceptance cutoff", peaksCutoff));
            if (scatter.DeepNovoMinimumAcceptedScore is double deepCutoff) acceptedFacts.Add(new("Recorded DeepNovo acceptance cutoff", deepCutoff));
            Add(ReportSectionIds.Quality, PresentationFigures.DeNovoAcceptedScoreScatterFile, "Accepted source scores",
                "Only compatible precursor hypotheses accepted by both complete source filters are shown; this differs from all imported export calls. "
                    + scatter.PeaksFilterDescription + ". " + scatter.DeepNovoFilterDescription + ". " + DeNovoAcceptedScoreScatter.ScoreScaleCaveat,
                () => new DeNovoAcceptedScoreScatterChart(scatter), acceptedFacts.ToArray());
        }
        else Missing(ReportSectionIds.Quality, "Accepted source score scatter");
        if (manifest.PresentationDeNovoVariants.Count > 0)
        {
            DeNovoVariantComparison.ValidateTalkVariantSet(manifest.PresentationDeNovoVariants);
            Add(ReportSectionIds.Sensitivity, PresentationFigures.DeNovoVariantComparisonFile, "Stored source variants",
                "The source variants actually computed and recorded in this project. Red whiskers compare fixed-bin and pooled-ratio envelope models; "
                    + "they show model sensitivity, not run-to-run SD or a confidence interval.", () => new DeNovoVariantComparisonChart(manifest.PresentationDeNovoVariants),
                new CaptionNumber("Stored source variants", manifest.PresentationDeNovoVariants.Count, "variants", "0"));
            var comparison = DeNovoTalkComparison.Build(manifest.PresentationDeNovoVariants);
            Add(ReportSectionIds.Sensitivity, PresentationFigures.DeNovoTalkComparisonFile, "Source effect comparison",
                "The program's comparison of the stored, provenance-matched source variants on one common search baseline. "
                    + "The Both callers bar uses " + DeNovoVariantComparison.OnlyPlusDeepNovoVariantName
                    + " (PEAKS de-novo-only plus DeepNovo). " + DeNovoVariantComparison.AllPlusDeepNovoVariantName
                    + " includes PEAKS-all and is a separate variant; its headline must not be substituted for the bar shown here.", () => new DeNovoTalkComparisonChart(comparison),
                new CaptionNumber("Stored source variants", manifest.PresentationDeNovoVariants.Count, "variants", "0"));
        }
        else Missing(ReportSectionIds.Sensitivity, "Source variant comparison");
        Missing(ReportSectionIds.Sensitivity, "Source score cutoff sweep", "A score cutoff sweep is not stored in this project.");
        Missing(ReportSectionIds.Quality, "Unfiltered source score scatter", "The underlying source-pair score study is not stored in this project.");
        Missing(ReportSectionIds.Quality, "Source set novelty", "The underlying three-source sequence-set study is not stored in this project.");
        var world = manifest.FamilyWorldResult?.ForLength(manifest.DatabaseGapMinimumTagLength ?? FamilyWorldOptions.DefaultMinimumTagLength);
        var hiddenWorld = world == null ? new HashSet<string>(StringComparer.Ordinal) : ReportWordMembership.HiddenWorldFamilies(world, reportGrouping);
        if (world is { CompositionRules.Length: > 0 })
        {
            var rows = world.Composition.Where(r => !hiddenWorld.Contains(r.Family)).OrderByDescending(r => r.WorldDeNovoEnvelope).ThenByDescending(r => r.TotalEnvelope)
                .ThenBy(r => r.Family, StringComparer.Ordinal).ToArray();
            Add(ReportSectionIds.DatabaseGap, "database_gap.png", "Database gap",
                "Identified, database-mapped de novo and family-world-only signal are separate components. The family-world-only component is excluded from database family composition.",
                () => new DatabaseGapChart(rows, ChartPalette.Light), new("Family rows shown", rows.Length, "families", "0"),
                new("Family-world-only envelope shown", rows.Sum(r => r.WorldDeNovoEnvelope), "area units", "G5"));
            charts[^1] = charts[^1] with { Height = Math.Max(540, 70 + rows.Length * 30) };
        }
        else Missing(ReportSectionIds.DatabaseGap, "Database gap");
        if (world != null)
        {
            var rows = world.Families.Where(r => !hiddenWorld.Contains(r.Family)).OrderBy(r => r.Family, StringComparer.Ordinal).ToArray();
            for (int offset = 0; offset < rows.Length; offset += 12)
            {
                var page = rows.Skip(offset).Take(12).ToArray();
                string suffix = (offset / 12 + 1).ToString(CultureInfo.InvariantCulture);
                Add(ReportSectionIds.Quality, "family_world_rates_" + suffix + ".png", "Family-world rates " + suffix,
                    "Stored decoy signal divided by credited signal, and correct known credits divided by known credits, by family. Missing rates remain unavailable; supporting counts and the evidence floor are printed in the quality table.",
                    () => new ReportFamilyWorldRatesChart(page), new("Family rows on this page", page.Length, "families", "0"),
                    new("Known credited sequences on this page", page.Sum(r => r.KnownCredited), "sequences", "0"));
            }
            if (rows.Length == 0) Missing(ReportSectionIds.Quality, "Family-world error rates", "No individually reportable per-family rates are available. Complete stored rates remain in the companion workbook.");
        }
        else Missing(ReportSectionIds.Quality, "Family-world error rates");
        return new(charts, unavailable);
    }

    /// <summary>The exact files written here are also embedded; no second PNG encoder is used.</summary>
    public static IReadOnlyList<ReportPlacedFigure> Render(ReportFigurePlan plan, string pngFolder,
        IReadOnlyDictionary<string, string>? captions = null, OperationContext? operation = null,
        int figureOffset = 0, int? figureTotal = null)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Report figure rendering requires an STA thread.");
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Directory.CreateDirectory(pngFolder);
            int total = figureTotal ?? plan.Charts.Count;
            if (figureOffset == 0) operation?.Report("report-prepare-figure", "Preparing report figures", 0, total);
            return plan.Charts.Select((chart, index) =>
            {
                int ordinal = figureOffset + index + 1;
                operation?.Report("report-prepare-figure", "Figure " + ordinal.ToString(CultureInfo.InvariantCulture) + " of "
                    + total.ToString(CultureInfo.InvariantCulture) + ": " + chart.Title, ordinal, total);
                string path = ChartImageExporter.Save(chart.CreateChart(), Path.Combine(pngFolder, chart.FileName), chart.Width, chart.Height,
                    variant: ChartPaletteVariant.Light);
                string caption = captions != null && captions.TryGetValue(chart.FileName, out string? text) ? text : chart.Caption;
                return new ReportPlacedFigure(chart.SectionId, new("figure:" + chart.FileName, chart.Title, File.ReadAllBytes(path), caption,
                    (int)Math.Round(chart.Width * ChartImageExporter.DefaultScale), (int)Math.Round(chart.Height * ChartImageExporter.DefaultScale)));
            }).ToArray();
        }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
    }
}
