using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportCommandTable() =>
    [new("--test-report-command", 431, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-report-command", (args, _) => ReportCommandTests(args))];

    private static bool FinishReportCommand(Task<bool> task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromMinutes(3)) { PumpDispatcher(); Thread.Sleep(10); }
        if (!task.IsCompleted) throw new TimeoutException("The report command did not complete within the fixture timeout.");
        PumpDispatcher();
        return task.GetAwaiter().GetResult();
    }

    private static void WaitForReportFixture(MainWindow shell)
    {
        var control = ((Grid)shell.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
        FinishBusyTask(control.FullXicReady);
        control.FullXicReady.GetAwaiter().GetResult();
        PumpDispatcher();
    }

    private static int ReportCommandTests(string[] args)
    {
        if (args.Length > 2) throw new ArgumentException("Use --test-report-command [fresh-output].");
        EnsureApplication();
        string output = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.ReportCommand", Guid.NewGuid().ToString("N"));
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a fresh report-command output folder.");
        Directory.CreateDirectory(output);
        var details = new ReportDetails { Title = "Synthetic command report", Authors = "Synthetic authors", SampleDescription = "Synthetic sample",
            Workflow = "Synthetic workflow", DatabaseKind = "Synthetic database kind", WrittenInterpretation = "First saved paragraph.\n\nSecond saved paragraph." };
        var file = new FamilyClusterFile { ReportDetails = details, MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "REPORT_COMMAND_TARGET", Sequence = "TTACDEFGHIKGG", Description = "Synthetic command fixture", UserID = 1, USearchID = 1 }
        }, ReportInputProvenance = new() { DatabaseFileName = "database.fcf", SeproFileName = "inputs.sepr2", SearchParameterFileName = "search.params",
            SearchParameterSha256 = new string('a', 64), SearchParameters = new[] { new ReportSearchParameter("recorded_setting", "12.5") } } };
        var manifest = new QuantManifest { PerRunTic = new() { ["report_command"] = 100000 }, FamilyMapping = new() { MinIdentity = .99, Method = "Recorded fixture mapping" } };
        var rows = new[] { Qr("identified", "report_command", 1, 2, "ACDEFGHIK", 100, 150) };
        string input = Path.Combine(output, "fixture.fqproj"), empty = Path.Combine(output, "empty.fqproj"), report = Path.Combine(output, "command-report.docx");
        ProjectStore.WriteFqproj(input, file, rows, manifest);
        ProjectStore.WriteFqproj(empty, file, Array.Empty<QuantRow>(), new QuantManifest());
        var oldPath = MainWindow.SelectReportPathOverride;
        var oldPresenter = MainWindow.ReportExportPresenterOverride;
        var oldFactory = MainWindow.ReportDocumentFactoryOverride;
        var oldStager = MainWindow.ReportFileStagerOverride;
        var oldPublication = MainWindow.ReportPublicationStepOverride;
        var oldMessages = ShellMessages.Presenter;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(output, "preferences.json");
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        ShellMessages.Presenter = (_, message) =>
        {
            File.AppendAllText(Path.Combine(output, "messages.log"), message.Text + Environment.NewLine);
            Console.WriteLine("Report fixture message: " + message.Text);
            return MessageBoxResult.OK;
        };
        var shell = new MainWindow { Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Width = 1024, Height = 768 };
        try
        {
            shell.Show(); PumpDispatcher();
            int choices = 0;
            MainWindow.SelectReportPathOverride = () => { choices++; return report; };
            MainWindow.ReportExportPresenterOverride = editor => editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            shell.OpenProjectPath(empty);
            WaitForReportFixture(shell);
            var menu = (MenuItem)shell.FindName("MenuItemExportReport");
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check("native File Export report refuses missing Results before choosing output", shell.ReportExportTask != null
                && !FinishReportCommand(shell.ReportExportTask) && choices == 0 && !File.Exists(report) && !shell.DocumentState.IsModified);

            shell.OpenProjectPath(input);
            WaitForReportFixture(shell);
            MainWindow.ReportDocumentFactoryOverride = null;
            MainWindow.ReportFileStagerOverride = null;
            MainWindow.ReportExportPresenterOverride = editor =>
            {
                editor.Left = -30000; editor.Top = -30000; editor.ShowActivated = false; editor.ShowInTaskbar = false; editor.WindowStartupLocation = WindowStartupLocation.Manual;
                editor.Show(); PumpDispatcher();
                ReportCheckboxes(editor).Single(box => Equals(box.Tag, ReportSectionIds.Quality)).IsChecked = false;
                editor.ThresholdEditor.Value = 0.2m;
                editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            bool exported = shell.ReportExportTask != null && FinishReportCommand(shell.ReportExportTask);
            Check("native File Export report writes the production Word document", exported && File.Exists(report) && !shell.Operations.IsBusy);
            string supplement = ReportSupplementWorkbook.PathForReport(report);
            Check("native File Export report publishes its same-base supplement workbook", exported && File.Exists(supplement));
            if (!exported) throw new InvalidOperationException("Native report export failed; inspect the shell message and fixture output.");
            using (var archive = ZipFile.OpenRead(report))
            {
                using var stream = archive.GetEntry("word/document.xml")!.Open();
                var document = XDocument.Load(stream);
                XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var text = document.Descendants(word + "t").Select(node => node.Value).ToList();
                Check("native report receives every stored detail and both interpretation paragraphs", new[] { details.Title, details.Authors,
                    details.SampleDescription, details.Workflow, details.DatabaseKind, "First saved paragraph.", "Second saved paragraph." }.All(text.Contains));
                Check("native report methods receive stored source names and parsed settings", new[] { "database.fcf", "inputs.sepr2", "search.params", "recorded_setting", "12.5" }.All(text.Contains));
                Check("the unticked section is absent while Methods and Results remain", !text.Contains("Quality") && text.Contains("Methods") && text.Contains("Results"));
            }
            var current = shell.NavigationModel.Document!;
            var expectedExport = ReportProjectExporter.Build(rows, manifest, current, Path.Combine(output, "reference-figures"), current.ReportDetails, current.ReportInputProvenance);
            if (expectedExport.SupplementSheets.Count == 0 && expectedExport.CsvExports.Count > 0)
            {
                var layout = ReportSupplementLayout.Apply(expectedExport.Document, expectedExport.CsvExports);
                expectedExport = expectedExport with { Document = layout.Document, SupplementSheets = layout.Sheets };
            }
            var workbookCells = ReadSupplement(File.ReadAllBytes(supplement));
            var expectedSheets = expectedExport.SupplementSheets.Where(sheet => sheet.OwnerSectionId != ReportSectionIds.Quality).ToArray();
            Check("published companion contains exactly the selected production sheets", workbookCells.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedSheets.Select(sheet => sheet.Name).Append("Contents")));
            Check("published companion cells equal every complete native source table", expectedSheets.All(sheet =>
            {
                var expectedCells = new[] { sheet.Table.Columns.ToArray() }.Concat(sheet.Table.Rows.Select(row => row.ToArray())).ToArray();
                return workbookCells[sheet.Name].Length == expectedCells.Length
                    && workbookCells[sheet.Name].Zip(expectedCells).All(pair => pair.First.SequenceEqual(pair.Second));
            }));
            Check("native export stores choices and threshold and leaves the project dirty", current.ReportSelection?.ExcludedIds is [ReportSectionIds.Quality]
                && current.ReportThreshold == 0.002 && shell.DocumentState.IsModified);
            string saved = Path.Combine(output, "chosen.fqproj");
            ProjectStore.WriteFqproj(saved, current, rows, manifest);
            var reopened = ProjectStore.ReadFqproj(saved).fcf;
            Check("native export options survive an actual project reopen", reopened.ReportThreshold == 0.002 && reopened.ReportSelection?.ExcludedIds is [ReportSectionIds.Quality]);

            // Subsequent guard cases use the real metadata builder and writer with no chart work.
            MainWindow.ReportDocumentFactoryOverride = (_, value, source, _) => ReportDocumentBuilder.Build(source, value, source.ReportDetails, source.ReportInputProvenance);
            MainWindow.ReportExportPresenterOverride = editor => editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            shell.OpenProjectPath(input);
            WaitForReportFixture(shell);
            MainWindow.SelectReportPathOverride = () => null;
            Check("cancelling the file chooser keeps project choices unchanged", !FinishReportCommand(shell.ExportReportAsync()) && !shell.DocumentState.IsModified);
            string originalInputHash = FileFingerprint.Sha256(input);
            MainWindow.SelectReportPathOverride = () => input;
            Check("a report cannot replace its open source project", !FinishReportCommand(shell.ExportReportAsync())
                && FileFingerprint.Sha256(input) == originalInputHash && !shell.DocumentState.IsModified);

            string previous = Path.Combine(output, "previous.docx");
            File.WriteAllText(previous, "previous report bytes");
            string previousBook = ReportSupplementWorkbook.PathForReport(previous);
            File.WriteAllText(previousBook, "previous workbook bytes");
            MainWindow.SelectReportPathOverride = () => previous;
            bool busy = false;
            MainWindow.ReportFileStagerOverride = (stage, document, selected, operation) =>
            {
                WordReportWriter.Write(stage, document, selected);
                shell.Dispatcher.Invoke(() =>
                {
                    busy = shell.Operations.IsBusy && ((BusyOverlay)shell.FindName("ShellBusyOverlay")).Visibility == Visibility.Visible;
                    ScreenHarness.Save(shell, Path.Combine(output, "shared-report-wait.png"), 1024, 768);
                    shell.Operations.Cancel();
                });
            };
            Check("shared Cancel prevents publication and preserves an existing report", !FinishReportCommand(shell.ExportReportAsync())
                && File.ReadAllText(previous) == "previous report bytes" && File.ReadAllText(previousBook) == "previous workbook bytes" && busy && !shell.Operations.IsBusy);
            Check("cancelled report staging leaves no destination temporary file", !Directory.EnumerateFiles(output, ".fq-report-*.docx").Any());

            MainWindow.ReportFileStagerOverride = null;
            string newPair = Path.Combine(output, "cancel-between.docx");
            MainWindow.SelectReportPathOverride = () => newPair;
            MainWindow.ReportPublicationStepOverride = step => { if (step == 1) shell.Operations.Cancel(); };
            Check("native Cancel between the two publications leaves neither artifact", !FinishReportCommand(shell.ExportReportAsync())
                && !File.Exists(newPair) && !File.Exists(ReportSupplementWorkbook.PathForReport(newPair)));
            MainWindow.SelectReportPathOverride = () => previous;
            Check("native Cancel between publications restores both previous files", !FinishReportCommand(shell.ExportReportAsync())
                && File.ReadAllText(previous) == "previous report bytes" && File.ReadAllText(previousBook) == "previous workbook bytes");
            Check("paired cancellation cleans its private staging folder", !Directory.EnumerateDirectories(output, ".fq-report-*").Any());
            MainWindow.ReportPublicationStepOverride = null;

            MainWindow.ReportFileStagerOverride = null;
            MainWindow.SelectReportPathOverride = () => Path.Combine(output, "stale.docx");
            MainWindow.ReportExportPresenterOverride = editor =>
            {
                shell.OpenProjectPath(empty);
                editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            bool stale = FinishReportCommand(shell.ExportReportAsync());
            WaitForReportFixture(shell);
            Check("a replaced project cannot publish a prepared report", !stale
                && !File.Exists(Path.Combine(output, "stale.docx")) && !shell.DocumentState.IsModified);
        }
        finally
        {
            shell.Close(); PumpDispatcher();
            MainWindow.SelectReportPathOverride = oldPath; MainWindow.ReportExportPresenterOverride = oldPresenter;
            MainWindow.ReportDocumentFactoryOverride = oldFactory; MainWindow.ReportFileStagerOverride = oldStager;
            MainWindow.ReportPublicationStepOverride = oldPublication;
            MainWindow.UnsavedProjectChoiceOverride = oldChoice; ShellMessages.Presenter = oldMessages; MainWindow.ShellSettingsPathOverride = oldSettings;
        }
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        Console.WriteLine("Native report-command fixture, no RAW computation: " + output);
        return failed == 0 ? 0 : 1;
    }
}
