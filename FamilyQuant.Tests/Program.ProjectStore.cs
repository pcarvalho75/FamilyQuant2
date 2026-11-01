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
        private static void GoldenNumberGuardTests()
        {
            string? repository = null;
            try { repository = GoldenNumbers.FindRepository(); }
            catch (DirectoryNotFoundException) { }
            Check("the golden guard recognizes the renamed 2.0 worktree",
                repository != null && File.Exists(Path.Combine(repository, "FamilyQuant2.sln")));

            var original = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["families.csv"] = Encoding.UTF8.GetBytes("family,share\r\n1,0.25\r\n"),
            };
            var same = original.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
            Check("golden guard accepts identical export bytes", GoldenNumbers.Differences(original, same).Count == 0);
            same["families.csv"] = Encoding.UTF8.GetBytes("family,share\r\n1,0.26\r\n");
            Check("golden guard catches a one-share mutation of equal length",
                GoldenNumbers.Differences(original, same).SequenceEqual(new[] { "families.csv" }));
            same.Clear();
            Check("golden guard catches a missing export", GoldenNumbers.Differences(original, same).Count == 1);
            same["extra.csv"] = Array.Empty<byte>();
            Check("golden guard catches an unexpected export", GoldenNumbers.Differences(original, same).Count == 2);
            string numbers = GoldenNumbers.NumericValues("{\"generatedUtc\":\"now\",\"values\":[9007199254740993,0.12345678901234567]}");
            Check("golden pack projection keeps integer and floating point literals without conversion",
                numbers.Contains("9007199254740993") && numbers.Contains("0.12345678901234567"));
            Check("golden pack projection ignores timestamp changes and object property order",
                GoldenNumbers.NumericValues("{\"x\":2,\"generatedUtc\":\"first\",\"y\":3}")
                == GoldenNumbers.NumericValues("{\"y\":3,\"generatedUtc\":\"second\",\"x\":2}"));
        }

        // ============================================================ project store (Q2)

        /// <summary>
        /// The .fqproj container round trip, the .fcf migration, and the on-disk structure. All synthetic
        /// and headless, so it runs anywhere and needs no RAW.
        /// </summary>
        private static void ProjectStoreTests(string workDir)
        {
            // ---- fixtures: a curated file, a mixed quant table, a manifest ---------------------------
            var fcf = new FamilyClusterFile
            {
                ClusteringIdentity = 80,
                WordSimilarity = 0.7,
                IsKeywordSuperCluster = false,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "sp|P00001|TEST", Description = "Metalloproteinase, \"partial\"",
                        Sequence = "MKAWLL", UserAnnotation = "SVMP", USearchID = 2, UserID = 5,
                        XIC = 0.125, XICUser = 0.0625,
                    },
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "sp|P00002|TEST2", Description = "Phospholipase A2",
                        Sequence = "SLFELGK", UserAnnotation = "", USearchID = 3, UserID = 5,
                        XIC = 0.5, XICUser = 0.5,
                    },
                },
            };
            fcf.LockedUserIds.Add(5);

            // A quant row with a NoCluster sentinel among its isotopes, one with a null family, one at a
            // charge that makes the isotope step small: the awkward cases, not just the tidy one.
            var rows = new List<QuantRow>
            {
                new QuantRow
                {
                    Side = "identified", Run = "run_01", Scan = 12345, PrecursorMz = 700.123456789,
                    Charge = 2, Sequence = "SAMPLER",
                    IsotopeAreas = new[] { 5627968.3, 100.25, 10.0, -1.0 },
                    EnvelopeSum = 5627968.3 + 100.25 + 10.0, FamilyUserId = 5,
                },
                new QuantRow
                {
                    Side = "denovo", Run = "run_02", Scan = 998, PrecursorMz = 1028.51038,
                    Charge = 3, Sequence = "PEPTIDEK",
                    IsotopeAreas = new[] { 42.0, 0.0, 3.5, 0.0 },
                    EnvelopeSum = 45.5, FamilyUserId = null,
                },
            };

            var manifest = new QuantManifest
            {
                FormatVersion = ProjectStore.CurrentFormatVersion,
                IsotopeCount = 4,
                Ppm = 10.0,
                Ms1Tolerance = 1,
                AppVersion = "test-1.3.0.0",
                CreatedUtc = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc),
                UpdatedUtc = new DateTime(2026, 8, 26, 12, 30, 0, DateTimeKind.Utc),
                Provenance = QuantManifest.DefaultProvenance(),
                DeNovoOverlap = new DeNovoOverlapSummary
                {
                    RawDeNovoRows = 10, FilteredDeNovoRows = 5,
                    NoSearchPsmInFileAndScan = 3, SharedFileAndScan = 2,
                    DifferentPrecursorInSharedScan = 1, SamePrecursorWithinTolerance = 1,
                    ScanLevelStrictSequenceMatchesIgnoringPtm = 0,
                    ScanLevelAdditionalIleLeuEquivalentMatches = 1,
                    ScanLevelSequenceDivergencesIgnoringPtmStrict = 2,
                    ScanLevelSequenceDivergencesAfterIleLeuFold = 1,
                    StrictSequenceMatchesIgnoringPtm = 0, AdditionalIleLeuEquivalentMatches = 1,
                    SequenceDivergencesIgnoringPtmStrict = 1, SequenceDivergencesAfterIleLeuFold = 0,
                    MinLength = 7, MinAverageLocalConfidence = 80, StretchLength = 5, StretchFloor = 90,
                    PrecursorTolerancePpm = 20, WeakXCorr = 2.5,
                },
                DeNovoScoreDistribution = DeNovoScoreDistributionStudy.Build(new[]
                {
                    new PeaksDeNovoRow { Length = 8, Alc = 75, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                    new PeaksDeNovoRow { Length = 8, Alc = 85, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                }),
                DeNovoIdentification = new DeNovoIdentificationSummary
                {
                    FilteredSpectrumCalls = 5,
                    CarriedForwardSpectrumCalls = 4,
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
                            Run = "run_01", FilteredSpectrumCalls = 3, CarriedForwardSpectrumCalls = 2,
                            FilteredSequenceChargeGroups = 2, FilteredDistinctSequencesIgnoringPtm = 2,
                            CarriedForwardSequenceChargeGroups = 1, CarriedForwardDistinctSequencesIgnoringPtm = 1,
                        },
                        new DeNovoIdentificationRunSummary
                        {
                            Run = "run_02", FilteredSpectrumCalls = 2, CarriedForwardSpectrumCalls = 2,
                            FilteredSequenceChargeGroups = 2, FilteredDistinctSequencesIgnoringPtm = 1,
                            CarriedForwardSequenceChargeGroups = 2, CarriedForwardDistinctSequencesIgnoringPtm = 1,
                        },
                    },
                },
                IdentificationConcordance = new IdentificationConcordanceSummary
                {
                    PatternLabPsmRows = 100,
                    PeaksDatabasePsmRows = 80,
                    PatternLabScanKeys = 90,
                    PeaksDatabaseScanKeys = 75,
                    SharedDatabaseSearchScanKeys = 60,
                    ExactDatabaseSearchSequenceScanKeys = 58,
                    AdditionalIleLeuEquivalentDatabaseSearchScanKeys = 1,
                    SameChargeDatabaseSearchScanKeys = 59,
                    ExactSameChargeDatabaseSearchScanKeys = 58,
                    PrecursorQualifiedDatabaseSearchPairs = 59,
                    ExactPrecursorQualifiedDatabaseSearchPairs = 58,
                    AdditionalIleLeuEquivalentPrecursorQualifiedPairs = 1,
                    PrecursorQualifiedDatabaseSearchScanKeys = 59,
                    ExactPrecursorQualifiedDatabaseSearchScanKeys = 58,
                    PatternLabPrecursorHypothesesInSharedScans = 60,
                    PeaksDatabasePrecursorHypothesesInSharedScans = 60,
                    PrecursorTolerancePpm = 20,
                    FilteredDeNovoRows = 20,
                    DeNovoRowsSharingPeaksDatabaseScan = 5,
                    DeNovoRowsSharingPeaksDatabaseScanAndCharge = 3,
                    DeNovoRowsExactToPeaksDatabaseAtSameCharge = 1,
                    PatternLabReportedChargeOneRows = 2,
                    PeaksDatabaseReportedChargeOneRows = 3,
                    DeNovoReportedChargeOneRows = 1,
                },
                ReferenceEnvelopeModelDiagnostics = new ReferenceEnvelopeDiagnostics
                {
                    Method = ReferenceEnvelopeEstimator.Method,
                    IdentifiedPeptides = 2,
                    DeNovoPeptides = 1,
                    PooledEnvelopeToM0Ratio = 1.25,
                    CrossValidatedPeptides = 2,
                    CrossValidatedWeightedAbsolutePercentError = 0.08,
                    CrossValidatedMedianAbsolutePercentError = 0.07,
                    CrossValidatedP90AbsolutePercentError = 0.09,
                    CrossValidatedSignedBiasFraction = -0.01,
                    FixedBinDeNovoEstimate = 125,
                    PooledRatioDeNovoEstimate = 120,
                    SensitivityLowerEstimate = 120,
                    SensitivityUpperEstimate = 125,
                    Bins = new List<ReferenceEnvelopeBinDiagnostic>
                    {
                        new ReferenceEnvelopeBinDiagnostic
                        {
                            Bin = 0, LowerMassDa = 0, UpperMassDa = 800,
                            IdentifiedPeptides = 2, IdentifiedM0 = 200,
                            IdentifiedEnvelope = 250, EnvelopeToM0Ratio = 1.25,
                        },
                        new ReferenceEnvelopeBinDiagnostic
                        {
                            Bin = 1, LowerMassDa = 800, UpperMassDa = null,
                            IdentifiedPeptides = 0, EnvelopeToM0Ratio = 1.25,
                            UsesPooledFallback = true,
                        },
                    },
                },
                PresentationIdentitySweep = new List<IdentityScanRow>
                {
                    new IdentityScanRow { Identity = 80, Groups = 42, TargetGroups = 21,
                        NonContaminantTargetGroups = 18, MixedGroups = 0, Score = 0 },
                    new IdentityScanRow { Identity = 70, Groups = 35, TargetGroups = 17,
                        NonContaminantTargetGroups = 14, MixedGroups = 1, Score = 1.0 / 35.0 },
                },
                SpectrumComparisonRows = new List<SpectrumComparisonRow>
                {
                    new SpectrumComparisonRow
                    {
                        FileName = "run_01.raw", NormalizedRunName = "run_01",
                        ScanNumber = 12345, PrecursorHypothesisIndex = 1,
                        Charge = 2, PrecursorMz = 700.123456789,
                        PeaksDeNovoSequence = "C(+57.02)EPTIDEK",
                        PeaksDeNovoCleanSequence = "CEPTIDEK", PeaksDeNovoScore = 88.5,
                        PeaksDeNovoScoreField = "ALC (%)", PeaksDeNovoAccepted = true,
                        PatternLabSequence = "CEPTIDEK", PatternLabCleanSequence = "CEPTIDEK",
                        PatternLabXScore = 4.25, PrimaryQuantValue = 5628078.55,
                        QuantPrimaryAssigned = 5628078.55,
                        QuantPrimaryBasis = "observed:identified credited envelope",
                        JoinStatus = "synthetic version-2 persistence fixture",
                    },
                },
            };
            manifest.PerRunTic["run_01"] = 875643650496.5421;
            manifest.PerRunTic["run_02"] = 123456789.0;

            string path = Path.Combine(workDir, "roundtrip.fqproj");
            ProjectStore.WriteFqproj(path, fcf, rows, manifest);

            var (fcf2, rows2, manifest2) = ProjectStore.ReadFqproj(path);

            // ---- curation is identical --------------------------------------------------------------
            Check("curation: item count survives", fcf2.MyExtendedFastaItems.Count == 2);
            Check("curation: clustering identity survives", fcf2.ClusteringIdentity == 80);
            Check("curation: word similarity survives", fcf2.WordSimilarity == 0.7);
            Check("curation: locks survive", fcf2.LockedUserIds.SetEquals(new[] { 5 }));
            var a0 = fcf.MyExtendedFastaItems[0];
            var b0 = fcf2.MyExtendedFastaItems[0];
            Check("curation: locus survives", b0.SequenceIdentifier == a0.SequenceIdentifier);
            Check("curation: a description with quotes survives", b0.Description == a0.Description);
            Check("curation: both family ids survive", b0.USearchID == a0.USearchID && b0.UserID == a0.UserID);
            Check("curation: the curation.json is byte-identical to what a .fcf would hold",
                fcf.ToJsonText() == fcf2.ToJsonText());

            // ---- every quant row round-trips bit for bit --------------------------------------------
            Check("quant: row count survives", rows2.Count == rows.Count, rows2.Count.ToString());

            bool allExact = rows2.Count == rows.Count;
            for (int i = 0; i < Math.Min(rows.Count, rows2.Count) && allExact; i++)
            {
                QuantRow x = rows[i], y = rows2[i];
                allExact &= x.Side == y.Side && x.Run == y.Run && x.Scan == y.Scan
                    && x.Charge == y.Charge && x.Sequence == y.Sequence
                    && x.FamilyUserId == y.FamilyUserId
                    && BitConverter.DoubleToInt64Bits(x.PrecursorMz) == BitConverter.DoubleToInt64Bits(y.PrecursorMz)
                    && BitConverter.DoubleToInt64Bits(x.EnvelopeSum) == BitConverter.DoubleToInt64Bits(y.EnvelopeSum)
                    && x.IsotopeAreas.Length == y.IsotopeAreas.Length;
                for (int k = 0; k < x.IsotopeAreas.Length && allExact; k++)
                    allExact &= BitConverter.DoubleToInt64Bits(x.IsotopeAreas[k])
                             == BitConverter.DoubleToInt64Bits(y.IsotopeAreas[k]);
            }
            Check("quant: every per-isotope area and envelope sum round-trips bit for bit", allExact);
            Check("quant: the NoCluster sentinel survives untouched among the isotopes",
                rows2[0].IsotopeAreas.Length == 4 && rows2[0].IsotopeAreas[3] == -1.0,
                string.Join(",", rows2[0].IsotopeAreas));
            Check("quant: a null family id stays null", rows2[1].FamilyUserId == null);

            // ---- the manifest params match ----------------------------------------------------------
            Check("manifest: format version", manifest2.FormatVersion == ProjectStore.CurrentFormatVersion);
            Check("manifest: isotope count", manifest2.IsotopeCount == 4);
            Check("manifest: ppm", manifest2.Ppm == 10.0);
            Check("manifest: MS1 tolerance", manifest2.Ms1Tolerance == 1);
            Check("manifest: app version", manifest2.AppVersion == "test-1.3.0.0");
            Check("manifest: per-run TIC survives bit for bit",
                BitConverter.DoubleToInt64Bits(manifest2.PerRunTic["run_01"])
                    == BitConverter.DoubleToInt64Bits(875643650496.5421)
                && manifest2.PerRunTic["run_02"] == 123456789.0);
            Check("manifest: timestamps survive", manifest2.CreatedUtc == manifest.CreatedUtc
                && manifest2.UpdatedUtc == manifest.UpdatedUtc);
            Check("manifest: provenance marks the areas measured and the envelope derived",
                manifest2.Provenance["area_m0"] == "measured" && manifest2.Provenance["envelope_sum"] == "derived");
            Check("manifest: the nested de novo overlap study survives",
                manifest2.DeNovoOverlap != null
                && manifest2.DeNovoOverlap.RawDeNovoRows == 10
                && manifest2.DeNovoOverlap.SharedFileAndScan == 2
                && manifest2.DeNovoOverlap.ScanLevelAdditionalIleLeuEquivalentMatches == 1
                && manifest2.DeNovoOverlap.AdditionalIleLeuEquivalentMatches == 1,
                manifest2.DeNovoOverlap == null ? "missing" :
                    manifest2.DeNovoOverlap.RawDeNovoRows + " / " +
                    manifest2.DeNovoOverlap.SharedFileAndScan + " / " +
                    manifest2.DeNovoOverlap.ScanLevelAdditionalIleLeuEquivalentMatches + " / " +
                    manifest2.DeNovoOverlap.AdditionalIleLeuEquivalentMatches);
            Check("manifest: the PEAKS ALC score distribution survives",
                manifest2.DeNovoScoreDistribution != null
                && manifest2.DeNovoScoreDistribution.RawRows == 2
                && manifest2.DeNovoScoreDistribution.RowsPassingAllCriteria == 1
                && manifest2.DeNovoScoreDistribution.Bins.Sum(b => b.RawRows) == 2,
                manifest2.DeNovoScoreDistribution == null ? "missing" :
                    manifest2.DeNovoScoreDistribution.RawRows + " / " +
                    manifest2.DeNovoScoreDistribution.RowsPassingAllCriteria);
            Check("manifest: the identification-only de novo consolidation survives",
                manifest2.DeNovoIdentification != null
                && manifest2.DeNovoIdentification.FilteredSpectrumCalls == 5
                && manifest2.DeNovoIdentification.CarriedForwardDistinctSequencesIgnoringPtm == 2
                && manifest2.DeNovoIdentification.Runs.Count == 2,
                manifest2.DeNovoIdentification == null ? "missing" :
                    manifest2.DeNovoIdentification.FilteredSpectrumCalls + " / " +
                    manifest2.DeNovoIdentification.CarriedForwardDistinctSequencesIgnoringPtm);
            Check("manifest: the independent database-search concordance survives",
                manifest2.IdentificationConcordance != null
                && manifest2.IdentificationConcordance.SharedDatabaseSearchScanKeys == 60
                && manifest2.IdentificationConcordance.ExactDatabaseSearchSequenceScanKeys == 58
                && manifest2.IdentificationConcordance.ExactSameChargeDatabaseSearchScanKeys == 58
                && manifest2.IdentificationConcordance.PrecursorQualifiedDatabaseSearchPairs == 59,
                manifest2.IdentificationConcordance == null ? "missing" :
                    manifest2.IdentificationConcordance.SharedDatabaseSearchScanKeys + " / " +
                    manifest2.IdentificationConcordance.ExactDatabaseSearchSequenceScanKeys + " / " +
                    manifest2.IdentificationConcordance.ExactSameChargeDatabaseSearchScanKeys);
            Check("manifest: reference-envelope support and validation diagnostics survive",
                manifest2.ReferenceEnvelopeModelDiagnostics != null
                && manifest2.ReferenceEnvelopeModelDiagnostics.Method == ReferenceEnvelopeEstimator.Method
                && manifest2.ReferenceEnvelopeModelDiagnostics.Bins.Count == 2
                && manifest2.ReferenceEnvelopeModelDiagnostics.Bins.Last().UpperMassDa == null
                && manifest2.ReferenceEnvelopeModelDiagnostics.CrossValidatedPeptides == 2,
                manifest2.ReferenceEnvelopeModelDiagnostics?.Method);
            Check("manifest: the measured presentation identity sweep survives",
                manifest2.PresentationIdentitySweep.Count == 2
                && manifest2.PresentationIdentitySweep[0].Identity == 80
                && manifest2.PresentationIdentitySweep[1].NonContaminantTargetGroups == 14,
                manifest2.PresentationIdentitySweep.Count.ToString(CultureInfo.InvariantCulture));
            Check("manifest: the streamed spectrum comparison table survives",
                manifest2.SpectrumComparisonRows.Count == 1
                && manifest2.SpectrumComparisonRowCount == 1
                && manifest2.SpectrumComparisonRows[0].ScanNumber == 12345
                && manifest2.SpectrumComparisonRows[0].PeaksDeNovoSequence
                    == "C(+57.02)EPTIDEK"
                && manifest2.SpectrumComparisonRows[0].PatternLabXScore == 4.25,
                manifest2.SpectrumComparisonRows.Count.ToString(CultureInfo.InvariantCulture));

            // ---- the v2 container keeps the large comparison table outside manifest.json ------------
            using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
            {
                var names = zip.Entries.Select(en => en.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
                Check("structure: the zip holds the four documented v2 entries",
                    names.SequenceEqual(new[]
                    {
                        "curation.json", "manifest.json", "quant.tsv.gz",
                        ProjectStore.SpectrumComparisonEntryName,
                    }.OrderBy(n => n, StringComparer.Ordinal)),
                    string.Join(", ", names));

                var manifestEntry = zip.GetEntry(ProjectStore.ManifestEntryName)!;
                using (var manifestReader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
                {
                    string persistedManifest = manifestReader.ReadToEnd();
                    Check("structure: manifest.json does not embed the spectrum comparison rows",
                        !persistedManifest.Contains("SpectrumComparisonRows", StringComparison.Ordinal)
                        && !persistedManifest.Contains("C(+57.02)EPTIDEK", StringComparison.Ordinal),
                        persistedManifest.Length.ToString(CultureInfo.InvariantCulture) + " chars");
                    Check("structure: manifest.json declares the exact spectrum sidecar row count",
                        persistedManifest.Contains("\"SpectrumComparisonRowCount\": 1",
                            StringComparison.Ordinal), persistedManifest);
                }

                var comparisonEntry = zip.GetEntry(ProjectStore.SpectrumComparisonEntryName)!;
                using (var comparisonGzip = new System.IO.Compression.GZipStream(
                    comparisonEntry.Open(), System.IO.Compression.CompressionMode.Decompress))
                using (var comparisonReader = new StreamReader(comparisonGzip, Encoding.UTF8))
                {
                    string persistedRow = comparisonReader.ReadLine() ?? string.Empty;
                    SpectrumComparisonRow? streamedComparison =
                        JsonSerializer.Deserialize<SpectrumComparisonRow>(persistedRow);
                    Check("structure: the spectrum comparison sidecar is line-streamable JSON",
                        streamedComparison?.PeaksDeNovoSequence == "C(+57.02)EPTIDEK"
                        && comparisonReader.ReadLine() == null, persistedRow);
                }

                Check("structure: the quant entry is parquet or tsv.gz",
                    ProjectStore.QuantEntryName.EndsWith(".parquet", StringComparison.Ordinal)
                    || ProjectStore.QuantEntryName.EndsWith(".tsv.gz", StringComparison.Ordinal),
                    ProjectStore.QuantEntryName);

                // The gz entry decompresses to a TSV whose header is exactly the documented columns, in
                // order: a structural check, not a Python invocation, but the same header pandas reads.
                var quantEntry = zip.GetEntry(ProjectStore.QuantEntryName)!;
                using var gz = new System.IO.Compression.GZipStream(quantEntry.Open(),
                    System.IO.Compression.CompressionMode.Decompress);
                using var reader = new StreamReader(gz);
                string header = reader.ReadLine() ?? string.Empty;
                Check("structure: the quant table header is the documented column set, in order",
                    header.Split('\t').SequenceEqual(ProjectStore.QuantColumns), header);
            }

            string missingSpectrumSidecar = Path.Combine(workDir,
                "roundtrip_missing_spectrum_sidecar.fqproj");
            File.Copy(path, missingSpectrumSidecar, overwrite: true);
            using (var zip = System.IO.Compression.ZipFile.Open(missingSpectrumSidecar,
                System.IO.Compression.ZipArchiveMode.Update))
                zip.GetEntry(ProjectStore.SpectrumComparisonEntryName)?.Delete();
            string missingSidecarMessage = string.Empty;
            try { _ = ProjectStore.ReadFqproj(missingSpectrumSidecar); }
            catch (InvalidDataException ex) { missingSidecarMessage = ex.Message; }
            Check("structure: a v2 project cannot silently lose its declared spectrum sidecar",
                missingSidecarMessage.Contains("declares 1 spectrum comparison row", StringComparison.Ordinal)
                && missingSidecarMessage.Contains("entry is missing", StringComparison.Ordinal),
                missingSidecarMessage);

            string shortSpectrumSidecar = Path.Combine(workDir,
                "roundtrip_short_spectrum_sidecar.fqproj");
            File.Copy(path, shortSpectrumSidecar, overwrite: true);
            using (var zip = System.IO.Compression.ZipFile.Open(shortSpectrumSidecar,
                System.IO.Compression.ZipArchiveMode.Update))
            {
                zip.GetEntry(ProjectStore.SpectrumComparisonEntryName)?.Delete();
                var entry = zip.CreateEntry(ProjectStore.SpectrumComparisonEntryName,
                    System.IO.Compression.CompressionLevel.NoCompression);
                using var gzip = new System.IO.Compression.GZipStream(entry.Open(),
                    System.IO.Compression.CompressionMode.Compress);
            }
            string shortSidecarMessage = string.Empty;
            try { _ = ProjectStore.ReadFqproj(shortSpectrumSidecar); }
            catch (InvalidDataException ex) { shortSidecarMessage = ex.Message; }
            Check("structure: a valid gzip sidecar with fewer rows than declared is rejected",
                shortSidecarMessage.Contains("declares 1 spectrum comparison row", StringComparison.Ordinal)
                && shortSidecarMessage.Contains("contains 0", StringComparison.Ordinal)
                && shortSidecarMessage.Contains("incomplete", StringComparison.Ordinal),
                shortSidecarMessage);

            // ---- migration: an old .fcf still opens; save as .fqproj; reload; curation identical -----
            string oldFcf = Path.Combine(workDir, "legacy.fcf");
            fcf.Save(oldFcf);                                   // a plain-JSON .fcf, as today's Save writes

            FamilyClusterFile legacy = FamilyClusterFile.Load(oldFcf);
            Check("migration: an old .fcf still opens with its curation",
                legacy.MyExtendedFastaItems.Count == 2 && legacy.LockedUserIds.SetEquals(new[] { 5 }));

            // Opening a .fcf carries no stored quant: the migration writes an empty quant part.
            string migrated = Path.Combine(workDir, "migrated.fqproj");
            var emptyManifest = new QuantManifest
            {
                IsotopeCount = 4, Ppm = 10.0, Ms1Tolerance = 1, AppVersion = "test",
                CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow,
                Provenance = QuantManifest.DefaultProvenance(),
            };
            ProjectStore.WriteFqproj(migrated, legacy, new List<QuantRow>(), emptyManifest);

            var (fcf3, rows3, _) = ProjectStore.ReadFqproj(migrated);
            Check("migration: the migrated project keeps the curation",
                fcf3.MyExtendedFastaItems.Count == 2 && fcf3.WordSimilarity == 0.7
                && fcf3.LockedUserIds.SetEquals(new[] { 5 }));
            Check("migration: the migrated project has an empty quant part until it is computed",
                rows3.Count == 0, rows3.Count.ToString());
            Check("migration: the old .fcf still opens after the migration, unchanged",
                FamilyClusterFile.Load(oldFcf).MyExtendedFastaItems.Count == 2);

            // ---- IsProjectPath branches by extension, so old .fcf reading is never touched ----------
            Check("IsProjectPath recognises a .fqproj and not a .fcf",
                ProjectStore.IsProjectPath("x.fqproj") && ProjectStore.IsProjectPath("X.FQPROJ")
                && !ProjectStore.IsProjectPath("x.fcf"));

            // ---- backward compatibility: v1 embedded comparison rows still open -------------------
            string embeddedLegacy = Path.Combine(workDir, "embedded_spectrum_v1.fqproj");
            var embeddedLegacyManifest = new QuantManifest
            {
                FormatVersion = 1,
                IsotopeCount = 4,
                Ppm = 10.0,
                Ms1Tolerance = 1,
                AppVersion = "legacy-v1",
                Provenance = QuantManifest.DefaultProvenance(),
                SpectrumComparisonRows = manifest.SpectrumComparisonRows,
            };
            ProjectStore.WriteFqproj(embeddedLegacy, fcf, new List<QuantRow>(),
                embeddedLegacyManifest);
            embeddedLegacyManifest.FormatVersion = 1;
            embeddedLegacyManifest.SpectrumComparisonRowCount = null;
            using (var zip = System.IO.Compression.ZipFile.Open(
                embeddedLegacy, System.IO.Compression.ZipArchiveMode.Update))
            {
                zip.GetEntry(ProjectStore.SpectrumComparisonEntryName)?.Delete();
                zip.GetEntry(ProjectStore.ManifestEntryName)?.Delete();
                var legacyManifestEntry = zip.CreateEntry(ProjectStore.ManifestEntryName,
                    System.IO.Compression.CompressionLevel.Optimal);
                using var legacyWriter = new StreamWriter(legacyManifestEntry.Open(),
                    new UTF8Encoding(false));
                legacyWriter.Write(JsonSerializer.Serialize(embeddedLegacyManifest,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition =
                            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                    }));
            }
            var (_, _, embeddedLegacyRead) = ProjectStore.ReadFqproj(embeddedLegacy);
            Check("migration: a v1 manifest with embedded spectrum rows remains readable",
                embeddedLegacyRead.FormatVersion == 1
                && embeddedLegacyRead.SpectrumComparisonRows.Count == 1
                && embeddedLegacyRead.SpectrumComparisonRows[0].ScanNumber == 12345
                && embeddedLegacyRead.SpectrumComparisonRows[0].PatternLabXScore == 4.25,
                embeddedLegacyRead.FormatVersion.ToString(CultureInfo.InvariantCulture) + " / " +
                embeddedLegacyRead.SpectrumComparisonRows.Count.ToString(CultureInfo.InvariantCulture));

            // ---- a version from the future is refused at write and read boundaries -----------------
            string future = Path.Combine(workDir, "future.fqproj");
            if (File.Exists(future)) File.Delete(future);
            var futureManifest = new QuantManifest
            {
                FormatVersion = ProjectStore.CurrentFormatVersion + 1, IsotopeCount = 4, Ppm = 10, Ms1Tolerance = 1,
                Provenance = QuantManifest.DefaultProvenance(),
            };
            Check("the current writer refuses a future manifest instead of labelling v2 bytes as newer",
                Throws<InvalidDataException>(() => ProjectStore.WriteFqproj(
                    future, fcf, new List<QuantRow>(), futureManifest))
                && !File.Exists(future));

            var currentManifestForFutureRead = new QuantManifest
            {
                IsotopeCount = 4, Ppm = 10, Ms1Tolerance = 1,
                Provenance = QuantManifest.DefaultProvenance(),
            };
            ProjectStore.WriteFqproj(future, fcf, new List<QuantRow>(),
                currentManifestForFutureRead);
            using (var zip = System.IO.Compression.ZipFile.Open(future,
                System.IO.Compression.ZipArchiveMode.Update))
            {
                var currentEntry = zip.GetEntry(ProjectStore.ManifestEntryName)!;
                string currentJson;
                using (var reader = new StreamReader(currentEntry.Open(), Encoding.UTF8))
                    currentJson = reader.ReadToEnd();
                QuantManifest futureOnDisk = JsonSerializer.Deserialize<QuantManifest>(currentJson)!;
                futureOnDisk.FormatVersion = ProjectStore.CurrentFormatVersion + 1;
                currentEntry.Delete();
                var futureEntry = zip.CreateEntry(ProjectStore.ManifestEntryName,
                    System.IO.Compression.CompressionLevel.Optimal);
                using var writer = new StreamWriter(futureEntry.Open(), new UTF8Encoding(false));
                writer.Write(JsonSerializer.Serialize(futureOnDisk));
            }
            Check("a project from a newer format version is refused, not misread",
                Throws<InvalidDataException>(() => ProjectStore.ReadFqproj(future)));

            string nullSpectrumRowPath = Path.Combine(workDir, "null_spectrum_row.fqproj");
            if (File.Exists(nullSpectrumRowPath)) File.Delete(nullSpectrumRowPath);
            var nullSpectrumRowManifest = new QuantManifest
            {
                IsotopeCount = 4, Ppm = 10, Ms1Tolerance = 1,
                Provenance = QuantManifest.DefaultProvenance(),
            };
            nullSpectrumRowManifest.SpectrumComparisonRows.Add(null!);
            Check("the writer rejects a null spectrum row before sidecar counting",
                Throws<InvalidDataException>(() => ProjectStore.WriteFqproj(nullSpectrumRowPath,
                    fcf, new List<QuantRow>(), nullSpectrumRowManifest))
                && !File.Exists(nullSpectrumRowPath));

            // ---- a legacy concordance cannot masquerade as measured 0% agreement ------------------
            string staleConcordance = Path.Combine(workDir, "stale_concordance.fqproj");
            var staleManifest = new QuantManifest
            {
                IsotopeCount = 4, Ppm = 10, Ms1Tolerance = 1,
                Provenance = QuantManifest.DefaultProvenance(),
                IdentificationConcordance = new IdentificationConcordanceSummary
                {
                    PatternLabPsmRows = 2,
                    PeaksDatabasePsmRows = 2,
                    PatternLabScanKeys = 1,
                    PeaksDatabaseScanKeys = 1,
                    SharedDatabaseSearchScanKeys = 1,
                    ExactDatabaseSearchSequenceScanKeys = 1,
                    SameChargeDatabaseSearchScanKeys = 1,
                    ExactSameChargeDatabaseSearchScanKeys = 1,
                },
            };
            ProjectStore.WriteFqproj(staleConcordance, fcf, new List<QuantRow>(), staleManifest);
            string staleMessage = string.Empty;
            try { _ = ProjectStore.ReadFqproj(staleConcordance); }
            catch (InvalidDataException ex) { staleMessage = ex.Message; }
            Check("a stale shared-scan concordance is rejected instead of rendered as zero agreement",
                staleMessage.Contains("precursor-qualified one-to-one PSM", StringComparison.Ordinal)
                && staleMessage.Contains("cannot be used", StringComparison.Ordinal), staleMessage);

            string currentZeroQualified = Path.Combine(workDir, "current_zero_qualified.fqproj");
            var currentZeroManifest = new QuantManifest
            {
                IsotopeCount = 4, Ppm = 10, Ms1Tolerance = 1,
                Provenance = QuantManifest.DefaultProvenance(),
                IdentificationConcordance = new IdentificationConcordanceSummary
                {
                    PatternLabPsmRows = 1, PeaksDatabasePsmRows = 1,
                    PatternLabScanKeys = 1, PeaksDatabaseScanKeys = 1,
                    SharedDatabaseSearchScanKeys = 1,
                    PatternLabPrecursorHypothesesInSharedScans = 1,
                    PeaksDatabasePrecursorHypothesesInSharedScans = 1,
                    PrecursorTolerancePpm = 20,
                },
            };
            ProjectStore.WriteFqproj(currentZeroQualified, fcf, new List<QuantRow>(), currentZeroManifest);
            var (_, _, currentZeroRead) = ProjectStore.ReadFqproj(currentZeroQualified);
            Check("a current audit with real precursor hypotheses but no qualifying pair remains valid",
                currentZeroRead.IdentificationConcordance != null
                && currentZeroRead.IdentificationConcordance.SharedDatabaseSearchScanKeys == 1
                && currentZeroRead.IdentificationConcordance.PrecursorQualifiedDatabaseSearchPairs == 0);

            // ---- an IsotopeCount past the 6 stored columns is refused, not silently truncated ---------
            // The quant table carries area_m0..area_m5 only. A manifest claiming 7+ isotopes would drop
            // the higher measured areas while the pre-truncation EnvelopeSum survived, so writing it is
            // refused at the source rather than persisted as a lossy, self-inconsistent table.
            string overCount = Path.Combine(workDir, "overcount.fqproj");
            var overManifest = new QuantManifest
            {
                IsotopeCount = 7, Ppm = 10, Ms1Tolerance = 1,
                Provenance = QuantManifest.DefaultProvenance(),
            };
            Check("WriteFqproj refuses an IsotopeCount past the stored isotope columns",
                Throws<ArgumentOutOfRangeException>(
                    () => ProjectStore.WriteFqproj(overCount, fcf, new List<QuantRow>(), overManifest)));
            Check("WriteFqproj wrote nothing when it refused the over-count manifest",
                !File.Exists(overCount) && !File.Exists(overCount + ".tmp"));

            // ---- a plain .fcf Save moves a complete temp file into place, leaving no torn file ---------
            // Save writes to fileName+".tmp" then File.Move-s it over the target, so a crash mid-write
            // cannot leave a truncated .fcf that FromJsonText would reject. After a successful save no
            // .tmp remains and the file reloads with its curation intact.
            string atomic = Path.Combine(workDir, "atomic.fcf");
            fcf.Save(atomic);
            Check("Save leaves no .tmp sidecar behind", !File.Exists(atomic + ".tmp"));
            Check("Save's file reloads with its curation intact",
                FamilyClusterFile.Load(atomic).ToJsonText() == fcf.ToJsonText());
        }

        private static void TalkOutputRootTests(string workDir)
        {
            string? prior = Environment.GetEnvironmentVariable(TalkFigures.OutputRootEnvironmentVariable);
            string dataDirectory = Path.Combine(workDir, "talk_input");
            string overrideRoot = Path.Combine(workDir, "evidence_corrected");
            try
            {
                Environment.SetEnvironmentVariable(TalkFigures.OutputRootEnvironmentVariable, null);
                Check("talk root defaults to BrProt_Talk below the scientific data directory",
                    TalkFigures.ResolveTalkOutputRoot(dataDirectory) ==
                        Path.Combine(dataDirectory, TalkFigures.TalkFolderName));

                Environment.SetEnvironmentVariable(
                    TalkFigures.OutputRootEnvironmentVariable, "  " + overrideRoot + "  ");
                Check("the environment override routes every talk lane to an explicit safe root",
                    TalkFigures.ResolveTalkOutputRoot(dataDirectory) == Path.GetFullPath(overrideRoot),
                    TalkFigures.ResolveTalkOutputRoot(dataDirectory));
            }
            finally
            {
                Environment.SetEnvironmentVariable(TalkFigures.OutputRootEnvironmentVariable, prior);
            }
        }
    }
}
