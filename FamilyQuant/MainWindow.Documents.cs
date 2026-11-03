using Microsoft.Win32;
using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class MainWindow
{
    public string? ProjectOpenLogPath { get; private set; }
    private sealed class ShellDocumentCommands
    {
        private readonly MainWindow owner;
        public ShellDocumentCommands(MainWindow owner) => this.owner = owner;

        public async void MenuItemCluster_Click(object sender, RoutedEventArgs e)
        {
            string? path = owner.ChooseOpenProjectPath();
            if (path != null) await owner.OpenProjectSafelyAsync(path);
        }

        public bool OpenProjectPath(string path)
        {
            try
            {
                // Read the container or the plain file BEFORE touching the layout, so a corrupt project
                // leaves the previous view in place instead of an empty window. A .fqproj restores the
                // stored quant too; an old .fcf loads exactly as it always did, with the quant empty.
                ShellProjectContent content = ShellProjectOperations.Read(path);
                return AttachProject(path, content);
            }
            catch (Exception ex) { return ReportOpenFailure(ex); }
        }

        public Task<bool> OpenProjectPathAsync(string path) => AppOperationHost.OnUi(owner, () => OpenProjectCoreAsync(path));
        private async Task<bool> OpenProjectCoreAsync(string path)
        {
            if (owner.Operations.IsBusy) return false;
            DateTimeOffset started = DateTimeOffset.UtcNow;
            owner.ProjectOpenLogPath = AppOperationHost.CreateLogPath("open");
            using var operation = owner.Operations.Begin("Open project", started, owner.ProjectOpenLogPath);
            try
            {
                ShellProjectContent content = await Task.Run(() => ShellProjectOperations.Read(path, operation.Context));
                operation.Context.ThrowIfCancellationRequested();
                operation.Dispose();
                return AttachProject(path, content, started, owner.ProjectOpenLogPath);
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                owner.StatusModel.ShowMessage("Cancelled. The previous project was kept.");
                return false;
            }
            catch (Exception ex) { owner.Operations.LogFailure(ex); return ReportOpenFailure(ex); }
        }

        private bool AttachProject(string path, ShellProjectContent content, DateTimeOffset? started = null, string? operationLogPath = null)
        {
                var loaded = new ClusterGenerationControl(content.Curation) { Name = "MyClusterGenerationControl" };
                List<QuantRow> quant = content.Rows;
                QuantManifest? manifest = content.Manifest;

                owner.GridApp.Children.Clear();
                owner.cgc = loaded;
                owner.usearch = null;
                owner.dnc = null;
                owner.analytics = null;
                owner.home = null;
                owner.lastFcfPath = Path.GetFullPath(path);
                owner.loadedQuant = quant;
                owner.loadedManifest = manifest;
                owner.loadedCuration = loaded.fcf;
                owner.openedDatabaseFileName = loaded.fcf.ReportInputProvenance?.DatabaseFileName;
                owner.openedDatabaseFileSha256 = loaded.fcf.ReportInputProvenance?.DatabaseFileSha256;
                if (!ProjectStore.IsProjectPath(path))
                {
                    owner.openedDatabaseFileName = Path.GetFileName(path);
                    owner.openedDatabaseFileSha256 = content.DatabaseFileSha256;
                }
                owner.GridApp.Children.Add(owner.cgc);

                // A .fqproj carries its own quantitation: the curation table shows it at once.
                _ = owner.cgc.SetFullXicQuantAsync(owner.loadedQuant, owner.loadedManifest?.PerRunTic, started, operationLogPath);

                owner.RefreshProjectCommandAvailability(resetSpectrum: true);
                owner.lastSpectrumComparisonCsvPath = null;
                owner.DocumentState.Open(loaded.fcf, ProjectStore.IsProjectPath(path) ? System.IO.Path.GetFullPath(path) : null);
                owner.TrackCuration(loaded);

                owner.UpdateGuide();
                owner.RememberProject(path);
                return true;
        }

        private bool ReportOpenFailure(Exception ex)
        {
                owner.ReportShellMessage(
                    "The project could not be opened, and what was on screen has been left alone.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that this really is a .fqproj or a .fcf written by FamilyQuant and " +
                    "that it is not open in another program. If it was interrupted while being saved it " +
                    "will have to be generated again from the database with File > New from database.",
                    "Could not open the file", MessageBoxImage.Error);
                return false;
        }

        public async void MenuItemSaveFCF_Click(object sender, RoutedEventArgs e)
        {
            if (owner.Operations.IsBusy) return;
            // Use the control we created in MenuItemCluster_Click. Reading GridApp.Children[0] and casting
            // it threw an InvalidCastException whenever the USearch panel was the one on screen.
            if (owner.cgc == null || owner.cgc.fcf == null)
            {
                ShellMessages.Report(owner,
                    "Nothing was saved: there are no families on screen to save.\n\n" +
                    "Next step: open a Family Cluster File with File > Open, or build " +
                    "one with File > New from database.",
                    "Nothing to save", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "FamilyClusterFile (*.fcf)|*.fcf",
                Title = "Save the current families as a Family Cluster File"
            };

            if (!ShellDialogs.ShowFile(owner, "families.export", sfd)) return;
            using var operation = owner.Operations.Begin("Export families");

            try
            {
                var document = owner.cgc.fcf;
                await Task.Run(() => document.Save(sfd.FileName, operation.Context));
                operation.Dispose();

                ShellMessages.Report(owner,
                    "The families were saved to\n" + sfd.FileName + "\n\n" +
                    "The file holds the grouping, your User IDs and your annotations. It does not hold the " +
                    "quantitation: reopening it restores the families, and the percentages come back when " +
                    "you load a PatternLab project again.\n\n" +
                    "Next step: nothing. Carry on working, or open this file later to return to exactly " +
                    "this grouping.",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                owner.StatusModel.ShowMessage("Cancelled. The previous family file was kept.");
            }
            catch (Exception ex)
            {
                ShellMessages.Report(owner,
                    "The file could not be written, so nothing was saved.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that the file is not open in another program and that you can write " +
                    "to that folder, then save again.",
                    "Could not save", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async void MenuItemSaveProject_Click(object sender, RoutedEventArgs e)
            => await owner.SaveProjectAsync(saveAs: false);

        public Task SaveProjectAsync(bool saveAs) => AppOperationHost.OnUi(owner, () => SaveProjectCoreAsync(saveAs));
        private async Task SaveProjectCoreAsync(bool saveAs)
        {
            if (owner.saveInProgress || owner.Operations.IsBusy) return;

            if (owner.cgc == null || owner.cgc.fcf == null)
            {
                ShellMessages.Report(owner,
                    "Nothing was saved: there is no project on screen to save.\n\n" +
                    "Next step: open a project or a Family Cluster File with File > Open, or build one " +
                    "with File > New from database.",
                    "Nothing to save", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? savePath = owner.ChooseSaveProjectPath(saveAs);
            if (savePath == null) return;
            using var operation = owner.Operations.Begin("Save project");

            // ONE project bundle, captured now and written as captured: the refresh below can wait for a
            // background attach, and a project opened or recomputed meanwhile must never be mixed with
            // this one in a file (its curation and quant with this manifest).
            ClusterGenerationControl project = owner.cgc;
            FamilyClusterFile curation = owner.cgc.fcf;
            List<QuantRow> quant = owner.loadedQuant;
            QuantManifest? heldManifest = owner.loadedManifest;
            owner.saveInProgress = true;
            owner.RefreshNavigationState();

            try
            {
                // The manifest travels with the quant. A project that has never been computed still gets
                // a manifest, so the container always describes itself; its updated timestamp is refreshed
                // on every save. A project with no computed quant carries the default provenance and the
                // go-forward isotope basis, both describing what a later Compute would fill.
                QuantManifest manifest = heldManifest ?? ShellProjectOperations.CreateManifest(MainWindow.AppVersion());
                // Timestamp the prepared manifest only after cancellation-sensitive work completes.

                var (completed, familyNote) = await owner.RefreshSpectrumFamiliesAsync();
                if (!completed || !ReferenceEquals(project, owner.cgc) || !ReferenceEquals(curation, owner.cgc?.fcf)
                    || !ReferenceEquals(quant, owner.loadedQuant) || !ReferenceEquals(heldManifest, owner.loadedManifest))
                {
                    ShellMessages.Report(owner,
                        "Nothing was saved: the open project changed (another one was opened, or its quantitation " +
                        "was recomputed) while the save was waiting for the family attribution to finish.\n\n" +
                        "Next step: save again.",
                        "Nothing saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                operation.Context.ThrowIfCancellationRequested();
                DateTime previousUpdated = manifest.UpdatedUtc;
                var previousInputs = curation.ReportInputProvenance;
                string checkText;
                try
                {
                    if (!string.IsNullOrEmpty(owner.openedDatabaseFileName) && !string.IsNullOrEmpty(owner.openedDatabaseFileSha256))
                        curation.ReportInputProvenance = (previousInputs ?? new Report.ReportInputProvenance()) with
                        { DatabaseFileName = owner.openedDatabaseFileName, DatabaseFileSha256 = owner.openedDatabaseFileSha256 };
                    manifest.UpdatedUtc = DateTime.UtcNow;
                    checkText = await Task.Run(() => ShellProjectOperations.WriteVerifiedProject(savePath, curation, quant, manifest, operation: operation.Context));
                }
                catch { manifest.UpdatedUtc = previousUpdated; curation.ReportInputProvenance = previousInputs; throw; }
                owner.loadedManifest = manifest;
                owner.lastFcfPath = Path.GetFullPath(savePath);
                owner.DocumentState.Saved(curation, owner.lastFcfPath, checkText);
                owner.UpdateGuide();
                owner.RememberProject(savePath);

                owner.StatusModel.ShowMessage(
                    "Saved. " + checkText + "\nThe project was saved to\n" + savePath + "\n\n" +
                    "The container holds the grouping, your User IDs and annotations, and " +
                    quant.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " stored quant row(s). Reopening it restores both.\n\n" +
                    (familyNote != null ? familyNote + "\n\n" : string.Empty) +
                    "Next step: nothing. Carry on working, or open this file later to return to exactly " +
                    "this project.\n\n" + checkText);
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                owner.StatusModel.ShowMessage("Cancelled. The previous project file was kept.");
            }
            catch (Exception ex)
            {
                owner.ReportShellMessage(
                    "The project could not be written, so nothing was saved.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that the file is not open in another program and that you can write " +
                    "to that folder, then save again.",
                    "Could not save", MessageBoxImage.Error);
            }
            finally
            {
                owner.saveInProgress = false;
                owner.RefreshNavigationState();
            }
        }

        public async Task<(bool Completed, string? Note)> RefreshSpectrumFamiliesAsync()
        {
            ClusterGenerationControl? control = owner.cgc;
            QuantManifest? manifest = owner.loadedManifest;
            List<QuantRow> quant = owner.loadedQuant;
            List<SpectrumComparisonRow>? rows = manifest?.SpectrumComparisonRows;
            if (control?.fcf == null || manifest == null || rows == null || rows.Count == 0 || quant.Count == 0)
                return (true, null);

            // The background attach is awaited, so its homology hits are reused instead of a second search
            // starting on the interface thread. A replaced attach releases its waiters at once, so the wait
            // is repeated until the readiness it waited on is still the current one.
            while (true)
            {
                Task ready = control.FullXicReady;
                await ready;
                if (ReferenceEquals(ready, control.FullXicReady)) break;
            }

            // Everything it is about to touch must still be the open project's.
            if (!ReferenceEquals(control, owner.cgc) || !ReferenceEquals(manifest, owner.loadedManifest)
                || !ReferenceEquals(quant, owner.loadedQuant)) return (false, null);

            string? note = ShellProjectOperations.RefreshSpectrumFamilies(rows, manifest, control.fcf, quant,
                control.CurrentFullXicAttribution, control.FullXicProteinFingerprint);
            return (true, note);
        }

        public async void MenuItemSpectrumComparison_Click(object sender, RoutedEventArgs e)
        {
            if (owner.spectrumViewInProgress || owner.Operations.IsBusy) return;
            owner.spectrumViewInProgress = true;
            using var operation = owner.Operations.Begin("Prepare spectrum comparison");
            try
            {
                var (completed, _) = await owner.RefreshSpectrumFamiliesAsync();
                if (!completed) return;
                operation.Context.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                owner.StatusModel.ShowMessage("Cancelled. The previous view was kept.");
                return;
            }
            finally
            {
                owner.spectrumViewInProgress = false;
            }

            bool havePersistedRows = owner.loadedManifest?.SpectrumComparisonRows != null
                && owner.loadedManifest.SpectrumComparisonRows.Count > 0;
            if (!havePersistedRows && owner.loadedQuant.Count == 0)
            {
                ShellMessages.Report(owner,
                    "There is no spectrum comparison table to show yet.\n\n" +
                    "Next step: load a .fqproj that contains full quantitation, or run Analysis > Compute " +
                    "full XIC first.",
                    "No spectrum table yet", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IReadOnlyList<SpectrumComparisonRow> rows =
                    havePersistedRows
                        ? owner.loadedManifest!.SpectrumComparisonRows
                        : await Task.Run(() => SpectrumComparisonTable.Build(owner.loadedQuant));
                operation.Context.ThrowIfCancellationRequested();
                operation.Dispose();
                SpectrumComparisonWindow.Show(rows, owner, owner.lastSpectrumComparisonCsvPath,
                    owner.loadedManifest?.DeNovoAcceptedScoreScatter);
            }
            catch (Exception ex)
            {
                ShellMessages.Report(owner,
                    "The spectrum comparison table could not be built, and the project was not changed.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: recompute the full XIC with the PEAKS de-novo-only + DeepNovo union if " +
                    "this is an older project that does not carry source-resolved evidence.",
                    "Could not build spectrum table", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async void MenuItemReanotateSEPro_Click(object sender, RoutedEventArgs e)
        {
            if (owner.Operations.IsBusy) return;
            if (owner.cgc == null || owner.cgc.fcf == null)
            {
                ShellMessages.Report(owner,
                    "Nothing was reannotated: there are no families on screen to write into a SEPro " +
                    "package.\n\n" +
                    "Next step: open a Family Cluster File with File > Open first.",
                    "Load the families first", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "SEPro 2.0 (*.sepr2)|*.sepr2",
                Title = "Choose a TRUSTED SEPro result package (legacy unsafe deserialization)"
            };

            if (!ShellDialogs.ShowFile(owner, "sepro.reannotate", ofd)) return;
            using var operation = owner.Operations.Begin("Reannotate SEPro");

            try
            {
                var spckg = await Task.Run(() => SeproPckg2.ResultPackage.Load(ofd.FileName));
                operation.Context.ThrowIfCancellationRequested();
                Console.WriteLine("SEPro Package loaded");

                ShellReannotationResult reannotation = ShellProjectOperations.Reannotate(owner.cgc.fcf, spckg);
                IReadOnlyList<string> missing = reannotation.Missing;

                if (missing.Count > 0)
                {
                    ShellMessages.Report(owner,
                        "Nothing was written: " + missing.Count.ToString("N0", CultureInfo.CurrentCulture) +
                        " protein(s) in the SEPro package are not in the database these families came " +
                        "from, so they have no family number to receive.\n\n" +
                        "This normally means the search and the clustering used different FASTA files, or " +
                        "different versions of the same one.\n\n" +
                        "Next step: recluster the exact database the SEPro search used, with File > New from database, " +
                        "and try again.\n\n" +
                        "Examples of the missing identifiers: " +
                        string.Join(", ", missing.Take(10)) + (missing.Count > 10 ? ", ..." : ""),
                        "The package and the database do not match", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var newFileName = ofd.FileName.Remove(ofd.FileName.Length - 6, 6) + "_regrouped.sepr2";
                await Task.Run(() => AppOperationFiles.WriteFile(newFileName, spckg.Save, operation.Context));
                operation.Dispose();

                ShellMessages.Report(owner,
                    spckg.MyProteins.MyProteinList.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " protein(s) were given their family number and written to\n" + newFileName + "\n\n" +
                    "The number written is the User ID column, so any family you merged by hand is what " +
                    "went in. The file you chose was not modified.\n\n" +
                    "Next step: use the _regrouped package in the rest of the PatternLab pipeline.",
                    "SEPro package reannotated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                owner.StatusModel.ShowMessage("Cancelled. The original package and previous copy were kept.");
            }
            catch (Exception ex)
            {
                ShellMessages.Report(owner,
                    "The SEPro package could not be reannotated, and no file was written.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that the .sepr2 opens in SEPro and that you can write to its folder, " +
                    "then try again.",
                    "Could not reannotate", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
