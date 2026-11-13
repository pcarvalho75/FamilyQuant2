using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FamilyQuant;

/// <summary>A filled pie over the same pooled buckets that the Results bars and HTML use.</summary>
public sealed class FamilyPieChart : CompositionDrawingBase
{
    private readonly List<(Geometry Shape, Rect Label)> targets = new();
    private int selected = -1;
    public FamilyPieData Data { get; }

    /// <summary>Legend lines drawn for the current render, in slice order. Each starts with its position.</summary>
    public IReadOnlyList<string> LegendLines { get; private set; } = [];

    /// <summary>Positions whose slices were wide enough to carry that same number.</summary>
    public IReadOnlyList<int> NumberedSlices { get; private set; } = [];

    public FamilyPieChart(FamilyPieData data)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Focusable = true;
        ToolTip = data.Unavailable ?? "Read family, other-family, shared and unattributed shares of all measured peptide features, with all runs pooled. Hover a slice or use the arrow keys to inspect exact values; this changes only the view.";
    }

    public static double RecommendedHeight(FamilyPieData data) => Math.Max(420, 160 + data.Slices.Count * 23);

    public string SliceDetails(int index)
    {
        var slice = Data.Slices[index];
        string Percent(double value) => value.ToString("G17", CultureInfo.CurrentCulture) + "%";
        return slice.Label + ": " + Percent(slice.Percent) + " of all measured peptide-feature signal. Identified "
            + Percent(slice.IdentifiedPercent) + ", de novo " + Percent(slice.DeNovoPercent) + "; all runs pooled.";
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Heading(dc, "Family contribution: Pie", "Percent of all measured peptide-feature signal; all runs pooled");
        targets.Clear();
        var legend = new List<string>();
        var numbered = new List<int>();
        LegendLines = legend;
        NumberedSlices = numbered;
        if (Data.Unavailable != null)
        {
            Label(dc, Data.Unavailable, 26, 110, ActualWidth - 52);
            return;
        }
        double radius = Math.Min(180, Math.Min(ActualWidth * .21, (ActualHeight - 160) / 2));
        if (radius <= 0) return;
        var center = new Point(ActualWidth * .25, 100 + radius);
        double legendLeft = ActualWidth * .51, start = -90;
        for (int index = 0; index < Data.Slices.Count; index++)
        {
            var slice = Data.Slices[index];
            double sweep = slice.Percent * 3.6;
            string number = (index + 1).ToString(CultureInfo.CurrentCulture);
            string line = number + ". " + slice.Label + "  " + slice.Percent.ToString("G3", CultureInfo.CurrentCulture) + "%";
            legend.Add(line);
            Geometry shape = sweep >= 360 ? new EllipseGeometry(center, radius, radius) : Wedge(center, radius, start, sweep);
            var fill = slice.Kind == FamilyContributionBarKind.Family ? Series(index) : palette.Remainder;
            if (sweep > 0) dc.DrawGeometry(fill, new Pen(selected == index ? palette.Ink : palette.Backdrop, selected == index ? 2.5 : 1.2), shape);
            // The arc at the label radius has to be wider than the digits. Smaller slices stay in the legend.
            var mark = Text(number, palette.SegmentLabel, 13, true);
            double midRadius = radius * .62;
            if (sweep > 0 && sweep * Math.PI / 180 * midRadius >= mark.Width + 4)
            {
                numbered.Add(index + 1);
                double middle = (start + sweep / 2) * Math.PI / 180;
                dc.DrawText(mark, new Point(center.X + midRadius * Math.Cos(middle) - mark.Width / 2,
                    center.Y + midRadius * Math.Sin(middle) - mark.Height / 2));
            }
            double top = 100 + index * 23;
            dc.DrawRectangle(fill, null, new Rect(legendLeft, top + 4, 10, 10));
            Label(dc, line, legendLeft + 18, top, ActualWidth - legendLeft - 44, 12, selected == index);
            targets.Add((shape, new Rect(legendLeft, top, Math.Max(1, ActualWidth - legendLeft - 26), 23)));
            start += sweep;
        }
        if (selected >= 0 && selected < Data.Slices.Count)
            Label(dc, SliceDetails(selected), 26, ActualHeight - 34, ActualWidth - 52, 11, brush: palette.Muted);
    }

    private static Geometry Wedge(Point center, double radius, double start, double sweep)
    {
        if (sweep <= 0) return Geometry.Empty;
        Point At(double angle) => new(center.X + radius * Math.Cos(angle * Math.PI / 180), center.Y + radius * Math.Sin(angle * Math.PI / 180));
        var shape = new StreamGeometry();
        using (var context = shape.Open())
        {
            context.BeginFigure(center, true, true);
            context.LineTo(At(start), true, false);
            context.ArcTo(At(start + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        shape.Freeze();
        return shape;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        int index = targets.FindIndex(target => target.Label.Contains(point) || target.Shape.FillContains(point));
        if (index == selected) return;
        selected = index;
        ToolTip = index >= 0 ? SliceDetails(index) : Data.Unavailable ?? "Hover a slice or use the arrow keys to read its exact share. This changes only the view.";
        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (Data.Slices.Count > 0 && selected < 0) selected = 0;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Data.Slices.Count > 0 && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
        {
            if (e.Key == Key.Home) selected = 0;
            else if (e.Key == Key.End) selected = Data.Slices.Count - 1;
            else
            {
                int direction = e.Key is Key.Left or Key.Up ? -1 : 1;
                selected = (Math.Max(0, selected) + direction + Data.Slices.Count) % Data.Slices.Count;
            }
            ToolTip = SliceDetails(selected);
            InvalidateVisual();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
