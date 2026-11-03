using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FullXicStorageCommands() =>
    [
        new("--test-fullxic-storage", 370, TestCommandPhase.Raw,
            args => args.Contains("--test-fullxic-storage", StringComparer.Ordinal), (_, _) =>
            {
                FullXicStorageTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void FullXicStorageTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.Storage", Guid.NewGuid().ToString("N"));
        string raw = Path.Combine(root, "raw"), cacheRoot = Path.Combine(root, "output");
        Directory.CreateDirectory(raw);
        var standard = new FullXicStoragePaths(raw);
        Check("default full-XIC output is per-user and outside acquisition inputs",
            !FullXicStoragePaths.IsInRawDirectory(standard.OutputDirectory, raw)
            && standard.OutputDirectory.StartsWith(ApplicationPaths.LocalDataDirectory, StringComparison.OrdinalIgnoreCase));
        var paths = new FullXicStoragePaths(raw, cacheRoot);
        Check("different acquisition folders cannot collide on the same run filename",
            paths.OutputDirectory != new FullXicStoragePaths(Path.Combine(root, "other-raw"), cacheRoot).OutputDirectory);
        var location = paths.Extraction("identified", "fixture");
        string legacy = Path.Combine(raw, FullXicComputer.CacheFolderName, "fullxic_identified_fixture.json");
        Check("legacy extraction sidecars remain an explicit read source", location.LegacyReadPath == legacy);
        string outside = Path.Combine(root, "isolated.json");
        var explicitPath = paths.Extraction("identified", "fixture", outside);
        Check("explicit external caches retain isolation and do not silently read legacy caches",
            explicitPath.WritePath == outside && explicitPath.LegacyReadPath == null);
        var unsafePath = paths.Extraction("identified", "fixture", Path.Combine(raw, "old-custom.json"));
        Check("an explicit acquisition-side cache can be read but is never a write destination",
            !FullXicStoragePaths.IsInRawDirectory(unsafePath.WritePath, raw)
            && unsafePath.LegacyReadPath == Path.Combine(raw, "old-custom.json"));
        Check("a selected cache root inside acquisition inputs is refused", Throws<ArgumentException>(() =>
            new FullXicStoragePaths(raw, Path.Combine(raw, "outputs"))));
        Check("path containment respects directory boundaries",
            !FullXicStoragePaths.IsInRawDirectory(Path.Combine(root, "raw-other", "file.csv"), raw));
        const string fingerprint = "owned-fixture-content";
        double ppm = DeNovoXicExtractor.ProductionPpm;
        int tolerance = DeNovoXicExtractor.ProductionMs1Tolerance;
        int isotopes = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount;
        int points = DeNovoXicExtractor.DefaultMinPointsForQuant;
        var old = DeNovoXicCache.Load(legacy, "fixture", ppm, tolerance, isotopes, points, fingerprint);
        old.Put(new() { RunName = "fixture", ScanNoMS2 = 1, PrecursorMz = 500, Charge = 2, IsotopeAreas = new double[] { 12, 6, 0, 0, 0, 0 } });
        old.Save();
        byte[] original = SHA256.HashData(File.ReadAllBytes(legacy));
        var imported = DeNovoXicCache.Load(location.WritePath, "fixture", ppm, tolerance, isotopes, points, fingerprint, legacy);
        Check("validated old sidecars answer new storage paths without changing their numbers",
            imported.Count == 1 && imported.Path == location.WritePath
            && imported.TryGet(1, 500, 2, out var hit) && hit?.AreaM0 == 12);
        var modern = DeNovoXicCache.Load(location.WritePath, "fixture", ppm, tolerance, isotopes, points, fingerprint);
        modern.Put(new() { RunName = "fixture", ScanNoMS2 = 2, PrecursorMz = 600, Charge = 2, IsotopeAreas = new double[] { 20, 10, 0, 0, 0, 0 } });
        modern.Save();
        imported = DeNovoXicCache.Load(location.WritePath, "fixture", ppm, tolerance, isotopes, points, fingerprint, legacy);
        Check("partial modern and legacy caches can supply distinct validated requests", imported.Count == 2);
        imported.Put(new() { RunName = "fixture", ScanNoMS2 = 3, PrecursorMz = 700, Charge = 2, IsotopeAreas = new double[] { 30, 15, 0, 0, 0, 0 } });
        imported.Save();
        Check("new cache entries are saved outside RAW and leave the legacy sidecar unchanged",
            original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(legacy))) && File.Exists(location.WritePath));
        Check("legacy fallbacks still reject mismatched content and extraction settings",
            DeNovoXicCache.Load(location.WritePath, "fixture", ppm, tolerance, isotopes, points, "different", legacy).Count == 0
            && DeNovoXicCache.Load(location.WritePath, "fixture", ppm + 1, tolerance, isotopes, points, fingerprint, legacy).Count == 0);
        var detected = new Ms1EnvelopeDetectionResult { RunName = "fixture", DetectedEnvelopeCount = 1, DetectedPeptideLikeArea = 123 };
        detected.DetectedAreaByCharge[2] = 123;
        detected.DetectedCountByCharge[2] = 1;
        detected.DetectedAreaMultiplyCharged = 123;
        detected.DetectedCountMultiplyCharged = 1;
        var detectorPaths = paths.Detector("fixture");
        typeof(Ms1EnvelopeDetector).GetMethod("SaveCache", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { detectorPaths.LegacyReadPath!, detected, fingerprint });
        Ms1EnvelopeDetectionResult? recovered = null;
        try { recovered = Ms1EnvelopeDetector.DetectRun(Path.Combine(raw, "fixture.raw"), detectorPaths.WritePath,
            sourceFingerprint: fingerprint, legacyCachePath: detectorPaths.LegacyReadPath); }
        catch (FileNotFoundException) { }
        Check("the detector can reuse a valid read-only legacy result without opening RAW", recovered?.DetectedPeptideLikeArea == 123);
        Check("default spectrum CSV is outside acquisition inputs", !FullXicStoragePaths.IsInRawDirectory(paths.SpectrumComparisonCsvPath, raw));
        var table = new FullXicComputeResult();
        table.Rows.Add(Qr("identified", "fixture", 1, 2, "PEPTIDEK", 1, 3));
        table.SpectrumComparisonRows.Add(new() { FileName = "fixture", NormalizedRunName = "fixture", ScanNumber = 1, Charge = 2 });
        Check("automatic explicit CSV inside RAW is refused without losing the table",
            !table.TryExportSpectrumComparisonCsvOutsideRaw(Path.Combine(raw, "refused.csv"), raw)
            && !File.Exists(Path.Combine(raw, "refused.csv")) && table.SpectrumComparisonCsvError.Contains("outside", StringComparison.Ordinal)
            && table.Rows.Count == 1 && table.SpectrumComparisonRows.Count == 1);
        Check("automatic CSV creates writable per-user/project output directories",
            table.TryExportSpectrumComparisonCsvOutsideRaw(paths.SpectrumComparisonCsvPath, raw)
            && File.Exists(paths.SpectrumComparisonCsvPath));
    }
}
