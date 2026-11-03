using FamilyQuant.DeNovo;
using FamilyQuant.Report.Captions;
using System;
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
public sealed record ReportFigureAvailability(string SectionId, string Title, string Reason);
public sealed record ReportFigurePlan(IReadOnlyList<ReportChart> Charts, IReadOnlyList<ReportFigureAvailability> Unavailable);
public sealed record ReportPlacedFigure(string SectionId, ReportFigure Figure);

/// <summary>Reuses the program's chart classes and PNG exporter. Call rendering on an STA thread.
/// An absent stored study is reported; a present but invalid study fails rather than being hidden.</summary>
public static class ReportFigureRenderer
{
    public static ReportFigurePlan Plan(AnalyticsData data, QuantManifest manifest,
        FamilyClusterFile curation, FamilyEnvelopeQuantResult? families)
    {
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
            "Grouped primary signal estimate and its components. The de novo estimate is distinct from directly observed isotope-window signal.", () => new SignalUpliftChart(data.Grouped),
            pooledFacts);
        if (families != null)
        {
            Add(ReportSectionIds.Results, AnalyticsFigures.FamilyContributionFile, "Family signal contributions",
                "Current curated family contributions on the chart's stated shared-peptide basis. Shared and unattributed signal remain explicit.", () => new FamilyContributionChart(families));
            Add(ReportSectionIds.Results, PresentationFigures.FamilySignalShareFile, "Family signal and de novo share",
                "Family signal against its de novo contribution; these axes describe abundance and evidence source, not diversity.", () => new FamilySignalShareChart(families, 10),
                new CaptionNumber("Family rows in the stored attribution", families.Families.Count, "families", "0"));
            Add(ReportSectionIds.Results, PresentationFigures.FamilySourceSequenceFile, "Family source sequence counts",
                "Distinct source sequence counts for the current family attribution. Sequence counts are not gene or proteoform counts.", () => new FamilySourceSequenceChart(families.SourceSequenceSummary),
                new("Family rows in the stored sequence census", families.SourceSequenceSummary.Families.Count, "families", "0"),
                new("Input identified distinct sequences", families.SourceSequenceSummary.SearchInputDistinctSequences, "sequences", "0"));
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
                "Representative counts and evidence categories from the stored consolidation analysis.", () => new PeptideRepresentativesChart(data, identification),
                new("Identified representatives", data.IdentifiedCount, "representatives", "0"),
                new("De novo representatives", data.DeNovoCount, "representatives", "0"));
        }
        else Missing(ReportSectionIds.Quality, "De novo consolidation and representatives");
        if (manifest.IdentificationConcordance is { } concordance)
        {
            concordance.Validate();
            Add(ReportSectionIds.Quality, PresentationFigures.DatabaseSearchConcordanceFile, "Database search concordance",
                "Sequence agreement among precursor-qualified one-to-one PSM pairs. Raw scan overlap is not its denominator.", () => new DatabaseSearchConcordanceChart(concordance),
                new("Precursor-qualified database-search pairs", concordance.PrecursorQualifiedDatabaseSearchPairs, "pairs", "0"),
                new("Exact pairs", concordance.ExactPrecursorQualifiedDatabaseSearchPairs, "pairs", "0"));
            if (manifest.DeNovoOverlap is { HasScanLevelSequenceComparison: true } overlap)
            {
                overlap.Validate();
                Add(ReportSectionIds.Quality, PresentationFigures.DeNovoSearchCrosscheckFile, "De novo alternative triage",
                    "Stored overlap and sequence-comparison stages, with scan and precursor denominators kept distinct.", () => new DeNovoSearchCrosscheckChart(overlap, concordance),
                    new CaptionNumber("Filtered de novo rows", overlap.FilteredDeNovoRows, "rows", "0"),
                    new CaptionNumber("Precursor-matched rows", overlap.SamePrecursorWithinTolerance, "rows", "0"));
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
            Add(ReportSectionIds.Sensitivity, "identity_scan.png", "Identity scan",
                "The saved identity scan in the program's identity-window view.", () => new IdentityScanChart(manifest.PresentationIdentitySweep,
                    curation.ClusteringIdentity > 0 ? curation.ClusteringIdentity : null),
                new CaptionNumber("Stored identity thresholds", manifest.PresentationIdentitySweep.Count, "thresholds", "0"));
        }
        else Missing(ReportSectionIds.Sensitivity, "Identity sensitivity");
        if (manifest.DeNovoCallerAgreement is { } agreement)
        {
            agreement.Validate();
            Add(ReportSectionIds.Quality, PresentationFigures.DeNovoAgreementFile, "De novo caller agreement",
                "Stored caller agreement under the chart's source-specific gates and precursor matching rule.", () => new DeNovoAgreementChart(agreement),
                new("Precursor-qualified comparisons", agreement.Total, "comparisons", "0"), new("Identical comparisons", agreement.Identical, "comparisons", "0"));
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
            Add(ReportSectionIds.Quality, PresentationFigures.DeNovoAcceptedScoreScatterFile, "Accepted source scores",
                "Accepted precursor hypotheses on separate source-native axes, labelled by sequence agreement.", () => new DeNovoAcceptedScoreScatterChart(scatter),
                new CaptionNumber("Accepted source-pair comparisons", scatter.Total, "comparisons", "0"));
        }
        else Missing(ReportSectionIds.Quality, "Accepted source score scatter");
        if (manifest.PresentationDeNovoVariants.Count > 0)
        {
            DeNovoVariantComparison.ValidateTalkVariantSet(manifest.PresentationDeNovoVariants);
            Add(ReportSectionIds.Sensitivity, PresentationFigures.DeNovoVariantComparisonFile, "Stored source variants",
                "The source variants actually computed and recorded in this project.", () => new DeNovoVariantComparisonChart(manifest.PresentationDeNovoVariants),
                new CaptionNumber("Stored source variants", manifest.PresentationDeNovoVariants.Count, "variants", "0"));
            var comparison = DeNovoTalkComparison.Build(manifest.PresentationDeNovoVariants);
            Add(ReportSectionIds.Sensitivity, PresentationFigures.DeNovoTalkComparisonFile, "Source effect comparison",
                "The program's comparison of the stored, provenance-matched source variants on one common search baseline.", () => new DeNovoTalkComparisonChart(comparison),
                new CaptionNumber("Stored source variants", manifest.PresentationDeNovoVariants.Count, "variants", "0"));
        }
        else Missing(ReportSectionIds.Sensitivity, "Source variant comparison");
        Missing(ReportSectionIds.Sensitivity, "Source score cutoff sweep", "A score cutoff sweep is not stored in this project.");
        Missing(ReportSectionIds.Quality, "Unfiltered source score scatter", "The underlying source-pair score study is not stored in this project.");
        Missing(ReportSectionIds.Quality, "Source set novelty", "The underlying three-source sequence-set study is not stored in this project.");
        var world = manifest.FamilyWorldResult?.ForLength(manifest.DatabaseGapMinimumTagLength ?? FamilyWorldOptions.DefaultMinimumTagLength);
        if (world is { CompositionRules.Length: > 0 })
        {
            var rows = world.Composition.OrderByDescending(r => r.WorldDeNovoEnvelope).ThenByDescending(r => r.TotalEnvelope)
                .ThenBy(r => r.Family, StringComparer.Ordinal).ToArray();
            Add(ReportSectionIds.DatabaseGap, "database_gap.png", "Database gap",
                "Identified, database-mapped de novo and family-world-only signal are separate components. The family-world-only component is excluded from database family composition.",
                () => new DatabaseGapChart(rows), new("Family rows shown", rows.Length, "families", "0"),
                new("Family-world-only envelope shown", rows.Sum(r => r.WorldDeNovoEnvelope), "area units", "G5"));
            charts[^1] = charts[^1] with { Height = Math.Max(540, 70 + rows.Length * 30) };
        }
        else Missing(ReportSectionIds.DatabaseGap, "Database gap");
        if (world != null)
        {
            var rows = world.Families.OrderBy(r => r.Family, StringComparer.Ordinal).ToArray();
            for (int offset = 0; offset < rows.Length; offset += 12)
            {
                var page = rows.Skip(offset).Take(12).ToArray();
                string suffix = (offset / 12 + 1).ToString(CultureInfo.InvariantCulture);
                Add(ReportSectionIds.Quality, "family_world_rates_" + suffix + ".png", "Family-world rates " + suffix,
                    "Stored decoy signal divided by credited signal, and correct known credits divided by known credits, by family. Missing rates remain unavailable; supporting counts and the evidence floor are printed in the quality table.",
                    () => new ReportFamilyWorldRatesChart(page), new("Family rows on this page", page.Length, "families", "0"),
                    new("Known credited sequences on this page", page.Sum(r => r.KnownCredited), "sequences", "0"));
            }
            if (rows.Length == 0) Missing(ReportSectionIds.Quality, "Family-world error rates", "No per-family rates were computed in this project.");
        }
        else Missing(ReportSectionIds.Quality, "Family-world error rates");
        return new(charts, unavailable);
    }

    /// <summary>The exact files written here are also embedded; no second PNG encoder is used.</summary>
    public static IReadOnlyList<ReportPlacedFigure> Render(ReportFigurePlan plan, string pngFolder,
        IReadOnlyDictionary<string, string>? captions = null)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Report figure rendering requires an STA thread.");
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Directory.CreateDirectory(pngFolder);
            return plan.Charts.Select(chart =>
            {
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
