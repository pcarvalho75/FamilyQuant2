using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record ComputeProgressMeasurement(string Project, string Raw, string Sepro, string DeNovo, string Cache, string Output,
        bool SaveProject = false);
    private sealed record TimedComputeStep(DateTimeOffset At, double Seconds, string Scope, OperationProgress Progress);
    private sealed class ComputeMeasurementTrace(string output) : IProgress<OperationProgress>
    {
        private readonly object gate = new();
        private readonly Stopwatch timer = Stopwatch.StartNew();
        public string Scope { get; set; } = "open";
        public List<TimedComputeStep> Steps { get; } = new();
        public void Report(OperationProgress value)
        {
            lock (gate)
            {
                var step = new TimedComputeStep(DateTimeOffset.UtcNow, timer.Elapsed.TotalSeconds, Scope, value); Steps.Add(step);
                string Clean(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                string line = step.At.ToString("O", CultureInfo.InvariantCulture) + "\t" + Scope + "/" + value.Stage + "\t"
                    + value.Completed?.ToString(CultureInfo.InvariantCulture) + "\t" + value.Total?.ToString(CultureInfo.InvariantCulture)
                    + "\t" + Clean(value.Message);
                Console.WriteLine(line); File.AppendAllText(Path.Combine(output, "progress.log"), line + Environment.NewLine);
            }
        }
    }

    [TestCommandTable]
    private static IEnumerable<TestCommand> ComputeMeasurementCommands() =>
    [new("--measure-compute-progress", 446, TestCommandPhase.Raw,
        args => args.Length == 2 && args[0] == "--measure-compute-progress", (args, _) => MeasureComputeProgress(args[1]))];

    private static string OwnedComputeProofPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(@"C:\tmp\familyquant\FQShell\", StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(@"D:\FamilyQuant-data\out-shell\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Measurement writes and mutable inputs must stay inside the lane-owned folder: " + full);
        return full;
    }

    private static int MeasureComputeProgress(string configuration)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var config = JsonSerializer.Deserialize<ComputeProgressMeasurement>(File.ReadAllText(OwnedComputeProofPath(configuration)))
            ?? throw new InvalidDataException("Missing measurement configuration.");
        string Readable(string path)
        {
            string full = Path.GetFullPath(path);
            return full.StartsWith(@"C:\tmp\familyquant\FQShared\", StringComparison.OrdinalIgnoreCase) ? full : OwnedComputeProofPath(full);
        }
        config = config with { Raw = Readable(config.Raw), DeNovo = Readable(config.DeNovo) };
        string input = OwnedComputeProofPath(config.Project), cache = OwnedComputeProofPath(config.Cache), output = OwnedComputeProofPath(config.Output);
        string sepro = OwnedComputeProofPath(config.Sepro);
        if (Directory.Exists(output)) throw new IOException("Use a fresh measurement output folder.");
        Directory.CreateDirectory(output);
        using var process = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(output, "process.pid"), process.Id.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("PID=" + process.Id + "; machine=" + Environment.MachineName + "; processors=" + Environment.ProcessorCount);
        var trace = new ComputeMeasurementTrace(output); var timer = Stopwatch.StartNew(); var cpu = process.TotalProcessorTime;
        string before = FileFingerprint.Sha256(input); int exit = 1; Exception? failure = null; bool computeCompleted = false;
        try
        {
            var opening = new OperationContext(progress: trace);
            trace.Report(new("measure-open", "Opening owned project copy", 0, 1));
            var saved = ProjectStore.ReadFqproj(input, opening);
            var view = new CurationQuantitationViewModel(saved.fcf);
            typeof(CurationQuantitationViewModel).GetField("fullXicCacheDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(view, Path.Combine(output, "curation-hits"));
            if (!view.AttachWithOperationAsync(saved.quant, saved.manifest.PerRunTic, opening).GetAwaiter().GetResult())
                throw new InvalidOperationException("Project attribution did not complete: " + view.Summary);
            trace.Report(new("measure-open", "Owned project copy and attribution ready", 1, 1));

            var criteria = saved.manifest.DeNovoSourceScoreDistributions.First();
            var options = new FullXicOptions { AppVersion = saved.manifest.AppVersion, FamilyCuration = saved.fcf,
                IsotopeCount = saved.manifest.IsotopeCount, MinPointsForQuant = saved.manifest.MinPointsForQuant,
                Ppm = saved.manifest.Ppm, Ms1Tolerance = saved.manifest.Ms1Tolerance, CacheDirectory = cache,
                SpectrumComparisonCsvPath = Path.Combine(output, "spectrum.csv"),
                DeNovoSelections = saved.manifest.DeNovoInputs.GroupBy(row => row.SourceId).Select(group => group.First()).OrderBy(row => row.SourcePriority)
                    .Select(row => new DeNovoSourceSelection(DeNovoSource.FromId(row.SourceId) ?? throw new InvalidDataException("Unknown saved source."),
                        row.MinimumConfidence, row.SourcePriority)).ToArray(),
                DeNovoCriteria = new() { MinLength = criteria.MinLength, StretchLength = criteria.StretchLength, StretchFloor = criteria.StretchFloor } };
            trace.Scope = "preflight"; trace.Report(new("cache-preflight", "Checking complete extraction and detector caches", 0, 1));
            var stage = typeof(FullXicComputer).Assembly.GetType("FamilyQuant.FullXicInputStage", true)!;
            object prepared = stage.GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object?[] { config.Raw, sepro, config.DeNovo, options, null, null, null })!;
            T Property<T>(object owner, string name) => (T)owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(owner)!;
            var runs = Property<IReadOnlyList<string>>(prepared, "Runs");
            var raws = Property<Dictionary<string, string>>(prepared, "RawByRun");
            var identified = Property<Dictionary<string, List<PatternLabPsm>>>(prepared, "PsmsByRun");
            var denovo = Property<Dictionary<string, List<PeaksDeNovoRow>>>(Property<object>(prepared, "Sources"), "ByRun");
            var files = Directory.GetFiles(cache, "*.json", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var paths = new Dictionary<(string, string), string>(); var detectors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int ordinal = 0;
            foreach (string run in runs)
            {
                trace.Report(new("cache-preflight-run", "Checking cached acquisition " + run, ordinal, runs.Count));
                string fingerprint = FileFingerprint.Sha256(raws[run]);
                void Side(string side, IReadOnlyList<DeNovoXicRequest> requests)
                {
                    if (requests.Count == 0) return;
                    foreach (string path in files.Where(path => Path.GetFileName(path).Contains(run, StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileName(path).Contains(side, StringComparison.OrdinalIgnoreCase)))
                    {
                        var loaded = DeNovoXicCache.Load(path, run, options.Ppm, options.Ms1Tolerance, options.IsotopeCount, options.MinPointsForQuant, fingerprint);
                        if (requests.All(row => row.Charge <= 0 || loaded.TryGet(row.ScanNoMS2,
                            Math.Round(row.PrecursorMz, DeNovoXicExtractor.MzDecimals), row.Charge, out var found) && found != null))
                        { paths[(side, run)] = path; return; }
                    }
                    throw new InvalidDataException("A complete validated " + side + " cache was not found for " + run + "; refusing an unexpected MS1 load.");
                }
                Side("identified", identified.TryGetValue(run, out var psms) ? IdentifiedQuantExtractor.BuildRequests(psms) : Array.Empty<DeNovoXicRequest>());
                Side("denovo", denovo.TryGetValue(run, out var rows) ? DeNovoBatchExtractor.BuildRequests(rows) : Array.Empty<DeNovoXicRequest>());
                foreach (string path in files.Where(path => Path.GetFileName(path).Contains(run, StringComparison.OrdinalIgnoreCase)))
                {
                    object?[] arguments = { path, run, fingerprint, null };
                    if ((bool)typeof(Ms1EnvelopeDetector).GetMethod("TryLoadCache", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments)!)
                    { detectors[run] = path; break; }
                }
                if (!detectors.ContainsKey(run)) throw new InvalidDataException("No validated detector cache for " + run + "; refusing an unexpected MS1 load.");
                trace.Report(new("cache-preflight-run", "All cached requests and detector verified: " + run, ++ordinal, runs.Count));
            }
            trace.Report(new("cache-preflight", "Every extraction request and detector answered by validated cache", 1, 1));
            trace.Scope = "compute";
            var context = new OperationContext(progress: trace);
            var result = FullXicComputer.ComputeFullXic(config.Raw, sepro, config.DeNovo, options,
                (side, run) => paths[(side, run)], detectedCachePathFor: run => detectors[run], operation: context);
            computeCompleted = true;
            trace.Report(new("measure-compute-complete", "Full public Compute returned completed results", 1, 1));
            if (config.SaveProject)
            {
                trace.Scope = "save"; trace.Report(new("measure-save", "Saving completed measured project", 0, 1));
                ProjectStore.WriteFqproj(Path.Combine(output, "computed.fqproj"), saved.fcf, result.Rows, result.Manifest);
                trace.Report(new("measure-save", "Measured project saved", 1, 1));
            }
            if (before != FileFingerprint.Sha256(input)) throw new InvalidDataException("The copied input project changed.");
            exit = 0;
        }
        catch (Exception error)
        {
            failure = error; File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Console.WriteLine(error);
        }
        finally
        {
            process.Refresh();
            var gaps = trace.Steps.Zip(trace.Steps.Skip(1), (left, right) => new { seconds = right.Seconds - left.Seconds,
                scope = left.Scope, stage = left.Progress.Stage, item = left.Progress.Message, next = right.Progress.Message })
                .OrderByDescending(gap => gap.seconds).ToArray();
            File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(new { exit, computeCompleted, saveRequested = config.SaveProject, machine = Environment.MachineName,
                process = process.Id, processors = Environment.ProcessorCount, elapsedSeconds = timer.Elapsed.TotalSeconds,
                cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds, peakWorkingSetBytes = process.PeakWorkingSet64,
                progressSteps = trace.Steps.Count, longestGap = gaps.FirstOrDefault(), topGaps = gaps.Take(12),
                inputUnchanged = before == FileFingerprint.Sha256(input), error = failure?.Message }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("MEASUREMENT COMPLETE exit=" + exit + "; longest=" + JsonSerializer.Serialize(gaps.FirstOrDefault()));
        }
        return exit;
    }
}
