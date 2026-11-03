using FamilyQuant.Operations;
using FamilyQuant.Report;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    private bool reportExportInProgress;
    public Task<bool>? ReportExportTask { get; private set; }
    public static Func<string?>? SelectReportPathOverride { get; set; }
    public static Action<ReportExportWindow>? ReportExportPresenterOverride { get; set; }
    public static Func<IReadOnlyList<QuantRow>, QuantManifest, FamilyClusterFile, string, ReportDocument>? ReportDocumentFactoryOverride { get; set; }
    public static Action<string, ReportDocument, ReportSelection, OperationContext>? ReportFileStagerOverride { get; set; }

    private async void MenuItemExportReport_Click(object sender, RoutedEventArgs e)
    {
        ReportExportTask = ExportReportAsync();
        await ReportExportTask;
    }

    public Task<bool> ExportReportAsync() => AppOperationHost.OnUi(this, ExportReportCoreAsync);

    private async Task<bool> ExportReportCoreAsync()
    {
        if (reportExportInProgress || Operations.IsBusy || saveInProgress) return false;
        if (cgc == null || loadedCuration == null || loadedManifest == null || loadedQuant.Count == 0)
        {
            ShellMessages.Report(this, "A report needs stored Results. Open a project, use Analysis > Compute full XIC, then choose File > Export report.", "Export report");
            return false;
        }
        reportExportInProgress = true;
        var project = cgc;
        var curation = loadedCuration;
        var rows = loadedQuant;
        var manifest = loadedManifest;
        bool SameProject() => ReferenceEquals(project, cgc) && ReferenceEquals(curation, loadedCuration)
            && ReferenceEquals(rows, loadedQuant) && ReferenceEquals(manifest, loadedManifest);
        void RequireSameProject()
        {
            if (!SameProject()) throw new InvalidOperationException("The open project or its Results changed. Choose Export report again.");
        }
        string scratchParent = Path.Combine(Path.GetTempPath(), "FamilyQuant.ReportExports");
        string scratch = Path.Combine(scratchParent, Guid.NewGuid().ToString("N"));
        string? stagedFile = null;
        try
        {
            FamilyClusterFile snapshot;
            QuantRow[] capturedRows;
            ReportDocument document;
            using (var operation = Operations.Begin("Prepare report"))
            {
                operation.Context.Report("report-attribution", "Waiting for family attribution");
                await project.FullXicReady.WaitAsync(operation.Context.CancellationToken);
                var ready = await RefreshSpectrumFamiliesAsync();
                operation.Context.ThrowIfCancellationRequested();
                if (!ready.Completed) throw new InvalidOperationException("The project changed while its family attribution was being prepared.");
                RequireSameProject();
                operation.Context.Report("report-snapshot", "Capturing the project and stored Results");
                var captured = await Task.Run(() => (File: FamilyClusterFile.FromJsonText(curation.ToJsonText()), Rows: rows.ToArray()));
                snapshot = captured.File; capturedRows = captured.Rows;
                RequireSameProject();
                document = await PrepareReportDocumentAsync(capturedRows, manifest, snapshot, Path.Combine(scratch, "preview"), operation.Context);
                operation.Context.ThrowIfCancellationRequested();
                RequireSameProject();
            }

            ReportSelection? selected = null;
            double threshold = snapshot.ReportThreshold ?? FamilyReportThreshold.DefaultThreshold;
            var editor = new ReportExportWindow(new(document, snapshot.ReportSelection, threshold),
                (choice, share) => { selected = choice; threshold = share; }, "Export report");
            if (ReportExportPresenterOverride != null) ReportExportPresenterOverride(editor);
            else
            {
                if (!ShellDialogs.IsDisplayed(this))
                {
                    ShellMessages.Report(this, "Export report needs a displayed application window.", "Export report", showDialog: false);
                    return false;
                }
                editor.Owner = this;
                editor.ShowDialog();
            }
            if (!editor.Accepted || selected == null) return false;
            RequireSameProject();
            string? path = ChooseReportPath();
            if (path == null) return false;
            RequireSameProject();
            string fullPath = Path.GetFullPath(path);
            if (!string.IsNullOrWhiteSpace(lastFcfPath) && string.Equals(fullPath, Path.GetFullPath(lastFcfPath), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a report path distinct from the open project.");
            if (!fullPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use a .docx file name for the Word report.");
            double previousThreshold = snapshot.ReportThreshold ?? FamilyReportThreshold.DefaultThreshold;
            project.ViewModel.ApplyReportOptions(selected, threshold);
            snapshot.ReportSelection = project.fcf.ReportSelection;
            snapshot.ReportThreshold = project.fcf.ReportThreshold;

            using (var operation = Operations.Begin("Export report"))
            {
                if (threshold != previousThreshold)
                    document = await PrepareReportDocumentAsync(capturedRows, manifest, snapshot, Path.Combine(scratch, "selected"), operation.Context);
                RequireSameProject();
                operation.Context.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                stagedFile = Path.Combine(Path.GetDirectoryName(fullPath)!, ".fq-report-" + Guid.NewGuid().ToString("N") + ".docx");
                string stage = stagedFile;
                var stager = ReportFileStagerOverride;
                operation.Context.Report("report-write", "Writing the selected report sections and figures");
                await Task.Run(() =>
                {
                    if (stager != null) stager(stage, document, selected, operation.Context);
                    else WordReportWriter.Write(stage, document, selected);
                });
                operation.Context.ThrowIfCancellationRequested();
                RequireSameProject();
                operation.Context.Report("report-publish", "Publishing the completed Word report", 1, 1);
                operation.Context.Commit(() => File.Move(stage, fullPath, true));
                StatusModel.ShowMessage("Report exported to " + fullPath + ". Save the project to keep any changed report details or choices.");
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusModel.ShowMessage("Report export cancelled. The previous report file was kept.");
            return false;
        }
        catch (Exception exception)
        {
            string guidance = exception is IOException or UnauthorizedAccessException
                ? " Check that the destination is writable and that the report is closed in other applications." : "";
            ShellMessages.Report(this, "The report could not be exported. " + exception.Message + guidance, "Export report", image: MessageBoxImage.Error);
            return false;
        }
        finally
        {
            if (stagedFile != null)
                try { if (File.Exists(stagedFile)) File.Delete(stagedFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            // Only this command's freshly created GUID folder is eligible for recursive cleanup.
            if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(scratch)), Path.GetFullPath(scratchParent), StringComparison.OrdinalIgnoreCase))
                try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            reportExportInProgress = false;
            RefreshProjectCommandAvailability();
        }
    }

    private static async Task<ReportDocument> PrepareReportDocumentAsync(IReadOnlyList<QuantRow> rows,
        QuantManifest manifest, FamilyClusterFile snapshot, string folder, OperationContext operation)
    {
        ReportDocument? prepared = null;
        var factory = ReportDocumentFactoryOverride;
        operation.Report("report-prepare", "Preparing report sections and figures");
        await AppOperationFiles.RunSta(() =>
        {
            operation.ThrowIfCancellationRequested();
            prepared = factory != null ? factory(rows, manifest, snapshot, folder)
                : ReportProjectExporter.Build(rows, manifest, snapshot, folder, snapshot.ReportDetails, snapshot.ReportInputProvenance).Document;
            operation.ThrowIfCancellationRequested();
        });
        return prepared ?? throw new InvalidOperationException("Report preparation returned no document.");
    }

    private string? ChooseReportPath()
    {
        if (SelectReportPathOverride != null) return SelectReportPathOverride();
        var dialog = new SaveFileDialog { Title = "Export Word report", Filter = "Word report (*.docx)|*.docx", DefaultExt = ".docx", AddExtension = true, FileName = "Report.docx" };
        return ShellDialogs.ShowFile(this, "report.word", dialog) ? dialog.FileName : null;
    }
}
