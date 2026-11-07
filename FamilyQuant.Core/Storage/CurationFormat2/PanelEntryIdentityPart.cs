using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FamilyQuant.CurationFormat2;

/// <summary>Panel identity fields share the entry's accession and derive valid position hashes on read.
/// Only an individual panel entry is buffered; canonical/default serialization never uses this converter.</summary>
[CurationFormatPart("panel-entry-identity", 40)]
internal sealed class PanelEntryIdentityPart : CurationFormatPart
{
    public override IEnumerable<JsonConverter> CreateConverters(CurationFormatContext context) => new[] { new EntryConverter() };

    private sealed class EntryConverter : JsonConverter<PanelEntry>
    {
        private const string LowerHash = "$panel-identity-lower-hash";
        private JsonSerializerOptions? plain;

        private JsonSerializerOptions Plain(JsonSerializerOptions options)
        {
            if (plain != null) return plain;
            plain = new(options);
            // Keep every other part's converters and contract modifiers on the nested objects.
            plain.Converters.Remove(this);
            return plain;
        }

        public override void Write(Utf8JsonWriter writer, PanelEntry value, JsonSerializerOptions options)
        {
            var item = JsonSerializer.SerializeToNode(value, Plain(options))!.AsObject();
            if (item[nameof(PanelEntry.Evidence)] is JsonObject evidence && value.Evidence is { } facts)
            {
                CompactAccession(evidence, facts.Accession, value.Accession);
                if (evidence[nameof(ProteinEvidence.Positions)] is JsonObject positions && facts.Positions is { } coordinates)
                {
                    CompactAccession(positions, coordinates.Accession, value.Accession);
                    string? derived = value.Sequence == null ? null : ProteinPositionFetcher.SequenceHash(value.Sequence);
                    if (derived != null && string.Equals(coordinates.SequenceSha256, derived, StringComparison.Ordinal))
                        positions.Remove(nameof(ProteinPositionEvidence.SequenceSha256));
                    else if (derived != null && string.Equals(coordinates.SequenceSha256, derived.ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        positions.Remove(nameof(ProteinPositionEvidence.SequenceSha256));
                        positions[LowerHash] = true;
                    }
                    else
                    {
                        // A stale, mixed-case, empty or null legacy hash must remain exactly as stored.
                        // Replacing it with a new valid hash could make stale coordinates appear current.
                        positions[nameof(ProteinPositionEvidence.SequenceSha256)] = coordinates.SequenceSha256;
                    }
                }
            }
            item.WriteTo(writer, options);
        }

        private static void CompactAccession(JsonObject target, string? nested, string? entry)
        {
            if (string.Equals(nested, entry, StringComparison.Ordinal)) target.Remove(nameof(PanelEntry.Accession));
            else target[nameof(PanelEntry.Accession)] = nested;
        }

        public override PanelEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var item = JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException("A reference panel entry must be an object.");
            if (!item.TryGetPropertyValue(nameof(PanelEntry.Accession), out var accession))
                throw new JsonException("A reference panel entry is missing its accession.");
            if (item[nameof(PanelEntry.Evidence)] is JsonObject evidence)
            {
                RestoreAccession(evidence, accession);
                if (evidence[nameof(ProteinEvidence.Positions)] is JsonObject positions)
                {
                    RestoreAccession(positions, accession);
                    bool lower = false;
                    if (positions.TryGetPropertyValue(LowerHash, out var spelling))
                    {
                        if (spelling is not JsonValue value || !value.TryGetValue(out lower)
                            || positions.ContainsKey(nameof(ProteinPositionEvidence.SequenceSha256)))
                            throw new JsonException("A panel position hash has an invalid spelling marker.");
                        positions.Remove(LowerHash);
                    }
                    if (!positions.ContainsKey(nameof(ProteinPositionEvidence.SequenceSha256)))
                    {
                        if (item[nameof(PanelEntry.Sequence)] is not JsonValue sequence || !sequence.TryGetValue<string>(out var letters) || letters == null)
                            throw new JsonException("A panel entry needs its sequence to restore the position hash.");
                        string hash = ProteinPositionFetcher.SequenceHash(letters);
                        positions[nameof(ProteinPositionEvidence.SequenceSha256)] = lower ? hash.ToLowerInvariant() : hash;
                    }
                }
            }
            return item.Deserialize<PanelEntry>(Plain(options)) ?? throw new JsonException("A reference panel entry could not be restored.");
        }

        private static void RestoreAccession(JsonObject target, JsonNode? entry)
        {
            if (!target.ContainsKey(nameof(PanelEntry.Accession))) target[nameof(PanelEntry.Accession)] = entry?.DeepClone();
        }
    }
}
