using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using Microsoft.Win32;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using System.Windows.Data;

namespace FamilyQuant;

public partial class DeNovoControl
{
        private bool CanShowDialog(string refusal, string nextStep)
        {
            if (PresentationSource.FromVisual(this) != null) return true;

            SetStatus(refusal + ", because this screen is not on a display and a file dialog there is " +
                      "a window nothing can close. Next step: " + nextStep);
            return false;
        }

        private void ButtonBrowsePeaksDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No folder was chosen",
                               "type the folder into the box beside the button instead.")) return;

            var ofd = new OpenFolderDialog
            {
                Title = "Choose the folder holding the selected de novo export"
            };

            if (!ShellDialogs.ShowFolder(this, "denovo.import", ofd)) return;

            TextBoxPeaksDirectory.Text = ofd.FolderName;

            // Count straight away rather than at import: the commonest mistake is picking a folder one
            // level too high or too low, and a count of zero says so before anything is read.
            DeNovoSource source = ActiveDeNovoSource;
            int files = DeNovoScreenOperations.CountExports(ofd.FolderName, source);
            LabelFilesFound.Content = "Files found: " + files.ToString("N0", CultureInfo.CurrentCulture);

            SetStatus(files == 0
                ? "That folder holds no " + source.DisplayName + " export: nothing under it is named " +
                  source.FileGlob + ". " +
                  "Next step: choose the export root holding the per-run sub-folders and check that the " +
                  "selected source was exported."
                : files.ToString("N0", CultureInfo.CurrentCulture) + " de novo export file(s) found under " +
                  "that folder. Next step: check the three filters and press \"Load and filter\".");
        }

        private void ButtonBrowseSeproPackage_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No SEPro package was chosen",
                               "type the path of the .sepr2 into the box beside the button instead.")) return;

            var ofd = new OpenFileDialog
            {
                Filter = "SEPro 2.0 (*.sepr2)|*.sepr2",
                Title = "Choose a TRUSTED SEPro package (legacy unsafe deserialization)"
            };

            if (!ShellDialogs.ShowFile(this, "denovo.sepro", ofd)) return;

            TextBoxSeproPackage.Text = ofd.FileName;

            SetStatus("Package chosen: " + Path.GetFileName(ofd.FileName) +
                      ". Next step: press Reconcile. Reading a real package takes a while, and the " +
                      "screen is disabled while it is read.");
        }



        private void ButtonBrowseHomologyFcf_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No Family Cluster File was chosen",
                               "type the path of the .fcf into the box beside the button instead.")) return;

            var ofd = new OpenFileDialog
            {
                Filter = "FamilyQuant project or Family Cluster File (*.fqproj;*.fcf)|*.fqproj;*.fcf",
                Title = "Choose the project or family file to search against and map onto"
            };

            if (!ShellDialogs.ShowFile(this, "denovo.curation", ofd)) return;

            TextBoxHomologyFcf.Text = ofd.FileName;

            SetStatus("Family Cluster File chosen: " + Path.GetFileName(ofd.FileName) +
                      ". Next step: check the identity threshold and the two residue options, then press " +
                      "Search. The file supplies both the sequences that are searched and the family " +
                      "each hit is attributed to.");
        }





        private void ButtonExportSummary_Click(object sender, RoutedEventArgs e)
        {
            if (!HaveReconciliationToExport()) return;

            Export("FamilyQuant_DeNovo_Summary.csv", "Save the per-run verdict summary as CSV",
                   ExportSummaryCsv,
                   "one line per run and verdict, with how many rows fell into each");
        }

        private void ButtonExportReviewQueue_Click(object sender, RoutedEventArgs e)
        {
            if (!HaveReconciliationToExport()) return;

            Export("FamilyQuant_DeNovo_ReviewQueue.csv", "Save the review queue as CSV",
                   ExportReviewQueueCsv,
                   "one line per row of the table, both sequences side by side");
        }

        private void ButtonExportOverlapStudy_Click(object sender, RoutedEventArgs e)
        {
            if (!HaveReconciliationToExport()) return;

            Export("FamilyQuant_DeNovo_OverlapStudy.csv", "Save the de novo/search-engine overlap study as CSV",
                   ExportOverlapStudyCsv,
                   "the nested scan, precursor, exact-sequence and divergence counts with their denominators");
        }

        private bool HaveReconciliationToExport()
        {
            if (LastReconciliation != null) return true;

            SetStatus("Nothing was exported: no reconciliation has been run, so there are no " +
                      "verdicts to write. Next step: press Reconcile in Step 2 first.");
            return false;
        }

        private async void Export(string suggestedName, string title, Action<string> write, string what)
        {
            if (!CanShowDialog("Nothing was exported", "nothing. The same rows are on screen.")) return;

            var sfd = new SaveFileDialog
            {
                Filter = FileDialogFilters.Csv,
                FileName = suggestedName,
                Title = title
            };

            if (!ShellDialogs.ShowFile(this, "denovo.export." + suggestedName, sfd)) return;
            using var operation = AppOperationHost.For(this).Begin("Export de novo evidence");

            try
            {
                await Task.Run(() => AppOperationFiles.WriteFile(sfd.FileName, write, operation.Context));

                SetStatus("Written to " + sfd.FileName + ": " + what + ". Numbers use a dot for the " +
                          "decimal separator whatever your machine's language is, so any program can " +
                          "read them. Nothing on screen changed. Next step: nothing.");
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                SetStatus("Cancelled. The previous export file was kept.");
            }
            catch (Exception ex)
            {
                SetStatus("The file could not be written, so nothing was exported. Reason given by the " +
                          "program: " + ex.Message + " Next step: check that it is not already open in " +
                          "Excel and that you can write to that folder, then export again.");
            }
        }







        public void SuggestFamilyClusterFile(string? path)
        {
            if (TextBoxHomologyFcf == null) return;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (TextBoxHomologyFcf.Text.Trim().Length > 0) return;

            TextBoxHomologyFcf.Text = path;
        }

        public string HelpText() => ViewModel.HelpText();

        public void ShowHelp()
        {
            // A harness has no display to put a window on, and a window opened there is one nothing
            // ever closes. The same guard the detached chart uses.
            if (PresentationSource.FromVisual(this) == null)
            {
                SetStatus("The help window was not opened, because this screen is not on a display. " +
                          "Next step: nothing.");
                return;
            }

            // Not wrapped: the verdict list and the provenance list are aligned columns, and reflowing
            // them at the window's width would take that alignment apart.
            TextReportWindow.Show("FamilyQuant · " + WorkflowNames.DeNovoReview, HelpText(),
                                  Window.GetWindow(this));
        }

        private void ButtonDeNovoHelp_Click(object sender, RoutedEventArgs e)
        {
            ShowHelp();
        }
}
