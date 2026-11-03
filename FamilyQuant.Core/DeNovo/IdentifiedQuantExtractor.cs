using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// What one run's identified-peptide extraction did: the counts, the isotope-envelope totals and
    /// the identified fraction of the run's ion current on both bases.
    ///
    /// THE COUNTS COUNT ANCHORS; THE TOTALS SUM OVER PEAKS. Everything up to <see cref="WithSignal"/>
    /// counts unique (scan, m/z, charge) requests. <see cref="TotalAreaM0"/> and
    /// <see cref="TotalEnvelope"/> sum over the DISTINCT chromatographic peaks
    /// (<see cref="DeNovoBatchExtractor.DistinctPeaks"/>), because a peptide sampled by several MS2
    /// scans inside one elution peak is several anchors and one peak, and the extractor hands the same
    /// peak back to each. This is the same anchor/peak discipline the de novo side uses, reused rather
    /// than reinvented.
    /// </summary>
    public sealed class IdentifiedQuantStats
    {
        public string RunName { get; set; } = string.Empty;

        /// <summary>The isotope count the areas were credited at, so a reader knows what
        /// <see cref="TotalEnvelope"/> sums over.</summary>
        public int IsotopeCount { get; set; }

        /// <summary>Identified PSMs handed in for this run.</summary>
        public int PsmsIn { get; set; }

        /// <summary>Unique precursors extracted: PSMs after deduplication by (scan, m/z, charge).</summary>
        public int UniquePrecursors { get; set; }

        /// <summary>Anchors whose extraction returned a cluster. NOT a peak count.</summary>
        public int WithSignal { get; set; }

        public int NoCluster { get; set; }

        /// <summary>Anchors whose M+0 cluster was FOUND and refused by the minimum-points rule. Counted
        /// apart from <see cref="NoCluster"/> because they are a different fact: there was signal, and
        /// the quantitation rule declined to integrate it. Its ion current is not lost - it is carried on
        /// the stored rows as under-sampled.</summary>
        public int UnderSampled { get; set; }

        public int Failed { get; set; }

        /// <summary>Distinct extracted chromatographic peaks, which is what the totals sum over.</summary>
        public int DistinctPeaks { get; set; }

        /// <summary>Sum of M+0 over the distinct peaks. The monoisotopic identified signal, on the
        /// same basis as the stored quants each of these rows reproduces bit for bit.</summary>
        public double TotalAreaM0 { get; set; }

        /// <summary>Sum of the isotope envelope (M+0 + ... + M+(IsotopeCount-1), the -1 sentinel
        /// clamped) over the same distinct peaks. The whole point of the new engine: this is strictly
        /// larger than <see cref="TotalAreaM0"/> whenever any credited isotope carries signal.</summary>
        public double TotalEnvelope { get; set; }

        /// <summary>The run's total ion current, from <see cref="DeNovoXicExtractor.GetTotalIonCurrentArea"/>
        /// with no trim. The denominator both fractions are taken over.</summary>
        public double TicArea { get; set; }

        /// <summary><see cref="TotalAreaM0"/> / <see cref="TicArea"/>: the identified fraction of the
        /// run measured monoisotopically, the traditional basis. Zero when the TIC is not positive.</summary>
        public double IdentifiedFractionM0 =>
            TicArea > 0 ? TotalAreaM0 / TicArea : 0.0;

        /// <summary><see cref="TotalEnvelope"/> / <see cref="TicArea"/>: the identified fraction of the
        /// run measured over the isotope envelope, the new basis. Larger than
        /// <see cref="IdentifiedFractionM0"/> by exactly the fraction the higher isotopes add.</summary>
        public double IdentifiedFractionEnvelope =>
            TicArea > 0 ? TotalEnvelope / TicArea : 0.0;

        public double ElapsedSeconds { get; set; }
    }

    /// <summary>
    /// Re-extracts the XIC of every IDENTIFIED peptide of one run straight from its RAW, with the full
    /// isotopic envelope, instead of reading PatternLab's stored monoisotopic quant.
    ///
    /// WHY THIS EXISTS. FamilyQuant's numbers have always come from PatternLab's stored quants, which
    /// are monoisotopic: they credit the M+0 peak alone and discard the rest of the isotope envelope.
    /// This class is the foundation (phase Q1) of FamilyQuant becoming its own quantitation engine:
    /// it drives <see cref="DeNovoXicExtractor"/> - the SAME machinery that reproduces those stored
    /// quants bit for bit - over the identified peptides, at a configurable isotope count, so the
    /// envelope (M+0 + M+1 + ...) can be measured beside the monoisotopic number it will eventually
    /// replace.
    ///
    /// IT IS FAITHFUL BY CONSTRUCTION. At the default isotope count of 3 the M+0 area of every request
    /// is bit-identical to the stored quant (the same recipe the de novo gate proves), because it IS
    /// the same recipe: this class only supplies the request list and sums the answers.
    ///
    /// ONE RUN AT A TIME. The MS1 list of a 3 GB RAW dominates memory, so exactly one run is loaded at
    /// a time; <see cref="ExtractForRuns"/> processes several and collects between them. Runs are
    /// matched to RAWs BY FILE NAME through <see cref="DeNovoReconciler.NormalizeRunName"/>, never by
    /// index: a positional pairing extracts every precursor against the wrong acquisition and returns
    /// plausible wrong areas.
    ///
    /// Q1 stands alone as the engine. It does not touch the family quantitation or the three-way
    /// partition; wiring the envelope numbers into those is a later Q-phase.
    /// </summary>
    public static class IdentifiedQuantExtractor
    {
        /// <summary>
        /// Deduplicates a run's PSMs into extraction requests by (scan, m/z to 5 decimals, charge),
        /// the same key the extraction window is selected by, so two PSMs that would produce the same
        /// area share one request. The peptide sequence rides along as the tag.
        /// </summary>
        public static List<DeNovoXicRequest> BuildRequests(IEnumerable<PatternLabPsm> psms)
        {
            if (psms == null) throw new ArgumentNullException(nameof(psms));

            var seen = new Dictionary<string, DeNovoXicRequest>(StringComparer.Ordinal);
            foreach (PatternLabPsm p in psms)
            {
                double mz = Math.Round(p.PrecursorMz, DeNovoXicExtractor.MzDecimals);
                string key = DeNovoXicCache.KeyFor(p.ScanNumber, mz, p.ChargeState);
                if (!seen.ContainsKey(key))
                {
                    seen[key] = new DeNovoXicRequest
                    {
                        ScanNoMS2 = p.ScanNumber,
                        PrecursorMz = mz,
                        Charge = p.ChargeState,
                        Tag = p.Sequence,
                    };
                }
            }
            return seen.Values.ToList();
        }

        /// <summary>
        /// Extracts one run. <paramref name="psms"/> must already be that run's PSMs only; a PSM of
        /// another run is refused rather than extracted against this RAW, because a wrong-run
        /// extraction returns a plausible wrong area, which is worse than an exception. Results come
        /// back in request order, one per unique precursor, so a caller replaying stored quants can
        /// compare row for row.
        /// </summary>
        public static (List<DeNovoXicResult> results, IdentifiedQuantStats stats) ExtractForRun(
            string rawPath, IReadOnlyList<PatternLabPsm> psms, DeNovoXicOptions options,
            FamilyQuant.Operations.OperationContext? operation = null)
        {
            if (rawPath == null) throw new ArgumentNullException(nameof(rawPath));
            if (psms == null) throw new ArgumentNullException(nameof(psms));
            if (options == null) throw new ArgumentNullException(nameof(options));

            string run = DeNovoReconciler.NormalizeRunName(rawPath);
            PatternLabPsm? foreign = psms.FirstOrDefault(p =>
                !string.Equals(DeNovoReconciler.NormalizeRunName(p.FileName), run, StringComparison.OrdinalIgnoreCase));
            if (foreign != null)
            {
                throw new ArgumentException(
                    "A PSM of run \"" + foreign.FileName + "\" was handed to the extraction of run \"" +
                    run + "\". Runs are matched by file name; extracting an identified peptide against " +
                    "another run's RAW returns a plausible wrong area, which is worse than this exception.");
            }

            List<DeNovoXicRequest> requests = BuildRequests(psms);

            DateTime started = DateTime.UtcNow;
            List<DeNovoXicResult> results = DeNovoXicExtractor.ExtractRun(rawPath, requests, options, operation, "identified");
            double elapsed = (DateTime.UtcNow - started).TotalSeconds;

            var stats = new IdentifiedQuantStats
            {
                RunName = run,
                IsotopeCount = options.IsotopeCount,
                PsmsIn = psms.Count,
                UniquePrecursors = requests.Count,
                ElapsedSeconds = Math.Round(elapsed, 1),
            };

            foreach (DeNovoXicResult r in results)
            {
                if (r.Failure != null) stats.Failed++;
                else if (r.AreaM0 == DeNovoXicExtractor.UnderSampled) stats.UnderSampled++;
                else if (r.AreaM0 <= DeNovoXicExtractor.NoCluster) stats.NoCluster++;
                else stats.WithSignal++;
            }

            // The totals are over PEAKS, not anchors: two MS2 scans that fired inside one elution peak
            // get the same integrated area back, and summing per anchor would count that ion current
            // once per scan. The de novo side already solved this; reuse its collapse verbatim.
            double ppm = options.Ppm;
            List<DeNovoXicResult> peaks = DeNovoBatchExtractor.DistinctPeaks(results, ppm);

            stats.DistinctPeaks = peaks.Count;
            stats.TotalAreaM0 = peaks.Sum(p => p.AreaM0);
            stats.TotalEnvelope = peaks.Sum(p => p.EnvelopeSum);

            // The run's own TIC, no trim, so the fraction is taken over the same denominator the rest
            // of the project uses. Kept out of the try above deliberately: a TIC read failure is a real
            // failure of this measurement, not a per-precursor one to swallow.
            stats.TicArea = DeNovoXicExtractor.GetTotalIonCurrentArea(rawPath, 0, 0);

            return (results, stats);
        }

        /// <summary>
        /// Extracts several runs, one at a time, collecting between them so two 3 GB MS1 lists are
        /// never resident together. <paramref name="rawByRun"/> maps a normalized run name to its RAW
        /// path; PSMs are grouped by <see cref="DeNovoReconciler.NormalizeRunName"/> and each group is
        /// matched to its RAW BY NAME. A run with no RAW, or a RAW with no PSMs, is skipped rather than
        /// paired by position.
        /// </summary>
        public static List<(List<DeNovoXicResult> results, IdentifiedQuantStats stats)> ExtractForRuns(
            IReadOnlyDictionary<string, string> rawByRun,
            IEnumerable<PatternLabPsm> psms,
            Func<string, DeNovoXicOptions> optionsForRun)
        {
            if (rawByRun == null) throw new ArgumentNullException(nameof(rawByRun));
            if (psms == null) throw new ArgumentNullException(nameof(psms));
            if (optionsForRun == null) throw new ArgumentNullException(nameof(optionsForRun));

            var byRun = new Dictionary<string, List<PatternLabPsm>>(StringComparer.OrdinalIgnoreCase);
            foreach (PatternLabPsm p in psms)
            {
                string run = DeNovoReconciler.NormalizeRunName(p.FileName);
                if (!byRun.TryGetValue(run, out List<PatternLabPsm>? list))
                    byRun[run] = list = new List<PatternLabPsm>();
                list.Add(p);
            }

            var normalizedRaw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rawByRun)
                normalizedRaw[DeNovoReconciler.NormalizeRunName(kv.Key)] = kv.Value;

            var outputs = new List<(List<DeNovoXicResult>, IdentifiedQuantStats)>();

            foreach (var kv in byRun)
            {
                if (!normalizedRaw.TryGetValue(kv.Key, out string? rawPath)) continue;

                outputs.Add(ExtractForRun(rawPath, kv.Value, optionsForRun(kv.Key)));

                // The MS1 list of the run just finished is the biggest allocation in the process. Let
                // it go before the next run's is loaded, so peak memory is one run and not two.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            return outputs;
        }
    }
}
