using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// Where the credited ion current sits by mass: mass binned along the bottom, and for each bin the
    /// share of the whole credited envelope signal that the peptides in that bin carry. When a run TIC is
    /// supplied the bin's share of the raw TIC is drawn as a fainter overlay, so the reader sees both what
    /// fraction of the FOUND signal and what fraction of ALL signal each mass band holds.
    ///
    /// Drawn with OnRender for the same reason as the other charts, and renders headless.
    /// </summary>
    public class PercentIonCurrentChart : ChartDrawingBase, IPalettedChart
    {
        private readonly List<AnalyticsPeptide> peptides;
        private readonly double tic;
        private readonly double binWidth;
        public MassIsotopeChartData.Signal Data { get; }


        /// <param name="tic">The run (or grouped) total ion current, or 0 to omit the share-of-TIC overlay.</param>
        /// <param name="palette">The colours to paint in, or null for the screen palette.</param>
        public PercentIonCurrentChart(IEnumerable<AnalyticsPeptide>? peptides, double tic = 0,
                                      double binWidth = 500, ChartPalette? palette = null)
        {
            this.palette = palette ?? ChartPalette.Dark;
            this.peptides = (peptides ?? Enumerable.Empty<AnalyticsPeptide>())
                .Where(p => p != null && p.Envelope > 0 && p.Mass > 0).ToList();
            this.tic = tic > 0 ? tic : 0;
            this.binWidth = binWidth > 0 ? binWidth : 500;
            Data = MassIsotopeChartData.SignalByMass(this.peptides, this.tic, this.binWidth);
            MinHeight = 240;
            Margin = new Thickness(0);
            ToolTip = "Peptide mass is binned along the bottom; filled bars show each band's share of credited peptide signal, summing to 100%, " +
                      "while outlined bars show its share of total ion current when TIC is known. " +
                      "This read-only chart changes no project values or files.";
        }


        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            ChartPalette pal = palette;
            Brush Backdrop = pal.Backdrop, Ink = pal.Ink, Muted = pal.Muted, Grid = pal.Grid;
            Brush Found = pal.Found, OfTic = pal.OfTic;

            DrawBackdrop(dc, Backdrop);

            var head = Text("Percent of ion current by mass", Ink, 13, bold: true);
            dc.DrawText(head, new Point(14, 10));

            if (ActualWidth < 200 || ActualHeight < 150) return;

            var plot = new Rect(64, 40, Math.Max(1, ActualWidth - 64 - 24),
                                Math.Max(1, ActualHeight - 40 - 56));

            if (peptides.Count == 0)
            {
                DrawEmpty(dc, "Nothing to show: no peptide carried a positive envelope yet.", Muted, new Point(plot.Left, plot.Top + plot.Height / 2));
                return;
            }

            int binCount = Data.Bins.Count;
            double top = Data.AxisMaximum;

            var gridPen = new Pen(Grid, 0.8);
            var axisPen = new Pen(pal.Axis, 1.0);

            for (int i = 0; i <= 4; i++)
            {
                double y = plot.Bottom - i / 4.0 * plot.Height;
                DrawHorizontalTick(dc, plot, y, gridPen, (top * i / 4.0).ToString("P0", CultureInfo.CurrentCulture), Muted);
            }

            DrawAxes(dc, plot, axisPen);

            double slot = plot.Width / binCount;
            double barWidth = Math.Max(2, slot * 0.7);

            for (int b = 0; b < binCount; b++)
            {
                double cx = plot.Left + (b + 0.5) * slot;
                double share = Data.Bins[b].Share;
                double h = Math.Min(1, share / top) * plot.Height;
                var rect = new Rect(cx - barWidth / 2, plot.Bottom - h, barWidth, h);
                dc.DrawRectangle(Found, null, rect);

                if (tic > 0)
                {
                    double ticShare = Data.Bins[b].TicShare!.Value;
                    double th = Math.Min(1, ticShare / top) * plot.Height;
                    dc.DrawRectangle(null, new Pen(OfTic, 1.6),
                        new Rect(cx - barWidth / 2, plot.Bottom - th, barWidth, th));
                }

                // Thinned mass labels so they never collide.
                int every = Math.Max(1, (int)Math.Ceiling(binCount * 40.0 / Math.Max(plot.Width, 1)));
                if (b % every == 0 || b == binCount - 1)
                {
                    var t = Text(((b + 1) * binWidth).ToString("N0", CultureInfo.CurrentCulture), Muted, 10);
                    dc.DrawText(t, new Point(cx - t.Width / 2, plot.Bottom + 6));
                }
            }

            var xName = Text("monoisotopic mass (Da), upper edge of each bin", Muted, 11);
            dc.DrawText(xName, new Point(plot.Left + (plot.Width - xName.Width) / 2, plot.Bottom + 24));

            DrawLegend(dc, plot);
        }

        private void DrawLegend(DrawingContext dc, Rect plot)
        {
            Brush Found = palette.Found, OfTic = palette.OfTic;

            double y = 14;
            var a = Text("share of credited envelope", Found, 11, bold: true);
            double totalW = a.Width + 34 + (tic > 0 ? Text("share of TIC", OfTic, 11, bold: true).Width + 34 : 0);
            double x = plot.Right - totalW;
            if (x < plot.Left + 260) return;

            dc.DrawRectangle(Found, null, new Rect(x, y + 2, 12, 12));
            dc.DrawText(a, new Point(x + 16, y));
            x += a.Width + 34;

            if (tic > 0)
            {
                dc.DrawRectangle(null, new Pen(OfTic, 1.6), new Rect(x, y + 2, 12, 12));
                var b = Text("share of TIC", OfTic, 11, bold: true);
                dc.DrawText(b, new Point(x + 16, y));
            }
        }
    }
}
