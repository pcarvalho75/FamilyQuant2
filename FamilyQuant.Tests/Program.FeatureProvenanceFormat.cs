using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FeatureProvenanceFormatCommands() =>
    [new("--test-feature-provenance-format2", 10610, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-feature-provenance-format2", (args, _) =>
        {
            FeatureProvenanceFormatTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.FeatureProvenance"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void FeatureProvenanceFormatTests(string output)
    {
        Section("Z15b2 feature provenance stored once per batch");
        Directory.CreateDirectory(output);
        var part = CurationFormatRegistry.Parts.Single(p => p.Name == "feature-provenance");
        var parts = new[] { part };
        var file = FeatureProvenanceFixture(256, 4);
        var canonical = file.ToJsonText();
        var fingerprint = EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems);
        byte[] bytes = CheckCurationFormatRoundTrip("Z15b2 repeated batches", file, parts);
        File.WriteAllBytes(Path.Combine(output, "pooled-curation.json"), bytes);
        var wire = JsonNode.Parse(bytes)![0]!.AsObject();
        var sources = wire["Sections"]![part.Name]!.AsArray();
        var proteins = wire["Curation"]![nameof(FamilyClusterFile.MyExtendedFastaItems)]!.AsArray();
        Check("Z15b2 complete equal sources appear once and every feature source is an index", sources.Count == 4
            && proteins.All(p => p!["Evidence"]!["Positions"]!["FeatureSource"] is JsonValue value && value.TryGetValue<int>(out _)));
        Check("Z15b2 the batched wire is smaller than half the canonical fixture", bytes.Length < Encoding.UTF8.GetByteCount(canonical) / 2);
        var restored = ReadFeatureProvenance(bytes, parts);
        Check("Z15b2 complete logical objects and scientific evidence fingerprints stay equal", restored.ToJsonText() == canonical
            && EvidenceClassifier.Fingerprint(restored.MyExtendedFastaItems) == fingerprint);
        var first = restored.MyExtendedFastaItems[0].Evidence!.Positions!.FeatureSource!;
        var repeated = restored.MyExtendedFastaItems[4].Evidence!.Positions!.FeatureSource!;
        Check("Z15b2 repeated sources and response lists are restored as detached objects", !ReferenceEquals(first, repeated)
            && !ReferenceEquals(first.Responses, repeated.Responses) && !ReferenceEquals(first.Responses[0], repeated.Responses[0]));
        string repeatedBefore = JsonSerializer.Serialize(repeated);
        first.Provider = "An independent edit"; first.Responses[0].Url = "https://example.invalid/edited";
        Check("Z15b2 editing one restored source leaves another occurrence intact", JsonSerializer.Serialize(repeated) == repeatedBefore
            && file.ToJsonText() == canonical);

        var distinct = FeatureProvenanceFixture(9, 1);
        PositionEvidenceSource Source(int index) => distinct.MyExtendedFastaItems[index].Evidence!.Positions!.FeatureSource!;
        for (int index = 1; index < 9; index++)
            distinct.MyExtendedFastaItems[index].Evidence!.Positions!.FeatureSource = JsonSerializer.Deserialize<PositionEvidenceSource>(JsonSerializer.Serialize(Source(0)))!;
        Source(1).Provider = Source(0).Provider.ToLowerInvariant();
        Source(2).Release += "-different";
        Source(3).RetrievedAtUtc = Source(0).RetrievedAtUtc.ToOffset(TimeSpan.FromHours(2));
        Source(4).Responses[0].Url += "-different";
        Source(5).Responses[0].Sha256 = Source(5).Responses[0].Sha256.ToLowerInvariant();
        Source(6).Responses.Reverse();
        Source(7).Responses.Add(new() { Url = Source(7).Responses[0].Url, Sha256 = Source(7).Responses[0].Sha256 });
        Source(8).Responses = null!;
        byte[] distinctBytes = CheckCurationFormatRoundTrip("Z15b2 distinct provenance fields", distinct, parts);
        Check("Z15b2 provider release offset URL hash case response order duplicates and null lists stay distinct",
            JsonNode.Parse(distinctBytes)![0]!["Sections"]![part.Name]!.AsArray().Count == 9);
        var sparse = FeatureProvenanceFixture(3, 1);
        sparse.MyExtendedFastaItems[0].Evidence!.Positions!.FeatureSource = null;
        sparse.MyExtendedFastaItems[1].Evidence!.Positions!.FeatureSource = new();
        sparse.MyExtendedFastaItems[2].Evidence!.Positions!.FeatureSource = new() { Responses = null! };
        CheckCurationFormatRoundTrip("Z15b2 null empty and absent records", sparse, parts);
        CheckCurationFormatRoundTrip("Z15b2 existing rich curation", CurationFormat2Fixture(), parts);
        CheckCurationFormatRoundTrip("Z15b2 source-free curation", ManualChoiceFile(), parts);
        var domains = FeatureProvenanceFixture(2, 1);
        domains.MyExtendedFastaItems[0].Evidence!.Positions!.DomainSource = domains.MyExtendedFastaItems[0].Evidence!.Positions!.FeatureSource;
        domains.MyExtendedFastaItems[1].Evidence!.Positions!.DomainSource = new()
        {
            Provider = "InterPro/Pfam", Release = "synthetic-domain-release",
            Responses = new() { new() { Url = "https://example.invalid/domain", Sha256 = new string('C', 64) } },
        };
        byte[] domainBytes = CheckCurationFormatRoundTrip("Z15b2 domain and feature provenance", domains, parts);
        Check("Z15b2 complete domain sources use the same table without losing their distinct metadata",
            JsonNode.Parse(domainBytes)![0]!["Sections"]![part.Name]!.AsArray().Count == 2);
        Console.WriteLine("Z15b2 synthetic fixture has " + Encoding.UTF8.GetByteCount(canonical)
            + " canonical bytes and " + bytes.Length + " pooled bytes across 256 occurrences and 4 stored source records.");

        byte[] beforePart;
        using (var stream = new MemoryStream()) { CurationFormat.Write(file, stream, Array.Empty<CurationFormatPart>()); beforePart = stream.ToArray(); }
        Check("Z15b2 a format-2 file written before the part existed remains unchanged", ReadFeatureProvenance(beforePart, parts).ToJsonText() == canonical);
        Check("Z15b2 format-1 objects keep default serialization and hashes", FamilyClusterFile.FromJsonText(canonical).ToJsonText() == canonical
            && file.JsonContentSha256() == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            using var stream = new MemoryStream(); CurationFormat.Write(file, stream, parts);
            Check("Z15b2 wire bytes repeat across decimal cultures", bytes.SequenceEqual(stream.ToArray()));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        var independent = Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(() =>
        {
            var local = FeatureProvenanceFixture(8, index + 1);
            using var stream = new MemoryStream(); CurationFormat.Write(local, stream, parts);
            var copy = ReadFeatureProvenance(stream.ToArray(), parts);
            return copy.ToJsonText() == local.ToJsonText();
        }))).GetAwaiter().GetResult();
        Check("Z15b2 concurrent documents have independent source tables", independent.All(equal => equal));
        using (var stream = new MemoryStream())
        {
            var local = FeatureProvenanceFixture(1, 1); CurationFormat.Write(local, stream, parts);
            Check("Z15b2 a later document starts its own table at zero", JsonNode.Parse(stream.ToArray())![0]!["Sections"]![part.Name]!.AsArray().Count == 1
                && ReadFeatureProvenance(stream.ToArray(), parts).ToJsonText() == local.ToJsonText());
        }

        bool Refuses(Action<JsonObject> edit)
        {
            var root = JsonNode.Parse(bytes)!; edit(root[0]!.AsObject());
            byte[] malformed = Encoding.UTF8.GetBytes(root.ToJsonString());
            try { ReadFeatureProvenance(malformed, parts); return false; }
            catch (Exception error) when (error is JsonException or InvalidDataException) { return true; }
        }
        Check("Z15b2 missing table is refused", Refuses(document => document["Sections"]!.AsObject().Remove(part.Name)));
        Check("Z15b2 null table and null records are refused", Refuses(document => document["Sections"]![part.Name] = null)
            && Refuses(document => document["Sections"]![part.Name]![0] = null));
        foreach (JsonNode value in new JsonNode[] { JsonValue.Create(-1)!, JsonValue.Create(int.MaxValue)!, JsonValue.Create(1.5)!, JsonValue.Create("0")!, new JsonObject() })
            Check("Z15b2 invalid source reference " + value.ToJsonString() + " is refused", Refuses(document =>
                document["Curation"]![nameof(FamilyClusterFile.MyExtendedFastaItems)]![0]!["Evidence"]!["Positions"]!["FeatureSource"] = value.DeepClone()));

        string project = Path.Combine(output, "provenance.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        Check("Z15b2 the native project path reopens identical objects and response hashes", ProjectStore.ReadFqproj(project).fcf.ToJsonText() == canonical);
    }

    private static FamilyClusterFile FeatureProvenanceFixture(int entries, int batches)
    {
        var sources = Enumerable.Range(0, batches).Select(batch => new PositionEvidenceSource
        {
            Provider = "UniProt", Release = "synthetic-release",
            RetrievedAtUtc = new DateTimeOffset(2000, 1, 1, 0, batch, 0, TimeSpan.Zero),
            Responses = new()
            {
                new() { Url = "https://example.invalid/batch/" + batch + "?entries=" + new string('A', 2048),
                    Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("batch-" + batch))) },
                new() { Url = "https://example.invalid/metadata/" + batch, Sha256 = new string('B', 64) },
            },
        }).ToArray();
        return new FamilyClusterFile
        {
            MyExtendedFastaItems = Enumerable.Range(0, entries).Select(index => new FastaItemExtended
            {
                SequenceIdentifier = "PROVENANCE_" + index, Sequence = "ACDEFGHIK", UserID = index, USearchID = index,
                Evidence = new ProteinEvidence
                {
                    Accession = "PROVENANCE_" + index,
                    Positions = new ProteinPositionEvidence
                    {
                        Accession = "PROVENANCE_" + index, SequenceLength = 9, SequenceSha256 = ProteinPositionFetcher.SequenceHash("ACDEFGHIK"),
                        FeatureSource = sources[index % batches],
                    },
                },
            }).ToList(),
        };
    }

    private static FamilyClusterFile ReadFeatureProvenance(byte[] bytes, IReadOnlyList<CurationFormatPart> parts) =>
        CurationFormat.Read(() => new MemoryStream(bytes, writable: false), parts);
}
