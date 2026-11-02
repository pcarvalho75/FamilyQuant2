using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

/// <summary>Common drawing primitives. Concrete charts retain their geometry and scientific basis.</summary>
public abstract class ChartDrawingBase : FrameworkElement
{
    protected ChartPalette palette = ChartPalette.Dark;

    protected ChartDrawingBase()
    {
        Loaded += ChartLoaded;
        Unloaded += ChartUnloaded;
    }

    private void ChartLoaded(object sender, RoutedEventArgs e)
    {
        // Detached charts retain the historical palettes for deterministic exports.
        // Only a chart hosted in a view follows the application tokens.
        Themes.ThemeManager.ThemeChanged -= RefreshTheme;
        Themes.ThemeManager.ThemeChanged += RefreshTheme;
        RefreshTheme(null, EventArgs.Empty);
    }

    private void ChartUnloaded(object sender, RoutedEventArgs e) =>
        Themes.ThemeManager.ThemeChanged -= RefreshTheme;

    private void RefreshTheme(object? sender, EventArgs e) => Palette = ChartPalette.UiPalette;

    public ChartPalette Palette
    {
        get => palette;
        set { palette = value ?? ChartPalette.Dark; InvalidateVisual(); }
    }

    protected static FormattedText Text(string s, Brush brush, double size, bool bold = false) =>
        new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, brush, 96);

    protected void DrawBackdrop(DrawingContext dc, Brush brush) =>
        dc.DrawRectangle(brush, null, new Rect(0, 0, ActualWidth, ActualHeight));

    protected static void DrawAxes(DrawingContext dc, Rect plot, Pen pen)
    {
        dc.DrawLine(pen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        dc.DrawLine(pen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
    }

    protected static void DrawHorizontalTick(DrawingContext dc, Rect plot, double y,
        Pen pen, string label, Brush ink)
    {
        dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
        var text = Text(label, ink, 11);
        dc.DrawText(text, new Point(plot.Left - text.Width - 8, y - text.Height / 2));
    }

    protected static void DrawEmpty(DrawingContext dc, string message, Brush ink, Point at) =>
        dc.DrawText(Text(message, ink, 12), at);
}
