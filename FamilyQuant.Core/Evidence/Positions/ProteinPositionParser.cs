using System;
using System.IO;
using System.Text.Json;

namespace FamilyQuant.Evidence.Positions;

internal static class ProteinPositionParser
{
    internal static ProteinPositionEvidence? ReadUniProt(JsonElement entry)
    {
        // Old responses/files did not request sequence or processing features. Their absence remains
        // unknown, rather than an empty fetched position list.
        if (!entry.TryGetProperty("sequence", out var sequence) || !sequence.TryGetProperty("value", out var value)) return null;
        string letters = value.GetString() ?? string.Empty;
        if (letters.Length == 0) throw new InvalidDataException("UniProt supplied an empty source sequence.");
        var positions = new ProteinPositionEvidence
        {
            Accession = entry.GetProperty("primaryAccession").GetString() ?? string.Empty,
            SequenceLength = letters.Length, SequenceSha256 = ProteinPositionFetcher.SequenceHash(letters),
        };
        if (!entry.TryGetProperty("features", out var features)) return positions;
        foreach (var item in features.EnumerateArray())
        {
            string type = Text(item, "type");
            if (type is not ("Chain" or "Peptide" or "Propeptide" or "Signal")) continue;
            var location = item.GetProperty("location");
            var start = Boundary(location.GetProperty("start"), positions.SequenceLength);
            var end = Boundary(location.GetProperty("end"), positions.SequenceLength);
            if (start.Value > end.Value) throw new InvalidDataException("UniProt supplied a reversed feature interval.");
            var feature = new ProteinFeaturePosition
            {
                Type = type, Description = Text(item, "description"), FeatureId = Text(item, "featureId"),
                Start = start.Value, End = end.Value, StartModifier = start.Modifier, EndModifier = end.Modifier,
            };
            if (item.TryGetProperty("evidences", out var evidences))
                foreach (var evidence in evidences.EnumerateArray()) feature.Evidences.Add(new PositionEvidenceReference
                {
                    EvidenceCode = Text(evidence, "evidenceCode"), Source = Text(evidence, "source"), Id = Text(evidence, "id"),
                });
            positions.Features.Add(feature);
        }
        return positions;
    }

    private static (int? Value, string Modifier) Boundary(JsonElement element, int length)
    {
        int? value = element.TryGetProperty("value", out var coordinate) && coordinate.ValueKind == JsonValueKind.Number
            ? coordinate.GetInt32() : null;
        if (value is < 1 || value > length) throw new InvalidDataException("UniProt supplied an out-of-range feature boundary.");
        string modifier = Text(element, "modifier");
        return (value, modifier.Length > 0 ? modifier : value.HasValue ? "EXACT" : "UNKNOWN");
    }

    internal static string Text(JsonElement value, string property) => value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty;
}
