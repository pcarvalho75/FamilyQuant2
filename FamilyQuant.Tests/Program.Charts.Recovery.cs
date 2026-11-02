using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        /// <summary>
        /// The two projector-size defects of the headline recovery figure, asserted on the PIXELS. Both
        /// were defects of the DRAWING and not of the arithmetic, so there is no number here to check: the
        /// only witness is the picture itself.
        ///
        /// THE LOWER PANEL USED TO SCALE EVERY ROW TO THE LARGEST ROW. The pooled row is three runs of ion
        /// current, so it drew about three times any single run and squeezed the per-run bars into a third
        /// of the panel; the picture then said "the grouped row is bigger", which is trivially true and not
        /// the claim. Every row is now normalised to ITS OWN total ion current, so each MS1 bar is full
        /// width and the levels under it are that row's own fractions, which is what makes the rows
        /// comparable and the ladder TIC >= detected >= assigned readable. THIS IS THE ASSERTION THAT
        /// WOULD HAVE FAILED BEFORE THE CHANGE: the four MS1 bars share one right edge, where the fixture's
        /// runs used to end at 5/12, 4/12, 3/12 and 12/12 of the panel. The annotated percentages are
        /// untouched, because they were fractions of the same TIC all along.
        ///
        /// THE RUN LABELS USED TO BE CUT MID-WORD. A fixed 150 DIP gutter is narrower than an acquisition
        /// name, and the bars are painted after the labels, so the tail - the only part that tells _01 from
        /// _02 from _03 - was covered over and a triplicate came out as three identical stubs. The second
        /// assertion is exactly that: the gutter of the three run rows must not look the same on all three.
        /// </summary>
        private static void RecoveryChartGeometryTests(string workDir)
        {
            // Three runs with DELIBERATELY different ion currents, and names that share everything but
            // their tail, which is the shape the talk's own data has.
            var fractions = new List<NativeRunFraction>();
            foreach ((string run, double tic) in new[]
            {
                ("20220624_Bjararaca_1ug_3step_01", 5.0e8),
                ("20220624_Bjararaca_1ug_3step_02", 4.0e8),
                ("20220624_Bjararaca_1ug_3step_03", 3.0e8),
            })
            {
                fractions.Add(new NativeRunFraction
                {
                    Run = run,
                    Tic = tic,
                    IdentifiedAreaM0 = tic * 0.12,
                    DeNovoAreaM0 = tic * 0.05,
                    IdentifiedEnvelope = tic * 0.28,
                    DeNovoEnvelope = tic * 0.22,
                    DeNovoEnvelopeReferenceCalibrated = tic * 0.14,
                    IdentifiedPeptides = 900,
                    DeNovoPeptides = 400,
                    DetectedPeptideLikeArea = tic * 0.62,
                    DetectedAreaMultiplyCharged = tic * 0.50,
                    DetectedAreaSinglyCharged = tic * 0.12,
                    DetectedCountMultiplyCharged = 900,
                    DetectedCountSinglyCharged = 220,
                    DetectedEnvelopeCount = 1120,
                });
            }
            NativeRunFraction? grouped = NativeQuantFractions.Grouped(fractions);

            ChartPalette pal = ChartPalette.For(ChartPaletteVariant.Light);
            var chart = new RecoveryChart(fractions, grouped, pal);

            const int W = 900, H = 420;
            byte[] px = RenderChartPixels(chart, W, H,
                Path.Combine(workDir, "recovery_geometry.png"));

            Color ticColour = ChartPalette.ColorOf(pal.Tic);
            Color quantColour = ChartPalette.ColorOf(pal.Quantified);
            Color singleColour = ChartPalette.ColorOf(pal.DetectedSinglyCharged);
            Color deNovoColour = ChartPalette.ColorOf(pal.DeNovo);

            // A minimum start of 100 px and a minimum length of 30 px keep the 12 px legend swatches, which
            // wear the very same colours, out of every measurement below.
            List<List<(int Y, int Start, int End)>> ticBands =
                ColourBands(ColourRuns(px, W, H, ticColour, minStartX: 100, minLength: 50));

            Check("recovery: the lower panel draws one MS1 bar per row, the three runs and the pooled row",
                ticBands.Count == 4, ticBands.Count.ToString(CultureInfo.InvariantCulture));

            if (ticBands.Count == 4)
            {
                List<int> ticEdges = ticBands.Select(b => b.Max(r => r.End)).ToList();
                int spread = ticEdges.Max() - ticEdges.Min();

                // One right edge, within a pixel of rounding. Scaled to the largest row, the three runs
                // would sit at roughly 5/12, 4/12 and 3/12 of the pooled row and this would be hundreds.
                Check("recovery: every row's MS1 bar is normalised to its OWN TIC, so all four end together",
                    spread <= 2, "spread " + spread.ToString(CultureInfo.InvariantCulture) + " px over " +
                                 string.Join(", ", ticEdges.Select(e => e.ToString(CultureInfo.InvariantCulture))));
                Check("recovery: and that shared edge really is the full width of the panel",
                    ticEdges.Min() > 0.70 * W,
                    ticEdges.Min().ToString(CultureInfo.InvariantCulture) + " of " +
                    W.ToString(CultureInfo.InvariantCulture));

                // The ladder the panel exists to show: the detected estimate cannot exceed the ion current
                // it is a part of, and the assigned envelope cannot exceed the detected estimate.
                List<List<(int Y, int Start, int End)>> detectedBands =
                    ColourBands(ColourRuns(px, W, H, singleColour, minStartX: 100, minLength: 30));
                List<List<(int Y, int Start, int End)>> assignedBands =
                    ColourBands(ColourRuns(px, W, H, deNovoColour, minStartX: 100, minLength: 30));

                Check("recovery: the detected and assigned levels are drawn on every row too",
                    detectedBands.Count == 4 && assignedBands.Count == 4,
                    detectedBands.Count.ToString(CultureInfo.InvariantCulture) + " / " +
                    assignedBands.Count.ToString(CultureInfo.InvariantCulture));

                if (detectedBands.Count == 4 && assignedBands.Count == 4)
                {
                    List<int> detEdges = detectedBands.Select(b => b.Max(r => r.End)).ToList();
                    List<int> asgEdges = assignedBands.Select(b => b.Max(r => r.End)).ToList();
                    bool ladder = true;
                    for (int i = 0; i < 4; i++)
                        ladder &= asgEdges[i] <= detEdges[i] + 1 && detEdges[i] <= ticEdges[i] + 1;

                    Check("recovery: and the panel reads as the ladder TIC >= detected >= assigned on every row",
                        ladder,
                        string.Join(" | ", Enumerable.Range(0, 4).Select(i =>
                            ticEdges[i].ToString(CultureInfo.InvariantCulture) + ">=" +
                            detEdges[i].ToString(CultureInfo.InvariantCulture) + ">=" +
                            asgEdges[i].ToString(CultureInfo.InvariantCulture))));
                }
            }

            // ---- the labels ---------------------------------------------------------------------------
            List<(int Y, int Start, int End)> quantRuns =
                ColourRuns(px, W, H, quantColour, minStartX: 100, minLength: 40);
            List<List<(int Y, int Start, int End)>> barBands = ColourBands(quantRuns);

            Check("recovery: the top panel draws one stacked bar per row",
                barBands.Count == 4, barBands.Count.ToString(CultureInfo.InvariantCulture));

            if (barBands.Count == 4 && quantRuns.Count > 0)
            {
                int barLeft = quantRuns.Min(r => r.Start);
                List<string> gutters = barBands
                    .Select(b => GutterFingerprint(px, W, b.Min(r => r.Y), b.Max(r => r.Y), barLeft - 10))
                    .ToList();

                // The three RUN rows, not the pooled one, which carries a label of its own kind.
                Check("recovery: the three run labels are drawn differently from one another, so a " +
                      "triplicate is not three identical stubs",
                    gutters[0] != gutters[1] && gutters[1] != gutters[2] && gutters[0] != gutters[2],
                    string.Join(", ", gutters.Take(3)));
            }
        }

        /// <summary>Whether one pixel carries exactly this colour. Exact on purpose: bar interiors are
        /// flat fills, and only the fractional edge pixels are blended.</summary>
        private static bool PixelIs(byte[] px, int width, int x, int y, Color colour)
        {
            int i = (y * width + x) * 4;
            return px[i] == colour.B && px[i + 1] == colour.G && px[i + 2] == colour.R;
        }

        /// <summary>A hash of the label gutter of one bar. Two rows whose labels were cut back to the same
        /// visible stub hash the same, which is precisely the failure being guarded against.</summary>
        private static string GutterFingerprint(byte[] px, int width, int top, int bottom, int rightEdge)
        {
            ulong hash = 14695981039346656037UL;
            for (int y = top; y <= bottom; y++)
            {
                for (int x = 0; x < rightEdge && x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    hash = (hash ^ px[i]) * 1099511628211UL;
                    hash = (hash ^ px[i + 1]) * 1099511628211UL;
                    hash = (hash ^ px[i + 2]) * 1099511628211UL;
                }
            }
            return hash.ToString("X16", CultureInfo.InvariantCulture);
        }

        /// <summary>Renders a chart to a PNG and returns the count of non-backdrop pixels, the way the
        /// identity-sweep and three-way chart tests prove a chart drew ink instead of a blank panel.</summary>
        private static int RenderChartInk(FrameworkElement chart, string pngPath)
        {
            const int W = 900, H = 360;
            chart.Width = W;
            chart.Height = H;
            chart.Measure(new Size(W, H));
            chart.Arrange(new Rect(0, 0, W, H));
            chart.UpdateLayout();

            var bmp = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(chart);

            var pixels = new byte[W * H * 4];
            bmp.CopyPixels(pixels, W * 4, 0);

            // The house backdrop is 0x333949; anything else is drawn ink.
            int ink = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] == 0) continue;
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                if (!(r == 0x33 && g == 0x39 && b == 0x49)) ink++;
            }

            using (var fs = File.Create(pngPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }
            Console.WriteLine("        chart rendered to " + pngPath);
            return ink;
        }

    }
}
