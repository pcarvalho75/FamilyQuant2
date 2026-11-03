using FamilyQuant.Operations;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class J32TimedProgress : IProgress<OperationProgress>
    {
        private readonly Stopwatch clock;
        private readonly StreamWriter log;
        public readonly List<(string Stage, string Message, double Seconds)> Events = new();
        public J32TimedProgress(Stopwatch clock, StreamWriter log) { this.clock = clock; this.log = log; }
        public void Report(OperationProgress value)
        {
            double seconds = clock.Elapsed.TotalSeconds;
            Events.Add((value.Stage, value.Message, seconds));
            log.WriteLine(seconds.ToString("F6", CultureInfo.InvariantCulture) + "\t" + value.Stage + "\t" + value.Message);
            log.Flush();
        }
        public double Seconds(string start, string end)
        {
            var first = Events.Single(e => e.Stage == start);
            var last = Events.Single(e => e.Stage == end);
            if (last.Seconds < first.Seconds) throw new InvalidDataException("Progress stages are out of order.");
            return last.Seconds - first.Seconds;
        }
    }

    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportAttributionTimingCommands() =>
    [new("--measure-report-attribution-cache", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--measure-report-attribution-cache", (args, _) => MeasureReportAttributionCache(args))];

    private static int MeasureReportAttributionCache(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Use --measure-report-attribution-cache <project-copy> <new-output-folder>.");
        string project = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        string allowed = Path.GetFullPath(@"D:\FamilyQuant-data\out-terra1\J32") + Path.DirectorySeparatorChar;
        if (!project.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(project)) throw new IOException("Use the project copy and a fresh output folder inside J32.");
        string logPath = Path.Combine(output, "measurement.log");
        if (!NativeProofDesktop.IsPrivate)
        {
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("The output folder must be new.");
            Directory.CreateDirectory(output);
            return NativeProofDesktop.Run(args, logPath, 21_600_000);
        }
        if (!Directory.Exists(output) || Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("The owned output folder is not fresh.");
        string cache = Path.Combine(output, "cache");
        string original = FileFingerprint.Sha256(project);
        using var log = new StreamWriter(logPath) { AutoFlush = true };
        var total = Stopwatch.StartNew();
        void Log(string line) => log.WriteLine(total.Elapsed.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture) + "\t" + line);
        try
        {
            EnsureApplication();
            var window = new Window { Width = 900, Height = 650, ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
            window.Show(); PumpDispatcher();
            Log("Reading copied project");
            var saved = ProjectStore.ReadFqproj(project);
            Log("Project read; rows=" + saved.quant.Count.ToString(CultureInfo.InvariantCulture));
            var measurements = new List<object>();
            Dictionary<string, string>? originalCsv = null;
            string? originalWorkbook = null;
            for (int round = 0; round < 2; round++)
            {
                string name = round == 0 ? "cold" : "warm";
                string folder = Path.Combine(output, name); Directory.CreateDirectory(folder);
                var clock = Stopwatch.StartNew(); var progress = new J32TimedProgress(clock, log);
                var context = new OperationContext(CancellationToken.None, progress);
                ReportProjectExport? export = null;
                Log("Starting " + name + " report preparation");
                Task work = AppOperationFiles.RunSta(() => export = ReportProjectExporter.Build(saved.quant, saved.manifest,
                    saved.fcf, Path.Combine(folder, "figures"), saved.fcf.ReportDetails, saved.fcf.ReportInputProvenance,
                    operation: context, homologyCacheDirectory: cache));
                var frame = new DispatcherFrame();
                _ = work.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
                Dispatcher.PushFrame(frame); work.GetAwaiter().GetResult();
                if (export == null) throw new InvalidOperationException("Report preparation returned no result.");
                string report = Path.Combine(folder, "report.docx");
                ReportProjectExporter.Write(export, report);
                string workbook = ReportSupplementWorkbook.PathForReport(report);
                if (!File.Exists(report) || !File.Exists(workbook)) throw new IOException("A report artifact was not written.");
                var csv = export.CsvExports.ToDictionary(kv => kv.Key,
                    kv => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kv.Value))), StringComparer.Ordinal);
                string workbookHash = FileFingerprint.Sha256(workbook);
                bool cacheHit = progress.Events.Any(e => e.Stage == "report-primary-cache" && e.Message.Contains("hit", StringComparison.OrdinalIgnoreCase));
                bool cacheMiss = progress.Events.Any(e => e.Stage == "report-primary-cache" && e.Message.Contains("miss", StringComparison.OrdinalIgnoreCase));
                if ((round == 0 && !cacheMiss) || (round == 1 && !cacheHit)) throw new InvalidDataException("Expected cold miss and warm hit were not observed.");
                if (round == 0) { originalCsv = csv; originalWorkbook = workbookHash; }
                else if (originalCsv == null || originalCsv.Count != csv.Count || originalCsv.Any(kv => !csv.TryGetValue(kv.Key, out string? v) || v != kv.Value)
                    || originalWorkbook != workbookHash) throw new InvalidDataException("Cold and warm report data differ.");
                var result = new { name, cacheHit, cacheMiss, primarySeconds = progress.Seconds("report-primary-start", "report-primary-end"),
                    identifiedCensusSeconds = progress.Seconds("report-census-identified-start", "report-census-identified-end"),
                    deNovoCensusSeconds = progress.Seconds("report-census-denovo-start", "report-census-denovo-end"),
                    totalSeconds = clock.Elapsed.TotalSeconds, csvCount = csv.Count, workbookSha256 = workbookHash };
                measurements.Add(result); Log(JsonSerializer.Serialize(result));
            }
            bool unchanged = original == FileFingerprint.Sha256(project);
            if (!unchanged) throw new InvalidDataException("The input project copy changed.");
            File.WriteAllText(Path.Combine(output, "measurement.json"), JsonSerializer.Serialize(new
            { projectSha256 = original, inputUnchanged = unchanged, csvAndWorkbookBytesEqual = true, measurements }, new JsonSerializerOptions { WriteIndented = true }));
            Log("Both complete exports match; project copy unchanged."); window.Close();
            return 0;
        }
        catch (Exception ex)
        {
            Log("Measurement failed: " + ex); File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString());
            return 1;
        }
    }
}
