using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// How many isotopes were actually credited per peptide, as a histogram over 1..N, where N is the
    /// go-forward ceiling <see cref="AnalyticsBuilder.IsotopeBasis"/>. The point it makes is that most
    /// peptides do not reach the ceiling: the crediting is EMPIRICAL, so the extractor credits an isotope
    /// only where it found signal and a lower-abundance precursor may therefore land in the 1 or 2 bin. Raising the
    /// ceiling adds bins on the right; it does not move a peptide into one it did not earn.
    ///
    /// The bars are optionally split by mass band (light / medium / heavy) so the reader can see whether
    /// the shortfall is a mass effect. Drawn with OnRender for the same reason as the other charts.
    /// </summary>
    public class IsotopeCountChart : ChartDrawingBase, IPalettedChart
    {
        private readonly List<AnalyticsPeptide> peptides;
        private readonly bool splitByMass;


        static readonly string[] BandNames = { "under 1500 Da", "1500 to 3000 Da", "over 3000 Da" };
        const double Band1 = 1500, Band2 = 3000;

        /// <param name="palette">The colours to paint in, or null for the screen palette. The three mass
        /// bands come from <see cref="ChartPalette.MassBands"/>, low mass to high.</param>
        public IsotopeCountChart(IEnumerable<AnalyticsPeptide>? peptides, bool splitByMass = true,
                                 ChartPalette? palette = null)
        {
            this.palette = palette ?? ChartPalette.Dark;
            this.peptides = (peptides ?? Enumerable.Empty<AnalyticsPeptide>())
                .Where(p => p != null && p.IsotopesCredited >= 1).ToList();
            this.splitByMass = splitByMass;
            MinHeight = 240;
            Margin = new Thickness(0);
            ToolTip = "Each bar counts the peptides credited that many isotopes, from 1 up to the " +
                      AnalyticsBuilder.IsotopeBasis.ToString(CultureInfo.CurrentCulture) +
                      "-isotope ceiling; an isotope is credited only where signal was found. The colours " +
                      "split each bar by peptide mass.";
        }


        private static int BandOf(double mass) => mass < Band1 ? 0 : mass < Band2 ? 1 : 2;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            ChartPalette pal = palette;
            Brush Backdrop = pal.Backdrop, Ink = pal.Ink, Muted = pal.Muted, Grid = pal.Grid;
            IReadOnlyList<Brush> Bands = pal.MassBands;

            DrawBackdrop(dc, Backdrop);

            var head = Text("Isotopes credited per peptide", Ink, 13, bold: true);
            dc.DrawText(head, new Point(14, 10));

            if (ActualWidth < 200 || ActualHeight < 150) return;

            var plot = new Rect(64, 40, Math.Max(1, ActualWidth - 64 - 24),
                                Math.Max(1, ActualHeight - 40 - 56));

            if (peptides.Count == 0)
            {
                DrawEmpty(dc, "Nothing to show: no peptide was credited an isotope yet.", Muted, new Point(plot.Left, plot.Top + plot.Height / 2));
                return;
            }

            // counts[isotopeCount 1..IsotopeBasis][band 0..2]
            int bins = AnalyticsBuilder.IsotopeBasis;
            var counts = new int[bins + 1, 3];
            foreach (AnalyticsPeptide p in peptides)
            {
                int c = Math.Min(AnalyticsBuilder.IsotopeBasis, p.IsotopesCredited);
                if (c >= 1) counts[c, BandOf(p.Mass)]++;
            }

            int maxTotal = 1;
            for (int c = 1; c <= bins; c++)
            {
                int total = counts[c, 0] + counts[c, 1] + counts[c, 2];
                if (total > maxTotal) maxTotal = total;
            }
            double top = Math.Ceiling(maxTotal / 4.0) * 4.0;
            if (top < 4) top = 4;

            var gridPen = new Pen(Grid, 0.8);
            var axisPen = new Pen(pal.Axis, 1.0);

            for (int i = 0; i <= 4; i++)
            {
                double y = plot.Bottom - i / 4.0 * plot.Height;
                DrawHorizontalTick(dc, plot, y, gridPen, (top * i / 4.0).ToString("N0", CultureInfo.CurrentCulture), Muted);
            }

            DrawAxes(dc, plot, axisPen);

            double slot = plot.Width / bins;
            double barWidth = Math.Min(90, slot * 0.6);

            for (int c = 1; c <= bins; c++)
            {
                double cx = plot.Left + (c - 1 + 0.5) * slot;
                double x = cx - barWidth / 2;

                int total = counts[c, 0] + counts[c, 1] + counts[c, 2];
                double yTop = plot.Bottom;

                if (splitByMass)
                {
                    for (int band = 0; band < 3; band++)
                    {
                        int n = counts[c, band];
                        if (n == 0) continue;
                        double h = n / top * plot.Height;
                        yTop -= h;
                        dc.DrawRectangle(Bands[band], null, new Rect(x, yTop, barWidth, h));
                    }
                }
                else
                {
                    double h = total / top * plot.Height;
                    yTop = plot.Bottom - h;
                    dc.DrawRectangle(Bands[0], null, new Rect(x, yTop, barWidth, h));
                }

                dc.DrawRectangle(null, new Pen(Grid, 1.0),
                    new Rect(x, plot.Bottom - total / top * plot.Height, barWidth, total / top * plot.Height));

                var count = Text(total.ToString("N0", CultureInfo.CurrentCulture), Ink, 11, bold: true);
                dc.DrawText(count, new Point(cx - count.Width / 2,
                    plot.Bottom - total / top * plot.Height - count.Height - 3));

                var axisLabel = Text(c + (c == bins ? " (ceiling)" : ""), Muted, 11);
                dc.DrawText(axisLabel, new Point(cx - axisLabel.Width / 2, plot.Bottom + 6));
            }

            var xName = Text("isotopes credited (area > 0)", Muted, 11);
            dc.DrawText(xName, new Point(plot.Left + (plot.Width - xName.Width) / 2, plot.Bottom + 24));

            if (splitByMass) DrawLegend(dc, plot);
        }

        private void DrawLegend(DrawingContext dc, Rect plot)
        {
            Brush Muted = palette.Muted;
            IReadOnlyList<Brush> Bands = palette.MassBands;

            double y = 14;
            double x = plot.Right;

            // Right-aligned in the title band, so it does not collide with the heading on the left.
            var widths = BandNames.Select(n => Text(n, Muted, 11).Width + 34).ToArray();
            x = plot.Right - widths.Sum();
            if (x < plot.Left + 260) return;   // no room; dropped rather than overlapping the title

            for (int band = 0; band < 3; band++)
            {
                dc.DrawRectangle(Bands[band], null, new Rect(x, y + 2, 12, 12));
                var t = Text(BandNames[band], Muted, 11);
                dc.DrawText(t, new Point(x + 16, y));
                x += widths[band];
            }
        }
    }
}
