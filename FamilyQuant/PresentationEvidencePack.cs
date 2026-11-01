using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant
{
    /// <summary>The files written by <see cref="PresentationEvidencePack.Save"/>.</summary>
    public sealed class PresentationEvidencePackResult
    {
        public string RootFolder { get; set; } = string.Empty;
        public List<string> FigurePaths { get; } = new List<string>();
        public List<string> DataPaths { get; } = new List<string>();
        public IReadOnlyList<string> AllPaths => FigurePaths.Concat(DataPaths).ToList();
    }

    /// <summary>
    /// A reproducible, denominator-aware export of every numerical result used by the FamilyQuant talk.
    /// The PNGs and the tables are built from the same in-memory project objects in one call, so the deck
    /// never needs a number calculated by PowerPoint or copied from a console transcript.
    /// </summary>
    public static class PresentationEvidencePack
    {
        public const string PlotDataFile = "presentation_plot_data.json";
        public const string ScoreCsvFile = "de_novo_score_distribution.csv";
        public const string DeNovoInputsCsvFile = "de_novo_inputs.csv";
        public const string SourceScoreCsvFile = "de_novo_source_score_distributions.csv";
        public const string SourceScoreJsonFile = "de_novo_source_score_distributions.json";
        public const string IdentificationCsvFile = "de_novo_identification_consolidation.csv";
        public const string PeptideCsvFile = "peptide_representatives.csv";
        public const string CrosscheckCsvFile = "de_novo_alternative_triage.csv";
        public const string ConcordanceCsvFile = "database_search_concordance.csv";
        public const string SignalByRunCsvFile = "signal_by_run.csv";
        public const string SignalUpliftCsvFile = "signal_uplift.csv";
        public const string IdentitySweepCsvFile = "identity_sweep.csv";
        public const string FamilyCsvFile = "family_attribution.csv";
        public const string FamilyCompositionCsvFile = "family_composition.csv";
        public const string FamilySourceSequenceCsvFile = "family_source_sequence_counts.csv";
        public const string ReferenceEnvelopeDiagnosticsCsvFile =
            "reference_envelope_model_diagnostics.csv";
        public const string SpectrumComparisonCsvFile = SpectrumComparisonTable.DefaultFileName;
        public const string DeNovoAgreementCsvFile = "de_novo_peaks_deepnovo_agreement.csv";
        public const string DeNovoAgreementMarkdownFile = "de_novo_peaks_deepnovo_agreement.md";
        public const string DeNovoSourceVennCsvFile = DeNovoSourceVennStudy.CsvFileName;
        public const string DeNovoAcceptedScoreScatterCsvFile =
            DeNovoAcceptedScoreScatterStudy.CsvFileName;
        public const string VariantComparisonCsvFile = DeNovoVariantComparison.CsvFileName;
        public const string VariantComparisonMarkdownFile = DeNovoVariantComparison.MarkdownFileName;
        public const string DeNovoTalkComparisonCsvFile = DeNovoTalkComparison.CsvFileName;
        public const string DeNovoTalkComparisonMarkdownFile = DeNovoTalkComparison.MarkdownFileName;
        public const string ReadmeFile = "README_presentation_evidence.txt";

        public static PresentationEvidencePackResult Save(
            AnalyticsData data, QuantManifest manifest, FamilyClusterFile curation,
            FamilyEnvelopeQuantResult families, string rootFolder,
            ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (curation == null) throw new ArgumentNullException(nameof(curation));
            if (families == null) throw new ArgumentNullException(nameof(families));
            if (string.IsNullOrWhiteSpace(rootFolder))
                throw new ArgumentException("An output folder is required.", nameof(rootFolder));

            // Build performs the complete-project validation before the first file is written.
            _ = PresentationFigures.Build(data, manifest, curation, families);
            DeNovoOverlapSummary overlap = manifest.DeNovoOverlap!;
            DeNovoScoreDistribution scores = manifest.DeNovoScoreDistribution!;
            DeNovoIdentificationSummary identification = manifest.DeNovoIdentification!;
            IdentificationConcordanceSummary concordance = manifest.IdentificationConcordance!;
            List<IdentityScanRow> sweep = manifest.PresentationIdentitySweep!;
            NativeRunFraction grouped = data.Grouped
                ?? throw new InvalidOperationException("The loaded project has no grouped quantitation.");
            ReferenceEnvelopeDiagnostics diagnostics =
                PresentationFigures.ResolveReferenceEnvelopeDiagnostics(data, manifest);
            List<DeNovoSourceScoreDistribution> sourceScores =
                ResolveSourceScoreDistributions(manifest, scores);
            DeNovoAcceptedScoreScatter acceptedScoreScatter =
                manifest.DeNovoAcceptedScoreScatter
                ?? DeNovoAcceptedScoreScatterStudy.Build(
                    manifest.SpectrumComparisonRows ?? new List<SpectrumComparisonRow>(),
                    sourceScores);
            if (acceptedScoreScatter.Total > 0)
                manifest.DeNovoAcceptedScoreScatter = acceptedScoreScatter;

            Directory.CreateDirectory(rootFolder);
            string figuresFolder = Path.Combine(rootFolder, "figures");
            var result = new PresentationEvidencePackResult { RootFolder = rootFolder };
            result.FigurePaths.AddRange(PresentationFigures.Save(
                data, manifest, curation, families, figuresFolder, variant));

            void Write(string fileName, string content)
            {
                string path = Path.Combine(rootFolder, fileName);
                File.WriteAllText(path, content, new UTF8Encoding(false));
                result.DataPaths.Add(path);
            }

            // These two presentation analyses are optional because a one-source project has no caller
            // comparison and an ordinary project has no source-variant collection. When present they are
            // persisted in the .fqproj and regenerated here from the loaded objects, never reread from a
            // mutable CSV directory and never calculated in PowerPoint.
            if (manifest.DeNovoCallerAgreement != null)
            {
                manifest.DeNovoCallerAgreement.Validate();
                result.FigurePaths.Add(PresentationFigures.SaveAgreement(
                    manifest.DeNovoCallerAgreement, figuresFolder, variant));
                Write(DeNovoAgreementCsvFile,
                    DeNovoAgreementStudy.BuildTableCsv(manifest.DeNovoCallerAgreement));
                Write(DeNovoAgreementMarkdownFile,
                    DeNovoAgreementStudy.BuildSummaryMarkdown(manifest.DeNovoCallerAgreement));
            }
            if (manifest.DeNovoSourceVenn != null)
            {
                manifest.DeNovoSourceVenn.Validate();
                result.FigurePaths.Add(PresentationFigures.SaveDeNovoSourceVenn(
                    manifest.DeNovoSourceVenn, figuresFolder, variant));
                Write(DeNovoSourceVennCsvFile,
                    DeNovoSourceVennStudy.ToCsv(manifest.DeNovoSourceVenn));
            }
            if (acceptedScoreScatter.Total > 0)
            {
                acceptedScoreScatter.Validate();
                result.FigurePaths.Add(PresentationFigures.SaveDeNovoAcceptedScoreScatter(
                    acceptedScoreScatter, figuresFolder, variant));
                Write(DeNovoAcceptedScoreScatterCsvFile,
                    DeNovoAcceptedScoreScatterStudy.ToCsv(acceptedScoreScatter));
            }
            if (manifest.PresentationDeNovoVariants != null
                && manifest.PresentationDeNovoVariants.Count > 0)
            {
                if (manifest.PresentationDeNovoVariants.Count ==
                    DeNovoVariantComparison.CurrentTalkVariantCount)
                    DeNovoVariantComparison.ValidateTalkVariantSet(
                        manifest.PresentationDeNovoVariants);
                else
                    DeNovoVariantComparison.ValidateComparableSet(manifest.PresentationDeNovoVariants,
                        expectedCount: manifest.PresentationDeNovoVariants.Count);
                result.FigurePaths.Add(PresentationFigures.SaveVariantComparison(
                    manifest.PresentationDeNovoVariants, figuresFolder, variant));
                Write(VariantComparisonCsvFile,
                    DeNovoVariantComparison.ToCsv(manifest.PresentationDeNovoVariants));
                Write(VariantComparisonMarkdownFile,
                    DeNovoVariantComparison.ToMarkdown(manifest.PresentationDeNovoVariants));

                if (manifest.PresentationDeNovoVariants.Count ==
                    DeNovoVariantComparison.CurrentTalkVariantCount)
                {
                    List<DeNovoTalkComparisonRow> simple = DeNovoTalkComparison.Build(
                        manifest.PresentationDeNovoVariants);
                    result.FigurePaths.Add(PresentationFigures.SaveDeNovoTalkComparison(
                        simple, figuresFolder, variant));
                    Write(DeNovoTalkComparisonCsvFile, DeNovoTalkComparison.ToCsv(simple));
                    Write(DeNovoTalkComparisonMarkdownFile, DeNovoTalkComparison.ToMarkdown(simple));
                }
            }

            Write(PlotDataFile, BuildPlotDataJson(data, grouped, families, overlap, scores,
                identification, concordance, sweep, curation, manifest, sourceScores, diagnostics));
            Write(ScoreCsvFile, BuildScoreCsv(scores));
            Write(DeNovoInputsCsvFile, BuildDeNovoInputsCsv(manifest.DeNovoInputs));
            Write(SourceScoreCsvFile, BuildSourceScoreCsv(sourceScores));
            Write(SourceScoreJsonFile, JsonSerializer.Serialize(sourceScores, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
            Write(IdentificationCsvFile, BuildIdentificationCsv(manifest, identification, data));
            Write(PeptideCsvFile, BuildPeptideCsv(data));
            Write(CrosscheckCsvFile, DeNovoOverlapStudy.BuildSummaryCsv(overlap));
            Write(ConcordanceCsvFile, IdentificationConcordanceStudy.BuildCsv(concordance));
            Write(SignalByRunCsvFile, BuildSignalByRunCsv(data));
            Write(SignalUpliftCsvFile, BuildSignalUpliftCsv(grouped));
            Write(IdentitySweepCsvFile, BuildIdentitySweepCsv(sweep));
            Write(FamilyCsvFile, BuildFamilyCsv(families));
            // The composition basis beside it: each peptide counted once, shared peptides in their own
            // row, so the fractions add up to 1 (the pie chart the users draw from this pack).
            Write(FamilyCompositionCsvFile, CsvReport.BuildFamilyComposition(families,
                SharedPeptidePolicy.FamilyUnique, families.MeasuredTotalEnvelopeReferenceCalibrated,
                "fraction of the quantified signal (every quantified peptide counted once); signal = identified " +
                "observed envelope plus the de novo identified-reference, mass-calibrated estimate"));
            Write(FamilySourceSequenceCsvFile,
                BuildFamilySourceSequenceCsv(families.SourceSequenceSummary));
            Write(ReferenceEnvelopeDiagnosticsCsvFile,
                ReferenceEnvelopeEstimator.ToCsv(diagnostics));
            // The spectrum table is by far the widest/largest CSV in the package. Stream it directly;
            // routing it through Write(string) would materialise the complete wide file in memory.
            string spectrumComparisonPath = SpectrumComparisonTable.SaveCsv(
                Path.Combine(rootFolder, SpectrumComparisonCsvFile),
                manifest.SpectrumComparisonRows ?? new List<SpectrumComparisonRow>());
            result.DataPaths.Add(spectrumComparisonPath);
            Write(ReadmeFile, BuildReadme(data, grouped, families, overlap, scores, identification,
                concordance, manifest, sourceScores, diagnostics));
            return result;
        }

        private static string BuildPlotDataJson(
            AnalyticsData data, NativeRunFraction grouped, FamilyEnvelopeQuantResult families,
            DeNovoOverlapSummary overlap, DeNovoScoreDistribution scores,
            DeNovoIdentificationSummary identification,
            IdentificationConcordanceSummary concordance,
            IReadOnlyList<IdentityScanRow> sweep, FamilyClusterFile curation,
            QuantManifest manifest, IReadOnlyList<DeNovoSourceScoreDistribution> sourceScores,
            ReferenceEnvelopeDiagnostics diagnostics)
        {
            ReferenceComponentSummary reference = ReferenceComponentSummary.FromCuration(curation);
            var runRows = data.Fractions.Where(f => !f.IsGrouped).Select((f, i) => new
            {
                run = f.Run,
                displayRun = "Run " + (i + 1).ToString(CultureInfo.InvariantCulture),
                creditedSearchEngineRepresentatives = f.IdentifiedPeptides,
                creditedDeNovoRepresentatives = f.DeNovoPeptides,
                deNovoRepresentativesBeforeCrossSideSuppression =
                    Count(data, f.Run, NativeQuantFractions.SideDeNovo),
                crossSideSuppressedDeNovoRepresentatives = f.DeNovoSuppressedCrossSideOverlaps,
                tic = f.Tic,
                identifiedFractionEnvelope = f.IdentifiedFractionEnvelope,
                deNovoModelEstimateFractionOfTic = f.DeNovoFractionReferenceCalibrated,
                combinedModelEstimateFractionOfTic = f.RecoveredFractionReferenceCalibrated,
            }).ToList();

            var payload = new
            {
                schemaVersion = 3,
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                dataset = string.Join("; ", data.Fractions.Where(f => !f.IsGrouped).Select(f => f.Run)),
                plotOrder = PresentationFigures.FileNames
                    .Concat(manifest.DeNovoCallerAgreement == null
                        ? Array.Empty<string>()
                        : new[] { PresentationFigures.DeNovoAgreementFile })
                    .Concat(manifest.DeNovoSourceVenn == null
                        ? Array.Empty<string>()
                        : new[] { PresentationFigures.DeNovoSourceVennFile })
                    .Concat(manifest.DeNovoAcceptedScoreScatter == null
                        || manifest.DeNovoAcceptedScoreScatter.Total == 0
                        ? Array.Empty<string>()
                        : new[] { PresentationFigures.DeNovoAcceptedScoreScatterFile })
                    .Concat(manifest.PresentationDeNovoVariants != null
                        && manifest.PresentationDeNovoVariants.Count > 0
                            ? new[] { PresentationFigures.DeNovoVariantComparisonFile }
                            : Array.Empty<string>())
                    .Concat(manifest.PresentationDeNovoVariants != null
                        && manifest.PresentationDeNovoVariants.Count ==
                            DeNovoVariantComparison.CurrentTalkVariantCount
                            ? new[] { PresentationFigures.DeNovoTalkComparisonFile }
                            : Array.Empty<string>())
                    .ToList(),
                reproducibility = new
                {
                    peptideRepresentativeGrain = "one deterministic largest-M+0 representative per run × evidence side × canonical PTM-aware peptidoform × charge",
                    identificationConsolidationGrain = "accepted source-filtered and union-resolved spectrum calls collapsed by PTM-cleaned residue sequence; charge and technical run ignored for the pooled distinct-sequence count; I/L remains distinct",
                    deNovoFilters = sourceScores.Select(s => new
                    {
                        s.SourceId,
                        s.SourceDisplayName,
                        s.ConfidenceField,
                        s.MinimumConfidence,
                        s.MinLength,
                        s.StretchLength,
                        s.StretchFloor,
                    }).ToList(),
                    scanCrosscheck = new
                    {
                        literalJoin = "normalized file name + scan number",
                        scanComparisonPsm = "highest-XCorr PSM in the same reported-charge stratum",
                        comparablePrecursorSet = "same normalized file, MS2 scan, reported charge, and precursor m/z within tolerance; highest XCorr selected",
                        precursorMzTolerancePpm = overlap.PrecursorTolerancePpm,
                        equalChargeRequired = true,
                        ptmNotationIgnored = true,
                        ileLeuFoldReportedSeparately = true,
                        sourceScope = overlap.SourceScopeLabel,
                        sourceFilter = overlap.SourceFilterDescription,
                        interpretation = overlap.IsStrictDeNovoOnlyScope
                            ? "de-novo-only alternative-hypothesis triage; sequence differences are not called database-search conflicts"
                            : "source-aware, union-resolved alternative-hypothesis triage; sequence differences are not database-search conflicts or caller-accuracy estimates",
                    },
                    familyMappingMinimumIdentity = families.IdentityThreshold,
                    familyBarsOverlapByDesign = true,
                },
                deNovoInputs = manifest.DeNovoInputs,
                deNovoCallerAgreement = manifest.DeNovoCallerAgreement == null ? null : new
                {
                    population = manifest.DeNovoCallerAgreement.PopulationDefinition,
                    observedPeaksAlcFloor = manifest.DeNovoCallerAgreement.PeaksObservedMinimumConfidence,
                    observedDeepNovoScoreFloor = manifest.DeNovoCallerAgreement.DeepNovoObservedMinimumConfidence,
                    primaryPairs = manifest.DeNovoCallerAgreement.Total,
                    exactPairs = manifest.DeNovoCallerAgreement.Identical,
                    exactFraction = manifest.DeNovoCallerAgreement.IdenticalFraction,
                },
                deNovoSourceVenn = manifest.DeNovoSourceVenn,
                deNovoAcceptedScoreScatter = manifest.DeNovoAcceptedScoreScatter,
                deNovoVariantComparison = manifest.PresentationDeNovoVariants,
                deNovoSourceScoreDistributions = sourceScores,
                deNovoScoreDistribution = scores,
                deNovoIdentification = new
                {
                    summary = identification,
                    stages = new
                    {
                        rawImportedSpectrumRows = RawImported(manifest, scores),
                        acceptedSpectrumCallsAfterSourceFiltersAndUnion = identification.FilteredSpectrumCalls,
                        pooledPtmCleanedSequencesIleLeuDistinct = identification.FilteredDistinctSequencesIgnoringPtm,
                        creditedDeNovoRepresentativesAfterXicDedupAndSearchSuppression = grouped.DeNovoPeptides,
                    },
                    quantifiedRunSequenceChargeRepresentativesBeforeCrossSideSuppression = data.DeNovoCount,
                    creditedRunPeptidoformChargeMassRepresentatives = grouped.DeNovoPeptides,
                    quantifiedDistinctSequencesIgnoringPtm = data.Peptides
                        .Where(p => p.Side == NativeQuantFractions.SideDeNovo)
                        .Select(p => DeNovoReconciler.NormalizeSequenceIgnoringPtm(p.Sequence ?? string.Empty, false))
                        .Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Count(),
                },
                peptideEvidence = new
                {
                    runs = runRows,
                    pooledCreditedSearchEngineRepresentatives = grouped.IdentifiedPeptides,
                    pooledCreditedDeNovoRepresentatives = grouped.DeNovoPeptides,
                    pooledCreditedRepresentatives = grouped.IdentifiedPeptides + grouped.DeNovoPeptides,
                    deNovoShareOfCreditedRepresentatives =
                        grouped.IdentifiedPeptides + grouped.DeNovoPeptides == 0 ? 0.0
                        : (double)grouped.DeNovoPeptides /
                            (grouped.IdentifiedPeptides + grouped.DeNovoPeptides),
                    diagnosticDeNovoRepresentativesBeforeCrossSideSuppression = data.DeNovoCount,
                    diagnosticRepresentativesBeforeCrossSideSuppression = data.Peptides.Count,
                    overlap,
                },
                databaseSearchConcordance = concordance,
                referenceEnvelopeModelDiagnostics = diagnostics,
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
                    deNovoModelEstimateEnvelope = families.MeasuredDeNovoEnvelopeReferenceCalibrated,
                    totalModelEstimateEnvelope = families.MeasuredTotalEnvelopeReferenceCalibrated,
                    unattributedIdentifiedEnvelope = families.UnattributedIdentifiedEnvelope,
                    unattributedDeNovoModelEstimateEnvelope =
                        families.UnattributedDeNovoEnvelopeReferenceCalibrated,
                    families = families.Families.OrderByDescending(f => f.TotalEnvelopeReferenceCalibrated)
                        .ThenBy(f => f.FamilyUserId).Select(f => new
                        {
                            familyUserId = f.FamilyUserId,
                            familyLabel = FamilyFigureLabel.Format(f.FamilyUserId, f.Description),
                            description = f.Description,
                            identifiedPeptides = f.IdentifiedPeptides,
                            deNovoPeptides = f.DeNovoPeptides,
                            identifiedEnvelope = f.IdentifiedEnvelope,
                            deNovoModelEstimateEnvelope = f.DeNovoEnvelopePrimary,
                            totalModelEstimateEnvelope = f.TotalEnvelopeReferenceCalibrated,
                            deNovoShare = f.TotalEnvelopeReferenceCalibrated > 0
                                ? f.DeNovoEnvelopePrimary / f.TotalEnvelopeReferenceCalibrated : 0.0,
                        }).ToList(),
                },
                familySourceSequenceCounts = families.SourceSequenceSummary,
            };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }

        private static string BuildScoreCsv(DeNovoScoreDistribution s)
        {
            var b = Header("alc_bin", "lower_inclusive", "upper_bound", "upper_inclusive", "raw_spectrum_rows", "rows_passing_all_three_filters");
            foreach (DeNovoScoreBin bin in s.Bins)
                Row(b, bin.Label, Num(bin.LowerInclusive), Num(bin.UpperBound), bin.UpperInclusive, bin.RawRows, bin.PassingAllCriteria);
            return b.ToString();
        }

        private static List<DeNovoSourceScoreDistribution> ResolveSourceScoreDistributions(
            QuantManifest manifest, DeNovoScoreDistribution legacy)
        {
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
                    "The stored source-confidence distributions are incomplete for the configured " +
                    "de novo inputs. Recompute the project before exporting evidence.");
            if (manifest.DeNovoSourceScoreDistributions != null
                && manifest.DeNovoSourceScoreDistributions.Count > 0)
            {
                foreach (DeNovoSourceScoreDistribution distribution in manifest.DeNovoSourceScoreDistributions)
                    distribution.Validate();
                return manifest.DeNovoSourceScoreDistributions.ToList();
            }

            legacy.Validate();
            return new List<DeNovoSourceScoreDistribution>
            {
                new DeNovoSourceScoreDistribution
                {
                    SourceId = "legacy-primary",
                    SourceDisplayName = legacy.ScoreName,
                    ConfidenceField = legacy.ScoreName,
                    MinimumConfidence = legacy.MinAverageLocalConfidence,
                    BinWidth = legacy.BinWidth,
                    RawRows = legacy.RawRows,
                    OutOfRangeScoreRows = legacy.OutOfRangeScoreRows,
                    RowsPassingLength = legacy.RowsPassingLength,
                    RowsAtOrAboveConfidence = legacy.RowsAtOrAboveAlc,
                    RowsPassingLocalStretch = legacy.RowsPassingLocalStretch,
                    RowsPassingLengthAndConfidence = legacy.RowsPassingLengthAndAlc,
                    RowsPassingAllCriteria = legacy.RowsPassingAllCriteria,
                    MinLength = legacy.MinLength,
                    StretchLength = legacy.StretchLength,
                    StretchFloor = legacy.StretchFloor,
                    Bins = legacy.Bins,
                },
            };
        }

        private static string BuildDeNovoInputsCsv(IEnumerable<DeNovoInputManifest>? inputs)
        {
            var b = Header("source_id", "source_name", "confidence_field", "minimum_confidence",
                "source_priority", "file_path", "sha256", "raw_rows", "rows_passing_source_filter");
            foreach (DeNovoInputManifest input in (inputs ?? Enumerable.Empty<DeNovoInputManifest>())
                .OrderBy(i => i.SourcePriority).ThenBy(i => i.FilePath, StringComparer.OrdinalIgnoreCase))
                Row(b, input.SourceId, input.SourceDisplayName, input.ConfidenceField,
                    Num(input.MinimumConfidence), input.SourcePriority, input.FilePath, input.Sha256,
                    input.RawRows, input.RowsPassingSourceFilter);
            return b.ToString();
        }

        private static string BuildSourceScoreCsv(
            IEnumerable<DeNovoSourceScoreDistribution> distributions)
        {
            var b = Header("source_id", "source_name", "confidence_field", "minimum_confidence",
                "bin_label", "lower_inclusive", "upper_bound", "upper_inclusive",
                "raw_rows_in_bin", "retained_rows_in_bin", "source_raw_rows",
                "source_rows_passing_all_filters", "minimum_length", "local_stretch_length",
                "minimum_local_confidence");
            foreach (DeNovoSourceScoreDistribution distribution in distributions
                .OrderBy(d => d.SourceDisplayName, StringComparer.Ordinal))
            {
                distribution.Validate();
                foreach (DeNovoScoreBin bin in distribution.Bins)
                    Row(b, distribution.SourceId, distribution.SourceDisplayName,
                        distribution.ConfidenceField, Num(distribution.MinimumConfidence), bin.Label,
                        Num(bin.LowerInclusive), Num(bin.UpperBound), bin.UpperInclusive, bin.RawRows,
                        bin.PassingAllCriteria, distribution.RawRows,
                        distribution.RowsPassingAllCriteria, distribution.MinLength,
                        distribution.StretchLength, distribution.StretchFloor);
            }
            return b.ToString();
        }

        private static string BuildIdentificationCsv(QuantManifest manifest,
            DeNovoIdentificationSummary s, AnalyticsData data)
        {
            s.Validate();
            var b = Header("scope", "grain", "count", "definition");
            Row(b, "imported", "spectrum_row", RawImported(manifest, manifest.DeNovoScoreDistribution),
                "sum of raw rows in the recorded de novo input files; before source-specific filters or union resolution");
            Row(b, "accepted", "spectrum_call", s.FilteredSpectrumCalls,
                "source-filtered and union-resolved spectrum calls; every source uses its recorded confidence field and cutoff");
            Row(b, "accepted", "run_sequence_charge", s.FilteredRunSequenceChargeGroups,
                "distinct normalized run + PTM-cleaned residue sequence + reported charge; I and L distinct; no XIC requirement");
            Row(b, "accepted", "run_sequence", s.FilteredRunSequenceGroups,
                "distinct normalized run + PTM-cleaned residue sequence; charge ignored; I and L distinct; no XIC requirement");
            Row(b, "accepted", "pooled_distinct_sequence", s.FilteredDistinctSequencesIgnoringPtm,
                "distinct PTM-cleaned residue sequence pooled across runs and charge states; I and L distinct; no XIC requirement");
            Row(b, "accepted", "pooled_distinct_sequence_il_folded", s.FilteredDistinctSequencesAfterIleLeuFold,
                "same pooled sequence count after folding I to L; sensitivity result; no XIC requirement");
            Row(b, "carried_forward", "spectrum_call", s.CarriedForwardSpectrumCalls,
                "accepted calls classified as de-novo-only or co-isolated extra after PatternLab reconciliation");
            Row(b, "carried_forward", "run_sequence_charge", s.CarriedForwardRunSequenceChargeGroups,
                "distinct normalized run + PTM-cleaned sequence + charge among carried-forward calls; no XIC requirement");
            Row(b, "carried_forward", "run_sequence", s.CarriedForwardRunSequenceGroups,
                "distinct normalized run + PTM-cleaned sequence among carried-forward calls; charge ignored; no XIC requirement");
            Row(b, "carried_forward", "pooled_distinct_sequence", s.CarriedForwardDistinctSequencesIgnoringPtm,
                "distinct PTM-cleaned sequences among carried-forward calls, pooled across runs and charge states; I and L distinct; no XIC requirement");
            Row(b, "carried_forward", "pooled_distinct_sequence_il_folded", s.CarriedForwardDistinctSequencesAfterIleLeuFold,
                "same carried-forward pooled count after folding I to L; sensitivity result; no XIC requirement");
            int credited = data.Grouped?.DeNovoPeptides
                ?? data.Fractions.Where(f => !f.IsGrouped).Sum(f => f.DeNovoPeptides);
            Row(b, "credited", "run_peptidoform_charge_mass_cluster", credited,
                "additional de novo representatives after search reconciliation, XIC admission, canonical PTM-aware within-side deduplication and cross-side search-overlap suppression");
            Row(b, "diagnostic_before_cross_side_suppression", "run_peptidoform_charge_mass_cluster",
                data.DeNovoCount,
                "post-XIC within-side representatives before cross-side search-overlap suppression; not the credited biological endpoint");
            int quantifiedDistinct = data.Peptides.Where(p => p.Side == NativeQuantFractions.SideDeNovo)
                .Select(p => DeNovoReconciler.NormalizeSequenceIgnoringPtm(p.Sequence ?? string.Empty, false))
                .Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Count();
            Row(b, "quantified", "pooled_distinct_sequence", quantifiedDistinct,
                "PTM-cleaned de novo sequences among quantified representatives, pooled across runs and charge states; I and L distinct");
            foreach (DeNovoIdentificationRunSummary run in s.Runs)
            {
                Row(b, run.Run + ":accepted", "spectrum_call", run.FilteredSpectrumCalls,
                    "accepted spectrum calls in this technical run");
                Row(b, run.Run + ":accepted", "run_sequence_charge", run.FilteredSequenceChargeGroups,
                    "accepted distinct PTM-cleaned sequence + charge groups in this technical run; no XIC requirement");
                Row(b, run.Run + ":accepted", "distinct_sequence", run.FilteredDistinctSequencesIgnoringPtm,
                    "accepted distinct PTM-cleaned sequences in this technical run; charge ignored; no XIC requirement");
            }
            return b.ToString();
        }

        private static int RawImported(QuantManifest manifest, DeNovoScoreDistribution? legacy)
        {
            if (manifest.DeNovoInputs != null && manifest.DeNovoInputs.Count > 0)
                return manifest.DeNovoInputs.Sum(i => i.RawRows);
            return legacy?.RawRows ?? 0;
        }

        private static string BuildPeptideCsv(AnalyticsData data)
        {
            var b = Header("run", "display_run", "credited_search_engine_representatives",
                "credited_de_novo_representatives", "credited_total_representatives",
                "credited_de_novo_share", "de_novo_representatives_before_cross_side_suppression",
                "cross_side_suppressed_de_novo_representatives");
            int i = 0;
            foreach (NativeRunFraction f in data.Fractions.Where(f => !f.IsGrouped))
            {
                int preSuppression = Count(data, f.Run, NativeQuantFractions.SideDeNovo);
                Row(b, f.Run, "Run " + (++i).ToString(CultureInfo.InvariantCulture),
                    f.IdentifiedPeptides, f.DeNovoPeptides, f.IdentifiedPeptides + f.DeNovoPeptides,
                    Ratio(f.DeNovoPeptides, f.IdentifiedPeptides + f.DeNovoPeptides), preSuppression,
                    f.DeNovoSuppressedCrossSideOverlaps);
            }
            NativeRunFraction grouped = data.Grouped ?? NativeQuantFractions.Grouped(data.Fractions);
            Row(b, "pooled", "Pooled", grouped.IdentifiedPeptides, grouped.DeNovoPeptides,
                grouped.IdentifiedPeptides + grouped.DeNovoPeptides,
                Ratio(grouped.DeNovoPeptides, grouped.IdentifiedPeptides + grouped.DeNovoPeptides),
                data.DeNovoCount, grouped.DeNovoSuppressedCrossSideOverlaps);
            return b.ToString();
        }

        private static string BuildSignalByRunCsv(AnalyticsData data)
        {
            var b = Header("run", "tic", "search_engine_assigned_area",
                "de_novo_reference_model_estimate_area", "search_engine_share_of_tic",
                "de_novo_reference_model_estimate_share_of_tic",
                "combined_model_estimate_share_of_tic");
            foreach (NativeRunFraction f in data.Fractions.Where(f => !f.IsGrouped))
                Row(b, f.Run, Num(f.Tic), Num(f.IdentifiedEnvelope), Num(f.DeNovoEnvelopeReferenceCalibrated),
                    Num(f.IdentifiedFractionEnvelope), Num(f.DeNovoFractionReferenceCalibrated),
                    Num(f.RecoveredFractionReferenceCalibrated));
            return b.ToString();
        }

        private static string BuildSignalUpliftCsv(NativeRunFraction g)
        {
            var b = Header("scope", "search_engine_share_of_tic",
                "de_novo_reference_model_estimate_share_of_tic",
                "combined_model_estimate_share_of_tic", "relative_model_uplift_over_search");
            Row(b, "pooled", Num(g.IdentifiedFractionEnvelope), Num(g.DeNovoFractionReferenceCalibrated),
                Num(g.RecoveredFractionReferenceCalibrated),
                Num(g.IdentifiedFractionEnvelope > 0 ? g.DeNovoFractionReferenceCalibrated / g.IdentifiedFractionEnvelope : 0));
            return b.ToString();
        }

        private static string BuildIdentitySweepCsv(IEnumerable<IdentityScanRow> rows)
        {
            var b = Header("minimum_identity_percent", "all_connected_components", "groups_with_real_sequence", "groups_with_curated_candidate", "mixed_target_decoy_groups", "mixing_score", "target_relative_score");
            foreach (IdentityScanRow r in rows.OrderByDescending(r => r.Identity))
                Row(b, r.Identity, r.Groups, r.TargetGroups, r.ReportedTargetGroups, r.MixedGroups,
                    Num(r.Score), Num(r.TargetRelativeScore));
            return b.ToString();
        }

        private static string BuildFamilyCsv(FamilyEnvelopeQuantResult families)
        {
            var b = Header("rank", "family_user_id", "family_label", "description",
                "identified_peptides", "de_novo_peptides", "identified_envelope",
                "de_novo_model_estimate_envelope", "total_model_estimate_envelope",
                "de_novo_share_of_family_signal", "mapping_minimum_identity");
            int rank = 0;
            foreach (FamilyEnvelopeQuantRow f in families.Families
                .OrderByDescending(f => f.TotalEnvelopeReferenceCalibrated).ThenBy(f => f.FamilyUserId))
                Row(b, ++rank, f.FamilyUserId, FamilyFigureLabel.Format(f.FamilyUserId, f.Description),
                    f.Description, f.IdentifiedPeptides, f.DeNovoPeptides,
                    Num(f.IdentifiedEnvelope), Num(f.DeNovoEnvelopePrimary), Num(f.TotalEnvelopeReferenceCalibrated),
                    Num(f.TotalEnvelopeReferenceCalibrated > 0 ? f.DeNovoEnvelopePrimary / f.TotalEnvelopeReferenceCalibrated : 0),
                    Num(families.IdentityThreshold));
            return b.ToString();
        }

        private static string BuildFamilySourceSequenceCsv(FamilySourceSequenceSummary summary)
        {
            summary ??= new FamilySourceSequenceSummary();
            var b = Header("rank", "family_user_id", "family_label", "description",
                "search_distinct_sequences",
                "peaks_denovo_only_exclusive_distinct_sequences",
                "deepnovo_exclusive_distinct_sequences",
                "shared_peaks_deepnovo_distinct_sequences",
                "peaks_denovo_only_total_distinct_sequences",
                "deepnovo_total_distinct_sequences", "denovo_union_distinct_sequences",
                "mapping_minimum_identity", "counting_grain", "accepted_tier");
            int rank = 0;
            foreach (FamilySourceSequenceRow row in summary.Families)
                Row(b, ++rank, row.FamilyUserId,
                    FamilyFigureLabel.Format(row.FamilyUserId, row.Description), row.Description,
                    row.SearchDistinctSequences,
                    row.PeaksDeNovoOnlyExclusiveDistinctSequences,
                    row.DeepNovoExclusiveDistinctSequences,
                    row.SharedPeaksDeepNovoDistinctSequences,
                    row.PeaksDeNovoOnlyTotalDistinctSequences,
                    row.DeepNovoTotalDistinctSequences,
                    row.DeNovoUnionDistinctSequences,
                    Num(summary.MappingMinimumIdentity), FamilySourceSequenceSummary.CountingGrain,
                    FamilySourceSequenceSummary.AcceptedTier);
            return b.ToString();
        }

        private static string BuildReadme(AnalyticsData data, NativeRunFraction grouped,
            FamilyEnvelopeQuantResult families, DeNovoOverlapSummary o, DeNovoScoreDistribution s,
            DeNovoIdentificationSummary identification, IdentificationConcordanceSummary c,
            QuantManifest manifest, IReadOnlyList<DeNovoSourceScoreDistribution> sourceScores,
            ReferenceEnvelopeDiagnostics diagnostics)
        {
            var b = new StringBuilder();
            b.AppendLine("FAMILYQUANT PRESENTATION EVIDENCE PACK");
            b.AppendLine("Generated from the currently loaded complete scientific project.");
            b.AppendLine();
            b.AppendLine("FIGURES");
            b.AppendLine("figures/de_novo_score_distribution.png: one panel per source, using that source's recorded confidence field and cutoff; ALC and SCORE are not pooled.");
            b.AppendLine("figures/de_novo_consolidation.png: raw imported spectrum rows, accepted source-filtered/union-resolved calls, pooled PTM-cleaned sequences (I/L distinct), and credited post-XIC representatives, with every counting grain stated.");
            b.AppendLine("figures/peptide_representatives.png: separates identification-only consolidation from credited post-XIC run × canonical peptidoform × charge × precursor-mass-cluster representatives after cross-side search-overlap suppression.");
            b.AppendLine("figures/database_search_concordance.png: PatternLab versus PEAKS database PSM sequence agreement in deterministic same-file/scan/equal-positive-charge/20-ppm precursor-qualified one-to-one pairs; raw shared scans are audit context only.");
            b.AppendLine("figures/de_novo_alternative_triage.png: selected de novo source calls (" + o.SourceScopeLabel + ") triaged through file, scan, charge and precursor-m/z gates.");
            b.AppendLine("figures/signal_by_run.png: identified envelope and the de novo reference-envelope model estimate as shares of each run TIC.");
            b.AppendLine("figures/signal_uplift.png: pooled assigned TIC before and after de novo; relative uplift is de novo addition / search-only signal.");
            b.AppendLine("figures/reference_envelope_model_diagnostics.png: fixed-bin reference support, fitted envelope/M+0 ratios, leave-one-representative-out prediction error and pooled-ratio sensitivity. The sensitivity span is not a confidence interval.");
            b.AppendLine("figures/identity_sweep.png: connected reference groups versus identity threshold, with biological inclusion definitions.");
            b.AppendLine("figures/family_contribution.png: leading overlapping family attributions, split by evidence source.");
            b.AppendLine("figures/family_signal_vs_denovo_share.png: every family with attributed signal: total attributed signal versus its de novo share; the ten strongest are labelled.");
            b.AppendLine("figures/family_source_sequence_counts.png: top families by exact PEAKS de-novo-only + DeepNovo sequence union, split into disjoint PEAKS-exclusive, shared and DeepNovo-exclusive sequence sets; search sequence counts are shown separately as context.");
            if (manifest.DeNovoCallerAgreement != null)
                b.AppendLine("figures/de_novo_peaks_deepnovo_agreement.png: same-file/scan/charge/20-ppm caller agreement across all imported export calls; observed source floors are printed and the >=80 quantified tier is explicitly not implied.");
            if (manifest.DeNovoSourceVenn != null)
                b.AppendLine("figures/de_novo_source_venn.png: exact PEAKS de-novo-only / DeepNovo sequence-set regions after all three source filters and, separately, among source claims attached to union rows carried past PatternLab reconciliation; regions come from this same computation, not subtraction of separate variants.");
            if (manifest.DeNovoAcceptedScoreScatter != null
                && manifest.DeNovoAcceptedScoreScatter.Total > 0)
                b.AppendLine("figures/de_novo_accepted_score_agreement.png: one translucent point per compatible precursor-hypothesis row where both PEAKS and DeepNovo passed all configured filters; blue circles are strict PTM-clean sequence matches and red diamonds are mismatches, with I/L kept distinct. ALC and SCORE remain separate, uncalibrated source-native scales.");
            if (manifest.PresentationDeNovoVariants != null && manifest.PresentationDeNovoVariants.Count > 0)
            {
                b.AppendLine("figures/de_novo_variant_comparison.png: the persisted, same-generation source-variant comparison with common dataset/settings fingerprints.");
                if (manifest.PresentationDeNovoVariants.Count ==
                    DeNovoVariantComparison.CurrentTalkVariantCount)
                    b.AppendLine("figures/de_novo_source_effect_comparison.png: the compact talk result: one observed search baseline beside PEAKS de-novo-only, DeepNovo and their exact union; added de novo signal is explicitly model-estimated.");
            }
            b.AppendLine();
            b.AppendLine("DE NOVO ACCEPTANCE FILTERS");
            foreach (DeNovoSourceScoreDistribution source in sourceScores)
                b.AppendLine("- " + source.SourceDisplayName + ": " + source.ConfidenceField + " >= " +
                    Num(source.MinimumConfidence) + "; peptide length >= " +
                    source.MinLength.ToString(CultureInfo.InvariantCulture) + "; at least " +
                    source.StretchLength.ToString(CultureInfo.InvariantCulture) +
                    " consecutive local-confidence residues >= " +
                    source.StretchFloor.ToString(CultureInfo.InvariantCulture) + "%.");
            b.AppendLine("The source confidence thresholds are operational rules, not calibrated de novo FDRs. ALC and SCORE are not interchangeable scales.");
            b.AppendLine();
            b.AppendLine("DE NOVO IDENTIFICATION CONSOLIDATION (NO XIC)");
            b.AppendLine(RawImported(manifest, s).ToString("N0", CultureInfo.InvariantCulture) +
                " imported spectrum rows become " +
                identification.FilteredSpectrumCalls.ToString("N0", CultureInfo.InvariantCulture) +
                " accepted source-filtered/union-resolved calls, which collapse to " +
                identification.FilteredDistinctSequencesIgnoringPtm.ToString("N0", CultureInfo.InvariantCulture) +
                " distinct PTM-cleaned residue sequences across all runs and charge states (I and L distinct).");
            b.AppendLine(identification.CarriedForwardSpectrumCalls.ToString("N0", CultureInfo.InvariantCulture) + " calls remain additional de novo hypotheses after PatternLab reconciliation; these collapse to " + identification.CarriedForwardDistinctSequencesIgnoringPtm.ToString("N0", CultureInfo.InvariantCulture) + " distinct sequences.");
            b.AppendLine("The I/L-folded sensitivity counts are " + identification.FilteredDistinctSequencesAfterIleLeuFold.ToString("N0", CultureInfo.InvariantCulture) + " accepted and " + identification.CarriedForwardDistinctSequencesAfterIleLeuFold.ToString("N0", CultureInfo.InvariantCulture) + " carried forward.");
            b.AppendLine("These are identification counts. They do not require an XIC, do not use peptide area, and do not keep technical-run or charge duplicates in the pooled distinct-sequence endpoint.");
            b.AppendLine(grouped.DeNovoPeptides.ToString("N0", CultureInfo.InvariantCulture) +
                " de novo representatives are finally credited after search reconciliation, XIC admission, within-side peptidoform/mass deduplication and cross-side search-overlap suppression. This final count restores run and charge context and therefore is not required to be below the pooled distinct-sequence count.");
            b.AppendLine();
            b.AppendLine("REFERENCE-ENVELOPE MODEL DIAGNOSTICS");
            b.AppendLine("Method = " + diagnostics.Method + ". Identified reference representatives = " +
                diagnostics.IdentifiedPeptides.ToString("N0", CultureInfo.InvariantCulture) +
                "; leave-one-representative-out weighted absolute prediction error = " +
                diagnostics.CrossValidatedWeightedAbsolutePercentError.ToString("P2", CultureInfo.InvariantCulture) +
                ". Fixed-bin and pooled-ratio de novo estimates are " +
                diagnostics.FixedBinDeNovoEstimate.ToString("E4", CultureInfo.InvariantCulture) + " and " +
                diagnostics.PooledRatioDeNovoEstimate.ToString("E4", CultureInfo.InvariantCulture) +
                ". This is model-form sensitivity, not a confidence interval and not empirical calibration.");
            b.AppendLine();
            b.AppendLine("SAME-SCAN CROSS-CHECK");
            b.AppendLine("Sources = " + o.SourceScopeLabel + ". " + o.SourceFilterDescription + ".");
            b.AppendLine("The literal join is normalized acquisition file name + scan number. Sequences are compared after PTM notation is removed, with I/L equivalence reported separately.");
            b.AppendLine("The " + o.SamePrecursorWithinTolerance.ToString("N0", CultureInfo.InvariantCulture) + "-row comparable-precursor set additionally requires the same reported charge and a search PSM within " + Num(o.PrecursorTolerancePpm) + " ppm of the de novo m/z; the highest-XCorr PSM is selected.");
            b.AppendLine(o.IsStrictDeNovoOnlyScope
                ? "PEAKS de-novo-only calls are alternative hypotheses by construction. Sequence differences in this set are not presented as database-search errors or isotopic-peak duplications."
                : "The selected source calls were union-resolved after source-native filtering. Sequence differences are alternative hypotheses, not database-search errors, isotopic-peak duplications, or caller-accuracy estimates.");
            b.AppendLine();
            b.AppendLine("DATABASE-SEARCH AGREEMENT");
            b.AppendLine("PatternLab and PEAKS database searching share " + c.SharedDatabaseSearchScanKeys.ToString("N0", CultureInfo.InvariantCulture) + " normalized file+scan keys (audit only). The primary set contains " + c.PrecursorQualifiedDatabaseSearchPairs.ToString("N0", CultureInfo.InvariantCulture) + " deterministic one-to-one PSM pairs with equal positive charge and precursor m/z within " + Num(c.PrecursorTolerancePpm) + " ppm; " + c.ExactPrecursorQualifiedDatabaseSearchPairs.ToString("N0", CultureInfo.InvariantCulture) + " (" + (100 * c.PrecursorQualifiedStrictAgreement).ToString("F2", CultureInfo.InvariantCulture) + "%) have an exact PTM-cleaned sequence match with I/L kept distinct.");
            b.AppendLine("This search-vs-search result is separate from the selected de novo-source overlap audit (" + o.SourceScopeLabel + ").");
            if (manifest.DeNovoCallerAgreement != null)
            {
                DeNovoAgreementResult a = manifest.DeNovoCallerAgreement;
                b.AppendLine();
                b.AppendLine("PEAKS-FULL VERSUS DEEPNOVO CALLER AGREEMENT");
                b.AppendLine("Population = " + a.PopulationDefinition + ". Observed export floors: PEAKS ALC " +
                    Num(a.PeaksObservedMinimumConfidence) + ", DeepNovo SCORE " +
                    Num(a.DeepNovoObservedMinimumConfidence) + ". This is not the downstream >=80 quantified tier.");
                b.AppendLine(a.Identical.ToString("N0", CultureInfo.InvariantCulture) + " of " +
                    a.Total.ToString("N0", CultureInfo.InvariantCulture) + " precursor-qualified pairs (" +
                    (100 * a.IdenticalFraction).ToString("F1", CultureInfo.InvariantCulture) +
                    "%) have an exact PTM-cleaned sequence match with I/L distinct; neither caller is ground truth.");
            }
            b.AppendLine();
            b.AppendLine("DENOMINATORS AND NON-ADDITIVITY");
            b.AppendLine("Score distributions are source-specific spectrum rows; cross-check counts use the recorded de novo source calls. Identification consolidation is PTM-cleaned sequence diversity without XIC; credited peptide representatives are post-XIC run × canonical peptidoform × charge × precursor-mass-cluster observations after cross-side search-overlap suppression; family results are attributed signal.");
            b.AppendLine("Family bars overlap by design because a peptide may map to more than one homologous family. Do not sum family bars to reconstruct total measured signal.");
            b.AppendLine(SpectrumComparisonCsvFile + " is the master table for scan-, source-, precursor- and quantitation-level plots. FamilyUserId is populated only for exactly one family; FamilyUserIds is the semicolon-delimited machine-readable many-family list; FamilyAssignments carries every full label; legacy FamilyAssignment mirrors that complete joined assignment rather than choosing the first family. FamilyMappingMinimumIdentity records the homology threshold used; FamilyMappingStatus distinguishes mapping-not-supplied, unusable-curation, mapped and run-but-unattributed rows, so an empty family list is interpreted only with that status.");
            b.AppendLine("family_source_sequence_counts.csv reports pooled distinct PTM-cleaned sequences by family for search, PEAKS de-novo-only-exclusive, DeepNovo-exclusive and shared caller sets. It uses source-filtered claims attached to quantified de novo precursors; I/L is distinct in the set comparison, alternate source claims do not create another XIC, and family rows may overlap.");
            b.AppendLine("Pooled search assigned TIC = " + Num(grouped.IdentifiedFractionEnvelope) + "; de novo addition = " + Num(grouped.DeNovoFractionReferenceCalibrated) + "; combined = " + Num(grouped.RecoveredFractionReferenceCalibrated) + ".");
            b.AppendLine("Family mapping minimum identity = " + Num(families.IdentityThreshold) + ". Pooled credited representatives = " + (grouped.IdentifiedPeptides + grouped.DeNovoPeptides).ToString(CultureInfo.InvariantCulture) + "; pre-suppression analytics-table representatives = " + data.Peptides.Count.ToString(CultureInfo.InvariantCulture) + ".");
            b.AppendLine();
            b.AppendLine("The CSV files contain the exact numerical series used by each plot. Identity, reference-model and family-sensitivity plots use their dedicated tables; no single CSV is claimed to contain every analysis. presentation_plot_data.json and the saved project carry the complete machine-readable payload used by the evidence-pack exporter.");
            return b.ToString();
        }

        private static int Count(AnalyticsData data, string run, string side) => data.Peptides.Count(p =>
            string.Equals(p.Run, run, StringComparison.OrdinalIgnoreCase) && p.Side == side);
        private static double Ratio(int numerator, int denominator) => denominator > 0 ? (double)numerator / denominator : 0;
        private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static StringBuilder Header(params object[] values) { var b = new StringBuilder(); Row(b, values); return b; }
        private static void Row(StringBuilder b, params object[] values)
        {
            b.Append(string.Join(",", values.Select(v => Csv(v?.ToString() ?? string.Empty)))).Append('\n');
        }
        private static string Csv(string value) => SpreadsheetSafeCsv.Encode(value);
    }
}
