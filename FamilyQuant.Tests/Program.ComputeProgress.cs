using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ComputeProgressCommands() =>
    [new("--test-compute-progress", 445, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-compute-progress", (args, _) =>
        {
            ComputeProgressTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.ComputeProgress"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static Dictionary<string, byte[]> ComputeProgressSnapshot(string path, FamilyClusterFile file, FullXicComputeResult result)
    {
        ProjectStore.WriteFqproj(path, file, result.Rows, result.Manifest);
        using var zip = ZipFile.OpenRead(path);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in new[] { ProjectStore.QuantEntryName, ProjectStore.SpectrumComparisonEntryName })
        {
            using var stream = zip.GetEntry(name)!.Open(); using var content = new GZipStream(stream, CompressionMode.Decompress);
            using var bytes = new MemoryStream(); content.CopyTo(bytes); entries[name] = bytes.ToArray();
        }
        using var reader = new StreamReader(zip.GetEntry(ProjectStore.ManifestEntryName)!.Open());
        var manifest = JsonNode.Parse(reader.ReadToEnd())!;
        // These two wall-clock fields are volatile by design; all scientific/study/source fields participate.
        manifest[nameof(QuantManifest.CreatedUtc)] = "<compute time>"; manifest[nameof(QuantManifest.UpdatedUtc)] = "<compute time>";
        entries[ProjectStore.ManifestEntryName] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        return entries;
    }

    private static void ComputeProgressTests(string folder)
    {
        var oldCulture = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string language in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
                ComputeProgressFixture(Path.Combine(folder, language));
                ComputeLongStudyProgressTests();
            }
        }
        finally { CultureInfo.CurrentCulture = oldCulture; CultureInfo.CurrentUICulture = oldUi; }
    }

    private sealed class ComputeProgressCollector : IProgress<OperationProgress>
    {
        public List<OperationProgress> Items { get; } = new();
        public void Report(OperationProgress value) => Items.Add(value);
    }

    private static void ComputeLongStudyProgressTests()
    {
        var rows = Enumerable.Range(1, 260).Select(index => Qr("identified", "reference_" + index.ToString(CultureInfo.InvariantCulture),
            index, 2, "ACDEFGHIK", 10 + index, 20 + index)).ToArray();
        var peptides = NativeQuantFractions.DedupToPeptides(rows);
        string before = ReferenceEnvelopeEstimator.ToCsv(ReferenceEnvelopeEstimator.Evaluate(peptides));
        var progress = new ComputeProgressCollector(); var operation = new OperationContext(progress: progress);
        string after = ReferenceEnvelopeEstimator.ToCsv(ReferenceEnvelopeEstimator.Evaluate(peptides, operation));
        bool Batches(string stage)
        {
            var items = progress.Items.Where(item => item.Stage == stage).ToArray();
            return items.Length == 4 && items[0].Completed == 0 && items.All(item => item.Total == 260)
                && items.Skip(1).Select(item => item.Completed).SequenceEqual(new long?[] { 128, 256, 260 });
        }
        Check("J35 reference diagnostics updates every representative batch without changing any diagnostic byte", Batches("reference-validation") && before == after);
        string original = SpectrumComparisonTable.ToCsv(SpectrumComparisonTable.Build(Array.Empty<PeaksDeNovoRow>(), Array.Empty<PeaksDeNovoRow>(),
            Array.Empty<PatternLabPsm>(), rows));
        progress.Items.Clear();
        string counted = SpectrumComparisonTable.ToCsv(SpectrumComparisonTable.Build(Array.Empty<PeaksDeNovoRow>(), Array.Empty<PeaksDeNovoRow>(),
            Array.Empty<PatternLabPsm>(), rows, operation: operation));
        Check("T3 spectrum comparison reports row assembly without fitting the removed primary model",
            !progress.Items.Any(p => p.Stage == "spectrum-reference-validation") && Batches("spectrum-rows") && original == counted);
    }

    private static void ComputeProgressFixture(string folder)
    {
        EnsureApplication(); Directory.CreateDirectory(folder);
        string raw = Path.Combine(folder, "raw"), exports = Path.Combine(folder, "denovo"), cache = Path.Combine(folder, "cache");
        Directory.CreateDirectory(raw); Directory.CreateDirectory(exports); Directory.CreateDirectory(cache);
        string package = Path.Combine(folder, "synthetic.sepr2"); CreateShellSeproFixture(Array.Empty<(string, int)>()).Save(package);
        var file = TaskFile();
        using (var taxonomy = new TaskTaxonomyHandler())
        using (var handler = new WorldFetchHandler())
        {
            var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, taxonomy).GetAwaiter().GetResult();
            var choice = EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
            CoreOperation.RunAsync("Fixture world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, choice, context, handler)).GetAwaiter().GetResult();
        }
        foreach (string run in new[] { "progress_a", "progress_b" })
        {
            string input = Path.Combine(raw, run + ".raw"); File.WriteAllText(input, "Owned synthetic cache marker; never parsed as an acquisition.");
            string fingerprint = FileFingerprint.Sha256(input);
            var storage = new FullXicStoragePaths(raw, cache);
            var xic = DeNovoXicCache.Load(storage.Extraction("denovo", run).WritePath, run, DeNovoXicExtractor.ProductionPpm,
                DeNovoXicExtractor.ProductionMs1Tolerance, 6, 3, fingerprint);
            var csv = new StringBuilder("Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,local confidence (%)\n");
            for (int i = 1; i <= 260; i++)
            {
                string sequence = i % 2 == 0 ? "ACDEFGHIK" : "WWWWWWWWWWK";
                csv.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',').Append(sequence).Append(",99,")
                    .Append(sequence.Length.ToString(CultureInfo.InvariantCulture)).Append(",500.25,2,12.5,100,")
                    .Append(string.Join(" ", Enumerable.Repeat("99", sequence.Length))).Append('\n');
                xic.Put(new() { RunName = run, ScanNoMS2 = i, PrecursorMz = 500.25, Charge = 2,
                    IsotopeAreas = new double[] { 10, 5, 2, 0, 0, 0 } });
            }
            xic.Save(); File.WriteAllText(Path.Combine(exports, run + ".raw.denovoOnly.csv"), csv.ToString());
            typeof(Ms1EnvelopeDetector).GetMethod("SaveCache", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                new object[] { storage.Detector(run).WritePath, new Ms1EnvelopeDetectionResult { RunName = run, Ms1ScanCount = 10,
                    DetectedEnvelopeCount = 1, DetectedPeptideLikeArea = 50 }, fingerprint });
        }
        FullXicComputeResult Engine(FullXicOptions options, Action<string>? messages, OperationContext? operation)
        {
            options.CacheDirectory = cache; options.ExportSpectrumComparisonCsv = false;
            typeof(FullXicOptions).GetProperty("ReadTicArea", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(options, (Func<string, double>)(_ => 1000));
            MethodInfo method = typeof(FullXicComputer).GetMethod(nameof(FullXicComputer.ComputeFullXic))!;
            object?[] args = { raw, package, exports, options, null, messages, null, null };
            if (method.GetParameters().Length > args.Length) args = args.Append((object?)operation).ToArray();
            try { return (FullXicComputeResult)method.Invoke(null, args)!; }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        FullXicOptions Options() => new() { AppVersion = "Synthetic progress fixture", FamilyCuration = FamilyClusterFile.FromJsonText(file.ToJsonText()),
            DeNovoSelections = new[] { new DeNovoSourceSelection(DeNovoSource.DenovoOnly, 80, 0) } };
        var baseline = Engine(Options(), null, null);
        var expected = ComputeProgressSnapshot(Path.Combine(folder, "baseline.fqproj"), file, baseline);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int pause = 1;
        var model = new ComputeXicViewModel("Synthetic progress fixture", familyCuration: FamilyClusterFile.FromJsonText(file.ToJsonText()),
            compute: (request, messages, token) => Task.Run(() => Engine(request.Options, messages.Report,
                new OperationContext(token, new InlineOperationProgress(item =>
        {
            request.Operation!.Report(item.Stage, item.Message, item.Completed, item.Total);
            if (item.Stage == "compute-study" && item.Completed == 1 && Interlocked.Exchange(ref pause, 0) == 1)
            { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Native compute fixture was not released."); }
        }))), token))
        { RawDirectory = raw, SeproPackage = package, DeNovoDirectory = exports };
        var window = OffscreenProofWindow(new Window { Content = new QuantifyControl(model), Width = 1024, Height = 768 });
        var trace = new ConcurrentQueue<OperationProgress>();
        model.Operations.ProgressReported += (_, item) => trace.Enqueue(item);
        var oldContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            window.Show(); PumpDispatcher(); Task work = model.ComputeAsync();
            var watch = Stopwatch.StartNew();
            while (!entered.IsSet && !work.IsCompleted && watch.Elapsed < TimeSpan.FromMinutes(2)) { PumpDispatcher(); Thread.Sleep(10); }
            PumpDispatcher();
            var overlay = WalkTree(window).OfType<BusyOverlay>().Single();
            Check("J35 native Compute pauses at a counted stored study", entered.IsSet && model.Operations.IsBusy && !model.Operations.IsIndeterminate);
            if (entered.IsSet)
            {
                var elapsed = (TextBlock)overlay.FindName("OperationElapsed"); var since = (TextBlock)overlay.FindName("OperationSinceLastStep");
                string beforeElapsed = elapsed.Text, beforeSince = since.Text;
                watch.Restart(); while (watch.Elapsed < TimeSpan.FromSeconds(1.25)) { PumpDispatcher(); Thread.Sleep(10); }
                Check("J35 Compute shows current study, determinate bar and two advancing clocks", elapsed.Text != beforeElapsed && since.Text != beforeSince
                    && ((ProgressBar)overlay.FindName("OperationProgressBar")).IsIndeterminate == false
                    && ((TextBlock)overlay.FindName("OperationCurrentItem")).Text.Contains("Derived fractions", StringComparison.Ordinal));
                ScreenHarness.Save(window, Path.Combine(folder, "counted-compute.png"), 1024, 768);
            }
            else Check("J35 Compute shows current study, determinate bar and two advancing clocks", false);
            release.Set(); FinishBusyTask(work); work.GetAwaiter().GetResult(); PumpDispatcher();
            Check("J35 native Compute finishes the real cache-backed fixture pipeline", model.Result is { Rows.Count: > 0 } && !model.IsBusy);
            var steps = trace.ToArray();
            void Series(string stage, int count)
            {
                var all = steps.Where(item => item.Stage == stage).ToArray();
                Check("J35 " + stage + " has known totals and each ordered item once", all.Length > 0 && all[0].Completed == 0 && all[0].Total == count
                    && all.Where(item => item.Completed > 0).Select(item => item.Completed!.Value).SequenceEqual(Enumerable.Range(1, count).Select(value => (long)value))
                    && all.All(item => item.Total == count));
            }
            Series("compute-phase", 5); Series("compute-run", 2); Series("compute-study", 13); Series("source-study", 10); Series("world-phase", 5);
            Check("J35 every counted Compute phase reaches its declared total", steps.Where(item => item.Total > 0).GroupBy(item => item.Stage).All(group =>
                group.First().Completed == 0 && group.Last().Completed == group.First().Total && group.All(item => item.Total == group.First().Total)
                && group.Zip(group.Skip(1), (previous, next) => next.Completed >= previous.Completed).All(value => value)));
            foreach (string run in new[] { "progress_a", "progress_b" })
            {
                var batches = steps.Where(item => item.Stage == "precursor-cache:denovo:" + run).ToArray();
                Check("J35 " + run + " precursor cache batches include every cached request", batches.Length >= 4 && batches[0].Completed == 0
                    && batches.All(item => item.Total == 260) && batches[^1].Completed == 260
                    && batches.Skip(1).Zip(batches, (next, previous) => next.Completed > previous.Completed).All(value => value));
            }
            Check("J35 de novo rows and family-world phases expose counts", steps.Any(item => item.Stage.StartsWith("denovo-rows:", StringComparison.Ordinal)
                && item.Total == 260 && item.Completed == 260) && steps.Any(item => item.Stage == "world-phase" && item.Total == 5 && item.Completed == 5));
            string? logPath = typeof(ComputeXicViewModel).GetProperty("OperationLogPath")?.GetValue(model) as string;
            string log = logPath != null && File.Exists(logPath) ? File.ReadAllText(logPath) : "";
            Check("J35 Compute records every counted step in a timestamped persistent log", steps.Length > 0 && log.Length > 0
                && steps.Where(item => item.Total.HasValue).All(item => log.Contains(item.Stage + "\t", StringComparison.Ordinal) && log.Contains(item.Message, StringComparison.Ordinal))
                && log.Split('\n', StringSplitOptions.RemoveEmptyEntries).All(line => DateTimeOffset.TryParse(line.Split('\t')[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out _)));
            if (logPath != null && File.Exists(logPath)) File.Copy(logPath, Path.Combine(folder, "compute-progress.log"), true);
            if (model.Result != null)
            {
                var actual = ComputeProgressSnapshot(Path.Combine(folder, "computed.fqproj"), file, model.Result);
                Check("J35 native Compute preserves every stored quant, spectrum and study byte", expected.All(pair => pair.Value.SequenceEqual(actual[pair.Key])));
            }
            else Check("J35 native Compute preserves every stored quant, spectrum and study byte", false);
        }
        finally
        {
            release.Set(); if (model.Operations.IsBusy) { model.Operations.Cancel(); FinishBusyOperation(model.Operations); }
            window.Close(); SynchronizationContext.SetSynchronizationContext(oldContext);
        }
        ComputeProgressOpenTests(folder, Path.Combine(folder, "computed.fqproj"));
    }

    private static void ComputeProgressOpenTests(string folder, string project)
    {
        if (!File.Exists(project)) { Check("J35 native open fixture has computed Results", false); return; }
        string before = FileFingerprint.Sha256(project), hits = Path.Combine(folder, "open-hits");
        var oldSettings = MainWindow.ShellSettingsPathOverride; var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldMessages = ShellMessages.Presenter; var oldContext = SynchronizationContext.Current;
        MainWindow.ShellSettingsPathOverride = Path.Combine(folder, "open-preferences.json");
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        var main = OffscreenProofWindow(new MainWindow { Width = 1024, Height = 768 });
        var trace = new ConcurrentQueue<OperationProgress>();
        main.Operations.ProgressReported += (_, item) => trace.Enqueue(item);
        main.Operations.PropertyChanged += (_, _) =>
        {
            if (!main.Operations.IsBusy || main.Operations.Title != "Attribute full XIC to families") return;
            var screen = ((Grid)main.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().SingleOrDefault();
            if (screen != null) typeof(CurationQuantitationViewModel).GetField("fullXicCacheDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(screen.ViewModel.Quantitation, hits);
        };
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
            main.Show(); PumpDispatcher();
            ClusterGenerationControl Open()
            {
                var task = main.OpenProjectSafelyAsync(project); FinishBusyTask(task);
                if (!task.GetAwaiter().GetResult()) throw new InvalidOperationException("Owned computed fixture did not open.");
                var screen = ((Grid)main.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
                FinishBusyTask(screen.FullXicReady); FinishBusyOperation(main.Operations); PumpDispatcher(); return screen;
            }
            var first = Open(); var searched = trace.Where(item => item.Stage == "attribution-denovo").ToArray();
            Check("J35 every counted native open phase reaches its declared total", trace.Where(item => item.Total > 0).GroupBy(item => item.Stage).All(group =>
                group.First().Completed == 0 && group.Last().Completed == group.First().Total && group.All(item => item.Total == group.First().Total)));
            Check("J35 native first open counts searched peptides against the total known before searching", first.HasFullXic
                && searched.Length >= 2 && searched[0].Completed == 0 && searched[0].Total > 0
                && searched.All(item => item.Total == searched[0].Total) && searched[^1].Completed == searched[0].Total);
            string? composition = first.FullXicCompositionCsv();
            string? firstLog = typeof(MainWindow).GetProperty("ProjectOpenLogPath")?.GetValue(main) as string;
            Check("J35 native open log covers both file reading and attribution", firstLog != null && File.Exists(firstLog)
                && File.ReadAllText(firstLog).Contains("\tproject-read\t0\t1\t", StringComparison.Ordinal)
                && File.ReadAllText(firstLog).Contains("\tattribution-denovo\t", StringComparison.Ordinal));
            if (firstLog != null && File.Exists(firstLog)) File.Copy(firstLog, Path.Combine(folder, "open-search.log"), true);
            while (trace.TryDequeue(out _)) { }
            var second = Open();
            Check("J35 native reopen reports verified cached hits with a known total and skips the search", second.HasFullXic
                && trace.Any(item => item.Stage == "attribution-cache" && item.Completed == 1 && item.Total == 1
                    && item.Message.Contains("read from the cache", StringComparison.Ordinal))
                && !trace.Any(item => item.Stage == "attribution-denovo"));
            Check("J35 native cached reopen preserves attributed numbers and the input project bytes", composition == second.FullXicCompositionCsv()
                && before == FileFingerprint.Sha256(project));
            string? secondLog = typeof(MainWindow).GetProperty("ProjectOpenLogPath")?.GetValue(main) as string;
            if (secondLog != null && File.Exists(secondLog)) File.Copy(secondLog, Path.Combine(folder, "open-cache.log"), true);
        }
        finally
        {
            if (main.Operations.IsBusy) { main.Operations.Cancel(); FinishBusyOperation(main.Operations); }
            main.Close(); MainWindow.ShellSettingsPathOverride = oldSettings; MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            ShellMessages.Presenter = oldMessages; SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }
}
