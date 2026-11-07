using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// The "XIC tipo massa": every peptide as one point, its credited envelope area (log10) against its
    /// monoisotopic mass, identified and de novo told apart by colour.
    ///
    /// Drawn with OnRender rather than a charting package for the same reason as
    /// <see cref="IdentityScanChart"/>: the application ships no such dependency, and a resize redraws
    /// for free. Renders headless to a bitmap because it paints its own backdrop.
    /// </summary>
    public class XicMassChart : ChartDrawingBase, IPalettedChart
    {
        private readonly List<AnalyticsPeptide> peptides;


        /// <param name="peptides">The deduped analytics table; only positive mass and envelope are drawn.</param>
        /// <param name="palette">The colours to paint in, or null for the screen palette.</param>
        public XicMassChart(IEnumerable<AnalyticsPeptide>? peptides, ChartPalette? palette = null)
        {
            this.palette = palette ?? ChartPalette.Dark;
            this.peptides = (peptides ?? Enumerable.Empty<AnalyticsPeptide>())
                .Where(p => p != null && p.Envelope > 0 && p.Mass > 0).ToList();
            MinHeight = 260;
            Margin = new Thickness(0);
            ToolTip = "One dot per peptide: its extracted isotope envelope (log10 area, up the side) " +
                      "against its mass (along the bottom). Separate series show identified peptides and " +
                      "de novo additions without changing project values or files.";
        }


        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            ChartPalette pal = palette;
            Brush Backdrop = pal.Backdrop, Ink = pal.Ink, Muted = pal.Muted, Grid = pal.Grid;
            Brush Identified = pal.Identified, DeNovo = pal.DeNovo;

            DrawBackdrop(dc, Backdrop);

            var head = Text("XIC by mass: envelope area against peptide mass", Ink, 13, bold: true);
            dc.DrawText(head, new Point(14, 10));

            if (ActualWidth < 200 || ActualHeight < 160) return;

            var plot = new Rect(70, 40, Math.Max(1, ActualWidth - 70 - 24),
                                Math.Max(1, ActualHeight - 40 - 56));

            if (peptides.Count == 0)
            {
                DrawEmpty(dc, "Nothing to show: no peptide carried a positive envelope yet.", Muted, new Point(plot.Left, plot.Top + plot.Height / 2));
                DrawLegend(dc, plot);
                return;
            }

            double maxMass = peptides.Max(p => p.Mass);
            double massHi = Math.Ceiling(maxMass / 500.0) * 500.0;
            if (massHi <= 0) massHi = 500;

            double logMin = Math.Floor(peptides.Min(p => Math.Log10(p.Envelope)));
            double logMax = Math.Ceiling(peptides.Max(p => Math.Log10(p.Envelope)));
            if (logMax <= logMin) logMax = logMin + 1;

            var gridPen = new Pen(Grid, 0.8);
            var axisPen = new Pen(pal.Axis, 1.0);

            // Horizontal grid and the log10(area) axis on the left.
            int logSteps = (int)(logMax - logMin);
            for (int i = 0; i <= logSteps; i++)
            {
                double v = logMin + i;
                double y = plot.Bottom - (v - logMin) / (logMax - logMin) * plot.Height;
                DrawHorizontalTick(dc, plot, y, gridPen, "1e" + v.ToString("0", CultureInfo.CurrentCulture), Muted);
            }

            // Mass ticks along the bottom.
            for (int i = 0; i <= 5; i++)
            {
                double m = massHi * i / 5.0;
                double x = plot.Left + m / massHi * plot.Width;
                dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var t = Text(m.ToString("N0", CultureInfo.CurrentCulture), Muted, 11);
                dc.DrawText(t, new Point(x - t.Width / 2, plot.Bottom + 6));
            }

            DrawAxes(dc, plot, axisPen);

            // The two series. De novo drawn first so the more numerous identified dots do not bury it.
            DrawSeries(dc, peptides.Where(p => p.Side == NativeQuantFractions.SideDeNovo), DeNovo, plot, massHi, logMin, logMax);
            DrawSeries(dc, peptides.Where(p => p.Side == NativeQuantFractions.SideIdentified), Identified, plot, massHi, logMin, logMax);

            // Axis names.
            var xName = Text("monoisotopic mass (Da)", Muted, 11);
            dc.DrawText(xName, new Point(plot.Left + (plot.Width - xName.Width) / 2, plot.Bottom + 24));

            DrawLegend(dc, plot);
        }

        private static void DrawSeries(DrawingContext dc, IEnumerable<AnalyticsPeptide> series, Brush brush,
                                       Rect plot, double massHi, double logMin, double logMax)
        {
            foreach (AnalyticsPeptide p in series)
            {
                double x = plot.Left + Math.Min(p.Mass, massHi) / massHi * plot.Width;
                double log = Math.Log10(p.Envelope);
                double y = plot.Bottom - (log - logMin) / (logMax - logMin) * plot.Height;
                dc.DrawEllipse(brush, null, new Point(x, y), 1.9, 1.9);
            }
        }

        private void DrawLegend(DrawingContext dc, Rect plot)
        {
            Brush Identified = palette.Identified, DeNovo = palette.DeNovo;

            int id = peptides.Count(p => p.Side == NativeQuantFractions.SideIdentified);
            int dn = peptides.Count(p => p.Side == NativeQuantFractions.SideDeNovo);

            double y = ActualHeight - 22;
            double x = plot.Left;

            dc.DrawEllipse(Identified, null, new Point(x + 6, y + 6), 4, 4);
            var a = Text("identified (" + id.ToString("N0", CultureInfo.CurrentCulture) + ")", Identified, 12, bold: true);
            dc.DrawText(a, new Point(x + 16, y));

            x += 16 + a.Width + 22;
            dc.DrawEllipse(DeNovo, null, new Point(x + 6, y + 6), 4, 4);
            var b = Text("de novo (" + dn.ToString("N0", CultureInfo.CurrentCulture) + ")", DeNovo, 12, bold: true);
            dc.DrawText(b, new Point(x + 16, y));
        }
    }
}
