using System.Text.Json;
using System.Text.Json.Serialization;
using PatternTools.MSParserLight;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// One run's resolved isotope-series diagnostic, detected straight from the MS1 map.
    ///
    /// WHAT THIS NUMBER IS, IN ONE HONEST SENTENCE: an estimate of ion current in resolved isotope
    /// series. It is not a peptide identification, has no peptide FDR, and does not partition the TIC.
    /// Differences between it, assigned estimates, and the TIC are not named as biological classes.
    ///
    /// AND IT IS SPLIT BY CHARGE, BECAUSE THE EVIDENCE IS NOT THE SAME AT EVERY CHARGE. At charge 1 the
    /// isotope spacing is 1.00335 Da, and "has a C13 isotope one dalton up" is true of essentially any
    /// organic molecule, so a singly charged isotope series is less specific. At charge
    /// 2 or more the spacing is 0.5 or 0.33 Da, which a singly charged small molecule cannot produce, so
    /// it is more specific for a multiply charged ion. Neither subset is peptide-FDR calibrated, so
    /// <see cref="Ms1EnvelopeDetectionResult"/> carries both parts and every surface names them apart.
    ///
    /// THE SINGLY CHARGED PART IS NOT "NOT A PEPTIDE". In venom the natural peptides - the
    /// bradykinin-potentiating peptides, the angiotensin-like fragments - are frequently singly charged,
    /// and this dataset contains identified charge-1 peptides. The honest statement is that the evidence
    /// is weaker, never that the signal is something else.
    /// </summary>
    public sealed class Ms1EnvelopeDetectionResult
    {
        /// <summary>The run this was detected from, normalized by
        /// <see cref="DeNovoReconciler.NormalizeRunName"/>.</summary>
        public string RunName { get; set; } = string.Empty;

        /// <summary>How many isotopic envelopes were tracked into RT features and integrated. One
        /// elution feature counts once, however many MS1 scans it spanned. Always the sum of
        /// <see cref="DetectedCountByCharge"/>.</summary>
        public int DetectedEnvelopeCount { get; set; }

        /// <summary>The total resolved isotope-series diagnostic area, on the SAME intensity-vs-retention-
        /// time-minutes trapezoid basis as the run TIC and the peptide XICs. See the type
        /// remarks. EXACTLY <see cref="DetectedAreaSinglyCharged"/> plus
        /// <see cref="DetectedAreaMultiplyCharged"/>, because the detector adds those two to make it.</summary>
        public double DetectedPeptideLikeArea { get; set; }

        /// <summary>How many MS1 scans were walked. A cheap sanity value for the status line.</summary>
        public int Ms1ScanCount { get; set; }

        /// <summary>
        /// The detected area per charge, indexed BY CHARGE: entry z is the integrated area of the features
        /// read at charge z, for z in <see cref="Ms1EnvelopeDetector.MinCharge"/> ..
        /// <see cref="Ms1EnvelopeDetector.MaxCharge"/>. Index 0 is unused and always zero, so a reader
        /// never has to remember an offset. Length is always MaxCharge + 1.
        /// </summary>
        public double[] DetectedAreaByCharge { get; set; } = NewChargeArray<double>();

        /// <summary>The feature count per charge, indexed BY CHARGE exactly like
        /// <see cref="DetectedAreaByCharge"/>.</summary>
        public int[] DetectedCountByCharge { get; set; } = NewChargeArray<int>();

        /// <summary>The multiply charged part (z &gt;= 2), more specific than z=1 but not peptide-FDR
        /// calibrated.</summary>
        public double DetectedAreaMultiplyCharged { get; set; }

        /// <summary>The SINGLY CHARGED part (z == 1): a real isotope envelope, but WEAKER evidence, since
        /// at charge 1 a C13 isotope is true of essentially any organic molecule. NOT a claim that the
        /// signal is not a peptide; see the type remarks.</summary>
        public double DetectedAreaSinglyCharged { get; set; }

        /// <summary>How many features carried the multiply charged (z &gt;= 2) part.</summary>
        public int DetectedCountMultiplyCharged { get; set; }

        /// <summary>How many features carried the singly charged (z == 1) part.</summary>
        public int DetectedCountSinglyCharged { get; set; }

        /// <summary>A zeroed per-charge array of the right length, so every producer and consumer agrees
        /// on the indexing without repeating the arithmetic.</summary>
        public static T[] NewChargeArray<T>() => new T[Ms1EnvelopeDetector.MaxCharge + 1];
    }

    /// <summary>
    /// Detects isotopic envelopes in a run's MS1 map and integrates their ion current over retention time
    /// as an independent resolved isotope-series diagnostic (see <see cref="Ms1EnvelopeDetectionResult"/>).
    ///
    /// IT IS A DOCUMENTED DIAGNOSTIC, NOT A PEPTIDE CLASSIFIER. Every knob is a named
    /// constant on this class, and the method is stated in one sentence the paper can quote:
    /// "ion current in resolved isotope series detected in the MS1 map." Its output is always presented
    /// and stored as a non-FDR-calibrated diagnostic.
    ///
    /// THE ALGORITHM, STEP BY STEP.
    ///   1. Load the MS1 spectra once, through <see cref="DeNovoXicExtractor.LoadMs1Spectra"/>, with the
    ///      production parse arguments - the same peaks the rest of the pipeline sees.
    ///   2. Within each MS1 scan, treat the centroided ions as peaks (sorted by m/z) and group them into
    ///      isotopic envelopes: an anchor peak plus consecutive isotopes at spacing
    ///      <see cref="DeNovoXicExtractor.Carbon13Spacing"/>/z for a plausible charge z in
    ///      {<see cref="MinCharge"/>..<see cref="MaxCharge"/>}, matched within <see cref="PpmTolerance"/>,
    ///      up to the <see cref="IsotopeCeiling"/>-peak ceiling, with an averagine-like / monotone-after-
    ///      apex shape (intensities may rise to an apex then fall, never rise again after falling). The
    ///      charge that explains the MOST isotopes wins, ties broken toward the HIGHER charge. A peak
    ///      grouped into an envelope is consumed, so within one scan the envelopes are disjoint and a
    ///      lone peak with no isotope partner is not counted by the diagnostic.
    ///   3. Track a detected envelope across ADJACENT MS1 scans (same charge, m/z within
    ///      <see cref="PpmTolerance"/>) into one RT feature, and integrate its envelope intensity over
    ///      retention time with the SAME trapezoid the peptide XICs use.
    ///   4. A feature must span at least <see cref="MinScansPerFeature"/> scans to be counted, so a
    ///      one-scan blip is not integrated and every area carries consistent intensity-times-minutes
    ///      units. Features are disjoint (each scan-envelope joins at most one feature), which is what
    ///      deduplicates overlapping detections.
    ///
    /// WHY THE ORDERING IT PRODUCES IS SAFE (detected &lt;= TIC). A feature integrates only between
    /// ADJACENT MS1 scans in which its envelope was seen, over the envelope's member-peak intensities,
    /// which are a SUBSET of that scan's peaks. So over any retention-time interval between two adjacent
    /// MS1 scans, the detected intensity summed over features never exceeds that scan's total peak
    /// intensity, which never exceeds the scan's recorded TIC. The trapezoid is monotone on a shared
    /// retention-time grid, so the integrated detected area cannot exceed the integrated TIC. That the
    /// detected level also exceeds the assigned level is expected but NOT guaranteed by arithmetic; it is
    /// asserted as a gate on real data (see the Q6 tests), because a detected value below the assigned
    /// one would be a bug in this detector, not a number to ship.
    ///
    /// AFFORDABLE, AND CACHED. It walks the MS1 map ONCE per run and holds one run at a time, exactly
    /// like <see cref="DeNovoXicExtractor.ExtractRun"/>. The per-run result (a count and an area, both
    /// tiny) is cached to a JSON sidecar keyed by the run name and the detection parameters, so a second
    /// session does not re-open the RAW.
    /// </summary>
    public static class Ms1EnvelopeDetector
    {
        /// <summary>The one-sentence method, for a manifest note or a caption.</summary>
        public const string MethodSentence =
            "a resolved isotope-series estimate from the MS1 map; it is not peptide-FDR calibrated.";

        /// <summary>What the z &gt;= 2 part means, in the words every surface has to use.</summary>
        public const string StrongEvidenceLabel =
            "multiply charged resolved isotope series (more specific, not peptide-FDR calibrated)";

        /// <summary>What the z == 1 part means, in the words every surface has to use. It says WEAKER, not
        /// "not a peptide"; see <see cref="SinglyChargedCaveat"/>.</summary>
        public const string WeakEvidenceLabel =
            "singly charged resolved isotope series (less specific, not peptide-FDR calibrated)";

        /// <summary>Why the singly charged part is never called non-peptide. Quoted on the surfaces that
        /// have room for it.</summary>
        public const string SinglyChargedCaveat =
            "Weaker evidence is not a claim of non-peptide: natural peptides are frequently singly " +
            "charged, and identified charge-1 peptides demonstrate that charge alone cannot rule out peptides.";

        /// <summary>The charge at and above which an isotope spacing is strong evidence of a peptide: 2,
        /// where the spacing is 0.5 Da and a singly charged small molecule cannot produce it.</summary>
        public const int MultiplyChargedFloor = 2;

        /// <summary>The one charge whose isotope envelope is weak evidence: 1, where "has a C13 isotope"
        /// is true of essentially any organic molecule.</summary>
        public const int SinglyCharged = 1;

        // ---- parameters, every one a named constant on purpose ----------------------------------

        /// <summary>Mass tolerance, in ppm, for both the isotope spacing within a scan and the m/z match
        /// that links an envelope across adjacent scans. 10 ppm, the production extraction window.</summary>
        public const double PpmTolerance = 10.0;

        /// <summary>Lowest charge an envelope may be assigned. 1: a peptide-like envelope is multi-isotope
        /// at charge &gt;= 1.</summary>
        public const int MinCharge = 1;

        /// <summary>Highest charge an envelope may be assigned.</summary>
        public const int MaxCharge = 5;

        /// <summary>The isotope ceiling: at most this many peaks in one envelope (M+0 .. M+3). A ceiling,
        /// not a floor.</summary>
        public const int IsotopeCeiling = 4;

        /// <summary>The minimum isotopes for a group to count as an envelope. 2: a single peak with no
        /// isotope partner is not peptide-like.</summary>
        public const int MinIsotopes = 3;

        /// <summary>The minimum number of adjacent MS1 scans a feature must span to be counted and
        /// integrated. 2: a one-scan detection is not a credible chromatographic feature, and integrating
        /// a single point would mix intensity with intensity-times-minutes.</summary>
        public const int MinScansPerFeature = 2;

        /// <summary>The C13-C12 spacing, from the extractor so the two never drift.</summary>
        public const double Carbon13Spacing = DeNovoXicExtractor.Carbon13Spacing;

        /// <summary>
        /// Detects and integrates the peptide-like envelopes of one RAW. Loads the MS1 map once (unless
        /// the cache already answers), walks it, and returns the count and the integrated area.
        ///
        /// <paramref name="cachePath"/> is the JSON sidecar to read and write, or null for no cache. A
        /// cache written for a different run or different detection parameters is discarded, never mixed.
        /// </summary>
        public static Ms1EnvelopeDetectionResult DetectRun(
            string rawPath, string? cachePath = null, Action<string>? progress = null,
            string? sourceFingerprint = null, string? legacyCachePath = null,
            FamilyQuant.Operations.OperationContext? operation = null)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                throw new ArgumentException("A RAW file path is required.", nameof(rawPath));

            string runName = DeNovoReconciler.NormalizeRunName(rawPath);
            operation?.Report("detector-cache:" + runName, "Checking resolved isotope-series cache: " + runName, 0, 1);
            string fingerprint = string.IsNullOrWhiteSpace(sourceFingerprint)
                ? FileFingerprint.Sha256(rawPath) : sourceFingerprint;

            if (cachePath != null && TryLoadCache(cachePath, runName, fingerprint,
                    out Ms1EnvelopeDetectionResult? cached)
                && cached != null)
            {
                progress?.Invoke(runName + ": resolved isotope-series diagnostic answered from the cache (" +
                    cached.DetectedEnvelopeCount.ToString("N0", CultureInfo.InvariantCulture) + " envelope(s)).");
                operation?.Report("detector-cache:" + runName, "Resolved isotope-series diagnostic read from the cache: " + runName, 1, 1);
                return cached;
            }
            if (legacyCachePath != null && TryLoadCache(legacyCachePath, runName, fingerprint, out var legacy) && legacy != null)
            {
                progress?.Invoke(runName + ": resolved isotope-series diagnostic answered from the legacy cache (read only).");
                operation?.Report("detector-cache:" + runName, "Resolved isotope-series diagnostic read from the legacy cache: " + runName, 1, 1);
                return legacy;
            }

            operation?.Report("detector-cache:" + runName, "No cached detector result; loading acquisition: " + runName, 1, 1);
            progress?.Invoke(runName + ": loading MS1 spectra to detect resolved isotope series...");
            List<MSUltraLight> spectra = DeNovoXicExtractor.LoadMs1Spectra(rawPath);

            Ms1EnvelopeDetectionResult result = Detect(runName, spectra, operation);
            progress?.Invoke(runName + ": detected " +
                result.DetectedEnvelopeCount.ToString("N0", CultureInfo.InvariantCulture) +
                " resolved isotope-series feature(s) over " +
                result.Ms1ScanCount.ToString("N0", CultureInfo.InvariantCulture) + " MS1 scan(s) - " +
                result.DetectedCountMultiplyCharged.ToString("N0", CultureInfo.InvariantCulture) +
                " " + StrongEvidenceLabel + ", " +
                result.DetectedCountSinglyCharged.ToString("N0", CultureInfo.InvariantCulture) +
                " " + WeakEvidenceLabel + ".");

            if (cachePath != null) SaveCache(cachePath, result, fingerprint);

            return result;
        }

        /// <summary>
        /// The pure detector: given a run's MS1 spectra already in memory, detect and integrate its
        /// peptide-like envelopes. Separated from the RAW load so it is exercised on synthetic spectra
        /// without a file. Deterministic: the same spectra in yield the same numbers out.
        /// </summary>
        public static Ms1EnvelopeDetectionResult Detect(string runName, IReadOnlyList<MSUltraLight> spectra,
            FamilyQuant.Operations.OperationContext? operation = null)
        {
            if (spectra == null) throw new ArgumentNullException(nameof(spectra));

            // MS1 only, in scan order, which is retention-time order for an LC run. Adjacency below is
            // by position in THIS ordered list.
            var scans = spectra
                .Where(s => s != null && s.MSLevel <= 1 && s.Ions != null && s.Ions.Count > 0)
                .OrderBy(s => s.ScanNumber)
                .ToList();

            var openFeatures = new List<Feature>();
            var closedFeatures = new List<Feature>();
            operation?.Report("detector-scans:" + runName, "Detecting resolved isotope series: " + runName, 0, scans.Count);

            for (int i = 0; i < scans.Count; i++)
            {
                operation?.ThrowIfCancellationRequested();
                MSUltraLight scan = scans[i];
                double rt = scan.CromatographyRetentionTime;

                List<ScanEnvelope> envelopes = DetectInScan(scan);

                // Match this scan's envelopes to features seen in the IMMEDIATELY PREVIOUS scan (index
                // i-1). A feature not matched this scan is closed, so a feature's samples are always a run
                // of consecutive scan indices - which is what makes the trapezoid never bridge an absent
                // scan and keeps the detected-area <= TIC guarantee exact. Only the features that existed
                // BEFORE this scan are match candidates (originalCount): a fresh feature opened for an
                // earlier envelope of this same scan must never absorb a later one of the same scan.
                int originalCount = openFeatures.Count;
                var used = new bool[originalCount];

                foreach (ScanEnvelope env in envelopes)
                {
                    int bestF = -1;
                    double bestPpm = PpmTolerance;
                    for (int f = 0; f < originalCount; f++)
                    {
                        if (used[f]) continue;
                        Feature feat = openFeatures[f];
                        if (feat.Charge != env.Charge) continue;
                        double d = PpmDiff(env.MonoMz, feat.Mz);
                        if (d <= bestPpm)
                        {
                            bestPpm = d;
                            bestF = f;
                        }
                    }

                    if (bestF >= 0)
                    {
                        Feature feat = openFeatures[bestF];
                        feat.Add(i, rt, env.Intensity, env.MonoMz);
                        used[bestF] = true;
                    }
                    else
                    {
                        var fresh = new Feature(env.Charge);
                        fresh.Add(i, rt, env.Intensity, env.MonoMz);
                        openFeatures.Add(fresh);
                    }
                }

                // Close every feature not matched in this scan; the next scan can only continue features
                // that reached it, so adjacency holds.
                var stillOpen = new List<Feature>(openFeatures.Count);
                for (int f = 0; f < openFeatures.Count; f++)
                {
                    if (f < used.Length && used[f]) stillOpen.Add(openFeatures[f]);
                    else if (openFeatures[f].LastScanIndex == i) stillOpen.Add(openFeatures[f]); // opened this scan
                    else closedFeatures.Add(openFeatures[f]);
                }
                openFeatures = stillOpen;
                if ((i + 1) % 128 == 0 || i + 1 == scans.Count)
                    operation?.Report("detector-scans:" + runName, "Detector MS1 scans " + (i + 1).ToString(CultureInfo.InvariantCulture)
                        + " of " + scans.Count.ToString(CultureInfo.InvariantCulture) + ": " + runName, i + 1, scans.Count);
            }

            closedFeatures.AddRange(openFeatures);

            // Accumulated PER CHARGE and never as one running total, so the two evidence groups and the
            // grand total cannot disagree: the total below is literally the sum of the parts, which makes
            // "detected = z>=2 part + z=1 part" an identity in floating point and not a near-equality that
            // a test has to forgive. A charge outside the configured range cannot occur (ChargeOf clamps),
            // but it is dropped rather than trusted into an index.
            double[] areaByCharge = Ms1EnvelopeDetectionResult.NewChargeArray<double>();
            int[] countByCharge = Ms1EnvelopeDetectionResult.NewChargeArray<int>();

            foreach (Feature feat in closedFeatures)
            {
                if (feat.ScanCount < MinScansPerFeature) continue;   // a one-scan blip is not a feature
                int z = feat.Charge;
                if (z < MinCharge || z > MaxCharge) continue;
                countByCharge[z]++;
                areaByCharge[z] += feat.Area();
            }

            // z >= 2 first, left to right, then charge 1 added to it: the same grouping the two exposed
            // parts use, so total == single + multi holds bit for bit.
            double multiArea = 0.0;
            int multiCount = 0;
            for (int z = MultiplyChargedFloor; z <= MaxCharge; z++)
            {
                multiArea += areaByCharge[z];
                multiCount += countByCharge[z];
            }

            double singleArea = SinglyCharged >= MinCharge && SinglyCharged <= MaxCharge
                ? areaByCharge[SinglyCharged] : 0.0;
            int singleCount = SinglyCharged >= MinCharge && SinglyCharged <= MaxCharge
                ? countByCharge[SinglyCharged] : 0;

            return new Ms1EnvelopeDetectionResult
            {
                RunName = runName,
                DetectedEnvelopeCount = singleCount + multiCount,
                DetectedPeptideLikeArea = singleArea + multiArea,
                Ms1ScanCount = scans.Count,
                DetectedAreaByCharge = areaByCharge,
                DetectedCountByCharge = countByCharge,
                DetectedAreaMultiplyCharged = multiArea,
                DetectedAreaSinglyCharged = singleArea,
                DetectedCountMultiplyCharged = multiCount,
                DetectedCountSinglyCharged = singleCount,
            };
        }

        /// <summary>
        /// The envelopes of one MS1 scan: the centroided ions grouped into disjoint isotopic series. A
        /// peak already claimed by an envelope is consumed, so a lone peak with no isotope partner is
        /// never returned as an envelope.
        /// </summary>
        public static List<ScanEnvelope> DetectInScan(MSUltraLight scan)
        {
            var result = new List<ScanEnvelope>();
            if (scan?.Ions == null || scan.Ions.Count == 0) return result;

            int n = scan.Ions.Count;
            var mz = new double[n];
            var intensity = new double[n];
            // Sort a copy by m/z; the source list order is not guaranteed and binary search needs it sorted.
            var order = Enumerable.Range(0, n).OrderBy(k => scan.Ions[k].MZ).ToArray();
            for (int k = 0; k < n; k++)
            {
                mz[k] = scan.Ions[order[k]].MZ;
                intensity[k] = scan.Ions[order[k]].Intensity;
            }

            var consumed = new bool[n];

            for (int a = 0; a < n; a++)
            {
                if (consumed[a]) continue;

                // The charge that explains the most isotopes wins; ties go to the HIGHER charge, so the
                // loop runs high-to-low and keeps the first maximum.
                List<int>? bestChain = null;
                for (int z = MaxCharge; z >= MinCharge; z--)
                {
                    List<int> chain = BuildChain(mz, intensity, consumed, a, z);
                    if (chain.Count >= MinIsotopes && (bestChain == null || chain.Count > bestChain.Count))
                        bestChain = chain;
                }

                if (bestChain == null) continue;   // no isotope partner: not peptide-like, leave uncounted

                double envInt = 0.0;
                foreach (int idx in bestChain)
                {
                    envInt += intensity[idx];
                    consumed[idx] = true;
                }

                // The winning charge is recoverable from the anchor spacing, but carry it explicitly.
                int charge = ChargeOf(mz, bestChain);
                result.Add(new ScanEnvelope(charge, mz[a], envInt, bestChain.Count));
            }

            return result;
        }

        /// <summary>
        /// The isotope chain from anchor <paramref name="a"/> at charge <paramref name="z"/>: the anchor
        /// plus consecutive isotopes at anchor.mz + iso*(spacing/z), each within <see cref="PpmTolerance"/>,
        /// up to the ceiling, with a monotone-after-apex shape. Returns the member indices (at least the
        /// anchor).
        /// </summary>
        private static List<int> BuildChain(double[] mz, double[] intensity, bool[] consumed, int a, int z)
        {
            var members = new List<int> { a };
            double step = Carbon13Spacing / z;
            double lastInt = intensity[a];
            bool falling = false;

            for (int iso = 1; iso < IsotopeCeiling; iso++)
            {
                double expected = mz[a] + iso * step;
                int j = NearestWithinPpm(mz, expected, PpmTolerance);
                if (j < 0 || consumed[j] || members.Contains(j)) break;

                double inten = intensity[j];
                if (inten > lastInt)
                {
                    if (falling) break;   // rose again after the apex: not one averagine-like envelope
                }
                else if (inten < lastInt)
                {
                    falling = true;
                }

                members.Add(j);
                lastInt = inten;
            }

            return members;
        }

        /// <summary>The charge a chain implies, from the anchor-to-next spacing. Robust to the exact
        /// isotope by rounding <c>Carbon13Spacing / observed-gap</c> to the nearest integer.</summary>
        private static int ChargeOf(double[] mz, List<int> chain)
        {
            if (chain.Count < 2) return 1;
            double gap = Math.Abs(mz[chain[1]] - mz[chain[0]]);
            if (gap <= 0) return 1;
            int z = (int)Math.Round(Carbon13Spacing / gap);
            if (z < MinCharge) z = MinCharge;
            if (z > MaxCharge) z = MaxCharge;
            return z;
        }

        /// <summary>The index of the peak nearest <paramref name="target"/> whose ppm distance is within
        /// <paramref name="ppm"/>, or -1. Binary search over the m/z-sorted array, checking the two
        /// neighbours of the insertion point.</summary>
        private static int NearestWithinPpm(double[] sortedMz, double target, double ppm)
        {
            if (sortedMz.Length == 0) return -1;

            int lo = Array.BinarySearch(sortedMz, target);
            if (lo >= 0) return lo;   // exact hit
            int ins = ~lo;

            int best = -1;
            double bestPpm = ppm;
            for (int k = ins - 1; k <= ins; k++)
            {
                if (k < 0 || k >= sortedMz.Length) continue;
                double d = PpmDiff(sortedMz[k], target);
                if (d <= bestPpm)
                {
                    bestPpm = d;
                    best = k;
                }
            }
            return best;
        }

        private static double PpmDiff(double a, double b)
        {
            if (b <= 0) return double.MaxValue;
            return Math.Abs(a - b) / b * 1e6;
        }

        // ---- the cross-scan feature ------------------------------------------------------------

        /// <summary>One detected envelope inside one MS1 scan: its charge, its monoisotopic m/z, the
        /// summed intensity of its member peaks, and how many isotopes it carried.</summary>
        public readonly struct ScanEnvelope
        {
            public ScanEnvelope(int charge, double monoMz, double intensity, int isotopes)
            {
                Charge = charge;
                MonoMz = monoMz;
                Intensity = intensity;
                Isotopes = isotopes;
            }

            public int Charge { get; }
            public double MonoMz { get; }
            public double Intensity { get; }
            public int Isotopes { get; }
        }

        /// <summary>An RT feature: one envelope tracked across adjacent scans. Integrated over its own
        /// samples with the same trapezoid the peptide XICs use.</summary>
        private sealed class Feature
        {
            private readonly List<int> _scanIdx = new List<int>();
            private readonly List<double> _rt = new List<double>();
            private readonly List<double> _intensity = new List<double>();

            public Feature(int charge) { Charge = charge; }

            public int Charge { get; }
            public double Mz { get; private set; }
            public int LastScanIndex { get; private set; } = -1;
            public int ScanCount => _scanIdx.Count;

            public void Add(int scanIndex, double rt, double intensity, double mz)
            {
                _scanIdx.Add(scanIndex);
                _rt.Add(rt);
                _intensity.Add(intensity);
                Mz = mz;                 // track the m/z forward so drift over the peak is followed
                LastScanIndex = scanIndex;
            }

            /// <summary>The integrated area, on the same intensity-vs-retention-time-minutes trapezoid
            /// basis as the peptide XICs (a trapezoid rounded to one decimal, exactly
            /// <see cref="DeNovoXicExtractor.AreaFor"/>'s rule). Only ADJACENT samples (consecutive scan
            /// indices) contribute a segment, which never bridges an absent scan.</summary>
            public double Area()
            {
                if (_scanIdx.Count < 2) return 0.0;

                double area = 0.0;
                for (int k = 1; k < _scanIdx.Count; k++)
                {
                    if (_scanIdx[k] - _scanIdx[k - 1] != 1) continue;   // not adjacent: no segment
                    double dx = _rt[k] - _rt[k - 1];
                    double y1 = _intensity[k - 1];
                    double y2 = _intensity[k];
                    area += dx * y1 + dx * ((y2 - y1) / 2.0);
                }
                return Math.Round(area, 1);
            }
        }

        // ---- the JSON cache --------------------------------------------------------------------

        /// <summary>Bumped whenever the meaning of a stored field or a detection parameter changes, so a
        /// file written by an older detector is discarded and recomputed rather than trusted. Version 2
        /// added the per-charge split; a version 1 file carries no charge breakdown and cannot be
        /// completed from what it stored, so it is discarded and the run re-walked.</summary>
        public const int CacheVersion = 3;

        private static bool TryLoadCache(string path, string runName, string sourceFingerprint,
            out Ms1EnvelopeDetectionResult? result)
        {
            result = null;
            try
            {
                if (!File.Exists(path)) return false;

                CacheFile? file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), ExtractionCacheJson.Options);
                if (file == null) return false;

                // Any disagreement discards the file: a stale estimate is worse than a re-extraction,
                // which costs nothing that cannot be recomputed from the RAW.
                if (file.Version != CacheVersion) return false;
                if (!string.Equals(file.RunName, runName, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(file.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal)) return false;
                if (file.PpmTolerance != PpmTolerance) return false;
                if (file.MinCharge != MinCharge || file.MaxCharge != MaxCharge) return false;
                if (file.IsotopeCeiling != IsotopeCeiling) return false;
                if (file.MinIsotopes != MinIsotopes) return false;
                if (file.MinScansPerFeature != MinScansPerFeature) return false;

                // A file whose per-charge arrays are the wrong length was written against another charge
                // range; the split could not be read from it without guessing, so it is discarded.
                if (file.DetectedAreaByCharge == null || file.DetectedCountByCharge == null) return false;
                if (file.DetectedAreaByCharge.Length != MaxCharge + 1) return false;
                if (file.DetectedCountByCharge.Length != MaxCharge + 1) return false;

                result = new Ms1EnvelopeDetectionResult
                {
                    RunName = file.RunName,
                    DetectedEnvelopeCount = file.DetectedEnvelopeCount,
                    DetectedPeptideLikeArea = file.DetectedPeptideLikeArea,
                    Ms1ScanCount = file.Ms1ScanCount,
                    DetectedAreaByCharge = file.DetectedAreaByCharge,
                    DetectedCountByCharge = file.DetectedCountByCharge,
                    DetectedAreaMultiplyCharged = file.DetectedAreaMultiplyCharged,
                    DetectedAreaSinglyCharged = file.DetectedAreaSinglyCharged,
                    DetectedCountMultiplyCharged = file.DetectedCountMultiplyCharged,
                    DetectedCountSinglyCharged = file.DetectedCountSinglyCharged,
                };
                return true;
            }
            catch (Exception)
            {
                // A cache is a convenience; an unreadable one costs a re-detection and nothing else.
                result = null;
                return false;
            }
        }

        private static void SaveCache(string path, Ms1EnvelopeDetectionResult result, string sourceFingerprint)
        {
            try
            {
                string? folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                var file = new CacheFile
                {
                    Version = CacheVersion,
                    RunName = result.RunName,
                    SourceFingerprint = sourceFingerprint,
                    PpmTolerance = PpmTolerance,
                    MinCharge = MinCharge,
                    MaxCharge = MaxCharge,
                    IsotopeCeiling = IsotopeCeiling,
                    MinIsotopes = MinIsotopes,
                    MinScansPerFeature = MinScansPerFeature,
                    DetectedEnvelopeCount = result.DetectedEnvelopeCount,
                    DetectedPeptideLikeArea = result.DetectedPeptideLikeArea,
                    Ms1ScanCount = result.Ms1ScanCount,
                    DetectedAreaByCharge = result.DetectedAreaByCharge,
                    DetectedCountByCharge = result.DetectedCountByCharge,
                    DetectedAreaMultiplyCharged = result.DetectedAreaMultiplyCharged,
                    DetectedAreaSinglyCharged = result.DetectedAreaSinglyCharged,
                    DetectedCountMultiplyCharged = result.DetectedCountMultiplyCharged,
                    DetectedCountSinglyCharged = result.DetectedCountSinglyCharged,
                };

                string json = JsonSerializer.Serialize(file, ExtractionCacheJson.Options);
                string temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception)
            {
                // Failing to write the convenience cache must not fail the detection that produced a
                // perfectly good number in memory.
            }
        }

        /// <summary>The on-disk shape, with the parameter stamps at the top so a human opening the file
        /// can see which run and which detection settings it belongs to.</summary>
        private sealed class CacheFile
        {
            [JsonPropertyName("version")] public int Version { get; set; }
            [JsonPropertyName("runName")] public string RunName { get; set; } = string.Empty;
            [JsonPropertyName("sourceSha256")] public string SourceFingerprint { get; set; } = string.Empty;
            [JsonPropertyName("ppmTolerance")] public double PpmTolerance { get; set; }
            [JsonPropertyName("minCharge")] public int MinCharge { get; set; }
            [JsonPropertyName("maxCharge")] public int MaxCharge { get; set; }
            [JsonPropertyName("isotopeCeiling")] public int IsotopeCeiling { get; set; }
            [JsonPropertyName("minIsotopes")] public int MinIsotopes { get; set; }
            [JsonPropertyName("minScansPerFeature")] public int MinScansPerFeature { get; set; }
            [JsonPropertyName("detectedEnvelopeCount")] public int DetectedEnvelopeCount { get; set; }
            [JsonPropertyName("detectedPeptideLikeArea")] public double DetectedPeptideLikeArea { get; set; }
            [JsonPropertyName("ms1ScanCount")] public int Ms1ScanCount { get; set; }

            // The charge split, indexed BY CHARGE (index 0 unused), plus the two evidence groups it rolls
            // up to. Stored rather than re-derived so a cached run answers the same question a fresh one
            // does.
            [JsonPropertyName("detectedAreaByCharge")] public double[]? DetectedAreaByCharge { get; set; }
            [JsonPropertyName("detectedCountByCharge")] public int[]? DetectedCountByCharge { get; set; }
            [JsonPropertyName("detectedAreaMultiplyCharged")] public double DetectedAreaMultiplyCharged { get; set; }
            [JsonPropertyName("detectedAreaSinglyCharged")] public double DetectedAreaSinglyCharged { get; set; }
            [JsonPropertyName("detectedCountMultiplyCharged")] public int DetectedCountMultiplyCharged { get; set; }
            [JsonPropertyName("detectedCountSinglyCharged")] public int DetectedCountSinglyCharged { get; set; }
        }
    }
}
