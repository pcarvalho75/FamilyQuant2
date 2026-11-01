using Microsoft.Win32;
using PatternTools.FastaTools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant
{
    /// <summary>
    /// Interaction logic for USearch.xaml
    /// </summary>
    public partial class USearchLib : UserControl
    {
        public FamilyClusterFile? fcf { get; private set; }

        /// <summary>
        /// A clustering sweep is running. The Wait cursor alone does NOT block input, so without this
        /// the user could start Divide &amp; Conquer on top of a running identity scan.
        /// </summary>
        private bool busy;
        public string ShellStatusText => busy ? "Running USEARCH" : fcf == null ? "Ready to build" : "Families written";
        public string ShellHelpText => TextBlockUSearchState.Text;
        public event EventHandler? ShellStatusChanged;
        public bool UseShellStatusBar
        {
            get => BorderUSearchStatus.Visibility == Visibility.Collapsed;
            set => BorderUSearchStatus.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }

        public USearchLib()
        {
            InitializeComponent();
        }

        private bool BeginRun(string what)
        {
            if (busy)
            {
                MessageBox.Show(
                    "Nothing was started: a clustering run is already going.\n\n" +
                    "USEARCH is being run on this database right now, and two runs over the same files " +
                    "would interfere with each other.\n\n" +
                    "Next step: wait for the current run to finish. The buttons come back by themselves.",
                    "Already running", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            busy = true;
            ButtonInput.IsEnabled = false;
            ButtonDivideAndConquer.IsEnabled = false;
            ButtonScanIdentities.IsEnabled = false;
            ButtonRemoveRedundancy.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            TextBlockUSearchState.Text = "Working · " + what + " This runs USEARCH and cannot be interrupted; " +
                                         "the buttons come back when it finishes.";
            ShellStatusChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void EndRun()
        {
            busy = false;
            ButtonInput.IsEnabled = true;
            ButtonDivideAndConquer.IsEnabled = true;
            ButtonScanIdentities.IsEnabled = true;
            ButtonRemoveRedundancy.IsEnabled = true;
            Mouse.OverrideCursor = null;

            UpdateState();
        }

        /// <summary>
        /// Says where the user is in this panel and what the next action is. Nothing in the window used
        /// to indicate that "Calculate FDR" was greyed out because no file had been generated yet.
        /// </summary>
        private void UpdateState()
        {
            TextBlockUSearchState.Text = fcf == null
                ? "Step 1 of 4 · Pick a protein database and press \"Input a FASTA file\". That writes a " +
                  "Family Cluster File (.fcf) beside it, which you then open with File · Load Family " +
                  "Cluster File."
                : "Step 1 done · A Family Cluster File has been written. You can check the clustering with " +
                  "\"Calculate FDR\", or go straight to File · Load Family Cluster File to attach " +
                  "quantitation to these families.";
            ShellStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Warns ONCE, at the start of a clustering operation, when exhaustive clustering is about to run
        /// the all-versus-all alignment on a large database (over 10,000 sequences). Returns true to
        /// proceed, false to abort. There is NO automatic cap: proceeding runs the full exhaustive pass
        /// at any size. This lives in the panel, not in GenerateFCF, because GenerateFCF is called many
        /// times per operation (once per identity in a sweep or a Divide-and-Conquer search) and the
        /// warning must be shown only once.
        ///
        /// It is guarded by PresentationSource so the headless test harness, which drives this panel with
        /// no window, is never blocked by a modal dialog; there, it simply proceeds at any size.
        /// </summary>
        private bool ConfirmLargeExhaustive(string inputFile, bool exhaustive)
        {
            if (!exhaustive) return true;

            int count;
            try { count = USearchTools.CountSequences(new FileInfo(inputFile)); }
            catch { return true; }   // if the count cannot be read, do not block; the run surfaces real errors

            if (count <= 10000) return true;

            // No live visual means the harness (or a design host) is driving this: proceed without a
            // dialog, and never impose a cap.
            if (PresentationSource.FromVisual(this) == null) return true;

            var answer = MessageBox.Show(
                "\"" + Path.GetFileName(inputFile) + "\" has " +
                count.ToString("N0", CultureInfo.CurrentCulture) + " sequences. Exhaustive clustering " +
                "aligns every pair of them in full, which on a database this large can take several " +
                "minutes and a large amount of memory.\n\n" +
                "It is not capped: choose Yes to run the full alignment at this size. Choose No to stop, " +
                "then either turn off \"Exhaustive clustering\" for the faster first-pass-only grouping, " +
                "or reduce the database first with \"Remove redundancy from FASTA\".",
                "Run exhaustive clustering on a large database?",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            return answer == MessageBoxResult.Yes;
        }

        private async void ButtonInput_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "FASTA or T-R (*.fasta or *.T-R)|*.fasta;*.T-R;*.T",
                Title = "Choose the protein database to group into families"
            };

            (Window.GetWindow(this) as MainWindow)?.PrepareFileDialog(ofd);
            if (ofd.ShowDialog() != true) return;
            (Window.GetWindow(this) as MainWindow)?.RememberFolder(ofd.FileName);

            int identity = IntegerUpDownIdentity.Value ?? 85;
            string inputFile = ofd.FileName;

            string fcfPath = Path.Combine(
                Path.GetDirectoryName(inputFile) ?? Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(inputFile) + ".fcf");

            // Ask before replacing curated work: the .fcf carries the user's own family ids and
            // annotations, and it used to be overwritten silently by a re-run at another identity.
            if (File.Exists(fcfPath))
            {
                var answer = MessageBox.Show(
                    "\"" + Path.GetFileName(fcfPath) + "\" already exists beside this database and will be " +
                    "replaced.\n\n" +
                    "That file may hold User IDs, hand-merged families and annotations, and none of them " +
                    "can be recovered afterwards.\n\n" +
                    "Next step: choose No and rename the existing file if you want to keep it. Choose Yes " +
                    "to replace it and cluster again at " + identity.ToString(CultureInfo.CurrentCulture) + "%.",
                    "Replace the existing Family Cluster File?",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

                if (answer != MessageBoxResult.Yes) return;
            }

            bool exhaustive = CheckBoxExhaustiveClustering.IsChecked == true;

            // One warning for the whole operation, before any USEARCH runs. Cancel aborts cleanly.
            if (!ConfirmLargeExhaustive(inputFile, exhaustive)) return;

            if (!BeginRun("clustering " + Path.GetFileName(inputFile) + " at " +
                          identity.ToString(CultureInfo.CurrentCulture) + "% identity.")) return;

            try
            {
                // GenerateFCF runs USEARCH; do it off the UI thread so the window stays responsive
                // and any failure becomes a dialog rather than a crash.
                FamilyClusterFile generated = await Task.Run(() => USearchTools.GenerateFCF(new FileInfo(inputFile), identity, exhaustive));

                generated.Save(fcfPath);

                fcf = generated;
                ButtonCalculateFDR.IsEnabled = true;

                EndRun();

                MessageBox.Show(
                    generated.MyExtendedFastaItems.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " sequences were grouped into " +
                    FamilyFilter.CountFamilies(generated.MyExtendedFastaItems).ToString("N0", CultureInfo.CurrentCulture) +
                    " families at " + identity.ToString(CultureInfo.CurrentCulture) + "% identity, and written to\n" +
                    fcfPath + "\n\n" +
                    "Next step: open that file with File · Load Family Cluster File to refine the grouping " +
                    "and attach quantitation. You can also press \"Calculate FDR\" here first to check " +
                    "whether this identity keeps real and decoy sequences apart.",
                    "Family Cluster File generated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                EndRun();
                MessageBox.Show(
                    "The database could not be clustered, and no Family Cluster File was written.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: the usual causes are a duplicated identifier in the database, which the " +
                    "message above names when that is the problem, and antivirus software blocking the " +
                    "bundled USEARCH program. For the second, set the FAMILYQUANT_USEARCH environment " +
                    "variable to a copy of usearch.exe you control.",
                    "Could not cluster the database", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonCalculateFDR_Click(object sender, RoutedEventArgs e)
        {
            if (fcf == null)
            {
                MessageBox.Show(
                    "Nothing was measured: no Family Cluster File has been generated in this panel yet, " +
                    "so there are no families to score.\n\n" +
                    "Next step: press \"Input a FASTA file\" above and choose a target-decoy (.T-R) " +
                    "database.",
                    "Generate the families first", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var score = fcf.CalculateGroupingScore();

            string reading = score.DecoyOnlyGroups + score.MixedGroups == 0
                ? "This database contains no decoy sequences, so the score is 0 by construction and says " +
                  "nothing at all about the clustering. Use a target-decoy (.T-R) database if you want " +
                  "this measurement to mean something."
                : score.MixedGroups == 0
                    ? "No family holds both a real and a decoy sequence, so this identity is above the " +
                      "point where alignments turn into noise. That is the good outcome."
                    : "Some families hold both a real and a decoy sequence, which means alignments at this " +
                      "identity are no longer reliably better than noise. Cluster again at a higher " +
                      "identity.";

            MessageBox.Show(
                "Mixing score: " + score.Score.ToString("P2", CultureInfo.CurrentCulture) + "\n\n" +
                "That is " + score.MixedGroups.ToString("N0", CultureInfo.CurrentCulture) + " of " +
                score.TotalGroups.ToString("N0", CultureInfo.CurrentCulture) +
                " families holding both a real sequence and a reversed (decoy) one.\n\n" +
                reading + "\n\n" +
                "What it cannot tell you: this score never sees two unrelated real families being fused " +
                "together, which is the error that actually distorts a composition. Read it as a floor on " +
                "the identity, not as a measure of clustering quality.",
                "Target/decoy mixing", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void ButtonDivideAndConquer_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "FASTA or T-R (*.fasta or *.T-R)|*.fasta;*.T-R;*.T",
                Title = "Choose the target-decoy database to search over"
            };

            (Window.GetWindow(this) as MainWindow)?.PrepareFileDialog(ofd);
            if (ofd.ShowDialog() != true) return;
            (Window.GetWindow(this) as MainWindow)?.RememberFolder(ofd.FileName);

            string inputFile = ofd.FileName;
            double acceptableScore = (double)(DecimalUpDownMaxFDR.Value ?? 0m);
            bool exhaustive = CheckBoxExhaustiveClustering.IsChecked == true;

            // One warning for the whole search, before any of its many USEARCH runs. Cancel aborts cleanly.
            if (!ConfirmLargeExhaustive(inputFile, exhaustive)) return;

            if (!BeginRun("searching for the lowest usable identity on " + Path.GetFileName(inputFile) + ".")) return;

            try
            {
                // Runs USEARCH several times; keep it off the UI thread.
                DivideAndConquerResult outcome = await Task.Run(() =>
                    FamilyClusterFile.DivideAndConquerDetailed(inputFile, acceptableScore, exhaustive: exhaustive));
                double optimumIdentity = outcome.Identity;
                string stopReason = outcome.StopReason();

                EndRun();

                string caveat = acceptableScore > 0
                    ? "Warning: with an allowance above 0 this lands inside the alignment twilight zone, " +
                      "where the score rises only because alignments have become noise. Families fused " +
                      "there are not biologically meaningful, and fusions between two unrelated real " +
                      "families are not counted by this score at all. The answer is also only approximate " +
                      "above 0, because the score is a ratio that can fall again as families merge."
                    : "This is the lowest identity showing no target/decoy mixing at all: the floor below " +
                      "which alignments stop being better than noise. It is a lower bound, not a " +
                      "recommendation. Pick a higher identity if the biology of your proteins calls for it.";

                MessageBox.Show(
                    "The search settled on " + optimumIdentity.ToString(CultureInfo.CurrentCulture) + "% identity, " +
                    "the lowest that keeps the mixing score at or below " +
                    acceptableScore.ToString("P0", CultureInfo.CurrentCulture) + ".\n\n" +
                    (stopReason.Length > 0
                        ? stopReason + "\n\nIf that is a single short peptide, the floor reflects that peptide " +
                          "rather than noise across the database; \"Scan identity range\" shows the whole curve.\n\n"
                        : string.Empty) +
                    caveat + "\n\n" +
                    "Next step: nothing was saved. Type that identity into the Identity box on the left and " +
                    "press \"Input a FASTA file\" to actually build the Family Cluster File, or use \"Scan " +
                    "identity range\" first to see the whole curve.",
                    "Divide and Conquer finished", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                EndRun();
                MessageBox.Show(
                    "The search could not be completed, and nothing was saved.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: this search needs a database containing reversed decoy sequences, whose " +
                    "identifiers start with \"" + SequenceLabels.DecoyPrefix + "\". On a database without them, choose the identity " +
                    "from \"Scan identity range\" and from the biology instead.",
                    "Could not finish the search", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ButtonScanIdentities_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "FASTA or T-R (*.fasta or *.T-R)|*.fasta;*.T-R;*.T",
                Title = "Choose the database to scan across identities"
            };

            (Window.GetWindow(this) as MainWindow)?.PrepareFileDialog(ofd);
            if (ofd.ShowDialog() != true) return;
            (Window.GetWindow(this) as MainWindow)?.RememberFolder(ofd.FileName);

            string inputFile = ofd.FileName;
            bool exhaustive = CheckBoxExhaustiveClustering.IsChecked == true;

            // One warning for the whole sweep, before any of its ~18 USEARCH runs. Cancel aborts cleanly.
            if (!ConfirmLargeExhaustive(inputFile, exhaustive)) return;

            if (!BeginRun("clustering " + Path.GetFileName(inputFile) + " at every identity from 95% down to 10%.")) return;

            try
            {
                List<IdentityScanRow> rows = await Task.Run(() => FamilyClusterFile.ScanIdentities(inputFile, exhaustive: exhaustive));

                EndRun();

                // The curve first, the table under it: the threshold is chosen by looking at where the
                // family count stops falling, which no column of numbers makes visible.
                // The generated file's own identity, so the sweep shows where this panel already sits.
                // Passing nothing here left the "this file" marker permanently undrawn.
                IdentityScanWindow.Show("Identity scan · " + Path.GetFileName(inputFile),
                                        rows, FormatScanReport(rows), Window.GetWindow(this),
                                        fcf?.ClusteringIdentity);
            }
            catch (Exception ex)
            {
                EndRun();
                MessageBox.Show(
                    "The scan could not be completed, and nothing was saved.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that the database opens and contains no duplicated identifiers, and " +
                    "that antivirus software is not blocking the bundled USEARCH program.",
                    "Could not scan the identity range", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ButtonRemoveRedundancy_Click(object sender, RoutedEventArgs e)
        {
            if (busy)
            {
                MessageBox.Show(
                    "Nothing was started: a run is already going. Wait for it to finish; the buttons come back by themselves.",
                    "Already running", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "FASTA (*.fasta;*.fa;*.fas)|*.fasta;*.fa;*.fas|All files (*.*)|*.*",
                Title = "Choose the FASTA to remove redundant sequences from"
            };
            (Window.GetWindow(this) as MainWindow)?.PrepareFileDialog(ofd);
            if (ofd.ShowDialog() != true) return;
            (Window.GetWindow(this) as MainWindow)?.RememberFolder(ofd.FileName);

            string inputFile = ofd.FileName;
            double identity = (double)(IntegerUpDownRedundancyIdentity.Value ?? 100);
            string outputFile = Path.Combine(
                Path.GetDirectoryName(inputFile) ?? Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(inputFile) + "_NO_REDUNDANCY.fasta");

            busy = true;
            ButtonInput.IsEnabled = false;
            ButtonDivideAndConquer.IsEnabled = false;
            ButtonScanIdentities.IsEnabled = false;
            ButtonRemoveRedundancy.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            TextBlockUSearchState.Text = "Working · removing redundant sequences from " + Path.GetFileName(inputFile) +
                                         ". The original order and headers are kept; the buttons come back when it finishes.";
            ShellStatusChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                var result = await Task.Run(() => RedundancyTools.RemoveRedundancy(new FileInfo(inputFile), identity, outputFile));

                EndRun();

                int removed = result.before - result.after;
                MessageBox.Show(
                    result.before.ToString("N0", CultureInfo.CurrentCulture) + " sequences went in and " +
                    result.after.ToString("N0", CultureInfo.CurrentCulture) + " came out - " +
                    removed.ToString("N0", CultureInfo.CurrentCulture) + " redundant sequence(s) removed at " +
                    identity.ToString(CultureInfo.CurrentCulture) + "% identity.\n\n" +
                    "The original sequence order and the headers were preserved. The result was written to\n" +
                    outputFile + "\n\n" +
                    "Next step: cluster this non-redundant file with \"Input a FASTA file\" above, so near-duplicate " +
                    "entries do not split a family.",
                    "Redundancy removed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                EndRun();
                MessageBox.Show(
                    "The redundant sequences could not be removed, and nothing was written.\n\n" +
                    "Reason given by the program: " + ex.Message,
                    "Could not remove redundancy", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string FormatScanReport(List<IdentityScanRow> rows)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Identity   Families   With targets   Mixed   Score   Score (of target families)");
            sb.AppendLine("---------------------------------------------------------------------------------");

            foreach (var row in rows)
            {
                sb.AppendLine(string.Format(
                    "{0,7}%   {1,8}   {2,12}   {3,5}   {4,5:P2}   {5,24:P2}",
                    row.Identity, row.Groups, row.TargetGroups, row.MixedGroups, row.Score, row.TargetRelativeScore));
            }

            sb.AppendLine();

            // Groups - TargetGroups is the number of decoy-only families: if that is 0 everywhere and
            // nothing ever mixed, the database simply has no decoys and the score column means nothing.
            bool hasDecoys = rows.Any(r => r.Groups > r.TargetGroups || r.MixedGroups > 0);

            if (!hasDecoys)
            {
                sb.AppendLine("This database contains no decoy sequences, so the two score columns are 0 by");
                sb.AppendLine("construction and say nothing about the clustering. Choose the threshold from the");
                sb.AppendLine("family counts and from the biology, or re-run on a target-decoy (.T-R) database.");
            }
            else
            {
                var lowestClean = rows.Where(r => r.MixedGroups == 0).OrderBy(r => r.Identity).FirstOrDefault();

                sb.AppendLine(lowestClean != null
                    ? $"Lowest identity with no target/decoy mixing: {lowestClean.Identity}% ({lowestClean.Groups} families)."
                    : "Every identity in this range already mixes target and decoy sequences.");

                // WHICH families mix, not only how many: one short peptide matching its own reversed
                // decoy can hold the floor up on its own, and only its name shows that.
                List<string> mixed = IdentityScanRow.MixedFamiliesSection(rows);
                if (mixed.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Families that mix target and decoy (smallest first)");
                    sb.AppendLine("---------------------------------------------------");
                    foreach (string line in mixed) sb.AppendLine(line);
                }
            }

            sb.AppendLine();
            sb.AppendLine("How to read this table");
            sb.AppendLine("----------------------");
            sb.AppendLine("'Mixed' counts families holding both target and decoy sequences. A reversed database has");
            sb.AppendLine("the same internal redundancy as the target one, so decoy families collapse at the same rate");
            sb.AppendLine("as target families and grouping two decoys is not an error. The score therefore marks only");
            sb.AppendLine("where alignments stop being better than noise - it CANNOT see two unrelated target families");
            sb.AppendLine("being fused. Use the identity where mixing first appears as a floor, then choose a threshold");
            sb.AppendLine("above it from the family counts and from what the biology of your proteins supports.");
            sb.AppendLine();
            sb.AppendLine("What to do with this table");
            sb.AppendLine("--------------------------");
            sb.AppendLine("Nothing here has been saved. Pick an identity, type it into the Identity box, and press");
            sb.AppendLine("\"Input a FASTA file\" to build the Family Cluster File at that value.");

            return sb.ToString();
        }
    }


}
