using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

/// <summary>Each family's subclass shares on the same denominator as the family composition chart.</summary>
public sealed class FamilySubclassChart : CompositionDrawingBase, IFigureDetails
{
    private readonly FamilyCompositionData data;
    public FamilyCompositionData Data => data;
    public IReadOnlyList<FamilyCompositionValue> Rows { get; }
    public int ReferenceLimit { get; }
    public static double RowHeight(FamilyCompositionValue row) => Math.Max(64, 37 + Math.Min(row.Subclasses.Count, FigureLayout.SubclassReferences) * 18);
    private static double FullRowHeight(FamilyCompositionValue row) => Math.Max(64, 37 + row.Subclasses.Count * 18);
    private static bool NeedsLimit(FamilyCompositionData data) => 190 + data.Families.Sum(FullRowHeight) > FigureLayout.MaximumUncappedHeight;
    public static double RecommendedHeight(FamilyCompositionData data) => Math.Max(540, NeedsLimit(data)
        ? 190 + LimitedRows(data).Sum(RowHeight) : 190 + data.Families.Sum(FullRowHeight));
    private static IReadOnlyList<FamilyCompositionValue> LimitedRows(FamilyCompositionData data) =>
        FigureLayout.Rows(data, RowHeight, forceLimit: true);
    public string DetailNotice => Rows.Count < data.Families.Count || Rows.Any(r => r.Subclasses.Count > ReferenceLimit)
        ? "Showing " + Rows.Count + " of " + data.Families.Count + " families by share; up to " + FigureLayout.SubclassReferences + " subclass references per row. " + FigureLayout.FullReference : "";
    public FamilySubclassChart(FamilyCompositionData data)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        if (data.IsMeanOfRuns) throw new ArgumentException("Subclass charts require all runs pooled.", nameof(data));
        ReferenceLimit = NeedsLimit(data) ? FigureLayout.SubclassReferences : int.MaxValue;
        Rows = NeedsLimit(data) ? LimitedRows(data) : data.Families;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Heading(dc, "Composition by subclass", data.Basis);
        if (Rows.Count == 0) { Label(dc, "No stored subclass shares are available.", 26, 104, ActualWidth - 52); return; }
        double left = Math.Min(260, ActualWidth * .29), right = ActualWidth - 96;
        if (right <= left) return;
        double maximum = Math.Max(10, Math.Ceiling(Rows.Max(r => r.MeanPercent) / 10) * 10);
        double y = 108;
        // Row height grows with the number of subclass labels; no class is silently omitted.
        double Height(FamilyCompositionValue row) => ReferenceLimit == int.MaxValue ? FullRowHeight(row) : RowHeight(row);
        double bottom = 108 + Rows.Sum(Height);
        PercentAxis(dc, left, right, 96, bottom, maximum);
        foreach (var row in Rows)
        {
            Label(dc, row.Label, 26, y + 2, left - 42, 13);
            double x = left;
            for (int i = 0; i < row.Subclasses.Count; i++)
            {
                var part = row.Subclasses[i];
                double width = (right - left) * part.Percent / maximum;
                Brush fill = string.IsNullOrEmpty(part.Key) ? palette.Remainder : Series(i);
                if (width > 0) dc.DrawRectangle(fill, DetailNotice.Length == 0 || width >= 2 ? new Pen(palette.Backdrop, .7) : null, new Rect(x, y, width, 23));
                if (i < ReferenceLimit)
                {
                    dc.DrawRectangle(fill, null, new Rect(left, y + 33 + i * 18, 7, 7));
                    Label(dc, part.Label + ": " + Number(part.Percent) + "%", left + 14, y + 29 + i * 18,
                        right - left - 14, 11, brush: palette.Muted);
                }
                x += width;
            }
            if (row.Subclasses.Count == 0)
            {
                dc.DrawRectangle(palette.Remainder, null, new Rect(left, y, (right - left) * row.MeanPercent / maximum, 23));
                Label(dc, "Subclass breakdown unavailable", left, y + 29, right - left, 11, brush: palette.Muted);
            }
            Label(dc, Number(row.MeanPercent) + "%", right + 12, y + 2, 70, 12, true);
            y += Height(row);
        }
        if (DetailNotice.Length > 0) Label(dc, DetailNotice, 26, ActualHeight - 46, ActualWidth - 52, 11, brush: palette.Muted);
        Label(dc, "Subclass parts add to each family. Grey marks unresolved parts or an unavailable breakdown, as labelled.",
            26, ActualHeight - 25, ActualWidth - 52, 11, brush: palette.Muted);
    }
}
