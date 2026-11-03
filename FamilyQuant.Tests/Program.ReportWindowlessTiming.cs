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

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportWindowlessTimingCommands() =>
    [new("--measure-report-windowless", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--measure-report-windowless", (args, _) => MeasureReportWindowless(args))];

    private static int MeasureReportWindowless(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Use --measure-report-windowless <project> <new-output>.");
        string root = Path.GetFullPath(@"C:\tmp\familyquant\FQTerra1");
        string prefix = root + Path.DirectorySeparatorChar;
        string project = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        string cache = Path.Combine(output, "cache");
        if (!string.Equals(Path.GetFullPath(Environment.CurrentDirectory), root, StringComparison.OrdinalIgnoreCase)
            || new[] { project, output, cache }.Any(p => !p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            || !File.Exists(project)) throw new IOException("All inputs and outputs must be inside the owned remote worker folder.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("The output folder must be new.");
        Directory.CreateDirectory(output);
        string logPath = Path.Combine(output, "measurement.log");
        using var log = new StreamWriter(logPath) { AutoFlush = true };
        var total = Stopwatch.StartNew();
        void Log(string message)
        {
            string line = total.Elapsed.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture) + "\t" + message;
            log.WriteLine(line); Console.WriteLine(line);
        }
        string original = FileFingerprint.Sha256(project);
        try
        {
            EnsureApplication(); // Initializes resources but does not show a window.
            Log("Reading project copy"); var saved = ProjectStore.ReadFqproj(project);
            Log("Read " + saved.quant.Count.ToString(CultureInfo.InvariantCulture) + " stored rows");
            var measurements = new List<object>();
            Dictionary<string, string>? firstCsv = null;
            string? firstWorkbookHash = null;
            bool csvEqual = false, bothReportsComplete = true, workbookEqual = false;
            for (int round = 0; round < 2; round++)
            {
                string name = round == 0 ? "cold" : "warm";
                string folder = Path.Combine(output, name); Directory.CreateDirectory(folder);
                var clock = Stopwatch.StartNew(); var progress = new J32TimedProgress(clock, log);
                var operation = new OperationContext(CancellationToken.None, progress);
                Log("Starting " + name + " windowless report preparation");
                var export = ReportProjectExporter.Build(saved.quant, saved.manifest, saved.fcf, Path.Combine(folder, "figures"),
                    saved.fcf.ReportDetails, saved.fcf.ReportInputProvenance, operation: operation, homologyCacheDirectory: cache);
                bool hit = progress.Events.Any(e => e.Stage == "report-primary-cache" && e.Message.Contains("hit", StringComparison.OrdinalIgnoreCase));
                bool miss = progress.Events.Any(e => e.Stage == "report-primary-cache" && e.Message.Contains("miss", StringComparison.OrdinalIgnoreCase));
                if ((round == 0 && !miss) || (round == 1 && !hit)) throw new InvalidDataException("Expected cold miss and warm hit were not observed.");
                string report = Path.Combine(folder, "report.docx"); string? workbookFailure = null;
                try { ReportProjectExporter.Write(export, report); }
                catch (InvalidDataException ex) when (ex.Message.Contains("exceeds Excel's text limit", StringComparison.Ordinal))
                { workbookFailure = ex.ToString(); File.WriteAllText(Path.Combine(folder, "workbook-failure.txt"), workbookFailure); }
                string workbook = ReportSupplementWorkbook.PathForReport(report);
                bool complete = workbookFailure == null && File.Exists(report) && File.Exists(workbook);
                bothReportsComplete &= complete;
                var csv = export.CsvExports.ToDictionary(kv => kv.Key,
                    kv => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kv.Value))), StringComparer.Ordinal);
                string? workbookHash = complete ? FileFingerprint.Sha256(workbook) : null;
                if (round == 0) { firstCsv = csv; firstWorkbookHash = workbookHash; }
                else
                {
                    csvEqual = firstCsv != null && firstCsv.Count == csv.Count
                        && firstCsv.All(kv => csv.TryGetValue(kv.Key, out string? value) && value == kv.Value);
                    workbookEqual = firstWorkbookHash != null && firstWorkbookHash == workbookHash;
                }
                var measurement = new { name, cacheHit = hit, cacheMiss = miss,
                    primarySeconds = progress.Seconds("report-primary-start", "report-primary-end"),
                    identifiedCensusSeconds = progress.Seconds("report-census-identified-start", "report-census-identified-end"),
                    deNovoCensusSeconds = progress.Seconds("report-census-denovo-start", "report-census-denovo-end"),
                    totalSeconds = clock.Elapsed.TotalSeconds, csvCount = csv.Count, reportComplete = complete,
                    workbookSha256 = workbookHash, workbookFailure };
                measurements.Add(measurement); Log(JsonSerializer.Serialize(measurement));
            }
            bool unchanged = original == FileFingerprint.Sha256(project);
            var result = new { projectSha256 = original, inputUnchanged = unchanged, csvBytesEqual = csvEqual,
                bothReportsComplete, workbookBytesEqual = workbookEqual, measurements };
            File.WriteAllText(Path.Combine(output, "measurement.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Log(JsonSerializer.Serialize(result));
            if (!unchanged || !csvEqual || (bothReportsComplete && !workbookEqual)) return 1;
            return bothReportsComplete ? 0 : 2;
        }
        catch (Exception ex)
        {
            Log("Measurement failed: " + ex); File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); return 1;
        }
    }
}
