using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

/// <summary>Concentric proportional rings from one pooled partition; original small-family members stay accessible.</summary>
public sealed partial class FamilyRingChart : CompositionDrawingBase, IFigureDetails
{
    private readonly FamilyCompositionData data;
    public FamilyCompositionData Data => data;
    private readonly FamilyRingGrouping grouping;
    public IReadOnlyList<FamilyRingGroup> Families { get; }
    public IReadOnlyList<FamilyRingReference> Reference { get; }
    public string DetailNotice => IsPartial(Families, Reference)
        ? "Reference: " + Reference.Count + " of " + Families.Count + " groups, " + Reference.Sum(r => r.PartCount)
            + " of " + Families.Sum(f => f.Parts.Count) + " parts; all arcs retained. " + FigureLayout.FullReference : "";
    public static double RecommendedHeight(FamilyCompositionData data, FamilyRingGrouping grouping)
    {
        var families = FamilyRingPlot.Build(data, grouping); var reference = BuildReference(families);
        return Math.Max(540, (IsPartial(families, reference) ? 190 : 160) + reference.Sum(r => 33 + 19 * r.PartCount));
    }

    public FamilyRingChart(FamilyCompositionData data, FamilyRingGrouping grouping)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.grouping = grouping;
        Families = FamilyRingPlot.Build(data, grouping);
        Reference = BuildReference(Families);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Heading(dc, "Families and subclasses", data.Basis);
        if (Families.Count == 0) { Label(dc, "No pooled family shares are available.", 26, 104, ActualWidth - 52); return; }
        double legendLeft = ActualWidth * .54;
        double radius = Math.Min(180, Math.Min(ActualWidth * .23, (ActualHeight - 230) / 2));
        if (radius <= 0) return;
        var center = new Point(ActualWidth * .265, 112 + radius);
        double angle = -90, legendY = 105;
        for (int i = 0; i < Families.Count; i++)
        {
            var family = Families[i];
            double sweep = family.Percent * 3.6;
            Brush familyFill = family.IsUnassigned ? palette.Remainder : Series(i);
            if (sweep > 0)
                dc.DrawGeometry(familyFill, DetailNotice.Length == 0 || sweep >= 2 ? new Pen(palette.Backdrop, 1.2) : null,
                    FamilyRingPlot.Annulus(center, radius * .48, radius * .74, angle, sweep));
            double partAngle = angle;
            for (int j = 0; j < family.Parts.Count; j++)
            {
                var part = family.Parts[j]; double partSweep = part.Percent * 3.6;
                Brush fill = part.IsUnresolved ? palette.Remainder : Series(i + j);
                if (partSweep > 0)
                    dc.DrawGeometry(fill, DetailNotice.Length == 0 || partSweep >= 2 ? new Pen(palette.Backdrop, 1) : null,
                        FamilyRingPlot.Annulus(center, radius * .78, radius, partAngle, partSweep));
                partAngle += partSweep;
            }
            // Only label arcs with adequate room. The data supplement retains every reference value.
            if (sweep >= 18)
            {
                double middle = (angle + sweep / 2) * Math.PI / 180;
                var label = Text((i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), palette.SegmentLabel, 13, true);
                dc.DrawText(label, new Point(center.X + radius * .61 * Math.Cos(middle) - label.Width / 2,
                    center.Y + radius * .61 * Math.Sin(middle) - label.Height / 2));
            }
            angle += sweep;
        }
        foreach (var reference in Reference)
        {
            int i = reference.FamilyIndex; var family = Families[i];
            dc.DrawRectangle(family.IsUnassigned ? palette.Remainder : Series(i), null, new Rect(legendLeft, legendY + 4, 9, 9));
            Label(dc, (i + 1) + ". " + family.Label + "  " + Number(family.Percent) + "%",
                legendLeft + 18, legendY, ActualWidth - legendLeft - 44, 12, true);
            legendY += 24;
            for (int j = 0; j < reference.PartCount; j++)
            {
                var part = family.Parts[j];
                dc.DrawRectangle(part.IsUnresolved ? palette.Remainder : Series(i + j), null,
                    new Rect(legendLeft + 19, legendY + 4, 6, 6));
                Label(dc, part.Label + "  " + Number(part.Percent) + "%", legendLeft + 33, legendY,
                    ActualWidth - legendLeft - 59, 11, brush: palette.Muted);
                legendY += 19;
            }
            legendY += 9;
        }
        var total = Text("100%", palette.Ink, 28, true);
        dc.DrawText(total, new Point(center.X - total.Width / 2, center.Y - total.Height / 2 - 7));
        var pooled = Text("all runs pooled", palette.Muted, 10);
        dc.DrawText(pooled, new Point(center.X - pooled.Width / 2, center.Y + 24));
        Label(dc, "Inner: report groups. Outer: subclass parts.", 40, center.Y + radius + 22, legendLeft - 65, 12);
        Label(dc, "Both rings use the same credited-signal total.", 40, center.Y + radius + 41, legendLeft - 65, 11, brush: palette.Muted);
        if (DetailNotice.Length > 0) Label(dc, DetailNotice, 26, ActualHeight - 46, ActualWidth - 52, 11, brush: palette.Muted);
        Label(dc, "Others uses the report's mean-of-runs membership" + (grouping.ThresholdPercent is double threshold
            ? " (threshold " + Number(threshold) + "%)" : "") + ". Both rings use all runs pooled.",
            26, ActualHeight - 25, ActualWidth - 52, 11, brush: palette.Muted);
    }
}
