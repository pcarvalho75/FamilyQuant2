using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelEntryIdentityFormatCommands() =>
    [new("--test-panel-entry-identity", 449, TestCommandPhase.Raw, args => args.Length is 1 or 2 && args[0] == "--test-panel-entry-identity", (args, _) =>
    {
        PanelEntryIdentityFormatTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.PanelEntryIdentity"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];

    private static JsonObject FirstPanelIdentityWire(byte[] bytes) => JsonNode.Parse(bytes)![0]!["Curation"]!["EvidenceSource"]!["ReferencePanel"]!["Entries"]![0]!.AsObject();
    private static byte[] PanelIdentityBytes(FamilyClusterFile file, IReadOnlyList<CurationFormatPart> parts)
    { using var stream = new MemoryStream(); CurationFormat.Write(file, stream, parts); return stream.ToArray(); }

    private static void PanelEntryIdentityFormatTests(string output)
    {
        Directory.CreateDirectory(output);
        var part = CurationFormatRegistry.Parts.SingleOrDefault(candidate => candidate.Name == "panel-entry-identity");
        Check("Z15b4 registry discovers the sealed identity part at order 40", part?.Order == 40 && part.GetType().IsSealed);
        var parts = part == null ? Array.Empty<CurationFormatPart>() : new[] { part };
        var file = CurationFormat2Fixture(); var entry = file.EvidenceSource!.ReferencePanel!.Entries[0];
        entry.Evidence.Positions!.SequenceSha256 = ProteinPositionFetcher.SequenceHash(entry.Sequence);
        byte[] wire = CheckCurationFormatRoundTrip("Z15b4 canonical entry", file, parts);
        var written = FirstPanelIdentityWire(wire);
        Check("Z15b4 entry identity accession is written once", written.ContainsKey("Accession") && !written["Evidence"]!.AsObject().ContainsKey("Accession")
            && !written["Evidence"]!["Positions"]!.AsObject().ContainsKey("Accession"));
        Check("Z15b4 positional sequence SHA is derived rather than stored", !written["Evidence"]!["Positions"]!.AsObject().ContainsKey("SequenceSha256"));
        if (part == null) return;
        var canonical = JsonSerializer.SerializeToNode(entry)!;
        Check("Z15b4 default serializer still retains all canonical identity fields", canonical["Evidence"]!["Accession"]!.GetValue<string>() == entry.Accession
            && canonical["Evidence"]!["Positions"]!["SequenceSha256"]!.GetValue<string>() == entry.Evidence.Positions.SequenceSha256);
        byte[] beforePart = PanelIdentityBytes(file, Array.Empty<CurationFormatPart>());
        var older = CurationFormat.Read(() => new MemoryStream(beforePart));
        Check("Z15b4 a format-2 file from before this part keeps every canonical hash", older.JsonContentSha256() == file.JsonContentSha256());
        Check("Z15b4 a format-1 file keeps every canonical hash", FamilyClusterFile.FromJsonText(file.ToJsonText()).JsonContentSha256() == file.JsonContentSha256());

        PanelEntry Copy(string accession)
        {
            var copy = JsonSerializer.Deserialize<PanelEntry>(JsonSerializer.Serialize(entry))!;
            copy.Accession = accession; copy.Evidence.Accession = accession; copy.Evidence.Positions!.Accession = accession; return copy;
        }
        var variants = CurationFormat2Fixture(); var list = new List<PanelEntry>();
        var lower = Copy("XL1"); lower.Evidence.Positions!.SequenceSha256 = lower.Evidence.Positions.SequenceSha256.ToLowerInvariant(); list.Add(lower);
        var mixed = Copy("XM1"); mixed.Evidence.Positions!.SequenceSha256 = new string(mixed.Evidence.Positions.SequenceSha256.Select((letter, index) => index % 2 == 0 ? char.ToLowerInvariant(letter) : letter).ToArray()); list.Add(mixed);
        var stale = Copy("XS1"); stale.Evidence.Positions!.SequenceSha256 = new string('0', 64); list.Add(stale);
        var mismatch = Copy("XI1"); mismatch.Evidence.Accession = "Different stored identity"; mismatch.Evidence.Positions!.Accession = ""; list.Add(mismatch);
        var nullable = Copy("XN1"); nullable.Evidence.Accession = null!; nullable.Evidence.Positions!.Accession = null!; nullable.Evidence.Positions.SequenceSha256 = null!; list.Add(nullable);
        var noPositions = Copy("XP1"); noPositions.Evidence.Positions = null; list.Add(noPositions);
        var noEvidence = Copy("XE1"); noEvidence.Evidence = null!; list.Add(noEvidence);
        var escaped = Copy("Invented \"quote\" \\ \u03bb"); list.Add(escaped);
        variants.EvidenceSource!.ReferencePanel!.Entries = list;
        byte[] variantWire = CheckCurationFormatRoundTrip("Z15b4 legacy identities and hash spelling", variants, parts);
        var variantRead = CurationFormat.Read(() => new MemoryStream(variantWire), parts);
        Check("Z15b4 stale coordinates stay stale after storage", !ProteinPositionFetcher.MatchesSequence(variantRead.EvidenceSource!.ReferencePanel!.Entries[2].Evidence.Positions!, stale.Sequence));
        Check("Z15b4 lower-case valid hashes are also absent from wire", !FirstPanelIdentityWire(variantWire)["Evidence"]!["Positions"]!.AsObject().ContainsKey("SequenceSha256"));

        var shared = CurationFormat2Fixture(); var protein = shared.MyExtendedFastaItems[0];
        shared.EvidenceSource!.ReferencePanel!.Entries[0] = new() { Accession = protein.Evidence!.Accession, Sequence = protein.Sequence, Evidence = protein.Evidence };
        byte[] sharedWire = CheckCurationFormatRoundTrip("Z15b4 shared file and panel evidence object", shared, parts);
        var fileEvidence = JsonNode.Parse(sharedWire)![0]!["Curation"]!["MyExtendedFastaItems"]![0]!["Evidence"]!.AsObject();
        Check("Z15b4 file protein identities are not changed by a panel item converter", fileEvidence.ContainsKey("Accession") && fileEvidence["Positions"]!.AsObject().ContainsKey("SequenceSha256"));

        var wrapper = CurationFormatRegistry.Discover(new[] { typeof(PanelIdentitySourceTestPart) }).Single();
        byte[] composed = CheckCurationFormatRoundTrip("Z15b4 nested provenance converter composition", file, new[] { wrapper, part });
        Check("Z15b4 nested provenance converter remains active inside a panel entry", FirstPanelIdentityWire(composed)["Evidence"]!["Positions"]!["FeatureSource"]!["wrapped"] != null);

        var many = CurationFormat2Fixture(); many.EvidenceSource!.ReferencePanel!.Entries = Enumerable.Range(0, 128).Select(index => Copy("XR" + (1000 + index))).ToList();
        byte[] expanded = PanelIdentityBytes(many, Array.Empty<CurationFormatPart>());
        byte[] compact = CheckCurationFormatRoundTrip("Z15b4 larger panel", many, parts);
        Check("Z15b4 identity storage saves bytes across a larger panel", compact.Length < expanded.Length - 128 * 50);
        Console.WriteLine($"Z15b4 entries=128 baseline_bytes={expanded.Length} identity_bytes={compact.Length} saved_bytes={expanded.Length - compact.Length}");
        File.WriteAllBytes(Path.Combine(output, "identity-format2.json"), compact);
        string expected = file.JsonContentSha256();
        var parallel = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            byte[] bytes = PanelIdentityBytes(file, parts); return CurationFormat.Read(() => new MemoryStream(bytes), parts).JsonContentSha256();
        })).ToArray();
        Check("Z15b4 registered part carries no shared write/read state", Task.WhenAll(parallel).GetAwaiter().GetResult().All(hash => hash == expected));
    }

    [CurationFormatPart("test-panel-source-wrap", 20)]
    private sealed class PanelIdentitySourceTestPart : CurationFormatPart
    {
        public override IEnumerable<JsonConverter> CreateConverters(CurationFormatContext context) => new[] { new SourceWrapperConverter() };
        private sealed class SourceWrapperConverter : JsonConverter<PositionEvidenceSource>
        {
            private JsonSerializerOptions? plain;
            private JsonSerializerOptions Plain(JsonSerializerOptions options)
            {
                if (plain != null) return plain;
                plain = new(options); plain.Converters.Remove(this); return plain;
            }
            public override PositionEvidenceSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            { using var json = JsonDocument.ParseValue(ref reader); return json.RootElement.GetProperty("wrapped").Deserialize<PositionEvidenceSource>(Plain(options)); }
            public override void Write(Utf8JsonWriter writer, PositionEvidenceSource value, JsonSerializerOptions options)
            { writer.WriteStartObject(); writer.WritePropertyName("wrapped"); JsonSerializer.Serialize(writer, value, Plain(options)); writer.WriteEndObject(); }
        }
    }
}
