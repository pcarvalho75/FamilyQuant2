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
        /// A two-file PEAKS de novo export, one file in a sub-folder so the recursive search is
        /// exercised. Five rows: one clean, one too short, one whose average confidence is too low, one
        /// whose confident residues never form a run of five, and one that passes only because of its
        /// middle stretch. So each criterion owns exactly one rejection.
        /// </summary>
        private static string BuildSyntheticPeaksExport(string workDir)
        {
            string root = Path.Combine(workDir, "synthetic_peaks_export");
            string sub = Path.Combine(root, "runB");

            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(sub);

            const string header = "Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,ppm,PTM,local confidence (%),Feature Id";

            File.WriteAllText(Path.Combine(root, "syntheticA.raw.denovoOnly.csv"),
                header + "\n" +
                "100,PEPTLDEK,95,8,500.25,2,10.5,1.12E7,-0.5,,99 98 97 96 95 94 93 92,42\n" +
                "101,SHORTL,95,5,400.10,2,11.0,1.0E7,0.2,,99 99 99 99 99,43\n" +
                "102,WEAKALCX,60,8,450.30,2,12.0,1.0E7,0.1,,99 99 99 99 99 99 99 99,44\n");

            File.WriteAllText(Path.Combine(sub, "syntheticB.raw.denovoOnly.csv"),
                header + "\n" +
                "200,LSEQVENCE,88,9,600.30,2,20.1,2.0E7,0.3,,80 92 93 94 95 96 20 80 80,45\n" +
                "201,NOSTRETCH,85,8,650.40,2,21.0,2.0E7,0.4,,95 95 95 95 20 95 95 95,46\n");

            return root;
        }

        /// <summary>
        /// The same screen against the real Bothrops jararaca export and the real .sepr2, driven through
        /// the methods the buttons call. The verdict counts are compared with the ones the engine-level
        /// section measured rather than with a second hand-written copy of them.
        /// </summary>
        private static void DeNovoScreenRealTests(string? peaksDir, string? desktopDir, string workDir,
            bool includeLegacyExtraction = true)
        {
            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");

            if (peaksDir == null || sepr2 == null || !Directory.Exists(peaksDir) || !File.Exists(sepr2))
            {
                Console.WriteLine("  SKIP  the de novo screen on real data - needs the PEAKS directory and PLVI.sepr2");
                skipped++;
                return;
            }

            DeNovoControl screen;

            try { screen = new DeNovoControl(); }
            catch (Exception ex)
            {
                Check("the de novo screen builds for the real-data pass", false,
                    ex.GetType().Name + ": " + ex.Message);
                return;
            }

            var status = UiField<TextBlock>(screen, "TextBlockDeNovoStatus")!;
            var reconcileButton = UiField<Button>(screen, "ButtonReconcile")!;
            var queueGrid = UiField<DataGrid>(screen, "DataGridReviewQueue")!;
            var breakdown = UiField<TextBlock>(screen, "TextBlockPerRunBreakdown")!;

            UiField<TextBox>(screen, "TextBoxPeaksDirectory")!.Text = peaksDir;
            UiField<TextBox>(screen, "TextBoxSeproPackage")!.Text = sepr2;

            Check("the screen reads its criteria from the boxes untouched", screen.ReadCriteriaFromBoxes());

            var import = DeNovoControl.ImportAndFilter(peaksDir, screen.Criteria);
            screen.ApplyImport(import);

            Check("the screen finds one de novo export per acquisition run", import.FilesFound == 3,
                import.FilesFound.ToString());
            Check("it reads the whole export", import.RowsRead == 181643, import.RowsRead.ToString("N0"));
            // The same 35,686 the engine-level section measures, reached here through the screen.
            Check("and the screen's own filter pass reproduces the row census",
                screen.FilteredRows.Count == 35686, screen.FilteredRows.Count.ToString("N0"));
            Check("the breakdown names three runs, each keeping some of what it read",
                import.PerRun.Count == 3 && import.PerRun.All(t => t.RowsKept > 0 && t.RowsKept < t.RowsRead),
                breakdown.Text.Replace("\n", " | "));
            Check("a real import unblocks Layer 2", reconcileButton.IsEnabled);

            var outcome = DeNovoControl.Reconcile(screen.FilteredRows, sepr2);
            screen.ApplyReconciliation(outcome);

            Check("the package yields the same PSM list the engine section loads",
                screen.Psms.Count > 20000, screen.Psms.Count.ToString("N0"));
            Check("every kept row received exactly one verdict",
                outcome.Counts.Values.Sum() == screen.FilteredRows.Count,
                outcome.Counts.Values.Sum().ToString("N0"));

            if (reconciliationCensus == null)
            {
                Console.WriteLine("        (no engine census to compare against; that section was skipped)");
            }
            else
            {
                Check("the screen's verdict counts are the ones the engine section measured",
                    Enum.GetValues<ReconciliationVerdict>()
                        .All(v => outcome.Counts[v] == reconciliationCensus[v]),
                    string.Join(", ", Enum.GetValues<ReconciliationVerdict>()
                        .Select(v => v + " " + outcome.Counts[v] + " vs " + reconciliationCensus[v])));
            }

            int decisions = outcome.Counts[ReconciliationVerdict.Conflict]
                          + outcome.Counts[ReconciliationVerdict.EqualButWeakPsm];

            Check("the review queue holds every conflict and every weak-PSM equality, and nothing else",
                screen.ReviewQueue.Count == decisions, screen.ReviewQueue.Count + " vs " + decisions);
            Check("and the table on screen holds exactly those rows",
                queueGrid.ItemsSource != null
                && queueGrid.ItemsSource.Cast<ReviewQueueRow>().Count() == decisions);
            Check("every queue row is one of the two verdicts that need a decision",
                screen.ReviewQueue.All(r => r.Verdict == ReconciliationVerdict.Conflict
                                         || r.Verdict == ReconciliationVerdict.EqualButWeakPsm));

            string summaryCsv = Path.Combine(workDir, "denovo_screen_summary.csv");
            string queueCsv = Path.Combine(workDir, "denovo_screen_review_queue.csv");

            screen.ExportSummaryCsv(summaryCsv);
            screen.ExportReviewQueueCsv(queueCsv);

            var summaryLines = File.ReadAllLines(summaryCsv).Where(l => l.Trim().Length > 0).ToList();
            var summaryRuns = summaryLines.Skip(1).Select(l => SplitCsvLine(l)[0]).Distinct().ToList();

            Check("the exported summary covers the three runs",
                summaryRuns.Count == 3, string.Join(" | ", summaryRuns));
            Check("and its counts add up to every reconciled row",
                summaryLines.Skip(1).Sum(l => int.Parse(SplitCsvLine(l)[2], CultureInfo.InvariantCulture))
                    == screen.FilteredRows.Count);

            var queueLines = File.ReadAllLines(queueCsv).Where(l => l.Trim().Length > 0).ToList();

            Check("the exported queue holds exactly the rows of the table",
                queueLines.Count == screen.ReviewQueue.Count + 1, queueLines.Count.ToString("N0"));
            Check("and every one of its ppm columns parses back as a number",
                queueLines.Skip(1).All(l => double.TryParse(SplitCsvLine(l)[11], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _)));

            Console.WriteLine("        status line: " + status.Text);
            Console.WriteLine("        relatorios: " + summaryCsv + " | " + queueCsv);

            if (!includeLegacyExtraction)
            {
                Console.WriteLine("        focused X6 scope: import, reconciliation, selections and review exports; no RAW extraction");
                return;
            }

            // Main has no enclosing try/catch, so an unguarded throw here (a locked .xic2, an IOException
            // on a OneDrive placeholder RAW) would abort every section after this one.
            try
            {
                DeNovoScreenRealLayersTests(screen, desktopDir!, workDir);
            }
            catch (Exception ex)
            {
                Check("the de novo screen's Layers 3 to 5 ran to completion on the real data", false,
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Layers 3, 4 and 5 of the de novo screen on the real Bothrops jararaca data, driven through
        /// the methods the buttons call.
        ///
        /// THE CACHE IS REDIRECTED, THE MEASUREMENT IS NOT. The suite has already extracted these three
        /// runs in the engine-level section and holds their caches in its own work directory; without
        /// the seam this pass would re-read three 3 GB acquisitions to arrive at numbers it has already
        /// proved. The application's own default, a folder beside the RAW files, is checked separately
        /// on fixtures and is left untouched.
        ///
        /// The numbers asserted here are the ones the engine-level sections measured, and the partition
        /// CSV is compared with the engine's own file BYTE FOR BYTE: the screen must be a way of
        /// reaching those numbers, never a second implementation of them.
        /// </summary>
        private static void DeNovoScreenRealLayersTests(DeNovoControl screen, string desktopDir, string workDir)
        {
            string[] rawNames =
            {
                "20220624_Bjararaca_1ug_3step-240min_01.raw",
                "20220624_Bjararaca_1ug_3step-240min_02.raw",
                "20220624_Bjararaca_1ug_3step-240min_03.raw",
            };

            string database = Path.Combine(desktopDir,
                "uniprotkb_taxonomy_id_8724_2026_06_25_ORDENADO_CONT123_NO-Redundancy.T-R");
            string plpPath = Path.Combine(desktopDir, "Bjararaca_TIC_Peptides_TechRep.plp");
            string xic2Path = Path.Combine(desktopDir, "FamilyQuant_XIC.xic2");

            if (rawNames.Any(n => !File.Exists(Path.Combine(desktopDir, n))))
            {
                Console.WriteLine("  SKIP  the de novo screen's Layers 3 to 5 - needs the three RAW files");
                skipped++;
                return;
            }

            var status = UiField<TextBlock>(screen, "TextBlockDeNovoStatus")!;

            // ---- Layer 3 ---------------------------------------------------------------------------
            // The PEAKS export sits one level under the acquisitions in this project, so the folder the
            // screen offered by itself is already the right one. Asserted rather than assumed, then set
            // explicitly so the rest of this pass does not depend on that layout.
            Check("the screen offered the PEAKS export's parent as the folder holding the acquisitions",
                screen.ViewModel.RawDirectory.Trim().Length > 0
                && Directory.Exists(screen.ViewModel.RawDirectory.Trim())
                && Directory.GetFiles(screen.ViewModel.RawDirectory.Trim(), "*.raw").Length >= 3,
                screen.ViewModel.RawDirectory);

            screen.ViewModel.RawDirectory = desktopDir; screen.ViewModel.RawDirectoryChanged();

            Check("all three kept runs found their acquisition by file name",
                screen.RawMatches.Count == 3 && screen.RawMatches.All(m => m.Found),
                screen.ViewModel.RawMatchPreview.Replace("\n", " | "));
            Check("and Extract is offered only once every one of them has", screen.ViewModel.CanExtract);

            var matches = screen.RawMatches.ToList();
            var keptByRun = screen.KeptRowsByRun();

            Check("the rows handed to the extraction are the reconciliation's kept rows, per run",
                keptByRun.Count == 3 && keptByRun.Values.All(v => v.Count > 5000),
                string.Join(" | ", keptByRun.Select(kv => kv.Key + "=" + kv.Value.Count.ToString("N0"))));

            DeNovoXicExtraction extraction;
            DeNovoControl.CachePathForRunOverride = run =>
                Path.Combine(workDir, "denovo_xic_run" + run.Substring(run.Length - 2) + ".json");

            try
            {
                extraction = DeNovoControl.ExtractXic(matches, keptByRun, desktopDir,
                                                      m => Console.WriteLine("        " + m));
            }
            finally
            {
                DeNovoControl.CachePathForRunOverride = null;
            }

            screen.ApplyExtraction(extraction);

            var shownStats = screen.ViewModel.XicStatsItems!.Cast<DeNovoBatchStats>().ToList();

            foreach (DeNovoBatchStats s in shownStats)
            {
                Console.WriteLine($"        {s.RunName}: linhas {s.RowsIn:N0} | precursores unicos " +
                    $"{s.UniquePrecursors:N0} | com sinal {s.WithSignal:N0} | picos distintos " +
                    $"{s.DistinctPeaks:N0} | ancoras redundantes {s.RedundantAnchors:N0} | M+0 " +
                    $"{s.TotalAreaM0:E4} | {s.ElapsedSeconds:F1} s");
            }

            Check("the table on screen holds one line per run, in run-name order",
                shownStats.Count == 3
                && shownStats.Select(s => s.RunName).SequenceEqual(
                    rawNames.Select(n => DeNovoReconciler.NormalizeRunName(n))),
                string.Join(" | ", shownStats.Select(s => s.RunName)));

            // The census the engine-level section measured, reached here through the screen. Compare to
            // that measured result rather than maintaining a second copy of dataset-specific constants.
            Check("and the distinct-peak counts are the ones the extraction engine measures",
                distinctPeakCensus != null
                && shownStats.Select(s => s.DistinctPeaks).SequenceEqual(distinctPeakCensus),
                string.Join(" | ", shownStats.Select(s => s.DistinctPeaks.ToString("N0"))));

            Check("every run collapsed some anchors, so the peak grain is doing work on all three",
                shownStats.All(s => s.RedundantAnchors > 0 && s.RedundantAreaM0 > 0),
                string.Join(" | ", shownStats.Select(s => s.RedundantAnchors.ToString("N0"))));
            Check("the totals shown are the sum of the per-run totals, not a second measurement",
                Math.Abs(screen.LastExtraction!.TotalAreaM0 - shownStats.Sum(s => s.TotalAreaM0)) < 1e-6,
                screen.LastExtraction.TotalAreaM0.ToString("R", CultureInfo.InvariantCulture));

            // ---- Layer 4 ---------------------------------------------------------------------------
            if (!File.Exists(database))
            {
                Console.WriteLine("  SKIP  the de novo screen's Layer 4 - needs the venom T-R");
                skipped++;
            }
            else
            {
                FamilyClusterFile? fcf = EnsureVenomFcf(database, workDir, out string fcfPath);

                if (fcf == null)
                {
                    Console.WriteLine("  SKIP  the de novo screen's Layer 4 - USEARCH/clustering unavailable");
                    skipped++;
                }
                else
                {
                    UiField<TextBox>(screen, "TextBoxHomologyFcf")!.Text = fcfPath;

                    Check("the screen reads its homology settings from the boxes untouched",
                        screen.ReadHomologySettingsFromBoxes());

                    List<string> peptides = screen.PeptidesForHomology();

                    Check("the kept rows collapse to a five-digit set of distinct peptides",
                        peptides.Count > 10000, peptides.Count.ToString("N0"));

                    DeNovoHomology homology = DeNovoControl.SearchHomology(
                        peptides, fcf, screen.HomologySettings, fcfPath);
                    screen.ApplyHomology(homology);

                    Console.WriteLine($"        peptides {homology.PeptidesSearched:N0} | alvos " +
                        $"{homology.MappedToTargets:N0} | so decoy {homology.DecoyOnly:N0} | sem par " +
                        $"{homology.Unmatched:N0} | familias {homology.Families.Count:N0} | " +
                        $"{homology.ElapsedSeconds:F1} s");

                    Check("the four classes partition the peptides that were searched",
                        homology.MappedToTargets + homology.DecoyOnly + homology.Unmatched
                            + homology.Rejected == homology.PeptidesSearched,
                        homology.PeptidesSearched.ToString("N0"));

                    // The census the engine-level homology section measures, reached through the screen.
                    Check("the screen maps the same peptides to targets that the engine section does",
                        homologyTargetMappedCensus.HasValue
                        && homology.MappedToTargets == homologyTargetMappedCensus.Value,
                        homology.MappedToTargets.ToString("N0"));
                    Check("and reaches the same number of target families",
                        homology.Families.Count == 33, homology.Families.Count.ToString("N0"));

                    Check("target hits dominate decoy-only hits, so the threshold is doing work",
                        homology.MappedToTargets > homology.DecoyOnly,
                        homology.MappedToTargets + " contra " + homology.DecoyOnly);

                    var familyRows = UiField<DataGrid>(screen, "DataGridHomologyFamilies")!
                        .ItemsSource!.Cast<FamilyQuant.Homology.DeNovoFamilyRow>().ToList();

                    Check("the family table on screen holds exactly those families, most peptides first",
                        familyRows.Count == homology.Families.Count
                        && familyRows.Zip(familyRows.Skip(1), (a, b) => a.PeptideCount >= b.PeptideCount).All(x => x),
                        familyRows.Count.ToString());
                    Check("and every one of them names a target protein rather than a reversed sequence",
                        familyRows.All(r => !r.Description.StartsWith("Reverse_", StringComparison.Ordinal)));

                    string homologyCsv = Path.Combine(workDir, "denovo_screen_homology.csv");
                    screen.ExportHomologyCsv(homologyCsv);

                    var homologyLines = File.ReadAllLines(homologyCsv).Where(l => l.Trim().Length > 0).ToList();
                    Check("the exported assignments hold one line per searched peptide, plus the header",
                        homologyLines.Count == homology.PeptidesSearched + 1,
                        homologyLines.Count.ToString("N0"));
                    Check("and the header names the four columns the request asked for",
                        homologyLines[0] == "peptide,family_ids,protein_ids,ambiguous", homologyLines[0]);

                    Console.WriteLine("        relatorio: " + homologyCsv);
                }
            }

            // ---- Layer 5 ---------------------------------------------------------------------------
            if (!File.Exists(plpPath) || !File.Exists(xic2Path))
            {
                Console.WriteLine("  SKIP  the de novo screen's Layer 5 - needs the .plp and the .xic2");
                skipped++;
                return;
            }

            screen.ViewModel.PartitionPlp = plpPath;
            screen.ViewModel.PartitionXic2 = xic2Path;

            Check("with every run extracted and a PatternLab project named, Layer 5 is unblocked",
                screen.ViewModel.CanPartition);

            DeNovoPartition partition = DeNovoControl.BuildPartition(
                matches, extraction.ByRun, plpPath, xic2Path, m => Console.WriteLine("        " + m));

            screen.ApplyPartition(partition);

            Console.WriteLine("        " + partition.BranchNote);

            Check("a source that resolves per run was found, and three bars were drawn",
                partition.IsAvailable && partition.Rows.Count == 3,
                partition.Rows.Count.ToString());

            // The .plp of this project cannot answer per run. THAT refusal is the reason the .xic2 is
            // being read, and it has to be on screen word for word rather than reduced to a flag. The
            // refusal's own wording is not asserted here: which of the .plp branch's several refusals
            // applies is a property of the project file, and pinning one of them would make this check
            // fail the day the project is re-exported rather than the day the screen stops showing it.
            const string marker = "because the .plp cannot resolve per run: ";
            int at = screen.ViewModel.BranchNote.IndexOf(marker, StringComparison.Ordinal);
            string plpRefusal = at < 0 ? "" : screen.ViewModel.BranchNote.Substring(at + marker.Length);

            Check("the branch note is shown verbatim, the .plp's own refusal included",
                screen.ViewModel.BranchNote == partition.BranchNote
                && screen.ViewModel.BranchNote.Contains(".xic2")
                && at > 0 && plpRefusal.Length > 40 && plpRefusal.EndsWith(".", StringComparison.Ordinal),
                screen.ViewModel.BranchNote);
            Check("and that refusal is the .plp branch's own sentence, not a summary of it",
                plpRefusal.StartsWith("This project", StringComparison.Ordinal), plpRefusal);

            var shownRows = screen.ViewModel.PartitionItems!.Cast<ThreeWayPartitionRow>().ToList();

            Check("the table holds the three runs and the aggregate, in that order",
                shownRows.Count == 4 && shownRows[3].IsGrouped
                && shownRows.Take(3).Select(r => r.RunName).SequenceEqual(
                    rawNames.Select(n => DeNovoReconciler.NormalizeRunName(n))),
                string.Join(" | ", shownRows.Select(r => r.RunName)));
            Check("and legacy partition rows are available for chart drawing", screen.ViewModel.PartitionChartRows != null);

            ThreeWayPartitionRow grouped = shownRows[3];

            Console.WriteLine($"        agrupado: PatternLab {grouped.SharePatternLabPercent:F4}% | " +
                $"de novo {grouped.ShareDeNovoPercent:F4}% | resto {grouped.ShareRemainderPercent:F4}%");

            // The three numbers the engine-level partition section measured, reached here through the
            // screen. This is an actual cross-layer comparison, not duplicated historical constants.
            Check("the grouped PatternLab share is the one the engine section measures",
                partitionShareCensus.HasValue
                && Math.Abs(grouped.SharePatternLabPercent - partitionShareCensus.Value.PatternLab) < 5e-10,
                grouped.SharePatternLabPercent.ToString("F6", CultureInfo.InvariantCulture));
            Check("and so is the grouped de novo share",
                partitionShareCensus.HasValue
                && Math.Abs(grouped.ShareDeNovoPercent - partitionShareCensus.Value.DeNovo) < 5e-10,
                grouped.ShareDeNovoPercent.ToString("F6", CultureInfo.InvariantCulture));
            Check("and so is the grouped remainder",
                partitionShareCensus.HasValue
                && Math.Abs(grouped.ShareRemainderPercent - partitionShareCensus.Value.Remainder) < 5e-10,
                grouped.ShareRemainderPercent.ToString("F6", CultureInfo.InvariantCulture));
            Check("every line of the table closes on 100%", shownRows.All(r => r.Closes),
                string.Join(" | ", shownRows.Select(r => r.ShareSum.ToString("R", CultureInfo.InvariantCulture))));

            string screenPartitionCsv = Path.Combine(workDir, "denovo_screen_three_way_partition.csv");
            screen.SavePartitionCsv(screenPartitionCsv);

            string enginePartitionCsv = Path.Combine(workDir, "denovo_three_way_partition.csv");

            if (!File.Exists(enginePartitionCsv))
            {
                Console.WriteLine("        (no engine partition file to compare against; that section was skipped)");
            }
            else
            {
                // BYTE FOR BYTE. A resemblance would be consistent with a different TIC trim, a
                // different grain on the de novo side or another source for the PatternLab side, each
                // of which moves the shares by a fraction of a percent - the same size as the effect
                // the plot exists to show.
                byte[] fromScreen = File.ReadAllBytes(screenPartitionCsv);
                byte[] fromEngine = File.ReadAllBytes(enginePartitionCsv);

                Check("the screen's partition file is the engine section's file, byte for byte",
                    fromScreen.Length == fromEngine.Length && fromScreen.SequenceEqual(fromEngine),
                    fromScreen.Length + " bytes vs " + fromEngine.Length);
            }

            Console.WriteLine("        relatorio: " + screenPartitionCsv);
            Console.WriteLine("        status line: " + status.Text);
        }

    }
}
