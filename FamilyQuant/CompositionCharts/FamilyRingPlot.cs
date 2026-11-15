using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

public sealed record FamilyRingPart(string Label, double Percent, bool IsUnresolved);
public sealed record FamilyRingGroup(string Label, double Percent, bool IsOthers,
    IReadOnlyList<FamilyCompositionValue> Members, IReadOnlyList<FamilyRingPart> Parts, bool IsUnassigned = false);

/// <summary>Membership supplied by the single report-threshold accessor. The ring never re-decides it.</summary>
public sealed class FamilyRingGrouping
{
    public IReadOnlyList<string> OthersFamilyKeys { get; }
    public IReadOnlyList<string> ConflictingFamilyKeys { get; }
    public IReadOnlyList<string> ExcludedFamilyKeys { get; }
    public IReadOnlyList<string> ContaminantFamilyKeys { get; }
    public double? ThresholdPercent { get; }
    public static FamilyRingGrouping FromReportThreshold(FamilyReportThresholdResult result) => new(
        result.OtherFamilies.Select(f => Key(f.FamilyUserId)), result.Threshold * 100,
        result.ConflictFamilies.Select(f => Key(f.Family.FamilyUserId)), result.ExcludedFamilies.Select(f => Key(f.FamilyUserId)),
        result.Contaminants.Families.Select(f => Key(f.FamilyUserId)));
    private static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);
    public FamilyRingGrouping(IEnumerable<string> othersFamilyKeys, double? thresholdPercent = null,
        IEnumerable<string>? conflictingFamilyKeys = null, IEnumerable<string>? excludedFamilyKeys = null, IEnumerable<string>? contaminantFamilyKeys = null)
    {
        var keys = othersFamilyKeys.ToArray();
        var conflicts = (conflictingFamilyKeys ?? Array.Empty<string>()).ToArray();
        var excluded = (excludedFamilyKeys ?? Array.Empty<string>()).ToArray();
        var contaminants = (contaminantFamilyKeys ?? Array.Empty<string>()).ToArray();
        var all = keys.Concat(conflicts).Concat(excluded).Concat(contaminants).ToArray();
        if (all.Any(string.IsNullOrWhiteSpace) || all.Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new ArgumentException("Ring membership keys must be distinct, disjoint and nonempty.", nameof(othersFamilyKeys));
        if (thresholdPercent is double threshold && (!double.IsFinite(threshold) || threshold < 0 || threshold > 100))
            throw new ArgumentOutOfRangeException(nameof(thresholdPercent));
        OthersFamilyKeys = Array.AsReadOnly(keys); ThresholdPercent = thresholdPercent;
        ConflictingFamilyKeys = Array.AsReadOnly(conflicts); ExcludedFamilyKeys = Array.AsReadOnly(excluded);
        ContaminantFamilyKeys = Array.AsReadOnly(contaminants);
    }
}

/// <summary>Groups only for display, with every original member retained. Never normalizes a non-closing result.</summary>
internal static class FamilyRingPlot
{
    internal static IReadOnlyList<FamilyRingGroup> Build(FamilyCompositionData data, FamilyRingGrouping grouping)
    {
        if (data.IsMeanOfRuns) throw new ArgumentException("Both rings require all runs pooled.", nameof(data));
        ArgumentNullException.ThrowIfNull(grouping);
        var othersKeys = grouping.OthersFamilyKeys.ToHashSet(StringComparer.Ordinal);
        var conflicts = grouping.ConflictingFamilyKeys.ToHashSet(StringComparer.Ordinal);
        var excluded = grouping.ExcludedFamilyKeys.ToHashSet(StringComparer.Ordinal);
        var contaminants = grouping.ContaminantFamilyKeys.ToHashSet(StringComparer.Ordinal);
        if (othersKeys.Concat(conflicts).Concat(excluded).Concat(contaminants).Except(data.Families.Select(f => f.Key), StringComparer.Ordinal).Any())
            throw new ArgumentException("A canonical ring member is absent from the pooled data.", nameof(grouping));
        if (data.Families.Count == 0) return Array.Empty<FamilyRingGroup>();
        if (Math.Abs(data.Families.Sum(f => f.MeanPercent) - 100) > 1e-8)
            throw new ArgumentException("Pooled family shares must sum to 100%; the ring does not normalize them.", nameof(data));
        var groups = new List<FamilyRingGroup>();
        FamilyRingGroup Group(string label, IReadOnlyList<FamilyCompositionValue> rows, bool others, bool unassigned = false)
        {
            var parts = rows.SelectMany(row => row.Subclasses.Count == 0
                ? new[] { new FamilyRingPart((others || unassigned ? row.Label + ": " : "") + "Breakdown unavailable", row.MeanPercent, true) }
                : row.Subclasses.Select(p => new FamilyRingPart((others || unassigned ? row.Label + ": " : "") + p.Label, p.Percent, unassigned || p.Key.Length == 0)))
                .ToArray();
            return new FamilyRingGroup(label, rows.Sum(r => r.MeanPercent), others, Array.AsReadOnly(rows.ToArray()), Array.AsReadOnly(parts), unassigned);
        }
        foreach (var row in data.Families.Where(r => !othersKeys.Contains(r.Key) && !conflicts.Contains(r.Key) && !excluded.Contains(r.Key) && !contaminants.Contains(r.Key)))
            groups.Add(Group(row.Label, new[] { row }, false));
        var small = data.Families.Where(r => othersKeys.Contains(r.Key)).ToArray();
        if (small.Length > 0) groups.Add(Group("Others (" + small.Length + (small.Length == 1 ? " family)" : " families)"), small, true));
        foreach (var (label, membership) in new[] { ("contaminants", contaminants), ("Conflicting families", conflicts), ("Not a panel family", excluded) })
        {
            var rows = data.Families.Where(r => membership.Contains(r.Key)).ToArray();
            if (rows.Length > 0) groups.Add(Group(label, rows, false, true));
        }
        return groups.AsReadOnly();
    }

    internal static Geometry Annulus(Point center, double inner, double outer, double startDegrees, double degrees)
    {
        if (degrees >= 360 - 1e-9)
            return new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(center, outer, outer), new EllipseGeometry(center, inner, inner));
        Point At(double radius, double angle) => new(center.X + radius * Math.Cos(angle * Math.PI / 180),
            center.Y + radius * Math.Sin(angle * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(At(outer, startDegrees), true, true);
            path.ArcTo(At(outer, startDegrees + degrees), new Size(outer, outer), 0, degrees > 180, SweepDirection.Clockwise, true, false);
            path.LineTo(At(inner, startDegrees + degrees), true, false);
            path.ArcTo(At(inner, startDegrees), new Size(inner, inner), 0, degrees > 180, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze(); return geometry;
    }
}
