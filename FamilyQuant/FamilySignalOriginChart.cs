using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant;

/// <summary>Geometry over supplied Core amounts. Shared and unexplained signal are never put into family bars.</summary>
public sealed class FamilySignalOriginChart : ChartDrawingBase, IPalettedChart
{
    private static readonly IReadOnlyList<string> sourceLabels = Array.AsReadOnly(new[]
        { "Identified", "De novo in database", "De novo only in UniProt families" });
    private readonly bool invariantNumbers;
    public IReadOnlyList<string> SourceLabels => sourceLabels;
    public IReadOnlyList<FamilyWorldCompositionRow> Rows { get; }
    public FamilySignalOriginChart(IReadOnlyList<FamilyWorldCompositionRow> rows) : this(rows, false) { }
    private FamilySignalOriginChart(IReadOnlyList<FamilyWorldCompositionRow> rows, bool invariantNumbers)
    {
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        this.invariantNumbers = invariantNumbers;
        Height = Math.Max(160, 130 + 30 * rows.Count);
    }

    /// <summary>Render all supplied stored rows in the pinned Light palette for the report.</summary>
    public static byte[] RenderReportPng(IReadOnlyList<FamilyWorldCompositionRow> rows)
    {
        var chart = new FamilySignalOriginChart(rows, true);
        var image = ChartImageExporter.Render(chart, 960, chart.Height, variant: ChartPaletteVariant.Light);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); DrawBackdrop(dc, Palette.Backdrop);
        dc.DrawText(Text("Family signal by source", Palette.Ink, 14, true), new(8, 5));
        dc.DrawText(Text("Envelope area; shared and unexplained signal are separate from family bars.", Palette.Muted, 11), new(8, 27));
        Brush[] brushes = { Palette.Identified, Palette.DeNovo, Palette.NotQuantified };
        double legendX = 8;
        for (int i = 0; i < sourceLabels.Count; i++)
        {
            var label = Text(sourceLabels[i], Palette.Ink, 11);
            dc.DrawRectangle(brushes[i], null, new Rect(legendX, 51, 12, 12));
            dc.DrawText(label, new Point(legendX + 17, 49));
            legendX += label.Width + 40;
        }
        if (Rows.Count == 0) { DrawEmpty(dc, "No family-specific signal is stored.", Palette.Muted, new(8, 78)); return; }
        double left = Math.Min(280, ActualWidth * .34), right = 85, plotWidth = Math.Max(1, ActualWidth - left - right);
        double largest = Rows.Max(row => row.TotalEnvelope);
        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i]; double y = 80 + 30 * i;
            var label = Text(DatabaseGapDisplayRow.Label(row), Palette.Ink, 11); label.MaxTextWidth = Math.Max(20, left - 16);
            label.Trimming = TextTrimming.CharacterEllipsis; label.MaxLineCount = 1;
            dc.DrawText(label, new(8, y + 3));
            double x = left;
            foreach (var (amount, brush) in new[] { (row.IdentifiedEnvelope, Palette.Identified),
                (row.DatabaseDeNovoEnvelope, Palette.DeNovo), (row.WorldDeNovoEnvelope, Palette.NotQuantified) })
            {
                double width = largest > 0 ? plotWidth * amount / largest : 0;
                if (width > 0) dc.DrawRectangle(brush, null, new Rect(x, y, width, 19));
                x += width;
            }
            dc.DrawText(Text(row.TotalEnvelope.ToString("G5", invariantNumbers ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture), Palette.Muted, 10), new(left + plotWidth + 8, y + 3));
        }
    }
}
