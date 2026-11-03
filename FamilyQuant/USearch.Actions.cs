using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class USearchLib
{
    public static string LargeDatabaseWarning(string inputFile, int count) =>
        "\"" + Path.GetFileName(inputFile) + "\" has " + count.ToString("N0", CultureInfo.CurrentCulture) +
        " sequences. Exhaustive clustering aligns every pair of them in full, which on a database this large can take several minutes and a large amount of memory.\n\n" +
        "It is not capped: choose Yes to run the full alignment at this size. Choose No to stop, " +
        "then either turn off \"Exhaustive second pass\" for the faster first-pass-only grouping, " +
        "or reduce the database first with \"Remove redundancy\".";

    private sealed class BuildActions
    {
        private readonly USearchLib owner;
        public BuildActions(USearchLib owner) => this.owner = owner;

        public bool ConfirmLargeExhaustive(string inputFile, bool exhaustive)
        {
            if (!exhaustive) return true;

            int count;
            try { count = USearchTools.CountSequences(new FileInfo(inputFile)); }
            catch { return true; }   // if the count cannot be read, do not block; the run surfaces real errors

            if (count <= 10000) return true;

            // No live visual means the harness (or a design host) is driving this: proceed without a
            // dialog, and never impose a cap.
            if (PresentationSource.FromVisual(owner) == null) return true;

            var answer = owner.ShowBuildMessage(
                LargeDatabaseWarning(inputFile, count),
                "Run exhaustive clustering on a large database?",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            return answer == MessageBoxResult.Yes;
        }

        public void ButtonInput_Click(object sender, RoutedEventArgs e)
        {
            string? selected = owner.ChooseDatabasePath();
            if (selected != null)
            {
                if (!string.Equals(selected, owner.ViewModel.DatabasePath, StringComparison.OrdinalIgnoreCase)) owner.fcf = null;
                owner.ViewModel.DatabasePath = selected;
                owner.UpdateState();
            }
        }

        public async void ButtonBuildFamilies_Click(object sender, RoutedEventArgs e) => await AppOperationHost.OnUi(owner, () => ButtonBuildFamilies_ClickAsync(sender, e));
        private async Task ButtonBuildFamilies_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (!owner.RequireDatabase()) return;
            int identity = owner.IntegerUpDownIdentity.Value ?? 85;
            string inputFile = owner.ViewModel.DatabasePath;

            string fcfPath = Path.Combine(
                Path.GetDirectoryName(inputFile) ?? Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(inputFile) + ".fcf");

            // Ask before replacing curated work: the .fcf carries the user's own family ids and
            // annotations, and it used to be overwritten silently by a re-run at another identity.
            if (File.Exists(fcfPath) && USearchLib.ConfirmOverwriteOverride?.Invoke(fcfPath) != true)
            {
                var answer = owner.ShowBuildMessage(
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

            bool exhaustive = owner.CheckBoxExhaustiveClustering.IsChecked == true;

            // One warning for the whole operation, before any USEARCH runs. Cancel aborts cleanly.
            if (!owner.ConfirmLargeExhaustive(inputFile, exhaustive)) return;

            if (!owner.BeginRun("clustering " + Path.GetFileName(inputFile) + " at " +
                          identity.ToString(CultureInfo.CurrentCulture) + "% identity.")) return;

            try
            {
                // GenerateFCF runs USEARCH; do it off the UI thread so the window stays responsive
                // and any failure becomes a dialog rather than a crash.
                FamilyClusterFile generated = USearchLib.BuildOverride != null ? await USearchLib.BuildOverride(inputFile, identity, exhaustive)
                    : await Task.Run(() => USearchTools.GenerateFCF(new FileInfo(inputFile), identity, exhaustive, owner.Operation));

                owner.Operation!.ThrowIfCancellationRequested();
                await Task.Run(() => generated.Save(fcfPath, owner.Operation));

                owner.fcf = generated;
                owner.ButtonCalculateFDR.IsEnabled = true;

                owner.EndRun();

                owner.ShowBuildMessage(
                    generated.MyExtendedFastaItems.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " sequences were grouped into " +
                    FamilyFilter.CountFamilies(generated.MyExtendedFastaItems).ToString("N0", CultureInfo.CurrentCulture) +
                    " families at " + identity.ToString(CultureInfo.CurrentCulture) + "% identity, and written to\n" +
                    fcfPath + "\n\n" +
                    "Next step: open that file with File > Open to refine the grouping " +
                    "and attach quantitation. You can also press \"Check mixing (FDR)\" here first to check " +
                    "whether this identity keeps real and decoy sequences apart.",
                    "Family Cluster File generated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException) when (owner.Operation?.IsCancellationRequested == true)
            {
                owner.EndRun();
                owner.ShowBuildMessage("Cancelled. The previous files and results were kept.", "Cancelled");
            }
            catch (Exception ex)
            {
                owner.EndRun();
                owner.ShowBuildMessage(
                    "The database could not be clustered, and no Family Cluster File was written.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: the usual causes are a duplicated identifier in the database, which the " +
                    "message above names when that is the problem, and antivirus software blocking the " +
                    "bundled USEARCH program. For the second, set the FAMILYQUANT_USEARCH environment " +
                    "variable to a copy of usearch.exe you control.",
                    "Could not cluster the database", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ButtonCalculateFDR_Click(object sender, RoutedEventArgs e)
        {
            if (owner.fcf == null)
            {
                owner.ShowBuildMessage(
                    "Nothing was measured: no Family Cluster File has been generated in this panel yet, " +
                    "so there are no families to score.\n\n" +
                    "Next step: press \"Build families\" above and choose a target-decoy (.T-R) " +
                    "database.",
                    "Generate the families first", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var score = owner.fcf.CalculateGroupingScore();

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

            owner.ShowBuildMessage(
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

        public async void ButtonDivideAndConquer_Click(object sender, RoutedEventArgs e) => await AppOperationHost.OnUi(owner, () => ButtonDivideAndConquer_ClickAsync(sender, e));
        private async Task ButtonDivideAndConquer_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (!owner.RequireDatabase()) return;
            string inputFile = owner.ViewModel.DatabasePath;
            double acceptableScore = (double)(owner.DecimalUpDownMaxFDR.Value ?? 0m);
            bool exhaustive = owner.CheckBoxExhaustiveClustering.IsChecked == true;

            // One warning for the whole search, before any of its many USEARCH runs. Cancel aborts cleanly.
            if (!owner.ConfirmLargeExhaustive(inputFile, exhaustive)) return;

            if (!owner.BeginRun("searching for the lowest usable identity on " + Path.GetFileName(inputFile) + ".")) return;

            try
            {
                // Runs USEARCH several times; keep it off the UI thread.
                DivideAndConquerResult outcome = await Task.Run(() =>
                    FamilyClusterFile.DivideAndConquerDetailed(inputFile, acceptableScore, exhaustive: exhaustive, operation: owner.Operation));
                owner.Operation!.ThrowIfCancellationRequested();
                double optimumIdentity = outcome.Identity;
                string stopReason = outcome.StopReason();

                owner.EndRun();

                string caveat = acceptableScore > 0
                    ? "Warning: with an allowance above 0 this lands inside the alignment twilight zone, " +
                      "where the score rises only because alignments have become noise. Families fused " +
                      "there are not biologically meaningful, and fusions between two unrelated real " +
                      "families are not counted by this score at all. The answer is also only approximate " +
                      "above 0, because the score is a ratio that can fall again as families merge."
                    : "This is the lowest identity showing no target/decoy mixing at all: the floor below " +
                      "which alignments stop being better than noise. It is a lower bound, not a " +
                      "recommendation. Pick a higher identity if the biology of your proteins calls for it.";

                owner.ShowBuildMessage(
                    "The search settled on " + optimumIdentity.ToString(CultureInfo.CurrentCulture) + "% identity, " +
                    "the lowest that keeps the mixing score at or below " +
                    acceptableScore.ToString("P0", CultureInfo.CurrentCulture) + ".\n\n" +
                    (stopReason.Length > 0
                        ? stopReason + "\n\nIf that is a single short peptide, the floor reflects that peptide " +
                          "rather than noise across the database; \"Scan identity range\" shows the whole curve.\n\n"
                        : string.Empty) +
                    caveat + "\n\n" +
                    "Next step: nothing was saved. Type that identity into the Identity box on the left and " +
                    "press \"Build families\" to actually build the Family Cluster File, or use \"Scan " +
                    "identity range\" first to see the whole curve.",
                    "Divide and Conquer finished", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException) when (owner.Operation?.IsCancellationRequested == true)
            {
                owner.EndRun();
                owner.ShowBuildMessage("Cancelled. The previous files and results were kept.", "Cancelled");
            }
            catch (Exception ex)
            {
                owner.EndRun();
                owner.ShowBuildMessage(
                    "The search could not be completed, and nothing was saved.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: this search needs a database containing reversed decoy sequences, whose " +
                    "identifiers start with \"" + SequenceLabels.DecoyPrefix + "\". On a database without them, choose the identity " +
                    "from \"Scan identity range\" and from the biology instead.",
                    "Could not finish the search", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async void ButtonScanIdentities_Click(object sender, RoutedEventArgs e) => await AppOperationHost.OnUi(owner, () => ButtonScanIdentities_ClickAsync(sender, e));
        private async Task ButtonScanIdentities_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (!owner.RequireDatabase()) return;
            string inputFile = owner.ViewModel.DatabasePath;
            bool exhaustive = owner.CheckBoxExhaustiveClustering.IsChecked == true;

            // One warning for the whole sweep, before any of its ~18 USEARCH runs. Cancel aborts cleanly.
            if (!owner.ConfirmLargeExhaustive(inputFile, exhaustive)) return;

            if (!owner.BeginRun("clustering " + Path.GetFileName(inputFile) + " at every identity from 95% down to 10%.")) return;

            try
            {
                List<IdentityScanRow> rows = await Task.Run(() => FamilyClusterFile.ScanIdentities(inputFile, exhaustive: exhaustive, operation: owner.Operation));
                owner.Operation!.ThrowIfCancellationRequested();

                owner.EndRun();

                // The curve first, the table under it: the threshold is chosen by looking at where the
                // family count stops falling, which no column of numbers makes visible.
                // The generated file's own identity, so the sweep shows where this panel already sits.
                // Passing nothing here left the "this file" marker permanently undrawn.
                IdentityScanWindow.Show("Identity scan · " + Path.GetFileName(inputFile),
                                        rows, BuildReportFormatter.Format(rows), Window.GetWindow(owner),
                                        owner.fcf?.ClusteringIdentity);
            }
            catch (OperationCanceledException) when (owner.Operation?.IsCancellationRequested == true)
            {
                owner.EndRun();
                owner.ShowBuildMessage("Cancelled. The previous files and results were kept.", "Cancelled");
            }
            catch (Exception ex)
            {
                owner.EndRun();
                owner.ShowBuildMessage(
                    "The scan could not be completed, and nothing was saved.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that the database opens and contains no duplicated identifiers, and " +
                    "that antivirus software is not blocking the bundled USEARCH program.",
                    "Could not scan the identity range", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async void ButtonRemoveRedundancy_Click(object sender, RoutedEventArgs e) => await AppOperationHost.OnUi(owner, () => ButtonRemoveRedundancy_ClickAsync(sender, e));
        private async Task ButtonRemoveRedundancy_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (owner.busy)
            {
                owner.ShowBuildMessage(
                    "Nothing was started: USEARCH is already running. Two runs over the same files would interfere with each other. Wait for it to finish; the buttons come back by themselves.",
                    "Already running", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!owner.RequireDatabase()) return;
            string inputFile = owner.ViewModel.DatabasePath;
            double identity = (double)(owner.IntegerUpDownRedundancyIdentity.Value ?? 100);
            string outputFile = Path.Combine(
                Path.GetDirectoryName(inputFile) ?? Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(inputFile) + "_NO_REDUNDANCY.fasta");

            if (!owner.BeginRun("removing redundant sequences from " + Path.GetFileName(inputFile))) return;
            try
            {
                var result = await Task.Run(() => RedundancyTools.RemoveRedundancy(new FileInfo(inputFile), identity, outputFile, owner.Operation));

                owner.EndRun();

                int removed = result.before - result.after;
                owner.ShowBuildMessage(
                    result.before.ToString("N0", CultureInfo.CurrentCulture) + " sequences went in and " +
                    result.after.ToString("N0", CultureInfo.CurrentCulture) + " came out - " +
                    removed.ToString("N0", CultureInfo.CurrentCulture) + " redundant sequence(s) removed at " +
                    identity.ToString(CultureInfo.CurrentCulture) + "% identity.\n\n" +
                    "The original sequence order and the headers were preserved. The result was written to\n" +
                    outputFile + "\n\n" +
                    "Next step: cluster this non-redundant file with \"Build families\" above, so near-duplicate " +
                    "entries do not split a family.",
                    "Redundancy removed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException) when (owner.Operation?.IsCancellationRequested == true)
            {
                owner.EndRun();
                owner.ShowBuildMessage("Cancelled. The previous files and results were kept.", "Cancelled");
            }
            catch (Exception ex)
            {
                owner.EndRun();
                owner.ShowBuildMessage(
                    "The redundant sequences could not be removed, and nothing was written.\n\n" +
                    "Reason given by the program: " + ex.Message,
                    "Could not remove redundancy", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
