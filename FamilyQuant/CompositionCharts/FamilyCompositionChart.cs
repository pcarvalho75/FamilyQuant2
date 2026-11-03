using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

/// <summary>Family means with sample SD whiskers supplied by the quantitation result.</summary>
public sealed class FamilyCompositionChart : CompositionDrawingBase
{
    private readonly FamilyCompositionData data;
    public FamilyCompositionData Data => data;
    public IReadOnlyList<FamilyCompositionValue> Rows => data.Families;
    public double AxisMinimum => Rows.Count == 0 ? 0 : Math.Min(0, Math.Floor(Rows.Min(r => r.MeanPercent - (r.SdPercent ?? 0)) / 10) * 10);
    public FamilyCompositionChart(FamilyCompositionData data) => this.data = data ?? throw new ArgumentNullException(nameof(data));

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Heading(dc, "Family composition", data.Basis);
        if (Rows.Count == 0) { Label(dc, "No stored family shares are available.", 26, 104, ActualWidth - 52); return; }
        double left = Math.Min(260, ActualWidth * .29), right = ActualWidth - 178;
        if (right <= left) return;
        double bottom = ActualHeight - 64, step = Math.Min(52, (bottom - 102) / Rows.Count);
        double maximum = Math.Max(10, Math.Ceiling(Rows.Max(r => r.MeanPercent + (r.SdPercent ?? 0)) / 10) * 10);
        double minimum = AxisMinimum;
        double X(double value) => left + (right - left) * (value - minimum) / (maximum - minimum);
        PercentAxis(dc, left, right, 96, 102 + Rows.Count * step, maximum, minimum);
        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i]; double y = 106 + i * step;
            Label(dc, row.Label, 26, y + 2, left - 42, 13);
            double x = X(row.MeanPercent), zero = X(0);
            dc.DrawRoundedRectangle(palette.Identified, null, new Rect(zero, y, Math.Max(0, x - zero), 23), 2, 2);
            if (row.SdPercent is double sd)
            {
                // Preserve a lower whisker below zero; it is a spread, not a negative observation.
                double lo = X(row.MeanPercent - sd);
                double hi = X(row.MeanPercent + sd);
                dc.DrawLine(new Pen(palette.Ink, 1.5), new Point(lo, y + 11.5), new Point(hi, y + 11.5));
                foreach (double end in new[] { lo, hi })
                    dc.DrawLine(new Pen(palette.Ink, 1.5), new Point(end, y + 6), new Point(end, y + 17));
            }
            Label(dc, Number(row.MeanPercent) + "%", right + 18, y - 1, 140, 13, true);
            Label(dc, !data.IsMeanOfRuns ? "Pooled share" : row.SdPercent is double s ? "SD " + Number(s) + "; n = " + row.RunCount
                : "SD unavailable; n = " + row.RunCount, right + 18, y + 17, 150, 10, brush: palette.Muted);
        }
        string note = data.IsMeanOfRuns ? "Bars: mean share. Whiskers: +/- 1 SD across runs; no whisker when SD is unavailable."
            : "Bars: pooled credited signal shares. Run-to-run uncertainty is not shown on this basis.";
        if (data.UnavailableFamilies.Count > 0) note += " Unavailable shares: " + string.Join(", ", data.UnavailableFamilies) + ".";
        Label(dc, note,
            26, ActualHeight - 25, ActualWidth - 52, 11, brush: palette.Muted);
    }
}
