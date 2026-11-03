using System;
using FamilyQuant.Report.Captions;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

/// <summary>Shared typography and restrained, non-yellow series for the composition figures.</summary>
public abstract class CompositionDrawingBase : ChartDrawingBase, IPalettedChart
{
    protected void Label(DrawingContext dc, string text, double x, double y, double width,
        double size = 12, bool bold = false, Brush? brush = null)
    {
        if (width < 1) return;
        var value = Text(text, brush ?? palette.Ink, size, bold);
        value.MaxTextWidth = width;
        value.MaxLineCount = 1;
        value.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(value, new Point(x, y));
    }

    protected void Heading(DrawingContext dc, string title, string subtitle)
    {
        DrawBackdrop(dc, palette.Backdrop);
        Label(dc, title, 26, 20, ActualWidth - 52, 21, true);
        Label(dc, subtitle, 26, 54, ActualWidth - 52, 12, brush: palette.Muted);
    }

    // Ordinary keyed series never use grey; unresolved parts and unassigned ring categories do.
    protected Brush Series(int index) => (index % 4) switch
    {
        0 => palette.Identified, 1 => palette.Quantified, 2 => palette.NotQuantified,
        _ => palette.DeNovo
    };

    protected static string Number(double value, string? format = null) =>
        ReportCaptionBuilder.Number(value, CultureInfo.CurrentCulture, format);

    protected void PercentAxis(DrawingContext dc, double left, double right, double top, double bottom, double maximum,
        double minimum = 0)
    {
        for (int i = 0; i <= 4; i++)
        {
            double x = left + (right - left) * i / 4;
            if (i > 0) dc.DrawLine(new Pen(palette.Grid, .5), new Point(x, top), new Point(x, bottom));
            Label(dc, Number(minimum + (maximum - minimum) * i / 4) + "%", x - (i == 0 ? 0 : 18), bottom + 12, 70, 11, brush: palette.Muted);
        }
    }
}
