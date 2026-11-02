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

        // ============================================================ MS1 peptide-like envelope (Q6)

        /// <summary>Builds one synthetic MS1 scan from (m/z, intensity) peaks.</summary>
        private static PatternTools.MSParserLight.MSUltraLight Ms1Scan(
            int scanNo, double rtMinutes, params (double mz, double intensity)[] peaks)
        {
            return new PatternTools.MSParserLight.MSUltraLight
            {
                ScanNumber = scanNo,
                CromatographyRetentionTime = rtMinutes,
                MSLevel = 1,
                Ions = peaks.Select(p => (p.mz, p.intensity)).ToList(),
            };
        }

        /// <summary>
        /// The MS1 envelope detector on SYNTHETIC spectra, so its invariants are proved on a machine that
        /// has never seen a RAW: a clean 3-isotope envelope is detected at the right charge and integrated;
        /// a lone peak is not counted; two series at different charges are separated; an envelope spanning
        /// several scans integrates once; a one-scan blip is not a feature; and the detector is
        /// deterministic.
        /// </summary>
        private static void Ms1EnvelopeUnitTests()
        {
            double spacing = Ms1EnvelopeDetector.Carbon13Spacing;
            double z2 = spacing / 2.0;
            double z3 = spacing / 3.0;

            // ---- a clean 3-isotope envelope at charge 2, plus a lone peak that must not count ----------
            var scanA = Ms1Scan(1, 10.0,
                (500.0, 1000), (500.0 + z2, 500), (500.0 + 2 * z2, 250), (800.0, 300));
            var envsA = Ms1EnvelopeDetector.DetectInScan(scanA);
            Check("Q6 unit: a clean 3-isotope envelope is detected", envsA.Count == 1, envsA.Count.ToString());
            Check("Q6 unit: it is read at the right charge (2)",
                envsA.Count == 1 && envsA[0].Charge == 2, envsA.Count == 1 ? envsA[0].Charge.ToString() : "-");
            Check("Q6 unit: the envelope intensity sums its isotope peaks (1000+500+250)",
                envsA.Count == 1 && Math.Abs(envsA[0].Intensity - 1750.0) < 1e-9,
                envsA.Count == 1 ? envsA[0].Intensity.ToString("R", CultureInfo.InvariantCulture) : "-");
            Check("Q6 unit: a lone peak with no isotope partner is not counted as an envelope",
                envsA.Count == 1);

            // ---- a scan of one lone peak yields nothing ----------------------------------------------
            Check("Q6 unit: a scan of a single lone peak yields no envelope",
                Ms1EnvelopeDetector.DetectInScan(Ms1Scan(1, 10.0, (600.0, 900))).Count == 0);

            // ---- two isotope series at different charges are separated --------------------------------
            var scanC = Ms1Scan(1, 10.0,
                (500.0, 1000), (500.0 + z2, 700), (500.0 + 2 * z2, 400),
                (900.0, 800), (900.0 + z3, 500), (900.0 + 2 * z3, 300));
            var envsC = Ms1EnvelopeDetector.DetectInScan(scanC);
            Check("Q6 unit: two isotope series at different charges are separated", envsC.Count == 2,
                envsC.Count.ToString());
            int[] charges = envsC.Select(e => e.Charge).OrderBy(x => x).ToArray();
            Check("Q6 unit: the two series read as charge 2 and charge 3",
                charges.SequenceEqual(new[] { 2, 3 }), string.Join(",", charges));

            // ---- an envelope spanning several scans integrates once ----------------------------------
            const double dt = 0.1;
            var span = new List<PatternTools.MSParserLight.MSUltraLight>();
            for (int s = 0; s < 5; s++)
                span.Add(Ms1Scan(s + 1, 10.0 + s * dt,
                    (500.0, 1000), (500.0 + z2, 500), (500.0 + 2 * z2, 250), (800.0, 200)));

            Ms1EnvelopeDetectionResult rSpan = Ms1EnvelopeDetector.Detect("span", span);
            Check("Q6 unit: an envelope spanning several scans integrates once (one feature)",
                rSpan.DetectedEnvelopeCount == 1, rSpan.DetectedEnvelopeCount.ToString());
            // Constant envelope intensity 1750 over five scans 0.1 min apart: 4 segments x 0.1 x 1750 = 700.
            Check("Q6 unit: the feature's area is the trapezoid over its scans (700.0)",
                Math.Abs(rSpan.DetectedPeptideLikeArea - 700.0) < 1e-6,
                rSpan.DetectedPeptideLikeArea.ToString("R", CultureInfo.InvariantCulture));

            // ---- a one-scan detection is not a feature -----------------------------------------------
            var oneScan = new List<PatternTools.MSParserLight.MSUltraLight>
            {
                Ms1Scan(1, 10.0, (500.0, 1000), (500.0 + z2, 500), (500.0 + 2 * z2, 250)),
            };
            Ms1EnvelopeDetectionResult rOne = Ms1EnvelopeDetector.Detect("one", oneScan);
            Check("Q6 unit: a one-scan envelope is not counted or integrated",
                rOne.DetectedEnvelopeCount == 0 && rOne.DetectedPeptideLikeArea == 0.0,
                rOne.DetectedEnvelopeCount + "/" + rOne.DetectedPeptideLikeArea.ToString("R", CultureInfo.InvariantCulture));

            // ---- determinism: same spectra in, same numbers out --------------------------------------
            Ms1EnvelopeDetectionResult d1 = Ms1EnvelopeDetector.Detect("d", span);
            Ms1EnvelopeDetectionResult d2 = Ms1EnvelopeDetector.Detect("d", span);
            Check("Q6 unit: the detector is deterministic (count and area bit-identical)",
                d1.DetectedEnvelopeCount == d2.DetectedEnvelopeCount
                    && BitConverter.DoubleToInt64Bits(d1.DetectedPeptideLikeArea)
                        == BitConverter.DoubleToInt64Bits(d2.DetectedPeptideLikeArea));

            // ---- the synthetic three-level ordering holds: detected is a subset of a constructed TIC --
            // A TIC-like denominator: every peak of every span scan integrated (the envelope 1750 plus the
            // lone 200 = 1950 per scan). The detector diagnostic must sit at or below it.
            double syntheticTic = 0.0;
            for (int s = 1; s < span.Count; s++)
                syntheticTic += dt * 1950.0;   // constant 1950 over adjacent scans
            Check("Q6 unit: the detected area does not exceed the constructed TIC",
                rSpan.DetectedPeptideLikeArea <= syntheticTic + 1e-6,
                rSpan.DetectedPeptideLikeArea.ToString("R", CultureInfo.InvariantCulture) + " vs " +
                syntheticTic.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The MS1 envelope detector (Q6) on the real _01 RAW, wired through the compute path. Proves the
        /// paper's three-level ordering on real data: TIC &gt;= detected peptide-like &gt;= assigned
        /// (identified + de novo). Prints the three numbers. A detected value below the assigned envelope,
        /// or above the TIC, is a detector bug, so it is asserted, not merely reported. The detected number
        /// is an ESTIMATE (see <see cref="Ms1EnvelopeDetector"/>).
        /// </summary>
        private static void Ms1EnvelopeRealTests(string? peaksDir, string? desktopDir, string workDir)
        {
            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");
            string? raw = desktopDir == null ? null
                : Path.Combine(desktopDir, "20220624_Bjararaca_1ug_3step-240min_01.raw");

            if (peaksDir == null || !Directory.Exists(peaksDir) || sepr2 == null || !File.Exists(sepr2)
                || raw == null || !File.Exists(raw))
            {
                Console.WriteLine("  SKIP  MS1 peptide-like envelope (Q6) - needs the PEAKS directory, PLVI.sepr2 and the _01 RAW");
                skipped++;
                return;
            }

            string run01 = DeNovoReconciler.NormalizeRunName(raw);

            Func<string, string, string> cacheFor = (side, run) => side == "identified"
                ? Path.Combine(workDir, "identified_xic_run01_n6.json")
                : Path.Combine(workDir, "q3_denovo_xic_run01_n6.json");

            var options = new FullXicOptions
            {
                IsotopeCount = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount,
                AppVersion = "test-q6",
                // Restricted to _01: the partial spectrum table goes to the harness, not the data directory.
                SpectrumComparisonCsvPath = Path.Combine(workDir, "q6_run01_spectrum_evidence.csv"),
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
                Check("the Q6 compute path runs against the _01 RAW", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            if (!store.Detected.TryGetValue(run01, out Ms1EnvelopeDetectionResult? det) || det == null)
            {
                Check("Q6 real: the compute path produced a peptide-like envelope estimate for _01", false,
                    "no detection recorded");
                return;
            }

            NativeRunFraction f = NativeQuantFractions.Compute(store.Rows, store.Manifest.PerRunTic)
                .Single(x => x.Run == run01);

            double tic = f.Tic;
            double assignedArea = f.IdentifiedEnvelope + f.DeNovoEnvelope;
            double detectedArea = det.DetectedPeptideLikeArea;
            double assignedFrac = f.RecoveredFractionEnvelope;
            double detectedFrac = tic > 0 ? detectedArea / tic : 0.0;

            // THE THREE NUMBERS, printed.
            Console.WriteLine("        THREE-LEVEL DECOMPOSITION (run _01):");
            Console.WriteLine($"        TIC (100%)             = {tic.ToString("E6", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"        detected peptide-like  = {detectedArea.ToString("E6", CultureInfo.InvariantCulture)} " +
                $"({detectedFrac.ToString("P2", CultureInfo.InvariantCulture)} of TIC, {det.DetectedEnvelopeCount:N0} envelope(s)) [ESTIMATE]");
            Console.WriteLine($"        assigned (id+de novo)  = {assignedArea.ToString("E6", CultureInfo.InvariantCulture)} " +
                $"({assignedFrac.ToString("P2", CultureInfo.InvariantCulture)} of TIC)");

            // THE SPLIT BY CHARGE, printed with the level it decomposes. z >= 2 is the strong evidence
            // (a 0.5 or 0.33 Da isotope spacing is not something a singly charged small molecule makes);
            // z == 1 is a real isotope envelope whose evidence is weaker, NEVER a non-peptide block.
            double multiArea = det.DetectedAreaMultiplyCharged;
            double singleArea = det.DetectedAreaSinglyCharged;
            Console.WriteLine($"          z >= 2 ({Ms1EnvelopeDetector.StrongEvidenceLabel}) = " +
                $"{multiArea.ToString("E6", CultureInfo.InvariantCulture)} " +
                $"({(tic > 0 ? multiArea / tic : 0).ToString("P2", CultureInfo.InvariantCulture)} of TIC, " +
                $"{det.DetectedCountMultiplyCharged:N0} feature(s))");
            Console.WriteLine($"          z  = 1 ({Ms1EnvelopeDetector.WeakEvidenceLabel}) = " +
                $"{singleArea.ToString("E6", CultureInfo.InvariantCulture)} " +
                $"({(tic > 0 ? singleArea / tic : 0).ToString("P2", CultureInfo.InvariantCulture)} of TIC, " +
                $"{det.DetectedCountSinglyCharged:N0} feature(s))");
            for (int z = Ms1EnvelopeDetector.MinCharge; z <= Ms1EnvelopeDetector.MaxCharge; z++)
            {
                Console.WriteLine($"          z = {z}: " +
                    $"{det.DetectedAreaByCharge[z].ToString("E6", CultureInfo.InvariantCulture)} " +
                    $"({(tic > 0 ? det.DetectedAreaByCharge[z] / tic : 0).ToString("P2", CultureInfo.InvariantCulture)} of TIC, " +
                    $"{det.DetectedCountByCharge[z]:N0} feature(s))");
            }

            // The arithmetic, on real data and bit for bit: the total IS the sum of the two parts.
            Check("Q6 real: the charge split sums to the detected total bit for bit",
                BitConverter.DoubleToInt64Bits(singleArea + multiArea)
                    == BitConverter.DoubleToInt64Bits(detectedArea),
                (singleArea + multiArea).ToString("R", CultureInfo.InvariantCulture) + " vs " +
                detectedArea.ToString("R", CultureInfo.InvariantCulture));
            Check("Q6 real: the feature counts split the same way",
                det.DetectedCountSinglyCharged + det.DetectedCountMultiplyCharged == det.DetectedEnvelopeCount,
                det.DetectedCountSinglyCharged + "+" + det.DetectedCountMultiplyCharged + " vs " +
                det.DetectedEnvelopeCount);
            Check("Q6 real: the whole per-charge breakdown adds up to the detected total",
                Math.Abs(det.DetectedAreaByCharge.Sum() - detectedArea) <= 1e-9 * Math.Max(1, detectedArea),
                det.DetectedAreaByCharge.Sum().ToString("R", CultureInfo.InvariantCulture));
            Check("Q6 real: this dataset really does carry BOTH evidence groups, so the split is not decorative",
                multiArea > 0 && singleArea > 0,
                multiArea.ToString("E4", CultureInfo.InvariantCulture) + " / " +
                singleArea.ToString("E4", CultureInfo.InvariantCulture));

            // THE INVARIANT: TIC >= detected >= assigned. Un-clamped, on real data.
            Check("Q6 real: detected peptide-like does not exceed the TIC (detected <= 100%)",
                detectedArea <= tic, detectedArea.ToString("E6", CultureInfo.InvariantCulture) + " vs TIC " +
                tic.ToString("E6", CultureInfo.InvariantCulture));
            Check("Q6 real: detected peptide-like is at least the assigned envelope (detected >= assigned)",
                detectedArea >= assignedArea, detectedArea.ToString("E6", CultureInfo.InvariantCulture) + " vs assigned " +
                assignedArea.ToString("E6", CultureInfo.InvariantCulture));
            Check("Q6 real: the detected fraction lies between the assigned fraction and 100%",
                detectedFrac >= assignedFrac && detectedFrac <= 1.0,
                assignedFrac.ToString("P2", CultureInfo.InvariantCulture) + " <= " +
                detectedFrac.ToString("P2", CultureInfo.InvariantCulture) + " <= 100%");

            // The estimate is stored in the manifest's derived section, measured, and round-trips.
            Check("Q6 real: the compute path stored the detected estimate in the manifest",
                store.Manifest.DerivedFractions.Any(d => d.Run == run01 && !d.Grouped && d.DetectedPeptideLikeArea > 0)
                    && store.Manifest.Provenance.TryGetValue("detected_peptidelike_area", out string? pv) && pv == "measured",
                store.Manifest.DerivedFractions.Count(d => d.DetectedPeptideLikeArea > 0).ToString());

            string path = Path.Combine(workDir, "q6_run01.fqproj");
            var fcf = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|X|Y", USearchID = 0, UserID = 0 },
                },
            };
            ProjectStore.WriteFqproj(path, fcf, store.Rows, store.Manifest);
            var (_, _, mf2) = ProjectStore.ReadFqproj(path);
            DerivedRunFraction? d01 = mf2.DerivedFractions.FirstOrDefault(d => d.Run == run01 && !d.Grouped);
            Check("Q6 real: the detected estimate survives the .fqproj round trip bit for bit",
                d01 != null && BitConverter.DoubleToInt64Bits(d01.DetectedPeptideLikeArea)
                    == BitConverter.DoubleToInt64Bits(detectedArea),
                d01 == null ? "missing" : d01.DetectedPeptideLikeArea.ToString("R", CultureInfo.InvariantCulture));

            Check("Q6 real: the charge split survives the .fqproj round trip bit for bit too",
                d01 != null
                && BitConverter.DoubleToInt64Bits(d01.DetectedAreaMultiplyCharged)
                    == BitConverter.DoubleToInt64Bits(multiArea)
                && BitConverter.DoubleToInt64Bits(d01.DetectedAreaSinglyCharged)
                    == BitConverter.DoubleToInt64Bits(singleArea)
                && d01.DetectedAreaByCharge.Length == Ms1EnvelopeDetector.MaxCharge + 1,
                d01 == null ? "missing" : d01.DetectedAreaMultiplyCharged.ToString("R", CultureInfo.InvariantCulture));

            Check("Q6 real: the manifest states both diagnostic specificity groups without an FDR claim",
                mf2.Provenance.TryGetValue("detected_peptidelike_area_z_ge_2", out string? pvMulti)
                && pvMulti.Contains("more specific", StringComparison.OrdinalIgnoreCase)
                && pvMulti.Contains("not peptide-FDR calibrated", StringComparison.OrdinalIgnoreCase)
                && mf2.Provenance.TryGetValue("detected_peptidelike_area_z_eq_1", out string? pvSingle)
                && pvSingle.Contains("less specific", StringComparison.OrdinalIgnoreCase)
                && pvSingle.Contains("singly charged", StringComparison.OrdinalIgnoreCase),
                mf2.Provenance.TryGetValue("detected_peptidelike_area_z_eq_1", out string? pv1) ? pv1 : "missing");

            Console.WriteLine("        METHOD: " + Ms1EnvelopeDetector.MethodSentence);
            Console.WriteLine("        EVIDENCE: z >= 2 is " + Ms1EnvelopeDetector.StrongEvidenceLabel +
                              "; z = 1 is " + Ms1EnvelopeDetector.WeakEvidenceLabel + ". " +
                              Ms1EnvelopeDetector.SinglyChargedCaveat);
        }

        /// <summary>
        /// The detected peptide-like level SPLIT BY CHARGE, on synthetic spectra and then all the way
        /// through the manifest and the CSV.
        ///
        /// WHY THE SPLIT EXISTS. At charge 1 the isotope spacing is 1.00335 Da and "has a C13 isotope" is
        /// true of essentially any organic molecule, so a singly charged two-isotope feature is WEAK
        /// evidence of a peptide. At charge 2 or more the spacing is 0.5 or 0.33 Da, which a singly
        /// charged small molecule cannot produce, so it is STRONG evidence. Reporting the two merged
        /// overstates the peptide-like claim.
        ///
        /// WHAT IS NOT CLAIMED. The singly charged part is never called non-peptide: venom peptides are
        /// frequently singly charged and this dataset carries identified charge-1 peptides. The tests
        /// below check the WORDS as well as the numbers, because the wording is the honest part.
        /// </summary>
        private static void Ms1ChargeSplitTests(string workDir)
        {
            double spacing = Ms1EnvelopeDetector.Carbon13Spacing;
            double z3 = spacing / 3.0;

            // A map with exactly two features: one KNOWN singly charged (a 1.00335 Da pair) and one KNOWN
            // triply charged (a 0.33445 Da triple), far apart in m/z so neither can claim the other's
            // peaks. Constant intensities over three scans 0.1 min apart, so both areas are exact.
            const double dt = 0.1;
            var map = new List<PatternTools.MSParserLight.MSUltraLight>();
            for (int s = 0; s < 3; s++)
            {
                map.Add(Ms1Scan(s + 1, 10.0 + s * dt,
                    (700.0, 1000), (700.0 + spacing, 400),
                    (700.0 + 2 * spacing, 150),                                 // z = 1, envelope 1550
                    (900.0, 600), (900.0 + z3, 300), (900.0 + 2 * z3, 100)));    // z = 3, envelope 1000
            }

            Ms1EnvelopeDetectionResult r = Ms1EnvelopeDetector.Detect("split", map);

            Check("Q6 charge split: both constructed features are detected, no more and no fewer",
                r.DetectedEnvelopeCount == 2, r.DetectedEnvelopeCount.ToString());
            Check("Q6 charge split: the KNOWN z = 1 feature lands in the singly charged group",
                r.DetectedCountSinglyCharged == 1
                && r.DetectedCountByCharge[Ms1EnvelopeDetector.SinglyCharged] == 1,
                r.DetectedCountSinglyCharged.ToString());
            Check("Q6 charge split: the KNOWN z = 3 feature lands in the multiply charged group",
                r.DetectedCountMultiplyCharged == 1 && r.DetectedCountByCharge[3] == 1,
                r.DetectedCountMultiplyCharged.ToString());
            Check("Q6 charge split: neither feature is counted in the other group",
                r.DetectedCountByCharge[2] == 0 && r.DetectedCountByCharge[4] == 0
                && r.DetectedCountByCharge[5] == 0);

            // Two adjacent segments of 0.1 min at a constant envelope: 2 x 0.1 x 1550 and 2 x 0.1 x 1000.
            Check("Q6 charge split: the singly charged area is that feature's own trapezoid (310.0)",
                Math.Abs(r.DetectedAreaSinglyCharged - 310.0) < 1e-9,
                r.DetectedAreaSinglyCharged.ToString("R", CultureInfo.InvariantCulture));
            Check("Q6 charge split: the multiply charged area is that feature's own trapezoid (200.0)",
                Math.Abs(r.DetectedAreaMultiplyCharged - 200.0) < 1e-9,
                r.DetectedAreaMultiplyCharged.ToString("R", CultureInfo.InvariantCulture));

            // THE ARITHMETIC, not merely the presence of two fields.
            Check("Q6 charge split: the two parts sum to the detected total BIT FOR BIT",
                BitConverter.DoubleToInt64Bits(r.DetectedAreaSinglyCharged + r.DetectedAreaMultiplyCharged)
                    == BitConverter.DoubleToInt64Bits(r.DetectedPeptideLikeArea),
                r.DetectedPeptideLikeArea.ToString("R", CultureInfo.InvariantCulture));
            Check("Q6 charge split: and the whole per-charge breakdown sums to it as well (510.0)",
                Math.Abs(r.DetectedAreaByCharge.Sum() - 510.0) < 1e-9
                && Math.Abs(r.DetectedPeptideLikeArea - 510.0) < 1e-9,
                r.DetectedAreaByCharge.Sum().ToString("R", CultureInfo.InvariantCulture));
            Check("Q6 charge split: the counts split the same way as the areas",
                r.DetectedCountSinglyCharged + r.DetectedCountMultiplyCharged == r.DetectedEnvelopeCount);

            // ---- the split rides through the manifest, the .fqproj and the CSV ----------------------
            const string run = "cs_01";
            const double tic = 4000.0;
            var rows = new List<QuantRow>
            {
                AnQr("identified", run, 11, 2, "PEPTIDEK", 500.0, new[] { 300.0, 120.0, 40.0 }),
                AnQr("denovo", run, 12, 2, "DENOVOK", 620.0, new[] { 100.0, 50.0 }),
            };

            var manifest = new QuantManifest
            {
                PerRunTic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [run] = tic },
                Provenance = QuantManifest.DefaultProvenance(),
            };
            NativeQuantFractions.PopulateManifest(manifest, rows);
            NativeQuantFractions.ApplyDetectedToManifest(manifest,
                new Dictionary<string, Ms1EnvelopeDetectionResult>(StringComparer.OrdinalIgnoreCase)
                {
                    [run] = new Ms1EnvelopeDetectionResult
                    {
                        RunName = run,
                        DetectedPeptideLikeArea = r.DetectedPeptideLikeArea,
                        DetectedEnvelopeCount = r.DetectedEnvelopeCount,
                        Ms1ScanCount = 3,
                        DetectedAreaByCharge = r.DetectedAreaByCharge,
                        DetectedCountByCharge = r.DetectedCountByCharge,
                        DetectedAreaMultiplyCharged = r.DetectedAreaMultiplyCharged,
                        DetectedAreaSinglyCharged = r.DetectedAreaSinglyCharged,
                        DetectedCountMultiplyCharged = r.DetectedCountMultiplyCharged,
                        DetectedCountSinglyCharged = r.DetectedCountSinglyCharged,
                    },
                });

            DerivedRunFraction stored = manifest.DerivedFractions.Single(d => !d.Grouped);
            Check("Q6 charge split: the manifest stores both parts and both derived fractions",
                stored.DetectedAreaMultiplyCharged == 200.0 && stored.DetectedAreaSinglyCharged == 310.0
                && Math.Abs(stored.DetectedFractionMultiplyCharged - 200.0 / tic) < 1e-12
                && Math.Abs(stored.DetectedFractionSinglyCharged - 310.0 / tic) < 1e-12,
                stored.DetectedAreaMultiplyCharged.ToString("R", CultureInfo.InvariantCulture));
            Check("Q6 charge split: the existing detected total in the manifest is untouched by the split",
                Math.Abs(stored.DetectedPeptideLikeArea - 510.0) < 1e-9
                && Math.Abs(stored.DetectedFractionPeptideLike - 510.0 / tic) < 1e-12,
                stored.DetectedPeptideLikeArea.ToString("R", CultureInfo.InvariantCulture));

            string path = Path.Combine(workDir, "q6_charge_split.fqproj");
            var fcf = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|X|Y", USearchID = 0, UserID = 0 },
                },
            };
            ProjectStore.WriteFqproj(path, fcf, rows, manifest);
            var (_, _, reread) = ProjectStore.ReadFqproj(path);
            DerivedRunFraction back = reread.DerivedFractions.Single(d => !d.Grouped);
            Check("Q6 charge split: it survives the .fqproj round trip, per-charge array and all",
                back.DetectedAreaMultiplyCharged == 200.0 && back.DetectedAreaSinglyCharged == 310.0
                && back.DetectedAreaByCharge.Length == Ms1EnvelopeDetector.MaxCharge + 1
                && back.DetectedAreaByCharge[3] == 200.0
                && back.DetectedAreaByCharge[Ms1EnvelopeDetector.SinglyCharged] == 310.0,
                back.DetectedAreaByCharge.Length.ToString(CultureInfo.InvariantCulture));

            // ---- the chart and the CSV both surface it -----------------------------------------------
            AnalyticsData data = AnalyticsBuilder.Build(rows, manifest.PerRunTic);
            NativeQuantFractions.ApplyDetectedToFractions(data.Fractions, data.Grouped, reread.DerivedFractions);

            NativeRunFraction f = data.Fractions.Single();
            Check("Q6 charge split: the fractions the recovery chart reads carry the split",
                f.HasDetectedChargeSplit
                && Math.Abs(f.DetectedFractionMultiplyCharged - 200.0 / tic) < 1e-12
                && Math.Abs(f.DetectedFractionSinglyCharged - 310.0 / tic) < 1e-12,
                f.DetectedFractionMultiplyCharged.ToString("R", CultureInfo.InvariantCulture));
            Check("Q6 charge split: the pooled row keeps the identity total = z>=2 part + z=1 part",
                data.Grouped != null
                && BitConverter.DoubleToInt64Bits(
                        data.Grouped.DetectedAreaSinglyCharged + data.Grouped.DetectedAreaMultiplyCharged)
                    == BitConverter.DoubleToInt64Bits(data.Grouped.DetectedPeptideLikeArea),
                data.Grouped?.DetectedPeptideLikeArea.ToString("R", CultureInfo.InvariantCulture));

            // A row with no split (an older store) must fall back to the undivided detected block rather
            // than drawing an empty second part.
            var legacy = new NativeRunFraction { Run = "old", Tic = tic, DetectedPeptideLikeArea = 480.0 };
            Check("Q6 charge split: a store written before the split draws the undivided block, not a gap",
                legacy.HasDetectedPeptideLike && !legacy.HasDetectedChargeSplit);

            string csv = AnalyticsCsv.BuildSummaryCsv(data.Fractions, data.Grouped);
            var lines = csv.Split('\n').Where(l => l.Length > 0).ToList();
            var header = SplitCsvLine(lines[0]).ToList();
            foreach (string column in new[]
            {
                "detected_peptidelike_area", "detected_area_z_ge_2", "detected_area_z_eq_1",
                "detected_fraction_z_ge_2", "detected_fraction_z_eq_1",
                "detected_features_z_ge_2", "detected_features_z_eq_1",
                "detected_area_z1", "detected_area_z3", "detected_features_z3",
            })
            {
                Check("Q6 charge split CSV: the summary carries a \"" + column + "\" column of its own",
                    header.Contains(column), string.Join(",", header));
            }

            string[] runRow = SplitCsvLine(lines[1]);
            Check("Q6 charge split CSV: every row writes exactly the documented column set",
                lines.All(l => SplitCsvLine(l).Length == AnalyticsCsv.SummaryColumns.Length),
                runRow.Length + " vs " + AnalyticsCsv.SummaryColumns.Length);

            double CsvAt(string column) =>
                double.Parse(runRow[header.IndexOf(column)], NumberStyles.Float, CultureInfo.InvariantCulture);

            Check("Q6 charge split CSV: the two areas add up to the detected total IN THE FILE",
                Math.Abs(CsvAt("detected_area_z_ge_2") + CsvAt("detected_area_z_eq_1")
                         - CsvAt("detected_peptidelike_area")) < 1e-9,
                CsvAt("detected_area_z_ge_2").ToString("R", CultureInfo.InvariantCulture) + " + " +
                CsvAt("detected_area_z_eq_1").ToString("R", CultureInfo.InvariantCulture));
            Check("Q6 charge split CSV: and so do the two fractions",
                Math.Abs(CsvAt("detected_fraction_z_ge_2") + CsvAt("detected_fraction_z_eq_1")
                         - CsvAt("resolved_isotope_series_fraction_diagnostic")) < 1e-12);
            Check("Q6 charge split CSV: the per-charge columns hold the breakdown, not a rollup",
                Math.Abs(CsvAt("detected_area_z1") - 310.0) < 1e-9
                && Math.Abs(CsvAt("detected_area_z3") - 200.0) < 1e-9
                && Math.Abs(CsvAt("detected_area_z2")) < 1e-12,
                CsvAt("detected_area_z1").ToString("R", CultureInfo.InvariantCulture));

            // ---- the wording, which is the honest part ------------------------------------------------
            Check("Q6 charge split: z >= 2 is described as more specific but not FDR-calibrated",
                Ms1EnvelopeDetector.StrongEvidenceLabel.Contains("multiply charged", StringComparison.Ordinal)
                && Ms1EnvelopeDetector.StrongEvidenceLabel.Contains("more specific", StringComparison.Ordinal)
                && Ms1EnvelopeDetector.StrongEvidenceLabel.Contains("not peptide-FDR calibrated", StringComparison.Ordinal),
                Ms1EnvelopeDetector.StrongEvidenceLabel);
            Check("Q6 charge split: z = 1 is described as less specific, never as non-peptide",
                Ms1EnvelopeDetector.WeakEvidenceLabel.Contains("singly charged", StringComparison.Ordinal)
                && Ms1EnvelopeDetector.WeakEvidenceLabel.Contains("less specific", StringComparison.Ordinal)
                && Ms1EnvelopeDetector.WeakEvidenceLabel.Contains("not peptide-FDR calibrated", StringComparison.Ordinal)
                && !Ms1EnvelopeDetector.WeakEvidenceLabel.Contains("not a peptide", StringComparison.OrdinalIgnoreCase)
                && !Ms1EnvelopeDetector.WeakEvidenceLabel.Contains("non-peptide", StringComparison.OrdinalIgnoreCase),
                Ms1EnvelopeDetector.WeakEvidenceLabel);
            Check("Q6 charge split: natural and identified charge-1 peptides remain peptides",
                Ms1EnvelopeDetector.SinglyChargedCaveat.Contains("natural peptides are frequently singly charged", StringComparison.Ordinal)
                && Ms1EnvelopeDetector.SinglyChargedCaveat.Contains("identified charge-1 peptides", StringComparison.Ordinal)
                && Ms1EnvelopeDetector.SinglyChargedCaveat.Contains("charge alone cannot rule out peptides", StringComparison.Ordinal),
                Ms1EnvelopeDetector.SinglyChargedCaveat);

            var chart = new RecoveryChart(data.Fractions, data.Grouped);
            string tip = chart.ToolTip as string ?? string.Empty;
            Check("Q6 charge split: the recovery chart says the detector is diagnostic, not a TIC partition",
                tip.Contains("not peptide-FDR calibrated", StringComparison.Ordinal)
                && tip.Contains("does not partition", StringComparison.Ordinal),
                tip.Length.ToString(CultureInfo.InvariantCulture) + " characters");

            int ink = RenderChartInk(chart, Path.Combine(workDir, "recovery_charge_split.png"));
            Check("Q6 charge split: the recovery chart draws the split detected bar without crashing",
                ink > 500, ink.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>One stored-quant row of package 2 (run _01): what PatternLab recorded for a
        /// precursor it identified, and what the identified extraction must reproduce.</summary>
        private readonly struct Pkg2StoredQuant
        {
            public Pkg2StoredQuant(int scan, int z, double area, double mz) { Scan = scan; Z = z; Area = area; Mz = mz; }
            public int Scan { get; }
            public int Z { get; }
            public double Area { get; }
            public double Mz { get; }
        }
    }
}
