using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant.Evidence;

public static partial class EvidenceApply
{
    private static readonly JsonSerializerOptions SessionMetadataOptions = CopyOptions(info =>
    {
        if (info.Type == typeof(EvidenceRecipe) || info.Type == typeof(EvidenceApplySelection))
            info.Properties.Single(p => p.Name == nameof(EvidenceRecipe.SessionReferences)).ShouldSerialize = (_, _) => false;
    });
    private static readonly JsonSerializerOptions ReferenceMetadataOptions = CopyOptions(info =>
    {
        if (info.Type == typeof(NeighbourReference))
            info.Properties.Single(p => p.Name == nameof(NeighbourReference.Sequence)).ShouldSerialize = (_, _) => false;
    });

    private static JsonSerializerOptions CopyOptions(Action<JsonTypeInfo> modify) => new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { modify } },
    };

    private static T CopyJsonMetadata<T>(T value, JsonSerializerOptions options) =>
        JsonStreamCopy.ReadWritten(stream => JsonSerializer.Serialize(stream, value, options), open =>
        {
            using var stream = open();
            return JsonSerializer.Deserialize<T>(stream, options)!;
        });

    // JSON still detaches every list, wrapper and mutable metadata field. Only immutable sequence
    // strings are reused; writing and reading them again would create another full reference table.
    private static T CloneSessionRecord<T>(T value, IReadOnlyList<NeighbourReference>? references)
    {
        var rows = references?.ToList();
        var copy = CopyJsonMetadata(value, SessionMetadataOptions);
        List<NeighbourReference>? copiedRows = rows == null ? null : CopyJsonMetadata(rows, ReferenceMetadataOptions);
        if (copiedRows != null)
        {
            var sequence = ReferenceMetadataOptions.GetTypeInfo(typeof(NeighbourReference)).Properties
                .Single(p => p.Name == nameof(NeighbourReference.Sequence));
            for (int index = 0; index < copiedRows.Count; index++)
                if (copiedRows[index] != null) sequence.Set!(copiedRows[index], rows![index].Sequence);
        }
        // Use the serializer's own setter, which also handles the selection's public init property.
        SessionMetadataOptions.GetTypeInfo(typeof(T)).Properties
            .Single(p => p.Name == nameof(EvidenceRecipe.SessionReferences)).Set!(copy!, copiedRows);
        return copy;
    }
}
