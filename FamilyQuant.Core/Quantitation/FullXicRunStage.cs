using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>Measures one acquisition at a time. Writes rows, TIC, metadata and the independent
    /// detector diagnostic into the pending computation, keeping the existing failure boundaries.</summary>
    internal static class FullXicRunStage
    {
        internal static void Extract(FullXicInput input, string run, FullXicOptions options,
            FullXicComputeResult result, List<RawScanPrecursorMetadata> rawPrecursorMetadata,
            Func<string, string, string>? cachePathFor, Action<string>? progress,
            Func<string, string>? detectedCachePathFor, OperationContext? operation = null)
        {
            operation?.Report("run-phase:" + run, "Preparing acquisition: " + run, 0, 7);
            void Phase(int index, string title) => operation?.Report("run-phase:" + run, "Run " + run + ", step "
                + index.ToString(CultureInfo.InvariantCulture) + " of 7: " + title, index, 7);
            string rawDirectory = input.RawDirectory;
            var storage = new FullXicStoragePaths(rawDirectory, options.CacheDirectory);
            Dictionary<string, string> rawByRun = input.RawByRun;
            Dictionary<string, List<PatternLabPsm>> psmsByRun = input.PsmsByRun;
            FullXicSourceInput deNovoImport = input.Sources;
            Dictionary<string, List<PeaksDeNovoRow>> deNovoByRun = deNovoImport.ByRun;
            QuantManifest manifest = result.Manifest;
            string rawPath = rawByRun[run];
            progress?.Invoke(run + ": fingerprinting RAW input...");
            Phase(1, "Fingerprinting acquisition");
            string rawFingerprint = FileFingerprint.Sha256(rawPath);
            manifest.ScientificInputFingerprints[Path.GetFullPath(rawPath)] = rawFingerprint;
            bool ticRecorded = false;

            // Instrument-selected precursor metadata is the audit reference. Source-reported charge
            // and m/z remain in their own columns and are never relabelled as RAW truth.
            Phase(2, "Reading precursor metadata");
            try
            {
                IEnumerable<int> scans = (psmsByRun.TryGetValue(run,
                        out List<PatternLabPsm>? metadataPsms)
                        ? metadataPsms.Select(p => p.ScanNumber) : Enumerable.Empty<int>())
                    .Concat(deNovoImport.AllSourceRows.Where(r => string.Equals(
                        DeNovoReconciler.NormalizeRunName(r.RunName), run,
                        StringComparison.OrdinalIgnoreCase)).Select(r => r.Scan));
                rawPrecursorMetadata.AddRange(
                    RawScanChargeReader.ReadMetadata(rawPath, scans).Values);
            }
            catch (Exception ex)
            {
                result.Notes.Add(run + " RAW precursor metadata could not be read (" +
                    ex.GetType().Name + ": " + ex.Message +
                    "); the spectrum table labels source-reported charge/mz as a fallback.");
            }

            // ---- identified side ----------------------------------------------------------------
            Phase(3, "Identified precursor batches");
            if (psmsByRun.TryGetValue(run, out List<PatternLabPsm>? runPsms) && runPsms.Count > 0)
            {
                FullXicCacheLocation cache = storage.Extraction("identified", run, cachePathFor?.Invoke("identified", run));
                var opt = new DeNovoXicOptions
                {
                    IsotopeCount = options.IsotopeCount,
                    MinPointsForQuant = options.MinPointsForQuant,
                    Ppm = options.Ppm,
                    Ms1Tolerance = options.Ms1Tolerance,
                    CachePath = cache.WritePath,
                    LegacyCachePath = cache.LegacyReadPath,
                    SourceFingerprint = rawFingerprint,
                    Progress = progress,
                };

                progress?.Invoke(run + ": extracting " +
                    runPsms.Count.ToString("N0", CultureInfo.InvariantCulture) + " identified PSM(s)...");

                var (results, stats) = IdentifiedQuantExtractor.ExtractForRun(rawPath, runPsms, opt, operation);

                List<QuantRow> rows = QuantRow.FromResults("identified", run, results);
                QuantRow.AttachPatternLabScores(rows, runPsms);
                result.Rows.AddRange(rows);

                manifest.PerRunTic[run] = stats.TicArea;
                ticRecorded = true;

                result.Notes.Add(run + " identified: " +
                    rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " row(s) with signal of " +
                    stats.UniquePrecursors.ToString("N0", CultureInfo.InvariantCulture) + " unique precursor(s)");
            }

            // ---- de novo side -------------------------------------------------------------------
            Phase(4, "De novo precursor batches");
            if (deNovoByRun.TryGetValue(run, out List<PeaksDeNovoRow>? runDeNovo) && runDeNovo.Count > 0)
            {
                FullXicCacheLocation cache = storage.Extraction("denovo", run, cachePathFor?.Invoke("denovo", run));
                var opt = new DeNovoXicOptions
                {
                    IsotopeCount = options.IsotopeCount,
                    MinPointsForQuant = options.MinPointsForQuant,
                    Ppm = options.Ppm,
                    Ms1Tolerance = options.Ms1Tolerance,
                    CachePath = cache.WritePath,
                    LegacyCachePath = cache.LegacyReadPath,
                    SourceFingerprint = rawFingerprint,
                    Progress = progress,
                };

                progress?.Invoke(run + ": extracting " +
                    runDeNovo.Count.ToString("N0", CultureInfo.InvariantCulture) + " kept de novo row(s)...");

                var (results, _) = DeNovoBatchExtractor.ExtractForRun(rawPath, runDeNovo, opt, operation);

                List<QuantRow> rows = QuantRow.FromResults("denovo", run, results);
                QuantRow.AttachDeNovoEvidence(rows, runDeNovo);
                result.Rows.AddRange(rows);

                result.Notes.Add(run + " de novo: " +
                    rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " row(s) with signal");
            }

            // A run with de novo rows but no identifications still needs its TIC recorded, so the
            // denominator every fraction is taken against is present for every run in the store.
            Phase(5, "Recording total ion current");
            if (!ticRecorded)
                manifest.PerRunTic[run] = options.ReadTicArea?.Invoke(rawPath) ?? DeNovoXicExtractor.GetTotalIonCurrentArea(rawPath, 0, 0);

            // ---- Q6: independent resolved isotope-series diagnostic ------------------------------
            // Walk the run's MS1 map once more to compute a detector diagnostic. It is not a peptide-
            // FDR-calibrated biological fraction and therefore does not close a physical TIC partition.
            // It loads the RAW again, so the result is cached using content and parameter provenance.
            // A failure here must not lose the per-peptide quant already measured, so it is caught and
            // noted rather than thrown.
            Phase(6, "Resolved isotope-series diagnostic");
            try
            {
                FullXicCacheLocation detectedCache = storage.Detector(run, detectedCachePathFor?.Invoke(run));

                Ms1EnvelopeDetectionResult det = Ms1EnvelopeDetector.DetectRun(
                    rawPath, detectedCache.WritePath, progress, rawFingerprint, detectedCache.LegacyReadPath, operation, requireMassRanges: true);
                result.Detected[run] = det;

                manifest.PerRunTic.TryGetValue(run, out double runTic);
                result.Notes.Add(run + " resolved isotope-series diagnostic, identified or not (NOT the share " +
                    "explained; not peptide-FDR calibrated): " +
                    det.DetectedEnvelopeCount.ToString("N0", CultureInfo.InvariantCulture) + " envelope(s), area " +
                    det.DetectedPeptideLikeArea.ToString("E4", CultureInfo.InvariantCulture) +
                    (runTic > 0
                        ? " (" + (det.DetectedPeptideLikeArea / runTic).ToString("P2", CultureInfo.InvariantCulture) + " of TIC)"
                        : string.Empty));
            }
            catch (Exception ex)
            {
                result.Notes.Add(run + " resolved isotope-series diagnostic did not run (" +
                    ex.GetType().Name + ": " + ex.Message + "); the per-peptide quant is unaffected.");
            }

            // The MS1 list of the run just finished is the biggest allocation in the process. Let it
            // go before the next run's is loaded, so peak memory is one run and not two.
            Phase(7, "Releasing acquisition memory");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

    }
}
