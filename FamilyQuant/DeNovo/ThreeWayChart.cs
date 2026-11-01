using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// The three-way partition as one stacked bar per run, plus one for the three runs together.
    ///
    /// THE BAR IS THE RUN'S TOTAL ION CURRENT AND IT IS ALWAYS THE FULL WIDTH. That is the whole
    /// point of the picture: the reader is not comparing three quantities, they are seeing one
    /// quantity divided into what PatternLab explains, what the de novo addition explains on top,
    /// and what is still without an owner. Scaling the bars against each other instead would answer
    /// a question nobody asked and would make the runs incomparable.
    ///
    /// ONE BASE, M+0, ON BOTH NUMERATORS. The PatternLab quants are monoisotopic areas, so the de
    /// novo slice is monoisotopic too. The de novo envelope sum exists and is larger, and it is
    /// deliberately NOT drawn here: a bar with an envelope slice next to an M+0 slice compares two
    /// different measurements across one axis. It appears in the CSV, in its own column, labelled.
    ///
    /// ONE GRAIN, THE INTEGRATED PEAK, ON BOTH NUMERATORS TOO. The .xic2 holds one area per peptide,
    /// charge and run, so the de novo slice is summed over distinct extracted peaks and not over MS2
    /// anchors. The two slices would otherwise be built by different aggregation rules, and a segment
    /// that counts one elution peak once per MS2 event that sampled it draws longer than the ion
    /// current it stands for.
    ///
    /// Drawn with OnRender rather than a charting package for the same reason as
    /// <see cref="FamilyQuant.IdentityScanChart"/>: the application ships no such dependency, and a
    /// resize redraws for free.
    /// </summary>
    public class ThreeWayChart : ChartDrawingBase
    {
        private readonly List<ThreeWayPartitionRow> rows;
        private readonly string title;

        static readonly Brush Backdrop = new SolidColorBrush(Color.FromRgb(0x33, 0x39, 0x49));
        static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xF2, 0xF0, 0xE9));
        static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0xA8, 0xA4, 0x9A));
        static readonly Brush Grid = new SolidColorBrush(Color.FromRgb(0x44, 0x4B, 0x5C));
        static readonly Brush PatternLab = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xF0));
        static readonly Brush DeNovo = new SolidColorBrush(Color.FromRgb(0x3F, 0xD3, 0x9C));
        static readonly Brush Remainder = new SolidColorBrush(Color.FromRgb(0x6B, 0x73, 0x8A));
        static readonly Brush Negative = new SolidColorBrush(Color.FromRgb(0xE0, 0x4F, 0x5F));

        /// <summary>Left gutter, holding the run names.</summary>
        private const double NameGutter = 178;

        /// <summary>Right gutter, holding each bar's remainder share. Reserved on every row rather
        /// than only on the ones that need it, so the bars all end at the same x and their segments
        /// stay comparable by eye.</summary>
        private const double ValueGutter = 124;

        private const double TitleBand = 40;
        private const double LegendBand = 40;

        /// <summary>Gap above the aggregate bar, so it reads as a summary of the bars above it and
        /// not as a fourth run.</summary>
        private const double GroupedGap = 16;

        public ThreeWayChart(IEnumerable<ThreeWayPartitionRow>? rows, string? title = null)
        {
            var given = rows?.ToList() ?? new List<ThreeWayPartitionRow>();

            // The aggregate is appended here rather than expected from the caller, so the bar at the
            // bottom always summarises the bars above it.
            var all = given.Where(r => !r.IsGrouped).ToList();
            this.rows = new List<ThreeWayPartitionRow>(all);
            if (all.Count > 0) this.rows.Add(ThreeWayPartition.Grouped(all));

            this.title = string.IsNullOrWhiteSpace(title)
                ? "Ion current per run: identified, added by de novo, still without an owner"
                : title!;

            MinHeight = 220;
            Margin = new Thickness(0);
        }


        private static string Percent(double value) =>
            value.ToString("F2", CultureInfo.CurrentCulture) + "%";

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            // Its own backdrop, so the light ink is legible wherever the control is hosted and a
            // render to a bitmap shows what the window shows rather than light text on transparency.
            DrawBackdrop(dc, Backdrop);

            if (rows.Count == 0 || ActualWidth < 320 || ActualHeight < 140) return;

            var head = Text(title, Ink, 13, bold: true);
            dc.DrawText(head, new Point(14, 12));

            double barLeft = NameGutter;
            double barWidth = Math.Max(1, ActualWidth - NameGutter - ValueGutter);
            double top = TitleBand;
            double bottom = Math.Max(top + 1, ActualHeight - LegendBand);

            double gaps = (rows.Count - 1) * 10 + (rows.Count > 1 ? GroupedGap : 0);
            double barHeight = Math.Min(46, Math.Max(12, (bottom - top - gaps) / rows.Count));
            double step = barHeight + 10;

            double y = top;
            for (int i = 0; i < rows.Count; i++)
            {
                ThreeWayPartitionRow row = rows[i];
                if (row.IsGrouped) y += GroupedGap;
                if (y + barHeight > bottom + barHeight) break;   // nothing legible left to draw

                DrawBar(dc, row, new Rect(barLeft, y, barWidth, barHeight));
                y += step;
            }

            DrawLegend(dc, new Point(14, ActualHeight - LegendBand + 12));
        }

        /// <summary>
        /// One run. The three segments are laid out left to right in the order they are read:
        /// what PatternLab explains, what de novo adds, what is left.
        ///
        /// WHEN THE REMAINDER IS NEGATIVE the two numerators together exceed the denominator, so
        /// there is no room to lay them out at their true shares. They are then compressed to fit the
        /// bar - the reader still sees the composition - and the amount by which they overflow is
        /// drawn as a hatched band over the right of the bar, in the negative colour, with the value
        /// spelled out in the gutter. Nothing is clamped: the number in the gutter is the real,
        /// signed share.
        /// </summary>
        private void DrawBar(DrawingContext dc, ThreeWayPartitionRow row, Rect bar)
        {
            var name = Text(row.RunName, row.IsGrouped ? Ink : Muted, 12, bold: row.IsGrouped);
            dc.DrawText(name, new Point(Math.Max(4, bar.Left - 12 - name.Width),
                                        bar.Top + (bar.Height - name.Height) / 2));

            double sharePl = Math.Max(0, row.SharePatternLab);
            double shareDn = Math.Max(0, row.ShareDeNovo);
            double shareRem = row.ShareRemainder;

            // The compression factor. 1 on every well-formed row; below 1 only when the numerators
            // overflow the denominator.
            double claimed = sharePl + shareDn;
            double scale = shareRem < 0 && claimed > 0 ? 1.0 / claimed : 1.0;

            double wPl = sharePl * scale * bar.Width;
            double wDn = shareDn * scale * bar.Width;
            double wRem = Math.Max(0, bar.Width - wPl - wDn);

            var segments = new (double X, double W, Brush Fill, double Share)[]
            {
                (bar.Left, wPl, PatternLab, row.SharePatternLab),
                (bar.Left + wPl, wDn, DeNovo, row.ShareDeNovo),
                (bar.Left + wPl + wDn, wRem, Remainder, row.ShareRemainder),
            };

            foreach (var s in segments)
            {
                if (s.W <= 0.5) continue;
                dc.DrawRectangle(s.Fill, null, new Rect(s.X, bar.Top, s.W, bar.Height));

                var label = Text(Percent(s.Share * 100.0), Backdrop, 11, bold: true);
                if (label.Width + 12 <= s.W && label.Height + 4 <= bar.Height)
                {
                    dc.DrawText(label, new Point(s.X + (s.W - label.Width) / 2,
                                                 bar.Top + (bar.Height - label.Height) / 2));
                }
            }

            if (row.NegativeRemainder)
            {
                double overflow = Math.Min(bar.Width, Math.Abs(shareRem) * scale * bar.Width);
                DrawHatch(dc, new Rect(bar.Right - overflow, bar.Top, overflow, bar.Height));
            }

            dc.DrawRectangle(null, new Pen(Grid, 1.0), bar);

            var tail = row.NegativeRemainder
                ? Text(Percent(row.ShareRemainderPercent) + " over", Negative, 11, bold: true)
                : Text(Percent(row.ShareRemainderPercent) + " left", Remainder, 11, bold: row.IsGrouped);
            dc.DrawText(tail, new Point(bar.Right + 10, bar.Top + (bar.Height - tail.Height) / 2));
        }

        /// <summary>Diagonal hatching over a region, clipped to it. Hand-drawn rather than a
        /// DrawingBrush so the stripe spacing does not change with the bar height.</summary>
        private static void DrawHatch(DrawingContext dc, Rect region)
        {
            if (region.Width <= 0 || region.Height <= 0) return;

            dc.DrawRectangle(Negative, null, region);

            dc.PushClip(new RectangleGeometry(region));
            var pen = new Pen(Backdrop, 1.6);
            for (double x = region.Left - region.Height; x < region.Right; x += 7)
            {
                dc.DrawLine(pen, new Point(x, region.Bottom), new Point(x + region.Height, region.Top));
            }
            dc.Pop();
        }

        private void DrawLegend(DrawingContext dc, Point at)
        {
            double x = at.X;

            var entries = new List<(Brush Fill, string Name)>
            {
                (PatternLab, "PatternLab, identified (M+0)"),
                (DeNovo, "De novo addition (M+0)"),
                (Remainder, "without an owner"),
            };

            if (rows.Any(r => r.NegativeRemainder))
                entries.Add((Negative, "numerators exceed the TIC"));

            foreach (var e in entries)
            {
                dc.DrawRectangle(e.Fill, null, new Rect(x, at.Y + 2, 12, 12));
                var t = Text(e.Name, Muted, 11);
                dc.DrawText(t, new Point(x + 18, at.Y));
                x += 18 + t.Width + 20;

                // Dropped rather than drawn off the edge when the window is too narrow to hold them.
                if (x > ActualWidth - 60) break;
            }
        }
    }
}
