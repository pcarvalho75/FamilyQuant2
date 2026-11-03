using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>
    /// Re-extracts the full isotope envelope of every identified peptide (and, when a PEAKS export is
    /// given, every kept de novo precursor) of each run straight from its RAW, and collects the numbers
    /// into a per-peptide quant table plus a manifest. This is the compute half of phase Q2: the storage
    /// half is <see cref="ProjectStore"/>.
    ///
    /// Coordinates input preparation, sequential extraction and completion. Stage contracts retain the
    /// source audit populations and measured rows used by the stored fractions and spectrum table.
    ///
    /// ONE RUN AT A TIME. The MS1 list of a 3 GB RAW dominates memory, so runs are strictly sequential
    /// and the per-run result lists are released and collected before the next acquisition is opened.
    ///
    /// RUNS ARE MATCHED BY FILE NAME, never by position, through
    /// <see cref="DeNovoReconciler.NormalizeRunName"/>: a PSM list, a PEAKS export and a directory of
    /// RAWs are not in the same order, and extracting a precursor against the wrong acquisition returns
    /// a plausible wrong area.
    /// </summary>
    public static class FullXicComputer
    {
        /// <summary>The cache folder name, now in per-user application data. Older folders beside
        /// acquisitions remain read sources only; new cache and automatic CSV writes stay outside RAW.</summary>
        public const string CacheFolderName = "familyquant_fullxic_cache";

        /// <summary>
        /// Computes the full XIC of every run that has both a RAW and something to extract.
        ///
        /// <paramref name="cachePathFor"/> maps (side, run name) to a cache file path, or is null for the
        /// per-user default. An explicit external path keeps the harness's cache isolation (no legacy
        /// lookup). An acquisition-side override is read only and new entries go to per-user storage.
        /// A SEAM for the harness (which already holds the caches it needs)
        /// and a legitimate hook for a project that keeps its caches elsewhere; the identified and de novo
        /// sides get SEPARATE cache files, because they credit a different isotope count and a cache is
        /// keyed by it.
        ///
        /// <paramref name="restrictToRuns"/>, when non-null, limits the computation to those normalized
        /// run names. Null processes every matched run, which is what the UI does; the harness restricts
        /// to one run so a real-data gate stays fast.
        /// </summary>
        public static FullXicComputeResult ComputeFullXic(
            string rawDirectory,
            string sepr2Path,
            string? deNovoDirectory,
            FullXicOptions options,
            Func<string, string, string>? cachePathFor = null,
            Action<string>? progress = null,
            IReadOnlyList<string>? restrictToRuns = null,
            Func<string, string>? detectedCachePathFor = null, OperationContext? operation = null)
        {
            operation?.Report("compute-phase", "Compute phases", 0, 5);
            operation?.Report("compute-phase", "Phase 1 of 5: Reading inputs", 1, 5);
            FullXicInput input = FullXicInputStage.Prepare(rawDirectory, sepr2Path,
                deNovoDirectory, options, progress, restrictToRuns, operation);
            FullXicComputeResult result = FullXicInputStage.CreateResult(input, options);
            var rawPrecursorMetadata = new List<RawScanPrecursorMetadata>();
            operation?.Report("compute-phase", "Phase 2 of 5: Extracting acquisitions", 2, 5);
            operation?.Report("compute-run", "Acquisitions to process", 0, input.Runs.Count);
            for (int index = 0; index < input.Runs.Count; index++)
            {
                string run = input.Runs[index];
                operation?.Report("compute-run", "Run " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of "
                    + input.Runs.Count.ToString(CultureInfo.InvariantCulture) + ": " + run, index + 1, input.Runs.Count);
                FullXicRunStage.Extract(input, run, options, result, rawPrecursorMetadata,
                    cachePathFor, progress, detectedCachePathFor, operation);
            }
            return FullXicCompletionStage.Complete(input, options, result, rawPrecursorMetadata, operation);
        }

        /// <summary>
        /// The share of the TIC explained, per run (and over all runs when there are several), in the
        /// language of SCIENTIFIC_METHOD.md: the identified observed envelope, plus, when de novo rows
        /// were quantified, the de novo reference-calibrated estimate and their sum (the primary
        /// estimate). The last line says what the resolved isotope-series diagnostic is, so the two
        /// percentages cannot be confused. Empty when there is no run to describe.
        /// </summary>
        public static List<string> DescribeTicShares(IEnumerable<DerivedRunFraction>? fractions)
        {
            var lines = new List<string>();
            List<DerivedRunFraction> all = (fractions ?? Enumerable.Empty<DerivedRunFraction>())
                .Where(d => d != null).ToList();
            List<DerivedRunFraction> runs = all.Where(d => !d.Grouped).ToList();
            if (runs.Count == 0) return lines;

            static string P(double v) => v.ToString("P2", CultureInfo.InvariantCulture);
            bool deNovo = all.Any(d => d.DeNovoPeptides > 0 || d.DeNovoEnvelopeReferenceCalibrated > 0);

            lines.Add(deNovo
                ? "Share of the TIC explained by the quantified peptides (identified observed envelope + " +
                  "de novo reference-calibrated estimate, one area per peptide):"
                : "Share of the TIC explained by the identified peptides (full isotope envelope, one area " +
                  "per peptide):");

            IEnumerable<DerivedRunFraction> shown = runs.Count > 1 ? runs.Concat(all.Where(d => d.Grouped)) : runs;
            foreach (DerivedRunFraction d in shown)
            {
                string name = d.Grouped ? "all runs" : d.Run;
                if (d.Tic <= 0)
                {
                    lines.Add("  " + name + ": no TIC recorded, so no share can be given");
                    continue;
                }
                lines.Add("  " + name + ": " + (deNovo
                    ? "identified " + P(d.IdentifiedFractionEnvelope) + " + de novo " +
                      P(d.DeNovoFractionReferenceCalibrated) + " = " + P(d.RecoveredFractionReferenceCalibrated)
                    : "identified " + P(d.IdentifiedFractionEnvelope)));
            }

            List<double> detected = runs.Where(d => d.Tic > 0 && d.DetectedFractionPeptideLike > 0)
                .Select(d => d.DetectedFractionPeptideLike).ToList();
            if (detected.Count > 0)
            {
                string range = detected.Min() == detected.Max()
                    ? P(detected.Min())
                    : P(detected.Min()) + " to " + P(detected.Max());
                lines.Add("The resolved isotope-series diagnostic further down (" + range + " of the TIC) is how " +
                    "much of the TIC looks like peptide isotope series, identified or not. It is a reference " +
                    "for what could still be explained, not signal already explained.");
            }

            return lines;
        }
    }
}
