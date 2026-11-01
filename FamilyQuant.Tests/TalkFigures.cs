using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// The conference-talk deliverable: the complete native plot pack and numbers file, produced from the REAL
    /// data through the application's own entry points and nothing else.
    ///
    /// WHY IT IS A GATED SECTION AND NOT PART OF THE SUITE. It re-extracts three 3 GB acquisitions at
    /// the go-forward 6-isotope ceiling, which is tens of minutes the first time, so it is behind
    /// <see cref="Argument"/> and a normal run never reaches it. Everything it produces is written, so
    /// a second run over warm caches is minutes and reproduces the same files byte for byte.
    ///
    /// NOTHING IS REIMPLEMENTED HERE. The clustering is <see cref="USearchTools.GenerateFCF"/>, the
    /// extraction is <see cref="FullXicComputer.ComputeFullXic"/> (which also runs the MS1 envelope
    /// detection and the per-run GC), the fractions are <see cref="NativeQuantFractions"/>, the family
    /// table is <see cref="FamilyEnvelopeQuant.BuildFromCuration"/>, and the exact plot pack is the
    /// production <see cref="PresentationFigures"/> list. This class only sequences them and writes the
    /// numbers down.
    /// </summary>
    internal static class TalkFigures
    {
        /// <summary>The argument that turns this section on. Anywhere on the command line, so it does
        /// not disturb the four positional data paths.</summary>
        public const string Argument = "--talk-figures";

        /// <summary>The argument that runs the FIVE de novo variants of the talk in one pass, each into its
        /// own subfolder of <see cref="TalkFolderName"/>. It loops <see cref="Variants"/> and calls
        /// <see cref="Run"/> once per variant; the baseline "only" variant reproduces the original
        /// <see cref="Argument"/> output exactly, byte for byte, into BrProt_Talk\de novo only. Like
        /// <see cref="Argument"/>, it re-extracts three acquisitions per variant and is heavy.</summary>
        public const string VariantsArgument = "--talk-variants";

        /// <summary>Runs only the scientifically requested PEAKS de-novo-only + DeepNovo exact-union
        /// variant and writes its spectrum comparison CSV directly in the supplied data directory.
        /// This reuses the same extraction caches as the full variant runner but avoids the other four
        /// variant computations.</summary>
        public const string OnlyPlusDeepNovoArgument = "--talk-only-plus-deepnovo";

        /// <summary>Fast, identification-only lane for the exact two-stage PEAKS de-novo-only /
        /// DeepNovo sequence-set evidence. It reads the source CSVs and PatternLab package but no RAW.</summary>
        public const string SourceVennArgument = "--talk-source-venn";

        /// <summary>The folder, under the data directory, the talk's artefacts are written into.</summary>
        public const string TalkFolderName = "BrProt_Talk";

        /// <summary>Optional absolute or relative output root for every talk lane. When set, the heavy
        /// variants, combined comparison, and all fast CSV-only figures write below this directory
        /// instead of the data directory's BrProt_Talk folder.</summary>
        public const string OutputRootEnvironmentVariable = "FAMILYQUANT_TALK_OUTPUT_ROOT";

        public const string FiguresFolderName = "figures";

        public const string NumbersFileName = "numbers.md";
        public const string OverlapStudyFileName = PresentationEvidencePack.CrosscheckCsvFile;
        public const string ConcordanceStudyFileName = PresentationEvidencePack.ConcordanceCsvFile;
        public const string PlotDataFileName = "presentation_plot_data.json";
        public const string ProjectFileName = "scientific_results.fqproj";
        public const string ReferenceEnvelopeDiagnosticsFileName = "reference_envelope_model_diagnostics.csv";

        /// <summary>Every data plot the current talk uses, in slide order.</summary>
        public static IReadOnlyList<string> FigureFiles { get; } = PresentationFigures.FileNames;

        private const string DatabaseName =
            "uniprotkb_taxonomy_id_8724_2026_06_25_ORDENADO_CONT123_NO-Redundancy.T-R";

        private const string Sepr2Name = "PLVI.sepr2";

        /// <summary>The USEARCH sequence identity the curation is built at.</summary>
        private const int ClusterIdentity = 70;

        /// <summary>
        /// The averagine rule of thumb for the first isotope: an average residue is about 111.1 Da and
        /// carries about 4.9 carbons, and C13 is 1.07% abundant, so M+1 / M+0 is roughly
        /// mass * 4.9 / 111.1 * 0.0107. It is used ONLY as context beside the mass-stratified within-bin
        /// comparison in the ratio answer, is labelled as an approximation wherever it is printed, and
        /// no production number stands on it.
        /// </summary>
        private const double AveragineM1PerDalton = 4.9 / 111.1 * 0.0107;

        /// <summary>The family count the exhaustive clustering of this T-R at 70% yields, the number the
        /// suite's own exhaustive section pins. It is asserted here because the cached .fcf carries no
        /// stamp of which pass produced it: a file left behind by a fast-only build would be reused
        /// silently and every family figure would stand on the wrong grouping (fast-only gives 365).</summary>
        private const int ExhaustiveFamiliesAt70 = 312;

        /// <summary>
        /// One de novo variant of the talk: the source (or sources, for the union), the folder its
        /// artefacts are written into under <see cref="TalkFolderName"/>, and the de novo directory its
        /// exports live under. The five the full run produces are <see cref="Variants"/>.
        /// </summary>
        public sealed class Variant
        {
            public Variant(string folderName, DeNovoSource primary,
                           IReadOnlyList<DeNovoSource>? additional = null)
            {
                FolderName = folderName;
                Primary = primary;
                Additional = additional;
            }

            public string FolderName { get; }
            public DeNovoSource Primary { get; }
            public IReadOnlyList<DeNovoSource>? Additional { get; }
        }

        /// <summary>
        /// The five de novo variants a full run produces, each into its own subfolder of
        /// <see cref="TalkFolderName"/>. "only" reproduces the original baseline exactly. The union
        /// (all + DeepNovo) concatenates the two source globs under one de novo tree, each read with its
        /// own confidence column (ALC for de novo all, SCORE for DeepNovo). A caller drives them by
        /// passing each variant's <see cref="Variant.Primary"/> / <see cref="Variant.Additional"/> and
        /// <see cref="Variant.FolderName"/> into <see cref="Run"/>; the multi-hour extraction itself is
        /// not run here.
        /// </summary>
        public static IReadOnlyList<Variant> Variants { get; } = new[]
        {
            new Variant(DeNovoSource.DenovoOnly.DisplayName, DeNovoSource.DenovoOnly),
            new Variant(DeNovoSource.DeNovoAll.DisplayName, DeNovoSource.DeNovoAll),
            new Variant(DeNovoSource.DeepNovo.DisplayName, DeNovoSource.DeepNovo),
            new Variant(DeNovoVariantComparison.OnlyPlusDeepNovoVariantName,
                DeNovoSource.DenovoOnly, new[] { DeNovoSource.DeepNovo }),
            new Variant(DeNovoVariantComparison.AllPlusDeepNovoVariantName,
                DeNovoSource.DeNovoAll, new[] { DeNovoSource.DeepNovo }),
        };

        public static void Run(Action<string, bool, string?> check,
                               Func<string, Color> pngCornerColour,
                               Func<string, (int Width, int Height)> pngPixelSize,
                               string? peaksDir, string? desktopDir, string workDir,
                               DeNovoSource? deNovoSource = null,
                               IReadOnlyList<DeNovoSource>? additionalDeNovoSources = null,
                               string? variantFolderName = null,
                               string? comparisonGenerationId = null,
                               string? spectrumComparisonCsvPath = null)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            deNovoSource ??= DeNovoSource.DenovoOnly;
            comparisonGenerationId ??= Guid.NewGuid().ToString("N");

            // ---- the inputs ---------------------------------------------------------------------------
            if (string.IsNullOrEmpty(desktopDir) || !Directory.Exists(desktopDir))
            {
                check("talk: the data directory was given (positional argument 4)", false, desktopDir);
                return;
            }

            string sepr2 = Path.Combine(desktopDir, Sepr2Name);
            string database = Path.Combine(desktopDir, DatabaseName);
            string[] raws = Directory
                .GetFiles(desktopDir, "20220624_Bjararaca_1ug_3step-240min_*.raw", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            check("talk: PLVI.sepr2 is present", File.Exists(sepr2), sepr2);
            check("talk: the venom T-R is present", File.Exists(database), database);
            check("talk: all three acquisitions are present", raws.Length == 3,
                raws.Length.ToString(CultureInfo.InvariantCulture));
            check("talk: the PEAKS export directory is present",
                !string.IsNullOrEmpty(peaksDir) && Directory.Exists(peaksDir), peaksDir);

            if (!File.Exists(sepr2) || !File.Exists(database) || raws.Length != 3
                || string.IsNullOrEmpty(peaksDir) || !Directory.Exists(peaksDir)) return;

            // The output folder derives from the variant: the default (no folder given) writes straight
            // into BrProt_Talk, which is the original baseline location, byte for byte. A named variant
            // writes into BrProt_Talk\<variant>, so a full run keeps the five variants side by side.
            string outputRoot = ResolveTalkOutputRoot(desktopDir);
            string talkFolder = string.IsNullOrEmpty(variantFolderName)
                ? outputRoot
                : Path.Combine(outputRoot, variantFolderName);
            string figuresFolder = Path.Combine(talkFolder, FiguresFolderName);
            Directory.CreateDirectory(figuresFolder);

            // ---- 1. the curation, at 70% identity, reused from the work dir when it is already there ---
            Console.WriteLine("        clustering the venom T-R at " +
                ClusterIdentity.ToString(CultureInfo.InvariantCulture) + "% identity (exhaustive)...");

            (FamilyClusterFile? curation, string fcfPath) = EnsureCuration(database, workDir);
            check("talk: the curation was produced at " +
                ClusterIdentity.ToString(CultureInfo.InvariantCulture) + "% identity",
                curation != null, fcfPath);
            if (curation == null) return;

            int familyCount = FamilyFilter.CountFamilies(curation.MyExtendedFastaItems);
            var curationGroups = curation.GroupedFastaItems;
            int targetBearingFamilies = curationGroups.Count(g => g.Any(i =>
                !(i.SequenceIdentifier ?? string.Empty).StartsWith("Reverse_", StringComparison.Ordinal)));
            int nonContaminantTargetFamilies = curationGroups.Count(g => g.Any(i =>
                !(i.SequenceIdentifier ?? string.Empty).StartsWith("Reverse_", StringComparison.Ordinal)
                && !(i.SequenceIdentifier ?? string.Empty).StartsWith("contaminant_", StringComparison.OrdinalIgnoreCase)));
            int contaminantTargetFamilies = curationGroups.Count(g => g.Any(i =>
                !(i.SequenceIdentifier ?? string.Empty).StartsWith("Reverse_", StringComparison.Ordinal)
                && (i.SequenceIdentifier ?? string.Empty).StartsWith("contaminant_", StringComparison.OrdinalIgnoreCase)));
            int pureDecoyFamilies = curationGroups.Count - targetBearingFamilies;
            Console.WriteLine("        curation: " +
                curation.MyExtendedFastaItems.Count.ToString("N0", CultureInfo.InvariantCulture) +
                " protein row(s) in " + familyCount.ToString("N0", CultureInfo.InvariantCulture) + " families");

            // The exhaustive pass really ran: a fast-only .fcf left over from an older build would give
            // 365 here and every family number below would stand on the wrong grouping.
            check("talk: the curation is the EXHAUSTIVE clustering, which pins " +
                ExhaustiveFamiliesAt70.ToString(CultureInfo.InvariantCulture) + " families at 70%",
                familyCount == ExhaustiveFamiliesAt70, familyCount.ToString(CultureInfo.InvariantCulture));
            if (familyCount != ExhaustiveFamiliesAt70) return;
            check("talk: biological headline count excludes contaminants and decoys",
                nonContaminantTargetFamilies > 0 && nonContaminantTargetFamilies < familyCount,
                nonContaminantTargetFamilies.ToString(CultureInfo.InvariantCulture));

            // ---- 2, 3. the full XIC of all three runs, plus the MS1 envelope detection per run --------
            // Production settings: the 6-isotope CEILING and the 3-point minimum, one run at a time with
            // a GC between them, which is what ComputeFullXic itself does. An explicit cache directory is
            // accepted for independent regeneration; otherwise production cache placement is used.
            var options = new FullXicOptions
            {
                IsotopeCount = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount,
                MinPointsForQuant = DeNovoXicExtractor.DefaultMinPointsForQuant,
                AppVersion = "talk-figures",
                FamilyCuration = curation,
                DeNovoSource = deNovoSource,
                AdditionalDeNovoSources = additionalDeNovoSources,
                SpectrumComparisonCsvPath = string.IsNullOrWhiteSpace(spectrumComparisonCsvPath)
                    ? Path.Combine(talkFolder, SpectrumComparisonTable.DefaultFileName)
                    : Path.GetFullPath(spectrumComparisonCsvPath),
            };
            var requestedSources = new List<DeNovoSource> { deNovoSource };
            if (additionalDeNovoSources != null) requestedSources.AddRange(additionalDeNovoSources);
            options.DeNovoSelections = requestedSources.Select((source, priority) =>
                new DeNovoSourceSelection(source, options.DeNovoCriteria.MinAverageLocalConfidence, priority))
                .ToList();

            check("talk: the isotope ceiling is the production 6", options.IsotopeCount == 6,
                options.IsotopeCount.ToString(CultureInfo.InvariantCulture));
            check("talk: the minimum points per peak is the production 3", options.MinPointsForQuant == 3,
                options.MinPointsForQuant.ToString(CultureInfo.InvariantCulture));

            DateTime startedExtraction = DateTime.UtcNow;
            FullXicComputeResult store;
            string? cacheDirectory = Environment.GetEnvironmentVariable("FAMILYQUANT_TALK_CACHE_DIR");
            Func<string, string, string>? cachePathFor = null;
            Func<string, string>? detectedCachePathFor = null;
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
                // The de-novo-side full XIC depends on WHICH de novo source drives it, so the fullxic cache
                // must be keyed by the variant or the four --talk-variants runs would reuse variant 1's
                // de novo extraction. The baseline (no variant folder) keeps the original filename exactly.
                // The MS1 envelope is RAW-only, independent of the de novo source, so it stays shared.
                string variantToken = string.IsNullOrEmpty(variantFolderName)
                    ? string.Empty
                    : SafeFileName(variantFolderName) + "_";
                cachePathFor = (side, run) => Path.Combine(cacheDirectory,
                    "fullxic_" + variantToken + side + "_" + SafeFileName(run) + ".json");
                detectedCachePathFor = run => Path.Combine(cacheDirectory,
                    "ms1envelope_" + SafeFileName(run) + ".json");
                Console.WriteLine("        independent RAW cache: " + cacheDirectory);
            }
            try
            {
                store = FullXicComputer.ComputeFullXic(
                    desktopDir, sepr2, peaksDir, options,
                    cachePathFor: cachePathFor,
                    progress: m => Console.WriteLine("        " + m),
                    restrictToRuns: null,
                    detectedCachePathFor: detectedCachePathFor);
            }
            catch (Exception ex)
            {
                check("talk: the full XIC of all three runs was computed", false,
                    ex.GetType().Name + ": " + ex.Message);
                return;
            }

            Console.WriteLine("        extraction finished in " +
                (DateTime.UtcNow - startedExtraction).TotalMinutes.ToString("F1", CultureInfo.InvariantCulture) +
                " min");
            foreach (string note in store.Notes) Console.WriteLine("        " + note);

            check("talk: the store carries all three runs",
                store.Manifest.PerRunTic.Count == 3,
                string.Join(", ", store.Manifest.PerRunTic.Keys));
            check("talk: every run carries a resolved isotope-series diagnostic",
                store.Detected.Count == 3 && store.Detected.Values.All(d => d.DetectedPeptideLikeArea > 0),
                store.Detected.Count.ToString(CultureInfo.InvariantCulture));
            check("talk: the store carries rows on both sides",
                store.IdentifiedRows > 0 && store.DeNovoRows > 0,
                store.IdentifiedRows.ToString("N0", CultureInfo.InvariantCulture) + " id / " +
                store.DeNovoRows.ToString("N0", CultureInfo.InvariantCulture) + " dn");

            // ---- 4. the fractions, per run and grouped ------------------------------------------------
            AnalyticsData data = AnalyticsBuilder.Build(store.Rows, store.Manifest.PerRunTic);
            NativeQuantFractions.ApplyDetectedToFractions(data.Fractions, data.Grouped,
                                                          store.Manifest.DerivedFractions);

            NativeRunFraction? grouped = data.Grouped;
            check("talk: a grouped fraction over the three runs was built with a positive TIC",
                grouped != null && grouped.Tic > 0,
                grouped?.Tic.ToString("E4", CultureInfo.InvariantCulture));
            if (grouped == null) return;

            List<NativePeptideQuant> diagnosticPeptides = NativeQuantFractions.DedupToPeptides(store.Rows);
            ReferenceEnvelopeEstimator.Apply(diagnosticPeptides);
            ReferenceEnvelopeDiagnostics envelopeDiagnostics =
                ReferenceEnvelopeEstimator.Evaluate(diagnosticPeptides);
            string envelopeDiagnosticsPath = Path.Combine(talkFolder, ReferenceEnvelopeDiagnosticsFileName);
            File.WriteAllText(envelopeDiagnosticsPath,
                ReferenceEnvelopeEstimator.ToCsv(envelopeDiagnostics), new UTF8Encoding(false));
            check("talk: reference-envelope model diagnostics include support, cross-validation and sensitivity",
                envelopeDiagnostics.Bins.Count == ReferenceEnvelopeEstimator.MassBinUpperBounds.Length
                && envelopeDiagnostics.CrossValidatedPeptides > 0
                && File.Exists(envelopeDiagnosticsPath), envelopeDiagnosticsPath);

            check("talk: the grouped TIC is the sum of the three per-run TICs",
                Math.Abs(grouped.Tic - data.Fractions.Sum(f => f.Tic)) < 1e-6 * Math.Abs(grouped.Tic),
                grouped.Tic.ToString("R", CultureInfo.InvariantCulture));

            check("talk: primary estimate is distinct from both sensitivity readings",
                grouped.RecoveredFractionReferenceCalibrated >= grouped.RecoveredFractionM0
                && grouped.RecoveredFractionReferenceCalibrated <= grouped.RecoveredFractionEnvelope,
                grouped.RecoveredFractionM0.ToString("P3", CultureInfo.InvariantCulture) + " <= " +
                grouped.RecoveredFractionReferenceCalibrated.ToString("P3", CultureInfo.InvariantCulture) + " <= " +
                grouped.RecoveredFractionEnvelope.ToString("P3", CultureInfo.InvariantCulture));

            foreach (NativeRunFraction f in data.Fractions)
            {
                check("talk: " + f.Run + " detected = z>=2 part + z=1 part, bit for bit",
                    f.DetectedPeptideLikeArea == f.DetectedAreaMultiplyCharged + f.DetectedAreaSinglyCharged,
                    f.DetectedPeptideLikeArea.ToString("R", CultureInfo.InvariantCulture));
            }

            // ---- the family table (one homology search, reused by the figure and by the numbers) ------
            Console.WriteLine("        attributing the peptides to families by homology...");
            FamilyEnvelopeQuantResult families = FamilyEnvelopeQuant.BuildFromCuration(store.Rows, curation);
            check("talk: the family table names the identity its attribution stood on",
                families.IdentityThreshold > 0,
                families.IdentityThreshold.ToString("R", CultureInfo.InvariantCulture));
            check("talk: the family table carries at least ten families to rank",
                families.Families.Count >= 10, families.Families.Count.ToString(CultureInfo.InvariantCulture));

            // ---- 5. the figures ----------------------------------------------------------------------
            // The identity scan is recorded in the manifest before the plot pack is made, so reopening
            // the .fqproj is sufficient to regenerate every figure without a PowerPoint-only number.
            List<IdentityScanRow> sweep = EnsureSweep(database, workDir);
            check("talk: the identity sweep carries the curve it is drawn from",
                sweep.Count > 3, sweep.Count.ToString(CultureInfo.InvariantCulture));
            store.Manifest.PresentationIdentitySweep = sweep.Select(r => new IdentityScanRow
            {
                Identity = r.Identity,
                Groups = r.Groups,
                TargetGroups = r.TargetGroups,
                NonContaminantTargetGroups = r.NonContaminantTargetGroups,
                MixedGroups = r.MixedGroups,
                Score = r.Score,
                TargetRelativeScore = r.TargetRelativeScore,
            }).ToList();

            DeNovoScoreDistribution? scoreDistribution = store.Manifest.DeNovoScoreDistribution;
            List<DeNovoSourceScoreDistribution> sourceDistributions =
                store.Manifest.DeNovoSourceScoreDistributions ?? new List<DeNovoSourceScoreDistribution>();
            check("talk: one separately labelled score distribution is stored per de novo source",
                scoreDistribution != null
                && sourceDistributions.Count == options.ResolveDeNovoSelections().Count
                && sourceDistributions.All(d => !string.IsNullOrWhiteSpace(d.ConfidenceField)),
                sourceDistributions.Count.ToString(CultureInfo.InvariantCulture));
            if (scoreDistribution == null) return;
            scoreDistribution.Validate();
            foreach (DeNovoSourceScoreDistribution distribution in sourceDistributions) distribution.Validate();
            DeNovoIdentificationSummary? identificationSummary = store.Manifest.DeNovoIdentification;
            int acceptedAcrossSourceInputs = sourceDistributions.Sum(d => d.RowsPassingAllCriteria);
            // RowsPassingAllCriteria is the source-file row grain.  FilteredSpectrumCalls is the
            // deterministic call grain after source-aware union resolution, so repeated rows can collapse
            // even when only one source preset is selected.  Equality is therefore not a valid one-source
            // invariant; the resolved count must simply be positive and cannot exceed the source-pass rows.
            bool acceptedCallCountCloses = identificationSummary != null
                && identificationSummary.FilteredSpectrumCalls > 0
                && identificationSummary.FilteredSpectrumCalls <= acceptedAcrossSourceInputs;
            check("talk: the identification-only de novo consolidation is stored after source filtering and union resolution",
                identificationSummary != null
                && acceptedCallCountCloses
                && identificationSummary.CarriedForwardDistinctSequencesIgnoringPtm > 0,
                identificationSummary == null ? "missing" :
                    identificationSummary.FilteredSpectrumCalls.ToString("N0", CultureInfo.InvariantCulture) + " calls / " +
                    acceptedAcrossSourceInputs.ToString("N0", CultureInfo.InvariantCulture) + " source-pass rows / " +
                    identificationSummary.CarriedForwardDistinctSequencesIgnoringPtm.ToString("N0", CultureInfo.InvariantCulture) + " sequences");
            if (identificationSummary == null) return;
            identificationSummary.Validate();

            PresentationEvidencePackResult evidence = PresentationEvidencePack.Save(
                data, store.Manifest, curation, families, talkFolder);
            List<string> written = evidence.FigurePaths;

            var expectedFigureFiles = FigureFiles.ToList();
            if (store.Manifest.DeNovoCallerAgreement != null)
                expectedFigureFiles.Add(PresentationFigures.DeNovoAgreementFile);
            if (store.Manifest.DeNovoSourceVenn != null)
                expectedFigureFiles.Add(PresentationFigures.DeNovoSourceVennFile);
            // The evidence pack writes the accepted-score scatter, in this order, whenever the persisted
            // scatter carries at least one accepted dual-source point. The gate omitted it, so a dual-source
            // variant that legitimately produced the PNG failed the SequenceEqual though the file existed.
            if (store.Manifest.DeNovoAcceptedScoreScatter != null
                && store.Manifest.DeNovoAcceptedScoreScatter.Total > 0)
                expectedFigureFiles.Add(PresentationFigures.DeNovoAcceptedScoreScatterFile);
            if (store.Manifest.PresentationDeNovoVariants != null
                && store.Manifest.PresentationDeNovoVariants.Count ==
                    DeNovoVariantComparison.CurrentTalkVariantCount)
            {
                expectedFigureFiles.Add(PresentationFigures.DeNovoVariantComparisonFile);
                expectedFigureFiles.Add(PresentationFigures.DeNovoTalkComparisonFile);
            }

            check("talk: the complete native plot pack was written, under the documented names",
                written.Select(Path.GetFileName).SequenceEqual(expectedFigureFiles),
                string.Join(", ", written.Select(Path.GetFileName)));

            Color lightGround = ChartPalette.ColorOf(ChartPalette.Light.Backdrop);
            foreach (string path in written)
            {
                long size = File.Exists(path) ? new FileInfo(path).Length : 0;
                (int w, int h) = size > 0 ? pngPixelSize(path) : (0, 0);
                Console.WriteLine("        figure " + path + "  " +
                    w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture) +
                    "  " + size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");

                check("talk: " + Path.GetFileName(path) + " exists and is not a stub",
                    size > 3000, size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
                if (size <= 0) continue;

                check("talk: " + Path.GetFileName(path) + " is the presentation size 1920x1080",
                    w == 1920 && h == 1080,
                    w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture));

                Color corner = pngCornerColour(path);
                check("talk: " + Path.GetFileName(path) + " has the LIGHT ground, sampled off the file",
                    corner == lightGround, corner.ToString() + " vs " + lightGround.ToString());
                check("talk: " + Path.GetFileName(path) + " ground is near-white when measured",
                    ChartPalette.RelativeLuminance(new SolidColorBrush(corner)) > 0.90,
                    ChartPalette.RelativeLuminance(new SolidColorBrush(corner))
                        .ToString("F4", CultureInfo.InvariantCulture));
            }

            // The evidence-pack exporter writes the exact tabular sources used by every plot beside
            // the figures. These are the same summaries persisted in the .fqproj.
            DeNovoOverlapSummary? overlap = store.Manifest.DeNovoOverlap;
            string overlapPath = Path.Combine(talkFolder, OverlapStudyFileName);
            check("talk: de novo/search-engine overlap study was written with explicit denominators",
                overlap != null && File.Exists(overlapPath)
                && File.ReadAllText(overlapPath).Contains("shared_file_scan_different_reported_charge")
                && File.ReadAllText(overlapPath).Contains("same_scan_charge_no_psm_within_mz_tolerance"),
                overlapPath);

            string concordancePath = Path.Combine(talkFolder, ConcordanceStudyFileName);
            check("talk: independent database-search concordance was written separately",
                store.Manifest.IdentificationConcordance != null && File.Exists(concordancePath)
                && File.ReadAllText(concordancePath).Contains("strict_sequence_match_ptm_removed"),
                concordancePath);

            string sourceVennPath = Path.Combine(talkFolder,
                PresentationEvidencePack.DeNovoSourceVennCsvFile);
            if (store.Manifest.DeNovoSourceVenn != null)
            {
                string sourceVennCsv = File.Exists(sourceVennPath)
                    ? File.ReadAllText(sourceVennPath) : string.Empty;
                check("talk: exact source Venn CSV is generated from the focused union project",
                    File.Exists(sourceVennPath)
                    && sourceVennCsv.Contains("filtered-before-union")
                    && sourceVennCsv.Contains("carried-forward-claim-provenance")
                    && sourceVennCsv.Contains("not inferred from separate source-variant totals"),
                    sourceVennPath);
            }

            string plotDataPath = Path.Combine(talkFolder, PlotDataFileName);
            check("talk: every plotted number was written as a machine-readable supplement source",
                File.Exists(plotDataPath)
                && File.ReadAllText(plotDataPath).Contains("databaseSearchConcordance")
                && File.ReadAllText(plotDataPath).Contains("equalChargeRequired")
                && File.ReadAllText(plotDataPath).Contains("deNovoScoreDistribution")
                && File.ReadAllText(plotDataPath).Contains("familyAttribution"),
                plotDataPath);
            string identificationPath = Path.Combine(talkFolder, PresentationEvidencePack.IdentificationCsvFile);
            check("talk: identification-only consolidation was written separately from XIC representatives",
                File.Exists(identificationPath)
                && File.ReadAllText(identificationPath).Contains("imported,spectrum_row")
                && File.ReadAllText(identificationPath).Contains("credited,run_peptidoform_charge_mass_cluster")
                && File.ReadAllText(identificationPath).Contains("pooled_distinct_sequence")
                && File.ReadAllText(identificationPath).Contains("no XIC requirement"),
                identificationPath);
            string inputsPath = Path.Combine(talkFolder, PresentationEvidencePack.DeNovoInputsCsvFile);
            check("talk: every de novo input and its source-native cutoff was exported",
                File.Exists(inputsPath)
                && File.ReadAllText(inputsPath).Contains("confidence_field")
                && File.ReadAllText(inputsPath).Contains("sha256"), inputsPath);
            string sourceScoresPath = Path.Combine(talkFolder, PresentationEvidencePack.SourceScoreCsvFile);
            string sourceScoresJsonPath = Path.Combine(talkFolder, PresentationEvidencePack.SourceScoreJsonFile);
            check("talk: source score distributions were exported separately as CSV and JSON",
                File.Exists(sourceScoresPath) && File.Exists(sourceScoresJsonPath)
                && File.ReadAllText(sourceScoresPath).Contains("source_rows_passing_all_filters")
                && File.ReadAllText(sourceScoresJsonPath).Contains("confidenceField"),
                sourceScoresPath + " / " + sourceScoresJsonPath);
            string familySourcePath = Path.Combine(talkFolder,
                PresentationEvidencePack.FamilySourceSequenceCsvFile);
            check("talk: source-resolved family distinct-sequence counts were exported at their stated grain",
                File.Exists(familySourcePath)
                && File.ReadAllText(familySourcePath).Contains(
                    "peaks_denovo_only_exclusive_distinct_sequences")
                && File.ReadAllText(familySourcePath).Contains(
                    "shared_peaks_deepnovo_distinct_sequences")
                && File.ReadAllText(familySourcePath).Contains("accepted_tier"), familySourcePath);
            check("talk: the evidence pack owns the reference-envelope model diagnostic table",
                evidence.DataPaths.Any(path => string.Equals(Path.GetFileName(path),
                    PresentationEvidencePack.ReferenceEnvelopeDiagnosticsCsvFile,
                    StringComparison.OrdinalIgnoreCase))
                && File.Exists(envelopeDiagnosticsPath)
                && File.ReadAllText(envelopeDiagnosticsPath).Contains(
                    "cross_validated_weighted_absolute_percent_error"),
                envelopeDiagnosticsPath);

            // ---- 6 and 7. the numbers file, including the envelope/M+0 ratio answer -------------------
            string numbersPath = Path.Combine(talkFolder, NumbersFileName);
            string markdown = BuildNumbers(data, grouped, families, store, options, sweep,
                                           curation, familyCount, targetBearingFamilies,
                                           nonContaminantTargetFamilies, contaminantTargetFamilies,
                                           pureDecoyFamilies, fcfPath, figuresFolder, written,
                                           envelopeDiagnostics);
            File.WriteAllText(numbersPath, markdown, new UTF8Encoding(false));

            check("talk: numbers.md was written and carries every required block",
                File.Exists(numbersPath)
                && markdown.Contains("## Grouped")
                && markdown.Contains("## Per run")
                && markdown.Contains("## Independent database-search concordance")
                && markdown.Contains("## De novo/search-engine overlap")
                && markdown.Contains("Isotopes credited")
                && markdown.Contains("Top 10 families")
                && markdown.Contains("envelope / M+0"),
                numbersPath);

            Console.WriteLine("        numbers written to " + numbersPath);

            string projectPath = Path.Combine(talkFolder, ProjectFileName);
            ProjectStore.WriteFqproj(projectPath, curation, store.Rows, store.Manifest);
            var reopened = ProjectStore.ReadFqproj(projectPath);
            check("talk: machine-readable scientific project round-trips",
                reopened.quant.Count == store.Rows.Count
                && reopened.manifest.DerivedFractions.Count == store.Manifest.DerivedFractions.Count,
                projectPath);

            IReadOnlyList<DeNovoSource> sources = SourcesOf(options);
            var variantSummary = new DeNovoVariantSummary
            {
                Variant = string.IsNullOrEmpty(variantFolderName)
                    ? string.Join(" + ", sources.Select(s => s.DisplayName)) : variantFolderName,
                GenerationId = comparisonGenerationId,
                GeneratedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                DatasetFingerprint = CompositeFingerprint(
                    store.Manifest.ScientificInputFingerprints
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(kv => "scientific=" + kv.Value)
                        .Concat(new[]
                        {
                            "database=" + FileFingerprint.Sha256(database),
                            "curation=" + FileFingerprint.Sha256(fcfPath),
                        })),
                DeNovoInputFingerprint = CompositeFingerprint(
                    store.Manifest.DeNovoInputs
                        .OrderBy(i => i.SourcePriority)
                        .ThenBy(i => i.SourceId, StringComparer.Ordinal)
                        .ThenBy(i => i.FilePath, StringComparer.OrdinalIgnoreCase)
                        .Select(i => i.SourceId + "|" + i.ConfidenceField + "|" +
                            i.MinimumConfidence.ToString("R", CultureInfo.InvariantCulture) + "|" + i.Sha256)),
                QuantitationSettings = "isotopes=" + store.Manifest.IsotopeCount.ToString(CultureInfo.InvariantCulture) +
                    ";min_points=" + store.Manifest.MinPointsForQuant.ToString(CultureInfo.InvariantCulture) +
                    ";ppm=" + store.Manifest.Ppm.ToString("R", CultureInfo.InvariantCulture) +
                    ";ms1_tolerance=" + store.Manifest.Ms1Tolerance.ToString(CultureInfo.InvariantCulture) +
                    ";grain=" + NativeQuantFractions.Method,
                AppVersion = store.Manifest.AppVersion,
                Sources = string.Join(" + ", sources.Select(s => s.DisplayName)),
                ConfidenceMetrics = string.Join(" + ", sources.Select(ConfidenceMetric).Distinct(StringComparer.Ordinal)),
                ConfidenceCutoffNote = ConfidenceCutoffNote(options),
                IdentifiedFractionOfTic = grouped.IdentifiedFractionEnvelope,
                DeNovoModelEstimateFractionOfTic = grouped.DeNovoFractionReferenceCalibrated,
                CombinedModelEstimateFractionOfTic = grouped.RecoveredFractionReferenceCalibrated,
                RelativeUplift = grouped.IdentifiedFractionEnvelope > 0
                    ? grouped.DeNovoFractionReferenceCalibrated / grouped.IdentifiedFractionEnvelope : 0.0,
                SearchRepresentatives = grouped.IdentifiedPeptides,
                DeNovoRepresentatives = grouped.DeNovoPeptides,
                TotalRepresentatives = grouped.IdentifiedPeptides + grouped.DeNovoPeptides,
                AcceptedDeNovoSpectrumCalls = identificationSummary.FilteredSpectrumCalls,
                DistinctDeNovoSequencesNoXic = identificationSummary.CarriedForwardDistinctSequencesIgnoringPtm,
                CrossSideSuppressed = grouped.DeNovoSuppressedCrossSideOverlaps,
                EnvelopeModelMethod = ReferenceEnvelopeEstimator.Method,
                EnvelopeModelCrossValidatedWape =
                    envelopeDiagnostics.CrossValidatedWeightedAbsolutePercentError,
                CombinedSensitivityLowerFractionOfTic = grouped.Tic > 0
                    ? (grouped.IdentifiedEnvelope + envelopeDiagnostics.SensitivityLowerEstimate) / grouped.Tic : 0.0,
                CombinedSensitivityUpperFractionOfTic = grouped.Tic > 0
                    ? (grouped.IdentifiedEnvelope + envelopeDiagnostics.SensitivityUpperEstimate) / grouped.Tic : 0.0,
            };
            variantSummary.Validate();
            string variantSummaryPath = Path.Combine(talkFolder, DeNovoVariantComparison.PerVariantFileName);
            DeNovoVariantComparison.Write(variantSummaryPath, new[] { variantSummary });
            check("talk: variant summary is machine-readable and closes arithmetically",
                File.Exists(variantSummaryPath)
                && DeNovoVariantComparison.Read(variantSummaryPath).Count == 1, variantSummaryPath);

        }

        /// <summary>Collects the five completed heavy-run summaries and writes the FamilyQuant-native
        /// comparison CSV, Markdown and PNG into BrProt_Talk. No PowerPoint-side arithmetic is involved.</summary>
        public static void WriteVariantComparison(Action<string, bool, string?> check,
                                                   Func<string, (int Width, int Height)> pngPixelSize,
                                                   string? desktopDir,
                                                   string? comparisonGenerationId = null)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            if (string.IsNullOrWhiteSpace(desktopDir))
            {
                check("variant comparison: data directory was given", false, desktopDir);
                return;
            }

            string talkFolder = ResolveTalkOutputRoot(desktopDir);
            var rows = new List<DeNovoVariantSummary>();
            foreach (Variant variant in Variants)
            {
                string path = Path.Combine(talkFolder, variant.FolderName,
                    DeNovoVariantComparison.PerVariantFileName);
                if (!File.Exists(path))
                {
                    check("variant comparison: summary exists for " + variant.FolderName, false, path);
                    continue;
                }
                rows.AddRange(DeNovoVariantComparison.Read(path));
            }
            if (rows.Count != Variants.Count) return;

            try
            {
                DeNovoVariantComparison.ValidateTalkVariantSet(rows, comparisonGenerationId);
            }
            catch (Exception ex)
            {
                check("variant comparison: all five summaries are fresh and provenance-matched",
                    false, ex.Message);
                return;
            }
            check("variant comparison: all five summaries are fresh and provenance-matched",
                true, comparisonGenerationId);

            string csv = Path.Combine(talkFolder, DeNovoVariantComparison.CsvFileName);
            string md = Path.Combine(talkFolder, DeNovoVariantComparison.MarkdownFileName);
            DeNovoVariantComparison.Write(csv, rows);
            File.WriteAllText(md, DeNovoVariantComparison.ToMarkdown(rows), new UTF8Encoding(false));
            string figure = PresentationFigures.SaveVariantComparison(rows,
                Path.Combine(talkFolder, FiguresFolderName));
            (int width, int height) = pngPixelSize(figure);
            check("variant comparison: CSV, Markdown and native PNG were written",
                File.Exists(csv) && File.Exists(md) && File.Exists(figure), figure);
            check("variant comparison: native PNG is 1920x1080", width == 1920 && height == 1080,
                width.ToString(CultureInfo.InvariantCulture) + "x" +
                height.ToString(CultureInfo.InvariantCulture));

            List<DeNovoTalkComparisonRow> simple = DeNovoTalkComparison.Build(rows);
            string simpleCsv = Path.Combine(talkFolder, DeNovoTalkComparison.CsvFileName);
            string simpleMarkdown = Path.Combine(talkFolder, DeNovoTalkComparison.MarkdownFileName);
            File.WriteAllText(simpleCsv, DeNovoTalkComparison.ToCsv(simple), new UTF8Encoding(false));
            File.WriteAllText(simpleMarkdown, DeNovoTalkComparison.ToMarkdown(simple),
                new UTF8Encoding(false));
            string simpleFigure = PresentationFigures.SaveDeNovoTalkComparison(simple,
                Path.Combine(talkFolder, FiguresFolderName));
            (int simpleWidth, int simpleHeight) = pngPixelSize(simpleFigure);
            check("variant comparison: compact CSV, Markdown and native PNG were written",
                File.Exists(simpleCsv) && File.Exists(simpleMarkdown) && File.Exists(simpleFigure)
                && File.ReadAllText(simpleCsv).Contains("PEAKS de novo-only + DeepNovo")
                && !File.ReadAllText(simpleCsv).Contains("de novo all + DeepNovo"), simpleFigure);
            check("variant comparison: compact native PNG is 1920x1080",
                simpleWidth == 1920 && simpleHeight == 1080,
                simpleWidth.ToString(CultureInfo.InvariantCulture) + "x" +
                simpleHeight.ToString(CultureInfo.InvariantCulture));

            // Store the complete comparison back into every generated scientific project. The loaded-
            // project evidence exporter can now regenerate this CSV/Markdown/PNG without inspecting the
            // five output folders or accepting a stale summary from an earlier invocation.
            foreach (Variant variant in Variants)
            {
                string projectPath = Path.Combine(talkFolder, variant.FolderName, ProjectFileName);
                if (!File.Exists(projectPath))
                {
                    check("variant comparison: project exists for persistence: " + variant.FolderName,
                        false, projectPath);
                    continue;
                }
                var saved = ProjectStore.ReadFqproj(projectPath);
                saved.manifest.PresentationDeNovoVariants = rows.Select(CloneVariantSummary).ToList();
                ProjectStore.WriteFqproj(projectPath, saved.fcf, saved.quant, saved.manifest);
                check("variant comparison: complete comparison persisted in " + variant.FolderName,
                    ProjectStore.ReadFqproj(projectPath).manifest.PresentationDeNovoVariants.Count == rows.Count,
                    projectPath);
            }
        }

        // ================================================================= the score sweep figure

        /// <summary>The flag that turns the score-sweep figure on. Its own lane like the talk figures,
        /// but FAST: it is pure sequence bookkeeping over the CSVs, no XIC, no RAW.</summary>
        public const string SweepArgument = "--sweep-figure";

        /// <summary>The flag that turns the PEAKS-vs-DeepNovo per-spectrum agreement figure on. Its own lane
        /// like the score sweep, and equally FAST: pure sequence bookkeeping over the CSVs, no XIC, no RAW.</summary>
        public const string AgreementArgument = "--agreement-figure";

        /// <summary>The flag that turns the score-vs-score dot plot on. Its own lane like the sweep and the
        /// agreement figure, and equally FAST: pure sequence bookkeeping over the CSVs, no XIC, no RAW.</summary>
        public const string ScatterArgument = "--score-scatter-figure";

        /// <summary>Fast project-native lane for the accepted-call score/match scatter. It reads the
        /// persisted spectrum comparison rows from an .fqproj, so its dots use exactly the DataGrid join
        /// and acceptance flags rather than reconstructing them from a second CSV parser.</summary>
        public const string AcceptedScoreScatterArgument = "--accepted-score-scatter";

        /// <summary>The flag that turns the UpSet-style set-novelty figure on. Its own lane, and equally
        /// FAST: pure sequence bookkeeping over the CSVs, no XIC, no RAW.</summary>
        public const string SetNoveltyArgument = "--set-novelty-figure";

        /// <summary>The flag that turns the PEAKS de novo ALC score histogram on. Its own lane, and equally
        /// FAST: pure per-spectrum census over the CSVs, no XIC, no RAW. It reuses the shared
        /// <see cref="DeNovoScoreDistributionStudy"/> census and the production
        /// <see cref="DeNovoScoreDistributionChart"/>, so it never re-derives bins or re-draws a chart.</summary>
        public const string ScoreDistributionArgument = "--score-distribution-figure";

        /// <summary>
        /// Builds the score-sweep-with-overlap figure from the three de novo sources' CSVs alone (no XIC),
        /// writes the per-threshold numbers beside it, renders the two-panel PNG, and self-checks the t=80
        /// distinct clean-sequence counts against the known anchors. The base directory must contain the
        /// three sources somewhere under it (the three globs are distinct, so one recursive search per
        /// source picks the right files).
        /// </summary>
        public static void RunScoreSweep(Action<string, bool, string?> check,
                                         Func<string, (int Width, int Height)> pngPixelSize,
                                         string? baseDirectory, string? outputDirectory)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));

            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            {
                check("sweep: the de novo base directory was given (positional argument 3)", false, baseDirectory);
                return;
            }

            DeNovoSource refSource = DeNovoSource.DenovoOnly;
            DeNovoSource allSource = DeNovoSource.DeNovoAll;
            DeNovoSource deepSource = DeNovoSource.DeepNovo;

            List<PeaksDeNovoRow> refRows = ReadSource(baseDirectory, refSource);
            List<PeaksDeNovoRow> allRows = ReadSource(baseDirectory, allSource);
            List<PeaksDeNovoRow> deepRows = ReadSource(baseDirectory, deepSource);

            check("sweep: the de novo only reference files were found", refRows.Count > 0,
                refRows.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            check("sweep: the de novo all files were found", allRows.Count > 0,
                allRows.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            check("sweep: the DeepNovo files were found", deepRows.Count > 0,
                deepRows.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            if (refRows.Count == 0 || allRows.Count == 0 || deepRows.Count == 0) return;

            DeNovoScoreSweep sweep = DeNovoScoreSweepStudy.Build(
                refRows, refSource, allRows, allSource, deepRows, deepSource,
                referenceCutoff: DeNovoScoreSweepStudy.DefaultReferenceCutoff,
                min: 50, max: 100, step: 5);

            // ---- self-check against the known anchors (see invest_0.md) ---------------------------
            check("sweep: the de novo only reference at ALC >= 80 is 23,718 distinct clean sequences",
                sweep.ReferenceCleanSequences == 23718,
                sweep.ReferenceCleanSequences.ToString(CultureInfo.InvariantCulture));

            DeNovoSweepPoint? allAt80 = sweep.DeNovoAll.Points.FirstOrDefault(p => p.Threshold == 80);
            check("sweep: de novo all at t=80 is 28,164 distinct clean sequences",
                allAt80 != null && allAt80.DistinctCleanTotal == 28164,
                allAt80 == null ? "missing" : allAt80.DistinctCleanTotal.ToString(CultureInfo.InvariantCulture));

            DeNovoSweepPoint? deepAt80 = sweep.DeepNovo.Points.FirstOrDefault(p => p.Threshold == 80);
            check("sweep: DeepNovo at t=80 is 3,460 distinct clean sequences",
                deepAt80 != null && deepAt80.DistinctCleanTotal == 3460,
                deepAt80 == null ? "missing" : deepAt80.DistinctCleanTotal.ToString(CultureInfo.InvariantCulture));

            // ---- write the inspectable per-threshold numbers --------------------------------------
            string outFolder = string.IsNullOrEmpty(outputDirectory)
                ? Path.Combine(ResolveTalkOutputRoot(baseDirectory), "sweep")
                : outputDirectory;
            Directory.CreateDirectory(outFolder);

            string csvPath = Path.Combine(outFolder, "de_novo_score_sweep_overlap.csv");
            File.WriteAllText(csvPath, DeNovoScoreSweepStudy.BuildTableCsv(sweep), new UTF8Encoding(false));
            check("sweep: the per-threshold csv was written", File.Exists(csvPath), csvPath);

            string mdPath = Path.Combine(outFolder, "de_novo_score_sweep_overlap.md");
            File.WriteAllText(mdPath, DeNovoScoreSweepStudy.BuildTableMarkdown(sweep), new UTF8Encoding(false));
            check("sweep: the per-threshold markdown was written", File.Exists(mdPath), mdPath);

            // ---- render the figure (no XIC) -------------------------------------------------------
            string figurePath;
            try
            {
                figurePath = PresentationFigures.SaveScoreSweep(sweep, outFolder);
            }
            catch (Exception ex)
            {
                check("sweep: the figure rendered", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            long size = File.Exists(figurePath) ? new FileInfo(figurePath).Length : 0;
            (int w, int h) = size > 0 ? pngPixelSize(figurePath) : (0, 0);
            Console.WriteLine("        sweep figure " + figurePath + "  " +
                w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture) +
                "  " + size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
            check("sweep: the figure exists and is not a stub", size > 3000,
                size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
            check("sweep: the figure is the presentation size 1920x1080", w == 1920 && h == 1080,
                w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("        sweep numbers written to " + csvPath + " and " + mdPath);
        }

        private static List<PeaksDeNovoRow> ReadSource(string baseDirectory, DeNovoSource source)
        {
            var rows = new List<PeaksDeNovoRow>();
            foreach (string csv in Directory
                .GetFiles(baseDirectory, source.FileGlob, SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal))
                rows.AddRange(PeaksCsvReader.ReadDeNovoOnly(csv, source.ConfidenceColumn));
            return rows;
        }

        // ================================================================= the score distribution figure

        /// <summary>
        /// Renders the PEAKS de novo ALC score histogram (single panel) plus the two source-specific
        /// confidence panels (PEAKS de novo only ALC and DeepNovo SCORE), from the de novo export CSVs
        /// alone (no XIC, no RAW), writes an auditable per-bin CSV beside them, and self-checks the raw
        /// and retained totals against the known real-data anchors. The base directory must contain the
        /// PEAKS de novo only and DeepNovo exports somewhere under it (each source's glob is distinct, so
        /// one recursive search per source picks the right files). The acceptance rule is the SAME canonical
        /// de novo criteria the --talk-only-plus-deepnovo lane uses: length >= 7; source-native confidence
        /// >= 80; 5 consecutive local confidences >= 90. Nothing is reimplemented: the census is the shared
        /// <see cref="DeNovoScoreDistributionStudy"/> / <see cref="DeNovoSourceScoreDistributionStudy"/> and
        /// the figure is the production <see cref="DeNovoScoreDistributionChart"/>.
        /// </summary>
        public static void RunScoreDistribution(Action<string, bool, string?> check,
                                                Func<string, (int Width, int Height)> pngPixelSize,
                                                string? baseDirectory, string? outputDirectory)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));

            string effectiveBase = string.IsNullOrWhiteSpace(baseDirectory)
                ? @"C:\Users\paulo\Desktop\FamilyQuant"
                : baseDirectory;
            if (!Directory.Exists(effectiveBase))
            {
                check("score distribution: the de novo export directory exists (positional argument 3)",
                    false, effectiveBase);
                return;
            }

            // The canonical de novo acceptance rule, identical to the --talk-only-plus-deepnovo lane:
            // length >= 7; source-native confidence >= 80; 5 consecutive local confidences >= 90. It is the
            // DeNovoFilterCriteria defaults, and the >= 80 cutoff travels through DeNovoSourceSelection.
            var criteria = new DeNovoFilterCriteria();
            double cutoff = criteria.MinAverageLocalConfidence;

            List<PeaksDeNovoRow> peaks = ReadSource(effectiveBase, DeNovoSource.DenovoOnly);
            List<PeaksDeNovoRow> deep = ReadSource(effectiveBase, DeNovoSource.DeepNovo);

            check("score distribution: the PEAKS de novo only files were found", peaks.Count > 0,
                peaks.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            check("score distribution: the DeepNovo files were found", deep.Count > 0,
                deep.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            if (peaks.Count == 0 || deep.Count == 0) return;

            // The single PEAKS ALC histogram (spectrum-row grain) and the two source-specific panels. The
            // OnlyPlusDeepNovo variant pairs PEAKS de novo only with DeepNovo, so those are the two panels.
            DeNovoScoreDistribution peaksAlc = DeNovoScoreDistributionStudy.Build(peaks, criteria);
            var sourceDistributions = new List<DeNovoSourceScoreDistribution>
            {
                DeNovoSourceScoreDistributionStudy.Build(peaks,
                    new DeNovoSourceSelection(DeNovoSource.DenovoOnly, cutoff, 0), criteria),
                DeNovoSourceScoreDistributionStudy.Build(deep,
                    new DeNovoSourceSelection(DeNovoSource.DeepNovo, cutoff, 1), criteria),
            };
            DeNovoSourceScoreDistribution deepPanel = sourceDistributions[1];

            // ---- census bookkeeping self-checks ---------------------------------------------------
            check("score distribution: the PEAKS raw rows equal the sum of the ALC bins' raw rows",
                peaksAlc.RawRows == peaksAlc.Bins.Sum(b => b.RawRows),
                peaksAlc.RawRows.ToString("N0", CultureInfo.InvariantCulture));
            check("score distribution: the PEAKS retained rows equal the sum of the ALC bins' retained rows",
                peaksAlc.RowsPassingAllCriteria == peaksAlc.Bins.Sum(b => b.PassingAllCriteria),
                peaksAlc.RowsPassingAllCriteria.ToString("N0", CultureInfo.InvariantCulture));

            // ---- print the raw and retained totals per source -------------------------------------
            Console.WriteLine("        PEAKS de novo only: " +
                peaksAlc.RawRows.ToString("N0", CultureInfo.InvariantCulture) + " raw -> " +
                peaksAlc.RowsPassingAllCriteria.ToString("N0", CultureInfo.InvariantCulture) +
                " retained (ALC (%) >= " + cutoff.ToString("0.#", CultureInfo.InvariantCulture) + ")");
            Console.WriteLine("        DeepNovo: " +
                deepPanel.RawRows.ToString("N0", CultureInfo.InvariantCulture) + " raw -> " +
                deepPanel.RowsPassingAllCriteria.ToString("N0", CultureInfo.InvariantCulture) +
                " retained (SCORE (%) >= " + cutoff.ToString("0.#", CultureInfo.InvariantCulture) + ")");

            // ---- sanity anchors against the known real-data counts (STOP if the glob is wrong) -----
            check("score distribution: PEAKS de novo only raw rows are about 181,643",
                Math.Abs(peaksAlc.RawRows - 181643) <= 2000,
                peaksAlc.RawRows.ToString("N0", CultureInfo.InvariantCulture));
            check("score distribution: PEAKS de novo only retained rows are about 35,686",
                Math.Abs(peaksAlc.RowsPassingAllCriteria - 35686) <= 1000,
                peaksAlc.RowsPassingAllCriteria.ToString("N0", CultureInfo.InvariantCulture));
            check("score distribution: DeepNovo raw rows are about 42,549",
                Math.Abs(deepPanel.RawRows - 42549) <= 2000,
                deepPanel.RawRows.ToString("N0", CultureInfo.InvariantCulture));
            check("score distribution: DeepNovo retained rows are about 17,875",
                Math.Abs(deepPanel.RowsPassingAllCriteria - 17875) <= 1000,
                deepPanel.RowsPassingAllCriteria.ToString("N0", CultureInfo.InvariantCulture));

            // ---- output folders -------------------------------------------------------------------
            string outFolder = string.IsNullOrWhiteSpace(outputDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(outputDirectory);
            string figuresFolder = Path.Combine(outFolder, FiguresFolderName);
            Directory.CreateDirectory(figuresFolder);

            // ---- the auditable per-bin CSV for the PEAKS ALC distribution --------------------------
            string csvPath = Path.Combine(outFolder, "de_novo_score_distribution_bins.csv");
            File.WriteAllText(csvPath, BuildScoreDistributionCsv(peaksAlc), new UTF8Encoding(false));
            check("score distribution: the auditable per-bin CSV was written", File.Exists(csvPath), csvPath);

            // ---- render the two PNGs (Light palette) reusing the production chart control ----------
            string singlePng = Path.Combine(figuresFolder, "de_novo_score_distribution.png");
            string sourcesPng = Path.Combine(figuresFolder, "de_novo_source_score_distributions.png");
            try
            {
                ChartImageExporter.Save(new DeNovoScoreDistributionChart(peaksAlc), singlePng,
                    variant: ChartPaletteVariant.Light);
                ChartImageExporter.Save(new DeNovoScoreDistributionChart(sourceDistributions), sourcesPng,
                    variant: ChartPaletteVariant.Light);
            }
            catch (Exception ex)
            {
                check("score distribution: the figures rendered", false,
                    ex.GetType().Name + ": " + ex.Message);
                return;
            }

            long singleSize = File.Exists(singlePng) ? new FileInfo(singlePng).Length : 0;
            long sourcesSize = File.Exists(sourcesPng) ? new FileInfo(sourcesPng).Length : 0;
            (int sw, int sh) = singleSize > 0 ? pngPixelSize(singlePng) : (0, 0);
            (int mw, int mh) = sourcesSize > 0 ? pngPixelSize(sourcesPng) : (0, 0);

            check("score distribution: the single PEAKS ALC PNG exists and is not a stub", singleSize > 3000,
                singleSize.ToString("N0", CultureInfo.InvariantCulture) + " bytes " +
                sw.ToString(CultureInfo.InvariantCulture) + "x" + sh.ToString(CultureInfo.InvariantCulture));
            check("score distribution: the PEAKS+DeepNovo panels PNG exists and is not a stub",
                sourcesSize > 3000,
                sourcesSize.ToString("N0", CultureInfo.InvariantCulture) + " bytes " +
                mw.ToString(CultureInfo.InvariantCulture) + "x" + mh.ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("        score distribution single PNG: " + singlePng);
            Console.WriteLine("        score distribution panels PNG: " + sourcesPng);
            Console.WriteLine("        score distribution bin CSV: " + csvPath);
        }

        /// <summary>The PEAKS ALC bins as a small CSV, so the histogram is auditable beside the figure. A
        /// final TOTAL row carries the distribution's own raw and retained totals for a one-line reconcile.</summary>
        private static string BuildScoreDistributionCsv(DeNovoScoreDistribution distribution)
        {
            if (distribution == null) throw new ArgumentNullException(nameof(distribution));
            var b = new StringBuilder();
            b.Append("ScoreLow,ScoreHigh,RawRows,RetainedPassingAllCriteria\n");
            foreach (DeNovoScoreBin bin in distribution.Bins)
            {
                b.Append(bin.LowerInclusive.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
                 .Append(bin.UpperBound.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
                 .Append(bin.RawRows.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(bin.PassingAllCriteria.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            b.Append("TOTAL,,")
             .Append(distribution.RawRows.ToString(CultureInfo.InvariantCulture)).Append(',')
             .Append(distribution.RowsPassingAllCriteria.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return b.ToString();
        }

        /// <summary>Writes the exact two-stage source Venn CSV and 1920x1080 PNG without XIC. This uses
        /// the same production reader, three filters, precursor union and PatternLab reconciliation as
        /// the focused full-XIC lane; the only omitted operation is RAW extraction, which neither set
        /// definition depends on.</summary>
        public static void RunSourceVenn(Action<string, bool, string?> check,
            Func<string, (int Width, int Height)> pngPixelSize,
            string? baseDirectory, string? dataDirectory)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
            {
                check("source Venn: the de novo export directory was given", false, baseDirectory);
                return;
            }
            if (string.IsNullOrWhiteSpace(dataDirectory) || !Directory.Exists(dataDirectory))
            {
                check("source Venn: the data directory was given", false, dataDirectory);
                return;
            }
            string sepr2 = Path.Combine(dataDirectory, Sepr2Name);
            if (!File.Exists(sepr2))
            {
                check("source Venn: PLVI.sepr2 is present", false, sepr2);
                return;
            }

            List<PeaksDeNovoRow> peaks = ReadSource(baseDirectory, DeNovoSource.DenovoOnly);
            List<PeaksDeNovoRow> deep = ReadSource(baseDirectory, DeNovoSource.DeepNovo);
            var criteria = new DeNovoFilterCriteria();
            double cutoff = criteria.MinAverageLocalConfidence;
            var selections = new[]
            {
                new DeNovoSourceSelection(DeNovoSource.DenovoOnly, cutoff, 0),
                new DeNovoSourceSelection(DeNovoSource.DeepNovo, cutoff, 1),
            };
            List<PeaksDeNovoRow> filtered = DeNovoFilter.Apply(peaks, criteria, cutoff)
                .Concat(DeNovoFilter.Apply(deep, criteria, cutoff)).ToList();
            List<PeaksDeNovoRow> union = DeNovoUnionResolver.Resolve(filtered, selections);
            List<PatternLabPsm> psms = SeproPsmAdapter.Load(sepr2);
            List<ReconciledRow> reconciliation = DeNovoReconciler.Reconcile(union, psms);
            DeNovoSourceVennSummary summary = DeNovoSourceVennStudy.Build(peaks, deep, criteria,
                cutoff, cutoff, reconciliation);

            string outFolder = Path.Combine(ResolveTalkOutputRoot(dataDirectory),
                DeNovoVariantComparison.OnlyPlusDeepNovoVariantName);
            string figuresFolder = Path.Combine(outFolder, FiguresFolderName);
            Directory.CreateDirectory(figuresFolder);
            string csvPath = Path.Combine(outFolder, DeNovoSourceVennStudy.CsvFileName);
            File.WriteAllText(csvPath, DeNovoSourceVennStudy.ToCsv(summary), new UTF8Encoding(false));
            string pngPath = PresentationFigures.SaveDeNovoSourceVenn(summary, figuresFolder);

            long size = File.Exists(pngPath) ? new FileInfo(pngPath).Length : 0;
            (int width, int height) = size > 0 ? pngPixelSize(pngPath) : (0, 0);
            check("source Venn: all source rows were read",
                summary.PeaksFunnel.RawRows > 0 && summary.DeepNovoFunnel.RawRows > 0,
                summary.PeaksFunnel.RawRows.ToString("N0", CultureInfo.InvariantCulture) + " PEAKS / " +
                summary.DeepNovoFunnel.RawRows.ToString("N0", CultureInfo.InvariantCulture) + " DeepNovo");
            check("source Venn: exact filtered regions close to their union",
                summary.FilteredBeforeUnion.Union == summary.FilteredBeforeUnion.PeaksExclusive +
                    summary.FilteredBeforeUnion.Shared + summary.FilteredBeforeUnion.DeepNovoExclusive,
                summary.FilteredBeforeUnion.Union.ToString("N0", CultureInfo.InvariantCulture));
            check("source Venn: carried-provenance regions come from the same reconciled union",
                summary.CarriedForwardClaimProvenance.Union ==
                    summary.CarriedForwardClaimProvenance.PeaksExclusive +
                    summary.CarriedForwardClaimProvenance.Shared +
                    summary.CarriedForwardClaimProvenance.DeepNovoExclusive,
                summary.CarriedForwardClaimProvenance.Union.ToString("N0", CultureInfo.InvariantCulture));
            check("source Venn: CSV was written with both exact grains", File.Exists(csvPath)
                && File.ReadAllText(csvPath).Contains("carried-forward-claim-provenance"), csvPath);
            check("source Venn: PNG is 1920x1080 and not a stub",
                width == 1920 && height == 1080 && size > 3000,
                width.ToString(CultureInfo.InvariantCulture) + "x" +
                height.ToString(CultureInfo.InvariantCulture) + " / " +
                size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");

            Console.WriteLine("        source Venn CSV: " + csvPath);
            Console.WriteLine("        source Venn PNG: " + pngPath);
        }

        /// <summary>Writes the exact accepted-call score/match CSV and the 1920x1080 translucent dot plot
        /// directly from a persisted scientific project. No RAW, XIC extraction or mutable source export
        /// is reopened.</summary>
        public static void RunAcceptedScoreScatter(Action<string, bool, string?> check,
            Func<string, (int Width, int Height)> pngPixelSize,
            string? projectPath, string? outputDirectory)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            {
                check("accepted score scatter: the scientific .fqproj was given", false, projectPath);
                return;
            }

            var (_, _, manifest) = ProjectStore.ReadFqproj(projectPath);
            DeNovoAcceptedScoreScatter scatter = manifest.DeNovoAcceptedScoreScatter
                ?? DeNovoAcceptedScoreScatterStudy.Build(
                    manifest.SpectrumComparisonRows ?? new List<SpectrumComparisonRow>(),
                    manifest.DeNovoSourceScoreDistributions);
            check("accepted score scatter: jointly accepted PEAKS/DeepNovo rows were persisted",
                scatter.Total > 0,
                scatter.Total.ToString("N0", CultureInfo.InvariantCulture));
            if (scatter.Total == 0) return;
            scatter.Validate();

            string outFolder = string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(projectPath))!
                : Path.GetFullPath(outputDirectory);
            string figuresFolder = Path.Combine(outFolder, FiguresFolderName);
            Directory.CreateDirectory(figuresFolder);
            string csvPath = Path.Combine(outFolder,
                DeNovoAcceptedScoreScatterStudy.CsvFileName);
            File.WriteAllText(csvPath, DeNovoAcceptedScoreScatterStudy.ToCsv(scatter),
                new UTF8Encoding(false));
            string pngPath = PresentationFigures.SaveDeNovoAcceptedScoreScatter(
                scatter, figuresFolder);

            long size = File.Exists(pngPath) ? new FileInfo(pngPath).Length : 0;
            (int width, int height) = size > 0 ? pngPixelSize(pngPath) : (0, 0);
            check("accepted score scatter: match and mismatch counts close to the total",
                scatter.Matches + scatter.Mismatches == scatter.Total,
                scatter.Matches.ToString("N0", CultureInfo.InvariantCulture) + " match / " +
                scatter.Mismatches.ToString("N0", CultureInfo.InvariantCulture) + " mismatch / " +
                scatter.Total.ToString("N0", CultureInfo.InvariantCulture) + " total");
            check("accepted score scatter: CSV preserves annotated and PTM-clean sequences",
                File.Exists(csvPath)
                && File.ReadAllText(csvPath).Contains("PeaksAnnotatedSequence")
                && File.ReadAllText(csvPath).Contains("StrictSequenceMatch"), csvPath);
            check("accepted score scatter: PNG is 1920x1080 and not a stub",
                width == 1920 && height == 1080 && size > 3000,
                width.ToString(CultureInfo.InvariantCulture) + "x" +
                height.ToString(CultureInfo.InvariantCulture) + " / " +
                size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");

            Console.WriteLine("        accepted score scatter total: " +
                scatter.Total.ToString("N0", CultureInfo.InvariantCulture));
            Console.WriteLine("        strict PTM-clean matches: " +
                scatter.Matches.ToString("N0", CultureInfo.InvariantCulture) + " (" +
                (100 * scatter.MatchFraction).ToString("F2", CultureInfo.InvariantCulture) + "%)");
            Console.WriteLine("        strict PTM-clean mismatches: " +
                scatter.Mismatches.ToString("N0", CultureInfo.InvariantCulture));
            Console.WriteLine("        accepted score CSV: " + csvPath);
            Console.WriteLine("        accepted score PNG: " + pngPath);
        }

        // ================================================================= the agreement bubble figure

        /// <summary>
        /// Builds the per-spectrum PEAKS-vs-DeepNovo agreement bubble study from the CSVs alone (no XIC),
        /// writes the per-spectrum table and the category summary beside it, renders the density PNG, and
        /// self-checks that the three categories partition the co-called spectra. The base directory must
        /// contain the de novo all, DeepNovo and de-novo-only exports somewhere under it (each source's
        /// glob is distinct, so one recursive search per source picks the right files).
        /// </summary>
        public static void RunAgreement(Action<string, bool, string?> check,
                                        Func<string, (int Width, int Height)> pngPixelSize,
                                        string? baseDirectory, string? outputDirectory)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));

            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            {
                check("agreement: the de novo base directory was given (positional argument 3)", false, baseDirectory);
                return;
            }

            // The PEAKS side is de novo ALL (confidence ALC); the other tool is DeepNovo (confidence SCORE);
            // the strict de-novo-only export supplies membership marking only.
            List<PeaksDeNovoRow> peaksAll = ReadSource(baseDirectory, DeNovoSource.DeNovoAll);
            List<PeaksDeNovoRow> deepNovo = ReadSource(baseDirectory, DeNovoSource.DeepNovo);
            List<PeaksDeNovoRow> deNovoOnly = ReadSource(baseDirectory, DeNovoSource.DenovoOnly);

            check("agreement: the de novo all (PEAKS) files were found", peaksAll.Count > 0,
                peaksAll.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            check("agreement: the DeepNovo files were found", deepNovo.Count > 0,
                deepNovo.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            check("agreement: the de novo only files were found (for membership marking)", deNovoOnly.Count > 0,
                deNovoOnly.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            if (peaksAll.Count == 0 || deepNovo.Count == 0 || deNovoOnly.Count == 0) return;

            DeNovoAgreementResult agreement = DeNovoAgreementStudy.Build(peaksAll, deepNovo, deNovoOnly);

            // ---- self-check: the three categories partition the precursor-qualified primary set -------
            int sum = agreement.Identical + agreement.SameMassRearrangement + agreement.DifferentMass;
            check("agreement: identical + same-theoretical-mass + different-theoretical-mass == qualified total",
                sum == agreement.Total && agreement.Spectra.Count == agreement.Total,
                sum.ToString(CultureInfo.InvariantCulture) + " == " +
                agreement.Total.ToString(CultureInfo.InvariantCulture));
            check("agreement: at least some co-called spectra pass same-charge and 20-ppm precursor gates",
                agreement.Total > 0,
                agreement.Total.ToString("N0", CultureInfo.InvariantCulture));
            if (agreement.Total == 0) return;

            check("agreement: scan-only audit partitions into different-charge and same-charge strata",
                agreement.DifferentChargeScanOnly + agreement.SameChargeScanOnly == agreement.ScanOnlyTotal,
                agreement.ScanOnlyTotal.ToString(CultureInfo.InvariantCulture));
            check("agreement: same-charge stratum partitions into outside-20-ppm and qualified sets",
                agreement.SameChargeOutsideMzTolerance + agreement.PrecursorQualifiedScanOnly
                    == agreement.SameChargeScanOnly,
                agreement.SameChargeScanOnly.ToString(CultureInfo.InvariantCulture));
            check("agreement: primary one-to-one precursor pairs cover every qualified scan",
                agreement.Total >= agreement.PrecursorQualifiedScanOnly,
                agreement.Total.ToString(CultureInfo.InvariantCulture) + " pairs / " +
                agreement.PrecursorQualifiedScanOnly.ToString(CultureInfo.InvariantCulture) + " scans");
            check("agreement: RT proximity is a labelled sensitivity subset, not part of the primary gate",
                agreement.RtSensitivityQualifiedCount >= 0
                && agreement.RtSensitivityQualifiedCount <= agreement.Total,
                agreement.RtSensitivityQualifiedCount.ToString(CultureInfo.InvariantCulture));
            check("agreement: theoretical-mass agreement fraction is bounded",
                agreement.TheoreticalMassMatchedFractionOfDisagreements >= 0
                && agreement.TheoreticalMassMatchedFractionOfDisagreements <= 1,
                (agreement.TheoreticalMassMatchedFractionOfDisagreements * 100)
                    .ToString("F1", CultureInfo.InvariantCulture) + "%");

            // A green partition does not by itself guarantee a non-empty scatter: every bubble is placed
            // on the X axis by the PEAKS-reported neutral mass, and a de novo export lacking a "Mass"
            // column would drop every bubble (empty cloud) while all the counts above stayed green. Assert
            // that at least one co-called spectrum actually carries a mass to place.
            int placedOnMass = agreement.Spectra.Count(s => s.MassDa > 0);
            check("agreement: at least one co-called spectrum carries a mass to place on the cloud",
                placedOnMass > 0,
                placedOnMass.ToString("N0", CultureInfo.InvariantCulture) + " of " +
                agreement.Total.ToString("N0", CultureInfo.InvariantCulture) + " placed");

            Console.WriteLine("        " + agreement.Headline() + ".");
            Console.WriteLine("        precursor-qualified co-called: " +
                agreement.Total.ToString("N0", CultureInfo.InvariantCulture));
            Console.WriteLine("        identical " + agreement.Identical.ToString("N0", CultureInfo.InvariantCulture) +
                " / same theoretical sequence mass " + agreement.SameMassRearrangement.ToString("N0", CultureInfo.InvariantCulture) +
                " / different theoretical sequence mass " + agreement.DifferentMass.ToString("N0", CultureInfo.InvariantCulture));
            Console.WriteLine("        identical fraction: " +
                (agreement.IdenticalFraction * 100).ToString("F2", CultureInfo.InvariantCulture) + "%");
            Console.WriteLine("        " + agreement.SameMassRearrangement.ToString("N0", CultureInfo.InvariantCulture) +
                " of " + agreement.Disagreements.ToString("N0", CultureInfo.InvariantCulture) +
                " disagreements have PTM-aware theoretical sequence masses within " +
                agreement.MassToleranceDa.ToString("0.###", CultureInfo.InvariantCulture) + " Da");
            Console.WriteLine("        single near-isobaric residue swaps (footnote): " +
                agreement.SingleIsobaricSwaps.ToString("N0", CultureInfo.InvariantCulture));
            Console.WriteLine("        also in de novo only: " +
                agreement.InDeNovoOnlyCount.ToString("N0", CultureInfo.InvariantCulture) + " of " +
                agreement.Total.ToString("N0", CultureInfo.InvariantCulture));

            // ---- write the inspectable tables ---------------------------------------------------------
            string outFolder = string.IsNullOrEmpty(outputDirectory)
                ? Path.Combine(ResolveTalkOutputRoot(baseDirectory), "agreement")
                : outputDirectory;
            Directory.CreateDirectory(outFolder);

            string csvPath = Path.Combine(outFolder, "de_novo_peaks_deepnovo_agreement.csv");
            File.WriteAllText(csvPath, DeNovoAgreementStudy.BuildTableCsv(agreement), new UTF8Encoding(false));
            check("agreement: the per-spectrum csv was written", File.Exists(csvPath), csvPath);

            string mdPath = Path.Combine(outFolder, "de_novo_peaks_deepnovo_agreement.md");
            File.WriteAllText(mdPath, DeNovoAgreementStudy.BuildSummaryMarkdown(agreement), new UTF8Encoding(false));
            check("agreement: the category summary markdown was written", File.Exists(mdPath), mdPath);

            // ---- render the figure (no XIC) -----------------------------------------------------------
            string figurePath;
            try
            {
                figurePath = PresentationFigures.SaveAgreement(agreement, outFolder);
            }
            catch (Exception ex)
            {
                check("agreement: the figure rendered", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            long size = File.Exists(figurePath) ? new FileInfo(figurePath).Length : 0;
            (int w, int h) = size > 0 ? pngPixelSize(figurePath) : (0, 0);
            Console.WriteLine("        agreement figure " + figurePath + "  " +
                w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture) +
                "  " + size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
            check("agreement: the figure exists and is not a stub", size > 3000,
                size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
            check("agreement: the figure is the presentation size 1920x1080", w == 1920 && h == 1080,
                w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("        agreement numbers written to " + csvPath + " and " + mdPath);
        }

        // ================================================================= the score-vs-score dot plot

        /// <summary>
        /// Builds the score-vs-score dot plot from the CSVs alone (no XIC): the clean sequences found by
        /// both de novo all (ALC) and DeepNovo (SCORE), the confidence each tool gave them, and the two
        /// unique sets' confidence distributions. Writes the shared points and the summary beside the
        /// figure, renders the PNG, and self-checks the shared / unique counts and the correlation against
        /// the known anchors. The base directory must contain the three sources somewhere under it.
        /// </summary>
        public static void RunScoreScatter(Action<string, bool, string?> check,
                                           Func<string, (int Width, int Height)> pngPixelSize,
                                           string? baseDirectory, string? outputDirectory)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            {
                check("scatter: the de novo base directory was given (positional argument 3)", false, baseDirectory);
                return;
            }

            List<PeaksDeNovoRow> allRows = ReadSource(baseDirectory, DeNovoSource.DeNovoAll);
            List<PeaksDeNovoRow> deepRows = ReadSource(baseDirectory, DeNovoSource.DeepNovo);
            check("scatter: the de novo all files were found", allRows.Count > 0,
                allRows.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            check("scatter: the DeepNovo files were found", deepRows.Count > 0,
                deepRows.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            if (allRows.Count == 0 || deepRows.Count == 0) return;

            DeNovoScoreScatter scatter = DeNovoScoreScatterStudy.Build(
                allRows, DeNovoSource.DeNovoAll, deepRows, DeNovoSource.DeepNovo);

            // ---- self-check against the known anchors (see the design brief) --------------------------
            check("scatter: 4,366 clean sequences shared by both tools", scatter.SharedCount == 4366,
                scatter.SharedCount.ToString(CultureInfo.InvariantCulture));
            check("scatter: 156,298 PEAKS-unique clean sequences", scatter.PeaksUniqueCount == 156298,
                scatter.PeaksUniqueCount.ToString(CultureInfo.InvariantCulture));
            check("scatter: 9,491 DeepNovo-unique clean sequences", scatter.DeepNovoUniqueCount == 9491,
                scatter.DeepNovoUniqueCount.ToString(CultureInfo.InvariantCulture));
            check("scatter: Pearson r of ALC vs SCORE is about 0.43",
                scatter.PearsonR >= 0.38 && scatter.PearsonR <= 0.48,
                scatter.PearsonR.ToString("0.###", CultureInfo.InvariantCulture));

            Console.WriteLine("        shared " + scatter.SharedCount.ToString("N0", CultureInfo.InvariantCulture) +
                " / PEAKS-unique " + scatter.PeaksUniqueCount.ToString("N0", CultureInfo.InvariantCulture) +
                " / DeepNovo-unique " + scatter.DeepNovoUniqueCount.ToString("N0", CultureInfo.InvariantCulture) +
                " / r " + scatter.PearsonR.ToString("0.###", CultureInfo.InvariantCulture));

            // ---- write the inspectable tables ---------------------------------------------------------
            string outFolder = string.IsNullOrEmpty(outputDirectory)
                ? Path.Combine(ResolveTalkOutputRoot(baseDirectory), "score_scatter")
                : outputDirectory;
            Directory.CreateDirectory(outFolder);

            string csvPath = Path.Combine(outFolder, "de_novo_score_vs_score_shared.csv");
            File.WriteAllText(csvPath, DeNovoScoreScatterStudy.BuildSharedCsv(scatter), new UTF8Encoding(false));
            check("scatter: the shared-point csv was written", File.Exists(csvPath), csvPath);

            string mdPath = Path.Combine(outFolder, "de_novo_score_vs_score.md");
            File.WriteAllText(mdPath, DeNovoScoreScatterStudy.BuildSummaryMarkdown(scatter), new UTF8Encoding(false));
            check("scatter: the summary markdown was written", File.Exists(mdPath), mdPath);

            string figurePath;
            try
            {
                figurePath = PresentationFigures.SaveScoreScatter(scatter, outFolder);
            }
            catch (Exception ex)
            {
                check("scatter: the figure rendered", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            long size = File.Exists(figurePath) ? new FileInfo(figurePath).Length : 0;
            (int w, int h) = size > 0 ? pngPixelSize(figurePath) : (0, 0);
            Console.WriteLine("        scatter figure " + figurePath + "  " +
                w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture) +
                "  " + size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
            check("scatter: the figure exists and is not a stub", size > 3000,
                size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
            check("scatter: the figure is the presentation size 1920x1080", w == 1920 && h == 1080,
                w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("        scatter numbers written to " + csvPath + " and " + mdPath);
        }

        // ================================================================= the set-novelty (UpSet) view

        /// <summary>
        /// Builds the UpSet-style set-novelty view from the CSVs alone (no XIC): three sets by clean
        /// sequence (de novo only, de novo all, DeepNovo), their intersection regions with counts and
        /// median confidence, and the two headline deltas. Writes the regions and the summary beside the
        /// figure, renders the PNG, and self-checks the region counts and deltas against the known anchors.
        /// The base directory must contain the three sources somewhere under it.
        /// </summary>
        public static void RunSetNovelty(Action<string, bool, string?> check,
                                        Func<string, (int Width, int Height)> pngPixelSize,
                                        string? baseDirectory, string? outputDirectory)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            {
                check("set-novelty: the de novo base directory was given (positional argument 3)", false, baseDirectory);
                return;
            }

            List<PeaksDeNovoRow> onlyRows = ReadSource(baseDirectory, DeNovoSource.DenovoOnly);
            List<PeaksDeNovoRow> allRows = ReadSource(baseDirectory, DeNovoSource.DeNovoAll);
            List<PeaksDeNovoRow> deepRows = ReadSource(baseDirectory, DeNovoSource.DeepNovo);
            check("set-novelty: the de novo only files were found", onlyRows.Count > 0,
                onlyRows.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            check("set-novelty: the de novo all files were found", allRows.Count > 0,
                allRows.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            check("set-novelty: the DeepNovo files were found", deepRows.Count > 0,
                deepRows.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows");
            if (onlyRows.Count == 0 || allRows.Count == 0 || deepRows.Count == 0) return;

            SetNovelty set = SetNoveltyStudy.Build(onlyRows, allRows, deepRows);

            // ---- self-check against the known anchors (see the design brief) --------------------------
            check("set-novelty: the full export adds +18,068 over the only subset",
                set.AddedByFullExportOverOnly == 18068,
                set.AddedByFullExportOverOnly.ToString(CultureInfo.InvariantCulture));
            check("set-novelty: DeepNovo unique is +9,491", set.DeepNovoUnique == 9491,
                set.DeepNovoUnique.ToString(CultureInfo.InvariantCulture));

            SetNoveltyRegion? core = set.Regions.FirstOrDefault(r =>
                r.InDeNovoOnly && r.InDeNovoAll && !r.InDeepNovo);
            check("set-novelty: the de-novo-only/all shared core is about 139,855",
                core != null && core.Count == 139855,
                core == null ? "missing" : core.Count.ToString(CultureInfo.InvariantCulture));

            SetNoveltyRegion? allThree = set.Regions.FirstOrDefault(r =>
                r.InDeNovoOnly && r.InDeNovoAll && r.InDeepNovo);
            check("set-novelty: a shared-by-all-three region is present",
                allThree != null && allThree.Count > 0,
                allThree == null ? "missing" : allThree.Count.ToString(CultureInfo.InvariantCulture));

            check("set-novelty: the regions partition the union of the three sets",
                set.Regions.Sum(r => r.Count) == set.UnionSize,
                set.Regions.Sum(r => r.Count).ToString(CultureInfo.InvariantCulture) + " == " +
                set.UnionSize.ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("        sets: only " + set.DeNovoOnlySize.ToString("N0", CultureInfo.InvariantCulture) +
                " / all " + set.DeNovoAllSize.ToString("N0", CultureInfo.InvariantCulture) +
                " / DeepNovo " + set.DeepNovoSize.ToString("N0", CultureInfo.InvariantCulture));
            foreach (SetNoveltyRegion r in set.Regions)
                Console.WriteLine("        region " + r.Label + ": " +
                    r.Count.ToString("N0", CultureInfo.InvariantCulture) + " (med " + r.ConfidenceLabel + " " +
                    r.MedianConfidence.ToString("0", CultureInfo.InvariantCulture) + "%)");

            // ---- write the inspectable tables ---------------------------------------------------------
            string outFolder = string.IsNullOrEmpty(outputDirectory)
                ? Path.Combine(ResolveTalkOutputRoot(baseDirectory), "set_novelty")
                : outputDirectory;
            Directory.CreateDirectory(outFolder);

            string csvPath = Path.Combine(outFolder, "de_novo_set_novelty_regions.csv");
            File.WriteAllText(csvPath, SetNoveltyStudy.BuildRegionsCsv(set), new UTF8Encoding(false));
            check("set-novelty: the regions csv was written", File.Exists(csvPath), csvPath);

            string mdPath = Path.Combine(outFolder, "de_novo_set_novelty.md");
            File.WriteAllText(mdPath, SetNoveltyStudy.BuildSummaryMarkdown(set), new UTF8Encoding(false));
            check("set-novelty: the summary markdown was written", File.Exists(mdPath), mdPath);

            string figurePath;
            try
            {
                figurePath = PresentationFigures.SaveSetNovelty(set, outFolder);
            }
            catch (Exception ex)
            {
                check("set-novelty: the figure rendered", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            long size = File.Exists(figurePath) ? new FileInfo(figurePath).Length : 0;
            (int w, int h) = size > 0 ? pngPixelSize(figurePath) : (0, 0);
            Console.WriteLine("        set-novelty figure " + figurePath + "  " +
                w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture) +
                "  " + size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
            check("set-novelty: the figure exists and is not a stub", size > 3000,
                size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
            check("set-novelty: the figure is the presentation size 1920x1080", w == 1920 && h == 1080,
                w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("        set-novelty numbers written to " + csvPath + " and " + mdPath);
        }

        // ================================================================= the numbers file

        private static string BuildNumbers(AnalyticsData data, NativeRunFraction grouped,
                                           FamilyEnvelopeQuantResult families, FullXicComputeResult store,
                                           FullXicOptions options, List<IdentityScanRow> sweep,
                                           FamilyClusterFile curation, int familyCount,
                                           int targetBearingFamilies, int nonContaminantTargetFamilies,
                                           int contaminantTargetFamilies, int pureDecoyFamilies, string fcfPath,
                                           string figuresFolder, List<string> figures,
                                           ReferenceEnvelopeDiagnostics envelopeDiagnostics)
        {
            var sb = new StringBuilder();
            IReadOnlyList<DeNovoSource> sources = SourcesOf(options);
            string sourceLabel = string.Join(" + ", sources.Select(s => s.DisplayName));
            string confidenceLabel = string.Join(" + ",
                sources.Select(ConfidenceMetric).Distinct(StringComparer.Ordinal));
            string confidenceCutoffNote = ConfidenceCutoffNote(options);
            string sourceRowLabel = sources.Count == 1 && sources[0].IsDenovoOnly
                ? sources[0].DisplayName + " rows" : sourceLabel + " rows";

            sb.Append("# FamilyQuant, BrProt talk: the numbers\n\n");
            sb.Append("Every number below was measured by FamilyQuant's own engine on the three ")
              .Append("`20220624_Bjararaca_1ug_3step-240min_0{1,2,3}` acquisitions, through the same entry ")
              .Append("points the application uses. Nothing here is copied from a stored PatternLab quant.\n\n");

            sb.Append("## How it was measured\n\n");
            sb.Append("| Setting | Value |\n|---|---|\n");
            Row(sb, "Isotope ceiling (a ceiling, not a floor)", Int(options.IsotopeCount));
            Row(sb, "Minimum points per peak, per isotope", Int(options.MinPointsForQuant));
            Row(sb, "Extraction window", Num(options.Ppm, "F1") + " ppm");
            Row(sb, "MS1 clustering tolerance", Int(options.Ms1Tolerance));
            Row(sb, "Clustering semantics", Int(ClusterIdentity) +
                "%-edge single-linkage connected components (exhaustive)");
            Row(sb, "All connected components (targets + contaminants + decoys)", Int(familyCount));
            Row(sb, "Target-bearing connected components", Int(targetBearingFamilies));
            Row(sb, "Non-contaminant target connected components (biological headline)",
                Int(nonContaminantTargetFamilies));
            Row(sb, "Contaminant-bearing target connected components", Int(contaminantTargetFamilies));
            Row(sb, "Pure-decoy connected components", Int(pureDecoyFamilies));
            Row(sb, "Protein rows in the curation", Int(curation.MyExtendedFastaItems.Count));
            Row(sb, "Homology identity for the family attribution", Pct(families.IdentityThreshold, 0));
            Row(sb, "Curation file", "`" + fcfPath + "`");
            Row(sb, "Stored quant rows (per MS2 anchor)", Int(store.Rows.Count));
            Row(sb, "Peptides after the per-peptide collapse", Int(data.Peptides.Count));
            Row(sb, "Derived-fraction method", "`" + store.Manifest.DerivedMethod + "`");
            sb.Append('\n');

            sb.Append("The fraction basis is the one sentence a reviewer accepts: each peptide is counted ")
              .Append("once, as its XIC peak area, over the run's total ion current on the identical ")
              .Append("intensity-versus-retention-time trapezoid basis. The collapse keeps one ")
              .Append("deterministic largest-M+0 representative per run, evidence side, canonical ")
              .Append("PTM-aware peptidoform and charge. Fixed carbamidomethyl-C notation is normalized, ")
              .Append("while genuinely distinct peptidoforms remain separate, so no elution peak is ")
              .Append("summed twice.\n\n");

            // ---- de novo/search-engine overlap ---------------------------------------------------
            sb.Append("## De novo/search-engine overlap\n\n");
            DeNovoOverlapSummary? overlap = store.Manifest.DeNovoOverlap;
            if (overlap == null)
            {
                sb.Append("No overlap audit is stored in this project.\n\n");
            }
            else
            {
                overlap.Validate();
                sb.Append("This is a nested row-level audit of the filtered ").Append(sourceLabel).Append(" input. ")
                  .Append("The literal join is normalized acquisition file + scan number. Sequence ")
                  .Append("comparison then requires the same reported charge. A fully comparable precursor ")
                  .Append("also requires m/z within ").Append(Num(overlap.PrecursorTolerancePpm, "0.###"))
                  .Append(" ppm; the highest-XCorr PSM in that stratum is selected. PTM notation is removed ")
                  .Append("before sequence comparison, with I/L folding reported separately. These are ")
                  .Append("alternative de novo hypotheses, not a database-search accuracy test.\n\n");

                sb.Append("| Stage | Count | Denominator |\n|---|---|---|\n");
                OverlapRow(sb, "Raw " + sourceRowLabel, overlap.RawDeNovoRows,
                           overlap.RawDeNovoRows);
                OverlapRow(sb, "Passed de novo filter", overlap.FilteredDeNovoRows,
                           overlap.RawDeNovoRows);
                OverlapRow(sb, "Shared normalized file + scan", overlap.SharedFileAndScan,
                           overlap.FilteredDeNovoRows);
                OverlapRow(sb, "Shared scan, different reported charge",
                           overlap.DifferentChargeInSharedScan, overlap.SharedFileAndScan);
                OverlapRow(sb, "Shared scan, same reported charge",
                           overlap.SameFileScanAndCharge, overlap.SharedFileAndScan);
                OverlapRow(sb, "Same scan + charge: strict sequence match, PTMs ignored (I/L distinct)",
                           overlap.ScanLevelStrictSequenceMatchesIgnoringPtm,
                           overlap.SameFileScanAndCharge);
                OverlapRow(sb, "Same scan + charge: additional match after I/L folding",
                           overlap.ScanLevelAdditionalIleLeuEquivalentMatches,
                           overlap.SameFileScanAndCharge);
                OverlapRow(sb, "Same scan + charge: alternative sequence, PTMs ignored (I/L distinct)",
                           overlap.ScanLevelSequenceDivergencesIgnoringPtmStrict,
                           overlap.SameFileScanAndCharge);
                OverlapRow(sb, "Same scan + charge: alternative sequence after PTM removal + I/L folding",
                           overlap.ScanLevelSequenceDivergencesAfterIleLeuFold,
                           overlap.SameFileScanAndCharge);
                OverlapRow(sb, "Different precursor context (charge or m/z gate failed)",
                           overlap.DifferentPrecursorInSharedScan, overlap.SharedFileAndScan);
                OverlapRow(sb, "Comparable precursor: same file + scan + charge + m/z",
                           overlap.SamePrecursorWithinTolerance, overlap.SharedFileAndScan);
                OverlapRow(sb, "Strict sequence match, PTMs ignored (I/L distinct)",
                           overlap.StrictSequenceMatchesIgnoringPtm,
                           overlap.SamePrecursorWithinTolerance);
                OverlapRow(sb, "Additional match after I/L folding",
                           overlap.AdditionalIleLeuEquivalentMatches,
                           overlap.SamePrecursorWithinTolerance);
                OverlapRow(sb, "Alternative sequence, PTMs ignored (I/L distinct)",
                           overlap.SequenceDivergencesIgnoringPtmStrict,
                           overlap.SamePrecursorWithinTolerance);
                OverlapRow(sb, "Alternative sequence after PTM removal + I/L folding",
                           overlap.SequenceDivergencesAfterIleLeuFold,
                           overlap.SamePrecursorWithinTolerance);
                sb.Append('\n');

                sb.Append("Inclusive de novo filters: peptide length >= ")
                  .Append(Int(overlap.MinLength)).Append("; source-specific score rules: ")
                  .Append(confidenceCutoffNote).Append("; and at least ").Append(Int(overlap.StretchLength))
                  .Append(" consecutive residues with local confidence >= ")
                  .Append(Int(overlap.StretchFloor)).Append("%. Missing or unparseable local-confidence ")
                  .Append("arrays fail the filter. XCorr < ")
                  .Append(Num(overlap.WeakXCorr, "0.###"))
                  .Append(" is a reconciliation decision threshold, not a de novo acceptance cutoff. ")
                  .Append("The >=80 rules are operational source-specific score thresholds, not calibrated ")
                  .Append("de novo FDRs. PEAKS ALC and DeepNovo SCORE are different scales and are not ")
                  .Append("directly comparable with one another, XCorr, or a search-engine q-value.\n\n");
            }

            IdentificationConcordanceSummary? concordance = store.Manifest.IdentificationConcordance;
            sb.Append("## Independent database-search concordance\n\n");
            if (concordance == null)
            {
                sb.Append("No PatternLab/PEAKS database-search concordance audit is stored in this project.\n\n");
            }
            else
            {
                concordance.Validate();
                sb.Append("PatternLab and PEAKS database PSMs are joined by normalized acquisition file + ")
                  .Append("MS2 scan, then paired one-to-one only when the reported positive charge is equal ")
                  .Append("and precursor m/z differs by no more than ")
                  .Append(Num(concordance.PrecursorTolerancePpm, "0.###"))
                  .Append(" ppm. This precursor-qualified pair set is the agreement denominator. Exact ")
                  .Append("agreement is evaluated after removing PTM notation; I/L remains distinct in the ")
                  .Append("headline and is folded separately. Raw shared-scan counts remain an audit only.\n\n")
                  .Append("| Quantity | Count |\n|---|---|\n");
                Row(sb, "PatternLab PSM rows", Int(concordance.PatternLabPsmRows));
                Row(sb, "PEAKS database PSM rows", Int(concordance.PeaksDatabasePsmRows));
                Row(sb, "Raw shared normalized file + scan keys (audit)",
                    Int(concordance.SharedDatabaseSearchScanKeys));
                Row(sb, "Shared scans with at least one precursor-qualified pair",
                    Int(concordance.PrecursorQualifiedDatabaseSearchScanKeys));
                Row(sb, "Precursor-qualified one-to-one PSM pairs (primary denominator)",
                    Int(concordance.PrecursorQualifiedDatabaseSearchPairs));
                Row(sb, "Exact PTM-cleaned pairs (I/L distinct)",
                    Int(concordance.ExactPrecursorQualifiedDatabaseSearchPairs));
                Row(sb, "Strict precursor-qualified agreement",
                    Pct(concordance.PrecursorQualifiedStrictAgreement, 2));
                Row(sb, "Additional I/L-equivalent pairs",
                    Int(concordance.AdditionalIleLeuEquivalentPrecursorQualifiedPairs));
                Row(sb, "Agreement after I/L folding",
                    Pct(concordance.PrecursorQualifiedIleLeuEquivalentAgreement, 2));
                sb.Append('\n');
            }

            DeNovoScoreDistribution? scoreDistribution = store.Manifest.DeNovoScoreDistribution;
            List<DeNovoSourceScoreDistribution> storedSourceDistributions =
                store.Manifest.DeNovoSourceScoreDistributions ?? new List<DeNovoSourceScoreDistribution>();
            if (storedSourceDistributions.Count > 0)
            {
                sb.Append("## De novo score distributions\n\n")
                  .Append("Each source remains on its own spectrum-row and score scale. The retained series ")
                  .Append("passes source score, peptide length, and local-confidence stretch gates; it is ")
                  .Append("not a peptide-level FDR estimate. PEAKS ALC and DeepNovo SCORE are not pooled or ")
                  .Append("treated as numerically equivalent.\n\n");
                foreach (DeNovoSourceScoreDistribution distribution in storedSourceDistributions)
                {
                    distribution.Validate();
                    sb.Append("### ").Append(distribution.SourceDisplayName).Append(": ")
                      .Append(distribution.ConfidenceField).Append("\n\n")
                      .Append("| Stage | Spectrum rows | Share of raw |\n|---|---:|---:|\n");
                    ScoreRow(sb, "Raw source rows", distribution.RawRows, distribution.RawRows);
                    ScoreRow(sb, "Length >= " + Int(distribution.MinLength),
                        distribution.RowsPassingLength, distribution.RawRows);
                    ScoreRow(sb, distribution.ConfidenceField + " >= " +
                        Num(distribution.MinimumConfidence, "0.###"),
                        distribution.RowsAtOrAboveConfidence, distribution.RawRows);
                    ScoreRow(sb, "Local-confidence stretch alone",
                        distribution.RowsPassingLocalStretch, distribution.RawRows);
                    ScoreRow(sb, "Length + source score",
                        distribution.RowsPassingLengthAndConfidence, distribution.RawRows);
                    ScoreRow(sb, "All three gates", distribution.RowsPassingAllCriteria,
                        distribution.RawRows);
                    sb.Append("\n| ").Append(distribution.ConfidenceField)
                      .Append(" bin | Raw rows | Retained after all gates |\n|---|---:|---:|\n");
                    foreach (DeNovoScoreBin bin in distribution.Bins)
                        sb.Append("| ").Append(bin.Label).Append(" | ").Append(Int(bin.RawRows))
                          .Append(" | ").Append(Int(bin.PassingAllCriteria)).Append(" |\n");
                    sb.Append('\n');
                }
            }
            else if (scoreDistribution != null)
            {
                scoreDistribution.Validate();
                sb.Append("## De novo score distribution\n\n")
                  .Append("Counts remain at the ").Append(sourceRowLabel).Append(" spectrum-row grain. The retained series ")
                  .Append("is the subset passing all three acceptance gates; it is not a peptide-level FDR estimate.\n\n")
                  .Append("| Stage | Spectrum rows | Share of raw |\n|---|---|---|\n");
                ScoreRow(sb, "Raw " + sourceRowLabel, scoreDistribution.RawRows, scoreDistribution.RawRows);
                ScoreRow(sb, "Length >= " + Int(scoreDistribution.MinLength),
                    scoreDistribution.RowsPassingLength, scoreDistribution.RawRows);
                ScoreRow(sb, confidenceLabel + " >= " + Num(scoreDistribution.MinAverageLocalConfidence, "0.###") + "%",
                    scoreDistribution.RowsAtOrAboveAlc, scoreDistribution.RawRows);
                ScoreRow(sb, "Local-confidence stretch alone", scoreDistribution.RowsPassingLocalStretch,
                    scoreDistribution.RawRows);
                ScoreRow(sb, "Length + " + confidenceLabel, scoreDistribution.RowsPassingLengthAndAlc,
                    scoreDistribution.RawRows);
                ScoreRow(sb, "All three gates", scoreDistribution.RowsPassingAllCriteria,
                    scoreDistribution.RawRows);
                sb.Append("\n| ").Append(confidenceLabel)
                  .Append(" bin (%) | Raw rows | Retained after all gates |\n|---|---|---|\n");
                foreach (DeNovoScoreBin bin in scoreDistribution.Bins)
                    sb.Append("| ").Append(bin.Label).Append(" | ").Append(Int(bin.RawRows)).Append(" | ")
                      .Append(Int(bin.PassingAllCriteria)).Append(" |\n");
                sb.Append('\n');
            }

            DeNovoIdentificationSummary? identification = store.Manifest.DeNovoIdentification;
            if (identification != null)
            {
                identification.Validate();
                sb.Append("## De novo identification consolidation (no XIC)\n\n")
                  .Append("This census starts from the accepted ").Append(sourceLabel)
                  .Append(" sequence calls and removes repeated spectra, ")
                  .Append("charge states and technical-run repeats. PTM notation is removed; I and L remain distinct ")
                  .Append("in the primary count. No peptide area or XIC success is required.\n\n")
                  .Append("| Scope and grain | Count |\n|---|---|\n");
                Row(sb, "Accepted spectrum calls", Int(identification.FilteredSpectrumCalls));
                Row(sb, "Accepted run × PTM-cleaned sequence × charge groups", Int(identification.FilteredRunSequenceChargeGroups));
                Row(sb, "Accepted distinct PTM-cleaned sequences, pooled", Int(identification.FilteredDistinctSequencesIgnoringPtm));
                Row(sb, "Accepted distinct sequences after I/L folding", Int(identification.FilteredDistinctSequencesAfterIleLeuFold));
                Row(sb, "Calls carried forward after PatternLab reconciliation", Int(identification.CarriedForwardSpectrumCalls));
                Row(sb, "Carried-forward distinct PTM-cleaned sequences, pooled", Int(identification.CarriedForwardDistinctSequencesIgnoringPtm));
                Row(sb, "Carried-forward distinct sequences after I/L folding", Int(identification.CarriedForwardDistinctSequencesAfterIleLeuFold));
                Row(sb, "Post-XIC de novo run × sequence × charge representatives", Int(data.DeNovoCount));
                sb.Append('\n');
            }

            // ---- grouped -------------------------------------------------------------------------
            sb.Append("## Grouped (the three runs pooled)\n\n");
            AppendFractionBlock(sb, grouped);

            sb.Append("## Reference-envelope model diagnostics\n\n")
              .Append("The de novo envelope is a model estimate from de novo M+0 and identified ")
              .Append("envelope/M+0 ratios in fixed neutral-mass bins. It is not an observed envelope and ")
              .Append("not an empirical calibration of de novo identification accuracy. Cross-validation ")
              .Append("holds out one identified representative at a time. The pooled-versus-bin range is ")
              .Append("a sensitivity analysis, not a confidence interval.\n\n")
              .Append("| Diagnostic | Value |\n|---|---|\n");
            Row(sb, "Identified representatives supporting the model", Int(envelopeDiagnostics.IdentifiedPeptides));
            Row(sb, "Leave-one-out weighted absolute percent error",
                Pct(envelopeDiagnostics.CrossValidatedWeightedAbsolutePercentError, 2));
            Row(sb, "Leave-one-out median absolute percent error",
                Pct(envelopeDiagnostics.CrossValidatedMedianAbsolutePercentError, 2));
            Row(sb, "Leave-one-out 90th-percentile absolute percent error",
                Pct(envelopeDiagnostics.CrossValidatedP90AbsolutePercentError, 2));
            Row(sb, "Leave-one-out signed bias", Pct(envelopeDiagnostics.CrossValidatedSignedBiasFraction, 2));
            Row(sb, "Fixed-bin de novo model estimate", Exp(envelopeDiagnostics.FixedBinDeNovoEstimate));
            Row(sb, "Pooled-ratio sensitivity comparator", Exp(envelopeDiagnostics.PooledRatioDeNovoEstimate));
            sb.Append("\n| Neutral-mass bin (Da) | Identified support | Envelope / M+0 | Pooled fallback |\n")
              .Append("|---|---:|---:|---|\n");
            foreach (ReferenceEnvelopeBinDiagnostic bin in envelopeDiagnostics.Bins)
            {
                string upper = bin.UpperMassDa.HasValue
                    ? Num(bin.UpperMassDa.Value, "F0") : "Inf";
                sb.Append("| ").Append(Num(bin.LowerMassDa, "F0")).Append("-").Append(upper)
                  .Append(" | ").Append(Int(bin.IdentifiedPeptides)).Append(" | ")
                  .Append(Num(bin.EnvelopeToM0Ratio, "F3")).Append("x | ")
                  .Append(bin.UsesPooledFallback ? "yes" : "no").Append(" |\n");
            }
            sb.Append('\n');

            // ---- per run -------------------------------------------------------------------------
            sb.Append("## Per run\n\n");
            foreach (NativeRunFraction f in data.Fractions)
            {
                sb.Append("### ").Append(f.Run).Append("\n\n");
                AppendFractionBlock(sb, f);
            }

            // ---- the side-by-side summary table --------------------------------------------------
            sb.Append("## Side by side, share of TIC\n\n");
            sb.Append("| Quantity | ");
            foreach (NativeRunFraction f in data.Fractions) sb.Append(f.Run).Append(" | ");
            sb.Append("grouped |\n|---|");
            for (int i = 0; i <= data.Fractions.Count; i++) sb.Append("---|");
            sb.Append('\n');

            var all = data.Fractions.Concat(new[] { grouped }).ToList();
            SummaryRow(sb, "TIC", all, f => Exp(f.Tic));
            SummaryRow(sb, "Identified peptides", all, f => Int(f.IdentifiedPeptides));
            SummaryRow(sb, "Identified M+0", all, f => Pct(f.IdentifiedFractionM0, 2));
            SummaryRow(sb, "Identified envelope", all, f => Pct(f.IdentifiedFractionEnvelope, 2));
            SummaryRow(sb, "De novo peptides", all, f => Int(f.DeNovoPeptides));
            SummaryRow(sb, "De novo M+0", all, f => Pct(f.DeNovoFractionM0, 2));
            SummaryRow(sb, "De novo envelope, reference-model estimate", all,
                f => Pct(f.DeNovoFractionReferenceCalibrated, 2));
            SummaryRow(sb, "Recovered M+0 (sensitivity; not a formal lower bound)", all,
                f => Pct(f.RecoveredFractionM0, 2));
            SummaryRow(sb, "Recovered, identified-reference model estimate", all,
                f => Pct(f.RecoveredFractionReferenceCalibrated, 2));
            SummaryRow(sb, "Recovered, unadjusted six-window diagnostic", all,
                f => Pct(f.RecoveredFractionEnvelope, 2));
            SummaryRow(sb, "Identified but not quantified", all, f => Pct(f.UnderSampledFraction, 4));
            SummaryRow(sb, "Resolved isotope-series diagnostic (not FDR-calibrated)", all,
                f => Pct(f.DetectedFractionPeptideLike, 2));
            SummaryRow(sb, "  of which z >= 2", all, f => Pct(f.DetectedFractionMultiplyCharged, 2));
            SummaryRow(sb, "  of which z = 1", all, f => Pct(f.DetectedFractionSinglyCharged, 2));
            sb.Append('\n');

            // ---- the isotopes-credited histogram --------------------------------------------------
            sb.Append("## Isotopes credited\n\n");
            sb.Append("How many of the six isotope slots actually carried signal, per peptide, over the ")
              .Append("pooled table. The crediting is EMPIRICAL: an isotope that is not there is not ")
              .Append("credited and no averagine fills it in, which is why most peptides sit below the ")
              .Append("ceiling.\n\n");

            sb.Append("| Isotopes credited | identified | de novo | all | share of all |\n");
            sb.Append("|---|---|---|---|---|\n");
            int basis = AnalyticsBuilder.IsotopeBasis;
            for (int k = 1; k <= basis; k++)
            {
                int kk = k;
                int id = data.Peptides.Count(p => Side(p) == 0 && Math.Min(basis, p.IsotopesCredited) == kk);
                int dn = data.Peptides.Count(p => Side(p) == 1 && Math.Min(basis, p.IsotopesCredited) == kk);
                sb.Append("| ").Append(k == basis ? Int(k) + "+" : Int(k)).Append(" | ").Append(Int(id)).Append(" | ").Append(Int(dn))
                  .Append(" | ").Append(Int(id + dn)).Append(" | ")
                  .Append(Pct(data.Peptides.Count == 0 ? 0 : (double)(id + dn) / data.Peptides.Count, 2))
                  .Append(" |\n");
            }
            sb.Append("| total | ").Append(Int(data.IdentifiedCount)).Append(" | ")
              .Append(Int(data.DeNovoCount)).Append(" | ").Append(Int(data.Peptides.Count))
              .Append(" | 100.00% |\n\n");

            sb.Append("Peptide counts after the collapse: identified ").Append(Int(data.IdentifiedCount))
              .Append(", de novo ").Append(Int(data.DeNovoCount)).Append(", total ")
              .Append(Int(data.Peptides.Count)).Append(".\n\n");

            // ---- the family table -----------------------------------------------------------------
            sb.Append("## Top 10 families\n\n");
            sb.Append("Attributed by homology at ").Append(Pct(families.IdentityThreshold, 0))
              .Append(" identity, target members only, ranked by total envelope. A peptide that reaches ")
              .Append("two families counts in both, so these rows deliberately overlap and must not be ")
              .Append("summed against the measured envelope.\n\n");

            sb.Append("| # | Family id | Description | Identified envelope | De novo reference-model estimate |\n");
            sb.Append("|---|---|---|---|---|\n");
            int rank = 1;
            foreach (FamilyEnvelopeQuantRow f in families.Families.Take(10))
            {
                string desc = (f.Description ?? string.Empty).Replace('|', '/');
                if (desc.Length > 90) desc = desc.Substring(0, 90);
                sb.Append("| ").Append(Int(rank++)).Append(" | ").Append(Int(f.FamilyUserId)).Append(" | ")
                  .Append(desc).Append(" | ").Append(Exp(f.IdentifiedEnvelope)).Append(" | ")
                  .Append(Exp(f.DeNovoEnvelopeReferenceCalibrated)).Append(" |\n");
            }
            sb.Append('\n');

            sb.Append("| Family rollup | Value |\n|---|---|\n");
            Row(sb, "Families reached", Int(families.Families.Count));
            Row(sb, "Measured identified envelope (each peptide once)", Exp(families.MeasuredIdentifiedEnvelope));
            Row(sb, "De novo reference-model estimate (each peptide once)",
                Exp(families.MeasuredDeNovoEnvelopeReferenceCalibrated));
            Row(sb, "Model-estimated total (identified observed + de novo reference model)",
                Exp(families.MeasuredTotalEnvelopeReferenceCalibrated));
            Row(sb, "Unattributed identified envelope", Exp(families.UnattributedIdentifiedEnvelope)
                + " over " + Int(families.UnattributedIdentifiedPeptides) + " peptide(s)");
            Row(sb, "Unattributed de novo reference-model estimate",
                Exp(families.UnattributedDeNovoEnvelopeReferenceCalibrated)
                + " over " + Int(families.UnattributedDeNovoPeptides) + " peptide(s)");
            Row(sb, "Sum of the bars plus the residual (overlaps)", Exp(families.TotalEnvelope));
            sb.Append('\n');

            // ---- the identity sweep ---------------------------------------------------------------
            sb.Append("## Identity sweep\n\n");
            sb.Append("| Identity | All components | Target-bearing | Non-contaminant target | Mixed target/decoy | Mixing score |\n")
              .Append("|---|---|---|---|---|---|\n");
            foreach (IdentityScanRow r in sweep)
            {
                sb.Append("| ").Append(Int(r.Identity)).Append("% | ").Append(Int(r.Groups)).Append(" | ")
                  .Append(Int(r.TargetGroups)).Append(" | ").Append(Int(r.NonContaminantTargetGroups)).Append(" | ")
                  .Append(Int(r.MixedGroups)).Append(" | ").Append(Num(r.Score, "F4")).Append(" |\n");
            }
            sb.Append('\n');

            // ---- the ratio answer ------------------------------------------------------------------
            AppendRatioAnswer(sb, data, grouped);

            // ---- the figures -----------------------------------------------------------------------
            sb.Append("## Figures\n\n");
            sb.Append("All eight at 1920x1080 device pixels over a 960x540 layout, in the LIGHT slide ")
              .Append("palette, written into `").Append(figuresFolder).Append("`.\n\n");
            sb.Append("| File | Pixels | Bytes |\n|---|---|---|\n");
            foreach (string p in figures)
            {
                long size = File.Exists(p) ? new FileInfo(p).Length : 0;
                sb.Append("| `").Append(Path.GetFileName(p)).Append("` | 1920x1080 | ")
                  .Append(Int((int)Math.Min(int.MaxValue, size))).Append(" |\n");
            }
            sb.Append('\n');

            sb.Append("Generated by `FamilyQuant.Tests ").Append(Argument).Append("` on ")
              .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
              .Append(" UTC.\n");

            return sb.ToString();
        }

        private static string BuildPlotDataJson(AnalyticsData data, NativeRunFraction grouped,
                                                FamilyEnvelopeQuantResult families,
                                                DeNovoOverlapSummary overlap,
                                                DeNovoScoreDistribution scoreDistribution,
                                                List<IdentityScanRow> sweep,
                                                FamilyClusterFile curation)
        {
            ReferenceComponentSummary reference = ReferenceComponentSummary.FromCuration(curation);
            var runRows = data.Fractions.Where(f => !f.IsGrouped).Select((f, i) => new
            {
                run = f.Run,
                displayRun = "Run " + (i + 1).ToString(CultureInfo.InvariantCulture),
                searchEngineRepresentatives = data.Peptides.Count(p =>
                    string.Equals(p.Run, f.Run, StringComparison.OrdinalIgnoreCase)
                    && p.Side == NativeQuantFractions.SideIdentified),
                deNovoRepresentatives = data.Peptides.Count(p =>
                    string.Equals(p.Run, f.Run, StringComparison.OrdinalIgnoreCase)
                    && p.Side == NativeQuantFractions.SideDeNovo),
                tic = f.Tic,
                identifiedFractionEnvelope = f.IdentifiedFractionEnvelope,
                deNovoFractionReferenceCalibrated = f.DeNovoFractionReferenceCalibrated,
                recoveredFractionReferenceCalibrated = f.RecoveredFractionReferenceCalibrated,
            }).ToList();

            var payload = new
            {
                schemaVersion = 1,
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                dataset = "three 20220624_Bjararaca_1ug_3step-240min acquisitions",
                plotOrder = PresentationFigures.FileNames,
                deNovoScoreDistribution = scoreDistribution,
                peptideEvidence = new
                {
                    runs = runRows,
                    pooledSearchEngineRepresentatives = data.IdentifiedCount,
                    pooledDeNovoRepresentatives = data.DeNovoCount,
                    pooledRepresentatives = data.Peptides.Count,
                    deNovoShareOfRepresentatives = data.Peptides.Count == 0 ? 0.0
                        : (double)data.DeNovoCount / data.Peptides.Count,
                    overlap,
                },
                signalByRun = runRows,
                signalUplift = new
                {
                    searchEngineOnlyFractionOfTic = grouped.IdentifiedFractionEnvelope,
                    searchPlusDeNovoFractionOfTic = grouped.RecoveredFractionReferenceCalibrated,
                    deNovoAdditionFractionOfTic = grouped.DeNovoFractionReferenceCalibrated,
                    relativeUplift = grouped.IdentifiedFractionEnvelope > 0
                        ? grouped.DeNovoFractionReferenceCalibrated / grouped.IdentifiedFractionEnvelope : 0.0,
                },
                identitySweep = sweep,
                referenceComponents = reference,
                familyAttribution = new
                {
                    identityThreshold = families.IdentityThreshold,
                    measuredIdentifiedEnvelope = families.MeasuredIdentifiedEnvelope,
                    measuredDeNovoEnvelopeReferenceCalibrated =
                        families.MeasuredDeNovoEnvelopeReferenceCalibrated,
                    measuredTotalEnvelopeReferenceCalibrated =
                        families.MeasuredTotalEnvelopeReferenceCalibrated,
                    unattributedIdentifiedEnvelope = families.UnattributedIdentifiedEnvelope,
                    unattributedDeNovoEnvelopeReferenceCalibrated =
                        families.UnattributedDeNovoEnvelopeReferenceCalibrated,
                    families = families.Families
                        .OrderByDescending(f => f.TotalEnvelopeReferenceCalibrated)
                        .ThenBy(f => f.FamilyUserId)
                        .Select(f => new
                        {
                            familyUserId = f.FamilyUserId,
                            description = f.Description,
                            identifiedPeptides = f.IdentifiedPeptides,
                            deNovoPeptides = f.DeNovoPeptides,
                            identifiedEnvelope = f.IdentifiedEnvelope,
                            deNovoEnvelopeReferenceCalibrated = f.DeNovoEnvelopePrimary,
                            totalEnvelopeReferenceCalibrated = f.TotalEnvelopeReferenceCalibrated,
                            deNovoShare = f.TotalEnvelopeReferenceCalibrated > 0
                                ? f.DeNovoEnvelopePrimary / f.TotalEnvelopeReferenceCalibrated : 0.0,
                        }).ToList(),
                },
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }

        private static void ScoreRow(StringBuilder sb, string label, int count, int denominator)
        {
            sb.Append("| ").Append(label).Append(" | ").Append(Int(count)).Append(" | ")
              .Append(Pct(denominator <= 0 ? 0 : (double)count / denominator, 2)).Append(" |\n");
        }

        /// <summary>One run's (or the grouped) whole decomposition as a two-column table.</summary>
        private static void AppendFractionBlock(StringBuilder sb, NativeRunFraction f)
        {
            sb.Append("| Quantity | Area | Share of TIC | Peptides / features |\n|---|---|---|---|\n");

            sb.Append("| TIC | ").Append(Exp(f.Tic)).Append(" | 100.00% | |\n");

            sb.Append("| Identified, M+0 | ").Append(Exp(f.IdentifiedAreaM0)).Append(" | ")
              .Append(Pct(f.IdentifiedFractionM0, 2)).Append(" | ").Append(Int(f.IdentifiedPeptides)).Append(" |\n");
            sb.Append("| Identified, envelope | ").Append(Exp(f.IdentifiedEnvelope)).Append(" | ")
              .Append(Pct(f.IdentifiedFractionEnvelope, 2)).Append(" | ").Append(Int(f.IdentifiedPeptides)).Append(" |\n");

            sb.Append("| De novo, M+0 | ").Append(Exp(f.DeNovoAreaM0)).Append(" | ")
              .Append(Pct(f.DeNovoFractionM0, 2)).Append(" | ").Append(Int(f.DeNovoPeptides)).Append(" |\n");
            sb.Append("| De novo, reference-envelope model estimate | ")
              .Append(Exp(f.DeNovoEnvelopeReferenceCalibrated)).Append(" | ")
              .Append(Pct(f.DeNovoFractionReferenceCalibrated, 2)).Append(" | ")
              .Append(Int(f.DeNovoPeptides)).Append(" |\n");
            sb.Append("| De novo, unadjusted six-window envelope (diagnostic) | ").Append(Exp(f.DeNovoEnvelope)).Append(" | ")
              .Append(Pct(f.DeNovoFractionEnvelope, 2)).Append(" | ").Append(Int(f.DeNovoPeptides)).Append(" |\n");

            sb.Append("| Recovered, M+0 sensitivity (not a formal lower bound) | ")
              .Append(Exp(f.IdentifiedAreaM0 + f.DeNovoAreaM0)).Append(" | ")
              .Append(Pct(f.RecoveredFractionM0, 2)).Append(" | ").Append(Int(f.IdentifiedPeptides + f.DeNovoPeptides)).Append(" |\n");
            sb.Append("| **Recovered, identified-reference model estimate** | **")
              .Append(Exp(f.IdentifiedEnvelope + f.DeNovoEnvelopeReferenceCalibrated)).Append("** | **")
              .Append(Pct(f.RecoveredFractionReferenceCalibrated, 2)).Append("** | ")
              .Append(Int(f.IdentifiedPeptides + f.DeNovoPeptides)).Append(" |\n");
            sb.Append("| Recovered, unadjusted six-window diagnostic | ")
              .Append(Exp(f.IdentifiedEnvelope + f.DeNovoEnvelope)).Append(" | ")
              .Append(Pct(f.RecoveredFractionEnvelope, 2)).Append(" | ").Append(Int(f.IdentifiedPeptides + f.DeNovoPeptides)).Append(" |\n");
            sb.Append("| De novo cross-side overlaps suppressed | | | ")
              .Append(Int(f.DeNovoSuppressedCrossSideOverlaps)).Append(" |\n");

            sb.Append("| Under-sampled, identified side | ").Append(Exp(f.IdentifiedUnderSampled)).Append(" | ")
              .Append(f.Tic > 0 ? Pct(f.IdentifiedUnderSampled / f.Tic, 4) : "n/a").Append(" | ")
              .Append(Int(f.IdentifiedUnderSampledPeptides)).Append(" |\n");
            sb.Append("| Under-sampled, de novo side | ").Append(Exp(f.DeNovoUnderSampled)).Append(" | ")
              .Append(f.Tic > 0 ? Pct(f.DeNovoUnderSampled / f.Tic, 4) : "n/a").Append(" | ")
              .Append(Int(f.DeNovoUnderSampledPeptides)).Append(" |\n");
            sb.Append("| Under-sampled, both sides | ").Append(Exp(f.IdentifiedUnderSampled + f.DeNovoUnderSampled))
              .Append(" | ").Append(Pct(f.UnderSampledFraction, 4)).Append(" | ")
              .Append(Int(f.IdentifiedUnderSampledPeptides + f.DeNovoUnderSampledPeptides))
              .Append(f.UnderSampledIsEstimate ? " (includes an ESTIMATE)" : string.Empty).Append(" |\n");

            sb.Append("| Resolved isotope-series diagnostic (not peptide-FDR calibrated) | ")
              .Append(Exp(f.DetectedPeptideLikeArea)).Append(" | ")
              .Append(Pct(f.DetectedFractionPeptideLike, 2)).Append(" | ").Append(Int(f.DetectedEnvelopeCount)).Append(" |\n");
            sb.Append("| Diagnostic, z >= 2 (more specific; not FDR-calibrated) | ").Append(Exp(f.DetectedAreaMultiplyCharged)).Append(" | ")
              .Append(Pct(f.DetectedFractionMultiplyCharged, 2)).Append(" | ").Append(Int(f.DetectedCountMultiplyCharged)).Append(" |\n");
            sb.Append("| Diagnostic, z = 1 (less specific; not FDR-calibrated) | ").Append(Exp(f.DetectedAreaSinglyCharged)).Append(" | ")
              .Append(Pct(f.DetectedFractionSinglyCharged, 2)).Append(" | ").Append(Int(f.DetectedCountSinglyCharged)).Append(" |\n");

            for (int z = Ms1EnvelopeDetector.MinCharge; z <= Ms1EnvelopeDetector.MaxCharge; z++)
            {
                double area = f.DetectedAreaByCharge != null && z < f.DetectedAreaByCharge.Length
                    ? f.DetectedAreaByCharge[z] : 0.0;
                int count = f.DetectedCountByCharge != null && z < f.DetectedCountByCharge.Length
                    ? f.DetectedCountByCharge[z] : 0;
                // Named "per charge" so the z = 1 line here is visibly the same measurement as the
                // weaker-evidence rollup above it and not a second, disagreeing number.
                sb.Append("| Detected, per charge z = ").Append(Int(z)).Append(" | ").Append(Exp(area))
                  .Append(" | ").Append(Pct(f.DetectedFractionForCharge(z), 2)).Append(" | ")
                  .Append(Int(count)).Append(" |\n");
            }

            sb.Append("\nThe detector is shown as an independent diagnostic. Its difference from an assigned ")
              .Append("estimate is not interpreted as a biological class, and the remainder of TIC is not ")
              .Append("called non-peptide or noise.\n\n");

            sb.Append("Envelope over M+0: identified ")
              .Append(f.IdentifiedAreaM0 > 0 ? Num(f.IdentifiedEnvelope / f.IdentifiedAreaM0, "F3") : "n/a")
              .Append("x, de novo ")
              .Append(f.DeNovoAreaM0 > 0 ? Num(f.DeNovoEnvelope / f.DeNovoAreaM0, "F3") : "n/a")
              .Append("x.\n\n");
        }

        // ================================================================= the ratio answer

        /// <summary>
        /// The question the talk will be asked: the identified side's envelope is about 2.3x its M+0 and
        /// the de novo side's looked about 4.4x. Both ratios are recomputed here on the SAME final basis
        /// (the pooled, collapsed table at the 6-isotope ceiling and the 3-point minimum), and then
        /// decomposed against the three candidate explanations with data rather than argument:
        /// the peptide mass distribution, the charge distribution, and the higher isotopes of the de novo
        /// side picking up interference.
        ///
        /// The decisive test is MODEL-FREE and needs no averagine: compare the two sides WITHIN the same
            /// mass bin. This controls the broad mass trend only; elemental composition, extraction
            /// interference and precursor assignment remain possible explanations for residual differences.
        /// </summary>
        private static void AppendRatioAnswer(StringBuilder sb, AnalyticsData data, NativeRunFraction grouped)
        {
            sb.Append("## The envelope / M+0 ratio, both sides on the same basis\n\n");

            double idRatio = grouped.IdentifiedAreaM0 > 0
                ? grouped.IdentifiedEnvelope / grouped.IdentifiedAreaM0 : 0;
            double dnRatio = grouped.DeNovoAreaM0 > 0
                ? grouped.DeNovoEnvelope / grouped.DeNovoAreaM0 : 0;

            sb.Append("Both are the POOLED ratio over the three runs, after the per-peptide collapse, at ")
              .Append("the 6-isotope ceiling and the 3-point minimum. Same table, same collapse, same ")
              .Append("ceiling: the two numbers are directly comparable.\n\n");

            sb.Append("| Side | Sum M+0 | Sum envelope | envelope / M+0 | Peptides |\n|---|---|---|---|---|\n");
            sb.Append("| Identified | ").Append(Exp(grouped.IdentifiedAreaM0)).Append(" | ")
              .Append(Exp(grouped.IdentifiedEnvelope)).Append(" | ").Append(Num(idRatio, "F3"))
              .Append("x | ").Append(Int(grouped.IdentifiedPeptides)).Append(" |\n");
            sb.Append("| De novo | ").Append(Exp(grouped.DeNovoAreaM0)).Append(" | ")
              .Append(Exp(grouped.DeNovoEnvelope)).Append(" | ").Append(Num(dnRatio, "F3"))
              .Append("x | ").Append(Int(grouped.DeNovoPeptides)).Append(" |\n\n");

            sb.Append("The gap is ").Append(Num(idRatio > 0 ? dnRatio / idRatio : 0, "F2"))
              .Append("x. Everything below asks what owns it.\n\n");

            List<AnalyticsPeptide> id = data.Peptides.Where(p => Side(p) == 0 && p.AreaAt(0) > 0).ToList();
            List<AnalyticsPeptide> dn = data.Peptides.Where(p => Side(p) == 1 && p.AreaAt(0) > 0).ToList();

            // ---- composition ----------------------------------------------------------------------
            sb.Append("### What the two sides are made of\n\n");
            sb.Append("| Quantity | identified | de novo |\n|---|---|---|\n");
            sb.Append("| Peptides with a positive M+0 | ").Append(Int(id.Count)).Append(" | ")
              .Append(Int(dn.Count)).Append(" |\n");
            sb.Append("| Median mass (Da) | ").Append(Num(Median(id.Select(p => p.Mass)), "F1")).Append(" | ")
              .Append(Num(Median(dn.Select(p => p.Mass)), "F1")).Append(" |\n");
            sb.Append("| M+0-weighted mean mass (Da) | ")
              .Append(Num(WeightedMean(id, p => p.Mass, p => p.AreaAt(0)), "F1")).Append(" | ")
              .Append(Num(WeightedMean(dn, p => p.Mass, p => p.AreaAt(0)), "F1")).Append(" |\n");
            sb.Append("| Median charge | ").Append(Num(Median(id.Select(p => (double)p.Charge)), "F2")).Append(" | ")
              .Append(Num(Median(dn.Select(p => (double)p.Charge)), "F2")).Append(" |\n");
            sb.Append("| M+0-weighted mean charge | ")
              .Append(Num(WeightedMean(id, p => p.Charge, p => p.AreaAt(0)), "F2")).Append(" | ")
              .Append(Num(WeightedMean(dn, p => p.Charge, p => p.AreaAt(0)), "F2")).Append(" |\n");
            sb.Append("| Median M+0 area | ").Append(Exp(Median(id.Select(p => p.AreaAt(0))))).Append(" | ")
              .Append(Exp(Median(dn.Select(p => p.AreaAt(0))))).Append(" |\n");
            sb.Append("| Mean isotopes credited | ")
              .Append(Num(id.Count == 0 ? 0 : id.Average(p => (double)p.IsotopesCredited), "F2")).Append(" | ")
              .Append(Num(dn.Count == 0 ? 0 : dn.Average(p => (double)p.IsotopesCredited), "F2")).Append(" |\n");
            sb.Append("| Median per-peptide envelope / M+0 | ")
              .Append(Num(Median(id.Select(p => p.Envelope / p.AreaAt(0))), "F3")).Append("x | ")
              .Append(Num(Median(dn.Select(p => p.Envelope / p.AreaAt(0))), "F3")).Append("x |\n\n");

            // ---- charge split ---------------------------------------------------------------------
            sb.Append("### The ratio by charge\n\n");
            sb.Append("| Charge | id peptides | id envelope / M+0 | dn peptides | dn envelope / M+0 |\n");
            sb.Append("|---|---|---|---|---|\n");
            foreach (int z in id.Select(p => p.Charge).Concat(dn.Select(p => p.Charge))
                                .Distinct().OrderBy(z => z))
            {
                int zz = z;
                List<AnalyticsPeptide> a = id.Where(p => p.Charge == zz).ToList();
                List<AnalyticsPeptide> b = dn.Where(p => p.Charge == zz).ToList();
                sb.Append("| ").Append(Int(z)).Append(" | ").Append(Int(a.Count)).Append(" | ")
                  .Append(RatioOf(a)).Append(" | ").Append(Int(b.Count)).Append(" | ")
                  .Append(RatioOf(b)).Append(" |\n");
            }
            sb.Append('\n');

            // ---- mass bins, the mass-stratified diagnostic ----------------------------------------
            double[] edges = { 0, 800, 1200, 1600, 2000, 2500, 3000, double.PositiveInfinity };
            sb.Append("### The ratio within the same mass bin (mass-stratified diagnostic)\n\n");
            sb.Append("| Mass bin (Da) | id peptides | id M+0 share | id envelope / M+0 | dn peptides | ")
              .Append("dn M+0 share | dn envelope / M+0 | dn / id |\n");
            sb.Append("|---|---|---|---|---|---|---|---|\n");

            double idM0Total = id.Sum(p => p.AreaAt(0));
            double dnM0Total = dn.Sum(p => p.AreaAt(0));
            var massStrata = new List<Stratum>();

            for (int b = 0; b + 1 < edges.Length; b++)
            {
                double lo = edges[b], hi = edges[b + 1];
                List<AnalyticsPeptide> a = id.Where(p => p.Mass >= lo && p.Mass < hi).ToList();
                List<AnalyticsPeptide> c = dn.Where(p => p.Mass >= lo && p.Mass < hi).ToList();

                var s = new Stratum
                {
                    IdM0 = a.Sum(p => p.AreaAt(0)),
                    IdEnvelope = a.Sum(p => p.Envelope),
                    DeNovoM0 = c.Sum(p => p.AreaAt(0)),
                    DeNovoEnvelope = c.Sum(p => p.Envelope),
                };
                massStrata.Add(s);

                string label = double.IsPositiveInfinity(hi)
                    ? Num(lo, "F0") + "+"
                    : Num(lo, "F0") + " to " + Num(hi, "F0");

                sb.Append("| ").Append(label).Append(" | ").Append(Int(a.Count)).Append(" | ")
                  .Append(Pct(idM0Total > 0 ? s.IdM0 / idM0Total : 0, 2)).Append(" | ")
                  .Append(s.IdM0 > 0 ? Num(s.IdRatio, "F3") + "x" : "n/a").Append(" | ")
                  .Append(Int(c.Count)).Append(" | ")
                  .Append(Pct(dnM0Total > 0 ? s.DeNovoM0 / dnM0Total : 0, 2)).Append(" | ")
                  .Append(s.DeNovoM0 > 0 ? Num(s.DeNovoRatio, "F3") + "x" : "n/a").Append(" | ")
                  .Append(s.IdM0 > 0 && s.DeNovoM0 > 0 && s.IdRatio > 0
                      ? Num(s.DeNovoRatio / s.IdRatio, "F2") + "x" : "n/a").Append(" |\n");
            }
            sb.Append('\n');

            // The two sides put on ONE composition and then compared. Only strata both sides occupy take
            // part, so the comparison is never carried by a bin one side does not reach.
            Standardized mass = Standardize(massStrata);

            var chargeStrata = new List<Stratum>();
            foreach (int z in id.Select(p => p.Charge).Concat(dn.Select(p => p.Charge)).Distinct().OrderBy(z => z))
            {
                int zz = z;
                chargeStrata.Add(new Stratum
                {
                    IdM0 = id.Where(p => p.Charge == zz).Sum(p => p.AreaAt(0)),
                    IdEnvelope = id.Where(p => p.Charge == zz).Sum(p => p.Envelope),
                    DeNovoM0 = dn.Where(p => p.Charge == zz).Sum(p => p.AreaAt(0)),
                    DeNovoEnvelope = dn.Where(p => p.Charge == zz).Sum(p => p.Envelope),
                });
            }
            Standardized charge = Standardize(chargeStrata);

            sb.Append("### Standardization: the two sides put on one composition\n\n");
            sb.Append("Each row asks the counterfactual directly. \"De novo on the identified masses\" is ")
              .Append("the de novo ratio measured inside each mass bin, reweighted by the IDENTIFIED ")
              .Append("side's M+0 in those bins: what the de novo ratio would be if its signal sat at the ")
              .Append("identified side's masses. The baseline beside it is the identified ratio over the ")
              .Append("same shared bins, so the two are on one composition and their quotient is the gap ")
              .Append("that mass cannot explain.\n\n");

            sb.Append("| Standardization | identified | de novo | de novo / identified |\n|---|---|---|---|\n");
            sb.Append("| As measured | ").Append(Num(idRatio, "F3")).Append("x | ")
              .Append(Num(dnRatio, "F3")).Append("x | ")
              .Append(idRatio > 0 ? Num(dnRatio / idRatio, "F2") + "x" : "n/a").Append(" |\n");
            sb.Append("| On the identified MASS distribution | ").Append(Num(mass.IdMatched, "F3")).Append("x | ")
              .Append(Num(mass.DeNovoOnIdWeights, "F3")).Append("x | ")
              .Append(mass.IdMatched > 0 ? Num(mass.DeNovoOnIdWeights / mass.IdMatched, "F2") + "x" : "n/a")
              .Append(" |\n");
            sb.Append("| On the de novo MASS distribution | ").Append(Num(mass.IdOnDeNovoWeights, "F3")).Append("x | ")
              .Append(Num(mass.DeNovoMatched, "F3")).Append("x | ")
              .Append(mass.IdOnDeNovoWeights > 0
                  ? Num(mass.DeNovoMatched / mass.IdOnDeNovoWeights, "F2") + "x" : "n/a").Append(" |\n");
            sb.Append("| On the identified CHARGE distribution | ").Append(Num(charge.IdMatched, "F3")).Append("x | ")
              .Append(Num(charge.DeNovoOnIdWeights, "F3")).Append("x | ")
              .Append(charge.IdMatched > 0
                  ? Num(charge.DeNovoOnIdWeights / charge.IdMatched, "F2") + "x" : "n/a").Append(" |\n");
            sb.Append("| On the de novo CHARGE distribution | ").Append(Num(charge.IdOnDeNovoWeights, "F3")).Append("x | ")
              .Append(Num(charge.DeNovoMatched, "F3")).Append("x | ")
              .Append(charge.IdOnDeNovoWeights > 0
                  ? Num(charge.DeNovoMatched / charge.IdOnDeNovoWeights, "F2") + "x" : "n/a").Append(" |\n\n");

            // ---- the isotope pattern itself -------------------------------------------------------
            sb.Append("### The shape of the envelope, isotope by isotope\n\n");
            sb.Append("Each cell is that isotope's summed area over the side's summed M+0. The `sum` ")
              .Append("column is what those cells actually total, and it lands a hair under the ")
              .Append("envelope / M+0 ratio above rather than exactly on it: the per-isotope areas are ")
              .Append("read back off the representative ANCHOR row, and for the few peptides whose ")
              .Append("representative is resolved by the join's fall-back the anchor's upper isotopes ")
              .Append("are not the ones the collapse's envelope sum was taken from. The ratio above is ")
              .Append("the number to quote; this table is for the SHAPE. The averagine column is the ")
              .Append("rule of thumb M+1 / M+0 = mass * ")
              .Append(Num(AveragineM1PerDalton, "E3"))
              .Append(" evaluated at the side's M+0-weighted mean mass, given for CONTEXT only; it is an ")
              .Append("approximation and no measured number here stands on it.\n\n");

            sb.Append("| Side | M+0 | M+1 | M+2 | M+3 | M+4 | M+5 | sum | averagine M+1 (approx) |\n");
            sb.Append("|---|---|---|---|---|---|---|---|---|\n");
            AppendIsotopeShape(sb, "identified", id);
            AppendIsotopeShape(sb, "de novo", dn);
            sb.Append('\n');

            // ---- the verdict, written from the numbers just printed --------------------------------
            sb.Append("### What explains it\n\n");

            double rawGap = idRatio > 0 ? dnRatio / idRatio : 0;
            double massGap = mass.IdMatched > 0 ? mass.DeNovoOnIdWeights / mass.IdMatched : 0;
            double chargeGap = charge.IdMatched > 0 ? charge.DeNovoOnIdWeights / charge.IdMatched : 0;

            sb.Append("1. **The two ratios really do differ, on one basis.** Identified ")
              .Append(Num(idRatio, "F3")).Append("x against de novo ").Append(Num(dnRatio, "F3"))
              .Append("x, both pooled over the three runs out of the one collapsed table, at the one ")
              .Append("ceiling. The difference is not a basis artefact and it is not a units artefact: ")
              .Append("numerator and denominator are the same trapezoid on both sides.\n\n");

            sb.Append("2. **Peptide mass distribution: ")
              .Append(Verdict(rawGap, massGap))
              .Append("** Standardizing the de novo ratio onto the identified side's mass distribution ")
              .Append("takes it to ").Append(Num(mass.DeNovoOnIdWeights, "F3"))
              .Append("x against an identified baseline of ").Append(Num(mass.IdMatched, "F3"))
              .Append("x over the same bins, so the gap moves from ").Append(Num(rawGap, "F2"))
              .Append("x to ").Append(Num(massGap, "F2")).Append("x. A heavier peptide genuinely ")
              .Append("carries more of its signal above M+0, so any mass difference between the two ")
              .Append("sides has to move this number, and the mass-bin table above shows how much.\n\n");

            sb.Append("3. **Charge distribution: ")
              .Append(Verdict(rawGap, chargeGap))
              .Append("** Standardizing on charge instead takes the gap from ").Append(Num(rawGap, "F2"))
              .Append("x to ").Append(Num(chargeGap, "F2")).Append("x. Charge enters twice, through the ")
              .Append("mass it implies at a given m/z and through the isotope spacing the window has to ")
              .Append("separate, so the by-charge table is worth reading beside the mass one rather than ")
              .Append("instead of it.\n\n");

            sb.Append("4. **Residual higher-isotope differences are descriptive, not a single diagnosed ")
              .Append("mechanism.** Equal neutral mass does not imply an identical theoretical isotope ")
              .Append("envelope: elemental composition matters. A residual within a broad mass bin can ")
              .Append("therefore reflect composition, different signal distributions, co-eluting ions, ")
              .Append("precursor/charge assignment, or extraction-window behaviour. In this dataset the ")
              .Append("median de novo M+0 is ").Append(Exp(Median(dn.Select(p => p.AreaAt(0)))))
              .Append(" versus ").Append(Exp(Median(id.Select(p => p.AreaAt(0)))))
              .Append(" for identified representatives; that observation is reported without assuming ")
              .Append("that every de novo precursor is low-abundance or that interference caused the gap.\n\n");

            sb.Append("**The one-liner for the slide.** The two measured six-window ratios differ on the ")
              .Append("same integration basis. Mass and charge stratification describe how the gap changes, ")
              .Append("but they do not identify a unique cause. The reference-envelope result used for ")
              .Append("recovery is a model estimate with cross-validation and pooled-versus-bin sensitivity ")
              .Append("reported above, not an empirically calibrated de novo envelope.\n\n");
        }

        /// <summary>How much of the raw gap a standardization removed, said in words rather than left to
        /// the reader to divide. Written from the two numbers, so it cannot disagree with them.</summary>
        private static string Verdict(double rawGap, double standardizedGap)
        {
            if (!(rawGap > 1.0)) return "no gap to explain.";

            double removed = (rawGap - standardizedGap) / (rawGap - 1.0);

            if (standardizedGap <= 1.02) return "it explains essentially all of the gap.";
            if (removed >= 0.5)
                return "it explains most of the gap (" + Pct(removed, 0) + " of it), but not all.";
            if (removed >= 0.15)
                return "it explains part of the gap (" + Pct(removed, 0) + " of it).";
            if (removed >= -0.05)
                return "it explains almost none of the gap (" + Pct(removed, 0) + " of it).";
            return "it does not explain the gap; standardizing on it makes the gap larger.";
        }

        /// <summary>One stratum (a mass bin, or a charge) of both sides, for the standardization.</summary>
        private sealed class Stratum
        {
            public double IdM0;
            public double IdEnvelope;
            public double DeNovoM0;
            public double DeNovoEnvelope;

            public double IdRatio => IdM0 > 0 ? IdEnvelope / IdM0 : 0;
            public double DeNovoRatio => DeNovoM0 > 0 ? DeNovoEnvelope / DeNovoM0 : 0;
        }

        /// <summary>The four numbers a standardization produces: each side's ratio over the SHARED strata,
        /// and each side's within-stratum ratios reweighted onto the other side's signal distribution.</summary>
        private readonly struct Standardized
        {
            public Standardized(double idMatched, double deNovoMatched,
                                double deNovoOnIdWeights, double idOnDeNovoWeights)
            {
                IdMatched = idMatched;
                DeNovoMatched = deNovoMatched;
                DeNovoOnIdWeights = deNovoOnIdWeights;
                IdOnDeNovoWeights = idOnDeNovoWeights;
            }

            public double IdMatched { get; }
            public double DeNovoMatched { get; }
            public double DeNovoOnIdWeights { get; }
            public double IdOnDeNovoWeights { get; }
        }

        /// <summary>
        /// Direct standardization over strata both sides occupy. A stratum only one side reaches is left
        /// out entirely rather than imputed: it carries no comparison, and letting it into one side's
        /// baseline while the other has nothing there is exactly the confound this is meant to remove.
        /// </summary>
        private static Standardized Standardize(IEnumerable<Stratum> strata)
        {
            double idM0 = 0, idEnv = 0, dnM0 = 0, dnEnv = 0, dnOnId = 0, idOnDn = 0;

            foreach (Stratum s in strata)
            {
                if (!(s.IdM0 > 0) || !(s.DeNovoM0 > 0)) continue;

                idM0 += s.IdM0;
                idEnv += s.IdEnvelope;
                dnM0 += s.DeNovoM0;
                dnEnv += s.DeNovoEnvelope;

                dnOnId += s.IdM0 * s.DeNovoRatio;
                idOnDn += s.DeNovoM0 * s.IdRatio;
            }

            return new Standardized(
                idM0 > 0 ? idEnv / idM0 : 0,
                dnM0 > 0 ? dnEnv / dnM0 : 0,
                idM0 > 0 ? dnOnId / idM0 : 0,
                dnM0 > 0 ? idOnDn / dnM0 : 0);
        }

        private static void AppendIsotopeShape(StringBuilder sb, string name, List<AnalyticsPeptide> peptides)
        {
            double m0 = peptides.Sum(p => p.AreaAt(0));
            sb.Append("| ").Append(name).Append(" | ");

            double sum = 0;
            for (int i = 0; i < AnalyticsBuilder.IsotopeBasis; i++)
            {
                int ii = i;
                double share = m0 > 0 ? peptides.Sum(p => p.AreaAt(ii)) / m0 : 0;
                sum += share;
                sb.Append(Num(share, "F3")).Append(" | ");
            }

            double meanMass = WeightedMean(peptides, p => p.Mass, p => p.AreaAt(0));
            sb.Append(Num(sum, "F3")).Append(" | ")
              .Append(Num(meanMass * AveragineM1PerDalton, "F3")).Append(" |\n");
        }

        private static string RatioOf(List<AnalyticsPeptide> peptides)
        {
            double m0 = peptides.Sum(p => p.AreaAt(0));
            double env = peptides.Sum(p => p.Envelope);
            return m0 > 0 ? Num(env / m0, "F3") + "x" : "n/a";
        }

        /// <summary>0 for the identified side, 1 for de novo, 2 for anything else.</summary>
        private static int Side(AnalyticsPeptide p) =>
            string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal) ? 0
            : string.Equals(p.Side, NativeQuantFractions.SideDeNovo, StringComparison.Ordinal) ? 1 : 2;

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToList();
            if (sorted.Count == 0) return 0;
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        private static double WeightedMean(IEnumerable<AnalyticsPeptide> peptides,
                                           Func<AnalyticsPeptide, double> value,
                                           Func<AnalyticsPeptide, double> weight)
        {
            double n = 0, d = 0;
            foreach (AnalyticsPeptide p in peptides)
            {
                double w = weight(p);
                if (!(w > 0)) continue;
                n += value(p) * w;
                d += w;
            }
            return d > 0 ? n / d : 0;
        }

        // ================================================================= the curation

        /// <summary>
        /// The venom curation at <see cref="ClusterIdentity"/>% with the EXHAUSTIVE second pass, generated
        /// once into the work dir and reused when it is already there.
        ///
        /// IT KEEPS ITS OWN FILE rather than sharing the suite's `homology_venom70.fcf`. An .fcf records
        /// no stamp of which clustering pass produced it, and the suite's cache is only invalidated when
        /// the DATABASE is newer than it - so a file written by a build whose exhaustive pass was off, or
        /// by a run that asked for the fast pass, is served back unchanged and nothing looks wrong. The
        /// caller checks the family count against the pinned exhaustive number as the second guard.
        /// </summary>
        private static (FamilyClusterFile? Fcf, string Path) EnsureCuration(string database, string workDir)
        {
            string path = Path.Combine(workDir, "talk_venom" +
                ClusterIdentity.ToString(CultureInfo.InvariantCulture) + "_exhaustive.fcf");
            string sourceSha256 = FileFingerprint.Sha256(database);
            int sourceSequenceCount = USearchTools.CountSequences(new FileInfo(database));

            try
            {
                if (File.Exists(path))
                {
                    try
                    {
                        FamilyClusterFile cached = FamilyClusterFile.Load(path);
                        bool valid = cached.ClusteringIdentity == ClusterIdentity
                            && string.Equals(cached.SourceSha256, sourceSha256, StringComparison.Ordinal)
                            && cached.SourceSequenceCount == sourceSequenceCount
                            && string.Equals(cached.ClusteringMethod, USearchTools.ExhaustiveClusteringMethod,
                                StringComparison.Ordinal);
                        if (valid)
                        {
                            Console.WriteLine("        curation read from content-verified cache " + path);
                            return (cached, path);
                        }
                        File.Delete(path);
                    }
                    catch
                    {
                        File.Delete(path);   // unreadable is regenerated, never failed over
                    }
                }

                // Written under a temporary name and moved, so a run killed mid-save cannot leave a torn
                // file that the next run would read back as a curation.
                string tmp = path + ".tmp";
                USearchTools.GenerateFCF(new FileInfo(database), ClusterIdentity, exhaustive: true).Save(tmp);
                File.Move(tmp, path, overwrite: true);
                return (FamilyClusterFile.Load(path), path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("        the curation could not be produced: " + ex.Message);
                return (null, path);
            }
        }

        // ================================================================= the identity sweep

        /// <summary>
        /// The families-versus-identity sweep, from <see cref="FamilyClusterFile.ScanIdentities"/>, cached
        /// into the work dir as a TSV so a second run of the section does not pay eighteen USEARCH runs
        /// again. The cache is discarded when the database is newer than it, exactly as the .fcf cache is.
        /// </summary>
        private static List<IdentityScanRow> EnsureSweep(string database, string workDir)
        {
            string path = Path.Combine(workDir, "talk_identity_sweep.tsv");
            string sourceSha256 = FileFingerprint.Sha256(database);
            int sourceSequenceCount = USearchTools.CountSequences(new FileInfo(database));

            try
            {
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    bool validHeader = lines.Any(l => l == "# source_sha256\t" + sourceSha256)
                        && lines.Any(l => l == "# source_sequence_count\t" +
                            sourceSequenceCount.ToString(CultureInfo.InvariantCulture))
                        && lines.Any(l => l == "# clustering_method\t" +
                            USearchTools.ExhaustiveClusteringMethod);
                    if (!validHeader) throw new InvalidDataException("input identity or clustering method changed");
                    var cached = new List<IdentityScanRow>();
                    foreach (string line in lines)
                    {
                        if (line.Length == 0 || line[0] == '#' || line.StartsWith("identity\t", StringComparison.Ordinal)) continue;
                        string[] f = line.Split('\t');
                        if (f.Length < 7) continue;
                        cached.Add(new IdentityScanRow
                        {
                            Identity = int.Parse(f[0], CultureInfo.InvariantCulture),
                            Groups = int.Parse(f[1], CultureInfo.InvariantCulture),
                            TargetGroups = int.Parse(f[2], CultureInfo.InvariantCulture),
                            NonContaminantTargetGroups = int.Parse(f[3], CultureInfo.InvariantCulture),
                            MixedGroups = int.Parse(f[4], CultureInfo.InvariantCulture),
                            Score = double.Parse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture),
                            TargetRelativeScore = double.Parse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture),
                        });
                    }
                    if (cached.Count > 3)
                    {
                        Console.WriteLine("        identity sweep read from " + path);
                        return cached;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("        the cached sweep could not be read (" + ex.Message + "); re-measuring.");
            }

            Console.WriteLine("        sweeping the identity range (one USEARCH run per step)...");
            List<IdentityScanRow> rows = FamilyClusterFile.ScanIdentities(database);

            var sb = new StringBuilder();
            sb.Append("# source_sha256\t").Append(sourceSha256).Append('\n');
            sb.Append("# source_sequence_count\t").Append(sourceSequenceCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("# clustering_method\t").Append(USearchTools.ExhaustiveClusteringMethod).Append('\n');
            sb.Append("identity\tgroups\ttarget_groups\tnoncontaminant_target_groups\tmixed_groups\tscore\ttarget_relative_score\n");
            foreach (IdentityScanRow r in rows)
            {
                sb.Append(r.Identity.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(r.Groups.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(r.TargetGroups.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(r.NonContaminantTargetGroups.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(r.MixedGroups.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(r.Score.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(r.TargetRelativeScore.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

            return rows;
        }

        // ================================================================= formatting

        private static IReadOnlyList<DeNovoSource> SourcesOf(FullXicOptions options)
        {
            return options.ResolveDeNovoSelections().Select(s => s.Source).ToList();
        }

        private static string CompositeFingerprint(IEnumerable<string> parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            string canonical = string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim()).OrderBy(p => p, StringComparer.Ordinal));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
        }

        private static DeNovoVariantSummary CloneVariantSummary(DeNovoVariantSummary row) =>
            JsonSerializer.Deserialize<DeNovoVariantSummary>(JsonSerializer.Serialize(row))
            ?? throw new InvalidDataException("A variant summary could not be cloned for persistence.");

        /// <summary>
        /// Resolves the common output root for every talk lane. The environment override is intentionally
        /// shared by the full-XIC variants and the fast sequence-only lanes so a corrected evidence run can
        /// be staged away from an existing Desktop export without changing any scientific input path.
        /// </summary>
        internal static string ResolveTalkOutputRoot(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
                throw new ArgumentException("A data directory is required.", nameof(dataDirectory));

            string? configured = Environment.GetEnvironmentVariable(OutputRootEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(dataDirectory, TalkFolderName)
                : Path.GetFullPath(configured.Trim());
        }

        private static string ConfidenceMetric(DeNovoSource source)
        {
            string label = source?.ConfidenceColumn ?? "confidence";
            return label.Replace(" (%)", string.Empty).Replace("%", string.Empty).Trim();
        }

        private static string ConfidenceCutoffNote(FullXicOptions options)
        {
            IReadOnlyList<DeNovoSourceSelection> selections = options.ResolveDeNovoSelections();
            string rules = string.Join("; ", selections.Select(s =>
                s.Source.DisplayName + " " + ConfidenceMetric(s.Source) + " >=" +
                s.MinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture)));
            return selections.Select(s => ConfidenceMetric(s.Source))
                .Distinct(StringComparer.Ordinal).Count() > 1
                ? rules + "; score scales are not equivalent" : rules;
        }

        private static void Row(StringBuilder sb, string name, string value) =>
            sb.Append("| ").Append(name).Append(" | ").Append(value).Append(" |\n");

        private static void OverlapRow(StringBuilder sb, string stage, int count, int denominator) =>
            sb.Append("| ").Append(stage).Append(" | ").Append(Int(count)).Append(" | ")
              .Append(Int(denominator)).Append(" |\n");

        private static void SummaryRow(StringBuilder sb, string name, List<NativeRunFraction> rows,
                                       Func<NativeRunFraction, string> cell)
        {
            sb.Append("| ").Append(name).Append(" | ");
            foreach (NativeRunFraction f in rows) sb.Append(cell(f)).Append(" | ");
            sb.Append('\n');
        }

        private static string Int(int v) => v.ToString("N0", CultureInfo.InvariantCulture);

        private static string Int(long v) => v.ToString("N0", CultureInfo.InvariantCulture);

        private static string Num(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

        private static string Exp(double v) => v.ToString("E4", CultureInfo.InvariantCulture);

        private static string Pct(double v, int decimals) =>
            (v * 100.0).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture),
                                 CultureInfo.InvariantCulture) + "%";

        private static string SafeFileName(string name)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            return new string((name ?? string.Empty).Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
