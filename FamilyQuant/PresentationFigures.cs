using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant
{
    public sealed class ReferenceComponentSummary
    {
        public int AllComponents { get; set; }
        public int PureDecoyComponents { get; set; }
        public int TargetBearingComponents { get; set; }
        public int ContaminantBearingTargetComponents { get; set; }
        public int NonContaminantTargetComponents { get; set; }
        public int MixedTargetDecoyComponents { get; set; }

        public static ReferenceComponentSummary FromCuration(FamilyClusterFile curation)
        {
            if (curation == null) throw new ArgumentNullException(nameof(curation));
            var groups = curation.GroupedFastaItems;
            bool IsDecoy(FastaItemExtended i) => (i.SequenceIdentifier ?? string.Empty)
                .StartsWith(SequenceLabels.DecoyPrefix, StringComparison.Ordinal);
            bool IsContaminant(FastaItemExtended i) => (i.SequenceIdentifier ?? string.Empty)
                .StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.OrdinalIgnoreCase);

            return new ReferenceComponentSummary
            {
                AllComponents = groups.Count,
                PureDecoyComponents = groups.Count(g => g.All(IsDecoy)),
                TargetBearingComponents = groups.Count(g => g.Any(i => !IsDecoy(i))),
                ContaminantBearingTargetComponents = groups.Count(g => g.Any(i => !IsDecoy(i) && IsContaminant(i))),
                NonContaminantTargetComponents = groups.Count(g => g.Any(i => !IsDecoy(i) && !IsContaminant(i))),
                MixedTargetDecoyComponents = groups.Count(g => g.Any(IsDecoy) && g.Any(i => !IsDecoy(i))),
            };
        }
    }

    public sealed class PresentationFigure
    {
        public PresentationFigure(string fileName, FrameworkElement chart)
        {
            FileName = fileName;
            Chart = chart;
        }

        public string FileName { get; }
        public FrameworkElement Chart { get; }
    }

    /// <summary>Independent database-search reproduction after same-file/scan/charge/mz pairing.</summary>
    public sealed class DatabaseSearchConcordanceChart : TalkChartBase
    {
        private readonly IdentificationConcordanceSummary audit;

        public DatabaseSearchConcordanceChart(IdentificationConcordanceSummary audit)
        {
            this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
            this.audit.Validate();
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "Independent database searches reproduce the same sequence",
                "PatternLab versus PEAKS: deterministic one-to-one PSM pairs, same file + scan + charge, precursor m/z within " +
                audit.PrecursorTolerancePpm.ToString("0.###", CultureInfo.InvariantCulture) + " ppm.");
            if (ActualWidth < 600 || ActualHeight < 300
                || audit.PrecursorQualifiedDatabaseSearchPairs <= 0) return;

            double left = 64, top = 134, width = ActualWidth * 0.61, height = 76;
            double agree = audit.PrecursorQualifiedStrictAgreement;
            dc.DrawRoundedRectangle(Palette.Grid, null, new Rect(left, top, width, height), 5, 5);
            dc.DrawRoundedRectangle(Palette.Identified, null, new Rect(left, top, width * agree, height), 5, 5);
            DrawText(dc, N(audit.ExactPrecursorQualifiedDatabaseSearchPairs) +
                " exact PTM-cleaned sequence matches",
                Palette.SegmentLabel, 13, new Point(left + 18, top + 26), true);
            DrawText(dc, N(audit.PrecursorQualifiedDatabaseSearchPairs) +
                " precursor-qualified one-to-one PSM pairs",
                Palette.Ink, 14, new Point(left, top - 34), true);
            DrawText(dc, "PTM annotations removed; I and L remain distinct in the headline.",
                Palette.Muted, 10, new Point(left, top + height + 18));

            double panelTop = 278;
            dc.DrawRoundedRectangle(Palette.Panel, null,
                new Rect(left, panelTop, width, ActualHeight - panelTop - 42), 8, 8);
            DrawText(dc, "NESTED AUDIT", Palette.Mixing, 11, new Point(left + 24, panelTop + 24), true);
            DrawText(dc, N(audit.PrecursorQualifiedDatabaseSearchScanKeys) + " / " +
                N(audit.SharedDatabaseSearchScanKeys), Palette.Ink, 22, new Point(left + 24, panelTop + 54), true);
            DrawText(dc, "shared scans contain at least one precursor-qualified pair",
                Palette.Identified, 12, new Point(left + 24, panelTop + 98), true);
            DrawText(dc, "Raw scan overlap is retained as an audit, not the agreement denominator.", Palette.Ink, 10,
                new Point(left + 24, panelTop + 132), true);
            DrawText(dc, "Selected de novo-source calls are alternative hypotheses and are audited separately.",
                Palette.Muted, 10, new Point(left + 24, panelTop + 158));

            double right = ActualWidth * 0.73;
            dc.DrawRoundedRectangle(Palette.Panel, null,
                new Rect(right, 112, ActualWidth - right - 32, ActualHeight - 154), 8, 8);
            DrawText(dc, P(agree), Palette.Identified, 36, new Point(right + 28, 152), true);
            DrawText(dc, "strict sequence agreement", Palette.Ink, 12, new Point(right + 28, 202), true);
            int strictMismatches = audit.PrecursorQualifiedDatabaseSearchPairs -
                audit.ExactPrecursorQualifiedDatabaseSearchPairs;
            DrawText(dc, N(strictMismatches) + " strict mismatches", Palette.Muted, 10,
                new Point(right + 28, 238));
            DrawText(dc, N(audit.AdditionalIleLeuEquivalentPrecursorQualifiedPairs) +
                " of these are I/L-equivalent", Palette.Muted, 10, new Point(right + 28, 262));
            DrawText(dc, N(audit.PatternLabPsmRows) + " PatternLab PSM rows", Palette.Ink, 10,
                new Point(right + 28, 312));
            DrawText(dc, N(audit.PeaksDatabasePsmRows) + " PEAKS database PSM rows", Palette.Ink, 10,
                new Point(right + 28, 338));
        }
    }

    /// <summary>
    /// The data plots used by the FamilyQuant talk. Every chart is built from the loaded
    /// scientific project's quant rows, manifest and curation; no PowerPoint-only arithmetic exists.
    /// The filenames are deliberately stable and follow the result-slide order in the presentation.
    /// </summary>
    public static class PresentationFigures
    {
        public const string DeNovoScoreDistributionFile = "de_novo_score_distribution.png";
        public const string DeNovoConsolidationFile = "de_novo_consolidation.png";
        public const string PeptideEvidenceFile = "peptide_representatives.png";
        public const string DatabaseSearchConcordanceFile = "database_search_concordance.png";
        public const string DeNovoSearchCrosscheckFile = "de_novo_alternative_triage.png";
        public const string SignalByRunFile = "signal_by_run.png";
        public const string SignalUpliftFile = "signal_uplift.png";
        public const string ReferenceEnvelopeDiagnosticsFile = "reference_envelope_model_diagnostics.png";
        public const string IdentitySweepFile = "identity_sweep.png";
        public const string FamilyContributionFile = "family_contribution.png";
        public const string FamilySignalShareFile = "family_signal_vs_denovo_share.png";
        public const string FamilySourceSequenceFile = "family_source_sequence_counts.png";

        /// <summary>The score-sweep-with-overlap figure. Deliberately NOT part of <see cref="FileNames"/>
        /// (the complete XIC talk pack): the sweep is pure CSV sequence bookkeeping and renders without
        /// any extraction, so it is built and saved on its own through <see cref="SaveScoreSweep"/>.</summary>
        public const string DeNovoScoreSweepFile = "de_novo_score_sweep_overlap.png";

        /// <summary>The PEAKS-vs-DeepNovo per-spectrum agreement bubble figure. It is optional because a
        /// one-source project has no caller comparison. When the result is stored in the manifest,
        /// <see cref="PresentationEvidencePack.Save"/> regenerates it through <see cref="SaveAgreement"/>.</summary>
        public const string DeNovoAgreementFile = "de_novo_peaks_deepnovo_agreement.png";

        /// <summary>The exact source-set comparison for the focused PEAKS de-novo-only + DeepNovo
        /// computation. Optional because a single-source project cannot populate it.</summary>
        public const string DeNovoSourceVennFile = "de_novo_source_venn.png";

        /// <summary>Accepted precursor-hypothesis calls plotted on their two source-native score axes,
        /// coloured and shaped by strict PTM-cleaned sequence equality.</summary>
        public const string DeNovoAcceptedScoreScatterFile =
            "de_novo_accepted_score_agreement.png";

        /// <summary>The score-vs-score dot plot with marginal histograms. Like the sweep and the agreement
        /// bubble, pure CSV sequence bookkeeping, so it is NOT part of <see cref="FileNames"/> and is built
        /// on its own through <see cref="SaveScoreScatter"/>.</summary>
        public const string DeNovoScoreScatterFile = "de_novo_score_vs_score_scatter.png";

        /// <summary>The UpSet-style set-novelty view of the three de novo sources. Pure CSV sequence
        /// bookkeeping, so it is NOT part of <see cref="FileNames"/> and is built on its own through
        /// <see cref="SaveSetNovelty"/>.</summary>
        public const string SetNoveltyFile = "de_novo_set_novelty.png";

        /// <summary>FamilyQuant-native comparison of the fully quantified de novo variants. Its CSV
        /// and Markdown companions are written by <see cref="DeNovoVariantComparison"/>; the evidence
        /// pack regenerates all three when a validated source-variant set is persisted in the project.</summary>
        public const string DeNovoVariantComparisonFile = "de_novo_variant_comparison.png";

        /// <summary>The deliberately compact talk result: PEAKS de-novo-only, DeepNovo and their exact
        /// union, shown on distinct-sequence and assigned-TIC metrics without isotope terminology.</summary>
        public const string DeNovoTalkComparisonFile = "de_novo_source_effect_comparison.png";

        public static IReadOnlyList<string> FileNames { get; } = new[]
        {
            DeNovoScoreDistributionFile, DeNovoConsolidationFile, PeptideEvidenceFile,
            DatabaseSearchConcordanceFile,
            DeNovoSearchCrosscheckFile,
            SignalByRunFile, SignalUpliftFile, IdentitySweepFile,
            FamilyContributionFile, FamilySignalShareFile, FamilySourceSequenceFile,
        };

        public static List<PresentationFigure> Build(AnalyticsData data, QuantManifest manifest,
                                                     FamilyClusterFile curation,
                                                     FamilyEnvelopeQuantResult families)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (curation == null) throw new ArgumentNullException(nameof(curation));
            if (families == null) throw new ArgumentNullException(nameof(families));
            if (manifest.DeNovoOverlap == null)
                throw new InvalidOperationException("This project has no stored de novo/search-engine overlap study. Recompute full XIC with the configured de novo input, or reconcile it in De novo review, step 2.");
            if (!manifest.DeNovoOverlap.HasScanLevelSequenceComparison)
                throw new InvalidOperationException("This project's overlap study predates the literal same-file/same-scan sequence comparison. Recompute full XIC with the configured de novo input before exporting the talk plots.");
            if (manifest.DeNovoScoreDistribution == null)
                throw new InvalidOperationException("This project has no stored de novo source-confidence distribution. Recompute full XIC with the configured de novo input before exporting the talk plots.");
            manifest.DeNovoScoreDistribution.Validate();
            var inputSourceIds = new HashSet<string>((manifest.DeNovoInputs ?? new List<DeNovoInputManifest>())
                .Select(i => i.SourceId).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            var distributionSourceIds = new HashSet<string>((manifest.DeNovoSourceScoreDistributions
                    ?? new List<DeNovoSourceScoreDistribution>())
                .Select(d => d.SourceId).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            if ((inputSourceIds.Count > 1 || distributionSourceIds.Count > 0)
                && !inputSourceIds.SetEquals(distributionSourceIds))
                throw new InvalidOperationException(
                    "The stored source-confidence distributions do not exactly match the configured " +
                    "de novo input sources. Inputs = [" +
                    string.Join(", ", inputSourceIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)) +
                    "]; distributions = [" +
                    string.Join(", ", distributionSourceIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)) +
                    "]. Recompute full XIC; a partial primary-source panel cannot stand in for a " +
                    "multi-source project.");
            if (manifest.DeNovoIdentification == null)
                throw new InvalidOperationException("This project has no stored de novo identification consolidation. Recompute full XIC with the configured de novo input before exporting the talk plots.");
            manifest.DeNovoIdentification.Validate();
            if (manifest.IdentificationConcordance == null)
                throw new InvalidOperationException("This project has no stored PatternLab/PEAKS database-search concordance audit. Recompute full XIC with the complete PEAKS export before exporting the talk plots.");
            manifest.IdentificationConcordance.Validate();
            if (manifest.IdentificationConcordance.SharedDatabaseSearchScanKeys > 0
                && manifest.IdentificationConcordance.PrecursorQualifiedDatabaseSearchScanKeys == 0
                && manifest.IdentificationConcordance.PrecursorQualifiedDatabaseSearchPairs == 0
                && manifest.IdentificationConcordance.PatternLabPrecursorHypothesesInSharedScans == 0
                && manifest.IdentificationConcordance.PeaksDatabasePrecursorHypothesesInSharedScans == 0)
                throw new InvalidOperationException(
                    "This project's PatternLab/PEAKS concordance audit predates precursor-qualified " +
                    "one-to-one pairing. Recompute full XIC before exporting figures; raw shared scans " +
                    "cannot be used as the sequence-agreement denominator.");
            if (manifest.PresentationIdentitySweep == null || manifest.PresentationIdentitySweep.Count == 0)
                throw new InvalidOperationException("This project has no stored identity sweep. Run the identity scan from the USearch screen and save the complete scientific project before exporting the talk plots.");

            var reference = ReferenceComponentSummary.FromCuration(curation);
            int currentIdentity = manifest.PresentationIdentitySweep
                .OrderBy(r => Math.Abs(r.Groups - reference.AllComponents)).ThenByDescending(r => r.Identity)
                .First().Identity;

            return new List<PresentationFigure>
            {
                new PresentationFigure(DeNovoScoreDistributionFile,
                    manifest.DeNovoSourceScoreDistributions != null
                    && manifest.DeNovoSourceScoreDistributions.Count > 0
                        ? new DeNovoScoreDistributionChart(manifest.DeNovoSourceScoreDistributions)
                        : new DeNovoScoreDistributionChart(manifest.DeNovoScoreDistribution)),
                new PresentationFigure(DeNovoConsolidationFile,
                    new DeNovoConsolidationChart(data, manifest)),
                new PresentationFigure(PeptideEvidenceFile,
                    new PeptideRepresentativesChart(data, manifest.DeNovoIdentification)),
                new PresentationFigure(DatabaseSearchConcordanceFile,
                    new DatabaseSearchConcordanceChart(manifest.IdentificationConcordance)),
                new PresentationFigure(DeNovoSearchCrosscheckFile,
                    new DeNovoSearchCrosscheckChart(manifest.DeNovoOverlap, manifest.IdentificationConcordance)),
                new PresentationFigure(SignalByRunFile, new SignalByRunChart(data.Fractions)),
                new PresentationFigure(SignalUpliftFile, new SignalUpliftChart(data.Grouped)),
                new PresentationFigure(IdentitySweepFile,
                    new ReferenceIdentitySweepChart(manifest.PresentationIdentitySweep, currentIdentity)),
                new PresentationFigure(FamilyContributionFile,
                    new FamilyContributionChart(families, topN: 10, asPercentOfTotal: false)),
                new PresentationFigure(FamilySignalShareFile, new FamilySignalShareChart(families, labelTopN: 10)),
                new PresentationFigure(FamilySourceSequenceFile,
                    new FamilySourceSequenceChart(families.SourceSequenceSummary, topN: 8)),
            };
        }

        public static List<string> Save(AnalyticsData data, QuantManifest manifest,
                                        FamilyClusterFile curation, FamilyEnvelopeQuantResult families,
                                        string folder,
                                        ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("An output folder is required.", nameof(folder));
            Directory.CreateDirectory(folder);
            var written = new List<string>();
            foreach (PresentationFigure figure in Build(data, manifest, curation, families))
                written.Add(ChartImageExporter.Save(figure.Chart, Path.Combine(folder, figure.FileName), variant: variant));
            return written;
        }

        /// <summary>Renders and writes the score-sweep-with-overlap figure into <paramref name="folder"/>
        /// and returns the path written. Pure sequence bookkeeping: it needs only the sweep data, no XIC,
        /// no manifest. Must be called on an STA thread, like every other figure Save.</summary>
        public static string SaveScoreSweep(DeNovoScoreSweep sweep, string folder,
                                            ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (sweep == null) throw new ArgumentNullException(nameof(sweep));
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("An output folder is required.", nameof(folder));
            Directory.CreateDirectory(folder);
            return ChartImageExporter.Save(new DeNovoScoreSweepChart(sweep),
                Path.Combine(folder, DeNovoScoreSweepFile), variant: variant);
        }

        /// <summary>Renders and writes the PEAKS-vs-DeepNovo per-spectrum agreement bubble figure into
        /// <paramref name="folder"/> and returns the path written. Pure sequence bookkeeping: it needs
        /// only the study result, no XIC. Must be called on an STA thread, like every other figure Save.</summary>
        public static string SaveAgreement(DeNovoAgreementResult agreement, string folder,
                                           ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (agreement == null) throw new ArgumentNullException(nameof(agreement));
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("An output folder is required.", nameof(folder));
            Directory.CreateDirectory(folder);
            return ChartImageExporter.Save(new DeNovoAgreementChart(agreement),
                Path.Combine(folder, DeNovoAgreementFile), variant: variant);
        }

        /// <summary>Renders the exact sequence-set regions and DeepNovo source-filter funnel from the
        /// stored focused-union study. Must be called on an STA thread.</summary>
        public static string SaveDeNovoSourceVenn(DeNovoSourceVennSummary summary, string folder,
            ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("An output folder is required.", nameof(folder));
            summary.Validate();
            Directory.CreateDirectory(folder);
            return ChartImageExporter.Save(new DeNovoSourceVennChart(summary),
                Path.Combine(folder, DeNovoSourceVennFile), variant: variant);
        }

        /// <summary>Renders the persisted accepted-call score/match scatter at 1920x1080 through the
        /// same projector-palette path as every other talk figure.</summary>
        public static string SaveDeNovoAcceptedScoreScatter(
            DeNovoAcceptedScoreScatter scatter, string folder,
            ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (scatter == null) throw new ArgumentNullException(nameof(scatter));
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("An output folder is required.", nameof(folder));
            scatter.Validate();
            Directory.CreateDirectory(folder);
            return ChartImageExporter.Save(new DeNovoAcceptedScoreScatterChart(scatter),
                Path.Combine(folder, DeNovoAcceptedScoreScatterFile), variant: variant);
        }

        /// <summary>Renders and writes the score-vs-score dot plot with marginal histograms into
        /// <paramref name="folder"/> and returns the path written. Pure sequence bookkeeping: it needs only
        /// the scatter data, no XIC. Must be called on an STA thread, like every other figure Save.</summary>
        public static string SaveScoreScatter(DeNovoScoreScatter scatter, string folder,
                                              ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (scatter == null) throw new ArgumentNullException(nameof(scatter));
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("An output folder is required.", nameof(folder));
            Directory.CreateDirectory(folder);
            return ChartImageExporter.Save(new DeNovoScoreScatterChart(scatter),
                Path.Combine(folder, DeNovoScoreScatterFile), variant: variant);
        }

        /// <summary>Renders and writes the UpSet-style set-novelty view into <paramref name="folder"/> and
        /// returns the path written. Pure sequence bookkeeping: it needs only the set-novelty data, no XIC.
        /// Must be called on an STA thread, like every other figure Save.</summary>
        public static string SaveSetNovelty(SetNovelty set, string folder,
                                            ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("An output folder is required.", nameof(folder));
            Directory.CreateDirectory(folder);
            return ChartImageExporter.Save(new SetNoveltyChart(set),
                Path.Combine(folder, SetNoveltyFile), variant: variant);
        }

        public static string SaveVariantComparison(IReadOnlyList<DeNovoVariantSummary> rows, string folder,
                                                   ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("An output folder is required.", nameof(folder));
            Directory.CreateDirectory(folder);
            return ChartImageExporter.Save(new DeNovoVariantComparisonChart(rows),
                Path.Combine(folder, DeNovoVariantComparisonFile), variant: variant);
        }

        public static string SaveDeNovoTalkComparison(
            IReadOnlyList<DeNovoTalkComparisonRow> rows, string folder,
            ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("An output folder is required.", nameof(folder));
            Directory.CreateDirectory(folder);
            return ChartImageExporter.Save(new DeNovoTalkComparisonChart(rows),
                Path.Combine(folder, DeNovoTalkComparisonFile), variant: variant);
        }

        /// <summary>Uses the diagnostics persisted by the current compute path. A legacy project can be
        /// upgraded deterministically from its stored peptide representatives; no RAW or source CSV is
        /// needed and the evidence pack records the resulting diagnostics.</summary>
        public static ReferenceEnvelopeDiagnostics ResolveReferenceEnvelopeDiagnostics(
            AnalyticsData data, QuantManifest manifest)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (manifest.ReferenceEnvelopeModelDiagnostics != null)
            {
                manifest.ReferenceEnvelopeModelDiagnostics.Validate();
                return manifest.ReferenceEnvelopeModelDiagnostics;
            }

            var peptides = data.Peptides.Select(p => new NativePeptideQuant
            {
                Run = p.Run,
                Side = p.Side,
                Sequence = p.Sequence,
                Charge = p.Charge,
                PrecursorMz = p.PrecursorMz,
                AreaM0 = p.AreaAt(0),
                EnvelopeSum = p.Envelope,
                UnderSampledArea = p.UnderSampledArea,
                UnderSampledIsEstimate = p.UnderSampledIsEstimate,
                AnchorCount = 1,
            }).ToList();
            return ReferenceEnvelopeEstimator.Evaluate(peptides);
        }
    }

    /// <summary>Stable, human-readable family labels used by every presentation surface. The numeric
    /// family id keeps otherwise repeated toxin names unambiguous.</summary>
    public static class FamilyFigureLabel
    {
        public static string Format(int id, string? description, int maximumNameLength = 28)
        {
            string name = (description ?? string.Empty).Trim();
            int os = name.IndexOf(" OS=", StringComparison.OrdinalIgnoreCase);
            if (os > 0) name = name.Substring(0, os);
            if (maximumNameLength > 1 && name.Length > maximumNameLength)
                name = name.Substring(0, maximumNameLength - 1) + "…";
            string prefix = "F" + id.ToString(CultureInfo.CurrentCulture);
            return name.Length == 0 ? prefix : prefix + " · " + name;
        }
    }

    public abstract class TalkChartBase : FrameworkElement, IPalettedChart
    {
        private ChartPalette palette = ChartPalette.Dark;

        public ChartPalette Palette
        {
            get => palette;
            set { palette = value ?? ChartPalette.Dark; InvalidateVisual(); }
        }

        // The four talk-deck series colours, fixed to the exact hex the design mockups use, so a figure
        // built here matches the mockups regardless of which palette variant its structure is painted in:
        // PEAKS / shared deep blue, DeepNovo / new deep green, context grey, accent amber.
        protected static readonly Brush DeepBlue = FreezeRgb(0x1F, 0x4E, 0x8C);
        protected static readonly Brush DeepGreen = FreezeRgb(0x1F, 0x8F, 0x5A);
        protected static readonly Brush ContextGray = FreezeRgb(0x6B, 0x72, 0x80);
        protected static readonly Brush AccentAmber = FreezeRgb(0xB4, 0x74, 0x1F);

        private static Brush FreezeRgb(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>A translucent clone of a solid brush, for overplotting. Falls back to the brush as-is
        /// when it is not a solid colour.</summary>
        protected static Brush Translucent(Brush brush, double opacity)
        {
            if (brush is SolidColorBrush s)
            {
                var clone = new SolidColorBrush(s.Color) { Opacity = opacity };
                clone.Freeze();
                return clone;
            }
            return brush;
        }

        protected static FormattedText Text(string s, Brush brush, double size, bool bold = false) =>
            new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                    bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, brush, 96);

        protected static void DrawText(DrawingContext dc, string value, Brush brush, double size,
                                       Point at, bool bold = false)
            => dc.DrawText(Text(value, brush, size, bold), at);

        protected void Ground(DrawingContext dc, string heading, string subheading)
        {
            dc.DrawRectangle(Palette.Backdrop, null, new Rect(0, 0, ActualWidth, ActualHeight));
            DrawText(dc, heading, Palette.Ink, 18, new Point(24, 18), true);
            DrawText(dc, subheading, Palette.Muted, 11, new Point(24, 48));
        }

        protected void Bar(DrawingContext dc, Rect rect, Brush fill, string label, bool inside = true)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            dc.DrawRoundedRectangle(fill, new Pen(Palette.Grid, 0.7), rect, 2, 2);
            FormattedText measure = Text(label, Palette.Ink, 10, true);
            bool fitsInside = inside && rect.Width >= measure.Width + 12;
            FormattedText t = Text(label, fitsInside ? Palette.SegmentLabel : Palette.Ink, 10, true);
            double x = fitsInside ? rect.Right - t.Width - 6 : rect.Right + 6;
            dc.DrawText(t, new Point(x, rect.Top + Math.Max(1, (rect.Height - t.Height) / 2)));
        }

        protected static string N(int v) => v.ToString("N0", CultureInfo.CurrentCulture);
        protected static string P(double fraction) => (fraction * 100).ToString("F1", CultureInfo.CurrentCulture) + "%";
    }

    /// <summary>
    /// One translucent point per compatible precursor-hypothesis row where both callers passed their
    /// configured filters. Blue circles encode exact PTM-cleaned sequence matches; red diamonds encode
    /// mismatches, so the result remains interpretable without colour. No diagonal is drawn because ALC
    /// and SCORE are different, uncalibrated confidence scales.
    /// </summary>
    public sealed class DeNovoAcceptedScoreScatterChart : TalkChartBase
    {
        private readonly DeNovoAcceptedScoreScatter scatter;
        // Unmistakable, projector-safe scientific red requested for mismatches: #C43B45.
        private static readonly Brush Mismatch = MakeBrush(0xC4, 0x3B, 0x45);

        public DeNovoAcceptedScoreScatterChart(DeNovoAcceptedScoreScatter scatter)
        {
            this.scatter = scatter ?? throw new ArgumentNullException(nameof(scatter));
            this.scatter.Validate();
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "When both callers pass, do they report the same peptide?",
                "One dot per compatible file + scan + charge/m/z precursor hypothesis; both source calls passed all configured filters.");
            if (ActualWidth < 700 || ActualHeight < 360 || scatter.Total == 0) return;

            // Extents are source-specific because ALC and SCORE are not a shared scale. Include each
            // configured cutoff when it is available, then round the observed extent to readable ticks.
            // The current 80+ talk data still resolves to the familiar 80-100 axes, while a different
            // valid cutoff or score range cannot be clipped or rejected by the renderer.
            ScoreAxis xAxis = ScoreAxis.Create(scatter.Points.Select(p => p.PeaksAlc),
                scatter.PeaksMinimumAcceptedScore);
            ScoreAxis yAxis = ScoreAxis.Create(scatter.Points.Select(p => p.DeepNovoScore),
                scatter.DeepNovoMinimumAcceptedScore);

            Rect plot = new Rect(92, 116, ActualWidth - 92 - 252, ActualHeight - 116 - 94);
            double MapX(double value) => plot.Left + plot.Width * xAxis.Fraction(value);
            double MapY(double value) => plot.Bottom - plot.Height * yAxis.Fraction(value);

            var gridPen = new Pen(Palette.Grid, 0.8);
            var axisPen = new Pen(Palette.Axis, 1.2);
            foreach (double value in xAxis.Ticks())
            {
                double x = MapX(value);
                dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                FormattedText xt = Text(ScoreAxis.Label(value),
                    Palette.Muted, 9);
                dc.DrawText(xt, new Point(x - xt.Width / 2, plot.Bottom + 7));
            }
            foreach (double value in yAxis.Ticks())
            {
                double y = MapY(value);
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                FormattedText yt = Text(ScoreAxis.Label(value),
                    Palette.Muted, 9);
                dc.DrawText(yt, new Point(plot.Left - yt.Width - 10, y - yt.Height / 2));
            }
            dc.DrawLine(axisPen, plot.BottomLeft, plot.TopLeft);
            dc.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);

            // An interior cutoff gets an explicit dashed reference. When the cutoff is the lower axis
            // boundary (as in the current 80+ talk result), the boundary itself is the reference and no
            // extra ink is added, preserving the established figure.
            DrawVerticalCutoff(dc, plot, xAxis, scatter.PeaksMinimumAcceptedScore, MapX);
            DrawHorizontalCutoff(dc, plot, yAxis, scatter.DeepNovoMinimumAcceptedScore, MapY);

            // Semi-transparent fills expose repeated scores; opaque outlines and different shapes keep
            // the categories visible without relying on hue. Draw mismatches first, then matches, so the
            // blue agreement population remains legible over dense red coordinates.
            Brush matchFill = Translucent(DeepBlue, 0.35);
            Brush mismatchFill = Translucent(Mismatch, 0.42);
            var matchPen = new Pen(DeepBlue, 0.75);
            var mismatchPen = new Pen(Mismatch, 0.9);
            foreach (DeNovoAcceptedScorePoint p in scatter.Points.Where(p => !p.StrictSequenceMatch))
                DrawDiamond(dc, new Point(MapX(p.PeaksAlc), MapY(p.DeepNovoScore)),
                    3.4, mismatchFill, mismatchPen);
            foreach (DeNovoAcceptedScorePoint p in scatter.Points.Where(p => p.StrictSequenceMatch))
                dc.DrawEllipse(matchFill, matchPen,
                    new Point(MapX(p.PeaksAlc), MapY(p.DeepNovoScore)), 2.8, 2.8);

            FormattedText xLabel = Text("PEAKS " + scatter.PeaksScoreField +
                " · accepted calls only", Palette.Muted, 11);
            dc.DrawText(xLabel, new Point(plot.Left + (plot.Width - xLabel.Width) / 2,
                plot.Bottom + 31));
            DrawVerticalText(dc, "DeepNovo " + scatter.DeepNovoScoreField +
                " · accepted calls only", Palette.Muted, 11, new Point(plot.Left - 56, plot.Bottom));

            double panelX = plot.Right + 24;
            double panelW = ActualWidth - panelX - 24;
            dc.DrawRoundedRectangle(Palette.Panel, new Pen(Palette.Grid, 0.8),
                new Rect(panelX, plot.Top, panelW, plot.Height), 8, 8);
            DrawText(dc, "JOINTLY ACCEPTED", Palette.Mixing, 9,
                new Point(panelX + 18, plot.Top + 18), true);
            DrawText(dc, N(scatter.Total), Palette.Ink, 28,
                new Point(panelX + 18, plot.Top + 40), true);
            DrawText(dc, "precursor-hypothesis rows", Palette.Muted, 9.5,
                new Point(panelX + 18, plot.Top + 76));

            double legendY = plot.Top + 116;
            dc.DrawEllipse(Translucent(DeepBlue, 0.35), new Pen(DeepBlue, 1),
                new Point(panelX + 27, legendY), 6, 6);
            DrawText(dc, "Exact match", Palette.Ink, 10.5,
                new Point(panelX + 42, legendY - 8), true);
            DrawText(dc, N(scatter.Matches) + " · " + P(scatter.MatchFraction), DeepBlue, 10.5,
                new Point(panelX + 42, legendY + 8), true);

            double mismatchY = legendY + 58;
            DrawDiamond(dc, new Point(panelX + 27, mismatchY), 7,
                Translucent(Mismatch, 0.42), new Pen(Mismatch, 1.1));
            DrawText(dc, "Sequence mismatch", Palette.Ink, 10.5,
                new Point(panelX + 42, mismatchY - 8), true);
            DrawText(dc, N(scatter.Mismatches) + " · " + P(1.0 - scatter.MatchFraction),
                Mismatch, 10.5, new Point(panelX + 42, mismatchY + 8), true);

            dc.DrawLine(new Pen(Palette.Grid, 0.8),
                new Point(panelX + 18, mismatchY + 40),
                new Point(panelX + panelW - 18, mismatchY + 40));
            DrawText(dc, "MATCH RULE", Palette.Mixing, 8.5,
                new Point(panelX + 18, mismatchY + 58), true);
            DrawText(dc, "PTM notation removed", Palette.Ink, 9.2,
                new Point(panelX + 18, mismatchY + 78));
            DrawText(dc, "I/L kept distinct", Palette.Ink, 9.2,
                new Point(panelX + 18, mismatchY + 95));

            double footer = plot.Bottom + 62;
            DrawText(dc,
                "Inclusion: both Accepted = true after source score + peptide length + local-confidence stretch filters.",
                Palette.Ink, 8.8, new Point(42, footer), true);
            DrawText(dc,
                "Important: ALC and SCORE are different source-native scales; the plot compares calls and does not treat equal numeric scores as equivalent probabilities.",
                Palette.Muted, 8.5, new Point(42, footer + 18));
        }

        private void DrawVerticalCutoff(DrawingContext dc, Rect plot, ScoreAxis axis,
            double? cutoff, Func<double, double> map)
        {
            if (!cutoff.HasValue || !axis.IsInterior(cutoff.Value)) return;
            double x = map(cutoff.Value);
            var pen = new Pen(Palette.Mixing, 1.1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            DrawText(dc, "cutoff " + ScoreAxis.Label(cutoff.Value), Palette.Mixing, 8.5,
                new Point(Math.Min(x + 4, plot.Right - 64), plot.Top + 5), true);
        }

        private void DrawHorizontalCutoff(DrawingContext dc, Rect plot, ScoreAxis axis,
            double? cutoff, Func<double, double> map)
        {
            if (!cutoff.HasValue || !axis.IsInterior(cutoff.Value)) return;
            double y = map(cutoff.Value);
            var pen = new Pen(Palette.Mixing, 1.1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, "cutoff " + ScoreAxis.Label(cutoff.Value), Palette.Mixing, 8.5,
                new Point(plot.Left + 5, Math.Max(plot.Top + 4, y - 18)), true);
        }

        /// <summary>A readable, source-local score axis built from accepted observations and the source's
        /// configured cutoff. The two callers intentionally receive independent axes because their score
        /// scales are not calibrated against one another.</summary>
        private sealed class ScoreAxis
        {
            private ScoreAxis(double minimum, double maximum, double tickStep)
            {
                Minimum = minimum;
                Maximum = maximum;
                TickStep = tickStep;
            }

            public double Minimum { get; }
            public double Maximum { get; }
            public double TickStep { get; }

            public static ScoreAxis Create(IEnumerable<double> values, double? cutoff)
            {
                var extent = values.Where(double.IsFinite).ToList();
                if (cutoff.HasValue && double.IsFinite(cutoff.Value)) extent.Add(cutoff.Value);
                if (extent.Count == 0)
                    throw new InvalidOperationException(
                        "An accepted-score axis requires at least one finite score or cutoff.");

                double rawMinimum = extent.Min();
                double rawMaximum = extent.Max();
                if (rawMaximum <= rawMinimum)
                {
                    double halfSpan = Math.Max(0.5, Math.Abs(rawMinimum) * 0.05);
                    rawMinimum -= halfSpan;
                    rawMaximum += halfSpan;
                }

                double step = NiceStep((rawMaximum - rawMinimum) / 4.0);
                double minimum = Math.Floor(rawMinimum / step) * step;
                double maximum = Math.Ceiling(rawMaximum / step) * step;
                if (!(maximum > minimum)) maximum = minimum + step;
                return new ScoreAxis(minimum, maximum, step);
            }

            public double Fraction(double value) => (value - Minimum) / (Maximum - Minimum);

            public IEnumerable<double> Ticks()
            {
                // Axis construction normally yields five or fewer intervals. The cap also protects the
                // renderer from floating-point pathologies if an extreme source-native scale is loaded.
                for (int i = 0; i <= 100; i++)
                {
                    double value = Minimum + i * TickStep;
                    if (value > Maximum + TickStep * 1e-9) yield break;
                    yield return Math.Abs(value) < TickStep * 1e-12 ? 0 : value;
                }
            }

            public bool IsInterior(double value)
            {
                double epsilon = Math.Max(1.0, Maximum - Minimum) * 1e-9;
                return value > Minimum + epsilon && value < Maximum - epsilon;
            }

            public static string Label(double value) =>
                value.ToString("0.###", CultureInfo.InvariantCulture);

            private static double NiceStep(double rawStep)
            {
                if (!(rawStep > 0) || !double.IsFinite(rawStep)) return 1;
                double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
                double fraction = rawStep / magnitude;
                double niceFraction = fraction <= 1 ? 1 : fraction <= 2 ? 2
                    : fraction <= 5 ? 5 : 10;
                double step = niceFraction * magnitude;
                return step > 0 && double.IsFinite(step) ? step : 1;
            }
        }

        private static Brush MakeBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static void DrawDiamond(DrawingContext dc, Point center, double radius,
            Brush fill, Pen pen)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext g = geometry.Open())
            {
                g.BeginFigure(new Point(center.X, center.Y - radius), true, true);
                g.LineTo(new Point(center.X + radius, center.Y), true, false);
                g.LineTo(new Point(center.X, center.Y + radius), true, false);
                g.LineTo(new Point(center.X - radius, center.Y), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(fill, pen, geometry);
        }

        private void DrawVerticalText(DrawingContext dc, string value, Brush brush,
            double size, Point at)
        {
            FormattedText label = Text(value, brush, size);
            dc.PushTransform(new RotateTransform(-90, at.X, at.Y));
            dc.DrawText(label, at);
            dc.Pop();
        }
    }

    /// <summary>
    /// Exact sequence-set comparison for the focused PEAKS de-novo-only + DeepNovo computation.
    /// Panel B reads source claims attached to carried union rows; it is not a subtraction of separately
    /// quantified variants. The source funnel explains why DeepNovo starts with fewer imported calls.
    /// </summary>
    public sealed class DeNovoSourceVennChart : TalkChartBase
    {
        private readonly DeNovoSourceVennSummary summary;

        public DeNovoSourceVennChart(DeNovoSourceVennSummary summary)
        {
            this.summary = summary ?? throw new ArgumentNullException(nameof(summary));
            this.summary.Validate();
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "PEAKS and DeepNovo contribute mostly distinct peptide sequences",
                "Exact directly observed sets from the same only+DeepNovo computation; each panel states when its sequence set is counted.");
            if (ActualWidth < 700 || ActualHeight < 360) return;

            double margin = 34;
            double gap = 34;
            double panelWidth = (ActualWidth - margin * 2 - gap) / 2.0;
            Rect left = new Rect(margin, 86, panelWidth, 302);
            Rect right = new Rect(margin + panelWidth + gap, 86, panelWidth, 302);
            DrawVenn(dc, left, summary.FilteredBeforeUnion,
                "A · FILTERED CALLS", "Before precursor union");
            DrawVenn(dc, right, summary.CarriedForwardClaimProvenance,
                "B · CARRIED FORWARD", "Source claims on retained union rows");

            dc.DrawLine(new Pen(Palette.Grid, 1.0),
                new Point(margin, 408), new Point(ActualWidth - margin, 408));
            DeNovoSourceFilterFunnel f = summary.DeepNovoFunnel;
            DrawText(dc, "WHY DEEPNOVO STARTS SMALLER", DeepGreen, 9.5,
                new Point(margin, 426), true);
            string[] counts =
            {
                N(f.RawRows) + " raw",
                N(f.ScorePassingRows) + " " + f.ConfidenceField + " ≥ " +
                    f.MinimumConfidence.ToString("0.###", CultureInfo.CurrentCulture),
                N(f.ScoreAndLengthPassingRows) + " + length ≥ " +
                    f.MinLength.ToString(CultureInfo.CurrentCulture),
                N(f.AllFiltersPassingRows) + " + local stretch",
            };
            double x = margin;
            for (int i = 0; i < counts.Length; i++)
            {
                DrawText(dc, counts[i], i == counts.Length - 1 ? DeepGreen : Palette.Ink,
                    10.5, new Point(x, 451), true);
                FormattedText m = Text(counts[i], Palette.Ink, 10.5, true);
                x += m.Width + 20;
                if (i < counts.Length - 1)
                {
                    DrawText(dc, "→", Palette.Axis, 13, new Point(x - 13, 448), true);
                    x += 8;
                }
            }
            DrawText(dc, "Local stretch = at least " + f.StretchLength.ToString(CultureInfo.CurrentCulture) +
                " consecutive residues at ≥" + f.StretchFloor.ToString(CultureInfo.CurrentCulture) +
                "% local confidence. Sequence sets remove PTM notation, pool runs and charge, and keep I/L distinct.",
                Palette.Muted, 8.1, new Point(margin, 485));
            DrawText(dc, "Panel B uses provenance from this exact union; it is not inferred from separately run source totals.",
                Palette.Muted, 8.1, new Point(margin, 505));
        }

        private void DrawVenn(DrawingContext dc, Rect panel, DeNovoSequenceSetRegions regions,
            string kicker, string subtitle)
        {
            dc.DrawRoundedRectangle(Palette.Panel, new Pen(Palette.Grid, 0.8), panel, 8, 8);
            DrawText(dc, kicker, Palette.Mixing, 10, new Point(panel.Left + 18, panel.Top + 16), true);
            DrawText(dc, subtitle, Palette.Ink, 10.5, new Point(panel.Left + 18, panel.Top + 37), true);
            DrawText(dc, "Circle areas approximate caller totals; overlap lens is schematic.",
                Palette.Muted, 7.3, new Point(panel.Left + 18, panel.Top + 57));

            double maxRadius = Math.Min(82, panel.Width * 0.215);
            int maxTotal = Math.Max(1, Math.Max(regions.PeaksTotal, regions.DeepNovoTotal));
            // Circle AREA, not radius, follows each caller's total sequence count. A lower visual bound
            // keeps the smaller caller and its labels legible on a projected slide. The lens position is
            // schematic because one pair of circles cannot simultaneously encode three arbitrary exact
            // region counts; the printed values remain authoritative.
            double peaksRadius = Math.Max(43,
                maxRadius * Math.Sqrt((double)regions.PeaksTotal / maxTotal));
            double deepRadius = Math.Max(43,
                maxRadius * Math.Sqrt((double)regions.DeepNovoTotal / maxTotal));
            double cy = panel.Top + 172;
            double center = panel.Left + panel.Width / 2.0;
            double visibleLens = Math.Min(34, Math.Min(peaksRadius, deepRadius) * 0.62);
            double centerDistance = peaksRadius + deepRadius - visibleLens;
            Point peaksCenter = new Point(center - centerDistance / 2.0, cy);
            Point deepCenter = new Point(center + centerDistance / 2.0, cy);
            var outlinePeaks = new Pen(DeepBlue, 2.0);
            var outlineDeep = new Pen(DeepGreen, 2.0);
            dc.DrawEllipse(Translucent(DeepBlue, 0.17), outlinePeaks,
                peaksCenter, peaksRadius, peaksRadius);
            dc.DrawEllipse(Translucent(DeepGreen, 0.17), outlineDeep,
                deepCenter, deepRadius, deepRadius);

            FormattedText peaksName = Text("PEAKS de-novo-only", DeepBlue, 8.6, true);
            FormattedText deepName = Text("DeepNovo", DeepGreen, 8.6, true);
            dc.DrawText(peaksName, new Point(peaksCenter.X - peaksName.Width / 2,
                panel.Top + 75));
            dc.DrawText(deepName, new Point(deepCenter.X - deepName.Width / 2,
                panel.Top + 75));

            DrawCount(dc, regions.PeaksExclusive, "exclusive", DeepBlue,
                new Point(peaksCenter.X - peaksRadius * 0.38, cy - 24), 17);
            double sharedCenterX = ((deepCenter.X - deepRadius) +
                (peaksCenter.X + peaksRadius)) / 2.0;
            DrawCount(dc, regions.Shared, "shared", AccentAmber,
                new Point(sharedCenterX, cy + 12), 15);
            DrawCount(dc, regions.DeepNovoExclusive, "exclusive", DeepGreen,
                new Point(deepCenter.X + deepRadius * 0.34, cy - 24), 17);

            string totals = "PEAKS total " + N(regions.PeaksTotal) + "   ·   DeepNovo total " +
                N(regions.DeepNovoTotal) + "   ·   union " + N(regions.Union);
            FormattedText totalText = Text(totals, Palette.Ink, 8.5, true);
            dc.DrawText(totalText, new Point(panel.Left + (panel.Width - totalText.Width) / 2,
                panel.Bottom - 40));
            string scope = regions.Scope == "pooled" ? "pooled distinct sequences" : regions.Scope;
            FormattedText scopeText = Text(scope, Palette.Muted, 8.0);
            dc.DrawText(scopeText, new Point(panel.Left + (panel.Width - scopeText.Width) / 2,
                panel.Bottom - 22));
        }

        private static void DrawCount(DrawingContext dc, int count, string label, Brush brush,
            Point center, double numberSize)
        {
            FormattedText number = Text(N(count), brush, numberSize, true);
            FormattedText caption = Text(label, brush, 8.3, true);
            dc.DrawText(number, new Point(center.X - number.Width / 2, center.Y));
            dc.DrawText(caption, new Point(center.X - caption.Width / 2,
                center.Y + number.Height + 1));
        }
    }

    /// <summary>The complete source-to-credit bookkeeping requested for the talk. The four cards use
    /// different counting grains, stated on the figure, so this is not misread as one monotone funnel.</summary>
    public sealed class DeNovoConsolidationChart : TalkChartBase
    {
        private readonly AnalyticsData data;
        private readonly QuantManifest manifest;
        private readonly DeNovoIdentificationSummary identification;

        public DeNovoConsolidationChart(AnalyticsData data, QuantManifest manifest)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            identification = manifest.DeNovoIdentification
                ?? throw new InvalidOperationException("The project has no de novo consolidation audit.");
            identification.Validate();
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "How imported de novo calls become credited representatives",
                "Every stage is project-native. Count grain changes are explicit; these four numbers are not one common-denominator funnel.");
            if (ActualWidth < 650 || ActualHeight < 330) return;

            int rawImported = QualityChartData.ImportedCalls(manifest);
            int credited = QualityChartData.CreditedFeatures(data);
            var stages = new[]
            {
                (Count: rawImported, Kicker: "1 · IMPORTED", Title: "raw spectrum calls",
                    Detail1: "sum of rows in recorded", Detail2: "source files", Fill: Palette.Tic),
                (Count: identification.FilteredSpectrumCalls, Kicker: "2 · ACCEPTED", Title: "calls after filters + union",
                    Detail1: "source score + length + local stretch", Detail2: "then one winner per precursor", Fill: Palette.Identified),
                (Count: identification.FilteredDistinctSequencesIgnoringPtm, Kicker: "3 · CONSOLIDATED", Title: "pooled distinct sequences",
                    Detail1: "PTM notation removed; runs + charge pooled", Detail2: "I/L remains distinct", Fill: Palette.Mixing),
                (Count: credited, Kicker: "4 · CREDITED", Title: "de novo representatives",
                    Detail1: "search reconciliation + XIC + dedup", Detail2: "cross-side search overlap suppressed", Fill: Palette.DeNovo),
            };

            double left = 38, gap = 22, top = 132;
            double cardW = (ActualWidth - left * 2 - gap * 3) / 4.0;
            const double cardH = 204;
            for (int i = 0; i < stages.Length; i++)
            {
                var stage = stages[i];
                double x = left + i * (cardW + gap);
                dc.DrawRoundedRectangle(Palette.Panel, new Pen(Palette.Grid, 0.8),
                    new Rect(x, top, cardW, cardH), 8, 8);
                dc.DrawRectangle(stage.Fill, null, new Rect(x, top, cardW, 8));
                DrawText(dc, stage.Kicker, stage.Fill, 9.5, new Point(x + 14, top + 24), true);
                DrawText(dc, N(stage.Count), Palette.Ink, 25, new Point(x + 14, top + 55), true);
                DrawText(dc, stage.Title, Palette.Ink, 10.5, new Point(x + 14, top + 98), true);
                DrawText(dc, stage.Detail1, Palette.Muted, 8.2, new Point(x + 14, top + 132));
                DrawText(dc, stage.Detail2, Palette.Muted, 8.2, new Point(x + 14, top + 151));
                if (i < stages.Length - 1)
                {
                    double ax = x + cardW + 3;
                    double ay = top + cardH / 2;
                    dc.DrawLine(new Pen(Palette.Axis, 1.8), new Point(ax, ay), new Point(ax + 15, ay));
                    dc.DrawLine(new Pen(Palette.Axis, 1.8), new Point(ax + 15, ay), new Point(ax + 9, ay - 5));
                    dc.DrawLine(new Pen(Palette.Axis, 1.8), new Point(ax + 15, ay), new Point(ax + 9, ay + 5));
                }
            }

            List<string> sourceRules = manifest.DeNovoInputs == null || manifest.DeNovoInputs.Count == 0
                ? new List<string> { "Legacy project: raw import count comes from the stored primary score distribution." }
                : manifest.DeNovoInputs
                    .GroupBy(i => new { i.SourceId, i.SourceDisplayName, i.ConfidenceField, i.MinimumConfidence })
                    .OrderBy(g => g.Min(i => i.SourcePriority))
                    .Select(g => g.Key.SourceDisplayName + " · " + g.Key.ConfidenceField + " >= " +
                        g.Key.MinimumConfidence.ToString("0.###", CultureInfo.CurrentCulture)).ToList();
            DrawText(dc, "RECORDED SOURCE RULES", Palette.Mixing, 8.5, new Point(left, 366), true);
            for (int i = 0; i < sourceRules.Count && i < 3; i++)
                DrawText(dc, sourceRules[i], Palette.Ink, 8.5, new Point(left, 386 + 18 * i), i == 0);
            double notesTop = 392 + Math.Min(3, sourceRules.Count) * 18;
            DrawText(dc, "Why stage 4 can exceed stage 3: stage 3 pools technical runs and charge states; stage 4 counts separately credited run × peptidoform × charge × mass-cluster representatives.",
                Palette.Muted, 8.3, new Point(left, notesTop));
            DrawText(dc, "Credited means an additional de novo hypothesis with admitted MS1 signal after the production search-overlap suppression rule; it is not a count of unique peptide backbones.",
                Palette.Muted, 8.3, new Point(left, notesTop + 22));
        }
    }

    /// <summary>
    /// The bridge between accepted spectrum calls, identification-only sequence consolidation and
    /// post-XIC peptide representatives. The lanes are explicitly separated because their counting grains
    /// answer different questions and must not be read as one nested XIC funnel.
    /// </summary>
    public sealed class PeptideRepresentativesChart : TalkChartBase
    {
        private readonly AnalyticsData data;
        private readonly DeNovoIdentificationSummary identification;

        public PeptideRepresentativesChart(AnalyticsData data, DeNovoIdentificationSummary identification)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.identification = identification ?? throw new ArgumentNullException(nameof(identification));
            this.identification.Validate();
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "Accepted de novo calls collapse to distinct peptide sequences",
                "Identification diversity is counted without XIC; credited representatives use a separate run × peptidoform × charge × precursor-mass grain.");
            if (ActualWidth < 600 || ActualHeight < 300) return;

            var runs = data.Peptides.Select(p => p.Run).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
            List<int> deNovoByRun = runs.Select(r => data.Fractions.FirstOrDefault(f =>
                !f.IsGrouped && string.Equals(f.Run, r, StringComparison.OrdinalIgnoreCase))
                ?.DeNovoPeptides ?? 0).ToList();

            DrawText(dc, "IDENTIFICATION ONLY · NO XIC", Palette.Identified, 11,
                new Point(54, 92), true);
            double y = 126, h = 92, gap = 34, boxW = (ActualWidth - 108 - 2 * gap) / 3.0;
            var boxes = new[]
            {
                (N(identification.FilteredSpectrumCalls), "accepted spectrum calls", Palette.Tic),
                (N(identification.CarriedForwardSpectrumCalls), "additional calls after search reconciliation", Palette.Mixing),
                (N(identification.CarriedForwardDistinctSequencesIgnoringPtm), "distinct PTM-cleaned sequences pooled across runs and charge", Palette.DeNovo),
            };
            for (int i = 0; i < boxes.Length; i++)
            {
                double x = 54 + i * (boxW + gap);
                dc.DrawRoundedRectangle(Palette.Panel, new Pen(Palette.Grid, 0.8), new Rect(x, y, boxW, h), 7, 7);
                DrawText(dc, boxes[i].Item1, boxes[i].Item3, 25, new Point(x + 18, y + 14), true);
                DrawText(dc, boxes[i].Item2, Palette.Ink, 10, new Point(x + 18, y + 54));
                if (i < boxes.Length - 1)
                {
                    double ax = x + boxW + 7;
                    dc.DrawLine(new Pen(Palette.Axis, 2), new Point(ax, y + h / 2), new Point(ax + 18, y + h / 2));
                    dc.DrawLine(new Pen(Palette.Axis, 2), new Point(ax + 18, y + h / 2), new Point(ax + 12, y + h / 2 - 5));
                    dc.DrawLine(new Pen(Palette.Axis, 2), new Point(ax + 18, y + h / 2), new Point(ax + 12, y + h / 2 + 5));
                }
            }
            DrawText(dc, "The pooled sequence count removes repeated spectra, charge states and technical-run repeats; I and L remain distinct.",
                Palette.Muted, 9.5, new Point(54, 230));

            DrawText(dc, "QUANTITATION · A DIFFERENT COUNTING GRAIN", Palette.Mixing, 11,
                new Point(54, 280), true);
            Rect quant = new Rect(54, 314, ActualWidth - 108, 116);
            dc.DrawRoundedRectangle(Palette.Panel, new Pen(Palette.Grid, 0.8), quant, 7, 7);
            int creditedDeNovo = data.Grouped?.DeNovoPeptides ?? deNovoByRun.Sum();
            DrawText(dc, N(creditedDeNovo), Palette.DeNovo, 27, new Point(quant.Left + 22, quant.Top + 18), true);
            DrawText(dc, "credited de novo representatives", Palette.Ink, 11,
                new Point(quant.Left + 160, quant.Top + 25), true);
            string runText = string.Join("  +  ", deNovoByRun.Select((v, i) =>
                "Run " + (i + 1).ToString(CultureInfo.CurrentCulture) + " " + N(v)));
            DrawText(dc, runText + "  =  " + N(creditedDeNovo), Palette.Ink, 11,
                new Point(quant.Left + 22, quant.Top + 67), true);
            DrawText(dc, "Keeps run, canonical peptidoform and charge separate; the cross-side join is isotope-error-aware (M, M+-1, M+-2) and search-overlapping de novo representatives are suppressed.",
                Palette.Muted, 9.5, new Point(quant.Left + 22, quant.Top + 92));
        }
    }

    /// <summary>
    /// The filtered-row cross-check used on the reconciliation slide. Sequence comparison is restricted
    /// to the same normalized file, MS2 scan, reported charge and an isotope-error-aware 20-ppm
    /// precursor-m/z window (M, M+-1, M+-2 of one precursor are treated as the same precursor).
    /// </summary>
    public sealed class DeNovoSearchCrosscheckChart : TalkChartBase
    {
        private readonly DeNovoOverlapSummary overlap;
        private readonly IdentificationConcordanceSummary concordance;

        public DeNovoSearchCrosscheckChart(DeNovoOverlapSummary overlap,
            IdentificationConcordanceSummary concordance)
        {
            this.overlap = overlap ?? throw new ArgumentNullException(nameof(overlap));
            this.concordance = concordance ?? throw new ArgumentNullException(nameof(concordance));
            this.overlap.Validate();
            this.concordance.Validate();
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, overlap.IsStrictDeNovoOnlyScope
                    ? "De-novo-only calls are alternative hypotheses, not failed database searches"
                    : "Selected de novo calls are precursor-triaged before any sequence comparison",
                "Sources: " + overlap.SourceScopeLabel + ". Triage requires the same file, MS2 scan, reported charge, and precursor m/z.");
            if (ActualWidth < 600 || ActualHeight < 300 || overlap.FilteredDeNovoRows <= 0) return;

            DrawText(dc, "All " + N(overlap.FilteredDeNovoRows) + " filtered de novo calls", Palette.Ink, 12,
                new Point(54, 92), true);
            double left = 54, top = 124, width = ActualWidth - 108, height = 58;
            double noScanW = width * overlap.NoSearchPsmInFileAndScan / overlap.FilteredDeNovoRows;
            double differentChargeW = width * overlap.DifferentChargeInSharedScan / overlap.FilteredDeNovoRows;
            double differentMzW = width * overlap.SameChargeButNoMzMatch / overlap.FilteredDeNovoRows;
            double similarMzW = Math.Max(1, width - noScanW - differentChargeW - differentMzW);
            dc.DrawRectangle(Palette.Identified, null, new Rect(left, top, noScanW, height));
            dc.DrawRectangle(Palette.Tic, null, new Rect(left + noScanW, top, differentChargeW, height));
            dc.DrawRectangle(Palette.Mixing, null,
                new Rect(left + noScanW + differentChargeW, top, differentMzW, height));
            dc.DrawRectangle(Palette.PeptideLikeUnidentified, null,
                new Rect(left + noScanW + differentChargeW + differentMzW, top, similarMzW, height));
            DrawText(dc, N(overlap.NoSearchPsmInFileAndScan) + " no search PSM in same scan",
                Palette.SegmentLabel, 11, new Point(left + 12, top + 20), true);
            DrawText(dc, N(overlap.DifferentChargeInSharedScan) + " different charge",
                Palette.SegmentLabel, 9.5, new Point(left + noScanW + 8, top + 20), true);
            DrawText(dc, N(overlap.SameChargeButNoMzMatch) + " same z, different m/z",
                Palette.SegmentLabel, 9.5, new Point(left + noScanW + differentChargeW + 8, top + 20), true);
            DrawText(dc, N(overlap.SamePrecursorWithinTolerance), Palette.PeptideLikeUnidentified, 15,
                new Point(ActualWidth - 115, top + height + 12), true);
            DrawText(dc, "same scan + charge + m/z", Palette.Ink, 9.5,
                new Point(ActualWidth - 155, top + height + 36), true);

            double panelTop = 248, panelH = ActualHeight - panelTop - 42;
            dc.DrawRoundedRectangle(Palette.Panel, null,
                new Rect(54, panelTop, ActualWidth * 0.46, panelH), 8, 8);
            dc.DrawRoundedRectangle(Palette.Panel, null,
                new Rect(ActualWidth * 0.54, panelTop, ActualWidth * 0.40, panelH), 8, 8);

            DrawText(dc, overlap.IsStrictDeNovoOnlyScope
                    ? "WHY THE LARGE NONMATCH COUNT IS EXPECTED"
                    : "SOURCE-AWARE INTERPRETATION", Palette.Mixing, 11,
                new Point(78, panelTop + 24), true);
            DrawText(dc, overlap.IsStrictDeNovoOnlyScope
                    ? "PEAKS labels these rows de-novo-only: its database search did"
                    : "Each caller was filtered on its own native score before union", Palette.Ink, 10,
                new Point(78, panelTop + 60));
            DrawText(dc, overlap.IsStrictDeNovoOnlyScope
                    ? "not accept the same sequence as a database PSM."
                    : "resolution; ALC and SCORE were never treated as one scale.", Palette.Ink, 10,
                new Point(78, panelTop + 82));
            DrawText(dc, "Only " + N(concordance.DeNovoRowsExactToPeaksDatabaseAtSameCharge) + " of " +
                N(concordance.DeNovoRowsSharingPeaksDatabaseScanAndCharge) +
                " reproduce a PEAKS DB sequence at the same charge.", Palette.Muted, 10,
                new Point(78, panelTop + 124), true);
            DrawText(dc, overlap.IsStrictDeNovoOnlyScope
                    ? "That is a property of the export category, not a search-accuracy estimate."
                    : "This contextual overlap is not a database-search or caller-accuracy estimate.",
                Palette.Muted, 10, new Point(78, panelTop + 150), true);

            double right = ActualWidth * 0.54 + 24;
            DrawText(dc, "SEQUENCE CHECK AFTER ALL PRECURSOR GATES", Palette.Mixing, 11,
                new Point(right, panelTop + 24), true);
            DrawText(dc, N(overlap.StrictSequenceMatchesIgnoringPtm) + " exact after PTM removal",
                Palette.DeNovo, 13, new Point(right, panelTop + 62), true);
            DrawText(dc, "+" + N(overlap.AdditionalIleLeuEquivalentMatches) + " after I/L equivalence",
                Palette.DeNovo, 13, new Point(right, panelTop + 94), true);
            DrawText(dc, N(overlap.SequenceDivergencesAfterIleLeuFold) + " alternative sequences",
                Palette.Mixing, 16, new Point(right, panelTop + 136), true);
            DrawText(dc, "Not called search-engine conflicts. These hypotheses are excluded from", Palette.Muted, 9.5,
                new Point(right, panelTop + 174));
            DrawText(dc, "additive de novo quantification when they overlap an identified precursor.", Palette.Muted, 9.5,
                new Point(right, panelTop + 196));
        }
    }

    /// <summary>
    /// Reference-group sensitivity plot used in the talk. It deliberately shows two inclusion-based
    /// counts at the same identity thresholds and does not reuse the separate target/decoy floor plot
    /// shown on the USearch screen.
    /// </summary>
    public sealed class ReferenceIdentitySweepChart : TalkChartBase
    {
        private readonly List<IdentityScanRow> rows;
        public IReadOnlyList<IdentityScanRow> Rows => rows; // U3d native drawing inputs.
        private readonly int selectedIdentity;
        public int SelectedIdentity => selectedIdentity;

        public ReferenceIdentitySweepChart(IEnumerable<IdentityScanRow> rows, int selectedIdentity)
        {
            this.rows = (rows ?? Enumerable.Empty<IdentityScanRow>())
                .Where(r => r.Identity >= 25)
                .OrderByDescending(r => r.Identity).ToList();
            this.selectedIdentity = selectedIdentity;
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "Reference-group count versus sequence-identity threshold",
                "Lower thresholds connect more related sequences, producing fewer and larger reference groups.");
            if (ActualWidth < 600 || ActualHeight < 300 || rows.Count == 0) return;

            Rect plot = new Rect(66, 104, ActualWidth - 360, ActualHeight - 180);
            double max = Math.Max(50, Math.Ceiling(rows.Max(r => r.TargetGroups) / 50.0) * 50.0);
            var gridPen = new Pen(Palette.Grid, 0.8);
            for (int i = 0; i <= 5; i++)
            {
                double y = plot.Bottom - plot.Height * i / 5.0;
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawText(dc, N((int)Math.Round(max * i / 5.0)), Palette.Muted, 9,
                    new Point(plot.Left - 44, y - 7));
            }
            dc.DrawLine(new Pen(Palette.Axis, 1.1), plot.BottomLeft, plot.TopLeft);
            dc.DrawLine(new Pen(Palette.Axis, 1.1), plot.BottomLeft, plot.BottomRight);

            double hi = rows.Max(r => r.Identity), lo = rows.Min(r => r.Identity);
            double span = Math.Max(1, hi - lo);
            Point Map(IdentityScanRow row, double value) => new Point(
                plot.Left + (hi - row.Identity) / span * plot.Width,
                plot.Bottom - value / max * plot.Height);
            var target = rows.Select(r => Map(r, r.TargetGroups)).ToList();
            var curated = rows.Select(r => Map(r, r.ReportedTargetGroups)).ToList();
            DrawSeries(dc, target, Palette.Mixing, 2.5);
            DrawSeries(dc, curated, Palette.DeNovo, 2.5);

            int every = Math.Max(1, (int)Math.Ceiling(rows.Count / 8.0));
            for (int i = 0; i < rows.Count; i++)
            {
                if (i % every != 0 && i != rows.Count - 1) continue;
                Point p = target[i];
                DrawText(dc, rows[i].Identity.ToString(CultureInfo.CurrentCulture), Palette.Muted, 9,
                    new Point(p.X - 7, plot.Bottom + 8));
            }

            IdentityScanRow? selected = rows.FirstOrDefault(r => r.Identity == selectedIdentity);
            if (selected != null)
            {
                double x = Map(selected, 0).X;
                dc.DrawLine(new Pen(Palette.Ink, 1.3) { DashStyle = DashStyles.Dash },
                    new Point(x, plot.Top), new Point(x, plot.Bottom));
                DrawText(dc, "selected " + selectedIdentity + "%", Palette.Ink, 9.5,
                    new Point(Math.Min(x + 5, plot.Right - 70), plot.Top + 3), true);
            }

            DrawText(dc, "Groups with ≥1 real sequence", Palette.Mixing, 10,
                new Point(plot.Left, ActualHeight - 50), true);
            DrawText(dc, "Groups with ≥1 curated candidate", Palette.DeNovo, 10,
                new Point(plot.Left + 220, ActualHeight - 50), true);
            DrawText(dc, "Minimum identity required to connect proteins (%)", Palette.Muted, 9.5,
                new Point(plot.Left + 150, plot.Bottom + 32));

            double right = plot.Right + 32;
            DrawText(dc, "DEFINITIONS", Palette.Mixing, 11, new Point(right, 112), true);
            DrawText(dc, "Real sequence", Palette.Ink, 11, new Point(right, 150), true);
            DrawText(dc, "Not a reversed decoy; may still", Palette.Muted, 9.5, new Point(right, 174));
            DrawText(dc, "be labelled contaminant.", Palette.Muted, 9.5, new Point(right, 194));
            DrawText(dc, "Curated candidate", Palette.Ink, 11, new Point(right, 234), true);
            DrawText(dc, "At least one real sequence in the", Palette.Muted, 9.5, new Point(right, 258));
            DrawText(dc, "group is not labelled contaminant.", Palette.Muted, 9.5, new Point(right, 278));
            if (selected != null)
            {
                DrawText(dc, "AT " + selectedIdentity + "%", Palette.Mixing, 11, new Point(right, 330), true);
                DrawText(dc, N(selected.TargetGroups) + " groups with a real sequence", Palette.Ink, 11,
                    new Point(right, 360), true);
                DrawText(dc, N(selected.ReportedTargetGroups) + " with a curated candidate", Palette.Ink, 11,
                    new Point(right, 388), true);
            }
        }

        private static void DrawSeries(DrawingContext dc, IReadOnlyList<Point> points, Brush brush, double width)
        {
            var pen = new Pen(brush, width);
            for (int i = 1; i < points.Count; i++) dc.DrawLine(pen, points[i - 1], points[i]);
            foreach (Point point in points) dc.DrawEllipse(brush, null, point, 4, 4);
        }
    }

    public sealed class PeptideEvidenceOverlapChart : TalkChartBase
    {
        private readonly AnalyticsData data;
        private readonly DeNovoOverlapSummary overlap;

        public PeptideEvidenceOverlapChart(AnalyticsData data, DeNovoOverlapSummary overlap)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.overlap = overlap ?? throw new ArgumentNullException(nameof(overlap));
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "Peptide evidence and de novo/search-engine reconciliation",
                "Counts on the left are quantified representatives; counts on the right are filtered de novo rows before XIC collapse.");
            if (ActualWidth < 600 || ActualHeight < 300) return;

            double split = ActualWidth * 0.48;
            dc.DrawLine(new Pen(Palette.Grid, 1), new Point(split, 78), new Point(split, ActualHeight - 38));
            DrawText(dc, "Quantified representatives by run", Palette.Ink, 13, new Point(28, 82), true);

            var runs = data.Peptides.Select(p => p.Run).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
            int max = runs.SelectMany(r => new[]
            {
                data.Peptides.Count(p => string.Equals(p.Run, r, StringComparison.OrdinalIgnoreCase) && p.Side == NativeQuantFractions.SideIdentified),
                data.Peptides.Count(p => string.Equals(p.Run, r, StringComparison.OrdinalIgnoreCase) && p.Side == NativeQuantFractions.SideDeNovo),
            }).DefaultIfEmpty(1).Max();
            double chartTop = 125, chartBottom = ActualHeight - 92;
            double groupW = (split - 70) / Math.Max(1, runs.Count);
            for (int i = 0; i < runs.Count; i++)
            {
                int id = data.Peptides.Count(p => string.Equals(p.Run, runs[i], StringComparison.OrdinalIgnoreCase)
                    && p.Side == NativeQuantFractions.SideIdentified);
                int dn = data.Peptides.Count(p => string.Equals(p.Run, runs[i], StringComparison.OrdinalIgnoreCase)
                    && p.Side == NativeQuantFractions.SideDeNovo);
                double hId = (chartBottom - chartTop) * id / Math.Max(1.0, max);
                double hDn = (chartBottom - chartTop) * dn / Math.Max(1.0, max);
                double x = 40 + i * groupW;
                Bar(dc, new Rect(x, chartBottom - hId, groupW * 0.34, hId), Palette.Identified, N(id));
                Bar(dc, new Rect(x + groupW * 0.38, chartBottom - hDn, groupW * 0.34, hDn), Palette.DeNovo, N(dn));
                DrawText(dc, "Run " + (i + 1).ToString(CultureInfo.CurrentCulture), Palette.Muted, 10,
                    new Point(x + 4, chartBottom + 8));
            }
            DrawText(dc, "Search engine", Palette.Identified, 10, new Point(40, ActualHeight - 45), true);
            DrawText(dc, "De novo", Palette.DeNovo, 10, new Point(150, ActualHeight - 45), true);
            DrawText(dc, N(data.Peptides.Count) + " pooled representatives · " +
                P(data.Peptides.Count == 0 ? 0 : (double)data.DeNovoCount / data.Peptides.Count) + " de novo",
                Palette.Ink, 12, new Point(40, ActualHeight - 68), true);

            double rightX = split + 28, rightW = ActualWidth - rightX - 28;
            DrawText(dc, "Overlap study: same scan is not automatically the same precursor", Palette.Ink, 13,
                new Point(rightX, 82), true);
            DrawText(dc, "Filter: length ≥ " + overlap.MinLength + "; ALC ≥ " +
                overlap.MinAverageLocalConfidence.ToString("0.#", CultureInfo.CurrentCulture) + "%; " +
                overlap.StretchLength + " consecutive residues ≥ " + overlap.StretchFloor + "% local confidence",
                Palette.Muted, 10, new Point(rightX, 106));

            var stages = new (string Label, int Count, int Denom, Brush Fill)[]
            {
                ("Passed de novo filter", overlap.FilteredDeNovoRows, overlap.RawDeNovoRows, Palette.DeNovo),
                ("Share file + scan with a search PSM", overlap.SharedFileAndScan, overlap.FilteredDeNovoRows, Palette.Identified),
                ("Same file + scan + charge", overlap.SameFileScanAndCharge, overlap.SharedFileAndScan, Palette.DeNovo),
                ("Exact sequence in same-charge scans", overlap.ScanLevelStrictSequenceMatchesIgnoringPtm, overlap.SameFileScanAndCharge, Palette.Identified),
                ("Same charge but no m/z match", overlap.SameChargeButNoMzMatch, overlap.SameFileScanAndCharge, Palette.Remainder),
                ("Comparable precursor within " + overlap.PrecursorTolerancePpm.ToString("0.#", CultureInfo.CurrentCulture) + " ppm", overlap.SamePrecursorWithinTolerance, overlap.SameFileScanAndCharge, Palette.PeptideLikeUnidentified),
            };
            double y = 143, rowH = 54;
            foreach (var s in stages)
            {
                DrawText(dc, s.Label, Palette.Ink, 10, new Point(rightX, y));
                double frac = s.Denom > 0 ? (double)s.Count / s.Denom : 0;
                Bar(dc, new Rect(rightX, y + 21, Math.Max(2, rightW * frac), 18), s.Fill,
                    N(s.Count) + " / " + N(s.Denom) + " (" + P(frac) + ")");
                y += rowH;
            }
            DrawText(dc, "Precursor-qualified subset: strict exact = " + N(overlap.StrictSequenceMatchesIgnoringPtm) +
                "; +" + N(overlap.AdditionalIleLeuEquivalentMatches) + " after I/L folding; " +
                N(overlap.SequenceDivergencesAfterIleLeuFold) + " still divergent.",
                Palette.Muted, 10, new Point(rightX, ActualHeight - 48));
        }
    }

    public sealed class DeNovoScoreDistributionChart : TalkChartBase
    {
        private readonly List<DeNovoSourceScoreDistribution> distributions;
        public IReadOnlyList<DeNovoSourceScoreDistribution> Distributions => distributions;

        public DeNovoScoreDistributionChart(DeNovoScoreDistribution distribution)
        {
            if (distribution == null) throw new ArgumentNullException(nameof(distribution));
            distribution.Validate();
            distributions = new() { DeNovoSourceScoreDistributionStudy.FromLegacy(distribution) };
        }

        public DeNovoScoreDistributionChart(IEnumerable<DeNovoSourceScoreDistribution> distributions)
        {
            if (distributions == null) throw new ArgumentNullException(nameof(distributions));
            this.distributions = distributions.ToList();
            if (this.distributions.Count == 0)
                throw new ArgumentException("At least one source distribution is required.", nameof(distributions));
            foreach (DeNovoSourceScoreDistribution distribution in this.distributions) distribution.Validate();
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "De novo score distributions and source-specific acceptance rules",
                "Background bars show raw spectrum calls; overlaid bars show calls retained after score, length and local-stretch filters. ALC and SCORE are not equivalent scales.");
            if (ActualWidth < 600 || ActualHeight < 300) return;

            int count = distributions.Count;
            double gap = 28;
            double panelWidth = (ActualWidth - 70 - gap * (count - 1)) / count;
            for (int panel = 0; panel < count; panel++)
            {
                DeNovoSourceScoreDistribution distribution = distributions[panel];
                double left = 40 + panel * (panelWidth + gap);
                Rect plot = new Rect(left + 42, 134, panelWidth - 70, ActualHeight - 246);
                int rawMax = distribution.Bins.Max(b => b.RawRows);
                double max = NiceMaximum(rawMax);
                dc.DrawLine(new Pen(Palette.Axis, 1.1), plot.BottomLeft, plot.TopLeft);
                dc.DrawLine(new Pen(Palette.Axis, 1.1), plot.BottomLeft, plot.BottomRight);
                for (int i = 0; i <= 4; i++)
                {
                    double value = max * i / 4.0;
                    double y = plot.Bottom - plot.Height * value / max;
                    dc.DrawLine(new Pen(Palette.Grid, 0.7), new Point(plot.Left, y), new Point(plot.Right, y));
                    DrawText(dc, value.ToString("0.#", CultureInfo.CurrentCulture), Palette.Muted, 8,
                        new Point(plot.Left - 39, y - 6));
                }

                double slot = plot.Width / distribution.Bins.Count;
                for (int i = 0; i < distribution.Bins.Count; i++)
                {
                    DeNovoScoreBin bin = distribution.Bins[i];
                    double x = plot.Left + i * slot + slot * 0.08;
                    double rawH = plot.Height * bin.RawRows / max;
                    double keptH = plot.Height * bin.PassingAllCriteria / max;
                    dc.DrawRectangle(Palette.Grid, null,
                        new Rect(x, plot.Bottom - rawH, slot * 0.84, rawH));
                    dc.DrawRectangle(Palette.DeNovo, null,
                        new Rect(x + slot * 0.17, plot.Bottom - keptH, slot * 0.50, keptH));
                    if (i % 2 == 0)
                        DrawText(dc, bin.LowerInclusive.ToString("0", CultureInfo.CurrentCulture),
                            Palette.Muted, 8, new Point(x - 1, plot.Bottom + 8));
                }

                double cutoffX = plot.Left + plot.Width * distribution.MinimumConfidence / 100.0;
                dc.DrawLine(new Pen(Palette.Mixing, 1.8) { DashStyle = DashStyles.Dash },
                    new Point(cutoffX, plot.Top), new Point(cutoffX, plot.Bottom));
                DrawText(dc, distribution.ConfidenceField + " >= " +
                    distribution.MinimumConfidence.ToString("0.#", CultureInfo.CurrentCulture),
                    Palette.Mixing, 9, new Point(Math.Min(cutoffX + 4, plot.Right - 95), plot.Top - 22), true);

                DrawText(dc, distribution.SourceDisplayName, Palette.Ink, 13,
                    new Point(left + 42, 88), true);
                DrawText(dc, distribution.ConfidenceField, Palette.Muted, 9,
                    new Point(left + 42, 111));
                DrawText(dc, N(distribution.RawRows) + " raw calls", Palette.Ink, 10,
                    new Point(left + 42, plot.Bottom + 36), true);
                DrawText(dc, N(distribution.RowsPassingAllCriteria) + " retained (" +
                    P(distribution.RawRows > 0
                        ? (double)distribution.RowsPassingAllCriteria / distribution.RawRows : 0) + ")",
                    Palette.DeNovo, 10, new Point(left + 42, plot.Bottom + 58), true);
                DrawText(dc, "length >= " + distribution.MinLength + "; " +
                    distribution.StretchLength + " consecutive local scores >= " +
                    distribution.StretchFloor, Palette.Muted, 8.5,
                    new Point(left + 42, plot.Bottom + 80));
            }

        }

        private static double NiceMaximum(int value)
        {
            if (value <= 0) return 1;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
            double scaled = value / magnitude;
            double nice = scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10;
            return nice * magnitude;
        }
    }

    /// <summary>
    /// The de novo yield-versus-cutoff figure: two panels side by side, PEAKS de novo (full) and DeepNovo.
    /// In each, a stacked bar per confidence cutoff (50 to 100 in steps of 5) of DISTINCT clean peptide
    /// sequences surviving at or above the cutoff, split into the part also present in the PEAKS de-novo-only
    /// reference (ALC &gt;= 80) and the part new to the source, with the overlap PERCENT printed above each
    /// bar. There is ONE vertical axis per panel, in thousands of distinct peptides, and the two panels use
    /// DIFFERENT vertical scales because PEAKS de novo (full) is about ten times DeepNovo; the subtitle says
    /// so. A callout marks the 80% cutoff. The X axis of each panel is that source's own confidence field.
    /// </summary>
    public sealed class DeNovoScoreSweepChart : TalkChartBase
    {
        private readonly DeNovoScoreSweep sweep;

        public DeNovoScoreSweepChart(DeNovoScoreSweep sweep)
        {
            this.sweep = sweep ?? throw new ArgumentNullException(nameof(sweep));
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Palette.Backdrop, null, new Rect(0, 0, ActualWidth, ActualHeight));
            DrawText(dc, "De novo peptide yield versus confidence cutoff", Palette.Ink, 21,
                new Point(24, 16), true);
            DrawText(dc, "Distinct clean sequences (I folded to L), three replicates pooled, " +
                "collapsed to each tool's max confidence per sequence.", Palette.Muted, 11,
                new Point(24, 48));
            DrawText(dc, "The two panels use DIFFERENT vertical scales: PEAKS de novo (full) is about 10x DeepNovo.",
                AccentAmber, 11.5, new Point(24, 68), true);
            if (ActualWidth < 600 || ActualHeight < 300) return;

            // The shared legend.
            double ly = 92;
            dc.DrawRectangle(DeepBlue, null, new Rect(150, ly, 14, 14));
            DrawText(dc, "in PEAKS de novo only reference (ALC >= " +
                sweep.ReferenceCutoff.ToString("0", CultureInfo.InvariantCulture) + ")",
                Palette.Ink, 11, new Point(170, ly - 2));
            dc.DrawRectangle(DeepGreen, null, new Rect(470, ly, 14, 14));
            DrawText(dc, "new (not in that reference)", Palette.Ink, 11, new Point(490, ly - 2));

            double gap = 56;
            double panelW = (ActualWidth - 96 - gap) / 2.0;
            double panelTop = 126, panelBottom = ActualHeight - 40;
            DrawPanel(dc, new Rect(56, panelTop, panelW, panelBottom - panelTop), sweep.DeNovoAll,
                "PEAKS de novo (full), filtered by ALC", DeepBlue);
            DrawPanel(dc, new Rect(56 + panelW + gap, panelTop, panelW, panelBottom - panelTop), sweep.DeepNovo,
                "DeepNovo, filtered by score", DeepGreen);
        }

        private void DrawPanel(DrawingContext dc, Rect area, DeNovoScoreSweepPanel panel,
                               string title, Brush titleBrush)
        {
            DrawText(dc, title, titleBrush, 14.5, new Point(area.Left + 40, area.Top), true);

            Rect plot = new Rect(area.Left + 64, area.Top + 34, area.Width - 78, area.Height - 74);
            if (plot.Width <= 0 || plot.Height <= 0 || panel.Points.Count == 0) return;

            int countMax = panel.Points.Max(p => p.DistinctCleanTotal);
            (double max, double step) = NiceAxis(countMax * 1.05);
            var gridPen = new Pen(Palette.Grid, 0.8);
            var axisPen = new Pen(Palette.Axis, 1.2);

            for (double value = 0; value <= max + 1e-6; value += step)
            {
                double y = plot.Bottom - plot.Height * value / max;
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawText(dc, (value / 1000.0).ToString("0", CultureInfo.InvariantCulture), Palette.Muted, 9,
                    new Point(plot.Left - 34, y - 8));
            }
            dc.DrawLine(axisPen, plot.BottomLeft, plot.TopLeft);
            dc.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);

            // The single Y axis title, rotated up the left edge.
            DrawVerticalText(dc, "distinct peptides (thousands)", Palette.Muted, 10.5,
                new Point(area.Left + 8, plot.Bottom));

            double slot = plot.Width / panel.Points.Count;
            DeNovoSweepPoint? at80 = null;
            double bar80CenterX = 0, bar80TopY = 0;
            for (int i = 0; i < panel.Points.Count; i++)
            {
                DeNovoSweepPoint p = panel.Points[i];
                double x = plot.Left + i * slot + slot * 0.18;
                double w = slot * 0.64;
                double overlapH = plot.Height * p.OverlapWithReference / max;
                double newH = plot.Height * p.NewNotInReference / max;
                double top = plot.Bottom - overlapH - newH;
                dc.DrawRectangle(DeepBlue, null, new Rect(x, plot.Bottom - overlapH, w, overlapH));
                dc.DrawRectangle(DeepGreen, null, new Rect(x, top, w, newH));

                // The overlap PERCENT above the bar.
                string pct = (p.OverlapFraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
                FormattedText ft = Text(pct, Palette.Ink, 10, true);
                dc.DrawText(ft, new Point(x + (w - ft.Width) / 2.0, top - ft.Height - 2));

                DrawText(dc, p.Threshold.ToString("0", CultureInfo.InvariantCulture), Palette.Muted, 9.5,
                    new Point(x + w / 2.0 - 7, plot.Bottom + 8));

                if (Math.Abs(p.Threshold - sweep.ReferenceCutoff) < 1e-6)
                {
                    at80 = p;
                    bar80CenterX = x + w / 2.0;
                    bar80TopY = top;
                    dc.DrawRectangle(null, new Pen(AccentAmber, 2.2),
                        new Rect(x - 2, top - 2, w + 4, plot.Bottom - top + 4));
                }
            }

            DrawText(dc, "confidence cutoff (%)", Palette.Muted, 11,
                new Point(plot.Left + plot.Width / 2 - 62, plot.Bottom + 28));

            if (at80 != null) DrawCallout(dc, plot, at80, bar80CenterX, bar80TopY);
        }

        /// <summary>The amber callout box at the reference cutoff, with a line down to the marked bar.</summary>
        private void DrawCallout(DrawingContext dc, Rect plot, DeNovoSweepPoint at80,
                                 double barCenterX, double barTopY)
        {
            string line1 = "at " + at80.Threshold.ToString("0", CultureInfo.InvariantCulture) + "% cutoff";
            string line2 = N(at80.DistinctCleanTotal) + " peptides";
            string line3 = (at80.OverlapFraction * 100).ToString("0", CultureInfo.InvariantCulture) + "% in reference";

            double boxW = 168, boxH = 62;
            double bx = Math.Max(plot.Left + 6, Math.Min(barCenterX - boxW - 24, plot.Right - boxW - 6));
            double by = plot.Top + plot.Height * 0.30;
            var box = new Rect(bx, by, boxW, boxH);
            // Opaque so the bars and their percent labels behind it do not bleed through: a near-white
            // ground first, then a faint amber wash, then the amber border.
            dc.DrawRoundedRectangle(Palette.Backdrop, null, box, 6, 6);
            dc.DrawRoundedRectangle(Translucent(AccentAmber, 0.10), new Pen(AccentAmber, 1.6), box, 6, 6);
            DrawText(dc, line1, Palette.Ink, 11.5, new Point(bx + 12, by + 8), true);
            DrawText(dc, line2, Palette.Ink, 11.5, new Point(bx + 12, by + 24), true);
            DrawText(dc, line3, Palette.Ink, 11.5, new Point(bx + 12, by + 40), true);

            var pen = new Pen(AccentAmber, 1.8);
            dc.DrawLine(pen, new Point(box.Right, box.Bottom - 8), new Point(barCenterX, barTopY - 4));
            dc.DrawEllipse(AccentAmber, null, new Point(barCenterX, barTopY - 4), 2.5, 2.5);
        }

        private void DrawVerticalText(DrawingContext dc, string s, Brush brush, double size, Point at)
        {
            FormattedText ft = Text(s, brush, size);
            dc.PushTransform(new RotateTransform(-90, at.X, at.Y));
            dc.DrawText(ft, at);
            dc.Pop();
        }

        /// <summary>A rounded axis maximum and a tick step giving roughly seven divisions.</summary>
        private static (double Max, double Step) NiceAxis(double dataMax, int targetTicks = 7)
        {
            if (dataMax <= 0) return (1, 1);
            double rough = dataMax / targetTicks;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double norm = rough / magnitude;
            double niceStep = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10) * magnitude;
            double max = Math.Ceiling(dataMax / niceStep) * niceStep;
            return (max, niceStep);
        }
    }

    /// <summary>
    /// The PEAKS-vs-DeepNovo per-spectrum agreement study after a same-charge and precursor-m/z gate.
    /// Each qualified pair is placed by X = PEAKS neutral mass (Da) and Y = sequence identity between the
    /// two tools' calls, and coloured by whether the calls are identical, have modification-aware
    /// theoretical masses within tolerance, or have different theoretical masses. At this scale a raw
    /// scatter is an unreadable blob, so the cloud is drawn
    /// as a per-category 2D density (translucent bins whose opacity grows with the count), with the
    /// identical calls forming their own light band along the 100% identity line. A side panel carries the
    /// three-category tally, explicit gate counts and tolerances, the isobaric-swap footnote, and the
    /// membership count. The raw scan-only pairs remain in the companion CSV. No XIC is used.
    /// </summary>
    public sealed class DeNovoAgreementChart : TalkChartBase
    {
        private readonly DeNovoAgreementResult agreement;

        private const int MassBins = 72;
        private const int IdentityBins = 50;

        public DeNovoAgreementChart(DeNovoAgreementResult agreement)
        {
            this.agreement = agreement ?? throw new ArgumentNullException(nameof(agreement));
            this.agreement.Validate();
        }

        private Brush ColourOf(AgreementCategory category) => category switch
        {
            AgreementCategory.Identical => DeepBlue,
            AgreementCategory.SameMassRearrangement => AccentAmber,
            AgreementCategory.DifferentMass => ContextGray,
            _ => ContextGray,
        };

        private static string LabelOf(AgreementCategory category) => category switch
        {
            AgreementCategory.Identical => "identical (same sequence)",
            AgreementCategory.SameMassRearrangement => "different sequence; theoretical mass match",
            AgreementCategory.DifferentMass => "different theoretical mass",
            _ => "unknown",
        };

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Palette.Backdrop, null, new Rect(0, 0, ActualWidth, ActualHeight));
            DrawText(dc, "PEAKS vs DeepNovo: agreement after matching the precursor", Palette.Ink, 20,
                new Point(24, 16), true);
            DrawText(dc, "Primary pair: deterministic one-to-one calls within normalized run + scan, same charge, m/z within " +
                agreement.PrecursorTolerancePpm.ToString("0.###", CultureInfo.InvariantCulture) +
                " ppm. X: PEAKS reported neutral mass. Y: PTM-stripped sequence identity (I/L distinct).",
                Palette.Muted, 11, new Point(24, 46));
            DrawText(dc, "Population: all imported export calls (observed floors: PEAKS ALC " +
                agreement.PeaksObservedMinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture) +
                ", DeepNovo SCORE " +
                agreement.DeepNovoObservedMinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture) +
                "); this is not the downstream >=80 quantified tier.",
                Palette.Mixing, 10.5, new Point(24, 68), true);
            DrawText(dc, "Primary: " + N(agreement.Total) + " precursor-call pairs across " +
                N(agreement.PrecursorQualifiedScanOnly) + " of " + N(agreement.ScanOnlyTotal) +
                " shared scans; identical PTM-stripped sequences: " +
                (agreement.IdenticalFraction * 100).ToString("F1", CultureInfo.InvariantCulture) + "%.",
                Palette.Ink, 12.5, new Point(24, 90), true);
            DrawText(dc, "Among " + N(agreement.Disagreements) + " sequence disagreements, " +
                N(agreement.SameMassRearrangement) +
                " have modification-aware theoretical masses within " +
                agreement.MassToleranceDa.ToString("0.###", CultureInfo.InvariantCulture) + " Da.",
                Palette.Muted, 11, new Point(24, 112));
            if (ActualWidth < 600 || ActualHeight < 300) return;

            double panelW = 322;
            double gap = 30;
            Rect plot = new Rect(84, 140, ActualWidth - 84 - panelW - gap - 24, ActualHeight - 140 - 66);
            if (plot.Width <= 0 || plot.Height <= 0) return;

            double massMax = agreement.Spectra.Count > 0 ? agreement.Spectra.Max(s => s.MassDa) : 0;
            double xMax = NiceCeiling(massMax);
            if (xMax <= 0) xMax = 1;

            var gridPen = new Pen(Palette.Grid, 0.8);
            var axisPen = new Pen(Palette.Axis, 1.2);
            for (int i = 0; i <= 5; i++)
            {
                double v = 100.0 * i / 5.0;
                double y = plot.Bottom - plot.Height * v / 100.0;
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawText(dc, v.ToString("0", CultureInfo.InvariantCulture) + "%", Palette.Muted, 8.5,
                    new Point(plot.Left - 40, y - 7));
            }
            for (int i = 0; i <= 5; i++)
            {
                double v = xMax * i / 5.0;
                double x = plot.Left + plot.Width * v / xMax;
                dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                DrawText(dc, v.ToString("0", CultureInfo.InvariantCulture), Palette.Muted, 8.5,
                    new Point(x - 12, plot.Bottom + 8));
            }
            dc.DrawLine(axisPen, plot.BottomLeft, plot.TopLeft);
            dc.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);
            DrawText(dc, "PEAKS peptide neutral mass (Da)", Palette.Muted, 10,
                new Point(plot.Left + plot.Width / 2 - 84, plot.Bottom + 28));
            DrawVerticalText(dc, "sequence identity (%)", Palette.Muted, 10,
                new Point(plot.Left - 62, plot.Bottom));

            // ---- per-category 2D density: bin, then draw occupied cells with opacity by count ----------
            double cellW = plot.Width / MassBins;
            double cellH = plot.Height / IdentityBins;
            // Draw order: different mass (context) first, then same-mass, then identical on top as the
            // light band along 100% identity.
            foreach (AgreementCategory category in new[]
            {
                AgreementCategory.DifferentMass,
                AgreementCategory.SameMassRearrangement,
                AgreementCategory.Identical,
            })
            {
                int[,] cells = new int[MassBins, IdentityBins];
                int catMax = 0;
                foreach (AgreementSpectrum s in agreement.Spectra)
                {
                    if (s.Category != category || s.MassDa <= 0) continue;
                    int mx = (int)(Math.Min(s.MassDa, xMax) / xMax * MassBins);
                    if (mx >= MassBins) mx = MassBins - 1;
                    double id = Math.Max(0, Math.Min(100, s.SequenceIdentityPercent));
                    int iy = (int)(id / 100.0 * IdentityBins);
                    if (iy >= IdentityBins) iy = IdentityBins - 1;
                    int c = ++cells[mx, iy];
                    if (c > catMax) catMax = c;
                }
                if (catMax == 0) continue;
                double logMax = Math.Log(1 + catMax);
                Color baseColor = ((SolidColorBrush)ColourOf(category)).Color;
                double floor = category == AgreementCategory.Identical ? 0.22 : 0.16;
                double span = category == AgreementCategory.Identical ? 0.55 : 0.74;
                for (int mx = 0; mx < MassBins; mx++)
                    for (int iy = 0; iy < IdentityBins; iy++)
                    {
                        int c = cells[mx, iy];
                        if (c == 0) continue;
                        double op = floor + span * (Math.Log(1 + c) / logMax);
                        var fill = new SolidColorBrush(baseColor) { Opacity = Math.Min(0.95, op) };
                        fill.Freeze();
                        double x = plot.Left + mx * cellW;
                        double y = plot.Bottom - (iy + 1) * cellH;
                        dc.DrawRectangle(fill, null, new Rect(x, y, cellW + 0.6, cellH + 0.6));
                    }
            }

            int placed = agreement.Spectra.Count(s => s.MassDa > 0);
            if (placed < agreement.Total)
                DrawText(dc, N(agreement.Total - placed) + " spectra without a reported mass are omitted",
                    Palette.Muted, 8.5, new Point(plot.Left + 6, plot.Top + 4));

            // ---- side panel: category tally, disagreement headline, footnote, membership --------------
            double px = plot.Right + gap;
            Rect side = new Rect(px, 118, panelW, ActualHeight - 118 - 44);
            dc.DrawRoundedRectangle(Palette.Panel, new Pen(Palette.Grid, 0.8), side, 7, 7);
            double ix = side.Left + 18, iy2 = side.Top + 18;

            DrawText(dc, N(agreement.Total) + " primary pairs", Palette.Ink, 12.5,
                new Point(ix, iy2), true);
            DrawText(dc, "across " + N(agreement.PrecursorQualifiedScanOnly) + " precursor-qualified shared scans",
                Palette.Muted, 9.5, new Point(ix, iy2 + 17));
            iy2 += 42;
            foreach (AgreementCategory category in new[]
            {
                AgreementCategory.Identical,
                AgreementCategory.SameMassRearrangement,
                AgreementCategory.DifferentMass,
            })
            {
                int count = agreement.CountOf(category);
                double share = agreement.Total > 0 ? (double)count / agreement.Total * 100.0 : 0.0;
                dc.DrawRectangle(ColourOf(category), null, new Rect(ix, iy2 + 2, 13, 13));
                DrawText(dc, LabelOf(category), Palette.Ink, 11, new Point(ix + 22, iy2 - 1));
                DrawText(dc, N(count) + "  (" + share.ToString("F1", CultureInfo.InvariantCulture) + "%)",
                    Palette.Muted, 11, new Point(ix + 22, iy2 + 14));
                iy2 += 42;
            }

            iy2 += 4;
            dc.DrawLine(new Pen(Palette.Grid, 0.8), new Point(ix, iy2), new Point(side.Right - 18, iy2));
            iy2 += 14;
            DrawText(dc, "Among sequence disagreements", Palette.Ink, 11.5, new Point(ix, iy2), true);
            DrawText(dc, N(agreement.SameMassRearrangement) + " of " +
                N(agreement.Disagreements) + " disagreements", Palette.Muted, 11, new Point(ix, iy2 + 17));
            DrawText(dc, "match in modification-aware theoretical mass.", Palette.Muted, 11,
                new Point(ix, iy2 + 31));
            iy2 += 54;
            DrawText(dc, N(agreement.SingleIsobaricSwaps) +
                (agreement.SingleIsobaricSwaps == 1 ? " pair differs" : " pairs differ") + " by one equal-mass",
                Palette.Muted, 9.5, new Point(ix, iy2));
            DrawText(dc, "residue substitution (descriptive, not validation).", Palette.Muted, 9.5,
                new Point(ix, iy2 + 13));
            iy2 += 34;
            DrawText(dc, "Theoretical-mass tolerance: |delta| <= " +
                agreement.MassToleranceDa.ToString("0.###", CultureInfo.InvariantCulture) + " Da.",
                Palette.Muted, 9.5, new Point(ix, iy2));
            iy2 += 22;
            DrawText(dc, "RT <= " + agreement.RtSensitivityToleranceMin.ToString("0.###", CultureInfo.InvariantCulture) +
                " min: " + N(agreement.RtSensitivityQualifiedCount) + " pairs (sensitivity only).",
                Palette.Muted, 9.5, new Point(ix, iy2));
            iy2 += 22;
            if (agreement.DeNovoOnlyMembershipAvailable)
                DrawText(dc, "Same-precursor member of de novo only: " + N(agreement.InDeNovoOnlyCount) + " of " +
                    N(agreement.Total) + " (" +
                    (agreement.InDeNovoOnlyFraction * 100).ToString("F1", CultureInfo.InvariantCulture) + "%).",
                    Palette.Muted, 9.5, new Point(ix, iy2));
            else
                DrawText(dc, "De-novo-only membership: not evaluated (audit source not loaded).",
                    Palette.Muted, 9.5, new Point(ix, iy2));
        }

        private void DrawVerticalText(DrawingContext dc, string s, Brush brush, double size, Point at)
        {
            FormattedText ft = Text(s, brush, size);
            dc.PushTransform(new RotateTransform(-90, at.X, at.Y));
            dc.DrawText(ft, at);
            dc.Pop();
        }

        private static double NiceCeiling(double value)
        {
            if (value <= 0) return 0;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
            double scaled = value / magnitude;
            double nice = scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10;
            return nice * magnitude;
        }
    }

    /// <summary>
    /// The score-vs-score dot plot: for clean sequences (mods stripped, letters only, I folded to L) found
    /// by BOTH tools, a dot per sequence at X = PEAKS ALC, Y = DeepNovo SCORE, with a faint y=x reference
    /// and two marginal histograms - the ALC of the sequences PEAKS finds alone along the top, and the
    /// SCORE of the sequences DeepNovo finds alone down the right. Pure sequence bookkeeping: no XIC.
    /// </summary>
    public sealed class DeNovoScoreScatterChart : TalkChartBase
    {
        private readonly DeNovoScoreScatter scatter;
        private const double Lo = 48, Hi = 100;

        public DeNovoScoreScatterChart(DeNovoScoreScatter scatter)
        {
            this.scatter = scatter ?? throw new ArgumentNullException(nameof(scatter));
            this.scatter.Validate();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Palette.Backdrop, null, new Rect(0, 0, ActualWidth, ActualHeight));
            DrawText(dc, "Do PEAKS and DeepNovo agree when they call the same peptide?", Palette.Ink, 20,
                new Point(24, 16), true);
            DrawText(dc, "Three pooled replicates. Each dot is one peptide sequence " +
                "identified by both tools; axes give each tool's own confidence.", Palette.Muted, 11,
                new Point(24, 46));
            DrawText(dc, "Matched by modification-stripped, I=L-folded sequence, keeping the max confidence per tool.",
                Palette.Muted, 11, new Point(24, 64));
            if (ActualWidth < 600 || ActualHeight < 300) return;

            double topH = 92;      // top marginal histogram height
            double rightW = 150;   // right marginal histogram width
            Rect main = new Rect(96, 108 + topH + 14, ActualWidth - 96 - rightW - 40, ActualHeight - 108 - topH - 14 - 66);
            if (main.Width <= 0 || main.Height <= 0) return;
            Rect top = new Rect(main.Left, 108, main.Width, topH);
            Rect right = new Rect(main.Right + 14, main.Top, rightW, main.Height);

            var gridPen = new Pen(Palette.Grid, 0.8);
            var axisPen = new Pen(Palette.Axis, 1.2);

            double MapX(double v) => main.Left + main.Width * (v - Lo) / (Hi - Lo);
            double MapY(double v) => main.Bottom - main.Height * (v - Lo) / (Hi - Lo);

            // ---- main scatter grid + ticks (50..100) --------------------------------------------------
            for (int v = 50; v <= 100; v += 10)
            {
                double x = MapX(v), y = MapY(v);
                dc.DrawLine(gridPen, new Point(x, main.Top), new Point(x, main.Bottom));
                dc.DrawLine(gridPen, new Point(main.Left, y), new Point(main.Right, y));
                DrawText(dc, v.ToString(CultureInfo.InvariantCulture), Palette.Muted, 9,
                    new Point(x - 9, main.Bottom + 8));
                DrawText(dc, v.ToString(CultureInfo.InvariantCulture), Palette.Muted, 9,
                    new Point(main.Left - 30, y - 8));
            }
            dc.DrawLine(axisPen, main.BottomLeft, main.TopLeft);
            dc.DrawLine(axisPen, main.BottomLeft, main.BottomRight);

            // y = x reference.
            dc.DrawLine(new Pen(ContextGray, 1.4) { DashStyle = DashStyles.Dash },
                new Point(MapX(Lo), MapY(Lo)), new Point(MapX(Hi), MapY(Hi)));

            // ---- the dots -----------------------------------------------------------------------------
            Brush dot = Translucent(DeepBlue, 0.22);
            foreach (ScoreScatterPoint p in scatter.Shared)
                dc.DrawEllipse(dot, null, new Point(MapX(p.PeaksAlc), MapY(p.DeepNovoScore)), 2.3, 2.3);

            DrawText(dc, "PEAKS de novo confidence, ALC (%)", Palette.Muted, 11,
                new Point(main.Left + main.Width / 2 - 96, main.Bottom + 28));
            DrawVerticalText(dc, "DeepNovo confidence, SCORE (%)", Palette.Muted, 11,
                new Point(main.Left - 44, main.Bottom));

            // ---- top marginal: ALC of PEAKS-unique ----------------------------------------------------
            DrawHistogramHorizontal(dc, top, scatter.PeaksUniqueAlc, DeepBlue, upward: true);
            DrawText(dc, "PEAKS-only peptides  (n = " + N(scatter.PeaksUniqueCount) + ")",
                DeepBlue, 11.5, new Point(top.Left, top.Top - 22), true);

            // ---- right marginal: SCORE of DeepNovo-unique ---------------------------------------------
            DrawHistogramVertical(dc, right, scatter.DeepNovoUniqueScore, DeepGreen);
            DrawText(dc, "DeepNovo-only peptides", DeepGreen, 10.5,
                new Point(right.Left + 2, right.Top - 4), true);
            DrawText(dc, "n = " + N(scatter.DeepNovoUniqueCount), DeepGreen, 10.5,
                new Point(right.Left + 2, right.Top + 12), true);

            // ---- annotation box -----------------------------------------------------------------------
            double boxW = 210, boxH = 56;
            var box = new Rect(main.Right - boxW - 8, main.Bottom - boxH - 8, boxW, boxH);
            dc.DrawRoundedRectangle(Palette.Backdrop, new Pen(DeepBlue, 1.6), box, 5, 5);
            DrawText(dc, "Shared peptides: " + N(scatter.SharedCount), Palette.Ink, 13,
                new Point(box.Left + 12, box.Top + 8), true);
            DrawText(dc, "Pearson r = " + scatter.PearsonR.ToString("0.00", CultureInfo.InvariantCulture),
                Palette.Ink, 13, new Point(box.Left + 12, box.Top + 30), true);
        }

        /// <summary>A histogram over [Lo,Hi] drawn along a horizontal band, bars growing upward from the
        /// band's bottom (the top marginal).</summary>
        private void DrawHistogramHorizontal(DrawingContext dc, Rect band, IReadOnlyList<double> values,
                                             Brush fill, bool upward)
        {
            const int bins = 26;
            int[] counts = Bin(values, bins);
            int max = counts.Length > 0 ? counts.Max() : 0;
            if (max <= 0) return;
            double bw = band.Width / bins;
            for (int i = 0; i < bins; i++)
            {
                if (counts[i] == 0) continue;
                double h = band.Height * counts[i] / max;
                double x = band.Left + i * bw;
                double y = upward ? band.Bottom - h : band.Top;
                dc.DrawRectangle(fill, new Pen(Palette.Backdrop, 0.5), new Rect(x, y, bw, h));
            }
            // A couple of count ticks so the marginal has a scale.
            DrawText(dc, N(max), Palette.Muted, 8.5, new Point(band.Left - 42, band.Top - 4));
            DrawText(dc, "0", Palette.Muted, 8.5, new Point(band.Left - 16, band.Bottom - 12));
        }

        /// <summary>A histogram over [Lo,Hi] drawn down a vertical band, bars growing rightward (the right
        /// marginal), aligned to the same Y scale as the main scatter.</summary>
        private void DrawHistogramVertical(DrawingContext dc, Rect band, IReadOnlyList<double> values, Brush fill)
        {
            const int bins = 26;
            int[] counts = Bin(values, bins);
            int max = counts.Length > 0 ? counts.Max() : 0;
            if (max <= 0) return;
            double bh = band.Height / bins;
            for (int i = 0; i < bins; i++)
            {
                if (counts[i] == 0) continue;
                double w = band.Width * counts[i] / max;
                // bin i covers [Lo + i*step, ...]; higher score is higher on the axis, so draw from the top.
                double y = band.Bottom - (i + 1) * bh;
                dc.DrawRectangle(fill, new Pen(Palette.Backdrop, 0.5), new Rect(band.Left, y, w, bh));
            }
            DrawText(dc, "0", Palette.Muted, 8.5, new Point(band.Left, band.Bottom + 6));
            DrawText(dc, N(max), Palette.Muted, 8.5, new Point(band.Left + band.Width - 26, band.Bottom + 6));
        }

        private static int[] Bin(IReadOnlyList<double> values, int bins)
        {
            var counts = new int[bins];
            foreach (double v in values)
            {
                double t = (Math.Max(Lo, Math.Min(Hi, v)) - Lo) / (Hi - Lo);
                int idx = (int)(t * bins);
                if (idx >= bins) idx = bins - 1;
                if (idx < 0) idx = 0;
                counts[idx]++;
            }
            return counts;
        }

        private void DrawVerticalText(DrawingContext dc, string s, Brush brush, double size, Point at)
        {
            FormattedText ft = Text(s, brush, size);
            dc.PushTransform(new RotateTransform(-90, at.X, at.Y));
            dc.DrawText(ft, at);
            dc.Pop();
        }
    }

    /// <summary>
    /// The UpSet-style set-novelty view of the three de novo sources by clean sequence (de novo only, de
    /// novo all, DeepNovo). Two header stat cards carry the deltas; below, a bar per non-empty intersection
    /// region (biggest first) sits above a three-row dot matrix that says which sets each region belongs to,
    /// and set-size bars run down the left. Each region bar is annotated with its count and median
    /// confidence. Pure sequence bookkeeping: no XIC.
    /// </summary>
    public sealed class SetNoveltyChart : TalkChartBase
    {
        private readonly SetNovelty set;

        public SetNoveltyChart(SetNovelty set)
        {
            this.set = set ?? throw new ArgumentNullException(nameof(set));
            this.set.Validate();
        }

        private Brush RegionColour(SetNoveltyRegion r)
        {
            if (r.InDeepNovo && !r.InDeNovoAll && !r.InDeNovoOnly) return AccentAmber; // DeepNovo unique
            if (r.InDeNovoOnly && r.InDeNovoAll && r.InDeepNovo) return DeepGreen;      // all three
            if (r.InDeNovoOnly && r.InDeNovoAll && !r.InDeepNovo) return ContextGray;   // shared PEAKS core
            if (r.InDeNovoAll && !r.InDeNovoOnly) return DeepBlue;                      // added / all+deep
            return ContextGray;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Palette.Backdrop, null, new Rect(0, 0, ActualWidth, ActualHeight));
            DrawText(dc, "What each de novo source adds: set-novelty view of the peptides",
                Palette.Ink, 19, new Point(24, 16), true);
            DrawText(dc, "Three replicates pooled; peptides matched by clean sequence (mods stripped, I folded " +
                "to L); confidence collapsed to the max per sequence.", Palette.Muted, 11, new Point(24, 44));
            if (ActualWidth < 600 || ActualHeight < 300) return;

            // ---- two header stat cards ----------------------------------------------------------------
            StatCard(dc, new Rect(24, 70, 388, 46), Translucent(DeepBlue, 0.10), DeepBlue,
                "+" + N(set.AddedByFullExportOverOnly),
                "sequences the full de novo export adds",
                "over the database-unassigned only subset");
            StatCard(dc, new Rect(424, 70, 470, 46), Translucent(AccentAmber, 0.12), AccentAmber,
                "+" + N(set.DeepNovoUnique),
                "DeepNovo sequences PEAKS de novo never reports",
                DeepNovoSharedNote());

            // ---- legend (top-right) -------------------------------------------------------------------
            double lx = ActualWidth - 340, lyTop = 128;
            LegendRow(dc, lx, lyTop, ContextGray, "Shared PEAKS core (only and all)");
            LegendRow(dc, lx, lyTop + 20, DeepBlue, "Added by the full export over only");
            LegendRow(dc, lx, lyTop + 40, DeepGreen, "Reported by all three sources");
            LegendRow(dc, lx, lyTop + 60, AccentAmber, "DeepNovo unique (never in PEAKS de novo)");

            // ---- geometry -----------------------------------------------------------------------------
            double matrixRowH = 26;
            double row0 = ActualHeight - 40 - matrixRowH * 3;   // top of the 3-row matrix
            double[] rowY = { row0 + matrixRowH * 0.5, row0 + matrixRowH * 1.5, row0 + matrixRowH * 2.5 };

            double colLeft = 470, colRight = ActualWidth - 50;
            int n = set.Regions.Count;
            double colStep = n > 0 ? (colRight - colLeft) / n : 0;

            double barsBase = row0 - 16;
            double barsTop = 150;
            int maxCount = set.Regions.Count > 0 ? set.Regions.Max(r => r.Count) : 1;
            (double axMax, double axStep) = NiceAxis(maxCount * 1.02);

            // count-axis gridlines + labels (labels right-aligned into the gutter left of the bars)
            var gridPen = new Pen(Palette.Grid, 0.8);
            for (double v = 0; v <= axMax + 1e-6; v += axStep)
            {
                double y = barsBase - (barsBase - barsTop) * v / axMax;
                dc.DrawLine(gridPen, new Point(colLeft - 8, y), new Point(colRight, y));
                FormattedText t = Text(N((int)Math.Round(v)), Palette.Muted, 8.5);
                dc.DrawText(t, new Point(colLeft - 16 - t.Width, y - 7));
            }
            DrawVerticalText(dc, "Clean sequences in region (count)", Palette.Muted, 10,
                new Point(colLeft - 96, barsBase));

            // ---- the region bars + dot matrix ---------------------------------------------------------
            for (int i = 0; i < n; i++)
            {
                SetNoveltyRegion r = set.Regions[i];
                double cx = colLeft + colStep * (i + 0.5);
                double bw = Math.Min(64, colStep * 0.5);
                double h = (barsBase - barsTop) * r.Count / axMax;
                Brush colour = RegionColour(r);
                dc.DrawRectangle(colour, null, new Rect(cx - bw / 2, barsBase - h, bw, h));

                // count and median above the bar
                FormattedText cnt = Text(N(r.Count), Palette.Ink, 12, true);
                dc.DrawText(cnt, new Point(cx - cnt.Width / 2, barsBase - h - cnt.Height - 2));
                string med = "med " + r.ConfidenceLabel + " " +
                    r.MedianConfidence.ToString("0", CultureInfo.InvariantCulture) + "%";
                FormattedText mt = Text(med, Palette.Muted, 9.5);
                dc.DrawText(mt, new Point(cx - mt.Width / 2, barsBase - h - cnt.Height - mt.Height - 3));

                // dot column
                bool[] mem = { r.InDeNovoOnly, r.InDeNovoAll, r.InDeepNovo };
                double firstOn = -1, lastOn = -1;
                for (int k = 0; k < 3; k++) if (mem[k]) { if (firstOn < 0) firstOn = rowY[k]; lastOn = rowY[k]; }
                if (firstOn >= 0 && lastOn > firstOn)
                    dc.DrawLine(new Pen(colour, 3), new Point(cx, firstOn), new Point(cx, lastOn));
                for (int k = 0; k < 3; k++)
                    dc.DrawEllipse(mem[k] ? colour : Palette.Grid, null, new Point(cx, rowY[k]), 6.5, 6.5);
            }

            // ---- row backgrounds, labels and set-size bars --------------------------------------------
            string[] rowNames = { "PEAKS de novo ONLY", "PEAKS de novo ALL", "DeepNovo" };
            Brush[] rowNameBrush = { ContextGray, DeepBlue, DeepGreen };
            int[] rowSizes = { set.DeNovoOnlySize, set.DeNovoAllSize, set.DeepNovoSize };
            int maxSize = Math.Max(1, rowSizes.Max());
            // Three clean zones left of the dot matrix: the size bar (grows right from xBar0), the size
            // number (right-aligned into numRight), then the bold row label (right-aligned just left of the
            // first dot column). They do not overlap because each has its own horizontal band.
            double xBar0 = 44, sizeBarMax = 150, numRight = 250;
            for (int k = 0; k < 3; k++)
            {
                if (k % 2 == 1)
                    dc.DrawRectangle(Translucent(ContextGray, 0.06), null,
                        new Rect(colLeft - 8, rowY[k] - matrixRowH / 2, colRight - colLeft + 8, matrixRowH));

                double w = sizeBarMax * rowSizes[k] / maxSize;
                dc.DrawRectangle(rowNameBrush[k], null, new Rect(xBar0, rowY[k] - 6, w, 12));
                FormattedText num = Text(N(rowSizes[k]), Palette.Ink, 9.5, true);
                dc.DrawText(num, new Point(numRight - num.Width, rowY[k] - num.Height / 2));

                FormattedText name = Text(rowNames[k], rowNameBrush[k], 12, true);
                dc.DrawText(name, new Point(colLeft - 22 - name.Width, rowY[k] - name.Height / 2));
            }
            DrawText(dc, "Set size (sequences)", Palette.Muted, 9.5,
                new Point(xBar0, rowY[2] + 22));
        }

        private string DeepNovoSharedNote()
        {
            int sharedWithAll = set.DeepNovoSize - set.DeepNovoUnique;
            return "(" + N(sharedWithAll) + " more are shared with PEAKS de novo all)";
        }

        private void StatCard(DrawingContext dc, Rect box, Brush fill, Brush border,
                              string big, string line1, string line2)
        {
            dc.DrawRoundedRectangle(fill, new Pen(border, 1.4), box, 6, 6);
            FormattedText b = Text(big, border, 22, true);
            dc.DrawText(b, new Point(box.Left + 14, box.Top + (box.Height - b.Height) / 2));
            double tx = box.Left + 14 + b.Width + 14;
            DrawText(dc, line1, Palette.Ink, 10.5, new Point(tx, box.Top + 8));
            DrawText(dc, line2, Palette.Muted, 10, new Point(tx, box.Top + 24));
        }

        private void LegendRow(DrawingContext dc, double x, double y, Brush swatch, string label)
        {
            dc.DrawRectangle(swatch, null, new Rect(x, y, 14, 14));
            DrawText(dc, label, Palette.Ink, 10.5, new Point(x + 20, y - 2));
        }

        private void DrawVerticalText(DrawingContext dc, string s, Brush brush, double size, Point at)
        {
            FormattedText ft = Text(s, brush, size);
            dc.PushTransform(new RotateTransform(-90, at.X, at.Y));
            dc.DrawText(ft, at);
            dc.Pop();
        }

        private static (double Max, double Step) NiceAxis(double dataMax, int targetTicks = 8)
        {
            if (dataMax <= 0) return (1, 1);
            double rough = dataMax / targetTicks;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double norm = rough / magnitude;
            double niceStep = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10) * magnitude;
            double max = Math.Ceiling(dataMax / niceStep) * niceStep;
            return (max, niceStep);
        }
    }

    public sealed class SignalByRunChart : TalkChartBase
    {
        private readonly List<NativeRunFraction> rows;
        public SignalByRunChart(IEnumerable<NativeRunFraction> rows) =>
            this.rows = (rows ?? Enumerable.Empty<NativeRunFraction>()).Where(r => !r.IsGrouped).ToList();

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "Assigned signal by technical run",
                "Identified observed envelope plus the measured envelope for de novo peptides, each divided by run TIC.");
            double left = 80, top = 100, bottom = ActualHeight - 80, width = ActualWidth - 120;
            double max = 0.50, groupW = width / Math.Max(1, rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                NativeRunFraction r = rows[i];
                double idH = (bottom - top) * r.IdentifiedFractionEnvelope / max;
                double dnH = (bottom - top) * r.DeNovoFractionReferenceCalibrated / max;
                double x = left + i * groupW + groupW * 0.18, w = groupW * 0.56;
                Bar(dc, new Rect(x, bottom - idH, w, idH), Palette.Identified, P(r.IdentifiedFractionEnvelope));
                Bar(dc, new Rect(x, bottom - idH - dnH, w, dnH), Palette.DeNovo, P(r.DeNovoFractionReferenceCalibrated));
                DrawText(dc, "Run " + (i + 1), Palette.Ink, 11, new Point(x + w * 0.28, bottom + 10), true);
                DrawText(dc, "total " + P(r.RecoveredFractionReferenceCalibrated), Palette.Ink, 10,
                    new Point(x, bottom - idH - dnH - 24), true);
            }
            DrawText(dc, "Search-engine signal", Palette.Identified, 11, new Point(left, ActualHeight - 42), true);
            DrawText(dc, "De novo addition", Palette.DeNovo, 11, new Point(left + 165, ActualHeight - 42), true);
        }
    }

    public sealed class SignalUpliftChart : TalkChartBase
    {
        private readonly NativeRunFraction? grouped;
        public SignalUpliftChart(NativeRunFraction? grouped) => this.grouped = grouped;

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "Pooled value of de novo sequencing",
                "Both bars use the same pooled TIC denominator and the same envelope basis.");
            if (grouped == null) return;
            double[] values = { grouped.IdentifiedFractionEnvelope, grouped.RecoveredFractionReferenceCalibrated };
            string[] labels = { "Search engine only", "Search + de novo" };
            Brush[] fills = { Palette.Identified, Palette.Quantified };
            double max = 0.50, left = 190, top = 105, bottom = ActualHeight - 105, width = ActualWidth - 380;
            double groupW = width / 2;
            for (int i = 0; i < 2; i++)
            {
                double h = (bottom - top) * values[i] / max;
                double x = left + i * groupW + groupW * 0.25, w = groupW * 0.50;
                Bar(dc, new Rect(x, bottom - h, w, h), fills[i], P(values[i]));
                DrawText(dc, labels[i], Palette.Ink, 12, new Point(x - 18, bottom + 12), true);
            }
            double relative = grouped.IdentifiedFractionEnvelope > 0
                ? grouped.DeNovoFractionReferenceCalibrated / grouped.IdentifiedFractionEnvelope : 0;
            DrawText(dc, "+" + P(relative) + " relative uplift", Palette.DeNovo, 22,
                new Point(ActualWidth * 0.67, 110), true);
            DrawText(dc, "+" + (grouped.DeNovoFractionReferenceCalibrated * 100).ToString("F2", CultureInfo.CurrentCulture) +
                " percentage points", Palette.Muted, 12, new Point(ActualWidth * 0.67, 150));
        }
    }

    public sealed class ReferenceComponentsChart : TalkChartBase
    {
        private readonly ReferenceComponentSummary s;
        private readonly int identity;
        public ReferenceComponentsChart(ReferenceComponentSummary s, int identity) { this.s = s; this.identity = identity; }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "Reference-database components at " + identity + "% identity",
                "Database bookkeeping, not peptide identifications, detected proteins, or abundance. Categories can overlap by definition.");
            var rows = new (string Label, int Value, Brush Fill)[]
            {
                ("All connected components", s.AllComponents, Palette.Tic),
                ("Pure-decoy control components", s.PureDecoyComponents, Palette.Mixing),
                ("Target-bearing components", s.TargetBearingComponents, Palette.Identified),
                ("Contaminant-bearing target components", s.ContaminantBearingTargetComponents, Palette.PeptideLikeUnidentified),
                ("Non-contaminant target components", s.NonContaminantTargetComponents, Palette.DeNovo),
            };
            double left = 275, top = 105, rowH = 70, maxW = ActualWidth - left - 100;
            foreach (var r in rows)
            {
                DrawText(dc, r.Label, Palette.Ink, 11, new Point(28, top + 9));
                Bar(dc, new Rect(left, top, maxW * r.Value / Math.Max(1.0, s.AllComponents), 34), r.Fill, N(r.Value));
                top += rowH;
            }
            DrawText(dc, N(s.MixedTargetDecoyComponents) + " component(s) contain both target and decoy entries.",
                Palette.Muted, 10, new Point(28, ActualHeight - 48));
        }
    }

    public sealed class FamilySignalShareChart : TalkChartBase
    {
        private readonly List<FamilyEnvelopeQuantRow> rows;
        private readonly HashSet<int> labelledFamilyIds;
        private IReadOnlyList<Rect> lastLabelBounds = Array.Empty<Rect>();
        public IReadOnlyList<FamilyEnvelopeQuantRow> Rows => rows; // U3c exact native scatter inputs.
        public IReadOnlyDictionary<int, string> FamilyLabels { get; }
        public static (double Signal, double DeNovoShare) Value(FamilyEnvelopeQuantRow row) =>
            (Math.Max(0, row.TotalEnvelopeReferenceCalibrated), row.TotalEnvelopeReferenceCalibrated > 0
                ? row.DeNovoEnvelopePrimary / row.TotalEnvelopeReferenceCalibrated : 0);

        /// <summary>The most recently rendered label rectangles, exposed so the headless chart test can
        /// guard against a future return of clipped or overlapping family labels.</summary>
        public IReadOnlyList<Rect> LastLabelBounds => lastLabelBounds;

        /// <summary>Number of labels whose most recent placement required a leader line.</summary>
        public int LastLabelLeaderCount { get; private set; }

        public FamilySignalShareChart(FamilyEnvelopeQuantResult families, int labelTopN, IReadOnlyDictionary<int, string>? familyLabels = null,
            IReadOnlySet<int>? contaminantFamilyIds = null)
        {
            if (families == null) throw new ArgumentNullException(nameof(families));
            rows = families.Families.Where(f => contaminantFamilyIds?.Contains(f.FamilyUserId) != true).OrderBy(f => f.FamilyUserId).ToList();
            FamilyLabels = familyLabels ?? new Dictionary<int, string>();
            labelledFamilyIds = rows.OrderByDescending(f => f.TotalEnvelopeReferenceCalibrated)
                .ThenBy(f => f.FamilyUserId).Take(Math.Max(0, labelTopN))
                .Select(f => f.FamilyUserId).ToHashSet();
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "De novo contribution versus total attributed family signal",
                "Each point is one of " + N(rows.Count) +
                " families with attributed signal; labels mark the ten strongest. X = identified signal + de novo measured envelope; Y = de novo share.");
            if (rows.Count == 0) return;
            Rect plot = new Rect(96, 112, ActualWidth - 148, ActualHeight - 198);
            double dataMaxX = rows.Max(r => Math.Max(0, r.TotalEnvelopeReferenceCalibrated)) / 1e9;
            double maxX = NiceUpper(dataMaxX <= 0 ? 1 : dataMaxX, 5);
            double dataMaxY = rows.Max(r => r.TotalEnvelopeReferenceCalibrated > 0
                ? r.DeNovoEnvelopePrimary / r.TotalEnvelopeReferenceCalibrated : 0);
            double maxY = Math.Min(1.0, Math.Max(0.30, Math.Ceiling(dataMaxY * 10.0) / 10.0));
            var grid = new Pen(Palette.Grid, 0.7);
            var axis = new Pen(Palette.Axis, 1.2);
            const int ticks = 5;
            for (int i = 0; i <= ticks; i++)
            {
                double fraction = (double)i / ticks;
                double x = plot.Left + plot.Width * fraction;
                double y = plot.Bottom - plot.Height * fraction;
                dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
                dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawText(dc, (maxX * fraction).ToString("0.#", CultureInfo.InvariantCulture),
                    Palette.Muted, 8.5, new Point(x - 11, plot.Bottom + 8));
                DrawText(dc, (100 * maxY * fraction).ToString("0", CultureInfo.InvariantCulture) + "%",
                    Palette.Muted, 8.5, new Point(plot.Left - 40, y - 7));
            }
            dc.DrawLine(axis, plot.BottomLeft, plot.TopLeft);
            dc.DrawLine(axis, plot.BottomLeft, plot.BottomRight);

            var positions = new Dictionary<int, Point>();
            foreach (FamilyEnvelopeQuantRow r in rows)
            {
                var value = Value(r);
                double share = value.DeNovoShare;
                double xValue = value.Signal / 1e9;
                double x = plot.Left + plot.Width * xValue / maxX;
                double y = plot.Bottom - plot.Height * share / maxY;
                Brush fill = share >= 0.20 ? Palette.Mixing : Palette.DeNovo;
                dc.DrawEllipse(fill, new Pen(Palette.Backdrop, 0.7), new Point(x, y), 3.5, 3.5);
                positions[r.FamilyUserId] = new Point(x, y);
            }
            var labelSpecs = rows.Where(r => labelledFamilyIds.Contains(r.FamilyUserId))
                .OrderByDescending(r => r.TotalEnvelopeReferenceCalibrated)
                .ThenBy(r => r.FamilyUserId)
                .Select(r => new FamilyLabelSpec(
                    positions[r.FamilyUserId],
                    Text(ShortName(FamilyLabels.GetValueOrDefault(r.FamilyUserId, r.Description), r.FamilyUserId), Palette.Ink, 8.5, true)))
                .ToList();
            List<FamilyLabelPlacement> labels = PlaceLabels(labelSpecs, plot);
            lastLabelBounds = labels.Select(label => label.Bounds).ToArray();
            LastLabelLeaderCount = labels.Count(label => label.HasLeader);

            var leaderPen = new Pen(Palette.Axis, 0.75);
            foreach (FamilyLabelPlacement label in labels.Where(label => label.HasLeader))
            {
                dc.DrawLine(leaderPen, label.Marker, NearestPoint(label.Bounds, label.Marker));
            }
            foreach (FamilyLabelPlacement label in labels)
            {
                // A small opaque ground keeps grid lines and other leaders from cutting through text.
                dc.DrawRoundedRectangle(Palette.Backdrop, null, label.Bounds, 1.5, 1.5);
                dc.DrawText(label.Text, label.Bounds.TopLeft);
            }
            DrawText(dc, "Total attributed family signal (×10⁹ intensity·min)", Palette.Muted, 10,
                new Point(plot.Left + plot.Width * 0.30, plot.Bottom + 36));
            DrawText(dc, "De novo share of family signal (%)", Palette.Muted, 10,
                new Point(12, plot.Top - 24));
            DrawText(dc, "Orange: ≥20% de novo contribution", Palette.Mixing, 10,
                new Point(ActualWidth - 275, ActualHeight - 42), true);
        }

        private static double NiceUpper(double value, int targetTicks)
        {
            double rough = value / Math.Max(1, targetTicks);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(rough, 1e-12))));
            double normalized = rough / magnitude;
            double step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 :
                normalized <= 2.5 ? 2.5 : normalized <= 5 ? 5 : 10) * magnitude;
            return Math.Ceiling(value / step) * step;
        }

        private static List<FamilyLabelPlacement> PlaceLabels(
            IReadOnlyList<FamilyLabelSpec> labels, Rect plot)
        {
            const double padding = 2.0;
            Rect safe = new Rect(plot.Left + padding, plot.Top + padding,
                Math.Max(1, plot.Width - 2 * padding), Math.Max(1, plot.Height - 2 * padding));
            var occupied = new List<Rect>();
            var markerKeepouts = labels.Select(label => new Rect(
                label.Marker.X - 5, label.Marker.Y - 5, 10, 10)).ToList();
            var result = new List<FamilyLabelPlacement>(labels.Count);

            for (int labelIndex = 0; labelIndex < labels.Count; labelIndex++)
            {
                FamilyLabelSpec label = labels[labelIndex];
                List<Rect> candidates = CandidateRects(label.Marker, label.Text.Width,
                        label.Text.Height, safe)
                    .Select(candidate => Clamp(candidate, safe))
                    .Where(candidate => candidate.Width > 0 && candidate.Height > 0)
                    .Distinct(RectCoordinateComparer.Instance)
                    .ToList();

                List<Rect> collisionFree = candidates
                    .Where(candidate => !occupied.Any(previous =>
                        Inflated(previous, padding).IntersectsWith(Inflated(candidate, padding))))
                    .OrderBy(candidate => MarkerOverlapCount(candidate, markerKeepouts, labelIndex))
                    .ThenBy(candidate => PlacementDistance(candidate, label.Marker))
                    .ThenBy(candidate => candidate.Left < label.Marker.X ? 1 : 0)
                    .ThenBy(candidate => candidate.Top)
                    .ThenBy(candidate => candidate.Left)
                    .ToList();

                Rect chosen;
                if (collisionFree.Count > 0)
                {
                    chosen = collisionFree[0];
                }
                else
                {
                    // The plot is intentionally roomy enough for ten concise labels. This fallback is
                    // deterministic if a future font or unusually long locale string exhausts it.
                    chosen = candidates
                        .OrderBy(candidate => occupied.Sum(previous =>
                            IntersectionArea(Inflated(previous, padding), Inflated(candidate, padding))))
                        .ThenBy(candidate => MarkerOverlapCount(candidate, markerKeepouts, labelIndex))
                        .ThenBy(candidate => PlacementDistance(candidate, label.Marker))
                        .ThenBy(candidate => candidate.Top)
                        .ThenBy(candidate => candidate.Left)
                        .First();
                }

                double leaderDistance = Distance(label.Marker, NearestPoint(chosen, label.Marker));
                result.Add(new FamilyLabelPlacement(label.Marker, label.Text, chosen,
                    leaderDistance > 11.0));
                occupied.Add(chosen);
            }

            return result;
        }

        private static IEnumerable<Rect> CandidateRects(Point marker, double width, double height,
                                                          Rect safe)
        {
            const double gap = 8.0;
            yield return new Rect(marker.X + gap, marker.Y - height - 3, width, height);
            yield return new Rect(marker.X + gap, marker.Y + 3, width, height);
            yield return new Rect(marker.X - width - gap, marker.Y - height - 3, width, height);
            yield return new Rect(marker.X - width - gap, marker.Y + 3, width, height);
            yield return new Rect(marker.X + gap, marker.Y - height / 2, width, height);
            yield return new Rect(marker.X - width - gap, marker.Y - height / 2, width, height);
            yield return new Rect(marker.X - width / 2, marker.Y - height - gap, width, height);
            yield return new Rect(marker.X - width / 2, marker.Y + gap, width, height);

            // Nearby vertical lanes resolve dense point clouds without moving a label across the plot.
            for (int ring = 1; ring <= 5; ring++)
            {
                double dy = ring * (height + 4);
                yield return new Rect(marker.X + gap, marker.Y - height / 2 - dy, width, height);
                yield return new Rect(marker.X + gap, marker.Y - height / 2 + dy, width, height);
                yield return new Rect(marker.X - width - gap, marker.Y - height / 2 - dy, width, height);
                yield return new Rect(marker.X - width - gap, marker.Y - height / 2 + dy, width, height);
            }

            // Rail candidates are the guaranteed collision-free fallback for ten labels sharing nearly
            // the same point. Sorting later still selects the shortest available leader deterministically.
            double verticalStep = Math.Max(height + 4, 12);
            int slots = Math.Max(1, (int)Math.Floor((safe.Height - height) / verticalStep) + 1);
            for (int slot = 0; slot < slots; slot++)
            {
                double y = Math.Min(safe.Bottom - height, safe.Top + slot * verticalStep);
                yield return new Rect(marker.X + 14, y, width, height);
                yield return new Rect(marker.X - width - 14, y, width, height);
                yield return new Rect(safe.Left, y, width, height);
                yield return new Rect(safe.Right - width, y, width, height);
            }
        }

        private static Rect Clamp(Rect value, Rect bounds)
        {
            double width = Math.Min(value.Width, bounds.Width);
            double height = Math.Min(value.Height, bounds.Height);
            double x = Math.Max(bounds.Left, Math.Min(value.Left, bounds.Right - width));
            double y = Math.Max(bounds.Top, Math.Min(value.Top, bounds.Bottom - height));
            return new Rect(x, y, width, height);
        }

        private static Rect Inflated(Rect value, double amount)
        {
            Rect copy = value;
            copy.Inflate(amount, amount);
            return copy;
        }

        private static int MarkerOverlapCount(Rect candidate, IReadOnlyList<Rect> markers, int own)
        {
            int count = 0;
            for (int i = 0; i < markers.Count; i++)
            {
                if (i != own && candidate.IntersectsWith(markers[i])) count++;
            }
            return count;
        }

        private static double PlacementDistance(Rect label, Point marker)
        {
            Point nearest = NearestPoint(label, marker);
            double distance = Distance(marker, nearest);
            // Prefer a similar vertical level when two candidates have the same leader length.
            return distance + Math.Abs(label.Top + label.Height / 2 - marker.Y) * 0.03;
        }

        private static Point NearestPoint(Rect rect, Point point) => new Point(
            Math.Max(rect.Left, Math.Min(point.X, rect.Right)),
            Math.Max(rect.Top, Math.Min(point.Y, rect.Bottom)));

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double IntersectionArea(Rect a, Rect b)
        {
            double width = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left));
            double height = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
            return width * height;
        }

        private sealed class FamilyLabelSpec
        {
            public FamilyLabelSpec(Point marker, FormattedText text)
            {
                Marker = marker;
                Text = text;
            }

            public Point Marker { get; }
            public FormattedText Text { get; }
        }

        private sealed class FamilyLabelPlacement
        {
            public FamilyLabelPlacement(Point marker, FormattedText text, Rect bounds, bool hasLeader)
            {
                Marker = marker;
                Text = text;
                Bounds = bounds;
                HasLeader = hasLeader;
            }

            public Point Marker { get; }
            public FormattedText Text { get; }
            public Rect Bounds { get; }
            public bool HasLeader { get; }
        }

        private sealed class RectCoordinateComparer : IEqualityComparer<Rect>
        {
            public static RectCoordinateComparer Instance { get; } = new RectCoordinateComparer();

            public bool Equals(Rect a, Rect b) =>
                Math.Abs(a.Left - b.Left) < 0.01 && Math.Abs(a.Top - b.Top) < 0.01
                && Math.Abs(a.Width - b.Width) < 0.01 && Math.Abs(a.Height - b.Height) < 0.01;

            public int GetHashCode(Rect value) => HashCode.Combine(
                Math.Round(value.Left, 2), Math.Round(value.Top, 2),
                Math.Round(value.Width, 2), Math.Round(value.Height, 2));
        }

        private static string ShortName(string? description, int id)
            => FamilyFigureLabel.Format(id, description, maximumNameLength: 24);
    }

    /// <summary>
    /// Top-family sequence diversity from the exact PEAKS de-novo-only + DeepNovo project. The three
    /// coloured segments are disjoint exact sequence sets, so each stacked total is the de novo union;
    /// the search sequence count is context and is deliberately not stacked into that union.
    /// </summary>
    public sealed class FamilySourceSequenceChart : TalkChartBase
    {
        private readonly FamilySourceSequenceSummary summary;
        private readonly int topN;

        public FamilySourceSequenceChart(FamilySourceSequenceSummary summary, int topN = 8)
        {
            this.summary = summary ?? throw new ArgumentNullException(nameof(summary));
            this.topN = Math.Max(1, topN);
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "De novo sequence evidence across families",
                "Pooled PTM-cleaned sequences; PEAKS-only, shared and DeepNovo-only sets are disjoint. Search is separate context.");
            if (ActualWidth < 700 || ActualHeight < 360) return;

            List<FamilySourceSequenceRow> rows = summary.Families
                .Where(r => r.DeNovoUnionDistinctSequences > 0)
                .OrderByDescending(r => r.DeNovoUnionDistinctSequences)
                .ThenBy(r => r.FamilyUserId).Take(topN).ToList();
            if (rows.Count == 0)
            {
                DrawText(dc, "No source-resolved PEAKS de-novo-only / DeepNovo family sequences are stored in this project.",
                    Palette.Muted, 13, new Point(70, 180));
                DrawText(dc, "Recompute or load the exact only+DeepNovo variant to populate this figure.",
                    Palette.Ink, 11, new Point(70, 214), true);
                return;
            }

            double left = 230, right = ActualWidth - 78, top = 112;
            double rowH = Math.Min(43, (ActualHeight - top - 92) / rows.Count);
            int max = Math.Max(1, rows.Max(r => r.DeNovoUnionDistinctSequences));
            double usable = right - left;

            for (int i = 0; i < rows.Count; i++)
            {
                FamilySourceSequenceRow row = rows[i];
                double y = top + i * rowH;
                string label = FamilyFigureLabel.Format(row.FamilyUserId, row.Description, 24);
                FormattedText name = Text(label, Palette.Ink, 8.8, i < 3);
                dc.DrawText(name, new Point(left - name.Width - 12, y + 7));

                double peaksW = usable * row.PeaksDeNovoOnlyExclusiveDistinctSequences / max;
                double sharedW = usable * row.SharedPeaksDeepNovoDistinctSequences / max;
                double deepW = usable * row.DeepNovoExclusiveDistinctSequences / max;
                double x = left;
                if (peaksW > 0)
                    dc.DrawRectangle(DeepBlue, null, new Rect(x, y, peaksW, 25));
                x += peaksW;
                if (sharedW > 0)
                    dc.DrawRectangle(AccentAmber, null, new Rect(x, y, sharedW, 25));
                x += sharedW;
                if (deepW > 0)
                    dc.DrawRectangle(DeepGreen, null, new Rect(x, y, deepW, 25));

                DrawText(dc, N(row.DeNovoUnionDistinctSequences), Palette.Ink, 8.8,
                    new Point(x + deepW + 6, y + 5), true);
                DrawText(dc, "search " + N(row.SearchDistinctSequences), ContextGray, 7.7,
                    new Point(left, y + 27));
            }

            double legendY = ActualHeight - 58;
            var legend = new[]
            {
                (DeepBlue, "PEAKS de-novo-only exclusive"),
                (AccentAmber, "shared exact sequence"),
                (DeepGreen, "DeepNovo exclusive"),
            };
            double legendX = left;
            foreach ((Brush brush, string label) in legend)
            {
                dc.DrawRectangle(brush, null, new Rect(legendX, legendY, 12, 12));
                DrawText(dc, label, Palette.Ink, 8.1, new Point(legendX + 18, legendY - 3));
                legendX += Text(label, Palette.Ink, 8.1).Width + 48;
            }
            DrawText(dc, "Family mapping identity >= " +
                summary.MappingMinimumIdentity.ToString("P0", CultureInfo.CurrentCulture) +
                "; one sequence may map to more than one family, so rows are non-additive.",
                Palette.Muted, 8.1, new Point(34, ActualHeight - 28));
        }
    }

    /// <summary>
    /// A simplified, two-metric talk figure that answers only what each caller changes. The left panel
    /// uses identification-only distinct sequences; the right uses one common observed search baseline
    /// and labels the added de novo signal as a model estimate. No isotope mechanics are exposed here.
    /// </summary>
    public sealed class DeNovoTalkComparisonChart : TalkChartBase
    {
        private readonly List<DeNovoTalkComparisonRow> rows;
        public IReadOnlyList<DeNovoTalkComparisonRow> Rows => rows; // U3d native drawing inputs.

        public DeNovoTalkComparisonChart(IEnumerable<DeNovoTalkComparisonRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            this.rows = rows.ToList();
            if (this.rows.Count != 4)
                throw new ArgumentException(
                    "The compact talk comparison requires search plus exactly three de novo conditions.",
                    nameof(rows));
            foreach (DeNovoTalkComparisonRow row in this.rows) row.Validate();
            if (!string.Equals(this.rows[0].Condition, "Search only", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The first compact comparison row must be the search baseline.",
                    nameof(rows));
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "What PEAKS de novo-only and DeepNovo add",
                "Left: carried-forward distinct sequences (no XIC). Right: assigned share of the same pooled TIC; the de novo addition uses its measured envelope.");
            if (ActualWidth < 700 || ActualHeight < 360) return;

            List<DeNovoTalkComparisonRow> arms = rows.Skip(1).ToList();
            double left = 38, top = 112, panelGap = 34;
            double panelW = (ActualWidth - 2 * left - panelGap) / 2.0;
            double panelH = ActualHeight - top - 74;
            Rect sequencePanel = new Rect(left, top, panelW, panelH);
            Rect signalPanel = new Rect(left + panelW + panelGap, top, panelW, panelH);
            dc.DrawRoundedRectangle(Palette.Panel, new Pen(Palette.Grid, 0.8), sequencePanel, 8, 8);
            dc.DrawRoundedRectangle(Palette.Panel, new Pen(Palette.Grid, 0.8), signalPanel, 8, 8);

            DrawText(dc, "CARRIED-FORWARD DISTINCT SEQUENCES", Palette.Identified, 10,
                new Point(sequencePanel.Left + 18, sequencePanel.Top + 16), true);
            DrawText(dc, "PTM notation removed · runs and charge pooled · I/L distinct",
                Palette.Muted, 8.3, new Point(sequencePanel.Left + 18, sequencePanel.Top + 38));

            int maxSequences = Math.Max(1, arms.Max(r => r.CarriedForwardDistinctSequences));
            double labelW = 136, sequenceBarW = sequencePanel.Width - labelW - 44;
            for (int i = 0; i < arms.Count; i++)
            {
                DeNovoTalkComparisonRow row = arms[i];
                double y = sequencePanel.Top + 82 + i * 78;
                string label = i == 0 ? "PEAKS de novo-only" : i == 1 ? "DeepNovo" : "Both callers";
                DrawText(dc, label, Palette.Ink, 9.2,
                    new Point(sequencePanel.Left + 18, y + 10), i == 2);
                double width = sequenceBarW * row.CarriedForwardDistinctSequences / maxSequences;
                Brush fill = i == 0 ? DeepBlue : i == 1 ? DeepGreen : AccentAmber;
                Bar(dc, new Rect(sequencePanel.Left + labelW, y, width, 34), fill,
                    N(row.CarriedForwardDistinctSequences));
            }
            DrawText(dc, "These counts are sequence diversity, not signal intensity.", Palette.Muted, 8.5,
                new Point(sequencePanel.Left + 18, sequencePanel.Bottom - 32));

            DrawText(dc, "ASSIGNED SHARE OF POOLED TIC", Palette.Identified, 10,
                new Point(signalPanel.Left + 18, signalPanel.Top + 16), true);
            DrawText(dc, "Observed search baseline + measured de novo envelope",
                Palette.Muted, 8.3, new Point(signalPanel.Left + 18, signalPanel.Top + 38));

            double maxSignal = Math.Max(0.01, rows.Max(r => r.CombinedAssignedFractionOfTic) * 1.12);
            double signalLabelW = 102, signalBarW = signalPanel.Width - signalLabelW - 48;
            for (int i = 0; i < rows.Count; i++)
            {
                DeNovoTalkComparisonRow row = rows[i];
                double y = signalPanel.Top + 72 + i * 62;
                string label = i == 0 ? "Search only" : i == 1 ? "PEAKS only" :
                    i == 2 ? "DeepNovo" : "Both callers";
                DrawText(dc, label, Palette.Ink, 9,
                    new Point(signalPanel.Left + 18, y + 9), i == 3);
                double x = signalPanel.Left + signalLabelW;
                double searchWidth = signalBarW * row.ObservedSearchAssignedFractionOfTic / maxSignal;
                double deNovoWidth = signalBarW * row.DeNovoModelEstimateFractionOfTic / maxSignal;
                dc.DrawRoundedRectangle(Palette.Identified, new Pen(Palette.Grid, 0.5),
                    new Rect(x, y, searchWidth, 30), 2, 2);
                if (deNovoWidth > 0)
                    dc.DrawRoundedRectangle(Palette.DeNovo, new Pen(Palette.Grid, 0.5),
                        new Rect(x + searchWidth, y, deNovoWidth, 30), 2, 2);
                DrawText(dc, P(row.CombinedAssignedFractionOfTic), Palette.Ink, 9.5,
                    new Point(x + searchWidth + deNovoWidth + 6, y + 7), true);
            }

            double legendY = signalPanel.Bottom - 42;
            dc.DrawRectangle(Palette.Identified, null,
                new Rect(signalPanel.Left + 18, legendY, 12, 12));
            DrawText(dc, "observed search", Palette.Ink, 8.3,
                new Point(signalPanel.Left + 36, legendY - 3));
            dc.DrawRectangle(Palette.DeNovo, null,
                new Rect(signalPanel.Left + 150, legendY, 12, 12));
            DrawText(dc, "de novo measured envelope", Palette.Ink, 8.3,
                new Point(signalPanel.Left + 168, legendY - 3));

            DrawText(dc,
                "The exact union is PEAKS de-novo-only + DeepNovo; PEAKS-all + DeepNovo is a separate variant and is not substituted here.",
                Palette.Muted, 8.5, new Point(left, ActualHeight - 36));
        }
    }

    /// <summary>
    /// Fully processed de novo-source variants on the one comparable quantity: share of the same
    /// pooled TIC. Identified and de novo measured envelopes are directly labelled.
    /// Confidence metrics remain source-specific in the subtitle.
    /// </summary>
    public sealed class DeNovoVariantComparisonChart : TalkChartBase
    {
        private readonly List<DeNovoVariantSummary> rows;
        public IReadOnlyList<DeNovoVariantSummary> Rows => rows; // U3d native drawing inputs.

        public DeNovoVariantComparisonChart(IEnumerable<DeNovoVariantSummary> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            this.rows = rows.ToList();
            if (this.rows.Count == 0) throw new ArgumentException("At least one variant is required.", nameof(rows));
            foreach (DeNovoVariantSummary row in this.rows) row.Validate();
        }

        protected override void OnRender(DrawingContext dc)
        {
            Ground(dc, "Measured search and de novo envelopes across source variants",
                "Same pooled TIC denominator; both parts use measured envelopes, and each source keeps its native confidence field and cutoff. ALC and SCORE are not a common scale.");
            if (ActualWidth < 600 || ActualHeight < 300) return;

            Rect plot = new Rect(86, 104, ActualWidth - 128, ActualHeight - 240);
            double max = Math.Max(0.01, rows.Max(r => r.CombinedModelEstimateFractionOfTic) * 1.18);
            // Round upward to a clean five-percentage-point increment.
            max = Math.Ceiling(max * 20.0) / 20.0;
            var axis = new Pen(Palette.Axis, 1.0);
            dc.DrawLine(axis, plot.BottomLeft, plot.TopLeft);
            dc.DrawLine(axis, plot.BottomLeft, plot.BottomRight);

            const int ticks = 5;
            for (int i = 0; i <= ticks; i++)
            {
                double value = max * i / ticks;
                double y = plot.Bottom - plot.Height * value / max;
                dc.DrawLine(new Pen(Palette.Grid, 0.6), new Point(plot.Left, y), new Point(plot.Right, y));
                DrawText(dc, P(value), Palette.Muted, 9, new Point(18, y - 7));
            }

            double slot = plot.Width / rows.Count;
            double barWidth = Math.Min(112, slot * 0.55);
            for (int i = 0; i < rows.Count; i++)
            {
                DeNovoVariantSummary row = rows[i];
                double x = plot.Left + slot * (i + 0.5) - barWidth / 2;
                double idHeight = plot.Height * row.IdentifiedFractionOfTic / max;
                double dnHeight = plot.Height * row.DeNovoModelEstimateFractionOfTic / max;
                Rect identified = new Rect(x, plot.Bottom - idHeight, barWidth, idHeight);
                Rect deNovo = new Rect(x, plot.Bottom - idHeight - dnHeight, barWidth, dnHeight);
                Bar(dc, identified, Palette.Identified, P(row.IdentifiedFractionOfTic));
                Bar(dc, deNovo, Palette.DeNovo, "+" + P(row.DeNovoModelEstimateFractionOfTic));

                DrawText(dc, P(row.CombinedModelEstimateFractionOfTic), Palette.Ink, 11,
                    new Point(x + barWidth / 2 - 25, deNovo.Top - 24), true);
                FormattedText name = Text(row.Variant, Palette.Ink, 10, true);
                dc.DrawText(name, new Point(x + barWidth / 2 - name.Width / 2, plot.Bottom + 16));
                string[] cutoffs = row.ConfidenceCutoffNote.Split(';', StringSplitOptions.RemoveEmptyEntries);
                for (int line = 0; line < Math.Min(2, cutoffs.Length); line++)
                {
                    FormattedText metric = Text(cutoffs[line].Trim(), Palette.Muted, 8.5);
                    dc.DrawText(metric, new Point(x + barWidth / 2 - metric.Width / 2,
                        plot.Bottom + 35 + line * 15));
                }
                FormattedText reps = Text(N(row.DeNovoRepresentatives) + " credited de novo reps", Palette.Muted, 8.2);
                dc.DrawText(reps, new Point(x + barWidth / 2 - reps.Width / 2, plot.Bottom + 68));
            }

            DrawText(dc, "Share of pooled TIC", Palette.Muted, 10, new Point(18, 78));
            dc.DrawRectangle(Palette.Identified, null,
                new Rect(plot.Left, ActualHeight - 33, 13, 13));
            DrawText(dc, "observed search envelope", Palette.Ink, 9,
                new Point(plot.Left + 20, ActualHeight - 36));
            dc.DrawRectangle(Palette.DeNovo, null,
                new Rect(plot.Left + 195, ActualHeight - 33, 13, 13));
            DrawText(dc, "measured envelope for de novo peptides", Palette.Ink, 9,
                new Point(plot.Left + 215, ActualHeight - 36));

        }
    }
}
