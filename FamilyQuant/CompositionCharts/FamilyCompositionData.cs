using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

/// <summary>Chart input in percentage points of the declared denominator, never a within-family percent.</summary>
public sealed record FamilySubclassValue(string Key, string Label, double Percent);

/// <summary>Immutable view of stored results. Null SD/count means unavailable, never zero.</summary>
public sealed record FamilyCompositionValue(string Key, string Label, double MeanPercent, double? SdPercent,
    int RunCount, IReadOnlyList<FamilySubclassValue> Subclasses, int? MinimumProteinCount, int? UniqueProteinCount);

/// <summary>Validated drawing snapshot. No attribution, thresholding or normalization occurs here.</summary>
public sealed class FamilyCompositionData
{
    public IReadOnlyList<FamilyCompositionValue> Families { get; }
    public string Basis { get; }

    public FamilyCompositionData(IEnumerable<FamilyCompositionValue> families, string basis)
    {
        ArgumentNullException.ThrowIfNull(families);
        if (string.IsNullOrWhiteSpace(basis)) throw new ArgumentException("A share denominator is required.", nameof(basis));
        var rows = families.ToArray();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key) || string.IsNullOrWhiteSpace(row.Label)
                || !double.IsFinite(row.MeanPercent) || row.MeanPercent < 0 || row.MeanPercent > 100
                || row.RunCount < 0 || row.MinimumProteinCount < 0 || row.UniqueProteinCount < 0
                || row.UniqueProteinCount > row.MinimumProteinCount
                || (row.SdPercent is double sd && (!double.IsFinite(sd) || sd < 0 || row.RunCount < 2)))
                throw new ArgumentException("Invalid family share, SD or count.", nameof(families));
            if (row.Subclasses.Any(p => !double.IsFinite(p.Percent) || p.Percent < 0 || string.IsNullOrWhiteSpace(p.Label))
                || (row.Subclasses.Count > 0 && Math.Abs(row.Subclasses.Sum(p => p.Percent) - row.MeanPercent) > 1e-8))
                throw new ArgumentException("Subclass parts must sum to their family share.", nameof(families));
            if (row.Subclasses.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() != row.Subclasses.Count)
                throw new ArgumentException("Subclass keys must be unique within a family.", nameof(families));
        }
        if (rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw new ArgumentException("Family keys must be unique.", nameof(families));
        Families = Array.AsReadOnly(rows.Select(r => r with { Subclasses = Array.AsReadOnly(r.Subclasses.ToArray()) }).ToArray());
        Basis = basis;
    }
}
