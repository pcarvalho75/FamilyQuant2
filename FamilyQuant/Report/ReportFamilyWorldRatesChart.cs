using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant.Report;

/// <summary>Draws recorded rates only. Null stays unavailable; ratios are never clamped to 100%.</summary>
public sealed class ReportFamilyWorldRatesChart : ChartDrawingBase, IPalettedChart
{
    public IReadOnlyList<FamilyWorldFamilySummary> Rows { get; }

    public ReportFamilyWorldRatesChart(IReadOnlyList<FamilyWorldFamilySummary> rows)
    {
        Rows = rows;
        foreach (var row in rows)
            if (new[] { row.DecoyRate, row.RightRate }.Any(r => r.HasValue && (!double.IsFinite(r.Value) || r.Value < 0)))
                throw new ArgumentException("Stored family-world rates must be finite and nonnegative.", nameof(rows));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawBackdrop(dc, Palette.Backdrop);
        dc.DrawText(Text("Stored family-world rates", Palette.Ink, 16, true), new Point(18, 12));
        dc.DrawText(Text("Decoy signal / credited signal; correct known credits / known credits", Palette.Muted, 11), new Point(18, 39));
        if (Rows.Count == 0) { DrawEmpty(dc, "No family-world rates are stored.", Palette.Muted, new Point(18, 75)); return; }
        double labelWidth = Math.Min(280, ActualWidth * .3);
        double half = Math.Max(80, (ActualWidth - labelWidth - 90) / 2);
        double max = Math.Max(1, Rows.SelectMany(r => new[] { r.DecoyRate ?? 0, r.RightRate ?? 0 }).Max());
        dc.DrawText(Text("Decoy ratio", Palette.Muted, 11, true), new Point(labelWidth, 66));
        dc.DrawText(Text("Right rate", Palette.Muted, 11, true), new Point(labelWidth + half + 35, 66));
        for (int index = 0; index < Rows.Count; index++)
        {
            var row = Rows[index];
            double y = 95 + index * 32;
            var name = Text(row.Family, Palette.Ink, 11); name.MaxTextWidth = Math.Max(20, labelWidth - 25);
            name.Trimming = TextTrimming.CharacterEllipsis; name.MaxLineCount = 1;
            dc.DrawText(name, new Point(18, y));
            void Rate(double? value, double x, Brush brush)
            {
                if (!value.HasValue) { dc.DrawText(Text("Unavailable", Palette.Muted, 10), new Point(x, y)); return; }
                double width = (half - 70) * value.Value / max;
                if (width > 0) dc.DrawRectangle(brush, null, new Rect(x, y, width, 15));
                dc.DrawText(Text((100 * value.Value).ToString("0.##", CultureInfo.InvariantCulture) + "%", Palette.Ink, 10), new Point(x + width + 6, y));
            }
            Rate(row.DecoyRate, labelWidth, Palette.DeNovo);
            Rate(row.RightRate, labelWidth + half + 35, Palette.Identified);
        }
    }
}
