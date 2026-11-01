using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// Geometry for the identity sweep, separated from the drawing so it can be checked without opening
    /// a window.
    /// </summary>
    public static class IdentityScanPlot
    {
        /// <summary>
        /// The loosest identity at which no family mixed a target with a decoy.
        ///
        /// This is the only operating point the sweep can defend. Below it, alignments are no better than
        /// noise, which is what a target meeting a decoy means. It is a FLOOR - "do not cluster looser
        /// than this" - and not an optimum: fusions of two unrelated target families happen above it and
        /// are invisible here. null when the database carries no decoys, or when every identity mixed.
        /// </summary>
        public static int? CleanFloor(IReadOnlyList<IdentityScanRow> rows)
        {
            if (rows == null || rows.Count == 0) return null;
            if (rows.All(r => r.Groups == r.TargetGroups && r.MixedGroups == 0)) return null;  // no decoys

            // The lower edge of the clean run that STARTS at the strictest identity, not the loosest
            // clean identity anywhere in the sweep. Mixing can appear, vanish and return as the threshold
            // loosens - a decoy family that mixes at 60% can come apart again at 50% - and taking the
            // minimum over every clean row would then label an identity as a floor although looser
            // clustering above it already mixed. Everything at or above the value returned here is clean.
            var ordered = rows.OrderByDescending(r => r.Identity).ToList();
            if (ordered[0].MixedGroups != 0) return null;

            int floor = ordered[0].Identity;
            foreach (var row in ordered)
            {
                if (row.MixedGroups != 0) break;
                floor = row.Identity;
            }

            return floor;
        }

        /// <summary>
        /// Maps a sweep onto a plot rectangle. X is the identity, laid out strict to loose from left to
        /// right regardless of the order the rows arrive in. Y is <paramref name="value"/>, from zero to
        /// the largest value in the sweep, so the reader is never shown a truncated axis.
        /// </summary>
        public static List<Point> Map(IReadOnlyList<IdentityScanRow> rows, Rect plot,
                                      Func<IdentityScanRow, double> value)
        {
            var points = new List<Point>();
            if (rows == null || rows.Count == 0 || plot.Width <= 0 || plot.Height <= 0) return points;

            var ordered = rows.OrderByDescending(r => r.Identity).ToList();

            double hi = ordered.Max(r => r.Identity);
            double lo = ordered.Min(r => r.Identity);
            double top = Math.Max(ordered.Max(value), double.Epsilon);
            double span = Math.Max(hi - lo, 1);

            foreach (var row in ordered)
            {
                // Strictest identity on the left, so the family count rises as the eye travels right.
                double x = plot.Left + (hi - row.Identity) / span * plot.Width;
                double y = plot.Bottom - value(row) / top * plot.Height;
                points.Add(new Point(x, y));
            }

            return points;
        }

        public static double MaxFamilies(IReadOnlyList<IdentityScanRow> rows) =>
            rows == null || rows.Count == 0 ? 0 : rows.Max(r => (double)r.ReportedTargetGroups);

        public static double MaxMixing(IReadOnlyList<IdentityScanRow> rows) =>
            rows == null || rows.Count == 0 ? 0 : rows.Max(r => r.Score);
    }

    /// <summary>
    /// The identity sweep as a curve: how many families each USEARCH threshold produces, against how
    /// often that threshold put a target and a decoy in one family.
    ///
    /// Drawn here rather than with a charting package because the application ships no such dependency,
    /// and OnRender redraws on resize for free.
    /// </summary>
    public class IdentityScanChart : ChartDrawingBase, IPalettedChart
    {
        private readonly List<IdentityScanRow> rows;
        private readonly int? currentIdentity;


        /// <param name="palette">The colours to paint in, or null for the screen palette. The exporter
        /// swaps it for the light one when a figure is written for a slide.</param>
        public IdentityScanChart(List<IdentityScanRow> rows, int? currentIdentity = null,
                                 ChartPalette? palette = null)
        {
            this.rows = rows ?? new List<IdentityScanRow>();
            this.currentIdentity = currentIdentity;
            this.palette = palette ?? ChartPalette.Dark;
            MinHeight = 260;
            Margin = new Thickness(0);
        }


        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            ChartPalette pal = palette;
            Brush Backdrop = pal.Backdrop, Ink = pal.Ink, Muted = pal.Muted, Grid = pal.Grid;
            Brush Families = pal.Families, Mixing = pal.Mixing, Floor = pal.Floor;

            // Its own backdrop, so the ink is legible wherever the control is hosted and a render to a
            // bitmap shows what the window shows instead of text on transparency.
            DrawBackdrop(dc, Backdrop);

            if (rows.Count == 0 || ActualWidth < 120 || ActualHeight < 120) return;

            var plot = new Rect(64, 26, Math.Max(1, ActualWidth - 64 - 74),
                                Math.Max(1, ActualHeight - 26 - 52));

            double maxFamilies = IdentityScanPlot.MaxFamilies(rows);
            double maxMixing = IdentityScanPlot.MaxMixing(rows);
            var ordered = rows.OrderByDescending(r => r.Identity).ToList();
            bool hasDecoys = !(rows.All(r => r.Groups == r.TargetGroups && r.MixedGroups == 0));

            var gridPen = new Pen(Grid, 0.8);
            var axisPen = new Pen(pal.Axis, 1.0);

            // ---- horizontal grid and the family axis on the left
            for (int i = 0; i <= 4; i++)
            {
                double y = plot.Bottom - i / 4.0 * plot.Height;
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));

                var label = Text((maxFamilies * i / 4.0).ToString("N0", CultureInfo.CurrentCulture), Muted, 11);
                dc.DrawText(label, new Point(plot.Left - label.Width - 8, y - label.Height / 2));

                if (hasDecoys && maxMixing > 0)
                {
                    var right = Text((maxMixing * i / 4.0).ToString("P1", CultureInfo.CurrentCulture), Muted, 11);
                    dc.DrawText(right, new Point(plot.Right + 8, y - right.Height / 2));
                }
            }

            DrawAxes(dc, plot, axisPen);

            // ---- identity ticks along the bottom, thinned so they never collide
            var xs = IdentityScanPlot.Map(rows, plot, r => 0);
            int everyNth = Math.Max(1, (int)Math.Ceiling(ordered.Count * 34.0 / Math.Max(plot.Width, 1)));
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i % everyNth != 0 && i != ordered.Count - 1) continue;

                var t = Text(ordered[i].Identity + "%", Muted, 11);
                dc.DrawText(t, new Point(xs[i].X - t.Width / 2, plot.Bottom + 6));
            }

            // ---- the floor: the loosest identity that never mixed a target with a decoy
            // Same condition as the mixing series: a floor drawn without the curve it refers to leaves
            // the reader with a green line and nothing that explains it.
            int? floor = hasDecoys && maxMixing > 0 ? IdentityScanPlot.CleanFloor(rows) : null;
            if (floor.HasValue)
            {
                int index = ordered.FindIndex(r => r.Identity == floor.Value);
                if (index >= 0)
                {
                    double x = xs[index].X;
                    dc.DrawLine(new Pen(Floor, 1.6) { DashStyle = new DashStyle(new double[] { 5, 4 }, 0) },
                                new Point(x, plot.Top), new Point(x, plot.Bottom));

                    var t = Text("floor " + floor.Value + "%", Floor, 11, bold: true);
                    dc.DrawText(t, new Point(Math.Min(x + 6, plot.Right - t.Width), plot.Top - 2));
                }
            }

            // ---- where the file currently sits
            if (currentIdentity.HasValue)
            {
                int index = ordered.FindIndex(r => r.Identity == currentIdentity.Value);
                if (index >= 0)
                {
                    double x = xs[index].X;
                    dc.DrawLine(new Pen(Ink, 1.2) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) },
                                new Point(x, plot.Top), new Point(x, plot.Bottom));

                    // Along the bottom, because this line and the floor line land on the same identity
                    // whenever the file was clustered at the floor, which is the recommended case.
                    var t = Text("this file, " + currentIdentity.Value + "%", Ink, 11);
                    dc.DrawText(t, new Point(Math.Min(Math.Max(plot.Left, x - t.Width / 2), plot.Right - t.Width),
                                             plot.Bottom - t.Height - 4));
                }
            }

            // ---- the two series
            DrawSeries(dc, IdentityScanPlot.Map(rows, plot, r => r.ReportedTargetGroups), Families, 2.4);

            if (hasDecoys && maxMixing > 0)
            {
                DrawSeries(dc, IdentityScanPlot.Map(rows, plot, r => r.Score), Mixing, 2.0);
            }

            // ---- legend and axis names
            var famName = Text("non-contaminant target connected components", Families, 12, bold: true);
            dc.DrawText(famName, new Point(plot.Left, plot.Bottom + 26));

            if (hasDecoys && maxMixing > 0)
            {
                var mixName = Text("families mixing target and decoy", Mixing, 12, bold: true);
                dc.DrawText(mixName, new Point(plot.Left + famName.Width + 22, plot.Bottom + 26));
            }

            // Dropped when the window is too narrow to hold it beside the legend, rather than drawn on
            // top of it.
            var axis = Text("USEARCH identity, strictest on the left", Muted, 11);
            double legendEnd = plot.Left + famName.Width + (hasDecoys && maxMixing > 0 ? 22 + 210 : 0);
            if (plot.Right - axis.Width > legendEnd + 16)
            {
                dc.DrawText(axis, new Point(plot.Right - axis.Width, plot.Bottom + 26));
            }
        }

        private static void DrawSeries(DrawingContext dc, List<Point> points, Brush brush, double thickness)
        {
            if (points.Count == 0) return;

            var pen = new Pen(brush, thickness);

            for (int i = 1; i < points.Count; i++)
            {
                dc.DrawLine(pen, points[i - 1], points[i]);
            }

            foreach (var p in points)
            {
                dc.DrawEllipse(brush, null, p, thickness + 0.6, thickness + 0.6);
            }
        }
    }
}
