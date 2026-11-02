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

        /// <summary>Builds a family envelope table by hand, so the fold and the ordering can be checked
        /// against arithmetic instead of against whatever the homology search happens to return.</summary>
        private static FamilyEnvelopeQuantResult FamilyFixture(int familyCount,
                                                               double unattributedIdentified = 0,
                                                               double unattributedDeNovo = 0)
        {
            var result = new FamilyEnvelopeQuantResult
            {
                UnattributedIdentifiedEnvelope = unattributedIdentified,
                UnattributedDeNovoEnvelope = unattributedDeNovo,
            };

            // Deliberately built SMALLEST first and with the ids ascending, so an implementation that
            // merely echoed the input order would fail the ordering check below.
            for (int i = 0; i < familyCount; i++)
            {
                result.Families.Add(new FamilyEnvelopeQuantRow
                {
                    FamilyUserId = 100 + i,
                    Description = "Snake venom metalloproteinase BJ-PI " + i +
                                  " OS=Bothrops jararaca OX=8724 GN=svmp PE=1 SV=2",
                    IdentifiedEnvelope = (i + 1) * 1000.0,
                    DeNovoEnvelope = (i + 1) * 250.0,
                    IdentifiedPeptides = i + 1,
                    DeNovoPeptides = i,
                });
            }

            // The measured envelope: each deduped peptide counted ONCE. This fixture's families share no
            // peptide, so it is exactly the rollup; a real table has FamilyEnvelopeQuant.Build set it, and
            // there it is SMALLER than the rollup wherever a peptide reaches more than one family.
            result.MeasuredIdentifiedEnvelope =
                result.Families.Sum(f => f.IdentifiedEnvelope) + unattributedIdentified;
            result.MeasuredDeNovoEnvelope =
                result.Families.Sum(f => f.DeNovoEnvelope) + unattributedDeNovo;

            return result;
        }

        /// <summary>
        /// What a family chart really PAINTED, read back from its own pixels instead of from its model:
        /// how many separate bar bands a column inside the bar area crosses, and whether the "not a
        /// family" colour - the fold and the unattributed residual - appears anywhere in that band.
        ///
        /// Pixels and not properties on purpose. A chart that stops drawing halfway still holds every bar
        /// and still prints a total over all of them, so only the picture can catch it.
        /// </summary>
        private static (int Runs, bool HasNotAFamily) ProbeBars(FamilyContributionChart chart,
                                                                double width, double height, int column)
        {
            // The DARK variant is asked for explicitly: this probe knows the house dark backdrop and the
            // dark "not a family" amber by their byte values, and the exporter's own default is the light
            // slide palette. Naming the variant is what keeps the probe measuring what it claims to.
            BitmapSource rendered = ChartImageExporter.Render(chart, width, height, scale: 1.0,
                                                             ChartPaletteVariant.Dark);
            var bgra = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);

            int w = bgra.PixelWidth;
            int h = bgra.PixelHeight;
            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            bgra.CopyPixels(pixels, stride, 0);

            // The chart's own backdrop and its "not a family" outline, in BGRA order.
            bool IsBackdrop(int i) => pixels[i] == 0x49 && pixels[i + 1] == 0x39 && pixels[i + 2] == 0x33;
            bool IsNotAFamily(int i) => pixels[i] == 0x4B && pixels[i + 1] == 0xA6 && pixels[i + 2] == 0xD8;

            int top = (int)FamilyContributionPlot.TopBand;
            int bottom = (int)(h - FamilyContributionPlot.LegendBand);

            int runs = 0;
            bool inRun = false;
            bool notAFamily = false;

            for (int y = top; y < bottom; y++)
            {
                int probe = y * stride + column * 4;
                bool painted = !IsBackdrop(probe);
                if (painted && !inRun) runs++;
                inRun = painted;

                for (int px = 0; px < w && !notAFamily; px++)
                {
                    if (IsNotAFamily(y * stride + px * 4)) notAFamily = true;
                }
            }

            return (runs, notAFamily);
        }

        /// <summary>Whether a palette brush carries exactly these bytes. The dark variant is asserted this
        /// way because any drift in it is a change to what the application looks like.</summary>
        private static bool IsRgb(Brush brush, byte r, byte g, byte b)
        {
            Color c = ChartPalette.ColorOf(brush);
            return c.R == r && c.G == g && c.B == b;
        }

        /// <summary>The top-left pixel a PNG on disk really carries. Every house chart fills its whole
        /// area with its backdrop first, so this is the figure's ground colour, read back from the file
        /// rather than assumed from the palette that was asked for.</summary>
        private static Color PngCornerColour(string path)
        {
            using var stream = File.OpenRead(path);
            BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4];
            bgra.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
        }

        /// <summary>Renders a chart at a chosen size, writes it out for eyeballing and hands back the raw
        /// BGRA pixels, so a test can assert on the geometry a reader actually sees.</summary>
        private static byte[] RenderChartPixels(FrameworkElement chart, int width, int height, string pngPath)
        {
            chart.Width = width;
            chart.Height = height;
            chart.Measure(new Size(width, height));
            chart.Arrange(new Rect(0, 0, width, height));
            chart.UpdateLayout();

            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(chart);

            var pixels = new byte[width * height * 4];
            bmp.CopyPixels(pixels, width * 4, 0);

            using (var fs = File.Create(pngPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }
            Console.WriteLine("        chart rendered to " + pngPath);
            return pixels;
        }

        /// <summary>Every maximal horizontal run of one EXACT colour, filtered by where it starts and how
        /// long it is: a bar is a long run out in the plot area, a legend swatch is a 12 px run at the
        /// left margin, and only the filter tells them apart.</summary>
        private static List<(int Y, int Start, int End)> ColourRuns(
            byte[] px, int width, int height, Color colour, int minStartX, int minLength)
        {
            var runs = new List<(int Y, int Start, int End)>();
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    if (!PixelIs(px, width, x, y, colour)) { x++; continue; }
                    int start = x;
                    while (x < width && PixelIs(px, width, x, y, colour)) x++;
                    int end = x - 1;
                    if (start >= minStartX && end - start + 1 >= minLength) runs.Add((y, start, end));
                }
            }
            return runs;
        }

        /// <summary>The runs grouped into the BARS they belong to: consecutive scanlines are one bar, a gap
        /// starts the next. Returned in top-to-bottom order, which is row order.</summary>
        private static List<List<(int Y, int Start, int End)>> ColourBands(
            List<(int Y, int Start, int End)> runs)
        {
            var bands = new List<List<(int Y, int Start, int End)>>();
            int lastY = int.MinValue;
            foreach ((int Y, int Start, int End) r in runs.OrderBy(r => r.Y))
            {
                if (bands.Count == 0 || r.Y - lastY > 1) bands.Add(new List<(int, int, int)>());
                bands[bands.Count - 1].Add(r);
                lastY = r.Y;
            }
            return bands;
        }

        /// <summary>The pixel size a PNG on disk really carries, read back from the file rather than
        /// assumed from what was asked for.</summary>
        private static (int Width, int Height) PngPixelSize(string path)
        {
            using var stream = File.OpenRead(path);
            BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return (frame.PixelWidth, frame.PixelHeight);
        }

        private static int CountVisual<T>(DependencyObject root) where T : DependencyObject =>
            WalkTree(root).OfType<T>().Count();
    }
}
