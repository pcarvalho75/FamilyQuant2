using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportStaCommands() => new[]
    {
        new TestCommand("--test-report-sta", 10, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-report-sta", (args, _) =>
            {
                ReportStaChecks(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "fq-report-sta-" + Guid.NewGuid().ToString("N")));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            })
    };

    private static void ReportStaChecks(string folder)
    {
        Section("J30 - interface-first Database gap then report-owned STA");
        EnsureApplication();
        int uiThread = Environment.CurrentManagedThreadId;
        var world = new FamilyWorldSummary
        {
            MinimumTagLength = FamilyWorldOptions.DefaultMinimumTagLength, CompositionRules = FamilyWorldErrors.CompositionRule,
            Composition = new() { new() { Family = "Stored reference row", IdentifiedEnvelope = 10, DatabaseDeNovoEnvelope = 3, WorldDeNovoEnvelope = 2 } },
        };
        var uiChart = new DatabaseGapChart(world.Composition);
        Check("the screen chart is initialized on the application dispatcher first", uiChart.Dispatcher.CheckAccess()
            && ChartPalette.ColorOf(uiChart.Palette.Backdrop) == ChartPalette.ColorOf(ChartPalette.UiPalette.Backdrop));
        var manifest = new QuantManifest { FamilyWorldResult = world };
        var project = new FamilyClusterFile();
        var rows = SyntheticAnalyticsRows();
        var families = FamilyFixture(3);
        int workerThread = 0;
        byte[]? firstWord = null, firstBook = null;
        var old = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string locale in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
                Exception? problem = null;
                ReportProjectExport? export = null;
                var watch = Stopwatch.StartNew();
                var work = AppOperationFiles.RunSta(() =>
                {
                    workerThread = Environment.CurrentManagedThreadId;
                    export = ReportProjectExporter.Build(rows, manifest, project, Path.Combine(folder, locale), familyResults: families);
                });
                while (!work.IsCompleted && watch.Elapsed < TimeSpan.FromMinutes(2)) { PumpDispatcher(); Thread.Sleep(5); }
                try { work.GetAwaiter().GetResult(); } catch (Exception exception) { problem = exception; }
                Console.WriteLine("Report STA " + locale + ": " + watch.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + " s; UI=" + uiThread + "; worker=" + workerThread);
                Check("report creation stays on its own STA", workerThread != 0 && workerThread != uiThread);
                Check("Database gap report exports after the UI chart was initialized", problem == null, problem?.ToString());
                if (problem != null) return;
                byte[] word = WordReportWriter.BuildBytes(export!.Document);
                byte[] book = ReportSupplementWorkbook.BuildBytes(export.Document, export.SupplementSheets);
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, locale + ".docx"), word);
                File.WriteAllBytes(Path.Combine(folder, locale + "_supplement.xlsx"), book);
                Check("the worker-built report includes the Database gap PNG", export.Figures.Any(f => f.Figure.Id == "figure:database_gap.png"));
                if (firstWord == null) { firstWord = word; firstBook = book; }
                else
                {
                    Check("worker-built Word is byte equal across locales", firstWord.SequenceEqual(word));
                    Check("worker-built supplement is byte equal across locales", firstBook!.SequenceEqual(book));
                }
            }
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
    }
}
