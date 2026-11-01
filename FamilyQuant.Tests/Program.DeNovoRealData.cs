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
        /// The full reconciliation on the real data: every filtered de novo row of the three runs
        /// against the COMPLETE PSM list of the .sepr2 - not just the quantified subset the early
        /// census used, which is why the counts here are the citable ones. Writes the two report
        /// files and prints the verdict table.
        /// </summary>
        /// <summary>The verdict census the engine-level reconciliation measured, so the screen can be
        /// checked against the same numbers. Null while that section has not run.</summary>
        private static Dictionary<ReconciliationVerdict, int>? reconciliationCensus;

        /// <summary>Other real-data engine results reused by the screen tests. Sharing the measured
        /// values avoids a second hand-maintained set of constants drifting from the engine sections.</summary>
        private static IReadOnlyList<int>? distinctPeakCensus;
        private static int? homologyTargetMappedCensus;
        private static (double PatternLab, double DeNovo, double Remainder)? partitionShareCensus;

        private static void ReconciliationRealTests(string? peaksDir, string? desktopDir, string workDir)
        {
            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");
            if (peaksDir == null || sepr2 == null || !Directory.Exists(peaksDir) || !File.Exists(sepr2))
            {
                Console.WriteLine("  SKIP  real reconciliation - needs the PEAKS directory and PLVI.sepr2");
                skipped++;
                return;
            }

            var psms = FamilyQuant.DeNovo.SeproPsmAdapter.Load(sepr2);
            Check("the .sepr2 yields a PSM list an order larger than the quantified subset",
                psms.Count > 20000, psms.Count.ToString("N0"));

            var runNames = psms.Select(p => FamilyQuant.DeNovo.DeNovoReconciler.NormalizeRunName(p.FileName))
                               .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check("the PSMs resolve to exactly the three acquisition runs",
                runNames.Count == 3 && runNames.All(n => n.Contains("Bjararaca")),
                string.Join(" | ", runNames));

            var criteria = new FamilyQuant.DeNovo.DeNovoFilterCriteria();
            var filtered = new List<FamilyQuant.DeNovo.PeaksDeNovoRow>();
            int rawDeNovoRows = 0;
            foreach (string csv in Directory.GetFiles(peaksDir, "*.denovoOnly.csv", SearchOption.AllDirectories))
            {
                var read = FamilyQuant.DeNovo.PeaksCsvReader.ReadDeNovoOnly(csv);
                rawDeNovoRows += read.Count;
                filtered.AddRange(FamilyQuant.DeNovo.DeNovoFilter.Apply(read, criteria));
            }
            // 35,686 ROWS; the early census quoted 33,119 because its scan-keyed dictionary
            // silently collapsed the ~2,500 spectra that carry more than one de novo candidate.
            Check("the filtered de novo set matches the row census", filtered.Count == 35686,
                filtered.Count.ToString("N0"));
            // The pair census DeNovoFilterCriteria's own comment quotes, measured rather than copied.
            Check("the kept rows collapse to the distinct (sequence, charge) census",
                filtered.Select(r => FamilyQuant.DeNovo.DeNovoReconciler.NormalizeSequence(r.Peptide)
                                     + "|" + r.Charge.ToString(CultureInfo.InvariantCulture))
                        .Distinct(StringComparer.Ordinal).Count() == 21939,
                filtered.Select(r => FamilyQuant.DeNovo.DeNovoReconciler.NormalizeSequence(r.Peptide)
                                     + "|" + r.Charge.ToString(CultureInfo.InvariantCulture))
                        .Distinct(StringComparer.Ordinal).Count().ToString("N0"));

            var rec = FamilyQuant.DeNovo.DeNovoReconciler.Reconcile(filtered, psms);
            var counts = FamilyQuant.DeNovo.DeNovoReconciliationReport.CountByVerdict(rec);
            DeNovoOverlapSummary overlap = DeNovoOverlapStudy.Build(rawDeNovoRows, rec, criteria);

            // Kept for the de novo SCREEN section, which drives the same reconciliation through the
            // control's own methods and must land on these very numbers. Comparing there against a
            // second hand-written copy of them would prove only that the copy was consistent.
            reconciliationCensus = counts;

            Console.WriteLine("        veredito       linhas");
            foreach (var kv in counts) Console.WriteLine($"        {kv.Key,-18} {kv.Value:N0}");

            Check("every filtered row received exactly one verdict",
                counts.Values.Sum() == filtered.Count);
            // Against the FULL PSM list, most shared scans carry a different precursor context rather
            // than a comparable precursor. De novo only stays the
            // majority, but at 25,437 of 35,686 rather than the 97% the subset suggested.
            Check("de novo only remains the majority",
                counts[FamilyQuant.DeNovo.ReconciliationVerdict.DeNovoOnly] * 2 > filtered.Count,
                counts[FamilyQuant.DeNovo.ReconciliationVerdict.DeNovoOnly].ToString("N0"));
            int samePrecursor = counts[FamilyQuant.DeNovo.ReconciliationVerdict.RedundantEqual]
                              + counts[FamilyQuant.DeNovo.ReconciliationVerdict.EqualButWeakPsm]
                              + counts[FamilyQuant.DeNovo.ReconciliationVerdict.Conflict];
            Check("true same-precursor meetings are at least the census floor of 122",
                samePrecursor >= 122, samePrecursor.ToString("N0"));
            Check("co-isolation is at least the census floor of 737",
                counts[FamilyQuant.DeNovo.ReconciliationVerdict.CoIsolatedExtra] >= 737,
                counts[FamilyQuant.DeNovo.ReconciliationVerdict.CoIsolatedExtra].ToString("N0"));
            // The figures HelpContent.BuildDeNovo quotes, pinned exactly rather than by a floor,
            // so a page that drifts from the data fails here instead of contradicting the labels the
            // screen puts one line above its own Help button.
            Check("the de novo only count is the one the help page quotes",
                counts[FamilyQuant.DeNovo.ReconciliationVerdict.DeNovoOnly] == 25437,
                counts[FamilyQuant.DeNovo.ReconciliationVerdict.DeNovoOnly].ToString("N0"));
            Check("the overlapping alternatives are the 1,016 the help page quotes",
                counts[FamilyQuant.DeNovo.ReconciliationVerdict.Conflict] == 1016,
                counts[FamilyQuant.DeNovo.ReconciliationVerdict.Conflict].ToString("N0"));
            Check("and the comparable-precursor meetings are 1,020",
                samePrecursor == 1020, samePrecursor.ToString("N0"));
            Check("and the scan overlaps split into 9,229 different-context plus 1,020 comparable rows",
                counts[FamilyQuant.DeNovo.ReconciliationVerdict.CoIsolatedExtra] == 9229
                && counts[FamilyQuant.DeNovo.ReconciliationVerdict.CoIsolatedExtra] + samePrecursor == 10249,
                counts[FamilyQuant.DeNovo.ReconciliationVerdict.CoIsolatedExtra].ToString("N0") + " of " +
                (counts[FamilyQuant.DeNovo.ReconciliationVerdict.CoIsolatedExtra] + samePrecursor).ToString("N0"));

            Check("the overlap study closes at scan, precursor, and sequence levels",
                overlap.SharedFileAndScan == 10249
                && overlap.DifferentChargeInSharedScan == 4874
                && overlap.SameFileScanAndCharge == 5375
                && overlap.SameChargeButNoMzMatch == 4355
                && overlap.ScanLevelStrictSequenceMatchesIgnoringPtm == 2
                && overlap.ScanLevelIleLeuEquivalentSequenceMatches == 4
                && overlap.ScanLevelSequenceDivergencesIgnoringPtmStrict == 5373
                && overlap.ScanLevelSequenceDivergencesAfterIleLeuFold == 5371
                && overlap.SamePrecursorWithinTolerance == 1020
                && overlap.IleLeuEquivalentSequenceMatches == 4
                && overlap.SequenceDivergencesAfterIleLeuFold == 1016,
                "scan " + overlap.SharedFileAndScan.ToString("N0") +
                "; scan-strict exact " + overlap.ScanLevelStrictSequenceMatchesIgnoringPtm.ToString("N0") +
                "; scan-strict divergent " + overlap.ScanLevelSequenceDivergencesIgnoringPtmStrict.ToString("N0") +
                "; precursor " + overlap.SamePrecursorWithinTolerance.ToString("N0") +
                "; strict exact " + overlap.StrictSequenceMatchesIgnoringPtm.ToString("N0") +
                "; I/L-equivalent exact " + overlap.IleLeuEquivalentSequenceMatches.ToString("N0") +
                "; divergent " + overlap.SequenceDivergencesAfterIleLeuFold.ToString("N0"));

            Check("every conflict carries its counterpart PSM with a cross-correlation",
                rec.Where(r => r.Verdict == FamilyQuant.DeNovo.ReconciliationVerdict.Conflict)
                   .All(r => r.CounterpartPsm != null));

            string summary = Path.Combine(workDir, "denovo_reconciliation_summary.csv");
            string queue = Path.Combine(workDir, "denovo_review_queue.csv");
            string overlapPath = Path.Combine(workDir, "denovo_overlap_study.csv");
            FamilyQuant.DeNovo.DeNovoReconciliationReport.WriteSummaryCsv(summary, rec);
            FamilyQuant.DeNovo.DeNovoReconciliationReport.WriteReviewQueueCsv(queue, rec);
            File.WriteAllText(overlapPath, DeNovoOverlapStudy.BuildSummaryCsv(overlap), new UTF8Encoding(false));

            int queueRows = File.ReadAllLines(queue).Length - 1;
            Check("the review queue holds every conflict and every weak-PSM equality",
                queueRows == counts[FamilyQuant.DeNovo.ReconciliationVerdict.Conflict]
                           + counts[FamilyQuant.DeNovo.ReconciliationVerdict.EqualButWeakPsm],
                queueRows.ToString("N0"));
            Console.WriteLine("        relatorios: " + summary + " | " + queue + " | " + overlapPath);
        }

        /// <summary>
        /// The reader and filter against a REAL PEAKS export, when one is supplied as the third
        /// argument. The expected counts were measured independently, in Python, before this reader
        /// existed: run _01 of the Bothrops jararaca export has 59,075 rows, of which 11,648 clear the
        /// default filter. If the C# side disagrees with that census, one of the two parsers is wrong.
        /// </summary>
        private static void DeNovoRealExportTests(string? peaksDir)
        {
            if (peaksDir == null || !Directory.Exists(peaksDir))
            {
                Console.WriteLine("  SKIP  real PEAKS export - pass the export directory as the third argument");
                skipped++;
                return;
            }

            string? csv = Directory.GetFiles(peaksDir, "*.denovoOnly.csv", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .FirstOrDefault(f => f.Contains("_01"));
            if (csv == null)
            {
                Console.WriteLine("  SKIP  no *_01 denovoOnly.csv under " + peaksDir);
                skipped++;
                return;
            }

            var rows = FamilyQuant.DeNovo.PeaksCsvReader.ReadDeNovoOnly(csv);
            Check("the real export is read in full", rows.Count == 59075, rows.Count.ToString("N0"));
            Check("every row parsed its confidence trace",
                rows.Count(r => r.LocalConfidence.Length == 0) == 0,
                rows.Count(r => r.LocalConfidence.Length == 0).ToString());

            var filtered = FamilyQuant.DeNovo.DeNovoFilter.Apply(rows, new FamilyQuant.DeNovo.DeNovoFilterCriteria());
            Check("the default filter reproduces the independent census",
                filtered.Count == 11648, filtered.Count.ToString("N0"));
        }
    }
}
