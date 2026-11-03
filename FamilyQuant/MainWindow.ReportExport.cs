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
    public string? ReportExportLogPath { get; private set; }
    public static Func<string?>? SelectReportPathOverride { get; set; }
    public static Action<ReportExportWindow>? ReportExportPresenterOverride { get; set; }
    public static Func<IReadOnlyList<QuantRow>, QuantManifest, FamilyClusterFile, string, ReportDocument>? ReportDocumentFactoryOverride { get; set; }
    public static Action<string, ReportDocument, ReportSelection, OperationContext>? ReportFileStagerOverride { get; set; }
    public static Action<int>? ReportPublicationStepOverride { get; set; }

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
        string? publicationStage = null, publicationParent = null;
        bool keepPublicationStage = false;
        DateTimeOffset exportStarted = DateTimeOffset.UtcNow;
        try
        {
            ReportExportLogPath = Path.Combine(ApplicationPaths.LocalDataDirectory, "operation-logs", "report-" + Guid.NewGuid().ToString("N") + ".log");
            FamilyClusterFile snapshot;
            QuantRow[] capturedRows;
            ReportDocument document;
            ReportProjectExport prepared;
            using (var operation = Operations.Begin("Prepare report", exportStarted, ReportExportLogPath))
            {
                operation.Context.Report("report-attribution", "Waiting for family attribution", 0, 1);
                await project.FullXicReady.WaitAsync(operation.Context.CancellationToken);
                var ready = await RefreshSpectrumFamiliesAsync();
                operation.Context.ThrowIfCancellationRequested();
                if (!ready.Completed) throw new InvalidOperationException("The project changed while its family attribution was being prepared.");
                RequireSameProject();
                operation.Context.Report("report-attribution", "Family attribution ready", 1, 1);
                operation.Context.Report("report-snapshot", "Capturing the project and stored Results", 0, 1);
                var captured = await Task.Run(() => (File: FamilyClusterFile.FromJsonText(curation.ToJsonText()), Rows: rows.ToArray()));
                snapshot = captured.File; capturedRows = captured.Rows;
                operation.Context.Report("report-snapshot", "Project and stored Results captured", 1, 1);
                RequireSameProject();
                prepared = await PrepareReportDocumentAsync(capturedRows, manifest, snapshot, Path.Combine(scratch, "preview"), operation.Context);
                document = prepared.Document;
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

            using (var operation = Operations.Begin("Export report", exportStarted, ReportExportLogPath))
            {
                if (threshold != previousThreshold)
                {
                    prepared = await PrepareReportDocumentAsync(capturedRows, manifest, snapshot, Path.Combine(scratch, "selected"), operation.Context);
                    document = prepared.Document;
                }
                RequireSameProject();
                operation.Context.ThrowIfCancellationRequested();
                publicationParent = Path.GetDirectoryName(fullPath)!;
                Directory.CreateDirectory(publicationParent);
                publicationStage = Path.Combine(publicationParent, ".fq-report-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(publicationStage);
                string stage = Path.Combine(publicationStage, Path.GetFileName(fullPath));
                string workbookStage = ReportSupplementWorkbook.PathForReport(stage);
                var stager = ReportFileStagerOverride;
                operation.Context.Report("report-write", "Writing the selected report and supplement workbook", 0, 1);
                await Task.Run(() =>
                {
                    if (stager != null)
                    {
                        stager(stage, document, selected, operation.Context);
                        operation.Context.ThrowIfCancellationRequested();
                    }
                    else
                    {
                        File.WriteAllBytes(stage, WordReportWriter.BuildBytes(document, selected, operation.Context));
                    }
                    operation.Context.ThrowIfCancellationRequested();
                    File.WriteAllBytes(workbookStage, ReportSupplementWorkbook.BuildBytes(document, prepared.SupplementSheets, selected, operation.Context));
                });
                operation.Context.ThrowIfCancellationRequested();
                RequireSameProject();
                operation.Context.Report("report-publish", "Publishing the Word report and supplement workbook", 0, 2);
                ReportArtifactPublication.Publish(stage, workbookStage, fullPath, operation.Context, step =>
                {
                    operation.Context.Report("report-publish", step == 1 ? "Published Word report" : "Published supplement workbook", step, 2);
                    ReportPublicationStepOverride?.Invoke(step);
                });
                StatusModel.ShowMessage("Report exported to " + fullPath + " with " + Path.GetFileName(ReportSupplementWorkbook.PathForReport(fullPath))
                    + ". Save the project to keep any changed report details or choices.");
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusModel.ShowMessage("Report export cancelled. The previous report files were kept.");
            return false;
        }
        catch (Exception exception)
        {
            Operations.LogFailure(exception);
            keepPublicationStage = exception is ReportPublicationRecoveryException;
            string guidance = exception is IOException or UnauthorizedAccessException
                ? " Check that the destination is writable and that the report is closed in other applications." : "";
            ShellMessages.Report(this, "The report could not be exported. " + exception.Message + guidance, "Export report", image: MessageBoxImage.Error);
            return false;
        }
        finally
        {
            if (!keepPublicationStage && publicationStage != null && publicationParent != null
                && string.Equals(Path.GetDirectoryName(Path.GetFullPath(publicationStage)), Path.GetFullPath(publicationParent), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(publicationStage).StartsWith(".fq-report-", StringComparison.Ordinal))
                try { if (Directory.Exists(publicationStage)) Directory.Delete(publicationStage, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            // Only this command's freshly created GUID folder is eligible for recursive cleanup.
            if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(scratch)), Path.GetFullPath(scratchParent), StringComparison.OrdinalIgnoreCase))
                try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            reportExportInProgress = false;
            RefreshProjectCommandAvailability();
        }
    }

    private static async Task<ReportProjectExport> PrepareReportDocumentAsync(IReadOnlyList<QuantRow> rows,
        QuantManifest manifest, FamilyClusterFile snapshot, string folder, OperationContext operation)
    {
        ReportProjectExport? prepared = null;
        var factory = ReportDocumentFactoryOverride;
        operation.Report("report-prepare", "Preparing report sections and figures", 0, 1);
        await AppOperationFiles.RunSta(() =>
        {
            operation.ThrowIfCancellationRequested();
            if (factory != null) prepared = new(factory(rows, manifest, snapshot, folder), new Dictionary<string, string>(StringComparer.Ordinal), Array.Empty<ReportPlacedFigure>());
            else
            {
                prepared = ReportProjectExporter.Build(rows, manifest, snapshot, folder, snapshot.ReportDetails, snapshot.ReportInputProvenance, operation: operation);
                // The published carrier precedes RS's final J6b Build wiring. Use the owner's
                // layout while the carrier is empty; a populated carrier is already compact.
                if (prepared.SupplementSheets.Count == 0 && prepared.CsvExports.Count > 0)
                {
                    var layout = ReportSupplementLayout.Apply(prepared.Document, prepared.CsvExports);
                    prepared = prepared with { Document = layout.Document, SupplementSheets = layout.Sheets };
                }
            }
            operation.ThrowIfCancellationRequested();
        });
        operation.Report("report-prepare", "Report sections and figures prepared", 1, 1);
        return prepared ?? throw new InvalidOperationException("Report preparation returned no document.");
    }

    private string? ChooseReportPath()
    {
        if (SelectReportPathOverride != null) return SelectReportPathOverride();
        var dialog = new SaveFileDialog { Title = "Export Word report", Filter = "Word report (*.docx)|*.docx", DefaultExt = ".docx", AddExtension = true, FileName = "Report.docx" };
        return ShellDialogs.ShowFile(this, "report.word", dialog) ? dialog.FileName : null;
    }
}
