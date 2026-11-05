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

        // ==================================================== the de novo screen

        /// <summary>
        /// The source-aware de novo evidence screen, driven through the very methods its buttons call.
        ///
        /// It builds its own two-file PEAKS export, so the whole path - folder to filter to verdicts to
        /// review queue to all three CSVs - runs with no arguments and no real data. The synthetic rows are
        /// chosen so that each criterion rejects exactly one of them, which is the only way to tell a
        /// filter that works from one that keeps everything.
        /// </summary>
        private static void DeNovoScreenTests(string workDir)
        {
            DeNovoControl screen;

            try { screen = new DeNovoControl(); }
            catch (Exception ex)
            {
                Check("the de novo screen builds", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            Check("the de novo screen builds", true);

            var status = UiField<TextBlock>(screen, "TextBlockDeNovoStatus")!;
            var layer2State = UiField<TextBlock>(screen, "TextBlockDeNovoLayer2State")!;
            var reconcile = UiField<Button>(screen, "ButtonReconcile")!;
            var exportSummary = UiField<Button>(screen, "ButtonExportSummary")!;
            var exportQueue = UiField<Button>(screen, "ButtonExportReviewQueue")!;
            var exportOverlap = UiField<Button>(screen, "ButtonExportOverlapStudy")!;
            var overlapScan = UiField<Label>(screen, "LabelOverlapScan")!;
            var overlapPrecursor = UiField<Label>(screen, "LabelOverlapPrecursor")!;
            var overlapExact = UiField<Label>(screen, "LabelOverlapExact")!;
            var overlapDivergent = UiField<Label>(screen, "LabelOverlapDivergent")!;
            var queueGrid = UiField<DataGrid>(screen, "DataGridReviewQueue")!;
            var minLength = UiField<TextBox>(screen, "TextBoxMinLength")!;
            var minAlc = UiField<TextBox>(screen, "TextBoxMinAlc")!;
            var stretchLength = UiField<TextBox>(screen, "TextBoxStretchLength")!;
            var stretchFloor = UiField<TextBox>(screen, "TextBoxStretchFloor")!;
            var directory = UiField<TextBox>(screen, "TextBoxPeaksDirectory")!;
            var breakdown = UiField<TextBlock>(screen, "TextBlockPerRunBreakdown")!;

            // --- the state it opens in ------------------------------------------------------------
            Check("Layer 2 is blocked until a filter pass has kept a row",
                !reconcile.IsEnabled && !exportSummary.IsEnabled && !exportQueue.IsEnabled
                && !exportOverlap.IsEnabled);
            Check("and the Layer 2 badge says so rather than looking merely pending",
                layer2State.Text.StartsWith("BLOCKED"), layer2State.Text);
            Check("the status line opens by naming step 1 and what to press",
                status.Text.Contains("Step 1") && status.Text.Contains("Load and filter"), status.Text);
            Check("nothing is imported or reconciled yet",
                screen.FilteredRows.Count == 0 && screen.Reconciled.Count == 0
                && screen.ReviewQueue.Count == 0 && screen.Psms.Count == 0);
            Check("the review queue starts empty", queueGrid.ItemsSource == null);

            // --- Layers 3 to 5 open blocked, and each of them says why ----------------------------
            var searchHomology = UiField<Button>(screen, "ButtonSearchHomology")!;

            var layer4State = UiField<TextBlock>(screen, "TextBlockDeNovoLayer4State")!;

            var familyGrid = UiField<DataGrid>(screen, "DataGridHomologyFamilies")!;
            var homologyMapped = UiField<Label>(screen, "LabelHomologyMapped")!;

            var minIdentity = UiField<TextBox>(screen, "TextBoxMinIdentity")!;
            var topHits = UiField<TextBox>(screen, "TextBoxTopHits")!;
            var considerIL = UiField<CheckBox>(screen, "CheckBoxConsiderILSame")!;
            var kqEquivalent = UiField<CheckBox>(screen, "CheckBoxKQEquivalent")!;
            var decoyLabel = UiField<TextBox>(screen, "TextBoxDecoyLabel")!;

            Check("every action of Layers 3 to 5 is disabled before anything has been imported",
                !screen.ViewModel.CanExtract && !searchHomology.IsEnabled && !screen.ViewModel.CanExportHomology
                && !screen.ViewModel.CanPartition && !(screen.LastPartition?.IsAvailable == true && !screen.ViewModel.IsBusy) && !(screen.LastPartition?.IsAvailable == true && !screen.ViewModel.IsBusy));

            Check("and the three badges say BLOCKED with a reason, not merely PENDING",
                screen.ViewModel.ExtractState.StartsWith("BLOCKED") && layer4State.Text.StartsWith("BLOCKED")
                && screen.ViewModel.PartitionState.StartsWith("BLOCKED"),
                screen.ViewModel.ExtractState + " | " + layer4State.Text + " | " + screen.ViewModel.PartitionState);

            Check("Layer 3 names the reconciliation as what would unblock it",
                screen.ViewModel.ExtractState.Contains("reconciliation"), screen.ViewModel.ExtractState);
            Check("Layer 4 names the import as what would unblock it",
                layer4State.Text.Contains("rows imported"), layer4State.Text);
            Check("Layer 5 names the extraction as what would unblock it",
                screen.ViewModel.PartitionState.Contains("extraction"), screen.ViewModel.PartitionState);

            // Every disabled control must SAY why it is disabled, which the sweep cannot check: it
            // requires a tooltip, not a tooltip that names the thing that would enable the control.
            Check("the live homology action explains its readiness",
                (searchHomology.ToolTip as string ?? "").Contains("Disabled until"));
            Check("the retired actions are absent from the De novo view",
                screen.FindName("ButtonExtractXic") == null && screen.FindName("ButtonBuildPartition") == null
                && screen.FindName("ButtonExportHomologyCsv") == null);

            Check("legacy extraction/partition state and the live family table start empty",
                screen.ViewModel.XicStatsItems == null && familyGrid.ItemsSource == null
                && screen.ViewModel.PartitionItems == null && screen.ViewModel.PartitionChartRows == null);
            Check("and the two notes say so rather than showing a stale sentence",
                screen.ViewModel.RawMatchPreview.Contains("No reconciliation yet")
                && screen.ViewModel.BranchNote.Contains("No partition yet"),
                screen.ViewModel.RawMatchPreview + " | " + screen.ViewModel.BranchNote);
            Check("nothing has been extracted, searched or partitioned yet",
                screen.LastExtraction == null && screen.LastHomology == null
                && screen.LastPartition == null && screen.RawMatches.Count == 0);

            // --- the homology settings, and their refusals ----------------------------------------
            var homologyDefaults = new FamilyQuant.Homology.HomologyOptions();
            Check("the homology boxes open at the engine's own defaults",
                minIdentity.Text == "0.8" && topHits.Text == "3"
                && considerIL.IsChecked == true && kqEquivalent.IsChecked == false
                && decoyLabel.Text == "Reverse_",
                $"{minIdentity.Text} / {topHits.Text} / {considerIL.IsChecked} / " +
                $"{kqEquivalent.IsChecked} / {decoyLabel.Text}");
            Check("and those really are the engine's defaults, not a second copy of them",
                Math.Abs(homologyDefaults.MinIdentity - 0.8) < 1e-12
                && homologyDefaults.TopHitsPerPeptide == 3 && homologyDefaults.ConsiderILSame
                && !homologyDefaults.KQEquivalent && homologyDefaults.DecoyLabel == "Reverse_");

            void HomologyRefused(string what, TextBox box, string typed, string namedInStatus)
            {
                box.Text = typed;
                bool accepted = screen.ReadHomologySettingsFromBoxes();

                Check("a " + what + " is refused", !accepted, typed);
                Check("and the status line says which box, and that the previous value still holds",
                    status.Text.Contains(namedInStatus) && status.Text.Contains("still in force"),
                    status.Text);
                Check("and the box is put back to the value actually in force after " + what,
                    box.Text != typed, box.Text);
                Check("and the homology settings in force are still the defaults after " + what,
                    Math.Abs(screen.HomologySettings.MinIdentity - 0.8) < 1e-12
                    && screen.HomologySettings.TopHitsPerPeptide == 3
                    && screen.HomologySettings.DecoyLabel == "Reverse_",
                    screen.HomologySettings.MinIdentity + " / " +
                    screen.HomologySettings.TopHitsPerPeptide + " / " +
                    screen.HomologySettings.DecoyLabel);
            }

            HomologyRefused("identity above one", minIdentity, "1.5", "Minimum identity");
            HomologyRefused("top-hit count of zero", topHits, "0", "Top hits per peptide");
            HomologyRefused("blank decoy prefix", decoyLabel, "   ", "Decoy prefix");

            minIdentity.Text = "0.7";
            Check("a valid homology change is accepted", screen.ReadHomologySettingsFromBoxes());
            Check("and takes effect on the settings in force",
                Math.Abs(screen.HomologySettings.MinIdentity - 0.7) < 1e-12,
                screen.HomologySettings.MinIdentity.ToString(CultureInfo.InvariantCulture));
            minIdentity.Text = "0.8";
            Check("and the default can be typed back", screen.ReadHomologySettingsFromBoxes()
                && Math.Abs(screen.HomologySettings.MinIdentity - 0.8) < 1e-12);

            var engineDefaults = new DeNovoFilterCriteria();
            Check("the four boxes open at the engine's own defaults",
                minLength.Text == "7" && minAlc.Text == "80" && stretchLength.Text == "5"
                && stretchFloor.Text == "90",
                $"{minLength.Text} / {minAlc.Text} / {stretchLength.Text} / {stretchFloor.Text}");
            Check("and those really are the engine's defaults, not a second copy of them",
                engineDefaults.MinLength == 7 && engineDefaults.MinAverageLocalConfidence == 80
                && engineDefaults.StretchLength == 5 && engineDefaults.StretchFloor == 90);

            // --- a refused parameter keeps the previous one ---------------------------------------
            // Each of the four is refused in turn, and each time the values IN FORCE must be untouched.
            // No dialog is raised on any of these paths: one would hang this run for ever.
            void Refused(string what, TextBox box, string typed, string namedInStatus)
            {
                box.Text = typed;
                bool accepted = screen.ReadCriteriaFromBoxes();

                Check("a " + what + " is refused", !accepted, typed);
                Check("and the status line says which box, and that the previous value still holds",
                    status.Text.Contains(namedInStatus) && status.Text.Contains("still in force"),
                    status.Text);
                Check("and the box is put back to the value actually in force",
                    box.Text != typed, box.Text);
                Check("and the criteria in force are still the defaults after " + what,
                    screen.Criteria.MinLength == 7 && screen.Criteria.MinAverageLocalConfidence == 80
                    && screen.Criteria.StretchLength == 5 && screen.Criteria.StretchFloor == 90,
                    $"{screen.Criteria.MinLength} / {screen.Criteria.MinAverageLocalConfidence} / " +
                    $"{screen.Criteria.StretchLength} / {screen.Criteria.StretchFloor}");
            }

            Refused("length that is not a number", minLength, "seven", "Minimum length");
            Refused("confidence above 100", minAlc, "150", "Minimum ALC");
            Refused("stretch of no residues", stretchLength, "0", "Stretch length");
            Refused("negative stretch floor", stretchFloor, "-5", "Stretch floor");

            // One bad box refuses the whole pass, so a good value typed beside it must not survive
            // either: the criteria are read all four or none.
            minLength.Text = "9";
            minAlc.Text = "not a number";
            Check("one bad box refuses the pass", !screen.ReadCriteriaFromBoxes());
            Check("and the good value typed beside it is put back too, since the pass is all or nothing",
                minLength.Text == "7" && screen.Criteria.MinLength == 7, minLength.Text);

            minLength.Text = "9";
            minAlc.Text = "75.5";
            Check("a valid change is accepted", screen.ReadCriteriaFromBoxes());
            Check("and takes effect on the criteria in force",
                screen.Criteria.MinLength == 9 && Math.Abs(screen.Criteria.MinAverageLocalConfidence - 75.5) < 1e-12,
                screen.Criteria.MinLength + " / " + screen.Criteria.MinAverageLocalConfidence);

            minLength.Text = "7";
            minAlc.Text = "80";
            Check("and the defaults can be typed back", screen.ReadCriteriaFromBoxes()
                && screen.Criteria.MinLength == 7 && screen.Criteria.MinAverageLocalConfidence == 80);

            // The button's own path, with no folder chosen: a refusal in the status line, never a modal.
            var loadHandler = typeof(DeNovoControl).GetMethod("ButtonLoadAndFilter_Click",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            loadHandler.Invoke(screen, new object?[] { null, null });

            Check("pressing Load and filter with no folder refuses in the status line",
                status.Text.Contains("no folder has been chosen"), status.Text);
            Check("and Layer 2 is still blocked", !reconcile.IsEnabled);

            // --- a whole import, through the control's own methods --------------------------------
            string exportDir = BuildSyntheticPeaksExport(workDir);
            directory.Text = exportDir;

            var import = DeNovoControl.ImportAndFilter(exportDir, screen.Criteria);
            screen.ApplyImport(import);

            Check("both exports are found, including the one in a sub-folder", import.FilesFound == 2,
                import.FilesFound.ToString());
            Check("every row of both files is read", import.RowsRead == 5, import.RowsRead.ToString());
            Check("and exactly the two rows that clear all three filters are kept",
                import.RowsKept == 2 && screen.FilteredRows.Count == 2,
                import.RowsKept + " / " + screen.FilteredRows.Count);
            Check("the short row, the low-ALC row and the row with no confident stretch are all dropped",
                screen.FilteredRows.Select(r => r.Scan).OrderBy(s => s).SequenceEqual(new[] { 100, 200 }),
                string.Join(",", screen.FilteredRows.Select(r => r.Scan)));

            Check("the breakdown reports both runs by name, with what each held and kept",
                breakdown.Text.Contains("syntheticA") && breakdown.Text.Contains("syntheticB")
                && breakdown.Text.Split('\n').Length == 2, breakdown.Text.Replace("\n", " | "));
            Check("the per-run tallies add up to the totals",
                import.PerRun.Sum(t => t.RowsRead) == import.RowsRead
                && import.PerRun.Sum(t => t.RowsKept) == import.RowsKept);

            Check("a filter pass unblocks Layer 2", reconcile.IsEnabled);
            Check("and the status line moves on to step 2, naming the criteria that produced the rows",
                status.Text.Contains("Step 2") && status.Text.Contains("Reconcile"), status.Text);
            Check("the exports stay disabled, because nothing has been reconciled yet",
                !exportSummary.IsEnabled && !exportQueue.IsEnabled && !exportOverlap.IsEnabled);

            // --- the reconciliation, and the queue it produces ------------------------------------
            // Two PSMs against the two kept rows: one disagreeing, one agreeing on a weak score. The
            // second carries a stacked extension (.raw.sqt), which is how SEPro names them.
            var psms = new List<PatternLabPsm>
            {
                new PatternLabPsm { FileName = "syntheticA.raw", ScanNumber = 100,
                    PrecursorMz = 500.2501, ChargeState = 2, Sequence = "K.ANOTHERR.A", XCorr = 4.0 },
                new PatternLabPsm { FileName = "syntheticB.raw.sqt", ScanNumber = 200,
                    PrecursorMz = 600.3000, ChargeState = 2, Sequence = "ISEQVENCE", XCorr = 1.2 },
            };

            var outcome = DeNovoControl.Reconcile(screen.FilteredRows, psms, "synthetic.sepr2");
            screen.ApplyReconciliation(outcome);

            Check("the disagreement is a conflict and the weak agreement is kept as one",
                outcome.Counts[ReconciliationVerdict.Conflict] == 1
                && outcome.Counts[ReconciliationVerdict.EqualButWeakPsm] == 1
                && outcome.Counts[ReconciliationVerdict.DeNovoOnly] == 0,
                string.Join(", ", outcome.Counts.Select(kv => kv.Key + "=" + kv.Value)));

            Check("the run whose PSM file name carries stacked extensions still matched",
                outcome.Rows.Single(r => r.DeNovo.Scan == 200).CounterpartPsm != null);

            Check("the review queue holds exactly the conflicts and the weak agreements",
                screen.ReviewQueue.Count == 2, screen.ReviewQueue.Count.ToString());
            Check("and the table shows exactly those rows, in run then scan order",
                queueGrid.ItemsSource != null
                && queueGrid.ItemsSource.Cast<ReviewQueueRow>().Select(r => r.Scan).SequenceEqual(new[] { 100, 200 }));
            Check("a queue row carries both sequences, the score and the confidences side by side",
                screen.ReviewQueue[0].DeNovoSequence == "PEPTLDEK"
                && screen.ReviewQueue[0].PatternLabSequence == "K.ANOTHERR.A"
                && Math.Abs(screen.ReviewQueue[0].XCorr - 4.0) < 1e-12
                && screen.ReviewQueue[0].MinResidueConfidence == 92
                && screen.ReviewQueue[0].PpmApart < 20,
                screen.ReviewQueue[0].DeNovoSequence + " vs " + screen.ReviewQueue[0].PatternLabSequence);

            Check("the overlap study distinguishes scan, precursor, strict equality and I/L equivalence",
                outcome.Overlap != null
                && outcome.Overlap.SharedFileAndScan == 2
                && outcome.Overlap.ScanLevelStrictSequenceMatchesIgnoringPtm == 0
                && outcome.Overlap.ScanLevelAdditionalIleLeuEquivalentMatches == 1
                && outcome.Overlap.ScanLevelSequenceDivergencesIgnoringPtmStrict == 2
                && outcome.Overlap.ScanLevelSequenceDivergencesAfterIleLeuFold == 1
                && outcome.Overlap.SamePrecursorWithinTolerance == 2
                && outcome.Overlap.StrictSequenceMatchesIgnoringPtm == 0
                && outcome.Overlap.AdditionalIleLeuEquivalentMatches == 1
                && outcome.Overlap.SequenceDivergencesAfterIleLeuFold == 1,
                outcome.Overlap == null ? "missing" :
                    outcome.Overlap.SharedFileAndScan + " / " + outcome.Overlap.SamePrecursorWithinTolerance +
                    " / " + outcome.Overlap.StrictSequenceMatchesIgnoringPtm + " / +" +
                    outcome.Overlap.AdditionalIleLeuEquivalentMatches + " / " +
                    outcome.Overlap.SequenceDivergencesAfterIleLeuFold);
            Check("the four overlap labels expose those nested stages on screen",
                overlapScan.Content.ToString()!.Contains("2")
                && overlapPrecursor.Content.ToString()!.Contains("2")
                && overlapExact.Content.ToString()!.Contains("0")
                && overlapDivergent.Content.ToString()!.Contains("2"),
                overlapScan.Content + " | " + overlapPrecursor.Content + " | " +
                overlapExact.Content + " | " + overlapDivergent.Content);

            Check("all three exports become available once there are verdicts",
                exportSummary.IsEnabled && exportQueue.IsEnabled && exportOverlap.IsEnabled);
            Check("the status line reports what was joined and what is in the review queue",
                status.Text.Contains("review queue"), status.Text);

            // --- the three CSVs, written and read back --------------------------------------------
            string summaryCsv = Path.Combine(workDir, "denovo_screen_synthetic_summary.csv");
            string queueCsv = Path.Combine(workDir, "denovo_screen_synthetic_queue.csv");
            string overlapCsv = Path.Combine(workDir, "denovo_screen_synthetic_overlap.csv");

            screen.ExportSummaryCsv(summaryCsv);
            screen.ExportReviewQueueCsv(queueCsv);
            screen.ExportOverlapStudyCsv(overlapCsv);

            var summaryLines = File.ReadAllLines(summaryCsv).Where(l => l.Trim().Length > 0).ToList();
            Check("the summary carries a header and one line per run and verdict",
                summaryLines.Count == 3, summaryLines.Count.ToString());
            Check("and its row counts add up to the rows that were reconciled",
                summaryLines.Skip(1).Sum(l => int.Parse(SplitCsvLine(l)[2], CultureInfo.InvariantCulture)) == 2);

            var queueLines = File.ReadAllLines(queueCsv).Where(l => l.Trim().Length > 0).ToList();
            Check("the queue CSV holds exactly the rows of the table",
                queueLines.Count == screen.ReviewQueue.Count + 1, queueLines.Count.ToString());
            Check("with the two sequences and the verdict where the header says they are",
                SplitCsvLine(queueLines[1])[2] == "OverlappingAlternativeSequence"
                && SplitCsvLine(queueLines[1])[3] == "PEPTLDEK"
                && SplitCsvLine(queueLines[1])[8] == "K.ANOTHERR.A",
                queueLines[1]);
            Check("and every number written with a dot, whatever the machine's language",
                double.TryParse(SplitCsvLine(queueLines[1])[11], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _), queueLines[1]);

            string overlapText = File.ReadAllText(overlapCsv);
            Check("the overlap CSV names each denominator and both strict and I/L-folded comparisons",
                overlapText.Contains("same_scan_psm_within_mz_tolerance,2,2")
                && overlapText.Contains("strict_sequence_match_ignoring_ptm,0,2")
                && overlapText.Contains("additional_il_equivalent_match,1,2")
                && overlapText.Contains("sequence_divergence_after_il_fold,1,2"), overlapText);

            // --- a fresh import must not leave the old verdicts standing ---------------------------
            screen.ApplyImport(DeNovoControl.ImportAndFilter(exportDir, screen.Criteria));

            Check("importing again drops the reconciliation, which was about other rows",
                screen.Reconciled.Count == 0 && screen.ReviewQueue.Count == 0
                && queueGrid.ItemsSource == null,
                screen.Reconciled.Count.ToString());
            Check("and the exports go back to being disabled, since there is nothing to write",
                !exportSummary.IsEnabled && !exportQueue.IsEnabled && !exportOverlap.IsEnabled);
            Check("while the imported rows themselves survive", screen.FilteredRows.Count == 2);

            // --- Layer 3: matching runs to acquisitions, and the gate that drives ------------------
            // Reconciled against an EMPTY PSM list, so every kept row is new territory and both runs
            // need an area. The reconciliation above kept nothing for extraction, which is a real
            // state of this screen but not the one the matching has to be checked in.
            var allNew = DeNovoControl.Reconcile(screen.FilteredRows, new List<PatternLabPsm>(), "no-psms.sepr2");
            screen.ApplyReconciliation(allNew);

            Check("with no identification to meet, both runs carry rows that need an area",
                screen.KeptRunNames().SequenceEqual(new[] { "syntheticA", "syntheticB" }),
                string.Join(",", screen.KeptRunNames()));

            string rawPartial = Path.Combine(workDir, "denovo_screen_raw_partial");
            string rawComplete = Path.Combine(workDir, "denovo_screen_raw_complete");
            Directory.CreateDirectory(rawPartial);
            Directory.CreateDirectory(rawComplete);
            File.WriteAllText(Path.Combine(rawPartial, "syntheticA.raw"), "");
            File.WriteAllText(Path.Combine(rawComplete, "syntheticA.raw"), "");
            File.WriteAllText(Path.Combine(rawComplete, "syntheticB.raw"), "");

            screen.ViewModel.RawDirectory = rawPartial; screen.ViewModel.RawDirectoryChanged();

            Check("the run whose acquisition is missing is NAMED in the preview, not merely counted",
                screen.ViewModel.RawMatchPreview.Contains("syntheticB.raw") && screen.ViewModel.RawMatchPreview.Contains("NOT FOUND"),
                screen.ViewModel.RawMatchPreview.Replace("\n", " | "));
            Check("and the run that was found is named with the file that answered for it",
                screen.ViewModel.RawMatchPreview.Contains("syntheticA · found syntheticA.raw"),
                screen.ViewModel.RawMatchPreview.Replace("\n", " | "));
            Check("Extract stays disabled while one kept run has no acquisition", !screen.ViewModel.CanExtract);

            screen.ViewModel.ExtractAsync().GetAwaiter().GetResult();

            Check("pressing it anyway refuses in the status line and names the missing acquisition",
                status.Text.Contains("syntheticB.raw"), status.Text);
            Check("and nothing was extracted", screen.LastExtraction == null);

            screen.ViewModel.RawDirectory = rawComplete; screen.ViewModel.RawDirectoryChanged();

            Check("once every kept run has found its acquisition, Extract is offered",
                screen.ViewModel.CanExtract && !screen.ViewModel.RawMatchPreview.Contains("NOT FOUND"),
                screen.ViewModel.RawMatchPreview.Replace("\n", " | "));
            Check("and the Layer 3 badge turns READY", screen.ViewModel.ExtractState == "READY", screen.ViewModel.ExtractState);

            // The cache belongs to the acquisitions, so in the application it goes beside them. The
            // seam exists so the suite can answer from caches it already has; it must not change the
            // path the application computes when it is not set.
            string cachePath = DeNovoControl.CachePathFor("syntheticA", rawComplete);
            Check("the extraction cache goes into a folder beside the acquisitions",
                cachePath.StartsWith(Path.Combine(rawComplete, DeNovoControl.CacheFolderName),
                    StringComparison.OrdinalIgnoreCase), cachePath);

            DeNovoControl.CachePathForRunOverride = run => Path.Combine(workDir, "seam_" + run + ".json");
            Check("and the harness seam replaces it without the application ever asking for one",
                DeNovoControl.CachePathFor("syntheticA", rawComplete)
                    == Path.Combine(workDir, "seam_syntheticA.json"),
                DeNovoControl.CachePathFor("syntheticA", rawComplete));
            DeNovoControl.CachePathForRunOverride = null;

            // --- Layer 4 does not wait on Layer 3 -------------------------------------------------
            Check("Search is offered on kept rows alone, because homology reads peptides and not areas",
                searchHomology.IsEnabled && screen.LastExtraction == null);
            Check("and the peptides it would search are the reconciliation's kept rows, deduplicated",
                screen.PeptidesForHomology().Count == 2,
                string.Join(",", screen.PeptidesForHomology()));

            // --- Layer 5: both inputs are required, and each absence says which -------------------

            screen.ViewModel.PartitionPlp = Path.Combine(workDir, "no_such_project.plp");
            Check("naming a PatternLab project does not unblock Layer 5 while no run has an area",
                !screen.ViewModel.CanPartition);
            Check("and the badge still names the extraction as what is missing",
                screen.ViewModel.PartitionState.Contains("extraction"), screen.ViewModel.PartitionState);

            var synthetic = new DeNovoXicExtraction { RawDirectory = rawComplete };
            foreach (string run in new[] { "syntheticA", "syntheticB" })
            {
                var stats = new DeNovoBatchStats
                {
                    RunName = run, RowsIn = 1, UniquePrecursors = 1, WithSignal = 1,
                    DistinctPeaks = 1, TotalAreaM0 = 1000, TotalEnvelope = 1200, ElapsedSeconds = 0.1,
                };
                synthetic.Stats.Add(stats);
                synthetic.ByRun[run] = stats;
            }

            screen.ApplyExtraction(synthetic);

            Check("an extraction fills the per-run table and the totals beside it",
                screen.ViewModel.XicStatsItems != null
                && screen.ViewModel.XicStatsItems.Cast<DeNovoBatchStats>().Count() == 2
                && Math.Abs(screen.LastExtraction!.TotalAreaM0 - 2000) < 1e-12,
                screen.LastExtraction?.TotalAreaM0.ToString(CultureInfo.InvariantCulture));
            Check("and with a PatternLab project already named, Layer 5 is unblocked",
                screen.ViewModel.CanPartition && screen.ViewModel.PartitionState == "READY", screen.ViewModel.PartitionState);

            screen.ViewModel.PartitionPlp = "";
            Check("taking the project away blocks it again, and the badge says it is waiting on you",
                !screen.ViewModel.CanPartition && screen.ViewModel.PartitionState == "PENDING", screen.ViewModel.PartitionState);
            screen.ViewModel.PartitionPlp = Path.Combine(workDir, "no_such_project.plp");

            // A refusal is a first-class answer: it is shown word for word and nothing is drawn.
            const string refusal = "Neither source resolved per run. .plp: This project merged technical "
                                 + "replicates. .xic2: No .xic2 path was given.";
            screen.ApplyPartition(new DeNovoPartition { BranchNote = refusal });

            Check("a partition that no source could answer shows the refusal word for word",
                screen.ViewModel.BranchNote == refusal, screen.ViewModel.BranchNote);
            Check("and draws nothing rather than an empty bar",
                screen.ViewModel.PartitionItems == null && screen.ViewModel.PartitionChartRows == null
                && !(screen.LastPartition?.IsAvailable == true && !screen.ViewModel.IsBusy) && !(screen.LastPartition?.IsAvailable == true && !screen.ViewModel.IsBusy));

            var ticFixture = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["syntheticA"] = PartitionValue.Measured(1000, ThreeWayPartition.SourceRawTic),
                ["syntheticB"] = PartitionValue.Measured(2000, ThreeWayPartition.SourceRawTic),
            };
            var plFixture = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["syntheticA"] = PartitionValue.Recorded(400, ThreeWayPartition.SourceXic2Quant),
                ["syntheticB"] = PartitionValue.Recorded(500, ThreeWayPartition.SourceXic2Quant),
            };
            var dnFixture = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["syntheticA"] = PartitionValue.Measured(100, ThreeWayPartition.SourceDeNovoM0),
                ["syntheticB"] = PartitionValue.Measured(200, ThreeWayPartition.SourceDeNovoM0),
            };

            var fixtureRows = ThreeWayPartition.Build(ticFixture, plFixture, dnFixture);
            const string branch = "PatternLab quants taken from the .xic2 stored quants.";
            screen.ApplyPartition(new DeNovoPartition
            {
                BranchNote = branch,
                Rows = fixtureRows,
                Grouped = ThreeWayPartition.Grouped(fixtureRows),
            });

            var shown = screen.ViewModel.PartitionItems!.Cast<ThreeWayPartitionRow>().ToList();
            Check("a built partition shows one line per run with the aggregate last",
                shown.Count == 3 && !shown[0].IsGrouped && !shown[1].IsGrouped && shown[2].IsGrouped
                && shown[2].RunName == ThreeWayPartition.GroupedRunName,
                string.Join(" | ", shown.Select(r => r.RunName)));
            Check("legacy partition chart input is retained for independent rendering",
                screen.ViewModel.PartitionChartRows != null);
            Check("legacy partition export state remains available",
                (screen.LastPartition?.IsAvailable == true && !screen.ViewModel.IsBusy));
            Check("the branch note is shown word for word, refusal or not", screen.ViewModel.BranchNote == branch,
                screen.ViewModel.BranchNote);
            Check("every line closes on 100%", shown.All(r => r.Closes),
                string.Join(" | ", shown.Select(r => r.ShareSum.ToString("R", CultureInfo.InvariantCulture))));

            // C11 retires this screen table. Its retained compatibility state still carries the exact provenance.
            string ticTip = screen.ViewModel.PartitionSample?.Tic.Label ?? "";
            string plTip = screen.ViewModel.PartitionSample?.PatternLab.Label ?? "";

            Check("legacy partition state names where total ion current came from",
                ticTip.Contains("measured:" + ThreeWayPartition.SourceRawTic), ticTip);
            Check("and legacy PatternLab state names the source that actually answered",
                plTip.Contains("recorded:" + ThreeWayPartition.SourceXic2Quant), plTip);

            string partitionCsv = Path.Combine(workDir, "denovo_screen_synthetic_partition.csv");
            screen.SavePartitionCsv(partitionCsv);
            var partitionLines = File.ReadAllLines(partitionCsv).Where(l => l.Trim().Length > 0).ToList();
            Check("the saved partition is the header, the two runs and the aggregate",
                partitionLines.Count == 4, partitionLines.Count.ToString());
            Check("and the file is the engine's own writer, byte for byte",
                File.ReadAllText(partitionCsv) == ThreeWayPartition.BuildCsv(fixtureRows));

            // Layer 4 searches whatever PeptidesForHomology yields when Search is pressed, and its
            // counts carry no record of which set that was, so a pass must not outlive those rows.
            DeNovoHomology PretendPass() => new DeNovoHomology
            {
                FcfPath = "synthetic.fcf",
                PeptidesSearched = 2,
                MappedToTargets = 1,
                Unmatched = 1,
                ElapsedSeconds = 0.5,
                Families = new List<FamilyQuant.Homology.DeNovoFamilyRow>
                {
                    new FamilyQuant.Homology.DeNovoFamilyRow
                        { FamilyId = 1, PeptideCount = 1, Description = "synthetic family" },
                },
            };

            screen.ApplyHomology(PretendPass());

            Check("a homology pass fills Layer 4 and offers the assignments export",
                screen.LastHomology != null && familyGrid.ItemsSource != null && screen.ViewModel.CanExportHomology,
                homologyMapped.Content as string);

            // A fresh import must not leave any of the four downstream Layers standing.
            screen.ApplyImport(DeNovoControl.ImportAndFilter(exportDir, screen.Criteria));

            Check("importing again drops the extraction and the partition it fed",
                screen.LastExtraction == null && screen.LastPartition == null
                && screen.ViewModel.XicStatsItems == null && screen.ViewModel.PartitionItems == null
                && screen.ViewModel.PartitionChartRows == null);
            Check("and their buttons go back to being disabled",
                !screen.ViewModel.CanExtract && !screen.ViewModel.CanPartition && !(screen.LastPartition?.IsAvailable == true && !screen.ViewModel.IsBusy)
                && !(screen.LastPartition?.IsAvailable == true && !screen.ViewModel.IsBusy));
            Check("importing again also drops the homology pass, which searched the replaced rows",
                screen.LastHomology == null && familyGrid.ItemsSource == null
                && !screen.ViewModel.CanExportHomology
                && (homologyMapped.Content as string) == "Mapped to targets: -",
                homologyMapped.Content as string);
            Check("while Layer 4 itself stays open, because the new rows are peptides too",
                searchHomology.IsEnabled && screen.FilteredRows.Count == 2);

            // The other side of the same door: a reconciliation changes the searched set as well,
            // because PeptidesForHomology switches from the filtered rows to the kept verdicts.
            screen.ApplyHomology(PretendPass());
            screen.ApplyReconciliation(
                DeNovoControl.Reconcile(screen.FilteredRows, psms, "synthetic.sepr2"));

            Check("reconciling drops the homology pass too, since it searched the other set of rows",
                screen.LastHomology == null && familyGrid.ItemsSource == null
                && !screen.ViewModel.CanExportHomology
                && (homologyMapped.Content as string) == "Mapped to targets: -",
                homologyMapped.Content as string);

            // --- every modal dialog guards on being on a display ----------------------------------
            // Off a display a Win32 file dialog is a window nothing can close, so this run would hang
            // rather than fail. Each handler is pressed here for exactly that reason.
            foreach (var (handlerName, dialogRefusal) in new[]
                     {
                         ("ButtonBrowsePeaksDirectory_Click", "No folder was chosen"),
                         ("ButtonBrowseSeproPackage_Click", "No SEPro package was chosen"),
                         ("ButtonBrowseHomologyFcf_Click", "No Family Cluster File was chosen"),
                         ("ButtonExportSummary_Click", "Nothing was exported"),
                         ("ButtonExportReviewQueue_Click", "Nothing was exported"),
                     })
            {
                typeof(DeNovoControl).GetMethod(handlerName, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(screen, new object?[] { null, null });

                Check("pressing " + handlerName.Replace("_Click", "") + " off a display refuses in the "
                      + "status line instead of opening a dialog nothing can close",
                    status.Text.StartsWith(dialogRefusal) && status.Text.Contains("not on a display"),
                    status.Text);
            }

            // --- a failed import puts the criteria back --------------------------------------------
            // The rows on screen were kept by the PREVIOUS criteria, so leaving the newly typed ones in
            // force would make the boxes describe a filter that produced none of what is displayed.
            minLength.Text = "12";
            directory.Text = Path.Combine(workDir, "denovo_screen_no_such_export");

            loadHandler.Invoke(screen, new object?[] { null, null });

            Check("an import that could not run puts the criteria in force back to the previous ones",
                screen.Criteria.MinLength == 7 && minLength.Text == "7",
                screen.Criteria.MinLength + " / " + minLength.Text);
            Check("and the status line says so as well as naming the folder it could not read",
                status.Text.Contains("does not exist") && status.Text.Contains("put back to"),
                status.Text);
            Check("while the rows kept by those criteria are still on screen",
                screen.FilteredRows.Count == 2);

            directory.Text = exportDir;

            // --- editing the RAW folder drops what was measured in the previous one -----------------
            screen.ApplyExtraction(synthetic);
            Check("an extraction is on screen before the acquisitions folder is edited",
                screen.LastExtraction != null && screen.ViewModel.XicStatsItems != null);

            screen.ViewModel.RawDirectory = Path.Combine(workDir, "denovo_screen_raw_elsewhere"); screen.ViewModel.RawDirectoryChanged();

            Check("editing the acquisitions folder drops the extraction and any partition built on it",
                screen.LastExtraction == null && screen.LastPartition == null
                && screen.ViewModel.XicStatsItems == null && screen.ViewModel.PartitionItems == null,
                screen.LastExtraction == null ? "dropped" : "still standing");
            Check("and the status line says what was dropped and why, rather than changing in silence",
                status.Text.Contains("folder holding the acquisitions was changed"), status.Text);
            Check("Layer 4 is deliberately untouched by it, because no peptide comes from that box",
                searchHomology.IsEnabled && screen.FilteredRows.Count == 2);

            screen.ViewModel.RawDirectory = rawComplete; screen.ViewModel.RawDirectoryChanged();

            // --- one busy discipline: a long operation disables ALL five Layers ---------------------
            // Driven through the real handler, with the import held in flight by a TaskCompletionSource
            // rather than by a sleep: a sleep observes the same screen slowly, and what has to be
            // observed is the screen WHILE the operation is unfinished. Before this discipline existed,
            // Layers 3 to 5 stayed live during an import and could be started against rows the import
            // was about to replace, landing after them.
            var groupBoxes = new[]
            {
                "GroupBoxImportAndFilter", "GroupBoxReconcile", "GroupBoxHomology",
            }.Select(n => UiField<GroupBox>(screen, n)!).ToList();

            // Q7 folded Extract areas, Attribute to families and Partition ion current into a collapsed
            // advanced Expander. IsEnabled coercion flows through the VISUAL tree, which a collapsed
            // Expander does not realise, so the advanced Layers are opened here first: that is the state a
            // user driving those controls is in, and it is what lets a disabled group box coerce the
            // buttons inside it, which is the thing being checked below.
            UiField<Expander>(screen, "ExpanderStandalone")!.IsExpanded = true;

            // WPF coerces a control's IsEnabled from its parent through the VISUAL tree, and that tree
            // does not exist until the templates have been applied. The screen is laid out once here so
            // that a disabled group box really does disable the buttons inside it, which is the thing
            // being checked; without this the buttons would keep reporting the state machine's own
            // value and the check would pass for the wrong reason.
            screen.Measure(new Size(1600, 1200));
            screen.Arrange(new Rect(0, 0, 1600, 1200));
            screen.UpdateLayout();

            screen.ApplyHomology(PretendPass());

            Check("before the import all remaining panels are enabled and Layer 4 offers its search",
                groupBoxes.All(g => g.IsEnabled) && searchHomology.IsEnabled && screen.ViewModel.CanExportHomology,
                string.Join(",", groupBoxes.Select(g => g.IsEnabled)));

            var inFlight = new TaskCompletionSource<DeNovoImport>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var previousContext = SynchronizationContext.Current;

            try
            {
                // The suite has no message loop, so the handler's continuation would never resume.
                // Posting it to this thread's dispatcher is what PumpDispatcher then runs.
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                DeNovoControl.ImportOverride = (dir, criteria) => inFlight.Task;

                loadHandler.Invoke(screen, new object?[] { null, null });

                Check("while an import is in flight every remaining panel is disabled",
                    groupBoxes.All(g => !g.IsEnabled),
                    string.Join(",", groupBoxes.Select(g => g.IsEnabled)));
                Check("so no later Layer can be started against the rows the import is about to replace",
                    !reconcile.IsEnabled && !screen.ViewModel.CanExtract && !searchHomology.IsEnabled
                    && !screen.ViewModel.CanExportHomology && !screen.ViewModel.CanPartition,
                    searchHomology.IsEnabled.ToString());
                Check("and the status line says the import is running",
                    status.Text.Contains("Reading the de novo exports"), status.Text);
                Check("nothing downstream has been replaced yet, because the import has not landed",
                    screen.LastHomology != null);

                inFlight.SetResult(DeNovoControl.ImportAndFilter(exportDir, screen.Criteria));
                PumpDispatcher();

                Check("once it lands all remaining panels are enabled again",
                    groupBoxes.All(g => g.IsEnabled),
                    string.Join(",", groupBoxes.Select(g => g.IsEnabled)));
                Check("and it is the state machine, not the busy flag, that decides what is offered",
                    reconcile.IsEnabled && searchHomology.IsEnabled
                    && !screen.ViewModel.CanExtract && !screen.ViewModel.CanPartition && !exportSummary.IsEnabled,
                    reconcile.IsEnabled + " / " + screen.ViewModel.CanExtract);
                Check("the import that landed still dropped the pass describing the replaced rows",
                    screen.LastHomology == null && familyGrid.ItemsSource == null
                    && !screen.ViewModel.CanExportHomology && screen.Reconciled.Count == 0,
                    screen.LastHomology == null ? "dropped" : "still standing");
                Check("while the rows it imported are the ones now on screen",
                    screen.FilteredRows.Count == 2, screen.FilteredRows.Count.ToString());
            }
            catch (Exception ex)
            {
                Check("the in-flight import ran to completion through the handler", false,
                    ex.ToString());
            }
            finally
            {
                DeNovoControl.ImportOverride = null;
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            // --- the screen's own help ------------------------------------------------------------
            var helpButton = UiField<Button>(screen, "ButtonDeNovoHelp")!;

            Check("the screen carries its own Help button, beside the line that says what to do next",
                helpButton.ToolTip is string helpTip && helpTip.Length > 100, helpButton.ToolTip as string);
            Check("and the button is always available, because the help does not depend on a Layer",
                helpButton.IsEnabled);

            Check("the page it opens is the de novo page, built from the criteria in force",
                screen.HelpText().Contains("THE DE NOVO REVIEW SCREEN")
                && screen.HelpText().Contains("Minimum length (" + screen.Criteria.MinLength + " residues)"));
            string deepNovoHelp = HelpContent.BuildDeNovo(screen.Criteria, DeNovoSource.DeepNovo);
            Check("DeepNovo help uses SCORE terminology without borrowing the PEAKS-only census",
                deepNovoHelp.Contains("Selected source: DeepNovo")
                && deepNovoHelp.Contains("Minimum SCORE (80%)")
                && !deepNovoHelp.Contains("35,686 rows of 181,643"), deepNovoHelp);

            // Headless: no display, so no window may be opened and the refusal has to be said in the
            // status line, exactly like the detached chart.
            typeof(DeNovoControl).GetMethod("ButtonDeNovoHelp_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(screen, new object?[] { null, null });

            Check("pressing Help off a display says so rather than opening a window nothing closes",
                status.Text.Contains("not on a display"), status.Text);

            DeNovoScreenNavigationTests();
        }
    }
}
