using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyQuant.CurationFormat2;

/// <summary>Stores each complete position-evidence source once and restores independent source objects.</summary>
[CurationFormatPart("feature-provenance", 20)]
internal sealed class FeatureProvenancePart : CurationFormatPart
{
    public override void PrepareWrite(FamilyClusterFile file, CurationFormatContext context)
    {
        var table = new SourceTable();
        var options = new JsonSerializerOptions(FamilyClusterFile.CurationJsonOptions);
        options.Converters.Add(new SourceCollector(table));
        JsonSerializer.Serialize(Stream.Null, file, options);
        context.State[Name] = table;
        context.SetSection(this, table.Values);
    }

    public override IEnumerable<JsonConverter> CreateConverters(CurationFormatContext context)
    {
        SourceTable table;
        if (context.Writing)
        {
            table = (SourceTable)context.State[Name]!;
        }
        else
        {
            if (!context.TryGetSection<List<PositionEvidenceSource>>(this, out var sources) || sources == null
                || sources.Any(source => source == null))
                throw new InvalidDataException("The curation's feature provenance table is missing or invalid.");
            table = new SourceTable(sources);
        }
        return new[] { new SourceReferenceConverter(table) };
    }

    private static string Key(PositionEvidenceSource source) => JsonSerializer.Serialize(source, FamilyClusterFile.CurationJsonOptions);

    private sealed class SourceTable
    {
        internal List<PositionEvidenceSource> Values { get; }
        internal Dictionary<string, int> Indices { get; } = new(StringComparer.Ordinal);

        internal SourceTable() : this(new List<PositionEvidenceSource>()) { }
        internal SourceTable(List<PositionEvidenceSource> values) => Values = values;

        internal void Add(PositionEvidenceSource source)
        {
            string key = Key(source);
            if (Indices.ContainsKey(key)) return;
            var snapshot = JsonSerializer.Deserialize<PositionEvidenceSource>(key, FamilyClusterFile.CurationJsonOptions)!;
            Indices.Add(key, Values.Count);
            Values.Add(snapshot);
        }
    }

    private sealed class SourceCollector(SourceTable table) : JsonConverter<PositionEvidenceSource>
    {
        public override PositionEvidenceSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("The provenance collector only prepares a curation write.");

        public override void Write(Utf8JsonWriter writer, PositionEvidenceSource value, JsonSerializerOptions options)
        {
            table.Add(value);
            writer.WriteNullValue();
        }
    }

    private sealed class SourceReferenceConverter(SourceTable table) : JsonConverter<PositionEvidenceSource>
    {
        public override PositionEvidenceSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int index)
                || index < 0 || index >= table.Values.Count)
                throw new JsonException("The curation contains an invalid feature provenance reference.");
            return JsonStreamCopy.Clone(table.Values[index]);
        }

        public override void Write(Utf8JsonWriter writer, PositionEvidenceSource value, JsonSerializerOptions options)
        {
            if (!table.Indices.TryGetValue(Key(value), out int index))
                throw new JsonException("The evidence provenance changed while the curation was being written.");
            writer.WriteNumberValue(index);
        }
    }
}
