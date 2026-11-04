using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> NativeReportExportCommands() => new[]
    {
        new TestCommand("--export-report-command", 10, TestCommandPhase.Raw,
            args => args.Length == 3 && args[0] == "--export-report-command", (args, _) => NativeReportExport(args[1], args[2]))
    };

    private static int NativeReportExport(string projectPath, string output)
    {
        EnsureApplication();
        projectPath = Path.GetFullPath(projectPath); output = Path.GetFullPath(output);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a fresh output folder.");
        Directory.CreateDirectory(output);
        string before = FileFingerprint.Sha256(projectPath), report = Path.Combine(output, "report.docx");
        var total = Stopwatch.StartNew(); var gate = new object();
        void Log(string text) { lock (gate) { string line = total.Elapsed.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " s " + text; Console.WriteLine(line); File.AppendAllText(Path.Combine(output, "command.log"), line + Environment.NewLine); } }
        void Wait(Func<bool> complete)
        {
            var watch = Stopwatch.StartNew();
            while (!complete()) { PumpDispatcher(); Thread.Sleep(20); if (watch.Elapsed > TimeSpan.FromHours(1)) throw new TimeoutException("The native report command exceeded its timeout."); }
            PumpDispatcher();
        }
        var oldPath = MainWindow.SelectReportPathOverride; var oldPresenter = MainWindow.ReportExportPresenterOverride;
        var oldFactory = MainWindow.ReportDocumentFactoryOverride; var oldStager = MainWindow.ReportFileStagerOverride;
        var oldMessages = ShellMessages.Presenter; var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        int ui = Environment.CurrentManagedThreadId;
        EventHandler<FirstChanceExceptionEventArgs> firstChance = (_, e) =>
        {
            if (e.Exception is InvalidOperationException && e.Exception.Message.Contains("different thread", StringComparison.OrdinalIgnoreCase))
                Log("First chance UI=" + ui + " current=" + Environment.CurrentManagedThreadId + " " + e.Exception);
        };
        AppDomain.CurrentDomain.FirstChanceException += firstChance;
        MainWindow.ShellSettingsPathOverride = Path.Combine(output, "preferences.json");
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        MainWindow.ReportDocumentFactoryOverride = null; MainWindow.ReportFileStagerOverride = null;
        ShellMessages.Presenter = (_, message) => { Log("Shell message: " + message.Text); return MessageBoxResult.OK; };
        var shell = new MainWindow { Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Width = 1200, Height = 800 };
        double openSeconds = 0, exportSeconds = 0;
        try
        {
            shell.Show(); PumpDispatcher();
            string last = "";
            shell.StatusModel.PropertyChanged += (_, _) => { if (shell.StatusModel.Text != last) { last = shell.StatusModel.Text; Log("Status: " + last); } };
            Log("Open " + projectPath + "; SHA-256=" + before);
            var open = Stopwatch.StartNew();
            if (!shell.OpenProjectPath(projectPath)) throw new InvalidOperationException("The project open was refused.");
            ClusterGenerationControl? Families() => typeof(MainWindow).GetField("cgc", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(shell) as ClusterGenerationControl;
            string lastOperation = "";
            if (Families() is { } loadedView)
                loadedView.Operations.PropertyChanged += (_, _) => { if (loadedView.Operations.Message != lastOperation) { lastOperation = loadedView.Operations.Message; Log("Program activity: " + lastOperation); } };
            Wait(() => !shell.Operations.IsBusy && Families() is { } view && !view.Operations.IsBusy && view.FullXicReady.IsCompleted);
            Families()!.FullXicReady.GetAwaiter().GetResult();
            openSeconds = open.Elapsed.TotalSeconds; Log("Open and attribution ready in " + openSeconds);
            var manifest = (QuantManifest)typeof(MainWindow).GetField("loadedManifest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            var world = manifest.FamilyWorldResult?.ForLength(manifest.DatabaseGapMinimumTagLength ?? FamilyWorldOptions.DefaultMinimumTagLength);
            if (world != null) { _ = new DatabaseGapChart(world.Composition); Log("Database gap screen constructor initialized on UI " + ui); }
            MainWindow.SelectReportPathOverride = () => report;
            MainWindow.ReportExportPresenterOverride = editor =>
            {
                editor.Left = -30000; editor.Top = -30000; editor.ShowActivated = false; editor.ShowInTaskbar = false;
                editor.WindowStartupLocation = WindowStartupLocation.Manual; editor.Show(); PumpDispatcher();
                foreach (var check in ReportCheckboxes(editor)) check.IsChecked = true;
                Log("Report choices accepted"); editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            var menu = (MenuItem)shell.FindName("MenuItemExportReport");
            Log("File > Export report; enabled=" + menu.IsEnabled);
            var export = Stopwatch.StartNew();
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (shell.ReportExportTask == null) throw new InvalidOperationException("The report menu did not start its command.");
            Wait(() => shell.ReportExportTask.IsCompleted);
            bool success = shell.ReportExportTask.GetAwaiter().GetResult();
            exportSeconds = export.Elapsed.TotalSeconds;
            var progress = shell.Operations.LogEntries.ToArray();
            var gaps = progress.Zip(progress.Skip(1), (previous, next) => new { Seconds = (next.Timestamp - previous.Timestamp).TotalSeconds,
                PreviousStage = previous.Stage, PreviousMessage = previous.Message, NextStage = next.Stage, NextMessage = next.Message }).ToArray();
            var longest = gaps.OrderByDescending(gap => gap.Seconds).FirstOrDefault();
            File.WriteAllText(Path.Combine(output, "export-progress.json"), JsonSerializer.Serialize(new { Entries = progress, LongestGap = longest }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "export-progress.log"), shell.Operations.LogText);
            if (longest != null) Log("Longest export gap " + longest.Seconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + " s: " + longest.PreviousStage + " / " + longest.PreviousMessage + " -> " + longest.NextStage + " / " + longest.NextMessage);
            string workbook = ReportSupplementWorkbook.PathForReport(report);
            string html = HtmlReportWriter.PathForReport(report);
            if (!success || !File.Exists(report) || !File.Exists(workbook) || !File.Exists(html)) throw new InvalidOperationException("Native report export failed; inspect command.log.");
            string after = FileFingerprint.Sha256(projectPath);
            if (after != before) throw new InvalidOperationException("Export changed its source project.");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { Project = projectPath, SourceSha256 = before,
                Report = report, Workbook = workbook, Html = html, OpenSeconds = openSeconds, ExportSeconds = exportSeconds, TotalSeconds = total.Elapsed.TotalSeconds,
                ReportSha256 = FileFingerprint.Sha256(report), WorkbookSha256 = FileFingerprint.Sha256(workbook), SourceUnchanged = true,
                ProgramStateDirectory = ApplicationPaths.LocalDataDirectory, LongestProgressGap = longest }, new JsonSerializerOptions { WriteIndented = true }));
            Log("Export succeeded in " + exportSeconds + " s; Word and supplement published; source unchanged");
            return 0;
        }
        finally
        {
            shell.Close(); PumpDispatcher();
            MainWindow.SelectReportPathOverride = oldPath; MainWindow.ReportExportPresenterOverride = oldPresenter;
            MainWindow.ReportDocumentFactoryOverride = oldFactory; MainWindow.ReportFileStagerOverride = oldStager;
            ShellMessages.Presenter = oldMessages; MainWindow.UnsavedProjectChoiceOverride = oldChoice; MainWindow.ShellSettingsPathOverride = oldSettings;
            AppDomain.CurrentDomain.FirstChanceException -= firstChance;
        }
    }
}
