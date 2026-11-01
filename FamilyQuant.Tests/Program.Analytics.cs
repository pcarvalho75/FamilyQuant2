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

        // ============================================================= analytics (Q4)

        /// <summary>A quant row with the full isotope vector spelled out, for the analytics tests that
        /// care about how many isotopes were credited (the <see cref="Qr"/> helper only sets M+0).</summary>
        private static QuantRow AnQr(string side, string run, int scan, int z, string seq,
                                     double mz, double[] areas, int? family = null)
        {
            return new QuantRow
            {
                Side = side,
                Run = run,
                Scan = scan,
                Charge = z,
                Sequence = seq,
                PrecursorMz = mz,
                IsotopeAreas = areas,
                EnvelopeSum = areas.Where(a => a > 0).Sum(),
                FamilyUserId = family,
            };
        }

        /// <summary>A small populated store for the UI sweep, so the analytics screen lays out real charts
        /// and real numbers rather than its empty state.</summary>
        private static List<QuantRow> SyntheticAnalyticsRows()
        {
            var rows = new List<QuantRow>();
            var rng = new Random(2024);
            foreach (string run in new[] { "ui_01", "ui_02" })
            {
                for (int i = 0; i < 60; i++)
                {
                    int z = 2 + (i % 2);
                    double mz = 400 + rng.NextDouble() * 900;
                    int credited = 1 + rng.Next(4);
                    var areas = new double[4];
                    for (int k = 0; k < credited; k++) areas[k] = 1000 * Math.Pow(10, rng.NextDouble() * 3) / (k + 1);
                    rows.Add(AnQr("identified", run, 100 + i, z, "SEQ" + run + i, mz, areas));
                }
                for (int i = 0; i < 20; i++)
                {
                    int z = 2 + (i % 2);
                    double mz = 400 + rng.NextDouble() * 900;
                    int credited = 1 + rng.Next(3);
                    var areas = new double[4];
                    for (int k = 0; k < credited; k++) areas[k] = 800 * Math.Pow(10, rng.NextDouble() * 3) / (k + 1);
                    rows.Add(AnQr("denovo", run, 5000 + i, z, "DNV" + run + i, mz, areas));
                }
            }
            return rows;
        }

        /// <summary>
        /// The analytics engine (Q4) on a machine that has never seen a RAW: the mass inversion, the
        /// isotopes-credited count, that the analytics dedup lands on the SAME representative set as
        /// <see cref="NativeQuantFractions.DedupToPeptides"/>, and that both CSV tables round-trip.
        /// </summary>
        private static void AnalyticsUnitTests(string workDir)
        {
            // ---- mass = (m/z - proton) * charge -----------------------------------------------------
            Check("mass from m/z and charge is the doubly-charged inversion",
                Math.Abs(AnalyticsBuilder.MonoisotopicMass(500.25, 2) - (500.25 - 1.007276) * 2) < 1e-9,
                AnalyticsBuilder.MonoisotopicMass(500.25, 2).ToString("R", CultureInfo.InvariantCulture));
            Check("and the singly-charged inversion",
                Math.Abs(AnalyticsBuilder.MonoisotopicMass(401.2, 1) - (401.2 - 1.007276)) < 1e-9,
                AnalyticsBuilder.MonoisotopicMass(401.2, 1).ToString("R", CultureInfo.InvariantCulture));
            Check("a zero charge cannot be inverted and yields zero, not a throw",
                AnalyticsBuilder.MonoisotopicMass(500.0, 0) == 0.0);

            // ---- isotopes credited = count of area_mN > 0 -------------------------------------------
            Check("only M+0 and M+1 credited counts as two",
                AnalyticsBuilder.CountIsotopesCredited(new[] { 90.0, 180.0, 0.0, 0.0 }) == 2);
            Check("only M+0 credited counts as one",
                AnalyticsBuilder.CountIsotopesCredited(new[] { 100.0, 0.0, 0.0, 0.0 }) == 1);
            Check("a full envelope counts as four",
                AnalyticsBuilder.CountIsotopesCredited(new[] { 10.0, 20.0, 30.0, 40.0 }) == 4);
            Check("a gap in the middle is still counted per slot, not run-length",
                AnalyticsBuilder.CountIsotopesCredited(new[] { 5.0, 0.0, 7.0, 0.0 }) == 2);
            Check("a sixth isotope is credited when it carries area",
                AnalyticsBuilder.CountIsotopesCredited(new[] { 9.0, 8.0, 7.0, 6.0, 5.0, 4.0 }) == 6);
            Check("and an absent sixth isotope credits nothing rather than being filled in",
                AnalyticsBuilder.CountIsotopesCredited(new[] { 9.0, 8.0, 7.0, 6.0, 5.0, 0.0 }) == 5);
            Check("the count never exceeds the six-isotope basis",
                AnalyticsBuilder.CountIsotopesCredited(new[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 })
                    == AnalyticsBuilder.IsotopeBasis);

            // ---- the analytics dedup is the SAME representative set as NativeQuantFractions ----------
            // A peptide re-anchored three times inside one peak, plus two others, on both sides.
            var rows = new List<QuantRow>
            {
                AnQr("identified", "an_01", 10, 2, "PEPTIDEK", 500.25, new[] { 90.0, 40.0, 0.0, 0.0 }),
                AnQr("identified", "an_01", 12, 2, "PEPTIDEK", 500.25, new[] { 100.0, 50.0, 10.0, 0.0 }),  // max M+0
                AnQr("identified", "an_01", 14, 2, "PEPTIDEK", 500.25, new[] { 95.0, 44.0, 0.0, 0.0 }),
                AnQr("identified", "an_01", 20, 3, "LIGHTR", 350.10, new[] { 60.0, 0.0, 0.0, 0.0 }, family: 7),
                AnQr("denovo", "an_01", 30, 2, "DENOVOAK", 640.30, new[] { 40.0, 20.0, 8.0, 4.0 }),
            };

            List<AnalyticsPeptide> analytics = AnalyticsBuilder.BuildPeptides(rows);
            List<NativePeptideQuant> canonical = NativeQuantFractions.DedupToPeptides(rows);

            string KeyA(AnalyticsPeptide p) => p.Run + "|" + p.Side + "|" + p.Sequence + "|" + p.Charge;
            string KeyN(NativePeptideQuant p) => p.Run + "|" + p.Side + "|" + p.Sequence + "|" + p.Charge;

            Check("the analytics collapse yields exactly the dedup's peptide count",
                analytics.Count == canonical.Count, analytics.Count + " vs " + canonical.Count);
            Check("and the same representative run/side/peptidoform/charge keys",
                analytics.Select(KeyA).OrderBy(k => k, StringComparer.Ordinal)
                    .SequenceEqual(canonical.Select(KeyN).OrderBy(k => k, StringComparer.Ordinal)));

            var canonByKey = canonical.ToDictionary(KeyN, p => p, StringComparer.Ordinal);
            bool m0Match = analytics.All(a =>
                BitConverter.DoubleToInt64Bits(a.AreaAt(0)) == BitConverter.DoubleToInt64Bits(canonByKey[KeyA(a)].AreaM0)
                && BitConverter.DoubleToInt64Bits(a.Envelope) == BitConverter.DoubleToInt64Bits(canonByKey[KeyA(a)].EnvelopeSum));
            Check("the M+0 and the envelope are the dedup's own numbers, bit for bit", m0Match);

            AnalyticsPeptide rep = analytics.Single(p => p.Sequence == "PEPTIDEK");
            Check("the representative is the largest-M+0 anchor, so its isotopes are that anchor's",
                rep.AreaAt(0) == 100.0 && rep.IsotopesCredited == 3, rep.IsotopesCredited.ToString());
            Check("the representative carries the mass its m/z and charge imply",
                Math.Abs(rep.Mass - (500.25 - 1.007276) * 2) < 1e-9);
            Check("a family on the kept anchor rides along",
                analytics.Single(p => p.Sequence == "LIGHTR").FamilyUserId == 7);

            // ---- the isotope histogram bins every peptide exactly once ------------------------------
            var hist = analytics.GroupBy(p => Math.Min(AnalyticsBuilder.IsotopeBasis, p.IsotopesCredited))
                                .ToDictionary(g => g.Key, g => g.Count());
            Check("the isotope histogram sums to the peptide count",
                hist.Values.Sum() == analytics.Count, hist.Values.Sum() + " vs " + analytics.Count);

            // ---- both CSV tables round-trip ---------------------------------------------------------
            var tic = new Dictionary<string, double> { ["an_01"] = 2000.0 };
            AnalyticsData data = AnalyticsBuilder.Build(rows, tic);

            string peptideCsv = AnalyticsCsv.BuildPeptideCsv(data.Peptides);
            var pLines = peptideCsv.Split('\n').Where(l => l.Length > 0).ToList();
            Check("the peptide CSV is the header plus one row per peptide",
                pLines.Count == data.Peptides.Count + 1, pLines.Count.ToString());
            Check("the peptide CSV header is the documented column set",
                pLines[0] == string.Join(",", AnalyticsCsv.PeptideColumns), pLines[0]);

            var header = SplitCsvLine(pLines[0]);
            int massCol = header.ToList().IndexOf("mass");
            int isoCol = header.ToList().IndexOf("isotopes_credited");
            int seqCol = header.ToList().IndexOf("sequence");
            var repLine = pLines.Skip(1).Select(SplitCsvLine).Single(f => f[seqCol] == "PEPTIDEK");
            Check("a peptide row round-trips its mass under InvariantCulture",
                Math.Abs(double.Parse(repLine[massCol], CultureInfo.InvariantCulture) - rep.Mass) < 1e-6,
                repLine[massCol]);
            Check("and its credited-isotope count",
                int.Parse(repLine[isoCol], CultureInfo.InvariantCulture) == 3, repLine[isoCol]);
            Check("the CSV uses a dot decimal separator whatever the machine's culture",
                repLine[massCol].Contains('.') && !repLine[massCol].Contains(','), repLine[massCol]);

            string summaryCsv = AnalyticsCsv.BuildSummaryCsv(data.Fractions, data.Grouped);
            var sLines = summaryCsv.Split('\n').Where(l => l.Length > 0).ToList();
            Check("the summary CSV is the header, the one run and the grouped row",
                sLines.Count == data.Fractions.Count + 2, sLines.Count.ToString());
            Check("the summary CSV header is the documented column set",
                sLines[0] == string.Join(",", AnalyticsCsv.SummaryColumns), sLines[0]);
            Check("the summary CSV marks the grouped row and none of the run rows",
                SplitCsvLine(sLines[sLines.Count - 1])[1] == "1"
                && sLines.Skip(1).Take(data.Fractions.Count).All(l => SplitCsvLine(l)[1] == "0"));

            // Written to disk and read back, to prove the file itself round-trips, not only the builder.
            string peptidePath = Path.Combine(workDir, "analytics_peptides_synth.csv");
            File.WriteAllText(peptidePath, peptideCsv);
            Check("the peptide CSV survives a disk round trip",
                File.ReadAllText(peptidePath) == peptideCsv);

            // ---- the family chart documents the identity threshold it stands on ---------------------
            // The de novo census can be run at a different identity from the one a figure is built with,
            // so a chart that does not name its threshold can silently disagree with the census beside it
            // and neither number looks wrong. The heading is where the figure says so.
            var stated = new FamilyEnvelopeQuantResult { IdentityThreshold = 0.80 };
            stated.Families.Add(new FamilyEnvelopeQuantRow
            {
                FamilyUserId = 3, Description = "Metalloproteinase", IdentifiedEnvelope = 500, DeNovoEnvelope = 120,
            });

            var statedChart = new FamilyContributionChart(stated);
            Check("the family chart's heading STATES the identity threshold the figure stands on",
                statedChart.Heading.Contains("80", StringComparison.Ordinal)
                && statedChart.Heading.Contains("identity", StringComparison.Ordinal),
                statedChart.Heading);
            Check("and the chart carries the threshold as a number, not only as text",
                statedChart.IdentityThreshold == 0.80,
                statedChart.IdentityThreshold.ToString("R", CultureInfo.InvariantCulture));

            var unstated = new FamilyEnvelopeQuantResult();
            unstated.Families.Add(new FamilyEnvelopeQuantRow { FamilyUserId = 3, IdentifiedEnvelope = 500 });
            Check("with no threshold to state the heading claims none rather than inventing one",
                !new FamilyContributionChart(unstated).Heading.Contains("identity", StringComparison.Ordinal),
                new FamilyContributionChart(unstated).Heading);

            // The real path records it: BuildFromCuration stamps the identity its homology search used.
            var curated = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "sp|P1|TEST", Description = "Phospholipase A2",
                        Sequence = "PEPTIDEKLIGHTR", USearchID = 1, UserID = 1,
                    },
                },
            };
            FamilyEnvelopeQuantResult fromCuration = FamilyEnvelopeQuant.BuildFromCuration(rows, curated);
            Check("BuildFromCuration records the identity its homology search actually ran at",
                fromCuration.IdentityThreshold == FamilyQuant.Homology.HomologyOptions.DefaultMinIdentity,
                fromCuration.IdentityThreshold.ToString("R", CultureInfo.InvariantCulture));

            // ---- the empty store is a table with no rows, never a throw -----------------------------
            AnalyticsData empty = AnalyticsBuilder.Build(new List<QuantRow>(), new Dictionary<string, double>());
            Check("an empty store yields an empty table and no grouped row, not a crash",
                empty.Peptides.Count == 0 && empty.Grouped == null);
        }

        /// <summary>
        /// Each of the four analytics charts renders to a PNG the way the identity-sweep chart test does,
        /// for a small synthetic quant set and for an empty one (which must draw a "nothing to show" state
        /// rather than crash). Headless, so it runs anywhere and needs no RAW.
        /// </summary>
        private static void AnalyticsRenderTests(string workDir)
        {
            var rows = new List<QuantRow>();
            var rng = new Random(1975);
            string[] runs = { "an_01", "an_02" };
            foreach (string run in runs)
            {
                for (int i = 0; i < 120; i++)
                {
                    int z = 2 + (i % 2);
                    double mz = 400 + rng.NextDouble() * 900;
                    int credited = 1 + rng.Next(4);
                    var areas = new double[4];
                    for (int k = 0; k < credited; k++) areas[k] = 1000 * Math.Pow(10, rng.NextDouble() * 3) / (k + 1);
                    rows.Add(AnQr("identified", run, 100 + i, z, "SEQ" + run + i, mz, areas));
                }
                for (int i = 0; i < 40; i++)
                {
                    int z = 2 + (i % 2);
                    double mz = 400 + rng.NextDouble() * 900;
                    int credited = 1 + rng.Next(3);
                    var areas = new double[4];
                    for (int k = 0; k < credited; k++) areas[k] = 800 * Math.Pow(10, rng.NextDouble() * 3) / (k + 1);
                    rows.Add(AnQr("denovo", run, 5000 + i, z, "DNV" + run + i, mz, areas));
                }
            }
            var tic = new Dictionary<string, double> { ["an_01"] = 5.0e8, ["an_02"] = 4.0e8 };
            AnalyticsData data = AnalyticsBuilder.Build(rows, tic);

            var populated = new (string Name, FrameworkElement Chart)[]
            {
                ("xic_by_mass", new XicMassChart(data.Peptides)),
                ("isotopes_credited", new IsotopeCountChart(data.Peptides)),
                ("percent_ion_current", new PercentIonCurrentChart(data.Peptides, data.Grouped?.Tic ?? 0)),
                ("recovery", new RecoveryChart(data.Fractions, data.Grouped)),
            };

            foreach (var (name, chart) in populated)
            {
                int ink = RenderChartInk(chart, Path.Combine(workDir, "analytics_" + name + ".png"));
                Check("the " + name + " chart draws something for a populated set", ink > 500, ink.ToString());
            }

            // The empty set: every chart must render without a throw and show its "nothing to show" state.
            var emptyPeptides = new List<AnalyticsPeptide>();
            var emptyCharts = new (string Name, FrameworkElement Chart)[]
            {
                ("xic_by_mass_empty", new XicMassChart(emptyPeptides)),
                ("isotopes_credited_empty", new IsotopeCountChart(emptyPeptides)),
                ("percent_ion_current_empty", new PercentIonCurrentChart(emptyPeptides)),
                ("recovery_empty", new RecoveryChart(new List<NativeRunFraction>(), null)),
            };

            foreach (var (name, chart) in emptyCharts)
            {
                bool ok = true;
                try { RenderChartInk(chart, Path.Combine(workDir, "analytics_" + name + ".png")); }
                catch (Exception ex) { ok = false; Console.WriteLine("        " + name + " threw: " + ex.Message); }
                Check("the " + name + " chart renders an empty state without crashing", ok);
            }

            // The control itself, headless: populated builds the five charts, empty shows the guidance.
            var control = new AnalyticsControl(rows, new QuantManifest { PerRunTic = tic });
            Check("the populated control holds its analytics and five charts",
                control.Data != null
                && CountVisual<XicMassChart>(control) == 1 && CountVisual<IsotopeCountChart>(control) == 1
                && CountVisual<PercentIonCurrentChart>(control) == 1 && CountVisual<RecoveryChart>(control) == 1
                && CountVisual<FamilyContributionChart>(control) == 1);

            // With no curation the family chart is still THERE, in its own empty state: a section that
            // vanishes leaves the reader with no way to learn that it exists.
            Check("with no Family Cluster File the family chart is present and empty, not absent",
                control.Families == null
                && WalkTree(control).OfType<FamilyContributionChart>().Single().Bars.Count == 0);
            Check("and the status line says which file would fill it",
                UiField<TextBlock>(control, "TextBlockStatus")!.Text.Contains("No Family Cluster File is open"),
                UiField<TextBlock>(control, "TextBlockStatus")!.Text);

            var emptyControl = new AnalyticsControl(new List<QuantRow>(), null);
            Check("the control with no quant shows the compute-first message and draws no chart",
                emptyControl.Data == null && CountVisual<XicMassChart>(emptyControl) == 0
                && CountVisual<FamilyContributionChart>(emptyControl) == 0);
        }

        /// <summary>
        /// Q4 on the real data: build the analytics from run _01's own store (reusing the Q1/Q2/Q3
        /// extraction and the caches the Q3 section already wrote, so no extra RAW load), and prove the
        /// recovery numbers the graphs stand on reproduce the current six-isotope, three-point reference
        /// dataset. These are release-regression pins for this exact input, not universal biological
        /// constants or values to copy into a talk without regenerating the evidence pack. Also proves
        /// that the isotope histogram bins every peptide and that the exported CSV round-trips.
        /// </summary>
        private static void AnalyticsRealTests(string? peaksDir, string? desktopDir, string workDir)
        {
            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");
            string? raw = desktopDir == null ? null
                : Path.Combine(desktopDir, "20220624_Bjararaca_1ug_3step-240min_01.raw");

            if (peaksDir == null || !Directory.Exists(peaksDir) || sepr2 == null || !File.Exists(sepr2)
                || raw == null || !File.Exists(raw))
            {
                Console.WriteLine("  SKIP  analytics real data - needs the PEAKS directory, PLVI.sepr2 and the _01 RAW");
                skipped++;
                return;
            }

            string run01 = DeNovoReconciler.NormalizeRunName(raw);

            // The SAME cache files the Q3 real section wrote, so this pays no RAW load when that ran first.
            Func<string, string, string> cacheFor = (side, run) => side == "identified"
                ? Path.Combine(workDir, "identified_xic_run01_n6.json")
                : Path.Combine(workDir, "q3_denovo_xic_run01_n6.json");

            var options = new FullXicOptions
            {
                IsotopeCount = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount,
                AppVersion = "test-q4",
                // Restricted to _01: the partial spectrum table goes to the harness, not the data directory.
                SpectrumComparisonCsvPath = Path.Combine(workDir, "q4_run01_spectrum_evidence.csv"),
            };

            FullXicComputeResult store;
            try
            {
                store = FullXicComputer.ComputeFullXic(
                    desktopDir!, sepr2, peaksDir, options, cacheFor,
                    m => Console.WriteLine("        " + m), new[] { run01 },
                    run => Path.Combine(workDir, "ms1envelope_" + run + ".json"));
            }
            catch (Exception ex)
            {
                Check("the analytics compute path runs against the _01 RAW", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            AnalyticsData data = AnalyticsBuilder.Build(store.Rows, store.Manifest.PerRunTic);

            Check("analytics real: the table carries both identified and de novo peptides for _01",
                data.IdentifiedCount > 1000 && data.DeNovoCount > 100,
                data.IdentifiedCount.ToString("N0") + " id / " + data.DeNovoCount.ToString("N0") + " dn");

            NativeRunFraction? g = data.Grouped;
            Check("analytics real: a grouped fraction with a positive TIC was built",
                g != null && g.Tic > 0, g?.Tic.ToString("E4", CultureInfo.InvariantCulture));
            if (g == null) return;

            Console.WriteLine("        RECOVERY (run _01): identified envelope " +
                g.IdentifiedFractionEnvelope.ToString("P2", CultureInfo.InvariantCulture) + " | de novo envelope " +
                g.DeNovoFractionEnvelope.ToString("P2", CultureInfo.InvariantCulture) + " | recovered " +
                g.RecoveredFractionEnvelope.ToString("P2", CultureInfo.InvariantCulture));

            // Dataset-specific regression pins (Run _01 envelope fractions), NOT universal biological
            // constants. Regenerated on real data after the isotope-offset dedup correction: the JOIN and
            // cross-side suppression are now isotope-error-aware (M, M+-1, M+-2), which removes the
            // remaining double credit that inflated the identified and recovered shares. The narrow
            // tolerance covers floating-point integration order only.
            Check("analytics real: identified envelope fraction reproduces the current reference dataset",
                Math.Abs(g.IdentifiedFractionEnvelope - 0.2784) < 0.005,
                g.IdentifiedFractionEnvelope.ToString("P3", CultureInfo.InvariantCulture));
            Check("analytics real: de novo envelope fraction reproduces the current reference dataset",
                Math.Abs(g.DeNovoFractionEnvelope - 0.2287) < 0.005,
                g.DeNovoFractionEnvelope.ToString("P3", CultureInfo.InvariantCulture));
            Check("analytics real: recovered envelope fraction reproduces the current reference dataset",
                Math.Abs(g.RecoveredFractionEnvelope - 0.5072) < 0.005,
                g.RecoveredFractionEnvelope.ToString("P3", CultureInfo.InvariantCulture));

            // ---- the identified-but-not-quantified block, on the real data ---------------------------
            // Reported whether or not it is zero: "nothing was excluded" and "exclusions were not
            // reported" are different facts, and only the first is a result.
            NativeQuantFractions.ApplyDetectedToFractions(data.Fractions, data.Grouped,
                                                          store.Manifest.DerivedFractions);

            Console.WriteLine("        IDENTIFIED BUT NOT QUANTIFIED (minimum " +
                store.Manifest.MinPointsForQuant.ToString(CultureInfo.InvariantCulture) +
                " point(s) per peak): identified " +
                g.IdentifiedUnderSampled.ToString("E4", CultureInfo.InvariantCulture) + " over " +
                g.IdentifiedUnderSampledPeptides.ToString("N0") + " peptide(s), de novo " +
                g.DeNovoUnderSampled.ToString("E4", CultureInfo.InvariantCulture) + " over " +
                g.DeNovoUnderSampledPeptides.ToString("N0") + " peptide(s) = " +
                g.UnderSampledFraction.ToString("P4", CultureInfo.InvariantCulture) + " of TIC" +
                (g.UnderSampledIsEstimate ? " (includes an ESTIMATE)" : string.Empty));

            // PatternTools pads every cluster with a zero row before its first scan and one after its
            // last, so no real cluster is shorter than three points and nothing on this acquisition is
            // excluded at the production minimum. The rule is a guard against the one branch that is not
            // an integral, not an active filter; if that ever changed, this check would say so.
            Check("analytics real: the production 3-point minimum excludes nothing on run _01",
                g.UnderSampledFraction == 0
                && g.IdentifiedUnderSampledPeptides == 0 && g.DeNovoUnderSampledPeptides == 0,
                g.UnderSampledFraction.ToString("P6", CultureInfo.InvariantCulture));

            // ---- the independent detector remains explicitly diagnostic -------------------------------
            Check("analytics real: a resolved isotope-series diagnostic is present",
                g.HasDetectedPeptideLike, g.DetectedFractionPeptideLike.ToString("P3", CultureInfo.InvariantCulture));
            Check("analytics real: detector diagnostic is finite and independently bounded by TIC",
                !double.IsNaN(g.DetectedFractionPeptideLike)
                && g.DetectedFractionPeptideLike >= 0 && g.DetectedFractionPeptideLike <= 1,
                g.DetectedFractionPeptideLike.ToString("P3", CultureInfo.InvariantCulture));

            Console.WriteLine("        DISTINCT READINGS (run _01, share of TIC): reference-envelope model estimate " +
                g.RecoveredFractionReferenceCalibrated.ToString("P2", CultureInfo.InvariantCulture) +
                " | M0 sensitivity " + g.RecoveredFractionM0.ToString("P2", CultureInfo.InvariantCulture) +
                " | unadjusted six-window diagnostic " + g.RecoveredFractionEnvelope.ToString("P2", CultureInfo.InvariantCulture) +
                " | resolved isotope-series detector diagnostic " +
                g.DetectedFractionPeptideLike.ToString("P2", CultureInfo.InvariantCulture));

            // The isotope histogram bins every peptide exactly once.
            int basis = AnalyticsBuilder.IsotopeBasis;
            var hist = data.Peptides.GroupBy(p => Math.Min(basis, p.IsotopesCredited)).ToDictionary(x => x.Key, x => x.Count());
            Check("analytics real: the isotope histogram sums to the peptide count",
                hist.Values.Sum() == data.Peptides.Count, hist.Values.Sum() + " vs " + data.Peptides.Count);
            Check("analytics real: most peptides fall short of the six-isotope ceiling",
                (hist.TryGetValue(basis, out int full) ? full : 0) < data.Peptides.Count / 2,
                (hist.TryGetValue(basis, out int f4) ? f4 : 0) + " of " + data.Peptides.Count);
            Console.WriteLine("        isotopes credited: " + string.Join(" | ",
                Enumerable.Range(1, basis).Select(k =>
                    k.ToString() + ": " + (hist.TryGetValue(k, out int c) ? c : 0).ToString("N0"))));

            // The exported peptide CSV round-trips: one data row per peptide, and its mass column parses.
            string csvPath = Path.Combine(workDir, "analytics_peptides_run01.csv");
            File.WriteAllText(csvPath, AnalyticsCsv.BuildPeptideCsv(data.Peptides));
            var lines = File.ReadAllLines(csvPath).Where(l => l.Length > 0).ToList();
            Check("analytics real: the peptide CSV is the header plus one row per peptide",
                lines.Count == data.Peptides.Count + 1, lines.Count + " vs " + (data.Peptides.Count + 1));
            Check("analytics real: the CSV round-trips its columns and dot decimals",
                lines[0] == string.Join(",", AnalyticsCsv.PeptideColumns)
                && SplitCsvLine(lines[1]).Length == AnalyticsCsv.PeptideColumns.Length);

            Console.WriteLine("        PAPER NUMBERS (run _01, analytics envelope recovery): identified " +
                g.IdentifiedFractionEnvelope.ToString("P2", CultureInfo.InvariantCulture) + " | de novo " +
                g.DeNovoFractionEnvelope.ToString("P2", CultureInfo.InvariantCulture) + " | recovered " +
                g.RecoveredFractionEnvelope.ToString("P2", CultureInfo.InvariantCulture));
        }
    }
}
