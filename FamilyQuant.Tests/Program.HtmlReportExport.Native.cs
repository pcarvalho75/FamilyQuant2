using FamilyQuant.Report;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void HtmlReportPanelSelectionTests(string output)
    {
        var (file, rows, hits, assigned, manifest) = EffectivePanelFixture();
        EffectivePanelFamilyStudy.Store(manifest, assigned, hits, file);
        var export = ReportProjectExporter.Build(rows, manifest, file, Path.Combine(output, "figures"),
            familyResults: FamilyEnvelopeQuant.BuildFromAttributedPeptides(assigned));
        Check("HTML export retains collapsed panel identity and the exact prepared coverage inputs",
            export.HtmlContext.UsesPanel && !export.HtmlContext.IsIdentity
            && ReferenceEquals(export.HtmlContext.StoredStudies!.FamilyRunShares, manifest.EffectivePanelFamilies!.RunShares)
            && export.HtmlContext.PanelKeys.Values.Contains("main")
            && export.HtmlContext.FamilySections.All(pair => pair.Value == ReportSectionIds.FamilyPrefix + export.HtmlContext.PanelKeys[pair.Key])
            && export.HtmlContext.FigureDetailsCsv == export.CsvExports[FigureLayout.DetailFileName]);
        var included = HtmlReportPayloadBuilder.Build(export.Document, export.SupplementSheets, context: export.HtmlContext);
        var main = included.Composition.Families.Single(f => included.Fields[f.Name].Text == "Main group");
        var hidden = HtmlReportPayloadBuilder.Build(export.Document, export.SupplementSheets,
            new() { ExcludedIds = new[] { ReportSectionIds.FamilyPrefix + "main" } }, export.HtmlContext);
        var headline = hidden.Composition.Families.Single(f => hidden.Fields[f.Name].Text == "Main group");
        Check("HTML excluded family section omits its details while retaining the selected Results headline",
            main.Parts.Count > 0 && headline.Parts.Count == 0 && headline.Coverage.Count == 0
            && hidden.Fields[headline.Mean!].Text == included.Fields[main.Mean!].Text);
    }

    private static void HtmlReportNativeExportTests(string output)
    {
        Directory.CreateDirectory(output);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
            { new() { SequenceIdentifier = "HTML_TARGET", Sequence = "TTACDEFGHIKGG", UserID = 1, USearchID = 1 } } };
        string input = Path.Combine(output, "fixture.fqproj"), word = Path.Combine(output, "report.docx");
        ProjectStore.WriteFqproj(input, file, new[] { Qr("identified", "fixture", 1, 2, "ACDEFGHIK", 100, 150) },
            new QuantManifest { PerRunTic = new() { ["fixture"] = 100000 } });
        string sourceHash = FileFingerprint.Sha256(input);
        string[] targets = { word, ReportSupplementWorkbook.PathForReport(word), HtmlReportWriter.PathForReport(word) };
        var oldPath = MainWindow.SelectReportPathOverride;
        var oldPresenter = MainWindow.ReportExportPresenterOverride;
        var oldFactory = MainWindow.ReportDocumentFactoryOverride;
        var oldStager = MainWindow.ReportFileStagerOverride;
        var oldStep = MainWindow.ReportPublicationStepOverride;
        var oldSettings = MainWindow.ShellSettingsPathOverride;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldMessages = ShellMessages.Presenter;
        var shell = OffscreenProofWindow(new MainWindow { Width = 1024, Height = 768 });
        try
        {
            MainWindow.ShellSettingsPathOverride = Path.Combine(output, "preferences.json");
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            MainWindow.SelectReportPathOverride = () => word;
            MainWindow.ReportExportPresenterOverride = editor => editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var fixture = HtmlExportFixture();
            MainWindow.ReportDocumentFactoryOverride = (_, _, _, _) => fixture.Document;
            MainWindow.ReportFileStagerOverride = null;
            MainWindow.ReportPublicationStepOverride = null;
            ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
            shell.Show(); PumpDispatcher();
            if (!shell.OpenProjectPath(input)) throw new InvalidOperationException("Could not open the HTML export fixture.");
            WaitForReportFixture(shell);
            Check("native HTML export publishes all three named artifacts and status", FinishReportCommand(shell.ExportReportAsync())
                && targets.All(File.Exists) && shell.StatusModel.Text.Contains("report.html", StringComparison.Ordinal)
                && shell.StatusModel.Text.Contains("report_supplement.xlsx", StringComparison.Ordinal));
            var previous = targets.Select(File.ReadAllBytes).ToArray();
            foreach (int boundary in new[] { 1, 2, 3 })
            {
                MainWindow.ReportPublicationStepOverride = step => { if (step == boundary) shell.Operations.Cancel(); };
                Check("native HTML Cancel restores the previous three files at boundary " + boundary,
                    !FinishReportCommand(shell.ExportReportAsync()) && !shell.Operations.IsBusy
                    && targets.Select((path, index) => previous[index].SequenceEqual(File.ReadAllBytes(path))).All(equal => equal)
                    && !Directory.EnumerateDirectories(output, ".fq-report-*").Any());
            }
            MainWindow.ReportPublicationStepOverride = null;
            bool reachedHtml = false;
            EventHandler<Operations.OperationProgress> cancelHtml = (_, progress) =>
            {
                if (progress.Stage != "report-html" || progress.Completed != 0) return;
                reachedHtml = true;
                shell.Dispatcher.Invoke(shell.Operations.Cancel);
            };
            shell.Operations.ProgressReported += cancelHtml;
            try
            {
                Check("native Cancel during HTML staging preserves all previous artifacts", !FinishReportCommand(shell.ExportReportAsync()) && reachedHtml
                    && targets.Select((path, index) => previous[index].SequenceEqual(File.ReadAllBytes(path))).All(equal => equal)
                    && !Directory.EnumerateDirectories(output, ".fq-report-*").Any());
            }
            finally { shell.Operations.ProgressReported -= cancelHtml; }
            Check("native HTML export and cancellation never save the source project", FileFingerprint.Sha256(input) == sourceHash);
        }
        finally
        {
            if (shell.Operations.IsBusy) { shell.Operations.Cancel(); FinishBusyOperation(shell.Operations); }
            shell.Close();
            MainWindow.SelectReportPathOverride = oldPath; MainWindow.ReportExportPresenterOverride = oldPresenter;
            MainWindow.ReportDocumentFactoryOverride = oldFactory; MainWindow.ReportFileStagerOverride = oldStager;
            MainWindow.ReportPublicationStepOverride = oldStep; MainWindow.ShellSettingsPathOverride = oldSettings;
            MainWindow.UnsavedProjectChoiceOverride = oldChoice; ShellMessages.Presenter = oldMessages;
        }
    }
}
