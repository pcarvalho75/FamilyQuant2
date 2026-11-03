using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

public sealed record FamilyRingPart(string Label, double Percent, bool IsUnresolved);
public sealed record FamilyRingGroup(string Label, double Percent, bool IsOthers,
    IReadOnlyList<FamilyCompositionValue> Members, IReadOnlyList<FamilyRingPart> Parts);

/// <summary>Membership supplied by the single report-threshold accessor. The ring never re-decides it.</summary>
public sealed class FamilyRingGrouping
{
    public IReadOnlyList<string> OthersFamilyKeys { get; }
    public double? ThresholdPercent { get; }
    public static FamilyRingGrouping FromReportThreshold(FamilyReportThresholdResult result) => new(
        result.OtherFamilies.Select(f => f.FamilyUserId.ToString(CultureInfo.InvariantCulture)), result.Threshold * 100);
    public FamilyRingGrouping(IEnumerable<string> othersFamilyKeys, double? thresholdPercent = null)
    {
        var keys = othersFamilyKeys.ToArray();
        if (keys.Any(string.IsNullOrWhiteSpace) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("Others membership keys must be distinct and nonempty.", nameof(othersFamilyKeys));
        if (thresholdPercent is double threshold && (!double.IsFinite(threshold) || threshold < 0 || threshold > 100))
            throw new ArgumentOutOfRangeException(nameof(thresholdPercent));
        OthersFamilyKeys = Array.AsReadOnly(keys); ThresholdPercent = thresholdPercent;
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
        if (othersKeys.Except(data.Families.Select(f => f.Key), StringComparer.Ordinal).Any())
            throw new ArgumentException("An Others member is absent from the pooled data.", nameof(grouping));
        if (data.Families.Count == 0) return Array.Empty<FamilyRingGroup>();
        if (Math.Abs(data.Families.Sum(f => f.MeanPercent) - 100) > 1e-8)
            throw new ArgumentException("Pooled family shares must sum to 100%; the ring does not normalize them.", nameof(data));
        var groups = new List<FamilyRingGroup>();
        FamilyRingGroup Group(string label, IReadOnlyList<FamilyCompositionValue> rows, bool others)
        {
            var parts = rows.SelectMany(row => row.Subclasses.Count == 0
                ? new[] { new FamilyRingPart((others ? row.Label + ": " : "") + "Breakdown unavailable", row.MeanPercent, true) }
                : row.Subclasses.Select(p => new FamilyRingPart((others ? row.Label + ": " : "") + p.Label, p.Percent, p.Key.Length == 0)))
                .ToArray();
            return new FamilyRingGroup(label, rows.Sum(r => r.MeanPercent), others, Array.AsReadOnly(rows.ToArray()), Array.AsReadOnly(parts));
        }
        foreach (var row in data.Families.Where(r => !othersKeys.Contains(r.Key)))
            groups.Add(Group(row.Label, new[] { row }, false));
        var small = data.Families.Where(r => othersKeys.Contains(r.Key)).ToArray();
        if (small.Length > 0) groups.Add(Group("Others (" + small.Length + (small.Length == 1 ? " family)" : " families)"), small, true));
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
