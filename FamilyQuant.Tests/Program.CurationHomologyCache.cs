using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationHomologyCacheCommands() =>
    [new("--test-curation-homology-cache", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-curation-homology-cache", (_, _) =>
        {
            CurationHomologyCacheTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void CurationHomologyCacheTests()
    {
        Section("J28 persisted curation homology hits");
        var type = typeof(FamilyEnvelopeQuant).Assembly.GetType("FamilyQuant.CurationHomologyCache");
        var get = type?.GetMethod("GetOrSearch", BindingFlags.Static | BindingFlags.Public);
        Check("J28 exposes the persistent curation hit cache", get != null);
        if (get == null) return;
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.J28", Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(root);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>
        {
            new() { SequenceIdentifier = "protein-a", Description = "First", Sequence = "MAACDEFGHIKLQQ", UserID = 1 },
            new() { SequenceIdentifier = "protein-b", Description = "Second", Sequence = "MMACDEFGHIKRPP", UserID = 2 },
            new() { SequenceIdentifier = "Reverse_protein-c", Description = "Decoy", Sequence = "QQTSRQPNMLKIHH", UserID = 3 },
        } };
        var rows = new List<QuantRow>
        {
            new() { Side = NativeQuantFractions.SideIdentified, Run = "run-a", Sequence = "ACDEFGHIKL", Charge = 2, Scan = 1, PrecursorMz = 500, EnvelopeSum = 10, IsotopeAreas = new[] { 10.0, 0.0 } },
            new() { Side = NativeQuantFractions.SideDeNovo, Run = "run-a", Sequence = "ACDEFGHIK", Charge = 2, Scan = 2, PrecursorMz = 550, EnvelopeSum = 3, IsotopeAreas = new[] { 3.0, 0.0 } },
        };
        string project = Path.Combine(root, "fixture.fqproj");
        ProjectStore.WriteFqproj(project, file, rows, new QuantManifest());
        var first = ProjectStore.ReadFqproj(project);
        var second = ProjectStore.ReadFqproj(project);
        bool? outcome = null;
        CurationHomologyHits Get(IReadOnlyList<QuantRow> input, FamilyClusterFile proteins, HomologyOptions? options = null, bool best = true)
        {
            outcome = null;
            return (CurationHomologyHits)get.Invoke(null, new object?[] { input, proteins, cache, options, best,
                (Action<bool>)(hit => outcome = hit), CancellationToken.None })!;
        }
        byte[] Shares(IReadOnlyList<QuantRow> input, FamilyClusterFile proteins, CurationHomologyHits hits) =>
            JsonSerializer.SerializeToUtf8Bytes(FamilyEnvelopeQuant.BuildFromAttribution(input, FamilyEnvelopeQuant.AttributeHits(hits, proteins)));
        CurationHomologyHits fresh = FamilyEnvelopeQuant.SearchCuration(first.quant, first.fcf);
        byte[] expected = Shares(first.quant, first.fcf, fresh);
        CurationHomologyHits initial = Get(first.quant, first.fcf);
        Check("J28 cache miss performs a fresh search", outcome == false);
        Check("J28 cached search equals the independent fresh family result byte for byte", Shares(first.quant, first.fcf, initial).SequenceEqual(expected));
        CurationHomologyHits restored = Get(second.quant, second.fcf);
        Check("J28 reopening the project reads a cache hit", outcome == true);
        Check("J28 reopened cached hits reproduce family results byte for byte", Shares(second.quant, second.fcf, restored).SequenceEqual(expected));
        byte[] StoredShares(string name, IReadOnlyList<QuantRow> input, FamilyClusterFile proteins, CurationHomologyHits hits)
        {
            var manifest = new QuantManifest { PerRunTic = new Dictionary<string, double> { ["run-a"] = 100 } };
            var assigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(input), FamilyEnvelopeQuant.AttributeHits(hits, proteins));
            FamilyCompositionStudy.StoreRunShares(manifest, assigned);
            string path = Path.Combine(root, name + ".fqproj");
            ProjectStore.WriteFqproj(path, proteins, input, manifest);
            using var archive = ZipFile.OpenRead(path);
            using var entry = archive.GetEntry(ProjectStore.ManifestEntryName)!.Open();
            using var memory = new MemoryStream(); entry.CopyTo(memory); return memory.ToArray();
        }
        Check("J28 cached attribution writes byte-identical stored family shares",
            StoredShares("fresh", first.quant, first.fcf, fresh).SequenceEqual(StoredShares("cached", second.quant, second.fcf, restored)));
        var offsets = typeof(CurationHomologyHits).GetMethod("LocalizationOffsets", BindingFlags.Instance | BindingFlags.NonPublic)!;
        string OffsetText(CurationHomologyHits hits) => JsonSerializer.Serialize(offsets.Invoke(hits, new object[] { NativeQuantFractions.SideDeNovo, "ACDEFGHIK" }));
        Check("J28 retains identical protein localization offsets", OffsetText(fresh) == OffsetText(restored));
        second.fcf.MyExtendedFastaItems[0].UserID = 7;
        CurationHomologyHits regrouped = Get(second.quant, second.fcf);
        Check("J28 reuses hits after family regrouping and rejoins the current User IDs", outcome == true
            && Shares(second.quant, second.fcf, regrouped).SequenceEqual(Shares(second.quant, second.fcf, FamilyEnvelopeQuant.SearchCuration(second.quant, second.fcf))));
        second.fcf.MyExtendedFastaItems[0].Sequence += "W";
        Get(second.quant, second.fcf);
        Check("J28 changed protein sequence triggers a fresh search", outcome == false);
        second.fcf.MyExtendedFastaItems[0].Sequence = first.fcf.MyExtendedFastaItems[0].Sequence;
        second.quant[0].EnvelopeSum += 1;
        Get(second.quant, second.fcf);
        Check("J28 a changed stored row triggers a fresh search", outcome == false);
        second.quant[0].EnvelopeSum = first.quant[0].EnvelopeSum;
        Get(second.quant, second.fcf, new HomologyOptions { MinIdentity = 0.95 });
        Check("J28 a changed search option triggers a fresh search", outcome == false);
        var changedOptions = new[]
        {
            new HomologyOptions { ConsiderILSame = false }, new HomologyOptions { KQEquivalent = true },
            new HomologyOptions { TopHitsPerPeptide = 2 }, new HomologyOptions { BestScoreOnly = true },
            new HomologyOptions { DecoyLabel = "alternate-decoy-" },
            new HomologyOptions { Strategy = HomologySearchStrategy.BruteForce },
            new HomologyOptions { BruteForceProteinLimit = 1 },
            new HomologyOptions { Strategy = HomologySearchStrategy.BruteForce, SeedLength = 5 },
        };
        bool everyOptionMissed = true;
        foreach (var option in changedOptions) { Get(second.quant, second.fcf, option); everyOptionMissed &= outcome == false; }
        Check("J28 fingerprints each search option including normalization, ranking, decoys and strategy", everyOptionMissed);
        Get(second.quant, second.fcf, best: false);
        Check("J28 a changed identified best-score rule triggers a fresh search", outcome == false);
        Get(first.quant, first.fcf);
        Check("J28 original entry remains independently reusable", outcome == true);
        string[] entries = Directory.GetFiles(cache, "*.fqhits");
        foreach (string entry in entries) File.WriteAllBytes(entry, new byte[] { 0, 1, 2 });
        CurationHomologyHits repaired = Get(first.quant, first.fcf);
        Check("J28 damaged entries are ignored and searched again", outcome == false && Shares(first.quant, first.fcf, repaired).SequenceEqual(expected));
        Get(first.quant, first.fcf);
        Check("J28 the damaged entry is atomically replaced by a usable cache", outcome == true);
        foreach (string entry in Directory.GetFiles(cache, "*.fqhits"))
        {
            byte[] bytes = File.ReadAllBytes(entry); bytes[bytes.Length / 2] ^= 1; File.WriteAllBytes(entry, bytes);
        }
        CurationHomologyHits flipped = Get(first.quant, first.fcf);
        Check("J28 checksum detects a changed byte in an otherwise complete cache entry", outcome == false
            && Shares(first.quant, first.fcf, flipped).SequenceEqual(expected));
        string validEntry = Directory.GetFiles(cache, "*.fqhits").Single(path =>
        {
            byte[] data = File.ReadAllBytes(path);
            return data.Length > 32 && SHA256.HashData(data.AsSpan(0, data.Length - 32)).SequenceEqual(data.AsSpan(data.Length - 32).ToArray());
        });
        byte[] malformed = File.ReadAllBytes(validEntry);
        Array.Fill(malformed, (byte)255, 0, 4);
        SHA256.HashData(malformed.AsSpan(0, malformed.Length - 32)).CopyTo(malformed.AsSpan(malformed.Length - 32));
        File.WriteAllBytes(validEntry, malformed);
        bool structuralFallback = false;
        try { structuralFallback = Get(first.quant, first.fcf) != null && outcome == false; }
        catch (TargetInvocationException) { }
        Check("J28 ignores a structurally invalid entry even when its checksum is intact", structuralFallback);

        var duringSearch = ProjectStore.ReadFqproj(project);
        string originalSequence = duringSearch.fcf.MyExtendedFastaItems[0].Sequence;
        string raceCache = Path.Combine(root, "changed-during-search");
        get.Invoke(null, new object?[] { duringSearch.quant, duringSearch.fcf, raceCache, null, true,
            (Action<bool>)(hit => { if (!hit) duringSearch.fcf.MyExtendedFastaItems[0].Sequence = new string('W', originalSequence.Length); }), CancellationToken.None });
        duringSearch.fcf.MyExtendedFastaItems[0].Sequence = originalSequence;
        bool? raceHit = null;
        var afterRace = (CurationHomologyHits)get.Invoke(null, new object?[] { duringSearch.quant, duringSearch.fcf, raceCache, null, true,
            (Action<bool>)(hit => raceHit = hit), CancellationToken.None })!;
        Check("J28 never publishes a cache under a fingerprint from before an input changed", raceHit == false
            && Shares(duringSearch.quant, duringSearch.fcf, afterRace).SequenceEqual(expected));
        var cancelInput = ProjectStore.ReadFqproj(project);
        using var cancellation = new CancellationTokenSource();
        bool cancelled = false;
        try
        {
            get.Invoke(null, new object?[] { cancelInput.quant, cancelInput.fcf, Path.Combine(root, "cancelled"), null, true,
                (Action<bool>)(hit => { if (!hit) { cancelInput.fcf.MyExtendedFastaItems[0].SequenceIdentifier = ""; cancellation.Cancel(); } }), cancellation.Token });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException) { cancelled = true; }
        Check("J28 cancellation at the search progress boundary prevents search and cache publication", cancelled
            && !Directory.Exists(Path.Combine(root, "cancelled")));
        string blockedCache = Path.Combine(root, "cache-path-is-a-file");
        File.WriteAllText(blockedCache, "kept");
        bool? blockedHit = null;
        var withoutCache = (CurationHomologyHits)get.Invoke(null, new object?[] { first.quant, first.fcf, blockedCache, null, true,
            (Action<bool>)(hit => blockedHit = hit), CancellationToken.None })!;
        Check("J28 an unwritable cache keeps the fresh attribution and existing file", blockedHit == false
            && Shares(first.quant, first.fcf, withoutCache).SequenceEqual(expected) && File.ReadAllText(blockedCache) == "kept");

        var cachePath = typeof(CurationQuantitationViewModel).GetField("fullXicCacheDirectory", BindingFlags.Instance | BindingFlags.NonPublic);
        Check("J28 native progress can be checked with an isolated cache", cachePath != null);
        if (cachePath == null) return;
        var messages = new List<string>();
        var view = new CurationQuantitationViewModel(first.fcf);
        cachePath.SetValue(view, Path.Combine(root, "native-cache"));
        bool Attach()
        {
            messages.Clear();
            var task = view.AttachWithOperationAsync(first.quant, first.manifest.PerRunTic,
                new OperationContext(progress: new HomologyCacheProgress(messages)));
            FinishBusyTask(task);
            return task.GetAwaiter().GetResult();
        }
        bool searched = Attach(); string? freshCsv = view.FullXicCompositionCsv();
        Check("J28 the native progress identifies a fresh search", searched && messages.Any(m => m.Contains("Searching the stored peptide", StringComparison.Ordinal)));
        bool cached = Attach(); string? cachedCsv = view.FullXicCompositionCsv();
        Check("J28 the native progress identifies cached hits and preserves the composition CSV", cached && freshCsv == cachedCsv
            && messages.Any(m => m.Contains("hits read from the cache", StringComparison.OrdinalIgnoreCase)));
        string measurement = Path.Combine(root, "measurement");
        Check("J28 two-open measurement uses the native path and verifies equal stored shares", MeasureCurationHitCache(project, measurement) == 0
            && File.ReadAllText(Path.Combine(measurement, "timings.txt")).Contains("shares_byte_equal=True", StringComparison.Ordinal));
    }

    private sealed class HomologyCacheProgress(List<string> messages) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => messages.Add(value.Message);
    }
}
