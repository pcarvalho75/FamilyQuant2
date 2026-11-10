using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>Recovery per neutral-mass range, using the independently detected envelope area.</summary>
    public class PercentIonCurrentChart : ChartDrawingBase, IPalettedChart
    {
        public MassRangeRecoveryData Data { get; }

        /// <summary>The historical constructor arguments are retained for existing chart callers.
        /// Recovery requires the optional stored detector-range projection.</summary>
        public PercentIonCurrentChart(IEnumerable<AnalyticsPeptide> peptides, double tic = 0,
            double binWidth = 500, ChartPalette? palette = null, MassRangeRecoveryData? recovery = null)
        {
            this.palette = palette ?? ChartPalette.Dark;
            Data = recovery ?? new(false, MassRangeRecoveryData.MissingMessage, Array.Empty<MassRangeRecoveryData.Bin>(),
                1, 0, MassRangeRecoveryData.MissingMessage);
            MinHeight = 240;
            Margin = new Thickness(0);
            ToolTip = "Each mass range compares measured peptide envelopes with detector isotope-envelope area in that same range. "
                + "This read-only chart pools run areas and changes no project values or files.";
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            DrawBackdrop(dc, palette.Backdrop);
            dc.DrawText(Text("Recovery by neutral-mass range", palette.Ink, 13, true), new Point(14, 10));
            if (ActualWidth < 200 || ActualHeight < 150) return;
            if (!Data.Available || Data.Bins.Count == 0)
            {
                var message = Text(Data.Message, palette.Muted, 12);
                message.MaxTextWidth = Math.Max(1, ActualWidth - 40);
                dc.DrawText(message, new Point(20, 54));
                return;
            }
            var plot = new Rect(64, 56, Math.Max(1, ActualWidth - 88), Math.Max(1, ActualHeight - 144));
            for (int tick = 0; tick <= 4; tick++)
            {
                double value = tick * Data.AxisMaximum / 4;
                DrawHorizontalTick(dc, plot, plot.Bottom - value / Data.AxisMaximum * plot.Height,
                    new Pen(palette.Grid, .8), (100 * value).ToString("0.#", CultureInfo.CurrentCulture) + "%", palette.Muted);
            }
            DrawAxes(dc, plot, new Pen(palette.Axis, 1));
            double slot = plot.Width / Data.Bins.Count;
            int labelEvery = Math.Max(1, (int)Math.Ceiling(55 / slot));
            for (int i = 0; i < Data.Bins.Count; i++)
            {
                var bin = Data.Bins[i];
                double x = plot.Left + (i + .5) * slot;
                if (bin.Recovery.HasValue)
                {
                    double h = bin.Recovery.Value / Data.AxisMaximum * plot.Height;
                    dc.DrawRectangle(palette.Found, null, new Rect(x - slot * .35, plot.Bottom - h, slot * .7, h));
                }
                else dc.DrawText(Text("n/a", palette.Muted, 9), new Point(x - 8, plot.Bottom - 15));
                if (i % labelEvery == 0)
                {
                    var label = Text((i * NeutralMassRanges.Width).ToString("N0", CultureInfo.CurrentCulture), palette.Muted, 10);
                    dc.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 7));
                }
            }
            dc.DrawText(Text("Neutral mass (Da); final stored range includes 19,500 Da and above", palette.Muted, 10),
                new Point(plot.Left, plot.Bottom + 26));
            var legend = Text("Peptide envelope / detector envelope in each range; pooled areas from " + Data.RunCount.ToString(CultureInfo.CurrentCulture)
                + (Data.RunCount == 1 ? " run." : " runs.") + " No run mean or whiskers. The independent detector is not peptide-FDR calibrated; ratios can exceed 100%.", palette.Muted, 10);
            legend.MaxTextWidth = plot.Width;
            dc.DrawText(legend, new Point(plot.Left, plot.Bottom + 44));
        }
    }
}
