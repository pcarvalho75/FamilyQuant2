using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

/// <summary>Adapts FL's persisted results for drawing. Fractions become percentages; no quantitation is rerun.</summary>
public static class StoredCompositionChartData
{
    public static FamilyCompositionData FromRunShares(FamilyRunSharesResult shares,
        FamilyProteinCountsResult? counts = null, IReadOnlyDictionary<int, string>? labels = null)
    {
        ArgumentNullException.ThrowIfNull(shares);
        FamilyStudyMembership.RequireCompatible(shares, counts);
        var countRows = counts?.Families.ToDictionary(r => r.FamilyUserId);
        var rows = new List<FamilyCompositionValue>();
        var unavailable = new List<string>();
        foreach (var family in shares.Families)
        {
            string label = Name(family.FamilyUserId, labels);
            if (family.MeanShare == null) { unavailable.Add(label); continue; }
            FamilyProteinCount? count = countRows?.GetValueOrDefault(family.FamilyUserId);
            rows.Add(new FamilyCompositionValue(family.FamilyUserId.ToString(CultureInfo.InvariantCulture), label,
                family.MeanShare.Value * 100, family.StandardDeviation * 100, family.AvailableRunCount,
                Array.Empty<FamilySubclassValue>(), count?.GreedyProteinCount, count?.UniquePeptideProteinCount,
                count?.UnexplainedPeptides.Count));
        }
        return new FamilyCompositionData(rows, "Mean per-run share of signal credited to families (" + shares.Policy.ShortName() + ")",
            unavailableFamilies: unavailable);
    }

    /// <summary>Pooled subclass fractions use pooled family signal, never rescaled to an unweighted run mean.</summary>
    public static FamilyCompositionData FromSubclassParts(FamilySubclassPartsResult parts,
        IReadOnlyDictionary<int, string>? labels = null)
    {
        ArgumentNullException.ThrowIfNull(parts);
        double denominator = parts.Families.Sum(f => f.FamilySignal);
        if (!double.IsFinite(denominator) || parts.Families.Any(f => !double.IsFinite(f.FamilySignal) || f.FamilySignal < 0))
            throw new ArgumentException("Invalid stored family signal.", nameof(parts));
        if (denominator <= 0)
            return new FamilyCompositionData(Array.Empty<FamilyCompositionValue>(), "Pooled credited family signal unavailable", false);
        var rows = parts.Families.Select(family =>
        {
            if (family.Parts.Any(p => !double.IsFinite(p.Signal) || p.Signal < 0 || !double.IsFinite(p.ShareOfFamily)
                    || p.ShareOfFamily < 0 || p.ShareOfFamily > 1
                    || (!p.IsUnresolved && (string.IsNullOrWhiteSpace(p.PartKey) || string.IsNullOrWhiteSpace(p.Label))))
                || Math.Abs(family.Parts.Sum(p => p.Signal) - family.FamilySignal) > Math.Max(1e-8, family.FamilySignal * 1e-10)
                || family.Parts.Any(p => family.FamilySignal > 0
                    && Math.Abs(p.Signal / family.FamilySignal - p.ShareOfFamily) > 1e-10))
                throw new ArgumentException("Stored subclass signal and shares do not close to the family.", nameof(parts));
            return new FamilyCompositionValue(family.FamilyUserId.ToString(CultureInfo.InvariantCulture),
                Name(family.FamilyUserId, labels), 100 * family.FamilySignal / denominator, null, 0,
                family.Parts.Select(p => new FamilySubclassValue(p.IsUnresolved ? "" : p.PartKey ?? "",
                    string.IsNullOrWhiteSpace(p.Label) ? "Subclass not resolved" : p.Label, 100 * p.Signal / denominator)).ToArray(),
                null, null);
        });
        return new FamilyCompositionData(rows, "Pooled share of signal credited to families (" + parts.Policy.ShortName() + ")", false);
    }

    private static string Name(int id, IReadOnlyDictionary<int, string>? labels) =>
        labels != null && labels.TryGetValue(id, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : "Family " + id.ToString(CultureInfo.InvariantCulture);
}
