using FamilyQuant.DeNovo;
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
        if (property.Length != 2 || property[1] is not ("total" or "outside" or "peptide-outside" or "rest-outside"))
            throw new InvalidDataException("Unknown ring statistic.");
        if (property[0] is not ("IdentifiedOnly" or "IdentifiedAndDeNovo" or "DetectorEnvelopes" or "Tic"))
            throw new InvalidDataException("Unknown ring base.");
        if (source.Study is not ("source" or "effective")) throw new InvalidDataException("Unknown family share study.");
        string[] families = source.Family.Length == 0 ? Array.Empty<string>() : source.Family.Split(';');
        if (families.Any(string.IsNullOrEmpty) || families.Distinct(StringComparer.Ordinal).Count() != families.Length)
            throw new InvalidDataException("Ring families must be distinct and recorded.");
        double total = 0;
        foreach (string family in families)
        {
            double? value = ReadFamilyShare(source with { Kind = "family-basis", Family = family, Property = property[0] + "/mean" }, manifest);
            if (!value.HasValue) return null;
            total += value.Value;
        }
        if (property[1] is "total" or "outside") return property[1] == "total" ? total : 1 - total;
        var parts = RingRemainder(source, manifest, total);
        return property[1] == "peptide-outside" ? parts.Peptide : parts.Unexplained;
    }

    private static (double? Peptide, double? Unexplained, string? Reason) RingRemainder(HtmlReportReference source, QuantManifest manifest, double shown)
    {
        string basis = source.Property!.Split('/')[0];
        if (basis is not ("Tic" or "DetectorEnvelopes")) return (null, null, null);
        var study = source.Study == "effective" ? manifest.EffectivePanelFamilies?.RunShares : manifest.FamilyRunShares;
        if (study == null || study.Runs.Count == 0) return (null, null, "not split: stored run totals unavailable");
        double mean = 0;
        int count = 0;
        foreach (var run in study.Runs)
        {
            double? denominator = basis == "Tic" ? run.TotalIonCurrent : manifest.DerivedFractions.SingleOrDefault(d => !d.Grouped
                && string.Equals(DeNovoReconciler.NormalizeRunName(d.Run), DeNovoReconciler.NormalizeRunName(run.Run),
                    StringComparison.OrdinalIgnoreCase))?.DetectedPeptideLikeArea;
            if (!(denominator > 0) || !double.IsFinite(denominator.Value) || !double.IsFinite(run.MeasuredSignal) || run.MeasuredSignal < 0)
                return (null, null, "not split: stored run totals unavailable");
            mean += (run.MeasuredSignal / denominator.Value - mean) / ++count;
        }
        if (!double.IsFinite(mean)) return (null, null, "not split: stored run totals unavailable");
        if (mean > 1) return (null, null, "not split: PSM and de novo exceed this base");
        if (shown > mean) return (null, null, "not split: shown families exceed PSM and de novo");
        return (mean - shown, 1 - mean, null);
    }

    private sealed partial class Builder
    {
        private IReadOnlyDictionary<string, HtmlRingBasis> RingBases(IReadOnlyList<HtmlReportFamily> families)
        {
            var shown = families.Where(family => !family.IsContaminants).ToArray();
            bool noShownFamilies = shown.Length == 0 && families.Any(family => family.IsContaminants);
            families = noShownFamilies ? families : shown;
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
                var source = new HtmlReportReference { Kind = "family-ring", Family = noShownFamilies ? "" : string.Join(";", references.Select(reference => reference.Family)),
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
            foreach (string key in new[] { "detectorEnvelopes", "tic" })
            {
                if (!result.TryGetValue(key, out var ring) || ring.Outside == null) continue;
                var outside = fields[ring.Outside];
                var parts = RingRemainder(outside.Source, context.StoredStudies, fields[ring.Total!].Value!.Value / 100);
                if (parts.Peptide.HasValue && parts.Unexplained.HasValue)
                {
                    string AddPart(double value, string statistic)
                    {
                        value *= 100;
                        var source = outside.Source with { Property = outside.Source.Property!.Split('/')[0] + "/" + statistic };
                        string id = Add(value.ToString("G6", CultureInfo.InvariantCulture), source, "%", value);
                        AddShort(id, value, source);
                        return id;
                    }
                    result[key] = ring with { OutsidePeptide = AddPart(parts.Peptide.Value, "peptide-outside"),
                        OutsideRest = AddPart(parts.Unexplained.Value, "rest-outside") };
                }
                else if (parts.Reason != null)
                {
                    fields[ring.Outside] = outside with { Source = outside.Source with { ParagraphText = parts.Reason } };
                    if (outside.Short != null)
                        fields[outside.Short] = fields[outside.Short] with { Source = fields[outside.Short].Source with { ParagraphText = parts.Reason } };
                }
            }
            return result;
        }
    }
}
