using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// What one run's batch extraction did, for the status line and the report.
    ///
    /// THE COUNTS COME IN TWO GRAINS AND THE NAMES SAY WHICH. Everything up to
    /// <see cref="WithSignal"/> counts ANCHORS - one per (MS2 scan, m/z, charge) request. Everything
    /// from <see cref="DistinctPeaks"/> down counts, or sums over, PEAKS - one per extracted
    /// chromatographic peak. The two differ, because a precursor sampled by several MS2 scans inside
    /// one elution peak yields several anchors and one peak; see
    /// <see cref="DeNovoBatchExtractor.DistinctPeaks"/>.
    /// </summary>
    public class DeNovoBatchStats
    {
        public string RunName { get; set; } = string.Empty;

        /// <summary>De novo rows handed in.</summary>
        public int RowsIn { get; set; }

        /// <summary>Anchors extracted: rows after deduplication by (scan, m/z, charge).</summary>
        public int UniquePrecursors { get; set; }

        /// <summary>Anchors whose extraction returned a cluster. NOT a peak count: several of these
        /// can be the same peak.</summary>
        public int WithSignal { get; set; }

        public int NoCluster { get; set; }

        /// <summary>Anchors whose M+0 cluster was FOUND and refused by the minimum-points rule. A
        /// different fact from <see cref="NoCluster"/>: there was signal, and the quantitation rule
        /// declined to integrate it.</summary>
        public int UnderSampled { get; set; }

        public int Failed { get; set; }

        /// <summary>Distinct extracted chromatographic peaks, which is what
        /// <see cref="TotalAreaM0"/> sums over.</summary>
        public int DistinctPeaks { get; set; }

        /// <summary><see cref="WithSignal"/> less <see cref="DistinctPeaks"/>: anchors that landed in
        /// a peak another anchor had already produced. Summing them would have added one integrated
        /// area more than once.</summary>
        public int RedundantAnchors { get; set; }

        /// <summary>The ion current those redundant anchors would have added a second time. Reported
        /// rather than merely removed, because its size is the measure of how wrong the per-anchor
        /// sum was.</summary>
        public double RedundantAreaM0 { get; set; }

        /// <summary>Distinct peaks that still share a precursor window (same charge, m/z inside the
        /// extraction window) with a larger one. See <see cref="SharedWindowAreaM0"/>.</summary>
        public int SharedWindowPeaks { get; set; }

        /// <summary>
        /// THE BOUND ON WHAT MAY STILL BE COUNTED TWICE, and the reason it is a bound and not a
        /// correction. These are distinct integrations - the extractor found a different cluster at
        /// each anchor - that nevertheless sit in one precursor window. Two separate elution events of
        /// one precursor across a 240 min gradient look exactly like one broad peak the cluster walk
        /// entered twice, and nothing in an extracted area distinguishes them: that needs the peak's
        /// scan span, which this extraction does not record. So they are all kept, and the most that
        /// could be redundant among them (each window's total less its largest peak) is reported here.
        /// </summary>
        public double SharedWindowAreaM0 { get; set; }

        /// <summary>Sum of M+0 over the distinct peaks. The numerator of the de novo slice.</summary>
        public double TotalAreaM0 { get; set; }

        /// <summary>Sum of M+0 + M+1 + M+2 over the same distinct peaks, on the same grain as
        /// <see cref="TotalAreaM0"/>. A DIAGNOSTIC: it has no counterpart on the PatternLab side,
        /// whose stored quants are monoisotopic.</summary>
        public double TotalEnvelope { get; set; }

        public double ElapsedSeconds { get; set; }
    }

    /// <summary>
    /// Drives <see cref="DeNovoXicExtractor.ExtractRun"/> over the reconciled de novo rows of one
    /// run: everything the reconciliation kept (DeNovoOnly and CoIsolatedExtra) gets an XIC, one RAW
    /// load per run, one run at a time - a 3.2 GB RAW held as an MS1 list dominates memory, so two
    /// runs are never in flight together.
    ///
    /// Requests are deduplicated by (scan, m/z to 5 decimals, charge): about 2,500 spectra of the
    /// motivating export carry more than one de novo candidate, and the XIC belongs to the
    /// precursor, not to the candidate sequence. Every row keeps its area through the shared key.
    ///
    /// THAT DEDUPLICATION IS NOT ENOUGH TO SUM. It makes the requests unique; it does not make the
    /// ANSWERS unique, because two different anchor scans inside one elution peak are two requests
    /// and one peak, and the extractor returns that one peak to both. Anything that reports ion
    /// current therefore goes through <see cref="DistinctPeaks"/> first, and
    /// <see cref="DeNovoBatchStats"/> says of every count and every total which of the two grains it
    /// is on.
    /// </summary>
    public static class DeNovoBatchExtractor
    {
        public static List<DeNovoXicRequest> BuildRequests(IEnumerable<PeaksDeNovoRow> rows)
        {
            var seen = new Dictionary<string, DeNovoXicRequest>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                double mz = Math.Round(r.Mz, DeNovoXicExtractor.MzDecimals);
                string key = DeNovoXicCache.KeyFor(r.Scan, mz, r.Charge);
                if (!seen.ContainsKey(key))
                {
                    seen[key] = new DeNovoXicRequest
                    {
                        ScanNoMS2 = r.Scan,
                        PrecursorMz = mz,
                        Charge = r.Charge,
                        Tag = r.Peptide,
                    };
                }
            }
            return seen.Values.ToList();
        }

        /// <summary>
        /// Extracts one run. <paramref name="rows"/> must already be that run's rows only; the run
        /// name check is a guard, not a filter, because silently dropping rows that belong to
        /// another run would hide a caller bug.
        /// </summary>
        public static (List<DeNovoXicResult> results, DeNovoBatchStats stats) ExtractForRun(
            string rawPath, IReadOnlyList<PeaksDeNovoRow> rows, DeNovoXicOptions options)
        {
            string run = DeNovoReconciler.NormalizeRunName(rawPath);
            var foreignRow = rows.FirstOrDefault(r =>
                !string.Equals(DeNovoReconciler.NormalizeRunName(r.RunName), run, StringComparison.OrdinalIgnoreCase));
            if (foreignRow != null)
            {
                throw new ArgumentException(
                    "A row of run \"" + foreignRow.RunName + "\" was handed to the extraction of run \"" +
                    run + "\". Runs are matched by file name; extracting a precursor against another " +
                    "run's RAW returns a plausible wrong area, which is worse than this exception.");
            }

            var requests = BuildRequests(rows);

            var started = DateTime.UtcNow;
            var results = DeNovoXicExtractor.ExtractRun(rawPath, requests, options);
            double elapsed = (DateTime.UtcNow - started).TotalSeconds;

            var stats = new DeNovoBatchStats
            {
                RunName = run,
                RowsIn = rows.Count,
                UniquePrecursors = requests.Count,
                ElapsedSeconds = Math.Round(elapsed, 1),
            };

            double perAnchorAreaM0 = 0;
            foreach (var r in results)
            {
                if (r.Failure != null) stats.Failed++;
                else if (r.AreaM0 == DeNovoXicExtractor.UnderSampled) stats.UnderSampled++;
                else if (r.AreaM0 <= DeNovoXicExtractor.NoCluster) stats.NoCluster++;
                else
                {
                    stats.WithSignal++;
                    perAnchorAreaM0 += r.AreaM0;
                }
            }

            // The totals are over PEAKS, not over anchors. Summing per anchor would add one integrated
            // area once per MS2 scan that happened to fire inside it, which is not the ion current the
            // de novo set accounts for and is not the grain the PatternLab side is summed on.
            double ppm = options?.Ppm ?? DeNovoXicExtractor.ProductionPpm;
            List<DeNovoXicResult> peaks = DistinctPeaks(results, ppm);

            stats.DistinctPeaks = peaks.Count;
            stats.RedundantAnchors = stats.WithSignal - peaks.Count;
            stats.TotalAreaM0 = peaks.Sum(p => p.AreaM0);
            stats.TotalEnvelope = peaks.Sum(p => p.EnvelopeSum);
            stats.RedundantAreaM0 = perAnchorAreaM0 - stats.TotalAreaM0;

            var (sharedPeaks, sharedArea) = SharedWindowResidual(peaks, ppm);
            stats.SharedWindowPeaks = sharedPeaks;
            stats.SharedWindowAreaM0 = sharedArea;

            return (results, stats);
        }

        /// <summary>
        /// One entry per extracted chromatographic PEAK, from a list that holds one entry per MS2
        /// ANCHOR.
        ///
        /// WHY THE TWO ARE NOT THE SAME. A precursor sampled by several MS2 scans inside one elution
        /// peak produces several requests, and XICGet5.GetXIC4 answers every one of them
        /// with the SAME peak: it finds the MS1 cluster present at the anchor scan and walks outwards
        /// from it, so two anchors inside one cluster get one and the same integrated trace back.
        /// Adding them all up counts that trace once per MS2 event, which inflates the total by a few
        /// percent on a real export and does it silently, never going negative and never failing a
        /// closure check.
        ///
        /// THE SIGNATURE OF ONE PEAK RETURNED TWICE is a BIT-IDENTICAL <see cref="DeNovoXicResult.AreaM0"/>
        /// at the same charge inside one extraction window. It is bit-identical because it is not a
        /// second measurement of the same thing: it is the same trapezoid over the same trace, so the
        /// same double comes back. Two genuinely different integrations of nine to eleven significant
        /// digits do not agree to the last bit.
        ///
        /// WHAT THIS DELIBERATELY DOES NOT COLLAPSE: two anchors at one m/z whose extractions returned
        /// DIFFERENT areas. Those are different clusters by the extractor's own reckoning. Whether
        /// they are two elution events of one precursor or one broad peak the cluster walk entered
        /// twice cannot be settled from an area, and guessing either way would be a fabrication - so
        /// both are kept and the size of the question is reported instead, by
        /// <see cref="SharedWindowResidual"/>.
        ///
        /// Rejected and empty extractions are dropped: <see cref="DeNovoXicExtractor.NoCluster"/> is
        /// not a peak, and it is not ion current.
        ///
        /// The representative of a peak is its LOWEST anchor scan, the first MS2 event that sampled
        /// it. Deterministic, and it does not pick the member with the largest isotope envelope, which
        /// would bias <see cref="DeNovoBatchStats.TotalEnvelope"/> upwards.
        /// </summary>
        public static List<DeNovoXicResult> DistinctPeaks(IEnumerable<DeNovoXicResult> results, double ppm)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (!(ppm > 0))
                throw new ArgumentException("A positive ppm window is required to tell two precursors apart.", nameof(ppm));

            // Grouped on the exact bits of the area, so "the same peak" is an exact test and not a
            // tolerance stacked on a tolerance.
            var byAreaAndCharge = new Dictionary<string, List<DeNovoXicResult>>(StringComparer.Ordinal);

            foreach (DeNovoXicResult r in results)
            {
                if (r == null || r.Failure != null || r.AreaM0 <= DeNovoXicExtractor.NoCluster) continue;

                string key = r.Charge.ToString(CultureInfo.InvariantCulture) + "|" +
                             BitConverter.DoubleToInt64Bits(r.AreaM0).ToString(CultureInfo.InvariantCulture);

                if (!byAreaAndCharge.TryGetValue(key, out List<DeNovoXicResult>? bucket))
                    byAreaAndCharge[key] = bucket = new List<DeNovoXicResult>();

                bucket.Add(r);
            }

            var kept = new List<DeNovoXicResult>();

            foreach (var bucket in byAreaAndCharge.Values)
            {
                foreach (var window in WindowsOf(bucket, ppm))
                {
                    DeNovoXicResult representative = window[0];
                    for (int i = 1; i < window.Count; i++)
                        if (window[i].ScanNoMS2 < representative.ScanNoMS2) representative = window[i];

                    kept.Add(representative);
                }
            }

            // A total order that does not depend on dictionary iteration, so the sum below is
            // reproducible bit for bit from one process to the next.
            kept.Sort((a, b) =>
            {
                int c = a.Charge.CompareTo(b.Charge);
                if (c != 0) return c;
                c = a.PrecursorMz.CompareTo(b.PrecursorMz);
                if (c != 0) return c;
                return a.ScanNoMS2.CompareTo(b.ScanNoMS2);
            });

            return kept;
        }

        /// <summary>
        /// How much of a set of distinct peaks could still be one ion current counted more than once:
        /// the peaks that share a precursor window (same charge, m/z inside <paramref name="ppm"/>)
        /// with a larger one, and the area they carry.
        ///
        /// It is a BOUND, not a correction, and nothing subtracts it. See
        /// <see cref="DeNovoBatchStats.SharedWindowAreaM0"/> for why it cannot be settled here.
        /// Reporting it is what keeps the residual from being silent.
        /// </summary>
        public static (int peaks, double area) SharedWindowResidual(
            IEnumerable<DeNovoXicResult> distinctPeaks, double ppm)
        {
            if (distinctPeaks == null) throw new ArgumentNullException(nameof(distinctPeaks));
            if (!(ppm > 0))
                throw new ArgumentException("A positive ppm window is required.", nameof(ppm));

            var byCharge = new Dictionary<int, List<DeNovoXicResult>>();
            foreach (DeNovoXicResult p in distinctPeaks)
            {
                if (p == null) continue;
                if (!byCharge.TryGetValue(p.Charge, out List<DeNovoXicResult>? bucket))
                    byCharge[p.Charge] = bucket = new List<DeNovoXicResult>();
                bucket.Add(p);
            }

            int count = 0;
            double area = 0;

            foreach (var bucket in byCharge.Values)
            {
                foreach (var window in WindowsOf(bucket, ppm))
                {
                    if (window.Count < 2) continue;
                    count += window.Count - 1;
                    area += window.Sum(p => p.AreaM0) - window.Max(p => p.AreaM0);
                }
            }

            return (count, area);
        }

        /// <summary>
        /// Splits results into precursor windows: sorted by m/z, a new window opens as soon as an
        /// entry is further than <paramref name="ppm"/> from the m/z that opened the current one.
        ///
        /// The comparison is against the window's OPENING m/z, never against the previous entry, so a
        /// dense ladder of near neighbours cannot chain an arbitrarily wide window into existence.
        /// </summary>
        private static List<List<DeNovoXicResult>> WindowsOf(List<DeNovoXicResult> entries, double ppm)
        {
            entries.Sort((a, b) =>
            {
                int c = a.PrecursorMz.CompareTo(b.PrecursorMz);
                return c != 0 ? c : a.ScanNoMS2.CompareTo(b.ScanNoMS2);
            });

            var windows = new List<List<DeNovoXicResult>>();
            List<DeNovoXicResult>? current = null;
            double opening = 0;

            foreach (DeNovoXicResult e in entries)
            {
                if (current == null || opening <= 0 ||
                    Math.Abs(e.PrecursorMz - opening) / opening * 1e6 > ppm)
                {
                    current = new List<DeNovoXicResult>();
                    windows.Add(current);
                    opening = e.PrecursorMz;
                }

                current.Add(e);
            }

            return windows;
        }

        /// <summary>
        /// Pearson correlation of log10 areas between PEAKS' own feature area and this module's M+0,
        /// over rows where both are positive. A DIAGNOSTIC: the two come from different detectors on
        /// different bases (PEAKS integrates the feature envelope; this is the monoisotopic XIC), so
        /// they must correlate but must never substitute for one another. Returns the pair count and
        /// the coefficient, or 0 pairs when there is nothing to correlate.
        /// </summary>
        public static (int pairs, double r) PeaksAreaCorrelation(
            IEnumerable<PeaksDeNovoRow> rows, IReadOnlyList<DeNovoXicResult> results)
        {
            var byKey = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var res in results)
            {
                if (res.Failure == null && res.AreaM0 > 0)
                    byKey[DeNovoXicCache.KeyFor(res.ScanNoMS2, res.PrecursorMz, res.Charge)] = res.AreaM0;
            }

            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var row in rows)
            {
                if (row.PeaksArea <= 0) continue;
                string key = DeNovoXicCache.KeyFor(row.Scan, Math.Round(row.Mz, DeNovoXicExtractor.MzDecimals), row.Charge);
                if (byKey.TryGetValue(key, out double m0))
                {
                    xs.Add(Math.Log10(row.PeaksArea));
                    ys.Add(Math.Log10(m0));
                }
            }

            int n = xs.Count;
            if (n < 3) return (n, 0);

            double mx = xs.Average(), my = ys.Average();
            double sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = xs[i] - mx, dy = ys[i] - my;
                sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
            }

            return sxx <= 0 || syy <= 0 ? (n, 0) : (n, sxy / Math.Sqrt(sxx * syy));
        }
    }
}
