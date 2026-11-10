using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class HtmlReportPayloadBuilder
{
    public static double? ReadRingShare(HtmlReportReference source, QuantManifest manifest)
    {
        if (source.Kind != "family-ring" || source.Family == null || source.Property == null)
            throw new ArgumentException("A family-ring source is required.");
        string[] property = source.Property.Split('/');
        if (property.Length != 2 || property[1] is not ("total" or "outside")) throw new InvalidDataException("Unknown ring statistic.");
        string[] families = source.Family.Split(';');
        if (families.Length == 0 || families.Any(string.IsNullOrEmpty) || families.Distinct(StringComparer.Ordinal).Count() != families.Length)
            throw new InvalidDataException("Ring families must be distinct and recorded.");
        double total = 0;
        foreach (string family in families)
        {
            double? value = ReadFamilyShare(source with { Kind = "family-basis", Family = family, Property = property[0] + "/mean" }, manifest);
            if (!value.HasValue) return null;
            total += value.Value;
        }
        return property[1] == "total" ? total : 1 - total;
    }

    private sealed partial class Builder
    {
        private IReadOnlyDictionary<string, HtmlRingBasis> RingBases(IReadOnlyList<HtmlReportFamily> families)
        {
            var result = new Dictionary<string, HtmlRingBasis>(StringComparer.Ordinal);
            if (context.StoredStudies == null || families.Count == 0) return result;
            var labels = new[] { (Key: "identifiedOnly", Source: "IdentifiedOnly"), (Key: "identifiedAndDeNovo", Source: "IdentifiedAndDeNovo"),
                (Key: "detectorEnvelopes", Source: "DetectorEnvelopes"), (Key: "tic", Source: "Tic") };
            foreach (var basis in labels)
            {
                var statistics = families.Select(family => basis.Key switch
                {
                    "identifiedOnly" => family.IdentifiedOnly, "identifiedAndDeNovo" => family.IdentifiedAndDeNovo,
                    "detectorEnvelopes" => family.DetectorEnvelopes, _ => family.Tic,
                }).ToArray();
                if (statistics.Any(statistic => statistic.Mean == null || !fields.ContainsKey(statistic.Mean)))
                {
                    result[basis.Key] = new(null, null, false, 0, "Some shown families have no complete share for this base. Compare their recorded values in the ranking.");
                    continue;
                }
                var references = statistics.Select(statistic => fields[statistic.Mean!].Source).ToArray();
                if (references.Any(reference => reference.Kind != "family-basis" || reference.Family == null)) continue;
                var source = new HtmlReportReference { Kind = "family-ring", Family = string.Join(";", references.Select(reference => reference.Family)),
                    Property = basis.Source + "/total", Study = references[0].Study, Scale = 100, Format = "G6" };
                double? total = ReadRingShare(source, context.StoredStudies) * 100;
                if (!total.HasValue) continue;
                string AddShare(double value, HtmlReportReference reference)
                {
                    string id = Add(value.ToString("G6", CultureInfo.InvariantCulture), reference, "%", value);
                    AddShort(id, value, reference);
                    return id;
                }
                string totalId = AddShare(total.Value, source);
                bool canDraw = total.Value >= 0 && total.Value <= 100;
                string? outside = canDraw ? AddShare(ReadRingShare(source with { Property = basis.Source + "/outside" }, context.StoredStudies)!.Value * 100,
                    source with { Property = basis.Source + "/outside" }) : null;
                int runs = context.StoredStudies.FamilyRunShares?.Runs.Count ?? 0;
                result[basis.Key] = new(totalId, outside, canDraw, runs,
                    canDraw ? null : "The shown shares exceed a full ring on this base. Compare the exact values in the ranking.");
            }
            return result;
        }
    }
}
