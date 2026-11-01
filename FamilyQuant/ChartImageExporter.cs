using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant
{
    /// <summary>
    /// Turns any house chart into a presentation-quality PNG.
    ///
    /// The charts are <see cref="FrameworkElement"/>s that draw themselves in OnRender, so they can be
    /// measured, arranged and rendered with NO window on screen - the technique the test suite has used
    /// for the identity-sweep chart since it was written, lifted here so the application can offer it
    /// too instead of the figures having to be redrawn by hand for a talk.
    ///
    /// RESOLUTION. The layout is done in DIP - <see cref="DefaultLayoutWidth"/> by
    /// <see cref="DefaultLayoutHeight"/>, a 16:9 slide - and the bitmap is rendered at
    /// <see cref="DefaultScale"/> times that dot pitch, which is 1920x1080 device pixels: crisp on a
    /// projector, and the same picture the screen shows rather than a different layout at a larger size.
    /// Scaling the DOT PITCH and not the layout is what keeps it the same picture: text, strokes and bar
    /// heights all grow together, so nothing re-flows and no label that fitted on screen is cut off here.
    ///
    /// A chart that is already on screen is NOT the one to export: measuring and arranging an element
    /// that has a parent fights that parent's layout. Callers build a fresh chart over the same data,
    /// which is what <see cref="AnalyticsFigures"/> does.
    ///
    /// COLOURS. A figure that goes into a talk is projected onto a white slide, so the export paints in
    /// <see cref="ChartPaletteVariant.Light"/> by DEFAULT and the PNG comes out on a near-white ground.
    /// The screen keeps its dark palette; a caller who wants the screen's colours in a file asks for
    /// <see cref="ChartPaletteVariant.Dark"/> explicitly. The variant is applied by SETTING the chart's
    /// palette before the layout pass, which only works for a chart that implements
    /// <see cref="IPalettedChart"/>; anything else is rendered in whatever colours it paints itself, and
    /// is not silently refused.
    /// </summary>
    public static class ChartImageExporter
    {
        /// <summary>The palette a figure is written in unless the caller says otherwise: the slide one.</summary>
        public const ChartPaletteVariant DefaultVariant = ChartPaletteVariant.Light;

        /// <summary>Layout width in DIP: half of a 1920-pixel slide, so the default 2x scale lands on it.</summary>
        public const double DefaultLayoutWidth = 960;

        /// <summary>Layout height in DIP, 16:9 against <see cref="DefaultLayoutWidth"/>.</summary>
        public const double DefaultLayoutHeight = 540;

        /// <summary>Device pixels per DIP. Two gives 1920x1080 at the default layout size.</summary>
        public const double DefaultScale = 2.0;

        /// <summary>WPF's own dot pitch, the unit <see cref="DefaultScale"/> multiplies.</summary>
        private const double BaseDpi = 96.0;

        /// <summary>
        /// Lays the chart out off-screen and renders it. The returned bitmap is frozen, so it can cross a
        /// thread and be encoded more than once.
        /// </summary>
        /// <param name="chart">A chart NOT currently in a visual tree; see the type remarks.</param>
        /// <param name="layoutWidth">Layout width in DIP.</param>
        /// <param name="layoutHeight">Layout height in DIP.</param>
        /// <param name="scale">Device pixels per DIP.</param>
        /// <param name="variant">The palette to paint in; the slide (light) one by default. Ignored by a
        /// chart that does not implement <see cref="IPalettedChart"/>.</param>
        public static BitmapSource Render(FrameworkElement chart,
                                          double layoutWidth = DefaultLayoutWidth,
                                          double layoutHeight = DefaultLayoutHeight,
                                          double scale = DefaultScale,
                                          ChartPaletteVariant variant = DefaultVariant)
        {
            if (chart == null) throw new ArgumentNullException(nameof(chart));

            if (layoutWidth <= 0 || layoutHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(layoutWidth),
                    "The layout size must be positive, was " +
                    layoutWidth.ToString("R", CultureInfo.InvariantCulture) + "x" +
                    layoutHeight.ToString("R", CultureInfo.InvariantCulture) + ".");
            }

            if (scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale),
                    "The scale must be positive, was " + scale.ToString("R", CultureInfo.InvariantCulture) + ".");
            }

            // Set BEFORE the layout pass: a chart may size a band by what its palette makes it draw, and
            // a palette applied after Arrange would be measured against the other variant's layout.
            if (chart is IPalettedChart paletted) paletted.Palette = ChartPalette.For(variant);

            chart.Width = layoutWidth;
            chart.Height = layoutHeight;
            chart.Measure(new Size(layoutWidth, layoutHeight));
            chart.Arrange(new Rect(0, 0, layoutWidth, layoutHeight));
            chart.UpdateLayout();

            // Rounded, not truncated: 960 x 2.0 is exactly 1920 in binary, but a caller's 1.5 over an odd
            // layout width is not, and a bitmap one pixel short of what was asked for is a defect.
            int pixelWidth = Math.Max(1, (int)Math.Round(layoutWidth * scale));
            int pixelHeight = Math.Max(1, (int)Math.Round(layoutHeight * scale));

            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight,
                                                BaseDpi * scale, BaseDpi * scale, PixelFormats.Pbgra32);
            bitmap.Render(chart);
            bitmap.Freeze();

            return bitmap;
        }

        /// <summary>
        /// Renders the chart and writes it as a PNG. The directory is created when it does not exist, so
        /// a caller may name a fresh folder. Returns the path written, so a caller can report it.
        /// </summary>
        public static string Save(FrameworkElement chart, string path,
                                  double layoutWidth = DefaultLayoutWidth,
                                  double layoutHeight = DefaultLayoutHeight,
                                  double scale = DefaultScale,
                                  ChartPaletteVariant variant = DefaultVariant)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A path is required to write a PNG.", nameof(path));
            }

            BitmapSource bitmap = Render(chart, layoutWidth, layoutHeight, scale, variant);

            string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            using (var stream = File.Create(path))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }

            return path;
        }
    }
}
