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

            if (ofd.ShowDialog() != true) return;

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

            if (ofd.ShowDialog() != true) return;

            TextBoxSeproPackage.Text = ofd.FileName;

            SetStatus("Package chosen: " + Path.GetFileName(ofd.FileName) +
                      ". Next step: press Reconcile. Reading a real package takes a while, and the " +
                      "screen is disabled while it is read.");
        }

        private void ButtonBrowseRawDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No folder was chosen",
                               "type the folder holding the .raw acquisitions into the box beside the " +
                               "button instead.")) return;

            var ofd = new OpenFolderDialog
            {
                Title = "Choose the folder holding the .raw acquisitions of these runs"
            };

            if (ofd.ShowDialog() != true) return;

            TextBoxRawDirectory.Text = ofd.FolderName;   // the TextChanged handler redoes the preview

            int found = RawMatches.Count(m => m.Found);

            SetStatus(found == RawMatches.Count && RawMatches.Count > 0
                ? "All " + found.ToString("N0", CultureInfo.CurrentCulture) + " kept run(s) found their " +
                  "acquisition in that folder, by file name. Next step: press Extract. Each RAW is opened " +
                  "once and the runs are done strictly one after another."
                : "Of " + RawMatches.Count.ToString("N0", CultureInfo.CurrentCulture) + " kept run(s), " +
                  found.ToString("N0", CultureInfo.CurrentCulture) + " found an acquisition in that " +
                  "folder. Missing: " + ViewModel.MissingRunsSentence() + " Next step: choose the folder those " +
                  "files really are in. Extract stays disabled until every kept run has one, because a " +
                  "partial extraction would draw a de novo slice with a whole run silently missing.");
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

            if (ofd.ShowDialog() != true) return;

            TextBoxHomologyFcf.Text = ofd.FileName;

            SetStatus("Family Cluster File chosen: " + Path.GetFileName(ofd.FileName) +
                      ". Next step: check the identity threshold and the two residue options, then press " +
                      "Search. The file supplies both the sequences that are searched and the family " +
                      "each hit is attributed to.");
        }

        private void ButtonBrowsePartitionPlp_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No PatternLab project was chosen",
                               "type the path of the .plp into the box beside the button instead.")) return;

            var ofd = new OpenFileDialog
            {
                Filter = "PatternLab project (*.plp)|*.plp",
                Title = "Choose the PatternLab project holding the quantitation of these runs"
            };

            if (ofd.ShowDialog() != true) return;

            TextBoxPartitionPlp.Text = ofd.FileName;

            SetStatus("PatternLab project chosen: " + Path.GetFileName(ofd.FileName) +
                      ". Next step: press \"Build partition\". If that project merged its technical " +
                      "replicates it cannot answer per run, and it will say so in its own words; give " +
                      "the .xic2 of the same extraction as well, so there is a source that can.");
        }

        private void ButtonBrowsePartitionXic2_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No stored quants file was chosen",
                               "type the path of the .xic2 into the box beside the button instead.")) return;

            var ofd = new OpenFileDialog
            {
                Filter = "Stored quants (*.xic2)|*.xic2",
                Title = "Choose the stored quants of the same extraction"
            };

            if (ofd.ShowDialog() != true) return;

            TextBoxPartitionXic2.Text = ofd.FileName;

            SetStatus("Stored quants chosen: " + Path.GetFileName(ofd.FileName) +
                      ". It is used only if the .plp cannot resolve one column per run. Next step: press " +
                      "\"Build partition\".");
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

        private void Export(string suggestedName, string title, Action<string> write, string what)
        {
            if (!CanShowDialog("Nothing was exported", "nothing. The same rows are on screen.")) return;

            var sfd = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = suggestedName,
                Title = title
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                write(sfd.FileName);

                SetStatus("Written to " + sfd.FileName + ": " + what + ". Numbers use a dot for the " +
                          "decimal separator whatever your machine's language is, so any program can " +
                          "read them. Nothing on screen changed. Next step: nothing.");
            }
            catch (Exception ex)
            {
                SetStatus("The file could not be written, so nothing was exported. Reason given by the " +
                          "program: " + ex.Message + " Next step: check that it is not already open in " +
                          "Excel and that you can write to that folder, then export again.");
            }
        }

        private void ButtonExportHomologyCsv_Click(object sender, RoutedEventArgs e)
        {
            if (LastHomology == null)
            {
                SetStatus("Nothing was exported: no homology search has been run, so there are no " +
                          "assignments to write. Next step: press Search in Step 4 first.");
                return;
            }

            Export("FamilyQuant_DeNovo_Homology.csv", "Save the per-peptide family assignments as CSV",
                   ExportHomologyCsv,
                   "one line per searched peptide, with its family ids, its protein ids and whether it " +
                   "is ambiguous");
        }

        private void ButtonSavePartitionCsv_Click(object sender, RoutedEventArgs e)
        {
            if (LastPartition == null || !LastPartition.IsAvailable)
            {
                SetStatus("Nothing was saved: no partition has been built, so there are no numbers to " +
                          "write. Next step: press \"Build partition\" in Step 5 first.");
                return;
            }

            Export("three_way_partition.csv", "Save the three-way partition as CSV",
                   SavePartitionCsv,
                   "the three components with the provenance of each, the three shares, the " +
                   "negative-remainder flag and the de novo envelope diagnostic, aggregate line last");
        }

        private void ButtonOpenPartitionWindow_Click(object sender, RoutedEventArgs e)
        {
            if (LastPartition == null || !LastPartition.IsAvailable)
            {
                SetStatus("Nothing was opened: no partition has been built, so there would be nothing " +
                          "to show. Next step: press \"Build partition\" in Step 5 first.");
                return;
            }

            // A harness has no screen to put a window on, and opening one there would be a window
            // nothing ever closes. The same guard the rest of this program uses for a modal box.
            if (PresentationSource.FromVisual(this) == null)
            {
                SetStatus("The detached window was not opened, because this screen is not on a display. " +
                          "The chart in Step 5 shows the same rows. Next step: nothing.");
                return;
            }

            ThreeWayWindow.Show("Three-way partition of the ion current", LastPartition.Rows,
                                Window.GetWindow(this), LastPartition.BranchNote);

            SetStatus("The chart is now also in a window of its own, with its caption and its own save " +
                      "button. It is a second view of the same rows: closing it changes nothing here. " +
                      "Next step: nothing.");
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
            TextReportWindow.Show("FamilyQuant · De novo evidence", HelpText(),
                                  Window.GetWindow(this));
        }

        private void ButtonDeNovoHelp_Click(object sender, RoutedEventArgs e)
        {
            ShowHelp();
        }
}
