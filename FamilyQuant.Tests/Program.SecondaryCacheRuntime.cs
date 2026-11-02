using FamilyQuant.DeNovo;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void SecondaryCacheRuntime(Assembly legacy, string output, bool negative, SecondaryAdd add)
    {
        var sample = new Ms1EnvelopeDetectionResult { RunName = "native-secondary", DetectedEnvelopeCount = 7,
            DetectedPeptideLikeArea = 123.125, Ms1ScanCount = 19, DetectedAreaMultiplyCharged = 90,
            DetectedAreaSinglyCharged = 33.125, DetectedCountMultiplyCharged = 5, DetectedCountSinglyCharged = 2 };
        sample.DetectedAreaByCharge[1] = 33.125; sample.DetectedAreaByCharge[2] = 90;
        sample.DetectedCountByCharge[1] = 2; sample.DetectedCountByCharge[2] = 5;
        Type oldResultType = legacy.GetType("FamilyQuant.DeNovo.Ms1EnvelopeDetectionResult", true)!;
        object oldSample = JsonSerializer.Deserialize(JsonSerializer.Serialize(sample), oldResultType)!;
        Type oldDetector = legacy.GetType("FamilyQuant.DeNovo.Ms1EnvelopeDetector", true)!;
        string beforePath = Path.Combine(output, "frozen-detector.json"), afterPath = Path.Combine(output, "current-detector.json");
        using var observed = new ManualResetEventSlim();
        using var watcher = new FileSystemWatcher(output) { Filter = Path.GetFileName(afterPath) + ".tmp", NotifyFilter = NotifyFilters.FileName };
        watcher.Created += (_, _) => observed.Set(); watcher.EnableRaisingEvents = true;
        SecondaryCall(null, oldDetector, "SaveCache", beforePath, oldSample, "native-source-fingerprint");
        SecondaryCall(null, typeof(Ms1EnvelopeDetector), "SaveCache", afterPath, sample, "native-source-fingerprint");
        bool temporaryObserved = observed.Wait(TimeSpan.FromSeconds(3));
        using var before = JsonDocument.Parse(File.ReadAllText(beforePath));
        using var after = JsonDocument.Parse(File.ReadAllText(afterPath));
        object?[] loadArgs = { afterPath, sample.RunName, "native-source-fingerprint", null };
        bool loaded = (bool)typeof(Ms1EnvelopeDetector).GetMethod("TryLoadCache", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, loadArgs)!;
        bool rowValues = loaded && JsonSerializer.Serialize((Ms1EnvelopeDetectionResult)loadArgs[3]!) == JsonSerializer.Serialize(sample);
        object?[] wrongArgs = { afterPath, sample.RunName, "other-source-fingerprint", null };
        bool staleRefused = !(bool)typeof(Ms1EnvelopeDetector).GetMethod("TryLoadCache", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, wrongArgs)!;
        add("FamilyQuant/DeNovo/Ms1EnvelopeDetector.cs", 646, 646, "native cache stages .tmp, publishes readable JSON and removes temporary file",
            new { TempObserved = true, Clean = true, Loaded = true }, new { TempObserved = temporaryObserved, Clean = !File.Exists(afterPath + ".tmp"), Loaded = loaded },
            temporaryObserved && !File.Exists(afterPath + ".tmp") && loaded, "Ms1EnvelopeDetector.SaveCache / TryLoadCache");
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        foreach (var row in inventory.Observations.Where(row => row.Source == "FamilyQuant/DeNovo/Ms1EnvelopeDetector.cs" && row.Line is >= 661 and <= 682))
        {
            string key = JsonSerializer.Deserialize<string>(row.Detail)!;
            object? Value(JsonElement element) => element.ValueKind switch
            {
                JsonValueKind.Number => element.GetDouble(), JsonValueKind.String => element.GetString(),
                JsonValueKind.Array => element.EnumerateArray().Select(Value).ToArray(),
                JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null,
                _ => throw new InvalidDataException("Unsupported cache value kind."),
            };
            string oldValue = JsonSerializer.Serialize(Value(before.RootElement.GetProperty(key)));
            string newValue = JsonSerializer.Serialize(Value(after.RootElement.GetProperty(key)));
            if (negative && key == "runName") newValue = "wrong-negative-control-run";
            add(row.Source, row.Line, row.Line, "native detector JSON key/decoded value and provenance-checked load: " + key,
                oldValue, newValue, oldValue == newValue && rowValues && staleRefused, "Ms1EnvelopeDetector.CacheFile." + key);
        }
    }
}
