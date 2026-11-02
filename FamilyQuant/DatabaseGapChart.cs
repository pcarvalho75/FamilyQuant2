using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

/// <summary>Geometry over supplied Core amounts. Shared and unexplained signal are never put into family bars.</summary>
public sealed class DatabaseGapChart : ChartDrawingBase, IPalettedChart
{
    public IReadOnlyList<FamilyWorldCompositionRow> Rows { get; }
    public DatabaseGapChart(IReadOnlyList<FamilyWorldCompositionRow> rows)
    {
        Rows = rows;
        Height = Math.Max(90, 50 + 30 * rows.Count);
        Palette = ChartPalette.UiPalette;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); DrawBackdrop(dc, Palette.Backdrop);
        if (Rows.Count == 0) { DrawEmpty(dc, "No family-specific signal is stored; the global rows remain below.", Palette.Muted, new(8, 16)); return; }
        double left = Math.Min(280, ActualWidth * .34), right = 85, plotWidth = Math.Max(1, ActualWidth - left - right);
        double largest = Rows.Max(row => row.TotalEnvelope);
        dc.DrawText(Text("Family bars ranked by database gap", Palette.Ink, 12, true), new(8, 5));
        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i]; double y = 35 + 30 * i;
            var label = Text(DatabaseGapDisplayRow.Label(row), Palette.Ink, 11); label.MaxTextWidth = Math.Max(20, left - 16);
            label.Trimming = TextTrimming.CharacterEllipsis; label.MaxLineCount = 1;
            dc.DrawText(label, new(8, y + 3));
            double x = left;
            foreach (var (amount, brush) in new[] { (row.IdentifiedEnvelope, Palette.Identified),
                (row.DatabaseDeNovoEnvelope, Palette.DeNovo), (row.WorldDeNovoEnvelope, Palette.MassBands[2]) })
            {
                double width = largest > 0 ? plotWidth * amount / largest : 0;
                if (width > 0) dc.DrawRectangle(brush, null, new Rect(x, y, width, 19));
                x += width;
            }
            dc.DrawText(Text(row.TotalEnvelope.ToString("G5", CultureInfo.CurrentCulture), Palette.Muted, 10), new(left + plotWidth + 8, y + 3));
        }
    }
}
