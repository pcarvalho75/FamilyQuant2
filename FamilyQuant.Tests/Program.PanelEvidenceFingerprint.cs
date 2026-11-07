using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelEvidenceFingerprintCommands() =>
    [new("--test-panel-evidence-fingerprint", 74, TestCommandPhase.Raw, args => args.Contains("--test-panel-evidence-fingerprint", StringComparer.Ordinal), (_, _) =>
    {
        PanelEvidenceFingerprintTests(); Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];

    private static void PanelEvidenceFingerprintTests()
    {
        string Legacy(EvidenceProvenance? source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source))));
        var file = EvidenceMatchingFile();
        var escaped = EvidenceMatchingFile(); escaped.EvidenceSource!.ReferencePanel!.Entries[0].Evidence.Keywords.Add("Invented \"quote\" \\ path \n \u00e9 \u03bb \uD83D\uDE00");
        var partial = EvidenceMatchingFile(); partial.EvidenceSource!.ReferencePanel!.MissingFamilies.Add("Invented missing family");
        foreach (var source in new EvidenceProvenance?[] { null, new(), file.EvidenceSource, escaped.EvidenceSource, partial.EvidenceSource })
            Check("Z12p5f2e streamed part SHA matches the exact previous serializer bytes", PanelEvidenceMatching.FingerprintSource(source) == Legacy(source));
        var snapshot = new PanelEvidenceMatching(file);
        Check("Z12p5f2e matching captures a bounded hash rather than the whole panel string", snapshot.SourceEvidenceFingerprint.Length == 64 && snapshot.SourceEvidenceFingerprint == Legacy(file.EvidenceSource));
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) _ = snapshot.SourceEvidenceFingerprint.Length;
        Check("Z12p5f2e captured-state fingerprint is reused without serialization or allocation", GC.GetAllocatedBytesForCurrentThread() == allocated);
        var equivalent = FamilyClusterFile.FromJsonText(file.ToJsonText());
        Check("Z12p5f2e an equivalent detached state remains current", snapshot.IsCurrent(equivalent));
        file.EvidenceSource!.ReferencePanel!.Entries[0].Evidence.Keywords.Add("Changed in place");
        Check("Z12p5f2e deep in-place reference annotations still invalidate matching", !snapshot.IsCurrent(file));
        file = EvidenceMatchingFile(); snapshot = new(file); file.EvidenceSource!.Release = "New stored release";
        Check("Z12p5f2e release edits still invalidate matching", !snapshot.IsCurrent(file));
        string fragment = new('x', 16384);
        var large = new EvidenceProvenance { ReferencePanel = new() { Entries = Enumerable.Range(0, 128).Select(i => new PanelEntry
            { Accession = "P" + i, Sequence = "ACDEFGHIK", Evidence = new() { Keywords = new() { fragment } } }).ToList() } };
        string json = JsonSerializer.Serialize(large); string expected = Legacy(large);
        _ = PanelEvidenceMatching.FingerprintSource(large); allocated = GC.GetAllocatedBytesForCurrentThread();
        string actual = PanelEvidenceMatching.FingerprintSource(large); long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check("Z12p5f2e large synthetic part keeps exact SHA with bounded working allocation", actual == expected && bytes < Encoding.UTF8.GetByteCount(json) / 2,
            "json_bytes=" + Encoding.UTF8.GetByteCount(json) + ", allocated_bytes=" + bytes);
    }
}
