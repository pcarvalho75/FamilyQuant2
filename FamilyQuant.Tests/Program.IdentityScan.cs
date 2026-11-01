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

        private static void ScanValidationTests()
        {
            Check("step 0 is rejected", Throws<ArgumentOutOfRangeException>(() => FamilyClusterFile.ScanIdentities("x", 95, 10, 0)));
            Check("a negative step is rejected", Throws<ArgumentOutOfRangeException>(() => FamilyClusterFile.ScanIdentities("x", 95, 10, -5)));
            Check("from below to is rejected", Throws<ArgumentException>(() => FamilyClusterFile.ScanIdentities("x", 10, 95, 5)));
            Check("above 100% is rejected", Throws<ArgumentOutOfRangeException>(() => FamilyClusterFile.ScanIdentities("x", 120, 10, 5)));
            Check("0% is rejected", Throws<ArgumentOutOfRangeException>(() => FamilyClusterFile.ScanIdentities("x", 95, 0, 5)));

            IdentityScanPlotTests();
        }

        /// <summary>
        /// The sweep drawn as a curve. A chart whose geometry does not match its numbers is worse than no
        /// chart, so the mapping is checked here rather than by looking at the window.
        /// </summary>
        private static void IdentityScanPlotTests()
        {
            var sweep = new List<IdentityScanRow>
            {
                new IdentityScanRow { Identity = 90, Groups = 400, TargetGroups = 200, MixedGroups = 0,  Score = 0.00 },
                new IdentityScanRow { Identity = 70, Groups = 200, TargetGroups = 100, MixedGroups = 0,  Score = 0.00 },
                new IdentityScanRow { Identity = 50, Groups = 100, TargetGroups = 60,  MixedGroups = 5,  Score = 0.05 },
                new IdentityScanRow { Identity = 30, Groups = 40,  TargetGroups = 25,  MixedGroups = 10, Score = 0.25 },
            };

            Check("the floor is the loosest identity that never mixed target with decoy",
                IdentityScanPlot.CleanFloor(sweep) == 70, $"{IdentityScanPlot.CleanFloor(sweep)}");

            // Mixing is not monotonic in identity: a decoy family that mixes at one threshold can come
            // apart again at a looser one. The floor is the bottom of the clean run that starts at the
            // strictest identity, so that everything at or above it really is clean. Taking the loosest
            // clean row anywhere would label 45% a floor here, although 65% above it already mixed.
            var wobbly = new List<IdentityScanRow>
            {
                new IdentityScanRow { Identity = 85, Groups = 300, TargetGroups = 150, MixedGroups = 0, Score = 0.00 },
                new IdentityScanRow { Identity = 75, Groups = 240, TargetGroups = 120, MixedGroups = 0, Score = 0.00 },
                new IdentityScanRow { Identity = 65, Groups = 180, TargetGroups = 95,  MixedGroups = 4, Score = 0.02 },
                new IdentityScanRow { Identity = 55, Groups = 120, TargetGroups = 70,  MixedGroups = 6, Score = 0.05 },
                new IdentityScanRow { Identity = 45, Groups = 80,  TargetGroups = 48,  MixedGroups = 0, Score = 0.00 },
            };
            Check("the floor is the bottom of the clean run, not the loosest clean row anywhere",
                IdentityScanPlot.CleanFloor(wobbly) == 75, $"{IdentityScanPlot.CleanFloor(wobbly)}");

            var mixedAtTop = new List<IdentityScanRow>(wobbly);
            mixedAtTop[0] = new IdentityScanRow { Identity = 85, Groups = 300, TargetGroups = 150, MixedGroups = 1, Score = 0.01 };
            Check("no floor is reported when even the strictest identity already mixed",
                IdentityScanPlot.CleanFloor(mixedAtTop) == null,
                $"{IdentityScanPlot.CleanFloor(mixedAtTop)}");

            var noDecoys = sweep.Select(r => new IdentityScanRow
            {
                Identity = r.Identity, Groups = r.Groups, TargetGroups = r.Groups, MixedGroups = 0, Score = 0
            }).ToList();
            Check("a database with no decoys reports no floor rather than a meaningless 30%",
                IdentityScanPlot.CleanFloor(noDecoys) == null);

            var allMixed = sweep.Select(r => new IdentityScanRow
            {
                Identity = r.Identity, Groups = r.Groups, TargetGroups = r.Groups - 1, MixedGroups = 1, Score = 0.1
            }).ToList();
            Check("when every identity mixed there is no floor to report",
                IdentityScanPlot.CleanFloor(allMixed) == null);

            var plot = new Rect(0, 0, 600, 300);
            var pts = IdentityScanPlot.Map(sweep, plot, r => r.Groups);

            Check("every point is plotted", pts.Count == sweep.Count, $"{pts.Count}");
            Check("the strictest identity sits on the left edge", Math.Abs(pts[0].X - 0) < 1e-9,
                pts[0].X.ToString(CultureInfo.InvariantCulture));
            Check("the loosest identity sits on the right edge", Math.Abs(pts[3].X - 600) < 1e-9,
                pts[3].X.ToString(CultureInfo.InvariantCulture));
            Check("x is proportional to the identity, not to the row order",
                Math.Abs(pts[1].X - 200) < 1e-9 && Math.Abs(pts[2].X - 400) < 1e-9,
                $"{pts[1].X}, {pts[2].X}");
            Check("the largest family count reaches the top of the plot", Math.Abs(pts[0].Y - 0) < 1e-9,
                pts[0].Y.ToString(CultureInfo.InvariantCulture));
            Check("half the families is half the height",
                Math.Abs(pts[1].Y - 150) < 1e-9, pts[1].Y.ToString(CultureInfo.InvariantCulture));

            // Rows arriving loosest-first must draw the same curve: the order is imposed, not assumed.
            var reversed = Enumerable.Reverse(sweep).ToList();
            var same = IdentityScanPlot.Map(reversed, plot, r => r.Groups);
            Check("the curve does not depend on the order the sweep arrives in",
                same.Select(p => (Math.Round(p.X, 9), Math.Round(p.Y, 9)))
                    .SequenceEqual(pts.Select(p => (Math.Round(p.X, 9), Math.Round(p.Y, 9)))));

            Check("an empty sweep draws nothing rather than throwing",
                IdentityScanPlot.Map(new List<IdentityScanRow>(), plot, r => r.Groups).Count == 0);
        }

        // ======================================================== UI smoke

        /// <summary>
        /// Builds the real WPF controls and drives them the way a user would. Everything above tests the
        /// engine; this is the only thing that parses ClusterGenerationControl's XAML, runs its
        /// constructor, and exercises the recalculation, the filters and the User ID edit.
        /// </summary>
        /// <summary>
        /// Renders the identity-scan chart offscreen and checks that it actually drew something, in both
        /// series, in the right halves of the plot. Geometry checks alone cannot catch a chart that maps
        /// its points correctly and then paints nothing, or paints everything in one corner. The PNG is
        /// written out so a person can look at it.
        /// </summary>
        private static void IdentityScanRenderTest(string workDir)
        {
            var sweep = new List<IdentityScanRow>
            {
                new IdentityScanRow { Identity = 95, Groups = 620, TargetGroups = 310, MixedGroups = 0,  Score = 0.000 },
                new IdentityScanRow { Identity = 85, Groups = 480, TargetGroups = 245, MixedGroups = 0,  Score = 0.000 },
                new IdentityScanRow { Identity = 75, Groups = 361, TargetGroups = 186, MixedGroups = 0,  Score = 0.000 },
                new IdentityScanRow { Identity = 65, Groups = 274, TargetGroups = 141, MixedGroups = 2,  Score = 0.007 },
                new IdentityScanRow { Identity = 55, Groups = 198, TargetGroups = 104, MixedGroups = 9,  Score = 0.045 },
                new IdentityScanRow { Identity = 45, Groups = 131, TargetGroups = 72,  MixedGroups = 22, Score = 0.168 },
                new IdentityScanRow { Identity = 35, Groups = 74,  TargetGroups = 44,  MixedGroups = 27, Score = 0.365 },
                new IdentityScanRow { Identity = 25, Groups = 31,  TargetGroups = 20,  MixedGroups = 19, Score = 0.613 },
            };

            var chart = new IdentityScanChart(sweep, currentIdentity: 75) { Width = 860, Height = 380 };
            chart.Measure(new Size(860, 380));
            chart.Arrange(new Rect(0, 0, 860, 380));
            chart.UpdateLayout();

            var bmp = new RenderTargetBitmap(860, 380, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(chart);

            var pixels = new byte[860 * 380 * 4];
            bmp.CopyPixels(pixels, 860 * 4, 0);

            int Count(Func<byte, byte, byte, bool> match, int fromX, int toX)
            {
                int n = 0;
                for (int y = 0; y < 380; y++)
                    for (int x = fromX; x < toX; x++)
                    {
                        int i = (y * 860 + x) * 4;
                        if (pixels[i + 3] > 0 && match(pixels[i + 2], pixels[i + 1], pixels[i])) n++;
                    }
                return n;
            }

            // The two series, by the colours they are drawn in.
            bool IsFamilies(byte r, byte g, byte b) => b > 200 && r < 140 && g > 130 && g < 200;
            bool IsMixing(byte r, byte g, byte b) => r > 200 && g > 100 && g < 170 && b < 110;

            int families = Count(IsFamilies, 0, 860);
            int mixing = Count(IsMixing, 0, 860);

            Check("the chart draws the family series", families > 400, families.ToString());
            Check("and the target/decoy mixing series", mixing > 200, mixing.ToString());

            // Families fall left to right and mixing rises, so each series must have ink on both halves
            // and cannot be bunched into one corner.
            Check("the family curve spans the whole width",
                Count(IsFamilies, 0, 430) > 100 && Count(IsFamilies, 430, 860) > 100,
                $"{Count(IsFamilies, 0, 430)} left, {Count(IsFamilies, 430, 860)} right");
            Check("the mixing curve spans the whole width",
                Count(IsMixing, 0, 430) > 40 && Count(IsMixing, 430, 860) > 40,
                $"{Count(IsMixing, 0, 430)} left, {Count(IsMixing, 430, 860)} right");

            string png = Path.Combine(workDir, "identity_scan_chart.png");
            using (var fs = File.Create(png))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }
            Console.WriteLine("        chart rendered to " + png);
        }
    }
}
