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
        if (args.Length is < 3 or > 4) throw new ArgumentException("Use --measure-report-windowless <project> <new-output> [primary-cache-to-copy].");
        string root = Path.GetFullPath(@"C:\tmp\familyquant\FQTerra1");
        string prefix = root + Path.DirectorySeparatorChar;
        string project = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        string cache = Path.Combine(output, "cache");
        if (!string.Equals(Path.GetFullPath(Environment.CurrentDirectory), root, StringComparison.OrdinalIgnoreCase)
            || new[] { project, output, cache }.Any(p => !p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            || !File.Exists(project)) throw new IOException("All inputs and outputs must be inside the owned remote worker folder.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("The output folder must be new.");
        string? seed = args.Length == 4 ? Path.GetFullPath(args[3]) : null;
        if (seed != null && (!seed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(seed)))
            throw new IOException("The primary cache must be inside the owned remote worker folder.");
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
            if (seed != null)
            {
                Directory.CreateDirectory(cache);
                string[] entries = Directory.GetFiles(seed, "*.fqhits", SearchOption.TopDirectoryOnly);
                if (entries.Length == 0) throw new IOException("No primary cache entry is available to seed this comparison.");
                foreach (string entry in entries)
                {
                    string source = Path.GetFullPath(entry), destination = Path.GetFullPath(Path.Combine(cache, Path.GetFileName(entry)));
                    if (!source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || !destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Cache copy is outside the owned remote worker folder.");
                    File.Copy(source, destination, false);
                    if (FileFingerprint.Sha256(source) != FileFingerprint.Sha256(destination))
                        throw new InvalidDataException("Primary cache copy differs from its source.");
                }
                Log("Copied and verified " + entries.Length.ToString(CultureInfo.InvariantCulture) + " primary cache entries");
            }
            EnsureApplication(); // Initializes resources but does not show a window.
            Log("Reading project copy"); var saved = ProjectStore.ReadFqproj(project);
            Log("Read " + saved.quant.Count.ToString(CultureInfo.InvariantCulture) + " stored rows");
            var measurements = new List<object>();
            Dictionary<string, string>? firstCsv = null;
            Dictionary<string, string>? firstDiskCsv = null;
            string? firstWorkbookHash = null;
            bool csvEqual = false, diskCsvEqual = false, bothReportsComplete = true, workbookEqual = false;
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
                if ((round == 0 && seed == null && !miss) || (round == 0 && seed != null && !hit) || (round == 1 && !hit))
                    throw new InvalidDataException("Expected primary cache state was not observed.");
                bool censusHit = progress.Events.Any(e => e.Stage == "report-census-cache" && e.Message.Contains("hit", StringComparison.OrdinalIgnoreCase));
                bool censusMiss = progress.Events.Any(e => e.Stage == "report-census-cache" && e.Message.Contains("miss", StringComparison.OrdinalIgnoreCase));
                if (seed != null && ((round == 0 && !censusMiss) || (round == 1 && !censusHit)))
                    throw new InvalidDataException("Expected source census cache state was not observed.");
                string report = Path.Combine(folder, "report.docx"); string? workbookFailure = null;
                try { ReportProjectExporter.Write(export, report); }
                catch (InvalidDataException ex) when (ex.Message.Contains("exceeds Excel's text limit", StringComparison.Ordinal))
                { workbookFailure = ex.ToString(); File.WriteAllText(Path.Combine(folder, "workbook-failure.txt"), workbookFailure); }
                string workbook = ReportSupplementWorkbook.PathForReport(report);
                bool complete = workbookFailure == null && File.Exists(report) && File.Exists(workbook);
                bothReportsComplete &= complete;
                var csv = export.CsvExports.ToDictionary(kv => kv.Key,
                    kv => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kv.Value))), StringComparer.Ordinal);
                var diskCsv = csv.Keys.ToDictionary(key => key, key => FileFingerprint.Sha256(Path.Combine(folder, "report_data", key)), StringComparer.Ordinal);
                string? workbookHash = complete ? FileFingerprint.Sha256(workbook) : null;
                if (round == 0) { firstCsv = csv; firstDiskCsv = diskCsv; firstWorkbookHash = workbookHash; }
                else
                {
                    csvEqual = firstCsv != null && firstCsv.Count == csv.Count
                        && firstCsv.All(kv => csv.TryGetValue(kv.Key, out string? value) && value == kv.Value);
                    diskCsvEqual = firstDiskCsv != null && firstDiskCsv.Count == diskCsv.Count
                        && firstDiskCsv.All(kv => diskCsv.TryGetValue(kv.Key, out string? value) && value == kv.Value);
                    workbookEqual = firstWorkbookHash != null && firstWorkbookHash == workbookHash;
                }
                var measurement = new { name, cacheHit = hit, cacheMiss = miss,
                    primarySeconds = progress.Seconds("report-primary-start", "report-primary-end"),
                    censusCacheSeconds = progress.OptionalSeconds("report-census-cache-start", "report-census-cache-end"),
                    censusCacheHit = censusHit, censusCacheMiss = censusMiss,
                    identifiedCensusSeconds = progress.Seconds("report-census-identified-start", "report-census-identified-end"),
                    deNovoCensusSeconds = progress.Seconds("report-census-denovo-start", "report-census-denovo-end"),
                    totalSeconds = clock.Elapsed.TotalSeconds, csvCount = csv.Count, reportComplete = complete,
                    workbookSha256 = workbookHash, workbookFailure };
                measurements.Add(measurement); Log(JsonSerializer.Serialize(measurement));
            }
            bool unchanged = original == FileFingerprint.Sha256(project);
            var result = new { projectSha256 = original, inputUnchanged = unchanged, csvBytesEqual = csvEqual,
                diskCsvBytesEqual = diskCsvEqual, bothReportsComplete, workbookBytesEqual = workbookEqual, measurements };
            File.WriteAllText(Path.Combine(output, "measurement.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Log(JsonSerializer.Serialize(result));
            if (!unchanged || !csvEqual || !diskCsvEqual || (bothReportsComplete && !workbookEqual)) return 1;
            return bothReportsComplete ? 0 : 2;
        }
        catch (Exception ex)
        {
            Log("Measurement failed: " + ex); File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); return 1;
        }
    }
}
