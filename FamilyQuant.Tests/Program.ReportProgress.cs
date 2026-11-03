using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Operations;
using FamilyQuant.Report;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportProgressCommands() =>
    [new("--test-report-progress", 436, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-report-progress", (args, _) =>
        {
            ReportProgressTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.ReportProgress"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReportProgressTests(string folder)
    {
        EnsureApplication(); Directory.CreateDirectory(folder);
        var file = new FamilyClusterFile { ReportDetails = new() { Title = "Synthetic progress report", Authors = "Fixture" },
            ReportThreshold = .001, ReportSelection = new(), MyExtendedFastaItems = new()
            { new() { SequenceIdentifier = "progress_target", Sequence = "PEPTIDEK", Description = "Progress fixture", UserID = 1, USearchID = 1 } } };
        var rows = new[] { Qr("identified", "progress", 1, 2, "PEPTIDEK", 100, 120) };
        var manifest = new QuantManifest { CreatedUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), PerRunTic = new() { ["progress"] = 1000 } };
        string input = Path.Combine(folder, "input.fqproj"), report = Path.Combine(folder, "report.docx");
        ProjectStore.WriteFqproj(input, file, rows, manifest);
        var baseline = ReportProjectExporter.Build(rows, manifest, FamilyClusterFile.FromJsonText(file.ToJsonText()), Path.Combine(folder, "baseline-png"));
        byte[] baselineWord = WordReportWriter.BuildBytes(baseline.Document, file.ReportSelection);
        byte[] baselineWorkbook = ReportSupplementWorkbook.BuildBytes(baseline.Document, baseline.SupplementSheets, file.ReportSelection);
        File.WriteAllBytes(Path.Combine(folder, "baseline.docx"), baselineWord);
        File.WriteAllBytes(Path.Combine(folder, "baseline_supplement.xlsx"), baselineWorkbook);
        var oldPath = MainWindow.SelectReportPathOverride; var oldPresenter = MainWindow.ReportExportPresenterOverride;
        var oldMessages = ShellMessages.Presenter; var oldSettings = MainWindow.ShellSettingsPathOverride;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldStager = MainWindow.ReportFileStagerOverride;
        var main = OffscreenProofWindow(new MainWindow { Width = 1024, Height = 768 });
        var trace = new ConcurrentQueue<OperationProgress>();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int pause = 1;
        EventHandler<OperationProgress> observe = (_, step) =>
        {
            trace.Enqueue(step);
            if (step.Stage == "report-write-section" && step.Completed == 1 && Interlocked.Exchange(ref pause, 0) == 1)
            { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Owned progress fixture was not released."); }
        };
        EventInfo? progressEvent = typeof(AppOperationHost).GetEvent("ProgressReported");
        bool WaitForStep(Task<bool> task)
        {
            var watch = Stopwatch.StartNew();
            while (!entered.IsSet && !task.IsCompleted && watch.Elapsed < TimeSpan.FromMinutes(2)) { PumpDispatcher(); Thread.Sleep(10); }
            PumpDispatcher(); return entered.IsSet;
        }
        TimeSpan ReadTime(string name) => typeof(AppOperationHost).GetProperty(name)?.GetValue(main.Operations) is TimeSpan value ? value : TimeSpan.Zero;
        try
        {
            MainWindow.ShellSettingsPathOverride = Path.Combine(folder, "preferences.json");
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            MainWindow.SelectReportPathOverride = () => report;
            MainWindow.ReportExportPresenterOverride = editor => editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
            main.Show(); PumpDispatcher();
            if (!main.OpenProjectPath(input)) throw new InvalidOperationException("Could not open the owned progress fixture.");
            var screen = ((Grid)main.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
            FinishBusyTask(screen.FullXicReady); FinishBusyOperation(main.Operations); PumpDispatcher();
            progressEvent?.AddEventHandler(main.Operations, observe);
            Check("J24 operation host exposes each native progress report", progressEvent != null);
            Task<bool> export = main.ExportReportAsync();
            bool paused = WaitForStep(export);
            Check("J24 native export reaches a counted writing item", paused);
            var overlay = (BusyOverlay)main.FindName("ShellBusyOverlay");
            if (paused)
            {
                var bar = overlay.FindName("OperationProgressBar") as ProgressBar;
                var current = overlay.FindName("OperationCurrentItem") as TextBlock;
                Check("J24 native wait screen shows a determinate bar and current section", main.Operations.IsBusy && bar is { IsIndeterminate: false }
                    && bar.Value > 0 && current?.Text.Contains("Writing section 1 of ", StringComparison.Ordinal) == true);
                TimeSpan elapsed = ReadTime("Elapsed"), since = ReadTime("SinceLastStep");
                string? elapsedBefore = (overlay.FindName("OperationElapsed") as TextBlock)?.Text;
                string? sinceBefore = (overlay.FindName("OperationSinceLastStep") as TextBlock)?.Text;
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(1.25)) { PumpDispatcher(); Thread.Sleep(10); }
                Check("J24 whole-export and last-step clocks keep advancing during a paused item", ReadTime("Elapsed") > elapsed + TimeSpan.FromSeconds(.8)
                    && ReadTime("SinceLastStep") > since + TimeSpan.FromSeconds(.8)
                    && (overlay.FindName("OperationElapsed") as TextBlock)?.Text is { } elapsedAfter && elapsedAfter != elapsedBefore
                    && elapsedAfter.Contains("Elapsed", StringComparison.Ordinal)
                    && (overlay.FindName("OperationSinceLastStep") as TextBlock)?.Text is { } sinceAfter && sinceAfter != sinceBefore
                    && sinceAfter.Contains("last step", StringComparison.Ordinal));
                Check("J24 native wait screen retains Cancel", overlay.FindName("CancelOperationButton") is Button { IsEnabled: true });
                ScreenHarness.Save(main, Path.Combine(folder, "counted-wait.png"), 1024, 768);
            }
            else
            {
                Check("J24 native wait screen shows a determinate bar and current section", false);
                Check("J24 whole-export and last-step clocks keep advancing during a paused item", false);
                Check("J24 native wait screen retains Cancel", false);
            }
            release.Set();
            Check("J24 native fixture export completes", FinishReportCommand(export));
            var captured = trace.ToArray();
            void Items(string stage, IReadOnlyList<string> titles)
            {
                var all = captured.Where(step => step.Stage == stage).ToArray();
                var items = all.Where(step => step.Completed > 0).ToArray();
                Check("J24 " + stage + " starts with a known total and reports each item exactly once in order",
                    all.Length > 0 && all[0].Completed == 0 && all[0].Total == titles.Count && items.Length == titles.Count
                    && items.Select((step, index) => step.Total == titles.Count && step.Completed == index + 1).All(value => value)
                    && titles.All(title => items.Count(step => step.Message.EndsWith(": " + title, StringComparison.Ordinal)) == 1));
            }
            Items("report-prepare-section", baseline.Document.Sections.Select(section => section.Title).ToArray());
            Items("report-prepare-figure", baseline.Figures.Select(figure => figure.Figure.Title).ToArray());
            Items("report-write-section", baseline.Document.Sections.Select(section => section.Title).ToArray());
            using (var workbook = SpreadsheetDocument.Open(ReportSupplementWorkbook.PathForReport(report), false))
                Items("supplement-sheet", workbook.WorkbookPart!.Workbook!.Sheets!.ChildElements.Cast<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
                    .Select(sheet => sheet.Name!.Value!).ToArray());
            Check("J24 progress leaves native Word and workbook bytes unchanged", baselineWord.SequenceEqual(File.ReadAllBytes(report))
                && baselineWorkbook.SequenceEqual(File.ReadAllBytes(ReportSupplementWorkbook.PathForReport(report))));
            string? logPath = typeof(MainWindow).GetProperty("ReportExportLogPath")?.GetValue(main) as string;
            string log = logPath != null && File.Exists(logPath) ? File.ReadAllText(logPath) : "";
            var logged = log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r').Split('\t'))
                .Where(fields => fields.Length == 5 && !fields[1].StartsWith("operation-", StringComparison.Ordinal)).ToArray();
            Check("J24 each native progress step is timestamped in a persistent operation log", captured.Length > 0 && log.Length > 0
                && captured.Select(step => string.Join("\t", step.Stage, step.Completed?.ToString(CultureInfo.InvariantCulture),
                    step.Total?.ToString(CultureInfo.InvariantCulture), step.Message)).SequenceEqual(logged.Select(fields => string.Join("\t", fields.Skip(1))))
                && log.Split('\n', StringSplitOptions.RemoveEmptyEntries).All(line => DateTimeOffset.TryParse(line.Split('\t')[0], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)));
            if (logPath != null && File.Exists(logPath)) File.Copy(logPath, Path.Combine(folder, "successful-export.log"), true);

            byte[] oldWord = File.ReadAllBytes(report), oldWorkbook = File.ReadAllBytes(ReportSupplementWorkbook.PathForReport(report));
            entered.Reset(); release.Reset(); Interlocked.Exchange(ref pause, 1);
            Task<bool> cancel = main.ExportReportAsync(); bool cancelPaused = WaitForStep(cancel);
            if (cancelPaused) main.Operations.CancelCommand.Execute(null);
            release.Set(); bool cancelled = !FinishReportCommand(cancel);
            Check("J24 native Cancel during writing preserves both previous files", cancelPaused && cancelled
                && oldWord.SequenceEqual(File.ReadAllBytes(report)) && oldWorkbook.SequenceEqual(File.ReadAllBytes(ReportSupplementWorkbook.PathForReport(report))));

            string userMessage = "";
            ShellMessages.Presenter = (_, message) => { userMessage = message.Text; return MessageBoxResult.OK; };
            [MethodImpl(MethodImplOptions.NoInlining)]
            static void ThrowInner() => throw new FormatException("Synthetic inner detail");
            [MethodImpl(MethodImplOptions.NoInlining)]
            static void ThrowStager(string _, ReportDocument __, ReportSelection? ___, OperationContext ____)
            {
                try { ThrowInner(); }
                catch (FormatException error) { throw new InvalidOperationException("Synthetic export failure", error); }
            }
            MainWindow.ReportFileStagerOverride = ThrowStager;
            Check("J24 native throwing stager returns a failed export", !FinishReportCommand(main.ExportReportAsync()));
            string? failureLogPath = typeof(MainWindow).GetProperty("ReportExportLogPath")?.GetValue(main) as string;
            string failureLog = failureLogPath != null && File.Exists(failureLogPath) ? File.ReadAllText(failureLogPath) : "";
            Check("J24 report failure log records timestamp, exception type, inner exception and full stack", failureLog.Split('\n').Any(line =>
                line.Contains("\toperation-failure\t", StringComparison.Ordinal)
                && DateTimeOffset.TryParse(line.Split('\t')[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && line.Contains("System.InvalidOperationException: Synthetic export failure", StringComparison.Ordinal)
                && line.Contains("System.FormatException: Synthetic inner detail", StringComparison.Ordinal)
                && line.Contains("ThrowInner", StringComparison.Ordinal) && line.Contains("ThrowStager", StringComparison.Ordinal)
                && line.Contains("ExportReport", StringComparison.Ordinal)));
            Check("J24 report failure keeps the short user message and both previous files", userMessage == "The report could not be exported. Synthetic export failure"
                && oldWord.SequenceEqual(File.ReadAllBytes(report)) && oldWorkbook.SequenceEqual(File.ReadAllBytes(ReportSupplementWorkbook.PathForReport(report))));
            if (failureLogPath != null && File.Exists(failureLogPath)) File.Copy(failureLogPath, Path.Combine(folder, "failed-export.log"), true);
        }
        finally
        {
            release.Set(); progressEvent?.RemoveEventHandler(main.Operations, observe);
            if (main.Operations.IsBusy) { main.Operations.Cancel(); FinishBusyOperation(main.Operations); }
            main.Close(); MainWindow.SelectReportPathOverride = oldPath; MainWindow.ReportExportPresenterOverride = oldPresenter;
            ShellMessages.Presenter = oldMessages; MainWindow.ShellSettingsPathOverride = oldSettings; MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            MainWindow.ReportFileStagerOverride = oldStager;
        }
    }
}
