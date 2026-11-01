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

        // Captured by BatchExtractionRealTests so the Q1 identified-envelope section can prove, from
        // the de novo cache and without a second RAW load, that the isotope generalization left the de
        // novo run _01 numbers byte-identical. Null / NaN when that section skipped.
        private static List<PeaksDeNovoRow>? _deNovoRun01Kept;
        private static string? _deNovoRun01Raw;
        private static string? _deNovoRun01CachePath;
        private static double _deNovoRun01TotalM0 = double.NaN;
        private static double _deNovoRun01TotalEnvelope = double.NaN;

        /// <summary>
        /// F4 on the real data: one whole run's kept de novo rows (DeNovoOnly plus CoIsolatedExtra)
        /// extracted from the RAW, the cache proven to answer a second pass without the RAW, and the
        /// module's M+0 correlated against PEAKS' own feature areas as a diagnostic. Also prints the
        /// first real preview of the plot's de novo slice: total M+0 over the run's stored D2.
        /// </summary>
        private static void BatchExtractionRealTests(string? peaksDir, string? desktopDir, string workDir)
        {
            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");
            string? raw = desktopDir == null ? null
                : Path.Combine(desktopDir, "20220624_Bjararaca_1ug_3step-240min_01.raw");
            if (peaksDir == null || sepr2 == null || !File.Exists(sepr2) || !File.Exists(raw!))
            {
                Console.WriteLine("  SKIP  batch extraction - needs the PEAKS directory, PLVI.sepr2 and the _01 RAW");
                skipped++;
                return;
            }

            var psms = FamilyQuant.DeNovo.SeproPsmAdapter.Load(sepr2);
            string run01 = FamilyQuant.DeNovo.DeNovoReconciler.NormalizeRunName(raw!);

            var criteria = new FamilyQuant.DeNovo.DeNovoFilterCriteria();
            var filtered = new List<FamilyQuant.DeNovo.PeaksDeNovoRow>();
            int rawDeNovoRows = 0;
            foreach (string csv in Directory.GetFiles(peaksDir, "*.denovoOnly.csv", SearchOption.AllDirectories))
            {
                var read = FamilyQuant.DeNovo.PeaksCsvReader.ReadDeNovoOnly(csv);
                rawDeNovoRows += read.Count;
                filtered.AddRange(FamilyQuant.DeNovo.DeNovoFilter.Apply(read, criteria));
            }

            var kept = FamilyQuant.DeNovo.DeNovoReconciler.Reconcile(filtered, psms)
                .Where(r => r.Verdict == FamilyQuant.DeNovo.ReconciliationVerdict.DeNovoOnly
                         || r.Verdict == FamilyQuant.DeNovo.ReconciliationVerdict.CoIsolatedExtra)
                .Select(r => r.DeNovo)
                .Where(d => string.Equals(FamilyQuant.DeNovo.DeNovoReconciler.NormalizeRunName(d.RunName),
                                          run01, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Check("run _01 keeps a five-digit de novo row count for extraction",
                kept.Count > 10000, kept.Count.ToString("N0"));

            var options = new FamilyQuant.DeNovo.DeNovoXicOptions
            {
                CachePath = Path.Combine(workDir, "denovo_xic_run01.json"),
            };
            var (results, stats) = FamilyQuant.DeNovo.DeNovoBatchExtractor.ExtractForRun(raw!, kept, options);

            Console.WriteLine($"        linhas {stats.RowsIn:N0} | precursores unicos {stats.UniquePrecursors:N0} | " +
                $"com sinal {stats.WithSignal:N0} | sem cluster {stats.NoCluster:N0} | falhas {stats.Failed:N0} | {stats.ElapsedSeconds:F1} s");

            Check("every unique precursor came back exactly once",
                results.Count == stats.UniquePrecursors, results.Count.ToString("N0"));
            Check("no precursor failed with an exception", stats.Failed == 0, stats.Failed.ToString());
            Check("most precursors yielded a cluster at their own anchor scan",
                stats.WithSignal * 2 > stats.UniquePrecursors,
                $"{stats.WithSignal:N0} of {stats.UniquePrecursors:N0}");

            // The wrong-run guard: handing run _02's rows to run _01's RAW must throw, not extract.
            var foreign = new FamilyQuant.DeNovo.PeaksDeNovoRow { RunName = "other_run", Scan = 1, Mz = 500, Charge = 2 };
            Check("rows of another run are refused rather than extracted against the wrong RAW",
                Throws<ArgumentException>(() =>
                    FamilyQuant.DeNovo.DeNovoBatchExtractor.ExtractForRun(raw!, new[] { foreign }, options)));

            // Second pass: the cache must answer everything without touching the RAW.
            var started = DateTime.UtcNow;
            var (again, stats2) = FamilyQuant.DeNovo.DeNovoBatchExtractor.ExtractForRun(raw!, kept, options);
            double cachedSeconds = (DateTime.UtcNow - started).TotalSeconds;
            Check("a second pass is answered from the cache in seconds, not minutes",
                cachedSeconds < 30, cachedSeconds.ToString("F1") + " s");
            Check("and returns identical areas",
                again.Count == results.Count &&
                again.Zip(results, (a, b) => a.AreaM0 == b.AreaM0 && a.ScanNoMS2 == b.ScanNoMS2).All(x => x));

            var (pairs, r2) = FamilyQuant.DeNovo.DeNovoBatchExtractor.PeaksAreaCorrelation(kept, results);
            Console.WriteLine($"        correlacao log10 com a Area do PEAKS: r = {r2:F4} sobre {pairs:N0} pares");
            Check("the module's M+0 correlates with PEAKS' independent feature area",
                pairs > 5000 && r2 > 0.4, $"r={r2:F4}, pares={pairs:N0}");

            // The first real number of the three-way plot: this run's de novo M+0 over its stored D2
            // (the .xic2 records 875,643,650,496.5421 for run _01, its package 2). Printed, not
            // asserted: the value is the deliverable of phase F6, this is its preview.
            double d2Run01 = 875643650496.5421;
            Console.WriteLine($"        previa do grafico: de novo M+0 = {stats.TotalAreaM0:E4} = " +
                $"{stats.TotalAreaM0 / d2Run01 * 100:F4}% do D2 da corrida _01 " +
                $"(envelope {stats.TotalEnvelope / d2Run01 * 100:F4}%)");

            // Hand the Q1 identified-envelope section a baseline it can re-check from this same cache,
            // without loading the RAW again, to prove the isotope generalization left these unchanged.
            _deNovoRun01Kept = kept;
            _deNovoRun01Raw = raw;
            _deNovoRun01CachePath = options.CachePath;
            _deNovoRun01TotalM0 = stats.TotalAreaM0;
            _deNovoRun01TotalEnvelope = stats.TotalEnvelope;
        }

        /// <summary>
        /// Q1 unit invariants for the configurable isotope envelope, pure and synthetic so they run on
        /// a machine that has never seen a RAW. Proves three things: the per-isotope m/z formula
        /// (<see cref="DeNovoXicExtractor.IsotopeMz"/>, the exact function the extractor uses), that the
        /// envelope credits N isotopes with the negative sentinel clamped and the M-1 control excluded,
        /// and that the default N = 3 reproduces the old AreaM0/AreaM1/AreaM2/EnvelopeSum exactly.
        /// </summary>
        private static void IsotopeEnvelopeUnitTests()
        {
            // ---- the per-isotope m/z formula, index 0 = monoisotopic, i = mz + i*(spacing/z) --------
            double mz = Math.Round(700.12345, DeNovoXicExtractor.MzDecimals);
            foreach (int z in new[] { 1, 2, 3 })
            {
                double step = DeNovoXicExtractor.Carbon13Spacing / z;
                Check($"IsotopeMz index 0 at z{z} is the monoisotopic m/z to the bit",
                    DeNovoXicExtractor.IsotopeMz(mz, z, 0) == mz,
                    DeNovoXicExtractor.IsotopeMz(mz, z, 0).ToString("R", CultureInfo.InvariantCulture));
                for (int i = 1; i <= 4; i++)
                    Check($"IsotopeMz index {i} at z{z} is mz + {i}*(spacing/z) rounded",
                        DeNovoXicExtractor.IsotopeMz(mz, z, i) == Math.Round(mz + i * step, DeNovoXicExtractor.MzDecimals));
                Check($"IsotopeMz index -1 at z{z} is the M-1 interference control",
                    DeNovoXicExtractor.IsotopeMz(mz, z, -1) == Math.Round(mz - step, DeNovoXicExtractor.MzDecimals));
            }
            Check("IsotopeMz rejects a non-positive charge rather than dividing by it",
                Throws<ArgumentException>(() => DeNovoXicExtractor.IsotopeMz(mz, 0, 1)));

            // ---- crediting N isotopes: 1, 3, 5 give arrays of that length; EnvelopeSum sums them ----
            foreach (int n in new[] { 1, 3, 5 })
            {
                var areas = new double[n];
                double expect = 0;
                for (int i = 0; i < n; i++) { areas[i] = (i + 1) * 100.0; expect += areas[i]; }

                var res = new DeNovoXicResult { IsotopeAreas = areas, AreaMMinus1 = -1 };
                Check($"IsotopeCount {n} yields exactly {n} per-isotope areas",
                    res.IsotopeAreas.Length == n, res.IsotopeAreas.Length.ToString(CultureInfo.InvariantCulture));
                Check($"EnvelopeSum at N={n} equals the sum of the credited isotopes",
                    res.EnvelopeSum == expect, res.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));
                Check($"AreaM0 at N={n} is IsotopeAreas[0]", res.AreaM0 == areas[0]);
            }

            // ---- the negative sentinel is clamped, and the M-1 control is never in the sum ----------
            var sentinel = new DeNovoXicResult
            {
                IsotopeAreas = new[] { 500.0, DeNovoXicExtractor.NoCluster, 20.0 },
                AreaMMinus1 = 999999,
            };
            Check("EnvelopeSum clamps a no-cluster isotope to 0 rather than subtracting 1",
                sentinel.EnvelopeSum == 500.0 + 0.0 + 20.0, sentinel.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));
            Check("EnvelopeSum excludes the M-1 control however large it is",
                sentinel.EnvelopeSum == 520.0, sentinel.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));

            // ---- the regression: N=3 reproduces the old AreaM0/M1/M2/EnvelopeSum exactly ------------
            var oldStyle = new DeNovoXicResult { AreaM0 = 5627968.3, AreaM1 = 100, AreaM2 = 10, AreaMMinus1 = -1 };
            var newStyle = new DeNovoXicResult { IsotopeAreas = new[] { 5627968.3, 100.0, 10.0 }, AreaMMinus1 = -1 };

            Check("setting AreaM0, AreaM1, AreaM2 grows IsotopeAreas to length 3",
                oldStyle.IsotopeAreas.Length == 3, oldStyle.IsotopeAreas.Length.ToString(CultureInfo.InvariantCulture));
            Check("old-style and array-style constructions agree on AreaM0", oldStyle.AreaM0 == newStyle.AreaM0);
            Check("old-style and array-style constructions agree on AreaM1", oldStyle.AreaM1 == newStyle.AreaM1);
            Check("old-style and array-style constructions agree on AreaM2", oldStyle.AreaM2 == newStyle.AreaM2);
            Check("old-style and array-style constructions agree on EnvelopeSum",
                oldStyle.EnvelopeSum == newStyle.EnvelopeSum);
            Check("N=3 EnvelopeSum is exactly M+0 + M+1 + M+2, nothing shifted",
                newStyle.EnvelopeSum == 5627968.3 + 100 + 10, newStyle.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));

            // ---- AreaM1/AreaM2 read their old default of 0 when the count did not credit them --------
            var single = new DeNovoXicResult { IsotopeAreas = new[] { 123.0 } };
            Check("AreaM1 reads 0 when the isotope count did not credit it", single.AreaM1 == 0.0);
            Check("AreaM2 reads 0 when the isotope count did not credit it", single.AreaM2 == 0.0);
        }

        /// <summary>
        /// Q1 on the real data: extract the IDENTIFIED peptides of run _01 from its RAW with the full
        /// isotope envelope and prove the extraction is faithful (M+0 reproduces the stored quants bit
        /// for bit over the overlap the F3/F4 gate uses), then measure what the envelope adds - the
        /// whole point of the new engine. Also confirms, from the de novo cache and without a second
        /// RAW load, that the de novo run _01 numbers are byte-identical after the generalization.
        ///
        /// COST: one RAW load, ~30-60 s, cached into workDir. The de novo cross-check reuses the cache
        /// BatchExtractionRealTests already wrote, so it adds no load.
        /// </summary>
        private static void IdentifiedQuantRealTests(string? desktopDir, string workDir)
        {
            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");
            string? raw = desktopDir == null ? null
                : Path.Combine(desktopDir, "20220624_Bjararaca_1ug_3step-240min_01.raw");
            string? tsv = desktopDir == null ? null : Path.Combine(desktopDir, "_curva", "stored_quants.tsv");

            if (sepr2 == null || !File.Exists(sepr2) || raw == null || !File.Exists(raw)
                || tsv == null || !File.Exists(tsv))
            {
                Console.WriteLine("  SKIP  identified-peptide envelope - needs PLVI.sepr2, the _01 RAW and _curva/stored_quants.tsv");
                skipped++;
                return;
            }

            List<PatternLabPsm> psms;
            try { psms = SeproPsmAdapter.Load(sepr2); }
            catch (Exception ex) { Check("PLVI.sepr2 loads its PSMs", false, ex.GetType().Name + ": " + ex.Message); return; }

            string run01 = DeNovoReconciler.NormalizeRunName(raw);
            var run01Psms = psms
                .Where(p => string.Equals(DeNovoReconciler.NormalizeRunName(p.FileName), run01, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Check("run _01 supplies its identified PSMs to extract",
                run01Psms.Count > 1000, run01Psms.Count.ToString("N0"));
            Check("the identified PSMs carry a charge, so the isotope step is defined",
                run01Psms.All(p => p.ChargeState > 0), run01Psms.Count(p => p.ChargeState <= 0).ToString("N0") + " with no charge");

            // The go-forward envelope basis is 4 isotopes (owner decision, a ceiling: absent isotopes
            // contribute 0). The M+0 gate below is unaffected by the count, and the de novo path keeps 3.
            var options = new DeNovoXicOptions
            {
                IsotopeCount = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount,
                MinPointsForQuant = DeNovoXicExtractor.DefaultMinPointsForQuant,
                CachePath = Path.Combine(workDir, "identified_xic_run01_n6.json"),
                Progress = m => Console.WriteLine("        " + m),
            };
            Check("the identified extraction uses the go-forward 6-isotope envelope ceiling",
                options.IsotopeCount == 6, options.IsotopeCount.ToString());
            Check("and the PRODUCTION minimum-points rule, not the legacy one the parity gate uses",
                options.MinPointsForQuant == 3, options.MinPointsForQuant.ToString());

            List<DeNovoXicResult> results;
            IdentifiedQuantStats stats;
            try
            {
                (results, stats) = IdentifiedQuantExtractor.ExtractForRun(raw, run01Psms, options);
            }
            catch (Exception ex)
            {
                Check("the identified extraction runs against the _01 RAW", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            Console.WriteLine($"        PSMs {stats.PsmsIn:N0} | unique precursors {stats.UniquePrecursors:N0} | " +
                $"with signal {stats.WithSignal:N0} | no cluster {stats.NoCluster:N0} | failed {stats.Failed:N0} | " +
                $"distinct peaks {stats.DistinctPeaks:N0} | {stats.ElapsedSeconds:F1} s");

            Check("every unique identified precursor came back exactly once",
                results.Count == stats.UniquePrecursors, results.Count.ToString("N0"));
            Check("no identified precursor failed with an exception", stats.Failed == 0, stats.Failed.ToString());

            // ---- the gate: M+0 reproduces the stored quant bit for bit over the overlap -------------
            var byKey = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (DeNovoXicResult r in results)
                if (r.Failure == null) byKey[DeNovoXicCache.KeyFor(r.ScanNoMS2, r.PrecursorMz, r.Charge)] = r.AreaM0;

            int overlap = 0, exact = 0;
            var mism = new List<string>();
            foreach (var s in ReadPkg2StoredQuants(tsv))
            {
                string key = DeNovoXicCache.KeyFor(s.Scan, Math.Round(s.Mz, DeNovoXicExtractor.MzDecimals), s.Z);
                if (!byKey.TryGetValue(key, out double got)) continue;
                overlap++;
                if (got == s.Area) exact++;
                else if (mism.Count < 3)
                    mism.Add($"scan {s.Scan} z{s.Z}: stored {s.Area.ToString("R", CultureInfo.InvariantCulture)}, " +
                             $"extracted {got.ToString("R", CultureInfo.InvariantCulture)}");
            }

            Check("the stored quants of run _01 overlap the identified extraction",
                overlap > 3000, overlap.ToString("N0"));
            // AT THE PRODUCTION MINIMUM OF THREE POINTS. This is the empirical claim the whole change
            // rests on: PatternTools pads every cluster with a zero row before its first scan and one
            // after its last, so the shortest array a real cluster can produce is 3 and nothing on this
            // acquisition is excluded. If the rule ever did exclude a real peptide here, this gate would
            // fail rather than quietly report a smaller number.
            Check("identified M+0 reproduces the stored quant BIT FOR BIT over the overlap, at minimum 3 points",
                exact == overlap && overlap > 0,
                exact.ToString("N0") + "/" + overlap.ToString("N0") +
                (mism.Count == 0 ? string.Empty : "; " + string.Join(" | ", mism)));

            // ---- the whole point: the envelope is strictly more ion current than M+0 alone ----------
            Check("the isotope envelope total strictly exceeds the M+0 total",
                stats.TotalEnvelope > stats.TotalAreaM0,
                $"envelope {stats.TotalEnvelope.ToString("E6", CultureInfo.InvariantCulture)} vs " +
                $"M+0 {stats.TotalAreaM0.ToString("E6", CultureInfo.InvariantCulture)}");

            Console.WriteLine($"        identified M+0 total      = {stats.TotalAreaM0.ToString("E6", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"        identified envelope total = {stats.TotalEnvelope.ToString("E6", CultureInfo.InvariantCulture)}" +
                $"  (envelope / M+0 = {(stats.TotalEnvelope / stats.TotalAreaM0).ToString("F4", CultureInfo.InvariantCulture)})");
            Console.WriteLine($"        run _01 TIC               = {stats.TicArea.ToString("E6", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"        identified fraction of TIC: M+0 {stats.IdentifiedFractionM0.ToString("P4", CultureInfo.InvariantCulture)} | " +
                $"envelope {stats.IdentifiedFractionEnvelope.ToString("P4", CultureInfo.InvariantCulture)}");

            Check("the identified fraction of TIC is larger on the envelope basis than on M+0",
                stats.IdentifiedFractionEnvelope > stats.IdentifiedFractionM0);

            // ---- the de novo numbers of run _01 are unchanged by the generalization -----------------
            if (_deNovoRun01Kept != null && _deNovoRun01Raw != null && !double.IsNaN(_deNovoRun01TotalM0))
            {
                var dnOpts = new DeNovoXicOptions { CachePath = _deNovoRun01CachePath };   // default IsotopeCount 3
                var (_, dnStats) = DeNovoBatchExtractor.ExtractForRun(_deNovoRun01Raw, _deNovoRun01Kept, dnOpts);

                Check("de novo run _01 M+0 total is byte-identical after the isotope generalization",
                    dnStats.TotalAreaM0 == _deNovoRun01TotalM0,
                    dnStats.TotalAreaM0.ToString("R", CultureInfo.InvariantCulture) + " vs " +
                    _deNovoRun01TotalM0.ToString("R", CultureInfo.InvariantCulture));
                Check("de novo run _01 envelope total (M+0+M+1+M+2) is byte-identical",
                    dnStats.TotalEnvelope == _deNovoRun01TotalEnvelope,
                    dnStats.TotalEnvelope.ToString("R", CultureInfo.InvariantCulture) + " vs " +
                    _deNovoRun01TotalEnvelope.ToString("R", CultureInfo.InvariantCulture));
            }
            else
            {
                Console.WriteLine("        (de novo run _01 baseline not captured - the batch section skipped; the de novo cross-check is skipped)");
            }
        }

        // ============================================================ minimum points per peak (Q8)

        /// <summary>One synthetic cluster in the layout GetXIC4 returns: [intensity, scanNo,
        /// retentionTime] per row, in scan order.</summary>
        private static double[][] Cluster(params (double intensity, int scan, double rt)[] points) =>
            points.Select(p => new[] { p.intensity, (double)p.scan, p.rt }).ToArray();

        /// <summary>
        /// The minimum-points-per-peak rule and the isotope ceiling, on synthetic clusters, so both are
        /// proved on a machine that has never seen a RAW.
        ///
        /// THE TWO CLAIMS THAT MATTER. First, that a minimum of 1 is TODAY'S RULE bit for bit, because
        /// that is what the parity gate against the stored quants depends on. Second, that the rule is
        /// applied per ISOTOPE and not per peptide: if it were per peptide, a single-point M+1 would still
        /// inject a raw intensity into an envelope whose M+0 is a proper integral, and the unit
        /// inconsistency would survive in exactly the number the paper reports.
        /// </summary>
        private static void MinimumPointsAndCeilingUnitTests()
        {
            const double spacing = 0.005134;   // run _01's measured median MS1 scan spacing, in minutes

            double[][] one = Cluster((5000.0, 100, 10.000));
            double[][] two = Cluster((4000.0, 100, 10.000), (6000.0, 101, 10.010));
            double[][] three = Cluster((0.0, 99, 9.990), (6000.0, 100, 10.000), (0.0, 101, 10.010));

            double oneLegacy = DeNovoXicExtractor.AreaFor(one);
            double twoLegacy = DeNovoXicExtractor.AreaFor(two);
            double threeLegacy = DeNovoXicExtractor.AreaFor(three);

            // ---- the legacy rule is untouched --------------------------------------------------------
            Check("AreaFor still credits a one-point cluster its raw INTENSITY, column 0",
                oneLegacy == 5000.0, oneLegacy.ToString("R", CultureInfo.InvariantCulture));
            Check("AreaFor still trapezoids two or more points over retention time",
                twoLegacy == Math.Round(5000.0 * 0.010, 1), twoLegacy.ToString("R", CultureInfo.InvariantCulture));
            Check("AreaFor still returns the no-cluster sentinel for a null signal",
                DeNovoXicExtractor.AreaFor(null) == DeNovoXicExtractor.NoCluster);

            // ---- minimum 1 reproduces the legacy rule EXACTLY ----------------------------------------
            foreach (var (name, signal, legacy) in new[]
            {
                ("one-point", one, oneLegacy),
                ("two-point", two, twoLegacy),
                ("three-point", three, threeLegacy),
            })
            {
                ClusterArea c = DeNovoXicExtractor.Classify(signal, DeNovoXicExtractor.LegacyMinPointsForQuant, spacing);
                Check($"at minimum 1 point a {name} cluster is quantified with the legacy area, bit for bit",
                    c.IsQuantified
                    && BitConverter.DoubleToInt64Bits(c.Area) == BitConverter.DoubleToInt64Bits(legacy)
                    && c.UnderSampledArea == 0,
                    c.Area.ToString("R", CultureInfo.InvariantCulture));
            }

            // ---- minimum 3 excludes the one-point and the two-point cluster --------------------------
            ClusterArea excludedOne = DeNovoXicExtractor.Classify(one, 3, spacing);
            ClusterArea excludedTwo = DeNovoXicExtractor.Classify(two, 3, spacing);
            ClusterArea keptThree = DeNovoXicExtractor.Classify(three, 3, spacing);

            Check("at minimum 3 points a ONE-point cluster is not quantified",
                excludedOne.IsUnderSampled && excludedOne.Area == DeNovoXicExtractor.UnderSampled,
                excludedOne.Area.ToString("R", CultureInfo.InvariantCulture));
            Check("at minimum 3 points a TWO-point cluster is not quantified",
                excludedTwo.IsUnderSampled && excludedTwo.Area == DeNovoXicExtractor.UnderSampled,
                excludedTwo.Area.ToString("R", CultureInfo.InvariantCulture));
            Check("at minimum 3 points a THREE-point cluster IS quantified, with the legacy area",
                keptThree.IsQuantified
                && BitConverter.DoubleToInt64Bits(keptThree.Area) == BitConverter.DoubleToInt64Bits(threeLegacy),
                keptThree.Area.ToString("R", CultureInfo.InvariantCulture));

            // ---- and neither vanishes: both are recorded, in ion-current units -----------------------
            // A two-point cluster already HAS a trapezoid, so that trapezoid is what is recorded. A
            // one-point cluster has no width, so its area is ESTIMATED from the run's scan spacing - which
            // is the whole reason the legacy branch is a trap: as a raw intensity it is 1/spacing times
            // too large, about 195x at run _01's median spacing.
            Check("a refused TWO-point cluster keeps its real trapezoid, and is not called an estimate",
                excludedTwo.UnderSampledArea == twoLegacy && !excludedTwo.UnderSampledIsEstimate,
                excludedTwo.UnderSampledArea.ToString("R", CultureInfo.InvariantCulture));
            Check("a refused ONE-point cluster is estimated as intensity x scan spacing, and says so",
                excludedOne.UnderSampledArea == 5000.0 * spacing && excludedOne.UnderSampledIsEstimate,
                excludedOne.UnderSampledArea.ToString("R", CultureInfo.InvariantCulture));
            Check("the estimate is roughly 195 times SMALLER than the raw intensity the legacy rule credits",
                Math.Abs(oneLegacy / excludedOne.UnderSampledArea - 1.0 / spacing) < 1e-6,
                (oneLegacy / excludedOne.UnderSampledArea).ToString("F1", CultureInfo.InvariantCulture) + "x");
            Check("an unknown scan spacing yields a zero estimate rather than an invented one",
                DeNovoXicExtractor.Classify(one, 3, 0).UnderSampledArea == 0);

            // ---- the two sentinels are different facts ----------------------------------------------
            ClusterArea absent = DeNovoXicExtractor.Classify(null, 3, spacing);
            Check("no cluster and an under-sampled cluster carry DIFFERENT sentinels",
                absent.Area == DeNovoXicExtractor.NoCluster
                && excludedOne.Area == DeNovoXicExtractor.UnderSampled
                && DeNovoXicExtractor.NoCluster != DeNovoXicExtractor.UnderSampled);
            Check("both sentinels are at or below NoCluster, so every existing guard reads them as no signal",
                absent.Area <= DeNovoXicExtractor.NoCluster
                && excludedOne.Area <= DeNovoXicExtractor.NoCluster);
            Check("no cluster is not an under-sampled measurement: it carries no area and no points",
                absent.NoCluster && !absent.IsUnderSampled && absent.PointCount == 0 && absent.UnderSampledArea == 0);
            Check("a minimum below 1 is refused rather than treated as 'no rule'",
                Throws<ArgumentOutOfRangeException>(() => DeNovoXicExtractor.Classify(three, 0, spacing)));

            // ---- the run's median MS1 scan spacing ---------------------------------------------------
            var spectra = new List<PatternTools.MSParserLight.MSUltraLight>
            {
                Ms1Scan(1, 0.000), Ms1Scan(2, 0.005), Ms1Scan(3, 0.010), Ms1Scan(4, 0.030), Ms1Scan(5, 0.035),
            };
            Check("the MS1 scan spacing is the MEDIAN gap, not the mean, so a duty-cycle tail cannot skew it",
                Math.Abs(DeNovoXicExtractor.MedianMs1ScanSpacingMinutes(spectra) - 0.005) < 1e-12,
                DeNovoXicExtractor.MedianMs1ScanSpacingMinutes(spectra).ToString("R", CultureInfo.InvariantCulture));
            Check("one spectrum, or none, yields no spacing at all rather than a guess",
                DeNovoXicExtractor.MedianMs1ScanSpacingMinutes(spectra.Take(1).ToList()) == 0
                && DeNovoXicExtractor.MedianMs1ScanSpacingMinutes(null) == 0);

            // ---- THE CRUX: the rule is applied per ISOTOPE, not once per peptide ---------------------
            // M+0, M+1 and M+2 carry a well-sampled cluster; M+3 carries only two points; M+4 and M+5 are
            // not there at all. At the production minimum of 3 the peptide credits what qualifies and
            // nothing else, and the refused M+3 is accounted for rather than dropped.
            double[][]? PerIsotope(int k) => k switch
            {
                0 or 1 or 2 => three,
                3 => two,
                _ => null,
            };

            var row = new DeNovoXicResult();
            DeNovoXicExtractor.CreditIsotopes(
                k => DeNovoXicExtractor.Classify(PerIsotope(k), 3, spacing),
                DeNovoXicExtractor.DefaultEnvelopeIsotopeCount, row);

            Check("per isotope: the ceiling gives one slot per isotope, six of them",
                row.IsotopeAreas.Length == 6 && row.IsotopePointCounts.Length == 6,
                row.IsotopeAreas.Length.ToString());
            Check("per isotope: the three well-sampled isotopes are credited",
                row.IsotopeAreas[0] == threeLegacy && row.IsotopeAreas[1] == threeLegacy
                && row.IsotopeAreas[2] == threeLegacy);
            Check("per isotope: the thin M+3 is NOT credited, though the peptide's M+0 qualified",
                row.IsotopeAreas[3] == DeNovoXicExtractor.UnderSampled,
                row.IsotopeAreas[3].ToString("R", CultureInfo.InvariantCulture));
            Check("per isotope: the absent M+4 and M+5 credit nothing, and nothing is filled in for them",
                row.IsotopeAreas[4] == DeNovoXicExtractor.NoCluster
                && row.IsotopeAreas[5] == DeNovoXicExtractor.NoCluster);
            Check("per isotope: the envelope sums ONLY what qualified",
                row.EnvelopeSum == 3 * threeLegacy, row.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));
            Check("per isotope: the refused M+3 lands in the under-sampled total, in the same units",
                row.UnderSampledArea == twoLegacy && row.UnderSampledIsotopes == 1
                && !row.UnderSampledIsEstimate,
                row.UnderSampledArea.ToString("R", CultureInfo.InvariantCulture));
            Check("per isotope: the point count of each cluster is recorded beside its area",
                row.IsotopePointCounts[0] == 3 && row.IsotopePointCounts[3] == 2
                && row.IsotopePointCounts[5] == 0,
                string.Join(",", row.IsotopePointCounts));

            // The same peptide under the legacy rule: M+3 is credited and nothing is under-sampled. This
            // is what today's numbers do, and it is why the rule is a change and not a no-op in general.
            var legacyRow = new DeNovoXicResult();
            DeNovoXicExtractor.CreditIsotopes(
                k => DeNovoXicExtractor.Classify(PerIsotope(k), DeNovoXicExtractor.LegacyMinPointsForQuant, spacing),
                DeNovoXicExtractor.DefaultEnvelopeIsotopeCount, legacyRow);

            Check("under the legacy rule the same M+3 IS credited and nothing is under-sampled",
                legacyRow.IsotopeAreas[3] == twoLegacy && legacyRow.UnderSampledArea == 0
                && legacyRow.EnvelopeSum == 3 * threeLegacy + twoLegacy,
                legacyRow.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));

            // ---- the ceiling: a sixth isotope is credited when present, zero when absent -------------
            var sixth = new DeNovoXicResult();
            DeNovoXicExtractor.CreditIsotopes(k => DeNovoXicExtractor.Classify(three, 3, spacing),
                DeNovoXicExtractor.DefaultEnvelopeIsotopeCount, sixth);
            Check("ceiling: a sixth isotope that is there IS credited at the 6-isotope basis",
                sixth.IsotopeAreas.Length == 6 && sixth.IsotopeAreas[5] == threeLegacy
                && sixth.EnvelopeSum == 6 * threeLegacy,
                sixth.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));

            var noSixth = new DeNovoXicResult();
            DeNovoXicExtractor.CreditIsotopes(k => DeNovoXicExtractor.Classify(k < 5 ? three : null, 3, spacing),
                DeNovoXicExtractor.DefaultEnvelopeIsotopeCount, noSixth);
            Check("ceiling: a sixth isotope that is NOT there contributes exactly zero",
                noSixth.EnvelopeSum == 5 * threeLegacy
                && noSixth.IsotopeAreas[5] == DeNovoXicExtractor.NoCluster,
                noSixth.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));
            Check("ceiling: raising it from four to six adds ONLY the isotopes that were really found",
                sixth.EnvelopeSum - 4 * threeLegacy == 2 * threeLegacy
                && noSixth.EnvelopeSum - 4 * threeLegacy == threeLegacy);

            Check("CreditIsotopes refuses a ceiling below the monoisotopic peak",
                Throws<ArgumentOutOfRangeException>(() =>
                    DeNovoXicExtractor.CreditIsotopes(k => ClusterArea.NoClusterFound(), 0, new DeNovoXicResult())));
        }

        // ============================================================ identified but not quantified (Q8)

        /// <summary>A stored row that was found and REFUSED: its M+0 is the under-sampled sentinel, its
        /// envelope is empty, and the ion current it carries is in the under-sampled column.</summary>
        private static QuantRow RefusedQr(string side, string run, int scan, int z, string seq,
                                          double underSampled, bool isEstimate)
        {
            return new QuantRow
            {
                Side = side,
                Run = run,
                Scan = scan,
                Charge = z,
                Sequence = seq,
                PrecursorMz = Math.Round(500.0 + scan * 1e-3, DeNovoXicExtractor.MzDecimals),
                IsotopeAreas = new[]
                {
                    DeNovoXicExtractor.UnderSampled, DeNovoXicExtractor.NoCluster,
                    DeNovoXicExtractor.NoCluster, DeNovoXicExtractor.NoCluster,
                    DeNovoXicExtractor.NoCluster, DeNovoXicExtractor.NoCluster,
                },
                EnvelopeSum = 0.0,
                UnderSampledArea = underSampled,
                UnderSampledIsEstimate = isEstimate,
            };
        }

        /// <summary>
        /// The identified-but-not-quantified fraction: that it is carried on the peptide grain, in the
        /// same ion-current units as everything else, that it survives the store, and that the whole
        /// decomposition still accounts for the TIC with nothing migrating silently.
        /// </summary>
        private static void UnderSampledAccountingTests(string workDir)
        {
            const string run = "us_01";
            const double tic = 1000.0;

            var rows = new List<QuantRow>
            {
                // Quantified outright.
                Qr("identified", run, 10, 2, "QUANTA", 100, 200),
                // Quantified M+0, one refused isotope: the per-isotope rule made visible in the store.
                Qr("identified", run, 20, 2, "PARTIALK", 50, 90),
                // Refused outright, sampled TWICE inside one elution peak: the collapse must count it once.
                RefusedQr("identified", run, 30, 2, "THINK", 30, isEstimate: true),
                RefusedQr("identified", run, 32, 2, "THINK", 30, isEstimate: true),
                // The de novo side carries the same two kinds.
                Qr("denovo", run, 40, 2, "DENOVOQ", 30, 60),
                RefusedQr("denovo", run, 50, 3, "DENOVOTHIN", 20, isEstimate: false),
            };
            rows[1].UnderSampledArea = 10.0;   // PARTIALK's refused isotope

            // ---- the collapse keeps a refused peptide, once ------------------------------------------
            List<NativePeptideQuant> peptides = NativeQuantFractions.DedupToPeptides(rows);
            Check("Q8 dedup: a peptide that was refused outright is KEPT, not dropped with the sentinels",
                peptides.Any(p => p.Sequence == "THINK"), peptides.Count.ToString());
            Check("Q8 dedup: its two anchors collapse to ONE peptide carrying 30, not 60",
                peptides.Count(p => p.Sequence == "THINK") == 1
                && peptides.Single(p => p.Sequence == "THINK").UnderSampledArea == 30.0
                && peptides.Single(p => p.Sequence == "THINK").AnchorCount == 2,
                peptides.Count(p => p.Sequence == "THINK").ToString());
            Check("Q8 dedup: a refused peptide's M+0 reads as zero ion current, never as the -2 sentinel",
                peptides.Single(p => p.Sequence == "THINK").AreaM0 == 0
                && peptides.Single(p => p.Sequence == "THINK").EnvelopeSum == 0);

            // ---- the per-run quantity ---------------------------------------------------------------
            var perRunTic = new Dictionary<string, double> { [run] = tic };
            NativeRunFraction f = NativeQuantFractions.Compute(rows, perRunTic).Single();

            Check("Q8 fraction: the identified under-sampled total is the sum over peptides (40)",
                f.IdentifiedUnderSampled == 40.0 && f.IdentifiedUnderSampledPeptides == 2,
                f.IdentifiedUnderSampled.ToString("R", CultureInfo.InvariantCulture));
            Check("Q8 fraction: the de novo under-sampled total is carried apart (20)",
                f.DeNovoUnderSampled == 20.0 && f.DeNovoUnderSampledPeptides == 1,
                f.DeNovoUnderSampled.ToString("R", CultureInfo.InvariantCulture));
            Check("Q8 fraction: the quantified envelopes are unaffected by the refusals",
                f.IdentifiedEnvelope == 290.0 && f.DeNovoEnvelope == 60.0,
                f.IdentifiedEnvelope + "/" + f.DeNovoEnvelope);
            Check("Q8 fraction: the under-sampled share is taken over the SAME TIC as everything else",
                Math.Abs(f.UnderSampledFraction - 60.0 / tic) < 1e-12,
                f.UnderSampledFraction.ToString("R", CultureInfo.InvariantCulture));
            Check("Q8 fraction: the estimate flag rides with the number wherever it goes",
                f.UnderSampledIsEstimate);

            // The detector is an independent diagnostic, not a physical partition of the TIC. Refused
            // signal remains accounted for explicitly without creating biological residual classes.
            f.DetectedPeptideLikeArea = 600.0;
            Check("Q8 diagnostic: under-sampled signal and the detector remain separate quantities",
                f.UnderSampledFraction == 0.06 && f.DetectedFractionPeptideLike == 0.60);

            var quantifiedOnly = rows
                .Where(r => r.AreaM0 > DeNovoXicExtractor.NoCluster)
                .Select(r => new QuantRow
                {
                    Side = r.Side, Run = r.Run, Scan = r.Scan, Charge = r.Charge,
                    Sequence = r.Sequence, PrecursorMz = r.PrecursorMz,
                    IsotopeAreas = r.IsotopeAreas, EnvelopeSum = r.EnvelopeSum,
                })
                .ToList();
            NativeRunFraction dropped = NativeQuantFractions.Compute(quantifiedOnly, perRunTic).Single();
            dropped.DetectedPeptideLikeArea = 600.0;
            Check("Q8 diagnostic: dropping refused rows loses their accounting but does not redefine the detector",
                dropped.UnderSampledFraction == 0.0
                && dropped.DetectedPeptideLikeArea == f.DetectedPeptideLikeArea);

            // ---- the store carries it, and says what rule produced it --------------------------------
            var manifest = new QuantManifest
            {
                IsotopeCount = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount,
                MinPointsForQuant = DeNovoXicExtractor.DefaultMinPointsForQuant,
                Ppm = DeNovoXicExtractor.ProductionPpm,
                Ms1Tolerance = DeNovoXicExtractor.ProductionMs1Tolerance,
                PerRunTic = new Dictionary<string, double>(perRunTic, StringComparer.OrdinalIgnoreCase),
                Provenance = QuantManifest.DefaultProvenance(),
            };
            NativeQuantFractions.PopulateManifest(manifest, rows);

            // The Q6 detected estimate, injected exactly as the compute path injects it, so the stored
            // decomposition has all four levels and can be checked for closure from the file alone.
            NativeQuantFractions.ApplyDetectedToManifest(manifest,
                new Dictionary<string, Ms1EnvelopeDetectionResult>(StringComparer.OrdinalIgnoreCase)
                {
                    [run] = new Ms1EnvelopeDetectionResult
                    {
                        RunName = run, DetectedPeptideLikeArea = 600.0, DetectedEnvelopeCount = 7,
                    },
                });

            var fcf = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|X|Y", USearchID = 0, UserID = 0 },
                },
            };
            string path = Path.Combine(workDir, "q8_undersampled.fqproj");
            ProjectStore.WriteFqproj(path, fcf, rows, manifest);
            var (_, rows2, mf2) = ProjectStore.ReadFqproj(path);

            Check("Q8 store: every row survives, refused ones included",
                rows2.Count == rows.Count, rows2.Count + " vs " + rows.Count);
            Check("Q8 store: the under-sampled area round-trips bit for bit, with its estimate flag",
                rows2.Zip(rows, (y, x) =>
                    BitConverter.DoubleToInt64Bits(y.UnderSampledArea) == BitConverter.DoubleToInt64Bits(x.UnderSampledArea)
                    && y.UnderSampledIsEstimate == x.UnderSampledIsEstimate).All(ok => ok));
            Check("Q8 store: the under-sampled sentinel survives among the isotopes, distinct from -1",
                rows2.Any(r => r.IsotopeAreas.Length > 1
                               && r.IsotopeAreas[0] == DeNovoXicExtractor.UnderSampled
                               && r.IsotopeAreas[1] == DeNovoXicExtractor.NoCluster));
            Check("Q8 store: the manifest records the minimum-points rule the areas were made under",
                mf2.MinPointsForQuant == DeNovoXicExtractor.DefaultMinPointsForQuant,
                mf2.MinPointsForQuant.ToString());
            Check("Q8 store: the manifest's provenance names the under-sampled numbers an ESTIMATE",
                mf2.Provenance.TryGetValue("identified_under_sampled", out string? uv)
                && uv.Contains("ESTIMATE", StringComparison.Ordinal), uv);

            DerivedRunFraction? d = mf2.DerivedFractions.FirstOrDefault(x => !x.Grouped);
            Check("Q8 store: the derived section persists the block per side and pooled",
                d != null && d.IdentifiedUnderSampled == 40.0 && d.DeNovoUnderSampled == 20.0
                && Math.Abs(d.UnderSampledFraction - 0.06) < 1e-12 && d.UnderSampledIsEstimate,
                d == null ? "missing" : d.UnderSampledFraction.ToString("R", CultureInfo.InvariantCulture));

            // ---- a container written before these columns existed still opens -------------------------
            // The reader resolves columns BY NAME, so a legacy 12-column table loads with zeros in the
            // new columns rather than being refused as "not the expected column set".
            string legacyPath = Path.Combine(workDir, "q8_legacy_columns.fqproj");
            ProjectStore.WriteFqproj(legacyPath, fcf, rows, manifest);
            WriteLegacyQuantEntry(legacyPath, rows);

            var (_, legacyRows, _) = ProjectStore.ReadFqproj(legacyPath);
            Check("Q8 store: a legacy 12-column quant table still opens, by column name",
                legacyRows.Count == rows.Count, legacyRows.Count + " vs " + rows.Count);
            Check("Q8 store: its M+0 and envelope are unchanged, and the new columns read as zero",
                legacyRows.Count == rows.Count
                && legacyRows.Zip(rows, (y, x) =>
                    BitConverter.DoubleToInt64Bits(y.EnvelopeSum) == BitConverter.DoubleToInt64Bits(x.EnvelopeSum)
                    && y.UnderSampledArea == 0 && !y.UnderSampledIsEstimate).All(ok => ok));

            // ---- the analytics CSV carries it as its own columns --------------------------------------
            AnalyticsData data = AnalyticsBuilder.Build(rows, perRunTic);
            NativeQuantFractions.ApplyDetectedToFractions(data.Fractions, data.Grouped, mf2.DerivedFractions);

            string summary = AnalyticsCsv.BuildSummaryCsv(data.Fractions, data.Grouped);
            var sLines = summary.Split('\n').Where(l => l.Length > 0).ToList();
            var header = SplitCsvLine(sLines[0]).ToList();

            foreach (string column in new[]
            {
                "identified_under_sampled", "denovo_under_sampled", "under_sampled_fraction",
                "under_sampled_estimate", "resolved_isotope_series_fraction_diagnostic",
                "recovered_fraction_m0", "recovered_fraction_reference_calibrated",
                "recovered_fraction_envelope_unadjusted",
            })
            {
                Check("Q8 CSV: the summary carries a \"" + column + "\" column of its own",
                    header.Contains(column), string.Join(",", header));
            }

            string[] runRow = SplitCsvLine(sLines[1]);
            double CsvAt(string column) =>
                double.Parse(runRow[header.IndexOf(column)], NumberStyles.Float, CultureInfo.InvariantCulture);

            Check("Q8 CSV: the under-sampled columns hold the measured numbers, dot-decimal",
                CsvAt("identified_under_sampled") == 40.0 && CsvAt("denovo_under_sampled") == 20.0
                && Math.Abs(CsvAt("under_sampled_fraction") - 0.06) < 1e-12,
                runRow[header.IndexOf("under_sampled_fraction")]);
            Check("Q8 CSV: the estimate flag is a column, not a footnote",
                runRow[header.IndexOf("under_sampled_estimate")] == "1");

            Check("Q8 CSV: no detector residual is mislabelled as unidentified peptide or non-peptide",
                !header.Contains("unidentified_peptidelike_fraction")
                && !header.Contains("non_peptide_fraction"), string.Join(",", header));

            string peptideCsv = AnalyticsCsv.BuildPeptideCsv(data.Peptides);
            var pHeader = SplitCsvLine(peptideCsv.Split('\n')[0]).ToList();
            Check("Q8 CSV: the per-peptide table carries the six isotope columns and the under-sampled pair",
                pHeader.Contains("area_m4") && pHeader.Contains("area_m5")
                && pHeader.Contains("under_sampled") && pHeader.Contains("under_sampled_estimate"),
                string.Join(",", pHeader));

            AnalyticsPeptide? thin = data.Peptides.FirstOrDefault(x => x.Sequence == "THINK");
            Check("Q8 CSV: a refused peptide reaches the table with its m/z and its under-sampled area",
                thin != null && thin.UnderSampledArea == 30.0 && thin.Envelope == 0
                && thin.Mass > 0 && thin.IsotopesCredited == 0,
                thin == null ? "missing" : thin.Mass.ToString("F2", CultureInfo.InvariantCulture));

            // ---- the chart draws the block rather than folding it away --------------------------------
            var chart = new RecoveryChart(data.Fractions, data.Grouped);
            int ink = RenderChartInk(chart, Path.Combine(workDir, "recovery_undersampled.png"));
            Check("Q8 chart: the recovery chart still draws the three scientifically separated readings",
                ink > 500, ink.ToString());
        }

        /// <summary>Replaces a container's quant table with the LEGACY 12-column layout, so the reader's
        /// back-compatibility can be checked against a real file rather than asserted.</summary>
        private static void WriteLegacyQuantEntry(string fqprojPath, List<QuantRow> rows)
        {
            using var zip = System.IO.Compression.ZipFile.Open(
                fqprojPath, System.IO.Compression.ZipArchiveMode.Update);

            zip.GetEntry(ProjectStore.QuantEntryName)?.Delete();

            System.IO.Compression.ZipArchiveEntry entry = zip.CreateEntry(
                ProjectStore.QuantEntryName, System.IO.Compression.CompressionLevel.NoCompression);

            using Stream stream = entry.Open();
            using var gzip = new System.IO.Compression.GZipStream(
                stream, System.IO.Compression.CompressionMode.Compress);
            using var writer = new StreamWriter(gzip, new UTF8Encoding(false));

            writer.Write(string.Join("\t", ProjectStore.LegacyQuantColumns));
            writer.Write('\n');

            foreach (QuantRow r in rows)
            {
                writer.Write(r.Side); writer.Write('\t');
                writer.Write(r.Run); writer.Write('\t');
                writer.Write(r.Scan.ToString(CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.PrecursorMz.ToString("R", CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.Charge.ToString(CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.Sequence); writer.Write('\t');
                for (int i = 0; i < 4; i++)
                {
                    writer.Write(i < r.IsotopeAreas.Length
                        ? r.IsotopeAreas[i].ToString("R", CultureInfo.InvariantCulture)
                        : string.Empty);
                    writer.Write('\t');
                }
                writer.Write(r.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.FamilyUserId.HasValue
                    ? r.FamilyUserId.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
                writer.Write('\n');
            }
        }

        /// <summary>Reads the package-2 rows of the stored quants export, header-driven and parsed with
        /// InvariantCulture (the file holds invariant decimals). Package 2 is run _01, the permuted
        /// mapping the de novo XIC gate establishes and this test reuses.</summary>
        private static List<Pkg2StoredQuant> ReadPkg2StoredQuants(string path)
        {
            using var reader = new StreamReader(path);
            string? header = reader.ReadLine();
            if (header == null) throw new InvalidDataException("The stored quants file is empty: " + path);

            string[] cols = header.Split('\t');
            int Col(string name)
            {
                for (int i = 0; i < cols.Length; i++)
                    if (string.Equals(cols[i].Trim(), name, StringComparison.OrdinalIgnoreCase)) return i;
                throw new InvalidDataException("The stored quants file has no \"" + name + "\" column.");
            }

            int iPkg = Col("pkg"), iZ = Col("z"), iArea = Col("quantArea"), iMz = Col("precursorMz"), iScan = Col("scanNoMS2");

            var rows = new List<Pkg2StoredQuant>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                string[] f = line.Split('\t');
                if (f.Length <= iScan) continue;
                if (int.Parse(f[iPkg], CultureInfo.InvariantCulture) != 2) continue;

                rows.Add(new Pkg2StoredQuant(
                    int.Parse(f[iScan], CultureInfo.InvariantCulture),
                    int.Parse(f[iZ], CultureInfo.InvariantCulture),
                    double.Parse(f[iArea], NumberStyles.Float, CultureInfo.InvariantCulture),
                    double.Parse(f[iMz], NumberStyles.Float, CultureInfo.InvariantCulture)));
            }
            return rows;
        }
    }
}
