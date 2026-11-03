using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

public sealed record FamilyDiversityPoint(string Key, string Label, double SharePercent, int ProteinCount, int Index);

/// <summary>Abundance is supplied mean share, diversity is the supplied greedy explanatory protein count.</summary>
public sealed class FamilyAbundanceDiversityChart : CompositionDrawingBase
{
    private readonly FamilyCompositionData data;
    public IReadOnlyList<FamilyDiversityPoint> Points { get; }
    public IReadOnlyList<FamilyCompositionValue> UnavailableFamilies { get; }
    public static double RecommendedHeight(FamilyCompositionData data) =>
        Math.Max(540, 160 + (data.Families.Count + data.UnavailableFamilies.Count) * 31);

    public FamilyAbundanceDiversityChart(FamilyCompositionData data)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        Points = Array.AsReadOnly(data.Families.Select((r, i) => (r, i)).Where(p => p.r.GreedyProteinCount.HasValue)
            .Select(p => new FamilyDiversityPoint(p.r.Key, p.r.Label, p.r.MeanPercent, p.r.GreedyProteinCount!.Value, p.i + 1)).ToArray());
        UnavailableFamilies = Array.AsReadOnly(data.Families.Where(r => !r.GreedyProteinCount.HasValue).ToArray());
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Heading(dc, "Abundance and diversity", data.Basis);
        if (data.Families.Count == 0) { Label(dc, "No stored family shares are available.", 26, 104, ActualWidth - 52); return; }
        double legendLeft = ActualWidth * .65;
        var plot = new Rect(72, 120, Math.Max(1, legendLeft - 112), Math.Max(1, Math.Min(430, ActualHeight - 225)));
        double xmax = Math.Max(10, Math.Ceiling(Points.Select(p => p.SharePercent).DefaultIfEmpty(0).Max() / 10) * 10);
        int ymax = Math.Max(1, (int)Math.Ceiling(Points.Select(p => p.ProteinCount).DefaultIfEmpty(0).Max() * 1.15));
        PercentAxis(dc, plot.Left, plot.Right, plot.Top, plot.Bottom, xmax);
        foreach (int tick in Enumerable.Range(0, 5).Select(i => (int)Math.Round(ymax * i / 4.0)).Distinct())
        {
            double y = plot.Bottom - plot.Height * tick / ymax;
            dc.DrawLine(new Pen(palette.Grid, .5), new Point(plot.Left, y), new Point(plot.Right, y));
            Label(dc, tick.ToString(System.Globalization.CultureInfo.CurrentCulture), 26, y - 7, 38, 11, brush: palette.Muted);
        }
        Label(dc, "Proteins needed to explain the peptides (greedy estimate)", plot.Left, 94, plot.Width, 11, true);
        Label(dc, "Mean family share (%)", plot.Left, plot.Bottom + 38, plot.Width, 12);
        DrawAxes(dc, plot, new Pen(palette.Axis, .8));
        var placed = new List<Rect>();
        foreach (var group in Points.GroupBy(p => (p.SharePercent, p.ProteinCount)))
        {
            var first = group.First();
            var marker = new Point(plot.Left + plot.Width * first.SharePercent / xmax,
                plot.Bottom - plot.Height * first.ProteinCount / ymax);
            dc.DrawEllipse(palette.Identified, new Pen(palette.Backdrop, 1), marker, 5, 5);
            string text = group.Count() == 1 ? first.Index.ToString(System.Globalization.CultureInfo.CurrentCulture)
                : group.Count().ToString(System.Globalization.CultureInfo.CurrentCulture) + " families";
            var ft = Text(text, palette.Ink, 11);
            Rect? label = null;
            foreach (int dy in new[] { -24, 10, -42, 28, -60, 46 })
            {
                var box = new Rect(Math.Clamp(marker.X + 8, plot.Left, Math.Max(plot.Left, plot.Right - ft.Width - 6)),
                    Math.Clamp(marker.Y + dy, plot.Top, Math.Max(plot.Top, plot.Bottom - ft.Height - 4)), ft.Width + 6, ft.Height + 4);
                if (!placed.Any(p => p.IntersectsWith(box))) { label = box; break; }
            }
            if (label is Rect bounds)
            {
                placed.Add(bounds);
                dc.DrawLine(new Pen(palette.Axis, .6), marker, new Point(bounds.Left, bounds.Top + bounds.Height / 2));
                dc.DrawRectangle(palette.Backdrop, null, bounds);
                dc.DrawText(ft, new Point(bounds.Left + 3, bounds.Top + 1));
            }
        }
        Label(dc, "Family reference", legendLeft, 94, ActualWidth - legendLeft - 26, 12, true);
        for (int i = 0; i < data.Families.Count; i++)
        {
            var row = data.Families[i]; double y = 120 + i * 31;
            Label(dc, (i + 1) + ". " + row.Label, legendLeft, y, ActualWidth - legendLeft - 26, 12);
            string count = row.GreedyProteinCount is int n ? n + " proteins (greedy estimate)" : "protein count unavailable";
            if (row.UnexplainedPeptideCount > 0) count += "; unexplained peptides: " + row.UnexplainedPeptideCount;
            Label(dc, Number(row.MeanPercent) + "% | " + count, legendLeft, y + 15, ActualWidth - legendLeft - 26, 10, brush: palette.Muted);
        }
        for (int i = 0; i < data.UnavailableFamilies.Count; i++)
            Label(dc, data.UnavailableFamilies[i] + ": share unavailable", legendLeft,
                120 + (data.Families.Count + i) * 31, ActualWidth - legendLeft - 26, 11, brush: palette.Muted);
        Label(dc, "Counts approximate the minimum database-protein set. Coincident points are retained; missing counts are listed, not plotted.",
            26, ActualHeight - 25, ActualWidth - 52, 11, brush: palette.Muted);
    }
}
