using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DeNovoSwitchFallbackCommands() =>
    [new("--test-denovo-switch-fallback", 100, TestCommandPhase.Raw,
        args => args[0] == "--test-denovo-switch-fallback", (args, _) =>
        {
            DeNovoSwitchFallbackTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "denovo-switch-fallback"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void DeNovoSwitchFallbackTests(string output)
    {
        string root = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>
        {
            new() { SequenceIdentifier = "entry_one", Description = "First protein", Sequence = "MACDEFGHIKLMN", UserID = 1 },
            new() { SequenceIdentifier = "entry_two", Description = "Second protein", Sequence = "MVVVVVVVKQ", UserID = 2 },
        } };
        var rows = new List<QuantRow>
        {
            Qr("identified", "run01", 1, 2, "ACDEFGHIK", 10, 20),
            Qr("denovo", "run01", 2, 2, "VVVVVVVK", 5, 15),
            Qr("denovo", "run01", 3, 2, "TTTTTTTK", 5, 40),
        };
        var options = new HomologyOptions();
        var mapping = FamilyEnvelopeQuant.BuildAttributionFromCuration(rows, file, options);
        var manifest = new QuantManifest { IsotopeCount = 6, DerivedMethod = NativeQuantFractions.Method,
            PerRunTic = new() { ["run01"] = 1000 }, FamilyMapping = FamilyEnvelopeQuant.ProvenanceFor(file, options.MinIdentity),
            SpectrumComparisonRows = SpectrumComparisonTable.Build(Array.Empty<PeaksDeNovoRow>(), Array.Empty<PeaksDeNovoRow>(),
                Array.Empty<PatternLabPsm>(), rows, familyAttribution: mapping) };
        var stored = CompareStoredDeNovoRules(rows, manifest, file, Path.Combine(root, "unused-cache"));
        Check("T3b complete stored family assignments need neither a cache nor homology search",
            !stored.HomologySearchPerformed && stored.FamilySharesAvailable && stored.FamilyAttribution == "complete stored family assignments"
            && stored.RecoveryPerRun.Single().OldModelRecovery == .04 && stored.RecoveryPerRun.Single().NewMeasuredRecovery == .075);

        manifest.SpectrumComparisonRows.Clear();
        string cold = Path.Combine(root, "cold-cache");
        var order = new List<string>();
        var fresh = CompareStoredDeNovoRules(rows, manifest, file, cold, _ => order.Add("recovery"), order.Add);
        Check("T3b missing stored assignments use the program's in-memory attribution after reporting recovery",
            fresh.FamilySharesAvailable && fresh.HomologySearchPerformed && fresh.HomologySearchSeconds >= 0
            && order[0] == "recovery" && order.Any(s => s.Contains("seconds", StringComparison.Ordinal)));
        Check("T3b fresh attribution reproduces the stored-path family shares under both quantitation rules",
            JsonSerializer.Serialize(fresh.OldModelTop15) == JsonSerializer.Serialize(stored.OldModelTop15)
            && JsonSerializer.Serialize(fresh.NewMeasuredTop15) == JsonSerializer.Serialize(stored.NewMeasuredTop15)
            && fresh.OldModelTop15[0].ShareOfAssignedSignal == 20d / 30
            && fresh.NewMeasuredTop15[0].ShareOfAssignedSignal == 20d / 35);
        Check("T3b an unavoidable search stays in memory and writes no hit cache", !Directory.Exists(cold));

        string project = Path.Combine(root, "missing-attribution.fqproj");
        ProjectStore.WriteFqproj(project, file, rows, manifest);
        var loaded = ProjectStore.ReadFqproj(project);
        string cache = Path.Combine(root, "retained-hits");
        CurationHomologyCache.GetOrSearch(loaded.quant, loaded.fcf, cache, options);
        var cachedFiles = Directory.GetFiles(cache, "*.fqhits").ToDictionary(p => p, FileFingerprint.Sha256);
        var reused = CompareStoredDeNovoRules(loaded.quant, loaded.manifest, loaded.fcf, cache);
        Check("T3b complete verified retained homology hits are reused without another search or cache write",
            !reused.HomologySearchPerformed && reused.FamilyAttribution.StartsWith("verified retained", StringComparison.Ordinal)
            && cachedFiles.All(p => p.Value == FileFingerprint.Sha256(p.Key))
            && Directory.GetFiles(cache, "*.fqhits").Length == cachedFiles.Count);

        string hash = FileFingerprint.Sha256(project);
        string receipt = Path.Combine(root, "receipt");
        var oldOut = Console.Out; var console = new StringWriter(CultureInfo.InvariantCulture);
        int exit;
        try
        {
            Console.SetOut(console);
            exit = DeNovoSwitchCompare(new[] { "--denovo-switch-compare", project, receipt, "--homology-cache", cache });
        }
        finally { Console.SetOut(oldOut); }
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(receipt, "switch-comparison.json")));
        Check("T3b public command prints recovery and cached family shares without changing the project",
            exit == 0 && hash == FileFingerprint.Sha256(project)
            && File.Exists(Path.Combine(receipt, "recovery.json"))
            && console.ToString().StartsWith("Recovery per run:", StringComparison.Ordinal)
            && json.RootElement.GetProperty("FamilySharesAvailable").GetBoolean()
            && !json.RootElement.GetProperty("HomologySearchPerformed").GetBoolean()
            && !console.ToString().Contains("ACDEFGHIK", StringComparison.Ordinal)
            && !console.ToString().Contains("entry_one", StringComparison.Ordinal));

        string freshReceipt = Path.Combine(root, "fresh-receipt");
        console = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(console);
            exit = DeNovoSwitchCompare(new[] { "--denovo-switch-compare", project, freshReceipt, "--homology-cache", cold });
        }
        finally { Console.SetOut(oldOut); }
        using var freshJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(freshReceipt, "switch-comparison.json")));
        Check("T3b public command times an unavoidable search in memory and preserves project and cache paths",
            exit == 0 && hash == FileFingerprint.Sha256(project) && !Directory.Exists(cold)
            && console.ToString().Contains("In-memory homology search:", StringComparison.Ordinal)
            && freshJson.RootElement.GetProperty("HomologySearchPerformed").GetBoolean()
            && freshJson.RootElement.GetProperty("HomologySearchSeconds").GetDouble() >= 0);

        string noFamilies = Path.Combine(root, "no-families.fqproj");
        ProjectStore.WriteFqproj(noFamilies, new FamilyClusterFile(), rows, manifest);
        string noFamilyHash = FileFingerprint.Sha256(noFamilies);
        string unavailable = Path.Combine(root, "unavailable");
        console = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(console);
            exit = DeNovoSwitchCompare(new[] { "--denovo-switch-compare", noFamilies, unavailable, "--homology-cache", cold });
        }
        finally { Console.SetOut(oldOut); }
        using var partial = JsonDocument.Parse(File.ReadAllText(Path.Combine(unavailable, "switch-comparison.json")));
        Check("T3b recovery still prints and persists when family shares are unavailable",
            exit == 1 && noFamilyHash == FileFingerprint.Sha256(noFamilies)
            && console.ToString().StartsWith("Recovery per run:", StringComparison.Ordinal)
            && JsonDocument.Parse(File.ReadAllText(Path.Combine(unavailable, "recovery.json"))).RootElement[0]
                .GetProperty("NewMeasuredRecovery").GetDouble() == .075
            && !partial.RootElement.GetProperty("FamilySharesAvailable").GetBoolean()
            && partial.RootElement.GetProperty("SharedFeatures").ValueKind == JsonValueKind.Null);
    }
}
