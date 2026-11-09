using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        /// <summary>
        /// The shared chart palette, and every chart drawn in both of its variants.
        ///
        /// THREE DIFFERENT CLAIMS ARE CHECKED. First, that the DARK variant still carries exactly the
        /// values the charts hardcoded before the palette existed, because any drift there is a change to
        /// what the application looks like and nobody asked for one. Second, that the LIGHT variant is a
        /// real light palette - near-white ground, near-black ink, light grid - and that it is not the
        /// dark one with the background inverted: every series moved, because the dark palette's pale blue
        /// and mint green carry about 2:1 against white and wash out on a projector. Third, that the
        /// colours can actually be TOLD APART: the two most important series, and every pair of segments
        /// that sit side by side in a stacked bar, are separated by a CIELAB distance and not merely by a
        /// hue name - and never by a red/green pair, which a colour-blind reader cannot use.
        /// </summary>
        private static void ChartPaletteTests(string workDir)
        {
            ChartThemeTests(workDir);
            UiChartPaletteTests(workDir);
            ChartPalette dark = ChartPalette.Dark;
            ChartPalette light = ChartPalette.Light;

            // ---- the dark variant is EXACTLY what the application always painted ---------------------
            Check("palette: the dark structure is the historical house set (backdrop, ink, muted, grid)",
                IsRgb(dark.Backdrop, 0x33, 0x39, 0x49) && IsRgb(dark.Ink, 0xF2, 0xF0, 0xE9)
                && IsRgb(dark.Muted, 0xA8, 0xA4, 0x9A) && IsRgb(dark.Grid, 0x44, 0x4B, 0x5C));
            Check("palette: the dark series are the historical house set too",
                IsRgb(dark.Identified, 0x6F, 0xA8, 0xF0) && IsRgb(dark.DeNovo, 0x3F, 0xD3, 0x9C)
                && IsRgb(dark.Quantified, 0x55, 0xBD, 0xC6) && IsRgb(dark.NotQuantified, 0xB0, 0x6C, 0xC8)
                && IsRgb(dark.PeptideLikeUnidentified, 0xD8, 0xA6, 0x4B)
                && IsRgb(dark.Remainder, 0x6B, 0x73, 0x8A) && IsRgb(dark.Detected, 0xC9, 0x9A, 0x40)
                && IsRgb(dark.Tic, 0x8A, 0x93, 0xA8) && IsRgb(dark.Mixing, 0xF0, 0x85, 0x4C)
                && IsRgb(dark.NotAFamily, 0xD8, 0xA6, 0x4B));
            Check("palette: the dark axis is still the muted brush and a segment label still the backdrop, " +
                  "so no pixel of the screen moves",
                IsRgb(dark.Axis, 0xA8, 0xA4, 0x9A) && IsRgb(dark.SegmentLabel, 0x33, 0x39, 0x49));
            Check("palette: the dark mass bands are still blue, green and orange, in that order",
                dark.MassBands.Count == 3 && IsRgb(dark.MassBands[0], 0x6F, 0xA8, 0xF0)
                && IsRgb(dark.MassBands[1], 0x3F, 0xD3, 0x9C) && IsRgb(dark.MassBands[2], 0xF0, 0x85, 0x4C));
            Check("palette: Dark is the default of the palette lookup, so nothing changes by accident",
                ReferenceEquals(ChartPalette.For(ChartPaletteVariant.Dark), dark)
                && ReferenceEquals(ChartPalette.For(ChartPaletteVariant.Light), light));

            // ---- the light variant really is light ---------------------------------------------------
            double backLum = ChartPalette.RelativeLuminance(light.Backdrop);
            double inkLum = ChartPalette.RelativeLuminance(light.Ink);
            Check("palette: the light backdrop is near-white (relative luminance above 0.90)",
                backLum > 0.90, backLum.ToString("F4", CultureInfo.InvariantCulture));
            Check("palette: the light ink is near-black (relative luminance below 0.02)",
                inkLum < 0.02, inkLum.ToString("F4", CultureInfo.InvariantCulture));
            Check("palette: ink on that backdrop is far past the readable threshold (12:1 or better)",
                ChartPalette.ContrastRatio(light.Ink, light.Backdrop) >= 12.0,
                ChartPalette.ContrastRatio(light.Ink, light.Backdrop).ToString("F1", CultureInfo.InvariantCulture));
            Check("palette: the light grid is a LIGHT grey - present, never competing with a bar",
                ChartPalette.RelativeLuminance(light.Grid) > 0.60
                && ChartPalette.ContrastRatio(light.Grid, light.Backdrop) < 2.0,
                ChartPalette.RelativeLuminance(light.Grid).ToString("F4", CultureInfo.InvariantCulture));
            Check("palette: the light quiet text is still readable on it (4.5:1 or better)",
                ChartPalette.ContrastRatio(light.Muted, light.Backdrop) >= 4.5,
                ChartPalette.ContrastRatio(light.Muted, light.Backdrop).ToString("F1", CultureInfo.InvariantCulture));

            // The lightness gap between the two main series is a LIGHT-variant requirement: on a projector
            // hue alone is the first thing a bad lamp takes away, and a colour-blind reader in the back row
            // is left with lightness. The historical dark pair does not have it (1.29:1) and is not being
            // changed here; it is separated by hue and chroma, which the CIELAB check above measures.
            Check("palette: in the light variant the two main series differ in LIGHTNESS as well as hue",
                ChartPalette.ContrastRatio(light.Identified, light.DeNovo) >= 1.8,
                ChartPalette.ContrastRatio(light.Identified, light.DeNovo)
                    .ToString("F2", CultureInfo.InvariantCulture));

            // NOT an inversion: every series was re-tuned, because the dark ones wash out on white.
            var reTuned = new (string Name, Brush Dark, Brush Light)[]
            {
                ("identified", dark.Identified, light.Identified),
                ("de novo", dark.DeNovo, light.DeNovo),
                ("quantified", dark.Quantified, light.Quantified),
                ("not quantified", dark.NotQuantified, light.NotQuantified),
                ("peptide-like unidentified", dark.PeptideLikeUnidentified, light.PeptideLikeUnidentified),
                ("remainder", dark.Remainder, light.Remainder),
                ("detected", dark.Detected, light.Detected),
            };
            Check("palette: the light variant is NOT the dark one inverted - every series was re-tuned",
                reTuned.All(s => ChartPalette.ColorOf(s.Dark) != ChartPalette.ColorOf(s.Light)),
                string.Join(", ", reTuned.Where(s => ChartPalette.ColorOf(s.Dark) == ChartPalette.ColorOf(s.Light))
                                        .Select(s => s.Name)));
            Check("palette: and each of them got DARKER, which is what holds contrast on white",
                reTuned.All(s => ChartPalette.RelativeLuminance(s.Light)
                                 < ChartPalette.RelativeLuminance(s.Dark)),
                string.Join(", ", reTuned.Where(s => ChartPalette.RelativeLuminance(s.Light)
                                                     >= ChartPalette.RelativeLuminance(s.Dark))
                                        .Select(s => s.Name)));
            Check("palette: the dark blue and mint green really would have washed out (under 2.6:1 on white)",
                ChartPalette.ContrastRatio(dark.Identified, light.Backdrop) < 2.6
                && ChartPalette.ContrastRatio(dark.DeNovo, light.Backdrop) < 2.6,
                ChartPalette.ContrastRatio(dark.Identified, light.Backdrop).ToString("F2", CultureInfo.InvariantCulture) +
                " / " + ChartPalette.ContrastRatio(dark.DeNovo, light.Backdrop).ToString("F2", CultureInfo.InvariantCulture));

            var lightSeries = new (string Name, Brush Brush)[]
            {
                ("identified", light.Identified), ("de novo", light.DeNovo),
                ("quantified", light.Quantified), ("not quantified", light.NotQuantified),
                ("peptide-like unidentified", light.PeptideLikeUnidentified),
                ("remainder", light.Remainder), ("detected z >= 2", light.DetectedMultiplyCharged),
                ("detected z = 1", light.DetectedSinglyCharged), ("TIC", light.Tic),
                ("mixing", light.Mixing),
            };
            Check("palette: every light series holds at least 3:1 against the backdrop, so it survives a projector",
                lightSeries.All(s => ChartPalette.ContrastRatio(s.Brush, light.Backdrop) >= 3.0),
                string.Join(", ", lightSeries
                    .Where(s => ChartPalette.ContrastRatio(s.Brush, light.Backdrop) < 3.0)
                    .Select(s => s.Name + " " +
                        ChartPalette.ContrastRatio(s.Brush, light.Backdrop).ToString("F2", CultureInfo.InvariantCulture))));

            // A fill that carries a percentage inside it has to contrast with the LABEL, not the panel.
            var labelled = new (string Name, Brush Fill)[]
            {
                ("quantified", light.Quantified), ("not quantified", light.NotQuantified),
                ("peptide-like unidentified", light.PeptideLikeUnidentified),
                ("remainder", light.Remainder),
                ("identified", light.Identified), ("de novo", light.DeNovo),
            };
            Check("palette: every light fill that carries a label is at least 4:1 against that label",
                labelled.All(s => ChartPalette.ContrastRatio(s.Fill, light.SegmentLabel) >= 4.0),
                string.Join(", ", labelled
                    .Where(s => ChartPalette.ContrastRatio(s.Fill, light.SegmentLabel) < 4.0)
                    .Select(s => s.Name + " " +
                        ChartPalette.ContrastRatio(s.Fill, light.SegmentLabel).ToString("F2", CultureInfo.InvariantCulture))));

            // ---- the colours can be told apart, in BOTH variants -------------------------------------
            foreach (ChartPalette p in new[] { dark, light })
            {
                string which = p.Variant.ToString().ToLowerInvariant();

                double idVsDn = ChartPalette.PerceptualDistance(p.Identified, p.DeNovo);
                Check("palette (" + which + "): the two main series are far apart perceptually (CIELAB 25+)",
                    idVsDn >= 25.0, idVsDn.ToString("F1", CultureInfo.InvariantCulture));

                // Not a red/green pair: one is blue-dominant, the other green-dominant, and NEITHER is a
                // red. A deuteranope reads the pair by that difference and by lightness, not by hue alone.
                Color id = ChartPalette.ColorOf(p.Identified);
                Color dn = ChartPalette.ColorOf(p.DeNovo);
                Check("palette (" + which + "): they are a blue/green pair, never red versus green",
                    id.B > id.R && id.B >= id.G && dn.G > dn.R && dn.G >= dn.B,
                    id.ToString() + " / " + dn.ToString());

                // The stacked bar's own neighbours, in the order the recovery chart paints them.
                var neighbours = new (string Name, Brush A, Brush B)[]
                {
                    ("quantified | not quantified", p.Quantified, p.NotQuantified),
                    ("not quantified | peptide-like unidentified", p.NotQuantified, p.PeptideLikeUnidentified),
                    ("peptide-like unidentified | noise", p.PeptideLikeUnidentified, p.Remainder),
                    ("identified | de novo", p.Identified, p.DeNovo),
                    ("detected z >= 2 | detected z = 1", p.DetectedMultiplyCharged, p.DetectedSinglyCharged),
                };
                Check("palette (" + which + "): every pair of adjacent bar segments is distinguishable (CIELAB 25+)",
                    neighbours.All(n => ChartPalette.PerceptualDistance(n.A, n.B) >= 25.0),
                    string.Join(", ", neighbours
                        .Where(n => ChartPalette.PerceptualDistance(n.A, n.B) < 25.0)
                        .Select(n => n.Name + " " +
                            ChartPalette.PerceptualDistance(n.A, n.B).ToString("F1", CultureInfo.InvariantCulture))));
            }

            // ---- every chart renders headless in BOTH variants ---------------------------------------
            var sweep = new List<IdentityScanRow>
            {
                new IdentityScanRow { Identity = 90, Groups = 500, TargetGroups = 260, MixedGroups = 0, Score = 0.000 },
                new IdentityScanRow { Identity = 70, Groups = 320, TargetGroups = 168, MixedGroups = 1, Score = 0.003 },
                new IdentityScanRow { Identity = 50, Groups = 170, TargetGroups = 92, MixedGroups = 8, Score = 0.061 },
            };

            var rows = SyntheticAnalyticsRows();
            var tic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                { ["ui_01"] = 5.0e8, ["ui_02"] = 4.0e8 };
            AnalyticsData data = AnalyticsBuilder.Build(rows, tic);

            // A detection with the charge split, so the recovery chart is exercised in its split form and
            // not only in the shape it had before.
            foreach (NativeRunFraction fr in data.Fractions)
            {
                fr.DetectedAreaMultiplyCharged = fr.Tic * 0.30;
                fr.DetectedAreaSinglyCharged = fr.Tic * 0.08;
                fr.DetectedPeptideLikeArea = fr.DetectedAreaSinglyCharged + fr.DetectedAreaMultiplyCharged;
                fr.DetectedCountMultiplyCharged = 900;
                fr.DetectedCountSinglyCharged = 220;
                fr.DetectedEnvelopeCount = 1120;
            }
            NativeRunFraction? grouped = NativeQuantFractions.Grouped(data.Fractions);

            FamilyEnvelopeQuantResult families = FamilyFixture(6, unattributedIdentified: 4000);
            var overlap = new DeNovoOverlapSummary
            {
                RawDeNovoRows = 100, FilteredDeNovoRows = 40,
                NoSearchPsmInFileAndScan = 24, SharedFileAndScan = 16,
                DifferentChargeInSharedScan = 4, SameFileScanAndCharge = 12,
                SameChargeButNoMzMatch = 8,
                DifferentPrecursorInSharedScan = 12, SamePrecursorWithinTolerance = 4,
                ScanLevelStrictSequenceMatchesIgnoringPtm = 2,
                ScanLevelAdditionalIleLeuEquivalentMatches = 1,
                ScanLevelSequenceDivergencesIgnoringPtmStrict = 10,
                ScanLevelSequenceDivergencesAfterIleLeuFold = 9,
                StrictSequenceMatchesIgnoringPtm = 1, AdditionalIleLeuEquivalentMatches = 1,
                SequenceDivergencesIgnoringPtmStrict = 3, SequenceDivergencesAfterIleLeuFold = 2,
                MinLength = 7, MinAverageLocalConfidence = 80, StretchLength = 5, StretchFloor = 90,
                PrecursorTolerancePpm = 20, WeakXCorr = 2.5,
            };
            DeNovoScoreDistribution scoreDistribution = DeNovoScoreDistributionStudy.Build(new[]
            {
                new PeaksDeNovoRow { Length = 8, Alc = 62, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                new PeaksDeNovoRow { Length = 8, Alc = 81, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                new PeaksDeNovoRow { Length = 8, Alc = 96, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
            });
            var sourceScoreDistributions = new[]
            {
                DeNovoSourceScoreDistributionStudy.Build(new[]
                {
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 62, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 81, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 96, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                }, new DeNovoSourceSelection(DeNovoSource.DeNovoAll, 80)),
                DeNovoSourceScoreDistributionStudy.Build(new[]
                {
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 71, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 77, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 91, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                }, new DeNovoSourceSelection(DeNovoSource.DeepNovo, 75)),
            };
            var identification = new DeNovoIdentificationSummary
            {
                FilteredSpectrumCalls = 4,
                CarriedForwardSpectrumCalls = 3,
                FilteredRunSequenceChargeGroups = 4,
                FilteredRunSequenceGroups = 3,
                FilteredDistinctSequencesIgnoringPtm = 2,
                FilteredDistinctSequencesAfterIleLeuFold = 2,
                CarriedForwardRunSequenceChargeGroups = 3,
                CarriedForwardRunSequenceGroups = 2,
                CarriedForwardDistinctSequencesIgnoringPtm = 2,
                CarriedForwardDistinctSequencesAfterIleLeuFold = 2,
                Runs = new List<DeNovoIdentificationRunSummary>
                {
                    new DeNovoIdentificationRunSummary
                    {
                        Run = "ui_01", FilteredSpectrumCalls = 2, CarriedForwardSpectrumCalls = 2,
                        FilteredSequenceChargeGroups = 2, FilteredDistinctSequencesIgnoringPtm = 2,
                        CarriedForwardSequenceChargeGroups = 2,
                        CarriedForwardDistinctSequencesIgnoringPtm = 1,
                    },
                    new DeNovoIdentificationRunSummary
                    {
                        Run = "ui_02", FilteredSpectrumCalls = 2, CarriedForwardSpectrumCalls = 1,
                        FilteredSequenceChargeGroups = 2, FilteredDistinctSequencesIgnoringPtm = 1,
                        CarriedForwardSequenceChargeGroups = 1,
                        CarriedForwardDistinctSequencesIgnoringPtm = 1,
                    },
                },
            };
            var concordance = new IdentificationConcordanceSummary
            {
                PatternLabPsmRows = 12, PeaksDatabasePsmRows = 11,
                PatternLabScanKeys = 10, PeaksDatabaseScanKeys = 9,
                SharedDatabaseSearchScanKeys = 8,
                ExactDatabaseSearchSequenceScanKeys = 7,
                AdditionalIleLeuEquivalentDatabaseSearchScanKeys = 1,
                SameChargeDatabaseSearchScanKeys = 8,
                ExactSameChargeDatabaseSearchScanKeys = 7,
                PrecursorQualifiedDatabaseSearchPairs = 8,
                ExactPrecursorQualifiedDatabaseSearchPairs = 7,
                AdditionalIleLeuEquivalentPrecursorQualifiedPairs = 1,
                PrecursorQualifiedDatabaseSearchScanKeys = 8,
                ExactPrecursorQualifiedDatabaseSearchScanKeys = 7,
                PatternLabPrecursorHypothesesInSharedScans = 8,
                PeaksDatabasePrecursorHypothesesInSharedScans = 8,
                PrecursorTolerancePpm = 20,
                FilteredDeNovoRows = 4,
                DeNovoRowsSharingPeaksDatabaseScan = 2,
                DeNovoRowsSharingPeaksDatabaseScanAndCharge = 1,
                DeNovoRowsExactToPeaksDatabaseAtSameCharge = 1,
            };
            ReferenceEnvelopeDiagnostics envelopeDiagnostics = ReferenceEnvelopeEstimator.Evaluate(
                NativeQuantFractions.DedupToPeptides(rows));
            var presentationManifest = new QuantManifest
            {
                DeNovoOverlap = overlap,
                DeNovoScoreDistribution = scoreDistribution,
                DeNovoSourceScoreDistributions = sourceScoreDistributions.ToList(),
                DeNovoIdentification = identification,
                IdentificationConcordance = concordance,
                ReferenceEnvelopeModelDiagnostics = envelopeDiagnostics,
                PresentationIdentitySweep = sweep,
                DeNovoUnionResolutionMethod = DeNovoUnionResolver.Method,
                DeNovoInputs = new List<DeNovoInputManifest>
                {
                    new DeNovoInputManifest
                    {
                        SourceId = DeNovoSource.DeNovoAll.Id,
                        SourceDisplayName = DeNovoSource.DeNovoAll.DisplayName,
                        ConfidenceField = DeNovoSource.DeNovoAll.ConfidenceColumn,
                        MinimumConfidence = 80, SourcePriority = 0,
                        FilePath = @"C:\fixture\run.denovo.csv", Sha256 = new string('a', 64),
                        RawRows = 3, RowsPassingSourceFilter = 2,
                    },
                    new DeNovoInputManifest
                    {
                        SourceId = DeNovoSource.DeepNovo.Id,
                        SourceDisplayName = DeNovoSource.DeepNovo.DisplayName,
                        ConfidenceField = DeNovoSource.DeepNovo.ConfidenceColumn,
                        MinimumConfidence = 75, SourcePriority = 1,
                        FilePath = @"C:\fixture\run.deepnovo.csv", Sha256 = new string('b', 64),
                        RawRows = 3, RowsPassingSourceFilter = 2,
                    },
                },
            };
            var variantSummaries = new[]
            {
                Variant(DeNovoSource.DenovoOnly.DisplayName, "ALC (%) >= 80", 0.28, 0.12,
                    4800, 7600, DeNovoSource.DenovoOnly.DisplayName),
                Variant(DeNovoSource.DeNovoAll.DisplayName, "ALC (%) >= 80", 0.28, 0.15,
                    4800, 9100, DeNovoSource.DeNovoAll.DisplayName),
                Variant(DeNovoSource.DeepNovo.DisplayName, "SCORE (%) >= 75", 0.28, 0.10,
                    4800, 6500, DeNovoSource.DeepNovo.DisplayName),
                Variant(DeNovoVariantComparison.OnlyPlusDeepNovoVariantName,
                    "ALC (%) >= 80; SCORE (%) >= 75", 0.28, 0.16, 4800, 9900,
                    DeNovoSource.DenovoOnly.DisplayName + " + " + DeNovoSource.DeepNovo.DisplayName),
                Variant(DeNovoVariantComparison.AllPlusDeepNovoVariantName,
                    "ALC (%) >= 80; SCORE (%) >= 75", 0.28, 0.17, 4800, 10400,
                    DeNovoSource.DeNovoAll.DisplayName + " + " + DeNovoSource.DeepNovo.DisplayName),
            };
            DeNovoVariantComparison.ValidateTalkVariantSet(variantSummaries);
            DeNovoVariantSummary exactUnionFixture = variantSummaries.Single(v => v.Variant ==
                DeNovoVariantComparison.OnlyPlusDeepNovoVariantName);
            string exactUnionSources = exactUnionFixture.Sources;
            exactUnionFixture.Sources = DeNovoSource.DeNovoAll.DisplayName + " + " +
                DeNovoSource.DeepNovo.DisplayName;
            Check("variant comparison rejects an only+DeepNovo row mislabeled with all+DeepNovo sources",
                Throws<InvalidDataException>(() =>
                    DeNovoVariantComparison.ValidateTalkVariantSet(variantSummaries)));
            exactUnionFixture.Sources = exactUnionSources;
            List<DeNovoTalkComparisonRow> compactVariantRows =
                DeNovoTalkComparison.Build(variantSummaries);
            Check("compact comparison selects the exact de-novo-only union and excludes PEAKS-all",
                compactVariantRows.Count == 4
                && compactVariantRows.Last().Sources == exactUnionSources
                && !DeNovoTalkComparison.ToCsv(compactVariantRows).Contains(
                    DeNovoSource.DeNovoAll.DisplayName + " + " + DeNovoSource.DeepNovo.DisplayName));
            presentationManifest.PresentationDeNovoVariants = variantSummaries.ToList();
            var sourceVennFixture = new DeNovoSourceVennSummary
            {
                FilteredBeforeUnion = new DeNovoSequenceSetRegions
                {
                    StageId = "filtered-before-union",
                    StageLabel = "After all three source filters · before precursor union",
                    Scope = "pooled",
                    Grain = DeNovoSourceVennStudy.FilteredBeforeUnionGrain,
                    PeaksExclusive = 20642, Shared = 936, DeepNovoExclusive = 2139,
                    Union = 23717,
                },
                CarriedForwardClaimProvenance = new DeNovoSequenceSetRegions
                {
                    StageId = "carried-forward-claim-provenance",
                    StageLabel = "Carried-forward union rows · source-claim provenance",
                    Scope = "pooled",
                    Grain = DeNovoSourceVennStudy.CarriedForwardClaimProvenanceGrain,
                    PeaksExclusive = 19000, Shared = 800, DeepNovoExclusive = 1200,
                    Union = 21000,
                },
                PeaksFunnel = new DeNovoSourceFilterFunnel
                {
                    SourceId = DeNovoSource.DenovoOnly.Id,
                    SourceDisplayName = DeNovoSource.DenovoOnly.DisplayName,
                    ConfidenceField = DeNovoSource.DenovoOnly.ConfidenceField,
                    MinimumConfidence = 80, MinLength = 7, StretchLength = 5, StretchFloor = 90,
                    RawRows = 181643, ScorePassingRows = 41878,
                    ScoreAndLengthPassingRows = 38381, AllFiltersPassingRows = 35686,
                },
                DeepNovoFunnel = new DeNovoSourceFilterFunnel
                {
                    SourceId = DeNovoSource.DeepNovo.Id,
                    SourceDisplayName = DeNovoSource.DeepNovo.DisplayName,
                    ConfidenceField = DeNovoSource.DeepNovo.ConfidenceField,
                    MinimumConfidence = 80, MinLength = 7, StretchLength = 5, StretchFloor = 90,
                    RawRows = 42549, ScorePassingRows = 19476,
                    ScoreAndLengthPassingRows = 18528, AllFiltersPassingRows = 17875,
                },
            };
            sourceVennFixture.Validate();
            presentationManifest.DeNovoSourceVenn = sourceVennFixture;
            presentationManifest.SpectrumComparisonRows = new List<SpectrumComparisonRow>
            {
                new SpectrumComparisonRow
                {
                    FileName = "run01.raw", NormalizedRunName = "run01", ScanNumber = 1001,
                    PrecursorHypothesisIndex = 1, Charge = 2, PrecursorMz = 500.25,
                    PeaksDeNovoSequence = "C(+57.02)EPTIDEK",
                    PeaksDeNovoCleanSequence = "CEPTIDEK", PeaksDeNovoScore = 88,
                    PeaksDeNovoScoreField = "ALC (%)", PeaksDeNovoAccepted = true,
                    DeepNovoSequence = "CEPTIDEK", DeepNovoCleanSequence = "CEPTIDEK",
                    DeepNovoScore = 91, DeepNovoScoreField = "SCORE (%)",
                    DeepNovoAccepted = true,
                },
                new SpectrumComparisonRow
                {
                    FileName = "run01.raw", NormalizedRunName = "run01", ScanNumber = 1002,
                    PrecursorHypothesisIndex = 1, Charge = 2, PrecursorMz = 600.25,
                    PeaksDeNovoSequence = "PEPTIDEK", PeaksDeNovoCleanSequence = "PEPTIDEK",
                    PeaksDeNovoScore = 93, PeaksDeNovoScoreField = "ALC (%)",
                    PeaksDeNovoAccepted = true,
                    DeepNovoSequence = "ALTERNATE", DeepNovoCleanSequence = "ALTERNATE",
                    DeepNovoScore = 89, DeepNovoScoreField = "SCORE (%)",
                    DeepNovoAccepted = true,
                },
            };
            DeNovoAcceptedScoreScatter acceptedScoreFixture =
                DeNovoAcceptedScoreScatterStudy.Build(
                    presentationManifest.SpectrumComparisonRows, sourceScoreDistributions);
            presentationManifest.DeNovoAcceptedScoreScatter = acceptedScoreFixture;
            var reference = new ReferenceComponentSummary
            {
                AllComponents = 100, PureDecoyComponents = 50, TargetBearingComponents = 50,
                ContaminantBearingTargetComponents = 15, NonContaminantTargetComponents = 36,
                MixedTargetDecoyComponents = 1,
            };

            string folder = Path.Combine(workDir, "palette");
            Directory.CreateDirectory(folder);

            foreach (ChartPaletteVariant variant in new[] { ChartPaletteVariant.Dark, ChartPaletteVariant.Light })
            {
                string which = variant.ToString().ToLowerInvariant();
                var charts = new (string Name, FrameworkElement Chart)[]
                {
                    ("identity_scan", new IdentityScanChart(sweep, currentIdentity: 70)),
                    ("recovery", new RecoveryChart(data.Fractions, grouped)),
                    ("xic_by_mass", new XicMassChart(data.Peptides)),
                    ("isotopes_credited", new IsotopeCountChart(data.Peptides)),
                    ("percent_ion_current", new PercentIonCurrentChart(data.Peptides, grouped?.Tic ?? 0)),
                    ("family_contribution", new FamilyContributionChart(families)),
                    ("de_novo_score_distribution", new DeNovoScoreDistributionChart(scoreDistribution)),
                    ("de_novo_source_score_distributions", new DeNovoScoreDistributionChart(sourceScoreDistributions)),
                    ("de_novo_consolidation", new DeNovoConsolidationChart(data, presentationManifest)),
                    ("de_novo_variant_comparison", new DeNovoVariantComparisonChart(variantSummaries)),
                    ("de_novo_source_effect_comparison", new DeNovoTalkComparisonChart(compactVariantRows)),
                    ("de_novo_source_venn", new DeNovoSourceVennChart(sourceVennFixture)),
                    ("de_novo_accepted_score_agreement",
                        new DeNovoAcceptedScoreScatterChart(acceptedScoreFixture)),
                    ("peptide_evidence_overlap", new PeptideEvidenceOverlapChart(data, overlap)),
                    ("signal_by_run", new SignalByRunChart(data.Fractions)),
                    ("signal_uplift", new SignalUpliftChart(grouped)),
                    ("reference_components", new ReferenceComponentsChart(reference, 70)),
                    ("family_signal_share", new FamilySignalShareChart(families, 10)),
                };

                foreach (var (name, chart) in charts)
                {
                    string png = Path.Combine(folder, name + "_" + which + ".png");
                    if (File.Exists(png)) File.Delete(png);

                    bool ok = true;
                    try { ChartImageExporter.Save(chart, png, variant: variant); }
                    catch (Exception ex)
                    {
                        ok = false;
                        Console.WriteLine("        " + name + " (" + which + ") threw: " + ex.Message);
                    }

                    long size = File.Exists(png) ? new FileInfo(png).Length : 0;
                    Check("the " + name + " chart renders headless in the " + which + " palette",
                        ok && size > 3000, size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");

                    if (!ok || size <= 0) continue;

                    Color ground = PngCornerColour(png);
                    Color expected = ChartPalette.ColorOf(ChartPalette.For(variant).Backdrop);
                    Check("and its ground is the " + which + " backdrop, read back from the file",
                        ground == expected, ground.ToString() + " vs " + expected.ToString());
                }
            }

            Check("family figure labels keep repeated biological names unambiguous",
                FamilyFigureLabel.Format(7, "Acidic PLA2 OS=Bothrops") == "F7 · Acidic PLA2"
                && FamilyFigureLabel.Format(8, "Acidic PLA2 OS=Bothrops") == "F8 · Acidic PLA2"
                && FamilyFigureLabel.Format(7, "Acidic PLA2 OS=Bothrops")
                    != FamilyFigureLabel.Format(8, "Acidic PLA2 OS=Bothrops"));

            var crowdedFamilies = new FamilyEnvelopeQuantResult();
            for (int i = 0; i < 10; i++)
            {
                double total = i == 9 ? 400e9 : (i + 1) * 0.1e9;
                crowdedFamilies.Families.Add(new FamilyEnvelopeQuantRow
                {
                    FamilyUserId = 700 + i,
                    Description = i == 9 ? "Right-edge control family" : "Crowded origin family " + i,
                    IdentifiedEnvelope = total * 0.95,
                    DeNovoEnvelopeReferenceCalibrated = total * 0.05,
                });
            }
            var crowdedChart = new FamilySignalShareChart(crowdedFamilies, labelTopN: 10);
            ChartImageExporter.Render(crowdedChart, variant: ChartPaletteVariant.Light);
            IReadOnlyList<Rect> crowdedBounds = crowdedChart.LastLabelBounds;
            Rect familyPlotSafe = new Rect(98, 114, 808, 338);
            bool labelsDoNotOverlap = Enumerable.Range(0, crowdedBounds.Count).All(i =>
                Enumerable.Range(i + 1, crowdedBounds.Count - i - 1).All(j =>
                    !crowdedBounds[i].IntersectsWith(crowdedBounds[j])));
            Check("family-signal labels stay inside the plot and do not overlap in a crowded layout",
                crowdedBounds.Count == 10
                && crowdedBounds.All(bounds => familyPlotSafe.Contains(bounds))
                && labelsDoNotOverlap,
                "labels=" + crowdedBounds.Count.ToString(CultureInfo.InvariantCulture));
            Check("family-signal labels use leader lines when a point cloud is compressed",
                crowdedChart.LastLabelLeaderCount > 0,
                crowdedChart.LastLabelLeaderCount.ToString(CultureInfo.InvariantCulture));

            // ---- one synthetic complete-project export covers every new reproducibility artefact ----
            string evidenceFolder = Path.Combine(folder, "presentation_evidence");
            if (Directory.Exists(evidenceFolder)) Directory.Delete(evidenceFolder, recursive: true);
            families.Families[0].Description = "=HYPERLINK(\"https://invalid\")";
            PresentationEvidencePackResult evidence = PresentationEvidencePack.Save(
                data, presentationManifest, BuildSyntheticFcf(), families, evidenceFolder);
            var expectedEvidenceFigures = PresentationFigures.FileNames.Concat(new[]
            {
                PresentationFigures.DeNovoSourceVennFile,
                PresentationFigures.DeNovoAcceptedScoreScatterFile,
                PresentationFigures.DeNovoVariantComparisonFile,
                PresentationFigures.DeNovoTalkComparisonFile,
            });
            Check("the presentation evidence pack writes the complete documented base and variant figure set",
                evidence.FigurePaths.Select(Path.GetFileName).SequenceEqual(expectedEvidenceFigures)
                && evidence.FigurePaths.Count == 15,
                string.Join(", ", evidence.FigurePaths.Select(Path.GetFileName)));
            Check("T3 the figure set keeps consolidation and removes the model-diagnostics PNG",
                evidence.FigurePaths.Any(p => Path.GetFileName(p) == PresentationFigures.DeNovoConsolidationFile)
                && !evidence.FigurePaths.Any(p => Path.GetFileName(p) == PresentationFigures.ReferenceEnvelopeDiagnosticsFile)
                && evidence.FigurePaths.Where(p =>
                    Path.GetFileName(p) == PresentationFigures.DeNovoConsolidationFile
                    || Path.GetFileName(p) == PresentationFigures.ReferenceEnvelopeDiagnosticsFile)
                    .All(p => new FileInfo(p).Length > 3000));

            string inputsCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.DeNovoInputsCsvFile));
            string scoresCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.SourceScoreCsvFile));
            string scoresJson = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.SourceScoreJsonFile));
            string consolidationCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.IdentificationCsvFile));
            string diagnosticCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.ReferenceEnvelopeDiagnosticsCsvFile));
            string familyCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.FamilyCsvFile));
            string familySourceCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.FamilySourceSequenceCsvFile));
            string compactSourceCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.DeNovoTalkComparisonCsvFile));
            string sourceVennCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.DeNovoSourceVennCsvFile));
            string acceptedScoreCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.DeNovoAcceptedScoreScatterCsvFile));
            string evidenceReadme = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.ReadmeFile));
            string plotJson = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.PlotDataFile));
            Check("de_novo_inputs.csv carries both source-native fields, cutoffs and fingerprints",
                inputsCsv.Contains("ALC (%)") && inputsCsv.Contains("SCORE (%)")
                && inputsCsv.Contains(new string('a', 64)) && inputsCsv.Contains(new string('b', 64)));
            Check("source-specific score CSV and JSON keep ALC and SCORE in separate records",
                scoresCsv.Contains("ALC (%)") && scoresCsv.Contains("SCORE (%)")
                && scoresJson.Contains("ALC (%)") && scoresJson.Contains("SCORE (%)"));
            Check("the consolidation CSV exposes all four exact count grains",
                consolidationCsv.Contains("imported,spectrum_row,6")
                && consolidationCsv.Contains("accepted,spectrum_call,4")
                && consolidationCsv.Contains("accepted,pooled_distinct_sequence,2")
                && consolidationCsv.Contains("credited,run_peptidoform_charge_mass_cluster"));
            Check("the model-diagnostics CSV exposes bin support, validation error and sensitivity",
                diagnosticCsv.Contains("fixed neutral-mass-bin support")
                && diagnosticCsv.Contains("cross_validated_weighted_absolute_percent_error")
                && diagnosticCsv.Contains("pooled_ratio_de_novo_estimate"));
            Check("family exports use the stable F{id} · name label",
                familyCsv.Contains("family_label") && familyCsv.Contains("F100 ·")
                && familyCsv.Contains("'=HYPERLINK", StringComparison.Ordinal));
            Check("family source export states exclusive/shared sequence categories and counting grain",
                familySourceCsv.Contains("peaks_denovo_only_exclusive_distinct_sequences")
                && familySourceCsv.Contains("shared_peaks_deepnovo_distinct_sequences")
                && familySourceCsv.Contains("accepted_tier"));
            Check("compact source-effect export contains the exact de-novo-only union, not the PEAKS-all union",
                compactSourceCsv.Contains("PEAKS de novo-only + DeepNovo")
                && !compactSourceCsv.Contains(
                    DeNovoSource.DeNovoAll.DisplayName + " + " + DeNovoSource.DeepNovo.DisplayName));
            Check("source Venn export carries both exact set grains and the DeepNovo filter funnel",
                sourceVennCsv.Contains("filtered-before-union")
                && sourceVennCsv.Contains("carried-forward-claim-provenance")
                && sourceVennCsv.Contains("42549")
                && sourceVennCsv.Contains("SCORE (%)"));
            Check("accepted score export preserves match classes, PTM-cleaning provenance and score scales",
                acceptedScoreCsv.Contains("strict PTM-clean match")
                && acceptedScoreCsv.Contains("strict PTM-clean mismatch")
                && acceptedScoreCsv.Contains("C(+57.02)EPTIDEK")
                && acceptedScoreCsv.Contains("not calibrated as equivalent probabilities"));
            Check("evidence README explains many-family mapping and the multi-table reproducibility boundary",
                evidenceReadme.Contains("FamilyMappingMinimumIdentity")
                && evidenceReadme.Contains("FamilyMappingStatus")
                && evidenceReadme.Contains("no single CSV is claimed to contain every analysis"));
            using (JsonDocument parsedPlot = JsonDocument.Parse(plotJson))
                Check("presentation_plot_data.json is valid JSON and carries the new project-native payloads",
                    parsedPlot.RootElement.TryGetProperty("deNovoInputs", out _)
                    && parsedPlot.RootElement.TryGetProperty("deNovoSourceScoreDistributions", out _)
                    && parsedPlot.RootElement.TryGetProperty("deNovoSourceVenn", out _)
                    && parsedPlot.RootElement.TryGetProperty("deNovoAcceptedScoreScatter", out _)
                    && parsedPlot.RootElement.TryGetProperty("referenceEnvelopeModelDiagnostics", out _)
                    && !plotJson.Contains("Infinity", StringComparison.Ordinal));

            IdentificationConcordanceSummary savedConcordance = presentationManifest.IdentificationConcordance;
            presentationManifest.IdentificationConcordance = new IdentificationConcordanceSummary
            {
                PatternLabPsmRows = 1, PeaksDatabasePsmRows = 1,
                PatternLabScanKeys = 1, PeaksDatabaseScanKeys = 1,
                SharedDatabaseSearchScanKeys = 1,
                ExactDatabaseSearchSequenceScanKeys = 1,
                SameChargeDatabaseSearchScanKeys = 1,
                ExactSameChargeDatabaseSearchScanKeys = 1,
            };
            Check("figure export refuses a stale raw-scan-only concordance manifest",
                Throws<InvalidOperationException>(() => PresentationFigures.Build(
                    data, presentationManifest, BuildSyntheticFcf(), families)));
            presentationManifest.IdentificationConcordance = savedConcordance;

            static DeNovoVariantSummary Variant(string name, string cutoff, double identified,
                                                 double deNovo, int searchReps, int deNovoReps,
                                                 string? sources = null) =>
                new DeNovoVariantSummary
                {
                    Variant = name,
                    Sources = sources ?? name,
                    GenerationId = "chart-fixture-generation",
                    GeneratedUtc = "2026-01-01T00:00:00.0000000Z",
                    DatasetFingerprint = "chart-fixture-dataset",
                    DeNovoInputFingerprint = "chart-fixture-input-" + name,
                    QuantitationSettings = "chart-fixture-settings",
                    AppVersion = "test",
                    ConfidenceMetrics = cutoff.Split(' ')[0],
                    ConfidenceCutoffNote = cutoff,
                    IdentifiedFractionOfTic = identified,
                    DeNovoModelEstimateFractionOfTic = deNovo,
                    CombinedModelEstimateFractionOfTic = identified + deNovo,
                    RelativeUplift = deNovo / identified,
                    SearchRepresentatives = searchReps,
                    DeNovoRepresentatives = deNovoReps,
                    TotalRepresentatives = searchReps + deNovoReps,
                    AcceptedDeNovoSpectrumCalls = deNovoReps * 2,
                    DistinctDeNovoSequencesNoXic = deNovoReps / 2,
                    CrossSideSuppressed = 80,
                    EnvelopeModelMethod = ReferenceEnvelopeEstimator.Method,
                    EnvelopeModelCrossValidatedWape = 0.08,
                    CombinedSensitivityLowerFractionOfTic = identified + deNovo * 0.95,
                    CombinedSensitivityUpperFractionOfTic = identified + deNovo * 1.05,
                };

            // ---- the default of the exporter is the SLIDE palette ------------------------------------
            string defaulted = Path.Combine(folder, "default_variant.png");
            ChartImageExporter.Save(new RecoveryChart(data.Fractions, grouped), defaulted);
            Color defaultGround = PngCornerColour(defaulted);
            Check("the exported PNG is LIGHT by default, because that is what goes into a slide",
                ChartImageExporter.DefaultVariant == ChartPaletteVariant.Light
                && defaultGround == ChartPalette.ColorOf(light.Backdrop),
                defaultGround.ToString());
            Check("and that ground really is near-white when measured off the file, not merely named so",
                ChartPalette.RelativeLuminance(new SolidColorBrush(defaultGround)) > 0.90,
                ChartPalette.RelativeLuminance(new SolidColorBrush(defaultGround))
                    .ToString("F4", CultureInfo.InvariantCulture));

            // The whole analytics figure set, which is what a talk actually receives.
            string figureFolder = Path.Combine(folder, "figures");
            List<string> written = AnalyticsFigures.SaveFromProject(
                rows, new QuantManifest { PerRunTic = tic }, curation: null, folder: figureFolder);
            Check("every saved analytics figure comes out on the light ground",
                written.All(pth => PngCornerColour(pth) == ChartPalette.ColorOf(light.Backdrop)),
                string.Join(", ", written.Where(pth => PngCornerColour(pth) != ChartPalette.ColorOf(light.Backdrop))
                                        .Select(Path.GetFileName)));

            // A chart the exporter was NOT asked to re-colour keeps the screen palette, so the default is
            // a choice the caller can undo rather than something imposed on every render.
            var screenChart = new RecoveryChart(data.Fractions, grouped);
            Check("a chart built for the screen still carries the dark palette until the exporter says otherwise",
                ReferenceEquals(screenChart.Palette, dark));
            screenChart.Palette = null!;
            Check("and assigning a null palette restores the screen one rather than painting nothing",
                ReferenceEquals(screenChart.Palette, dark));
        }

        private static bool PinnedLightPaletteMatches(ChartPalette palette)
        {
            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Backdrop"] = "#FFFBFAF7", ["Ink"] = "#FF16181E", ["Muted"] = "#FF565C68",
                ["Grid"] = "#FFDDDDE3", ["Panel"] = "#FFEEEFED", ["Axis"] = "#FF565C68", ["SegmentLabel"] = "#FFFFFFFF",
                ["Identified"] = "#FF10428C", ["DeNovo"] = "#FF1F8F5A", ["Quantified"] = "#FF0F6B84",
                ["NotQuantified"] = "#FF7B2D9E", ["PeptideLikeUnidentified"] = "#FFA85F13", ["Remainder"] = "#FF5F6675",
                ["Detected"] = "#FF8A5A12", ["DetectedMultiplyCharged"] = "#FF8A5A12", ["DetectedSinglyCharged"] = "#FF8C7C5C",
                ["Tic"] = "#FF7B8394", ["Families"] = "#FF10428C", ["Mixing"] = "#FFC2410C", ["Floor"] = "#FF1F8F5A",
                ["NotAFamily"] = "#FFA85F13", ["Found"] = "#FF10428C", ["OfTic"] = "#FF1F8F5A",
                ["Negative"] = "#FFC42B1C"
            };
            var actual = typeof(ChartPalette).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(Brush)).ToDictionary(p => p.Name,
                    p => ChartPalette.ColorOf((Brush)p.GetValue(palette)!).ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
            return actual.Count == expected.Count && expected.All(e => actual.GetValueOrDefault(e.Key) == e.Value)
                && palette.MassBands.Select(b => ChartPalette.ColorOf(b).ToString(CultureInfo.InvariantCulture))
                    .SequenceEqual(new[] { "#FF10428C", "#FF1F8F5A", "#FFC2410C" });
        }

    }
}
