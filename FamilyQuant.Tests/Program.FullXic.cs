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

        /// <summary>
        /// The reported case: after a successful Compute the only percentage on screen was the resolved
        /// isotope-series diagnostic (59.5% to 59.7% of the TIC on the three jararaca runs), and it was read
        /// as "the quantified peptides explain 60% of the TIC". The summary now leads with the share the
        /// quantified peptides explain, and says in so many words what the diagnostic is.
        /// </summary>
        private static void TicShareSummaryTests()
        {
            DerivedRunFraction Run(string name, double identified, double deNovo, double detected, double tic = 1e12) =>
                new DerivedRunFraction
                {
                    Run = name, Tic = tic,
                    IdentifiedFractionEnvelope = identified,
                    DeNovoPeptides = deNovo > 0 ? 10 : 0,
                    DeNovoEnvelopeReferenceCalibrated = deNovo * tic,
                    DeNovoFractionReferenceCalibrated = deNovo,
                    RecoveredFractionReferenceCalibrated = identified + deNovo,
                    DetectedFractionPeptideLike = detected,
                };

            var identifiedOnly = new List<DerivedRunFraction>
            {
                Run("run_01", 0.2784, 0, 0.5972), Run("run_02", 0.2799, 0, 0.5952), Run("run_03", 0.2863, 0, 0.5955),
                new DerivedRunFraction { Run = "all runs", Grouped = true, Tic = 3e12, IdentifiedFractionEnvelope = 0.2815 },
            };
            List<string> lines = FullXicComputer.DescribeTicShares(identifiedOnly);
            Check("without de novo the summary names the identified share, per run and over all runs",
                lines.Count == 6
                && lines[0].StartsWith("Share of the TIC explained by the identified peptides", StringComparison.Ordinal)
                && lines[1] == "  run_01: identified 27.84 %" && lines[4] == "  all runs: identified 28.15 %",
                string.Join(" | ", lines));
            Check("and its last line names the diagnostic range as NOT the share explained",
                lines[5].Contains("(59.52 % to 59.72 % of the TIC)", StringComparison.Ordinal)
                && lines[5].Contains("not signal already explained", StringComparison.Ordinal), lines[5]);

            var withDeNovo = new List<DerivedRunFraction> { Run("run_01", 0.2784, 0.1381, 0.5972) };
            List<string> dn = FullXicComputer.DescribeTicShares(withDeNovo);
            Check("with de novo the line adds the measured envelope and the recovered total",
                dn.Count == 3 && dn[0].Contains("de novo measured envelope", StringComparison.Ordinal)
                && dn[1] == "  run_01: identified 27.84 % + de novo 13.81 % = 41.65 %", string.Join(" | ", dn));
            Check("one run gets no 'all runs' line and a single diagnostic value",
                !dn.Any(l => l.Contains("all runs", StringComparison.Ordinal)) && dn[2].Contains("(59.72 % of the TIC)", StringComparison.Ordinal));

            List<string> noTic = FullXicComputer.DescribeTicShares(new[] { Run("run_01", 0, 0, 0, tic: 0) });
            Check("a run without a TIC says so instead of printing 0 %, and no diagnostic line is invented",
                noTic.Count == 2 && noTic[1] == "  run_01: no TIC recorded, so no share can be given", string.Join(" | ", noTic));
            Check("nothing to describe gives no summary at all",
                FullXicComputer.DescribeTicShares(null).Count == 0
                && FullXicComputer.DescribeTicShares(new DerivedRunFraction[0]).Count == 0);
        }

        /// <summary>
        /// The reported case: a user's acquisition arrived as "..._01-003.raw" while the search and the
        /// PEAKS export call the run "..._01". Compute paired nothing, finished as a success with zero
        /// rows and replaced the project's quant with that. The pairing stays exact; what is tested is
        /// that zero pairs is a refusal naming both sides, and that a partial pairing says what it left.
        /// The last check drives the real engine against a folder holding only an EMPTY renamed .raw,
        /// which is safe precisely because the refusal comes before any acquisition is opened.
        /// </summary>
        private static void FullXicRunMatchTests(string? desktopDir, string workDir)
        {
            const string R1 = "20220624_Bjararaca_1ug_3step-240min_01";
            const string R2 = "20220624_Bjararaca_1ug_3step-240min_02";
            const string R3 = "20220624_Bjararaca_1ug_3step-240min_03";

            var renamed = FullXicRunMatch.Build(new[] { R1 + "-003" }, new[] { R2, R1, R3 }, new[] { R1, R2, R3 });
            Check("a RAW renamed with a copy suffix pairs with no run",
                renamed.Matched.Count == 0, string.Join(", ", renamed.Matched));
            Check("the unpaired lists name the RAW and every identification run, in name order",
                renamed.RawWithoutEvidence.SequenceEqual(new[] { R1 + "-003" })
                && renamed.IdentifiedWithoutRaw.SequenceEqual(new[] { R1, R2, R3 })
                && renamed.DeNovoWithoutRaw.SequenceEqual(new[] { R1, R2, R3 }));
            Check("the rename hint points the suffixed RAW at its run",
                renamed.RenameHints().Count == 1
                && renamed.RenameHints()[0].Contains("\"" + R1 + "-003.raw\"", StringComparison.Ordinal)
                && renamed.RenameHints()[0].Contains("\"" + R1 + ".raw\"", StringComparison.Ordinal),
                string.Join(" | ", renamed.RenameHints()));

            string refusal = renamed.RefusalMessage(@"C:\AnaG\PLVI", "PLVI.sepr2", deNovoGiven: true);
            Check("the refusal says nothing was computed and the project keeps its quant",
                refusal.StartsWith("Nothing was computed", StringComparison.Ordinal)
                && refusal.Contains("keeps the quantitation it already had", StringComparison.Ordinal), refusal);
            Check("the refusal lists the RAW folder's names and the runs of both inputs",
                refusal.Contains(R1 + "-003", StringComparison.Ordinal)
                && refusal.Contains("Runs in PLVI.sepr2: " + R1 + ", " + R2 + ", " + R3, StringComparison.Ordinal)
                && refusal.Contains("Runs with kept de novo calls: " + R1, StringComparison.Ordinal), refusal);
            Check("without a de novo export the refusal does not mention de novo runs",
                !renamed.RefusalMessage("d", "x.sepr2", deNovoGiven: false)
                    .Contains("kept de novo calls:", StringComparison.Ordinal));
            Check("an empty RAW folder is told to choose the folder holding the acquisitions",
                FullXicRunMatch.Build(Array.Empty<string>(), new[] { R1 }, Array.Empty<string>())
                    .RefusalMessage("d", "x.sepr2", false).Contains("the .raw acquisitions themselves", StringComparison.Ordinal));

            var partial = FullXicRunMatch.Build(new[] { R2, "blank_wash", R1 }, new[] { R1, R2, R3 }, Array.Empty<string>());
            IReadOnlyList<string> partialNotes = partial.MismatchNotes("d", "PLVI.sepr2");
            Check("a partial pairing processes only the paired runs, in name order",
                partial.Matched.SequenceEqual(new[] { R1, R2 }), string.Join(", ", partial.Matched));
            Check("a partial pairing warns about the run without a RAW and the RAW without a run",
                partialNotes.Any(n => n.StartsWith("WARNING: 1 run(s) in PLVI.sepr2", StringComparison.Ordinal) && n.EndsWith(R3, StringComparison.Ordinal))
                && partialNotes.Any(n => n.Contains("skipped: blank_wash", StringComparison.Ordinal)),
                string.Join(" | ", partialNotes));

            var exact = FullXicRunMatch.Build(new[] { R1.ToUpperInvariant(), R2 }, new[] { R1, R2 }, new[] { R2 });
            Check("a complete pairing is case-insensitive and raises no warning",
                exact.Matched.Count == 2 && exact.MismatchNotes("d", "x").Count == 0,
                string.Join(" | ", exact.MismatchNotes("d", "x")));

            var many = FullXicRunMatch.Build(Array.Empty<string>(),
                Enumerable.Range(1, 12).Select(i => "run" + i.ToString("00", CultureInfo.InvariantCulture)), Array.Empty<string>());
            Check("a long run list is cut after eight names and counts the rest",
                many.RefusalMessage("d", "x.sepr2", false).Contains("run08 and 4 more", StringComparison.Ordinal));

            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");
            if (sepr2 == null || !File.Exists(sepr2))
            {
                Console.WriteLine("  SKIP  the engine refusal on the real .sepr2 - needs PLVI.sepr2");
                skipped++;
                return;
            }

            string rawDir = Path.Combine(workDir, "renamed_raw_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rawDir);
            File.WriteAllBytes(Path.Combine(rawDir, R1 + "-003.raw"), Array.Empty<byte>());
            try
            {
                FullXicNoMatchedRunsException? refused = null;
                try
                {
                    FullXicComputer.ComputeFullXic(rawDir, sepr2, null, new FullXicOptions { AppVersion = "test" },
                        (side, run) => Path.Combine(rawDir, side + "_" + run + ".json"), null, null,
                        run => Path.Combine(rawDir, "ms1envelope_" + run + ".json"));
                }
                catch (FullXicNoMatchedRunsException ex) { refused = ex; }
                catch (Exception ex)
                {
                    Check("the engine refuses a folder whose only RAW is renamed", false, ex.GetType().Name + ": " + ex.Message);
                    return;
                }

                Check("the engine refuses a folder whose only RAW is renamed, instead of returning zero rows",
                    refused != null && refused.Match.Matched.Count == 0
                    && refused.Match.IdentifiedWithoutRaw.Contains(R1, StringComparer.OrdinalIgnoreCase),
                    refused?.Message ?? "no refusal: a zero-row result came back");
                Check("the refusal is raised before anything is written beside the RAW",
                    Directory.GetFileSystemEntries(rawDir).Length == 1,
                    string.Join(", ", Directory.GetFileSystemEntries(rawDir).Select(Path.GetFileName)));
            }
            finally
            {
                try { Directory.Delete(rawDir, recursive: true); } catch (IOException) { }
            }
        }

        /// <summary>
        /// Q2 on the real data: drive the Compute / update full XIC engine over run _01's identified
        /// peptides and assert the stored rows carry the 6-isotope envelope, then round-trip them through
        /// a .fqproj. Reuses the cache the Q1 identified section wrote (identified_xic_run01_n6.json), so
        /// it adds no RAW load when that section ran; without the cache it pays one load, still bounded.
        /// </summary>
        private static void FullXicComputeRealTests(string? desktopDir, string workDir)
        {
            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");
            string? raw = desktopDir == null ? null
                : Path.Combine(desktopDir, "20220624_Bjararaca_1ug_3step-240min_01.raw");

            if (sepr2 == null || !File.Exists(sepr2) || raw == null || !File.Exists(raw))
            {
                Console.WriteLine("  SKIP  full XIC compute - needs PLVI.sepr2 and the _01 RAW");
                skipped++;
                return;
            }

            string run01 = DeNovoReconciler.NormalizeRunName(raw);

            // Point the identified side at the cache the Q1 section already wrote, so this runs in seconds
            // when that section ran; the de novo side is unused here (no export directory is given).
            Func<string, string, string> cacheFor = (side, run) => side == "identified"
                ? Path.Combine(workDir, "identified_xic_run01_n6.json")
                : Path.Combine(workDir, "fullxic_denovo_" + run + ".json");

            var options = new FullXicOptions
            {
                IsotopeCount = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount,
                AppVersion = "test",
                // This historical reference check predates D65; keep its frozen OFF basis.
                PatternLabWinsScanConflicts = false,
                // This gate restricts extraction to _01, so its spectrum table would credit quant for
                // _01 only. The default export path is rawDirectory (the shared data directory), where the
                // talk lanes write the COMPLETE three-run spectrum_evidence_comparison.csv. Route this
                // gate's partial CSV into the harness scratch directory so it exercises the writer on real
                // data without overwriting that complete three-run table.
                SpectrumComparisonCsvPath = Path.Combine(workDir, "compute_run01_spectrum_evidence.csv"),
            };

            FullXicComputeResult outcome;
            try
            {
                // rawDirectory is the desktop dir; restrictToRuns keeps it to _01 so a folder that also
                // held _02/_03 could not pull them into this gate.
                outcome = FullXicComputer.ComputeFullXic(
                    desktopDir!, sepr2, null, options, cacheFor,
                    m => Console.WriteLine("        " + m), new[] { run01 },
                    run => Path.Combine(workDir, "ms1envelope_" + run + ".json"));
            }
            catch (Exception ex)
            {
                Check("the compute path runs against the _01 RAW", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            var identified = outcome.Rows.Where(r => r.Side == "identified").ToList();

            Check("compute: run _01 stored a five-digit identified row count",
                identified.Count > 1000, identified.Count.ToString("N0"));
            Check("compute: every stored row carries the 6-isotope envelope",
                identified.Count > 0 && identified.All(r => r.IsotopeAreas.Length == 6),
                identified.Count == 0 ? "0 rows"
                    : identified.Count(r => r.IsotopeAreas.Length != 6).ToString() + " row(s) not length 6");
            Check("compute: no stored row is a no-cluster sentinel (those are dropped)",
                identified.All(r => r.AreaM0 > DeNovoXicExtractor.NoCluster));
            Check("compute: the envelope total strictly exceeds the M+0 total",
                identified.Sum(r => r.EnvelopeSum) > identified.Sum(r => r.AreaM0),
                identified.Sum(r => r.EnvelopeSum).ToString("E4", CultureInfo.InvariantCulture) + " vs " +
                identified.Sum(r => r.AreaM0).ToString("E4", CultureInfo.InvariantCulture));
            Check("compute: the manifest records run _01's TIC and the 6-isotope basis",
                outcome.Manifest.IsotopeCount == 6 && outcome.Manifest.PerRunTic.TryGetValue(run01, out double tic) && tic > 0,
                outcome.Manifest.PerRunTic.TryGetValue(run01, out double t2) ? t2.ToString("E4", CultureInfo.InvariantCulture) : "no TIC");

            // Store the computed rows and read them back: the full Q2 path on real data.
            var fcf = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|X|Y", USearchID = 0, UserID = 0 },
                },
            };
            string path = Path.Combine(workDir, "compute_run01.fqproj");
            ProjectStore.WriteFqproj(path, fcf, outcome.Rows, outcome.Manifest);
            var (_, rows2, mf2) = ProjectStore.ReadFqproj(path);

            Check("compute: the stored rows survive a .fqproj round trip",
                rows2.Count == outcome.Rows.Count, rows2.Count + " vs " + outcome.Rows.Count);
            Check("compute: the identified M+0 total is bit-identical after the store round trip",
                BitConverter.DoubleToInt64Bits(rows2.Where(r => r.Side == "identified").Sum(r => r.AreaM0))
                    == BitConverter.DoubleToInt64Bits(identified.Sum(r => r.AreaM0)));
            Check("compute: the manifest survives the store round trip",
                mf2.IsotopeCount == 6 && mf2.PerRunTic.ContainsKey(run01));
            Check("compute: the manifest records WHICH minimum-points rule produced the areas",
                mf2.MinPointsForQuant == DeNovoXicExtractor.DefaultMinPointsForQuant,
                mf2.MinPointsForQuant.ToString());

            Console.WriteLine($"        stored {identified.Count:N0} identified row(s); envelope/M+0 = " +
                $"{(identified.Sum(r => r.EnvelopeSum) / identified.Sum(r => r.AreaM0)):F4}");
        }
    }
}
