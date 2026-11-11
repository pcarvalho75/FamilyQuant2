using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record QuantifyScreenConfiguration(string Project, string Raw, string Sepro, string DeNovo, string Cache, string Output,
        bool SaveProject = false, int? IsotopeCeiling = null, int? MinimumPoints = null, double? PeaksMinimumAlc = null,
        string? DeNovoMode = null, double? DeepNovoMinimumScore = null, int? MinimumLength = null,
        int? StretchLength = null, double? StretchFloor = null, bool? PatternLabWins = null);

    [TestCommandTable]
    private static IEnumerable<TestCommand> QuantifyScreenRunCommands() =>
    [
        new("--quantify-screen-run", 447, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--quantify-screen-run", (a, _) =>
        {
            if (a.Length != 2) { Console.WriteLine("Use --quantify-screen-run <compute.json>."); return 1; }
            return QuantifyScreenRun(a[1]);
        }),
        new("--test-quantify-screen-run", 447, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--test-quantify-screen-run", (_, _) =>
        {
            QuantifyScreenRunTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static int QuantifyScreenRun(string configuration, Action<FullXicOptions>? syntheticOptions = null)
    {
        string? output = null;
        try
        {
            EnsureApplication();
            var config = JsonSerializer.Deserialize<QuantifyScreenConfiguration>(File.ReadAllText(configuration),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Missing compute configuration.");
            string destination = Path.GetFullPath(config.Output);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Use a fresh output directory.");
            Directory.CreateDirectory(destination); output = destination;
            string input = Path.GetFullPath(config.Project), before = FileFingerprint.Sha256(input);
            var saved = ProjectStore.ReadFqproj(input);
            var model = new ComputeXicViewModel(AppVersionInfo.Current(), saved.manifest, saved.fcf,
                compute: (request, messages, token) => Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    request.Options.CacheDirectory = Path.GetFullPath(config.Cache);
                    request.Options.SpectrumComparisonCsvPath = Path.Combine(destination, "spectrum.csv");
                    syntheticOptions?.Invoke(request.Options);
                    return FullXicComputer.ComputeFullXic(request.RawDirectory, request.SeproPackage, request.DeNovoDirectory,
                        request.Options, progress: message => { token.ThrowIfCancellationRequested(); messages.Report(message); }, operation: request.Operation);
                }, token))
            { RawDirectory = config.Raw, SeproPackage = config.Sepro, DeNovoDirectory = config.DeNovo ?? "" };
            if (config.IsotopeCeiling.HasValue) model.IsotopeCeiling = config.IsotopeCeiling.Value;
            if (config.MinimumPoints.HasValue) model.MinimumPoints = config.MinimumPoints.Value;
            if (config.PeaksMinimumAlc.HasValue) model.Filters.PeaksMinimumAlc = config.PeaksMinimumAlc.Value.ToString("R", CultureInfo.InvariantCulture);
            if (config.DeNovoMode != null) model.Filters.ModeTag = config.DeNovoMode;
            if (config.DeepNovoMinimumScore.HasValue) model.Filters.DeepNovoMinimumScore = config.DeepNovoMinimumScore.Value.ToString("R", CultureInfo.InvariantCulture);
            if (config.MinimumLength.HasValue) model.Filters.MinimumLength = config.MinimumLength.Value.ToString(CultureInfo.InvariantCulture);
            if (config.StretchLength.HasValue) model.Filters.StretchLength = config.StretchLength.Value.ToString(CultureInfo.InvariantCulture);
            if (config.StretchFloor.HasValue) model.Filters.StretchFloor = config.StretchFloor.Value.ToString("R", CultureInfo.InvariantCulture);
            if (config.PatternLabWins.HasValue) model.PatternLabWinsScanConflicts = config.PatternLabWins.Value;
            var settings = new
            {
                DeNovoMode = model.Filters.ModeTag, model.Filters.PeaksMinimumAlc, model.Filters.DeepNovoMinimumScore,
                model.Filters.MinimumLength, model.Filters.StretchLength, model.Filters.StretchFloor,
                IsotopeCeiling = model.IsotopeCount, MinimumPoints = model.MinPointsForQuant, PatternLabWins = model.PatternLabWinsScanConflicts,
            };
            Console.WriteLine("SETTINGS " + JsonSerializer.Serialize(settings));
            var grid = new Grid(); var screen = new QuantifyControl(model); grid.Children.Add(screen);
            var warnings = new List<string>();
            model.PropertyChanged += (_, _) => grid.Children.Contains(screen);
            model.Operations.ProgressReported += (_, item) =>
            {
                grid.Children.Contains(screen);
                if (item.Stage != "denovo-run-warning") return;
                warnings.Add(item.Message); Console.WriteLine("WARNING " + item.Message);
            };
            Task compute = AppOperationHost.OnUi(grid, model.ComputeAsync);
            while (!compute.IsCompleted) { PumpDispatcher(); Thread.Sleep(5); }
            compute.GetAwaiter().GetResult(); PumpDispatcher();
            if (!model.Computed) throw new InvalidOperationException(model.Status);
            foreach (string warning in model.Result!.DeNovoSourceWarnings.Where(warning => !warnings.Contains(warning, StringComparer.Ordinal)))
            { warnings.Add(warning); Console.WriteLine("WARNING " + warning); }
            if (config.SaveProject)
            {
                saved.fcf.ReportInputProvenance = model.CompletedInputProvenance;
                ProjectStore.WriteFqproj(Path.Combine(destination, "computed.fqproj"), saved.fcf, model.Result.Rows, model.Result.Manifest);
            }
            if (before != FileFingerprint.Sha256(input)) throw new InvalidDataException("The input project changed.");
            File.WriteAllText(Path.Combine(destination, "result.json"), JsonSerializer.Serialize(new
            {
                Exit = 0, model.RunSummary, Warnings = warnings, Rows = model.Result.Rows.Count,
                Runs = model.Result.Manifest.PerRunTic.Count, model.IsotopeCount, model.MinPointsForQuant,
                model.Filters.PeaksMinimumAlc, Settings = settings, config.SaveProject, InputUnchanged = true,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("RESULT " + model.RunSummary.Replace('\r', ' ').Replace('\n', ' '));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("RESULT " + error.Message);
            if (output != null) File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            return 1;
        }
    }

    private static void QuantifyScreenRunTests()
    {
        EnsureApplication();
        string root = Path.Combine(Path.GetTempPath(), "quantify-screen-run", Guid.NewGuid().ToString("N"));
        string raw = Path.Combine(root, "raw"), source = Path.Combine(root, "source"), cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(raw); Directory.CreateDirectory(source); Directory.CreateDirectory(cache);
        foreach (string run in new[] { "run01", "run02" }) File.WriteAllText(Path.Combine(raw, run + ".raw"), "Synthetic cache fixture, never an acquisition.");
        File.WriteAllText(Path.Combine(source, "run01.raw.denovo.csv"),
            "Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,local confidence (%)\n1,PEPTIDEK,99,8,500,2,1,100,99 99 99 99 99 99 99 99\n");
        string package = Path.Combine(root, "fixture.sepr2"); CreateShellSeproFixture(Array.Empty<(string, int)>()).Save(package);
        string project = Path.Combine(root, "fixture.fqproj");
        ProjectStore.WriteFqproj(project, new FamilyClusterFile(), Array.Empty<QuantRow>(), new QuantManifest
        {
            PatternLabWinsScanConflicts = false,
            DeNovoInputs = new() { new() { SourceId = DeNovoSource.DeNovoAll.Id, MinimumConfidence = 12 },
                new() { SourceId = DeNovoSource.DeepNovo.Id, MinimumConfidence = 13, SourcePriority = 1 } },
        });
        string hash = TestFileHash.Upper(project);
        var storage = new FullXicStoragePaths(raw, cache); string fingerprint = FileFingerprint.Sha256(Path.Combine(raw, "run01.raw"));
        var extraction = DeNovoXicCache.Load(storage.Extraction("denovo", "run01").WritePath, "run01", DeNovoXicExtractor.ProductionPpm,
            DeNovoXicExtractor.ProductionMs1Tolerance, 6, 5, fingerprint);
        extraction.Put(new() { RunName = "run01", ScanNoMS2 = 1, PrecursorMz = 500, Charge = 2, IsotopeAreas = new double[] { 10, 5, 2, 0, 0, 0 } }); extraction.Save();
        typeof(Ms1EnvelopeDetector).GetMethod("SaveCache", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
            new object[] { storage.Detector("run01").WritePath, new Ms1EnvelopeDetectionResult { RunName = "run01", Ms1ScanCount = 10,
                DetectedAreaByNeutralMass = new double[NeutralMassRanges.Count], NeutralMassBinWidth = NeutralMassRanges.Width }, fingerprint });
        var config = new QuantifyScreenConfiguration(project, raw, package, source, cache, Path.Combine(root, "output"),
            SaveProject: true, IsotopeCeiling: 6, MinimumPoints: 5, PeaksMinimumAlc: 90, DeNovoMode: "peaks-all",
            DeepNovoMinimumScore: 80, MinimumLength: 7, StretchLength: 5, StretchFloor: 90, PatternLabWins: true);
        string json = Path.Combine(root, "compute.json"); File.WriteAllText(json, JsonSerializer.Serialize(config));
        int uiThread = Environment.CurrentManagedThreadId; bool worker = false, requestedSettings = false;
        void SyntheticOptions(FullXicOptions options)
        {
            worker = Environment.CurrentManagedThreadId != uiThread;
            requestedSettings = options.IsotopeCount == 6 && options.MinPointsForQuant == 5 && options.PatternLabWinsScanConflicts
                && options.DeNovoCriteria.MinLength == 7 && options.DeNovoCriteria.StretchLength == 5 && options.DeNovoCriteria.StretchFloor == 90
                && options.DeNovoSelections is { Count: 1 } && options.DeNovoSelections[0].Source == DeNovoSource.DeNovoAll
                && options.DeNovoSelections[0].MinimumConfidence == 90;
            typeof(FullXicOptions).GetProperty("ReadTicArea", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(options, (Func<string, double>)(_ => 1000));
        }
        var console = Console.Out; var printed = new StringWriter(CultureInfo.InvariantCulture); int exit;
        try { Console.SetOut(printed); exit = QuantifyScreenRun(json, SyntheticOptions); }
        finally { Console.SetOut(console); }
        Console.Write(printed.ToString());
        Check("QX4-tool real Quantify screen and cache-backed full engine complete on separate threads", exit == 0 && worker);
        Check("QX4-tool prints each missing-source warning and a successful result line", printed.ToString().Contains("WARNING No de novo calls in the selected source(s) for RAW run run02.", StringComparison.Ordinal)
            && printed.ToString().Contains("RESULT 1 quant rows across 1 runs.", StringComparison.Ordinal));
        using (var effective = JsonDocument.Parse(printed.ToString().Split('\n').Single(line => line.StartsWith("SETTINGS ", StringComparison.Ordinal))[9..]))
        {
            var settings = effective.RootElement;
            Check("QX4-tool-b every screen field overrides saved DeepNovo mode and is printed before compute", requestedSettings
                && settings.GetProperty("DeNovoMode").GetString() == "peaks-all" && settings.GetProperty("PeaksMinimumAlc").GetString() == "90"
                && settings.GetProperty("DeepNovoMinimumScore").GetString() == "80" && settings.GetProperty("MinimumLength").GetString() == "7"
                && settings.GetProperty("StretchLength").GetString() == "5" && settings.GetProperty("StretchFloor").GetString() == "90"
                && settings.GetProperty("IsotopeCeiling").GetInt32() == 6 && settings.GetProperty("MinimumPoints").GetInt32() == 5
                && settings.GetProperty("PatternLabWins").GetBoolean()
                && printed.ToString().IndexOf("SETTINGS ", StringComparison.Ordinal) < printed.ToString().IndexOf("WARNING ", StringComparison.Ordinal));
        }
        if (exit == 0)
        {
            var result = ProjectStore.ReadFqproj(Path.Combine(config.Output, "computed.fqproj"));
            Check("QX4-tool optional settings and SaveProject reach the actual stored result", result.quant.Count == 1
                && result.manifest.IsotopeCount == 6 && result.manifest.MinPointsForQuant == 5 && result.manifest.DeNovoInputs.All(row => row.MinimumConfidence == 90));
        }
        else Check("QX4-tool optional settings and SaveProject reach the actual stored result", false);
        config = config with { Output = Path.Combine(root, "invalid"), SaveProject = false, PeaksMinimumAlc = 101 };
        File.WriteAllText(json, JsonSerializer.Serialize(config));
        int refused = QuantifyScreenRun(json, SyntheticOptions);
        Check("QX4-tool validation failure returns one and never saves or replaces a project", refused == 1
            && !File.Exists(Path.Combine(config.Output, "computed.fqproj")) && TestFileHash.Upper(project) == hash);
    }
}
