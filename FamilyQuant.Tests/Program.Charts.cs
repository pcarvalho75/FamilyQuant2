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

        /// <summary>
        /// The talk's key figure. Two things are checked and they are different things: that the FOLD is
        /// arithmetic (top N kept in order, everything else summed into exactly one bar, nothing dropped),
        /// and that the DRAWING survives the four inputs that could take it down - a normal set, an empty
        /// one, a family with de novo signal only, and a set whose every value is zero.
        /// </summary>
        private static void FamilyContributionChartTests(string workDir)
        {
            // ---- the fold and the order --------------------------------------------------------------
            const int topN = 15;
            FamilyEnvelopeQuantResult fixture = FamilyFixture(23, unattributedIdentified: 5000,
                                                              unattributedDeNovo: 1500);

            List<FamilyContributionBar> bars = FamilyContributionPlot.Fold(fixture, topN);

            var families = bars.Where(b => b.Kind == FamilyContributionBarKind.Family).ToList();
            var folded = bars.Where(b => b.Kind == FamilyContributionBarKind.OtherFamilies).ToList();
            var residual = bars.Where(b => b.Kind == FamilyContributionBarKind.Unattributed).ToList();

            Check("the fold keeps exactly TopN family bars, one folded bar and one unattributed bar",
                families.Count == topN && folded.Count == 1 && residual.Count == 1
                && bars.Count == topN + 2,
                families.Count + " families, " + folded.Count + " folded, " + residual.Count + " residual");

            Check("the family bars are ordered by total envelope, largest first",
                families.Select(b => b.Total).SequenceEqual(families.Select(b => b.Total).OrderByDescending(v => v)),
                string.Join(", ", families.Take(4).Select(b => b.Total.ToString("F0", CultureInfo.InvariantCulture))));

            Check("and the largest family really is the largest of the input",
                families[0].FamilyUserId == 122 && Math.Abs(families[0].Total - (23000.0 + 5750.0)) < 1e-9,
                families[0].FamilyUserId + " / " + families[0].Total.ToString("R", CultureInfo.InvariantCulture));

            // The folded bar IS the sum of the families it replaced - the arithmetic, not its presence.
            List<FamilyEnvelopeQuantRow> replaced = fixture.Families
                .OrderByDescending(f => f.TotalEnvelope).ThenBy(f => f.FamilyUserId)
                .Skip(topN).ToList();

            Check("the folded bar stands for every family below the cut and says how many",
                folded[0].FamilyCount == replaced.Count && replaced.Count == 23 - topN,
                folded[0].FamilyCount.ToString(CultureInfo.InvariantCulture));
            Check("and its identified value is exactly the sum of the identified values it replaced",
                Math.Abs(folded[0].Identified - replaced.Sum(f => f.IdentifiedEnvelope)) < 1e-9,
                folded[0].Identified.ToString("R", CultureInfo.InvariantCulture) + " vs " +
                replaced.Sum(f => f.IdentifiedEnvelope).ToString("R", CultureInfo.InvariantCulture));
            Check("and its de novo value is exactly the sum of the de novo values it replaced",
                Math.Abs(folded[0].DeNovo - replaced.Sum(f => f.DeNovoEnvelope)) < 1e-9,
                folded[0].DeNovo.ToString("R", CultureInfo.InvariantCulture));

            Check("the unattributed bar carries the residual and is NOT counted as a family",
                Math.Abs(residual[0].Identified - 5000) < 1e-9 && Math.Abs(residual[0].DeNovo - 1500) < 1e-9
                && residual[0].FamilyCount == 0 && residual[0].FamilyUserId == null);

            // Nothing is silently dropped: the bars sum to the whole measured signal.
            Check("the bars together are the whole measured signal, to the last count",
                Math.Abs(bars.Sum(b => b.Total) - fixture.TotalEnvelope) < 1e-6,
                bars.Sum(b => b.Total).ToString("R", CultureInfo.InvariantCulture) + " vs " +
                fixture.TotalEnvelope.ToString("R", CultureInfo.InvariantCulture));

            // A TopN at or above the family count folds nothing at all, and zero means "all".
            Check("a TopN that reaches every family adds no folded bar",
                FamilyContributionPlot.Fold(FamilyFixture(4), 15)
                    .All(b => b.Kind == FamilyContributionBarKind.Family));
            Check("and a TopN of zero means all families, folding nothing",
                FamilyContributionPlot.Fold(fixture, 0)
                    .Count(b => b.Kind == FamilyContributionBarKind.Family) == 23);

            // ---- the four inputs that must not crash -------------------------------------------------
            var deNovoOnly = new FamilyEnvelopeQuantResult();
            deNovoOnly.Families.Add(new FamilyEnvelopeQuantRow
            {
                FamilyUserId = 7,
                Description = "Zinc metalloproteinase-disintegrin-like jararhagin OS=Bothrops jararaca",
                IdentifiedEnvelope = 0,
                DeNovoEnvelope = 4.2e6,
                DeNovoPeptides = 31,
            });

            var zeroTotal = new FamilyEnvelopeQuantResult();
            for (int i = 0; i < 3; i++)
                zeroTotal.Families.Add(new FamilyEnvelopeQuantRow { FamilyUserId = i, Description = "zero " + i });

            var cases = new (string Name, FamilyContributionChart Chart)[]
            {
                ("family_contribution", new FamilyContributionChart(fixture, topN)),
                ("family_contribution_percent", new FamilyContributionChart(fixture, topN, asPercentOfTotal: true)),
                ("family_contribution_empty", new FamilyContributionChart((FamilyEnvelopeQuantResult?)null)),
                ("family_contribution_denovo_only", new FamilyContributionChart(deNovoOnly)),
                ("family_contribution_zero_total", new FamilyContributionChart(zeroTotal)),
            };

            foreach (var (name, chart) in cases)
            {
                string png = Path.Combine(workDir, name + ".png");
                if (File.Exists(png)) File.Delete(png);

                bool ok = true;
                try { ChartImageExporter.Save(chart, png); }
                catch (Exception ex) { ok = false; Console.WriteLine("        " + name + " threw: " + ex.Message); }

                long size = File.Exists(png) ? new FileInfo(png).Length : 0;
                Check("the " + name + " chart renders headless to a PNG that is not a blank stub",
                    ok && size > 3000, size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
                Console.WriteLine("        chart rendered to " + png);
            }

            Check("the empty chart folds to no bars at all, and says so rather than crashing",
                cases[2].Chart.Bars.Count == 0);
            Check("a family with de novo signal only is still one bar, with no identified segment",
                cases[3].Chart.Bars.Count == 1 && cases[3].Chart.Bars[0].Identified == 0
                && cases[3].Chart.Bars[0].DeNovo > 0);
            Check("an all-zero set draws its bars with no divide by zero",
                cases[4].Chart.Bars.Count == 3 && cases[4].Chart.Bars.All(b => b.Total == 0)
                && FamilyContributionPlot.BarTotal(cases[4].Chart.Bars) == 0);

            // The percent mode is a LABEL, not a different fold: the same bars in the same order.
            Check("the percent mode changes what the numbers say and not which bars there are",
                cases[1].Chart.IsPercentOfTotal && !cases[0].Chart.IsPercentOfTotal
                && cases[1].Chart.Bars.Select(b => b.Label)
                    .SequenceEqual(cases[0].Chart.Bars.Select(b => b.Label)));

            // ---- the height can never make a bar disappear --------------------------------------------
            // A chart too short for its bars folds MORE of them; it does not stop drawing. The bars that
            // would be lost by stopping are always the last two - the fold and the unattributed residual -
            // which are exactly the two a reader must not lose.
            FamilyEnvelopeQuantResult crowded = FamilyFixture(200, unattributedIdentified: 9.0e5);
            List<FamilyContributionBar> everyBar = FamilyContributionPlot.Fold(crowded, 0);

            Check("all families folds nothing, so there is one bar per family plus the residual",
                everyBar.Count == 201, everyBar.Count.ToString(CultureInfo.InvariantCulture));

            foreach (double height in new[] { 420.0, 540.0, 260.0 })
            {
                string at = " at " + height.ToString("F0", CultureInfo.InvariantCulture) + " DIP";
                int capacity = FamilyContributionPlot.BarCapacity(height);
                List<FamilyContributionBar> fitted = FamilyContributionPlot.FitToHeight(everyBar, capacity);

                Check("the chart" + at + " keeps only the bars it can draw at a legible height",
                    fitted.Count <= capacity && fitted.Count > 2,
                    fitted.Count + " bars, capacity " + capacity);
                Check("and that fold loses no signal" + at,
                    Math.Abs(fitted.Sum(b => b.Total) - everyBar.Sum(b => b.Total)) < 1e-6,
                    fitted.Sum(b => b.Total).ToString("R", CultureInfo.InvariantCulture) + " vs " +
                    everyBar.Sum(b => b.Total).ToString("R", CultureInfo.InvariantCulture));
                Check("and the residual is still last and still its own bar" + at,
                    fitted.Count(b => b.Kind == FamilyContributionBarKind.Unattributed) == 1
                    && fitted[fitted.Count - 1].Kind == FamilyContributionBarKind.Unattributed);
                Check("and the folded bar says how many families it now stands for" + at,
                    fitted.Single(b => b.Kind == FamilyContributionBarKind.OtherFamilies).FamilyCount
                        == 200 - fitted.Count(b => b.Kind == FamilyContributionBarKind.Family),
                    fitted.Single(b => b.Kind == FamilyContributionBarKind.OtherFamilies)
                        .FamilyCount.ToString(CultureInfo.InvariantCulture));
            }

            // And now from the PIXELS, which is the only witness that catches a chart drawing fewer bars
            // than it holds: the screen height the analytics stack gives the figure, and the export size.
            foreach (double height in new[] { 420.0, 540.0 })
            {
                var crowdedChart = new FamilyContributionChart(crowded, 0);
                int expected = crowdedChart.BarsAtHeight(height).Count;
                (int runs, bool notAFamily) = ProbeBars(crowdedChart, 960, height, column: 301);

                string at = " at 960x" + height.ToString("F0", CultureInfo.InvariantCulture);
                Check("every bar the chart holds" + at + " is really painted, none dropped off the bottom",
                    runs == expected, runs + " painted vs " + expected + " held");
                Check("and the fold and the residual, the two a stopped chart loses first, are on it" + at,
                    notAFamily);
            }

            // ---- the family table itself, over a curation, through the ONE entry point ---------------
            // Two families whose members carry the very sequences the store quantified, so the homology
            // pass has something to align and the rollup is checkable by hand.
            const string seqA = "QEEHRSPHLVATSK";
            const string seqB = "GDSLQLVFRDGSTLK";

            var curation = new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "sp|P00001|VM1_BOTJA",
                        Description = "Snake venom metalloproteinase BJ-PI OS=Bothrops jararaca",
                        Sequence = "MKAWLL" + seqA + "GGWTR",
                        USearchID = 0, UserID = 0,
                    },
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "sp|P00002|PA2_BOTJA",
                        Description = "Basic phospholipase A2 homolog OS=Bothrops jararaca",
                        Sequence = "MRTLWL" + seqB + "AHKPQ",
                        USearchID = 1, UserID = 1,
                    },
                },
            };

            var storeRows = new List<QuantRow>
            {
                AnQr("identified", "fam_01", 11, 2, seqA, 700.5, new double[] { 300, 100, 50, 0 }),
                AnQr("denovo", "fam_01", 12, 2, seqB, 800.5, new double[] { 200, 60, 0, 0 }),
                AnQr("identified", "fam_01", 13, 2, "WWWWWWWWWWWW", 900.5, new double[] { 90, 10, 0, 0 }),
            };

            FamilyEnvelopeQuantResult built = FamilyEnvelopeQuant.BuildFromCuration(storeRows, curation);

            Check("the curation entry point reaches both families and leaves the stranger unattributed",
                built.Families.Count == 2 && Math.Abs(built.UnattributedIdentifiedEnvelope - 100) < 1e-9,
                built.Families.Count + " families, unattributed " +
                built.UnattributedIdentifiedEnvelope.ToString("R", CultureInfo.InvariantCulture));

            FamilyEnvelopeQuantRow? famA = built.Families.FirstOrDefault(f => f.FamilyUserId == 0);
            FamilyEnvelopeQuantRow? famB = built.Families.FirstOrDefault(f => f.FamilyUserId == 1);

            Check("the identified peptide lands in its own family with its whole envelope",
                famA != null && Math.Abs(famA.IdentifiedEnvelope - 450) < 1e-9 && famA.DeNovoEnvelope == 0,
                famA == null ? "missing" : famA.IdentifiedEnvelope.ToString("R", CultureInfo.InvariantCulture));
            Check("the de novo peptide lands in the other, on the de novo side and not the identified one",
                famB != null && famB.IdentifiedEnvelope == 0 && Math.Abs(famB.DeNovoEnvelope - 260) < 1e-9,
                famB == null ? "missing" : famB.DeNovoEnvelope.ToString("R", CultureInfo.InvariantCulture));
            Check("and each family carries the description of its own protein",
                famA != null && famA.Description.Contains("metalloproteinase")
                && famB != null && famB.Description.Contains("phospholipase"),
                famA?.Description);

            // A curation with no proteins is honest emptiness, never a throw.
            FamilyEnvelopeQuantResult none = FamilyEnvelopeQuant.BuildFromCuration(storeRows,
                new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>() });
            Check("a curation with no proteins leaves everything unattributed rather than throwing",
                none.Families.Count == 0 && none.UnattributedIdentifiedEnvelope > 0);

            // ---- decoys are the control, never the biology --------------------------------------------
            // The project's own venom database is a target-reverse file, so the decoys ARE in the curation.
            // The family census has always counted target hits only; this table must attribute the same way
            // or a talk slide carries reversed sequences as families and counts them in the total.
            var decoyOnlyCuration = new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "Reverse_sp|P00001|VM1_BOTJA",
                        Description = "REVERSED decoy of snake venom metalloproteinase",
                        Sequence = "MKAWLL" + seqA + "GGWTR",
                        USearchID = 0, UserID = 0,
                    },
                },
            };

            List<QuantRow> oneIdentified = storeRows.Take(1).ToList();
            FamilyEnvelopeQuantResult decoyBuilt =
                FamilyEnvelopeQuant.BuildFromCuration(oneIdentified, decoyOnlyCuration);

            Check("a family reached only through a decoy gets no bar at all",
                decoyBuilt.Families.Count == 0,
                decoyBuilt.Families.Count.ToString(CultureInfo.InvariantCulture) + " family(ies)");
            Check("and its envelope sits in the unattributed residual, not in the totals as biology",
                Math.Abs(decoyBuilt.UnattributedIdentifiedEnvelope - 450) < 1e-9,
                decoyBuilt.UnattributedIdentifiedEnvelope.ToString("R", CultureInfo.InvariantCulture));

            // A real target-reverse file interleaves the two, and the decoy sorts first.
            var mixedCuration = new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "Reverse_sp|P00001|VM1_BOTJA",
                        Description = "REVERSED decoy of snake venom metalloproteinase",
                        Sequence = new string(("MKAWLL" + seqA + "GGWTR").Reverse().ToArray()),
                        USearchID = 0, UserID = 0,
                    },
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "sp|P00001|VM1_BOTJA",
                        Description = "Snake venom metalloproteinase BJ-PI OS=Bothrops jararaca",
                        Sequence = "MKAWLL" + seqA + "GGWTR",
                        USearchID = 0, UserID = 0,
                    },
                },
            };

            FamilyEnvelopeQuantResult mixedBuilt =
                FamilyEnvelopeQuant.BuildFromCuration(oneIdentified, mixedCuration);

            Check("a family holding both a decoy and a target is named by the target, never by the reversal",
                mixedBuilt.Families.Count == 1
                && mixedBuilt.Families[0].Description.Contains("Snake venom")
                && !mixedBuilt.Families[0].Description.Contains("REVERSED"),
                mixedBuilt.Families.Count == 1 ? mixedBuilt.Families[0].Description : "no family");

            // ---- the denominator is the measurement, not the sum of the bars --------------------------
            // Two deduped peptides of 1000 each: one reaching two families, one reaching none. The bars
            // then sum to 3000 while the run measured 2000, and the residual is a half of the signal and
            // not a third. Dividing by the bars would understate it, and in the flattering direction.
            var sharedRows = new List<QuantRow>
            {
                AnQr("identified", "share_01", 21, 2, "AAAAAAAAAA", 700.5, new double[] { 600, 400, 0, 0 }),
                AnQr("identified", "share_01", 22, 2, "CCCCCCCCCC", 800.5, new double[] { 600, 400, 0, 0 }),
            };

            int[] twoFamilies = new[] { 1, 2 };
            FamilyEnvelopeQuantResult sharedTable = FamilyEnvelopeQuant.Build(sharedRows,
                s => string.Equals(s, "AAAAAAAAAA", StringComparison.Ordinal) ? twoFamilies : Array.Empty<int>(),
                _ => Array.Empty<int>());

            Check("the measured envelope counts each deduped peptide once, whatever it maps to",
                Math.Abs(sharedTable.MeasuredTotalEnvelope - 2000) < 1e-9,
                sharedTable.MeasuredTotalEnvelope.ToString("R", CultureInfo.InvariantCulture));
            Check("while the family rollup counts the shared peptide in both families, as it is meant to",
                Math.Abs(sharedTable.TotalEnvelope - 3000) < 1e-9,
                sharedTable.TotalEnvelope.ToString("R", CultureInfo.InvariantCulture));

            var sharedChart = new FamilyContributionChart(sharedTable, FamilyContributionPlot.DefaultTopN,
                                                          asPercentOfTotal: true);
            double residualShare = sharedChart.Bars
                .Single(b => b.Kind == FamilyContributionBarKind.Unattributed).Total
                / sharedChart.MeasuredEnvelope;

            Check("the percent mode divides by the measured envelope and not by the sum of the bars",
                sharedChart.PercentApplies
                && Math.Abs(sharedChart.MeasuredEnvelope - 2000) < 1e-9
                && Math.Abs(FamilyContributionPlot.BarTotal(sharedChart.Bars) - 3000) < 1e-9,
                sharedChart.MeasuredEnvelope.ToString("R", CultureInfo.InvariantCulture) + " vs bars " +
                FamilyContributionPlot.BarTotal(sharedChart.Bars).ToString("R", CultureInfo.InvariantCulture));
            Check("so the residual reads the half of the signal it really is, not a third",
                Math.Abs(residualShare - 0.5) < 1e-9,
                residualShare.ToString("R", CultureInfo.InvariantCulture));

            // With no measured envelope there is nothing honest to divide by, so the values stay raw.
            Check("a table with no measured envelope labels raw signal instead of inventing a share",
                new FamilyContributionChart(new FamilyEnvelopeQuantResult(), 15, asPercentOfTotal: true)
                    .PercentApplies == false);

            // And the screen wired to a curation really does stand on that table.
            var wired = new AnalyticsControl(storeRows,
                new QuantManifest { PerRunTic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    { ["fam_01"] = 1.0e4 } },
                curation);

            Check("the analytics screen builds the family table from the curation it was handed",
                wired.Families != null && wired.Families.Families.Count == 2
                && WalkTree(wired).OfType<FamilyContributionChart>().Single().Bars.Count == 3,
                wired.Families == null ? "null" : wired.Families.Families.Count.ToString());
            Check("and its TopN selector opens on the documented default with the percent label off",
                wired.FamilyTopN == FamilyContributionPlot.DefaultTopN && !wired.FamilyAsPercent,
                wired.FamilyTopN.ToString(CultureInfo.InvariantCulture));
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

        /// <summary>
        /// The shared chart palette, and every chart drawn in both of its variants.
        ///
        /// THREE DIFFERENT CLAIMS ARE CHECKED. First, that the DARK variant still carries exactly the
        /// values the charts hardcoded before the palette existed, because any drift there is a change to
        /// what the application looks like and nobody asked for one. Second, that the LIGHT variant is a
        /// real light palette - near-white ground, near-black ink, light grid - and that it is not the
        /// dark one with the background inverted: every series moved, because the dark palette's pale blue
        /// and mint green carry about 2:1 against white and wash out on a projector. Third, that the
        /// colours can actually be TOLD APART: the two most important series, and every pair of segments
        /// that sit side by side in a stacked bar, are separated by a CIELAB distance and not merely by a
        /// hue name - and never by a red/green pair, which a colour-blind reader cannot use.
        /// </summary>
        private static void ChartPaletteTests(string workDir)
        {
            ChartThemeTests(workDir);
            UiChartPaletteTests(workDir);
            ChartPalette dark = ChartPalette.Dark;
            ChartPalette light = ChartPalette.Light;

            // ---- the dark variant is EXACTLY what the application always painted ---------------------
            Check("palette: the dark structure is the historical house set (backdrop, ink, muted, grid)",
                IsRgb(dark.Backdrop, 0x33, 0x39, 0x49) && IsRgb(dark.Ink, 0xF2, 0xF0, 0xE9)
                && IsRgb(dark.Muted, 0xA8, 0xA4, 0x9A) && IsRgb(dark.Grid, 0x44, 0x4B, 0x5C));
            Check("palette: the dark series are the historical house set too",
                IsRgb(dark.Identified, 0x6F, 0xA8, 0xF0) && IsRgb(dark.DeNovo, 0x3F, 0xD3, 0x9C)
                && IsRgb(dark.Quantified, 0x55, 0xBD, 0xC6) && IsRgb(dark.NotQuantified, 0xB0, 0x6C, 0xC8)
                && IsRgb(dark.PeptideLikeUnidentified, 0xD8, 0xA6, 0x4B)
                && IsRgb(dark.Remainder, 0x6B, 0x73, 0x8A) && IsRgb(dark.Detected, 0xC9, 0x9A, 0x40)
                && IsRgb(dark.Tic, 0x8A, 0x93, 0xA8) && IsRgb(dark.Mixing, 0xF0, 0x85, 0x4C)
                && IsRgb(dark.NotAFamily, 0xD8, 0xA6, 0x4B));
            Check("palette: the dark axis is still the muted brush and a segment label still the backdrop, " +
                  "so no pixel of the screen moves",
                IsRgb(dark.Axis, 0xA8, 0xA4, 0x9A) && IsRgb(dark.SegmentLabel, 0x33, 0x39, 0x49));
            Check("palette: the dark mass bands are still blue, green and orange, in that order",
                dark.MassBands.Count == 3 && IsRgb(dark.MassBands[0], 0x6F, 0xA8, 0xF0)
                && IsRgb(dark.MassBands[1], 0x3F, 0xD3, 0x9C) && IsRgb(dark.MassBands[2], 0xF0, 0x85, 0x4C));
            Check("palette: Dark is the default of the palette lookup, so nothing changes by accident",
                ReferenceEquals(ChartPalette.For(ChartPaletteVariant.Dark), dark)
                && ReferenceEquals(ChartPalette.For(ChartPaletteVariant.Light), light));

            // ---- the light variant really is light ---------------------------------------------------
            double backLum = ChartPalette.RelativeLuminance(light.Backdrop);
            double inkLum = ChartPalette.RelativeLuminance(light.Ink);
            Check("palette: the light backdrop is near-white (relative luminance above 0.90)",
                backLum > 0.90, backLum.ToString("F4", CultureInfo.InvariantCulture));
            Check("palette: the light ink is near-black (relative luminance below 0.02)",
                inkLum < 0.02, inkLum.ToString("F4", CultureInfo.InvariantCulture));
            Check("palette: ink on that backdrop is far past the readable threshold (12:1 or better)",
                ChartPalette.ContrastRatio(light.Ink, light.Backdrop) >= 12.0,
                ChartPalette.ContrastRatio(light.Ink, light.Backdrop).ToString("F1", CultureInfo.InvariantCulture));
            Check("palette: the light grid is a LIGHT grey - present, never competing with a bar",
                ChartPalette.RelativeLuminance(light.Grid) > 0.60
                && ChartPalette.ContrastRatio(light.Grid, light.Backdrop) < 2.0,
                ChartPalette.RelativeLuminance(light.Grid).ToString("F4", CultureInfo.InvariantCulture));
            Check("palette: the light quiet text is still readable on it (4.5:1 or better)",
                ChartPalette.ContrastRatio(light.Muted, light.Backdrop) >= 4.5,
                ChartPalette.ContrastRatio(light.Muted, light.Backdrop).ToString("F1", CultureInfo.InvariantCulture));

            // The lightness gap between the two main series is a LIGHT-variant requirement: on a projector
            // hue alone is the first thing a bad lamp takes away, and a colour-blind reader in the back row
            // is left with lightness. The historical dark pair does not have it (1.29:1) and is not being
            // changed here; it is separated by hue and chroma, which the CIELAB check above measures.
            Check("palette: in the light variant the two main series differ in LIGHTNESS as well as hue",
                ChartPalette.ContrastRatio(light.Identified, light.DeNovo) >= 1.8,
                ChartPalette.ContrastRatio(light.Identified, light.DeNovo)
                    .ToString("F2", CultureInfo.InvariantCulture));

            // NOT an inversion: every series was re-tuned, because the dark ones wash out on white.
            var reTuned = new (string Name, Brush Dark, Brush Light)[]
            {
                ("identified", dark.Identified, light.Identified),
                ("de novo", dark.DeNovo, light.DeNovo),
                ("quantified", dark.Quantified, light.Quantified),
                ("not quantified", dark.NotQuantified, light.NotQuantified),
                ("peptide-like unidentified", dark.PeptideLikeUnidentified, light.PeptideLikeUnidentified),
                ("remainder", dark.Remainder, light.Remainder),
                ("detected", dark.Detected, light.Detected),
            };
            Check("palette: the light variant is NOT the dark one inverted - every series was re-tuned",
                reTuned.All(s => ChartPalette.ColorOf(s.Dark) != ChartPalette.ColorOf(s.Light)),
                string.Join(", ", reTuned.Where(s => ChartPalette.ColorOf(s.Dark) == ChartPalette.ColorOf(s.Light))
                                        .Select(s => s.Name)));
            Check("palette: and each of them got DARKER, which is what holds contrast on white",
                reTuned.All(s => ChartPalette.RelativeLuminance(s.Light)
                                 < ChartPalette.RelativeLuminance(s.Dark)),
                string.Join(", ", reTuned.Where(s => ChartPalette.RelativeLuminance(s.Light)
                                                     >= ChartPalette.RelativeLuminance(s.Dark))
                                        .Select(s => s.Name)));
            Check("palette: the dark blue and mint green really would have washed out (under 2.6:1 on white)",
                ChartPalette.ContrastRatio(dark.Identified, light.Backdrop) < 2.6
                && ChartPalette.ContrastRatio(dark.DeNovo, light.Backdrop) < 2.6,
                ChartPalette.ContrastRatio(dark.Identified, light.Backdrop).ToString("F2", CultureInfo.InvariantCulture) +
                " / " + ChartPalette.ContrastRatio(dark.DeNovo, light.Backdrop).ToString("F2", CultureInfo.InvariantCulture));

            var lightSeries = new (string Name, Brush Brush)[]
            {
                ("identified", light.Identified), ("de novo", light.DeNovo),
                ("quantified", light.Quantified), ("not quantified", light.NotQuantified),
                ("peptide-like unidentified", light.PeptideLikeUnidentified),
                ("remainder", light.Remainder), ("detected z >= 2", light.DetectedMultiplyCharged),
                ("detected z = 1", light.DetectedSinglyCharged), ("TIC", light.Tic),
                ("mixing", light.Mixing),
            };
            Check("palette: every light series holds at least 3:1 against the backdrop, so it survives a projector",
                lightSeries.All(s => ChartPalette.ContrastRatio(s.Brush, light.Backdrop) >= 3.0),
                string.Join(", ", lightSeries
                    .Where(s => ChartPalette.ContrastRatio(s.Brush, light.Backdrop) < 3.0)
                    .Select(s => s.Name + " " +
                        ChartPalette.ContrastRatio(s.Brush, light.Backdrop).ToString("F2", CultureInfo.InvariantCulture))));

            // A fill that carries a percentage inside it has to contrast with the LABEL, not the panel.
            var labelled = new (string Name, Brush Fill)[]
            {
                ("quantified", light.Quantified), ("not quantified", light.NotQuantified),
                ("peptide-like unidentified", light.PeptideLikeUnidentified),
                ("remainder", light.Remainder),
                ("identified", light.Identified), ("de novo", light.DeNovo),
            };
            Check("palette: every light fill that carries a label is at least 4:1 against that label",
                labelled.All(s => ChartPalette.ContrastRatio(s.Fill, light.SegmentLabel) >= 4.0),
                string.Join(", ", labelled
                    .Where(s => ChartPalette.ContrastRatio(s.Fill, light.SegmentLabel) < 4.0)
                    .Select(s => s.Name + " " +
                        ChartPalette.ContrastRatio(s.Fill, light.SegmentLabel).ToString("F2", CultureInfo.InvariantCulture))));

            // ---- the colours can be told apart, in BOTH variants -------------------------------------
            foreach (ChartPalette p in new[] { dark, light })
            {
                string which = p.Variant.ToString().ToLowerInvariant();

                double idVsDn = ChartPalette.PerceptualDistance(p.Identified, p.DeNovo);
                Check("palette (" + which + "): the two main series are far apart perceptually (CIELAB 25+)",
                    idVsDn >= 25.0, idVsDn.ToString("F1", CultureInfo.InvariantCulture));

                // Not a red/green pair: one is blue-dominant, the other green-dominant, and NEITHER is a
                // red. A deuteranope reads the pair by that difference and by lightness, not by hue alone.
                Color id = ChartPalette.ColorOf(p.Identified);
                Color dn = ChartPalette.ColorOf(p.DeNovo);
                Check("palette (" + which + "): they are a blue/green pair, never red versus green",
                    id.B > id.R && id.B >= id.G && dn.G > dn.R && dn.G >= dn.B,
                    id.ToString() + " / " + dn.ToString());

                // The stacked bar's own neighbours, in the order the recovery chart paints them.
                var neighbours = new (string Name, Brush A, Brush B)[]
                {
                    ("quantified | not quantified", p.Quantified, p.NotQuantified),
                    ("not quantified | peptide-like unidentified", p.NotQuantified, p.PeptideLikeUnidentified),
                    ("peptide-like unidentified | noise", p.PeptideLikeUnidentified, p.Remainder),
                    ("identified | de novo", p.Identified, p.DeNovo),
                    ("detected z >= 2 | detected z = 1", p.DetectedMultiplyCharged, p.DetectedSinglyCharged),
                };
                Check("palette (" + which + "): every pair of adjacent bar segments is distinguishable (CIELAB 25+)",
                    neighbours.All(n => ChartPalette.PerceptualDistance(n.A, n.B) >= 25.0),
                    string.Join(", ", neighbours
                        .Where(n => ChartPalette.PerceptualDistance(n.A, n.B) < 25.0)
                        .Select(n => n.Name + " " +
                            ChartPalette.PerceptualDistance(n.A, n.B).ToString("F1", CultureInfo.InvariantCulture))));
            }

            // ---- every chart renders headless in BOTH variants ---------------------------------------
            var sweep = new List<IdentityScanRow>
            {
                new IdentityScanRow { Identity = 90, Groups = 500, TargetGroups = 260, MixedGroups = 0, Score = 0.000 },
                new IdentityScanRow { Identity = 70, Groups = 320, TargetGroups = 168, MixedGroups = 1, Score = 0.003 },
                new IdentityScanRow { Identity = 50, Groups = 170, TargetGroups = 92, MixedGroups = 8, Score = 0.061 },
            };

            var rows = SyntheticAnalyticsRows();
            var tic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                { ["ui_01"] = 5.0e8, ["ui_02"] = 4.0e8 };
            AnalyticsData data = AnalyticsBuilder.Build(rows, tic);

            // A detection with the charge split, so the recovery chart is exercised in its split form and
            // not only in the shape it had before.
            foreach (NativeRunFraction fr in data.Fractions)
            {
                fr.DetectedAreaMultiplyCharged = fr.Tic * 0.30;
                fr.DetectedAreaSinglyCharged = fr.Tic * 0.08;
                fr.DetectedPeptideLikeArea = fr.DetectedAreaSinglyCharged + fr.DetectedAreaMultiplyCharged;
                fr.DetectedCountMultiplyCharged = 900;
                fr.DetectedCountSinglyCharged = 220;
                fr.DetectedEnvelopeCount = 1120;
            }
            NativeRunFraction? grouped = NativeQuantFractions.Grouped(data.Fractions);

            FamilyEnvelopeQuantResult families = FamilyFixture(6, unattributedIdentified: 4000);
            var overlap = new DeNovoOverlapSummary
            {
                RawDeNovoRows = 100, FilteredDeNovoRows = 40,
                NoSearchPsmInFileAndScan = 24, SharedFileAndScan = 16,
                DifferentChargeInSharedScan = 4, SameFileScanAndCharge = 12,
                SameChargeButNoMzMatch = 8,
                DifferentPrecursorInSharedScan = 12, SamePrecursorWithinTolerance = 4,
                ScanLevelStrictSequenceMatchesIgnoringPtm = 2,
                ScanLevelAdditionalIleLeuEquivalentMatches = 1,
                ScanLevelSequenceDivergencesIgnoringPtmStrict = 10,
                ScanLevelSequenceDivergencesAfterIleLeuFold = 9,
                StrictSequenceMatchesIgnoringPtm = 1, AdditionalIleLeuEquivalentMatches = 1,
                SequenceDivergencesIgnoringPtmStrict = 3, SequenceDivergencesAfterIleLeuFold = 2,
                MinLength = 7, MinAverageLocalConfidence = 80, StretchLength = 5, StretchFloor = 90,
                PrecursorTolerancePpm = 20, WeakXCorr = 2.5,
            };
            DeNovoScoreDistribution scoreDistribution = DeNovoScoreDistributionStudy.Build(new[]
            {
                new PeaksDeNovoRow { Length = 8, Alc = 62, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                new PeaksDeNovoRow { Length = 8, Alc = 81, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                new PeaksDeNovoRow { Length = 8, Alc = 96, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
            });
            var sourceScoreDistributions = new[]
            {
                DeNovoSourceScoreDistributionStudy.Build(new[]
                {
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 62, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 81, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 96, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                }, new DeNovoSourceSelection(DeNovoSource.DeNovoAll, 80)),
                DeNovoSourceScoreDistributionStudy.Build(new[]
                {
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 71, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 77, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                    new PeaksDeNovoRow { Length = 8, ConfidenceValue = 91, LocalConfidence = new[] { 95, 95, 95, 95, 95 } },
                }, new DeNovoSourceSelection(DeNovoSource.DeepNovo, 75)),
            };
            var identification = new DeNovoIdentificationSummary
            {
                FilteredSpectrumCalls = 4,
                CarriedForwardSpectrumCalls = 3,
                FilteredRunSequenceChargeGroups = 4,
                FilteredRunSequenceGroups = 3,
                FilteredDistinctSequencesIgnoringPtm = 2,
                FilteredDistinctSequencesAfterIleLeuFold = 2,
                CarriedForwardRunSequenceChargeGroups = 3,
                CarriedForwardRunSequenceGroups = 2,
                CarriedForwardDistinctSequencesIgnoringPtm = 2,
                CarriedForwardDistinctSequencesAfterIleLeuFold = 2,
                Runs = new List<DeNovoIdentificationRunSummary>
                {
                    new DeNovoIdentificationRunSummary
                    {
                        Run = "ui_01", FilteredSpectrumCalls = 2, CarriedForwardSpectrumCalls = 2,
                        FilteredSequenceChargeGroups = 2, FilteredDistinctSequencesIgnoringPtm = 2,
                        CarriedForwardSequenceChargeGroups = 2,
                        CarriedForwardDistinctSequencesIgnoringPtm = 1,
                    },
                    new DeNovoIdentificationRunSummary
                    {
                        Run = "ui_02", FilteredSpectrumCalls = 2, CarriedForwardSpectrumCalls = 1,
                        FilteredSequenceChargeGroups = 2, FilteredDistinctSequencesIgnoringPtm = 1,
                        CarriedForwardSequenceChargeGroups = 1,
                        CarriedForwardDistinctSequencesIgnoringPtm = 1,
                    },
                },
            };
            var concordance = new IdentificationConcordanceSummary
            {
                PatternLabPsmRows = 12, PeaksDatabasePsmRows = 11,
                PatternLabScanKeys = 10, PeaksDatabaseScanKeys = 9,
                SharedDatabaseSearchScanKeys = 8,
                ExactDatabaseSearchSequenceScanKeys = 7,
                AdditionalIleLeuEquivalentDatabaseSearchScanKeys = 1,
                SameChargeDatabaseSearchScanKeys = 8,
                ExactSameChargeDatabaseSearchScanKeys = 7,
                PrecursorQualifiedDatabaseSearchPairs = 8,
                ExactPrecursorQualifiedDatabaseSearchPairs = 7,
                AdditionalIleLeuEquivalentPrecursorQualifiedPairs = 1,
                PrecursorQualifiedDatabaseSearchScanKeys = 8,
                ExactPrecursorQualifiedDatabaseSearchScanKeys = 7,
                PatternLabPrecursorHypothesesInSharedScans = 8,
                PeaksDatabasePrecursorHypothesesInSharedScans = 8,
                PrecursorTolerancePpm = 20,
                FilteredDeNovoRows = 4,
                DeNovoRowsSharingPeaksDatabaseScan = 2,
                DeNovoRowsSharingPeaksDatabaseScanAndCharge = 1,
                DeNovoRowsExactToPeaksDatabaseAtSameCharge = 1,
            };
            ReferenceEnvelopeDiagnostics envelopeDiagnostics = ReferenceEnvelopeEstimator.Evaluate(
                NativeQuantFractions.DedupToPeptides(rows));
            var presentationManifest = new QuantManifest
            {
                DeNovoOverlap = overlap,
                DeNovoScoreDistribution = scoreDistribution,
                DeNovoSourceScoreDistributions = sourceScoreDistributions.ToList(),
                DeNovoIdentification = identification,
                IdentificationConcordance = concordance,
                ReferenceEnvelopeModelDiagnostics = envelopeDiagnostics,
                PresentationIdentitySweep = sweep,
                DeNovoUnionResolutionMethod = DeNovoUnionResolver.Method,
                DeNovoInputs = new List<DeNovoInputManifest>
                {
                    new DeNovoInputManifest
                    {
                        SourceId = DeNovoSource.DeNovoAll.Id,
                        SourceDisplayName = DeNovoSource.DeNovoAll.DisplayName,
                        ConfidenceField = DeNovoSource.DeNovoAll.ConfidenceColumn,
                        MinimumConfidence = 80, SourcePriority = 0,
                        FilePath = @"C:\fixture\run.denovo.csv", Sha256 = new string('a', 64),
                        RawRows = 3, RowsPassingSourceFilter = 2,
                    },
                    new DeNovoInputManifest
                    {
                        SourceId = DeNovoSource.DeepNovo.Id,
                        SourceDisplayName = DeNovoSource.DeepNovo.DisplayName,
                        ConfidenceField = DeNovoSource.DeepNovo.ConfidenceColumn,
                        MinimumConfidence = 75, SourcePriority = 1,
                        FilePath = @"C:\fixture\run.deepnovo.csv", Sha256 = new string('b', 64),
                        RawRows = 3, RowsPassingSourceFilter = 2,
                    },
                },
            };
            var variantSummaries = new[]
            {
                Variant(DeNovoSource.DenovoOnly.DisplayName, "ALC (%) >= 80", 0.28, 0.12,
                    4800, 7600, DeNovoSource.DenovoOnly.DisplayName),
                Variant(DeNovoSource.DeNovoAll.DisplayName, "ALC (%) >= 80", 0.28, 0.15,
                    4800, 9100, DeNovoSource.DeNovoAll.DisplayName),
                Variant(DeNovoSource.DeepNovo.DisplayName, "SCORE (%) >= 75", 0.28, 0.10,
                    4800, 6500, DeNovoSource.DeepNovo.DisplayName),
                Variant(DeNovoVariantComparison.OnlyPlusDeepNovoVariantName,
                    "ALC (%) >= 80; SCORE (%) >= 75", 0.28, 0.16, 4800, 9900,
                    DeNovoSource.DenovoOnly.DisplayName + " + " + DeNovoSource.DeepNovo.DisplayName),
                Variant(DeNovoVariantComparison.AllPlusDeepNovoVariantName,
                    "ALC (%) >= 80; SCORE (%) >= 75", 0.28, 0.17, 4800, 10400,
                    DeNovoSource.DeNovoAll.DisplayName + " + " + DeNovoSource.DeepNovo.DisplayName),
            };
            DeNovoVariantComparison.ValidateTalkVariantSet(variantSummaries);
            DeNovoVariantSummary exactUnionFixture = variantSummaries.Single(v => v.Variant ==
                DeNovoVariantComparison.OnlyPlusDeepNovoVariantName);
            string exactUnionSources = exactUnionFixture.Sources;
            exactUnionFixture.Sources = DeNovoSource.DeNovoAll.DisplayName + " + " +
                DeNovoSource.DeepNovo.DisplayName;
            Check("variant comparison rejects an only+DeepNovo row mislabeled with all+DeepNovo sources",
                Throws<InvalidDataException>(() =>
                    DeNovoVariantComparison.ValidateTalkVariantSet(variantSummaries)));
            exactUnionFixture.Sources = exactUnionSources;
            List<DeNovoTalkComparisonRow> compactVariantRows =
                DeNovoTalkComparison.Build(variantSummaries);
            Check("compact comparison selects the exact de-novo-only union and excludes PEAKS-all",
                compactVariantRows.Count == 4
                && compactVariantRows.Last().Sources == exactUnionSources
                && !DeNovoTalkComparison.ToCsv(compactVariantRows).Contains(
                    DeNovoSource.DeNovoAll.DisplayName + " + " + DeNovoSource.DeepNovo.DisplayName));
            presentationManifest.PresentationDeNovoVariants = variantSummaries.ToList();
            var sourceVennFixture = new DeNovoSourceVennSummary
            {
                FilteredBeforeUnion = new DeNovoSequenceSetRegions
                {
                    StageId = "filtered-before-union",
                    StageLabel = "After all three source filters · before precursor union",
                    Scope = "pooled",
                    Grain = DeNovoSourceVennStudy.FilteredBeforeUnionGrain,
                    PeaksExclusive = 20642, Shared = 936, DeepNovoExclusive = 2139,
                    Union = 23717,
                },
                CarriedForwardClaimProvenance = new DeNovoSequenceSetRegions
                {
                    StageId = "carried-forward-claim-provenance",
                    StageLabel = "Carried-forward union rows · source-claim provenance",
                    Scope = "pooled",
                    Grain = DeNovoSourceVennStudy.CarriedForwardClaimProvenanceGrain,
                    PeaksExclusive = 19000, Shared = 800, DeepNovoExclusive = 1200,
                    Union = 21000,
                },
                PeaksFunnel = new DeNovoSourceFilterFunnel
                {
                    SourceId = DeNovoSource.DenovoOnly.Id,
                    SourceDisplayName = DeNovoSource.DenovoOnly.DisplayName,
                    ConfidenceField = DeNovoSource.DenovoOnly.ConfidenceField,
                    MinimumConfidence = 80, MinLength = 7, StretchLength = 5, StretchFloor = 90,
                    RawRows = 181643, ScorePassingRows = 41878,
                    ScoreAndLengthPassingRows = 38381, AllFiltersPassingRows = 35686,
                },
                DeepNovoFunnel = new DeNovoSourceFilterFunnel
                {
                    SourceId = DeNovoSource.DeepNovo.Id,
                    SourceDisplayName = DeNovoSource.DeepNovo.DisplayName,
                    ConfidenceField = DeNovoSource.DeepNovo.ConfidenceField,
                    MinimumConfidence = 80, MinLength = 7, StretchLength = 5, StretchFloor = 90,
                    RawRows = 42549, ScorePassingRows = 19476,
                    ScoreAndLengthPassingRows = 18528, AllFiltersPassingRows = 17875,
                },
            };
            sourceVennFixture.Validate();
            presentationManifest.DeNovoSourceVenn = sourceVennFixture;
            presentationManifest.SpectrumComparisonRows = new List<SpectrumComparisonRow>
            {
                new SpectrumComparisonRow
                {
                    FileName = "run01.raw", NormalizedRunName = "run01", ScanNumber = 1001,
                    PrecursorHypothesisIndex = 1, Charge = 2, PrecursorMz = 500.25,
                    PeaksDeNovoSequence = "C(+57.02)EPTIDEK",
                    PeaksDeNovoCleanSequence = "CEPTIDEK", PeaksDeNovoScore = 88,
                    PeaksDeNovoScoreField = "ALC (%)", PeaksDeNovoAccepted = true,
                    DeepNovoSequence = "CEPTIDEK", DeepNovoCleanSequence = "CEPTIDEK",
                    DeepNovoScore = 91, DeepNovoScoreField = "SCORE (%)",
                    DeepNovoAccepted = true,
                },
                new SpectrumComparisonRow
                {
                    FileName = "run01.raw", NormalizedRunName = "run01", ScanNumber = 1002,
                    PrecursorHypothesisIndex = 1, Charge = 2, PrecursorMz = 600.25,
                    PeaksDeNovoSequence = "PEPTIDEK", PeaksDeNovoCleanSequence = "PEPTIDEK",
                    PeaksDeNovoScore = 93, PeaksDeNovoScoreField = "ALC (%)",
                    PeaksDeNovoAccepted = true,
                    DeepNovoSequence = "ALTERNATE", DeepNovoCleanSequence = "ALTERNATE",
                    DeepNovoScore = 89, DeepNovoScoreField = "SCORE (%)",
                    DeepNovoAccepted = true,
                },
            };
            DeNovoAcceptedScoreScatter acceptedScoreFixture =
                DeNovoAcceptedScoreScatterStudy.Build(
                    presentationManifest.SpectrumComparisonRows, sourceScoreDistributions);
            presentationManifest.DeNovoAcceptedScoreScatter = acceptedScoreFixture;
            var reference = new ReferenceComponentSummary
            {
                AllComponents = 100, PureDecoyComponents = 50, TargetBearingComponents = 50,
                ContaminantBearingTargetComponents = 15, NonContaminantTargetComponents = 36,
                MixedTargetDecoyComponents = 1,
            };

            string folder = Path.Combine(workDir, "palette");
            Directory.CreateDirectory(folder);

            foreach (ChartPaletteVariant variant in new[] { ChartPaletteVariant.Dark, ChartPaletteVariant.Light })
            {
                string which = variant.ToString().ToLowerInvariant();
                var charts = new (string Name, FrameworkElement Chart)[]
                {
                    ("identity_scan", new IdentityScanChart(sweep, currentIdentity: 70)),
                    ("recovery", new RecoveryChart(data.Fractions, grouped)),
                    ("xic_by_mass", new XicMassChart(data.Peptides)),
                    ("isotopes_credited", new IsotopeCountChart(data.Peptides)),
                    ("percent_ion_current", new PercentIonCurrentChart(data.Peptides, grouped?.Tic ?? 0)),
                    ("family_contribution", new FamilyContributionChart(families)),
                    ("de_novo_score_distribution", new DeNovoScoreDistributionChart(scoreDistribution)),
                    ("de_novo_source_score_distributions", new DeNovoScoreDistributionChart(sourceScoreDistributions)),
                    ("de_novo_consolidation", new DeNovoConsolidationChart(data, presentationManifest)),
                    ("de_novo_variant_comparison", new DeNovoVariantComparisonChart(variantSummaries)),
                    ("de_novo_source_effect_comparison", new DeNovoTalkComparisonChart(compactVariantRows)),
                    ("de_novo_source_venn", new DeNovoSourceVennChart(sourceVennFixture)),
                    ("de_novo_accepted_score_agreement",
                        new DeNovoAcceptedScoreScatterChart(acceptedScoreFixture)),
                    ("peptide_evidence_overlap", new PeptideEvidenceOverlapChart(data, overlap)),
                    ("signal_by_run", new SignalByRunChart(data.Fractions)),
                    ("signal_uplift", new SignalUpliftChart(grouped)),
                    ("reference_envelope_diagnostics", new ReferenceEnvelopeDiagnosticsChart(envelopeDiagnostics)),
                    ("reference_components", new ReferenceComponentsChart(reference, 70)),
                    ("family_signal_share", new FamilySignalShareChart(families, 10)),
                };

                foreach (var (name, chart) in charts)
                {
                    string png = Path.Combine(folder, name + "_" + which + ".png");
                    if (File.Exists(png)) File.Delete(png);

                    bool ok = true;
                    try { ChartImageExporter.Save(chart, png, variant: variant); }
                    catch (Exception ex)
                    {
                        ok = false;
                        Console.WriteLine("        " + name + " (" + which + ") threw: " + ex.Message);
                    }

                    long size = File.Exists(png) ? new FileInfo(png).Length : 0;
                    Check("the " + name + " chart renders headless in the " + which + " palette",
                        ok && size > 3000, size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");

                    if (!ok || size <= 0) continue;

                    Color ground = PngCornerColour(png);
                    Color expected = ChartPalette.ColorOf(ChartPalette.For(variant).Backdrop);
                    Check("and its ground is the " + which + " backdrop, read back from the file",
                        ground == expected, ground.ToString() + " vs " + expected.ToString());
                }
            }

            Check("family figure labels keep repeated biological names unambiguous",
                FamilyFigureLabel.Format(7, "Acidic PLA2 OS=Bothrops") == "F7 · Acidic PLA2"
                && FamilyFigureLabel.Format(8, "Acidic PLA2 OS=Bothrops") == "F8 · Acidic PLA2"
                && FamilyFigureLabel.Format(7, "Acidic PLA2 OS=Bothrops")
                    != FamilyFigureLabel.Format(8, "Acidic PLA2 OS=Bothrops"));

            var crowdedFamilies = new FamilyEnvelopeQuantResult();
            for (int i = 0; i < 10; i++)
            {
                double total = i == 9 ? 400e9 : (i + 1) * 0.1e9;
                crowdedFamilies.Families.Add(new FamilyEnvelopeQuantRow
                {
                    FamilyUserId = 700 + i,
                    Description = i == 9 ? "Right-edge control family" : "Crowded origin family " + i,
                    IdentifiedEnvelope = total * 0.95,
                    DeNovoEnvelopeReferenceCalibrated = total * 0.05,
                });
            }
            var crowdedChart = new FamilySignalShareChart(crowdedFamilies, labelTopN: 10);
            ChartImageExporter.Render(crowdedChart, variant: ChartPaletteVariant.Light);
            IReadOnlyList<Rect> crowdedBounds = crowdedChart.LastLabelBounds;
            Rect familyPlotSafe = new Rect(98, 114, 808, 338);
            bool labelsDoNotOverlap = Enumerable.Range(0, crowdedBounds.Count).All(i =>
                Enumerable.Range(i + 1, crowdedBounds.Count - i - 1).All(j =>
                    !crowdedBounds[i].IntersectsWith(crowdedBounds[j])));
            Check("family-signal labels stay inside the plot and do not overlap in a crowded layout",
                crowdedBounds.Count == 10
                && crowdedBounds.All(bounds => familyPlotSafe.Contains(bounds))
                && labelsDoNotOverlap,
                "labels=" + crowdedBounds.Count.ToString(CultureInfo.InvariantCulture));
            Check("family-signal labels use leader lines when a point cloud is compressed",
                crowdedChart.LastLabelLeaderCount > 0,
                crowdedChart.LastLabelLeaderCount.ToString(CultureInfo.InvariantCulture));

            // ---- one synthetic complete-project export covers every new reproducibility artefact ----
            string evidenceFolder = Path.Combine(folder, "presentation_evidence");
            if (Directory.Exists(evidenceFolder)) Directory.Delete(evidenceFolder, recursive: true);
            families.Families[0].Description = "=HYPERLINK(\"https://invalid\")";
            PresentationEvidencePackResult evidence = PresentationEvidencePack.Save(
                data, presentationManifest, BuildSyntheticFcf(), families, evidenceFolder);
            var expectedEvidenceFigures = PresentationFigures.FileNames.Concat(new[]
            {
                PresentationFigures.DeNovoSourceVennFile,
                PresentationFigures.DeNovoAcceptedScoreScatterFile,
                PresentationFigures.DeNovoVariantComparisonFile,
                PresentationFigures.DeNovoTalkComparisonFile,
            });
            Check("the presentation evidence pack writes the complete documented base and variant figure set",
                evidence.FigurePaths.Select(Path.GetFileName).SequenceEqual(expectedEvidenceFigures)
                && evidence.FigurePaths.Count == 16,
                string.Join(", ", evidence.FigurePaths.Select(Path.GetFileName)));
            Check("the evidence pack includes the consolidation and model-diagnostics PNGs",
                evidence.FigurePaths.Any(p => Path.GetFileName(p) == PresentationFigures.DeNovoConsolidationFile)
                && evidence.FigurePaths.Any(p => Path.GetFileName(p) == PresentationFigures.ReferenceEnvelopeDiagnosticsFile)
                && evidence.FigurePaths.Where(p =>
                    Path.GetFileName(p) == PresentationFigures.DeNovoConsolidationFile
                    || Path.GetFileName(p) == PresentationFigures.ReferenceEnvelopeDiagnosticsFile)
                    .All(p => new FileInfo(p).Length > 3000));

            string inputsCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.DeNovoInputsCsvFile));
            string scoresCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.SourceScoreCsvFile));
            string scoresJson = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.SourceScoreJsonFile));
            string consolidationCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.IdentificationCsvFile));
            string diagnosticCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.ReferenceEnvelopeDiagnosticsCsvFile));
            string familyCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.FamilyCsvFile));
            string familySourceCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.FamilySourceSequenceCsvFile));
            string compactSourceCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.DeNovoTalkComparisonCsvFile));
            string sourceVennCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.DeNovoSourceVennCsvFile));
            string acceptedScoreCsv = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.DeNovoAcceptedScoreScatterCsvFile));
            string evidenceReadme = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.ReadmeFile));
            string plotJson = File.ReadAllText(Path.Combine(evidenceFolder,
                PresentationEvidencePack.PlotDataFile));
            Check("de_novo_inputs.csv carries both source-native fields, cutoffs and fingerprints",
                inputsCsv.Contains("ALC (%)") && inputsCsv.Contains("SCORE (%)")
                && inputsCsv.Contains(new string('a', 64)) && inputsCsv.Contains(new string('b', 64)));
            Check("source-specific score CSV and JSON keep ALC and SCORE in separate records",
                scoresCsv.Contains("ALC (%)") && scoresCsv.Contains("SCORE (%)")
                && scoresJson.Contains("ALC (%)") && scoresJson.Contains("SCORE (%)"));
            Check("the consolidation CSV exposes all four exact count grains",
                consolidationCsv.Contains("imported,spectrum_row,6")
                && consolidationCsv.Contains("accepted,spectrum_call,4")
                && consolidationCsv.Contains("accepted,pooled_distinct_sequence,2")
                && consolidationCsv.Contains("credited,run_peptidoform_charge_mass_cluster"));
            Check("the model-diagnostics CSV exposes bin support, validation error and sensitivity",
                diagnosticCsv.Contains("fixed neutral-mass-bin support")
                && diagnosticCsv.Contains("cross_validated_weighted_absolute_percent_error")
                && diagnosticCsv.Contains("pooled_ratio_de_novo_estimate"));
            Check("family exports use the stable F{id} · name label",
                familyCsv.Contains("family_label") && familyCsv.Contains("F100 ·")
                && familyCsv.Contains("'=HYPERLINK", StringComparison.Ordinal));
            Check("family source export states exclusive/shared sequence categories and counting grain",
                familySourceCsv.Contains("peaks_denovo_only_exclusive_distinct_sequences")
                && familySourceCsv.Contains("shared_peaks_deepnovo_distinct_sequences")
                && familySourceCsv.Contains("accepted_tier"));
            Check("compact source-effect export contains the exact de-novo-only union, not the PEAKS-all union",
                compactSourceCsv.Contains("PEAKS de novo-only + DeepNovo")
                && !compactSourceCsv.Contains(
                    DeNovoSource.DeNovoAll.DisplayName + " + " + DeNovoSource.DeepNovo.DisplayName));
            Check("source Venn export carries both exact set grains and the DeepNovo filter funnel",
                sourceVennCsv.Contains("filtered-before-union")
                && sourceVennCsv.Contains("carried-forward-claim-provenance")
                && sourceVennCsv.Contains("42549")
                && sourceVennCsv.Contains("SCORE (%)"));
            Check("accepted score export preserves match classes, PTM-cleaning provenance and score scales",
                acceptedScoreCsv.Contains("strict PTM-clean match")
                && acceptedScoreCsv.Contains("strict PTM-clean mismatch")
                && acceptedScoreCsv.Contains("C(+57.02)EPTIDEK")
                && acceptedScoreCsv.Contains("not calibrated as equivalent probabilities"));
            Check("evidence README explains many-family mapping and the multi-table reproducibility boundary",
                evidenceReadme.Contains("FamilyMappingMinimumIdentity")
                && evidenceReadme.Contains("FamilyMappingStatus")
                && evidenceReadme.Contains("no single CSV is claimed to contain every analysis"));
            using (JsonDocument parsedPlot = JsonDocument.Parse(plotJson))
                Check("presentation_plot_data.json is valid JSON and carries the new project-native payloads",
                    parsedPlot.RootElement.TryGetProperty("deNovoInputs", out _)
                    && parsedPlot.RootElement.TryGetProperty("deNovoSourceScoreDistributions", out _)
                    && parsedPlot.RootElement.TryGetProperty("deNovoSourceVenn", out _)
                    && parsedPlot.RootElement.TryGetProperty("deNovoAcceptedScoreScatter", out _)
                    && parsedPlot.RootElement.TryGetProperty("referenceEnvelopeModelDiagnostics", out _)
                    && !plotJson.Contains("Infinity", StringComparison.Ordinal));

            IdentificationConcordanceSummary savedConcordance = presentationManifest.IdentificationConcordance;
            presentationManifest.IdentificationConcordance = new IdentificationConcordanceSummary
            {
                PatternLabPsmRows = 1, PeaksDatabasePsmRows = 1,
                PatternLabScanKeys = 1, PeaksDatabaseScanKeys = 1,
                SharedDatabaseSearchScanKeys = 1,
                ExactDatabaseSearchSequenceScanKeys = 1,
                SameChargeDatabaseSearchScanKeys = 1,
                ExactSameChargeDatabaseSearchScanKeys = 1,
            };
            Check("figure export refuses a stale raw-scan-only concordance manifest",
                Throws<InvalidOperationException>(() => PresentationFigures.Build(
                    data, presentationManifest, BuildSyntheticFcf(), families)));
            presentationManifest.IdentificationConcordance = savedConcordance;

            static DeNovoVariantSummary Variant(string name, string cutoff, double identified,
                                                 double deNovo, int searchReps, int deNovoReps,
                                                 string? sources = null) =>
                new DeNovoVariantSummary
                {
                    Variant = name,
                    Sources = sources ?? name,
                    GenerationId = "chart-fixture-generation",
                    GeneratedUtc = "2026-01-01T00:00:00.0000000Z",
                    DatasetFingerprint = "chart-fixture-dataset",
                    DeNovoInputFingerprint = "chart-fixture-input-" + name,
                    QuantitationSettings = "chart-fixture-settings",
                    AppVersion = "test",
                    ConfidenceMetrics = cutoff.Split(' ')[0],
                    ConfidenceCutoffNote = cutoff,
                    IdentifiedFractionOfTic = identified,
                    DeNovoModelEstimateFractionOfTic = deNovo,
                    CombinedModelEstimateFractionOfTic = identified + deNovo,
                    RelativeUplift = deNovo / identified,
                    SearchRepresentatives = searchReps,
                    DeNovoRepresentatives = deNovoReps,
                    TotalRepresentatives = searchReps + deNovoReps,
                    AcceptedDeNovoSpectrumCalls = deNovoReps * 2,
                    DistinctDeNovoSequencesNoXic = deNovoReps / 2,
                    CrossSideSuppressed = 80,
                    EnvelopeModelMethod = ReferenceEnvelopeEstimator.Method,
                    EnvelopeModelCrossValidatedWape = 0.08,
                    CombinedSensitivityLowerFractionOfTic = identified + deNovo * 0.95,
                    CombinedSensitivityUpperFractionOfTic = identified + deNovo * 1.05,
                };

            // ---- the default of the exporter is the SLIDE palette ------------------------------------
            string defaulted = Path.Combine(folder, "default_variant.png");
            ChartImageExporter.Save(new RecoveryChart(data.Fractions, grouped), defaulted);
            Color defaultGround = PngCornerColour(defaulted);
            Check("the exported PNG is LIGHT by default, because that is what goes into a slide",
                ChartImageExporter.DefaultVariant == ChartPaletteVariant.Light
                && defaultGround == ChartPalette.ColorOf(light.Backdrop),
                defaultGround.ToString());
            Check("and that ground really is near-white when measured off the file, not merely named so",
                ChartPalette.RelativeLuminance(new SolidColorBrush(defaultGround)) > 0.90,
                ChartPalette.RelativeLuminance(new SolidColorBrush(defaultGround))
                    .ToString("F4", CultureInfo.InvariantCulture));

            // The whole analytics figure set, which is what a talk actually receives.
            string figureFolder = Path.Combine(folder, "figures");
            List<string> written = AnalyticsFigures.SaveFromProject(
                rows, new QuantManifest { PerRunTic = tic }, curation: null, folder: figureFolder);
            Check("every saved analytics figure comes out on the light ground",
                written.All(pth => PngCornerColour(pth) == ChartPalette.ColorOf(light.Backdrop)),
                string.Join(", ", written.Where(pth => PngCornerColour(pth) != ChartPalette.ColorOf(light.Backdrop))
                                        .Select(Path.GetFileName)));

            // A chart the exporter was NOT asked to re-colour keeps the screen palette, so the default is
            // a choice the caller can undo rather than something imposed on every render.
            var screenChart = new RecoveryChart(data.Fractions, grouped);
            Check("a chart built for the screen still carries the dark palette until the exporter says otherwise",
                ReferenceEquals(screenChart.Palette, dark));
            screenChart.Palette = null!;
            Check("and assigning a null palette restores the screen one rather than painting nothing",
                ReferenceEquals(screenChart.Palette, dark));
        }

        private static void UiChartPaletteTests(string workDir)
        {
            var exportBrushes = typeof(ChartPalette).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(Brush)).ToArray();
            ChartPalette MutatedExport(string property)
            {
                var clone = (ChartPalette)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(ChartPalette.Light, null)!;
                var target = typeof(ChartPalette).GetProperty(property)!;
                Color original = ChartPalette.ColorOf((Brush)target.GetValue(clone)!);
                target.SetValue(clone, new SolidColorBrush(Color.FromArgb(original.A, (byte)(original.R ^ 1), original.G, original.B)));
                return clone;
            }
            Check("every Light export brush and mass band retains its exact pinned colour",
                PinnedLightPaletteMatches(ChartPalette.Light));
            Check("the export guard detects a one-channel mutation of every brush",
                exportBrushes.All(p => !PinnedLightPaletteMatches(MutatedExport(p.Name))));
            var mutatedBands = (ChartPalette)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(ChartPalette.Light, null)!;
            var bands = ChartPalette.Light.MassBands.ToArray(); bands[0] = ChartPalette.Light.Mixing;
            typeof(ChartPalette).GetProperty(nameof(ChartPalette.MassBands))!.SetValue(mutatedBands, bands);
            Check("the export guard also detects a changed mass-band entry", !PinnedLightPaletteMatches(mutatedBands));
            var rows = new List<IdentityScanRow>
            {
                new() { Identity = 90, Groups = 500, TargetGroups = 260, MixedGroups = 0, Score = 0 },
                new() { Identity = 70, Groups = 320, TargetGroups = 168, MixedGroups = 1, Score = 0.003 },
                new() { Identity = 50, Groups = 170, TargetGroups = 92, MixedGroups = 8, Score = 0.061 }
            };
            byte[] Export(ChartPalette? palette = null)
            {
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(ChartImageExporter.Render(new IdentityScanChart(rows, 70, palette), 900, 500, 1)));
                using var memory = new MemoryStream(); png.Save(memory); return memory.ToArray();
            }
            byte[] before = Export();
            int changed = 0;
            EventHandler handler = (_, _) => changed++;
            FamilyQuant.Themes.ThemeManager.ThemeChanged += handler;
            try
            {
                foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
                {
                    FamilyQuant.Themes.ThemeManager.Apply(theme);
                    var ui = ChartPalette.UiPalette;
                    Check(theme + " UI chart structure follows the current tokens",
                        ChartPalette.ColorOf(ui.Backdrop) == ChartPalette.ColorOf((Brush)Application.Current.FindResource("CanvasBrush"))
                        && ChartPalette.ColorOf(ui.Ink) == ChartPalette.ColorOf((Brush)Application.Current.FindResource("TextBrush"))
                        && ChartPalette.ColorOf(ui.Families) == ChartPalette.ColorOf((Brush)Application.Current.FindResource("ChartIdentifiedBrush")));
                    Check(theme + " UI chart text and scan curves remain readable",
                        ChartPalette.ContrastRatio(ui.Ink, ui.Backdrop) >= 4.5 && ChartPalette.ContrastRatio(ui.Muted, ui.Backdrop) >= 4.5
                        && new[] { ui.Families, ui.Mixing, ui.Floor }.All(b => ChartPalette.ContrastRatio(b, ui.Backdrop) >= 3));
                    Check(theme + " UI snapshots are frozen and exporting them still selects the explicit Light palette",
                        typeof(ChartPalette).GetProperties().Where(p => p.PropertyType == typeof(Brush)).All(p => ((Brush)p.GetValue(ui)!).IsFrozen)
                        && ui.MassBands.All(b => b.IsFrozen) && before.SequenceEqual(Export(ui)));
                    var chart = new IdentityScanChart(rows, 70, ui);
                    chart.Measure(new Size(900, 500)); chart.Arrange(new Rect(0, 0, 900, 500)); chart.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(900, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(chart);
                    var image = new PngBitmapEncoder(); image.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(workDir, "ui-palette-" + theme.ToString().ToLowerInvariant() + ".png")); image.Save(stream);
                }
                Check("theme change notifications let open chart views refresh their snapshot", changed == 2);
            }
            finally
            {
                FamilyQuant.Themes.ThemeManager.ThemeChanged -= handler;
                FamilyQuant.Themes.ThemeManager.Apply(FamilyQuant.Themes.AppTheme.Dark);
            }
        }

        private static bool PinnedLightPaletteMatches(ChartPalette palette)
        {
            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Backdrop"] = "#FFFBFAF7", ["Ink"] = "#FF16181E", ["Muted"] = "#FF565C68",
                ["Grid"] = "#FFDDDDE3", ["Panel"] = "#FFEEEFED", ["Axis"] = "#FF565C68", ["SegmentLabel"] = "#FFFFFFFF",
                ["Identified"] = "#FF10428C", ["DeNovo"] = "#FF1F8F5A", ["Quantified"] = "#FF0F6B84",
                ["NotQuantified"] = "#FF7B2D9E", ["PeptideLikeUnidentified"] = "#FFA85F13", ["Remainder"] = "#FF5F6675",
                ["Detected"] = "#FF8A5A12", ["DetectedMultiplyCharged"] = "#FF8A5A12", ["DetectedSinglyCharged"] = "#FF8C7C5C",
                ["Tic"] = "#FF7B8394", ["Families"] = "#FF10428C", ["Mixing"] = "#FFC2410C", ["Floor"] = "#FF1F8F5A",
                ["NotAFamily"] = "#FFA85F13", ["Found"] = "#FF10428C", ["OfTic"] = "#FF1F8F5A",
                ["Negative"] = "#FFC42B1C"
            };
            var actual = typeof(ChartPalette).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(Brush)).ToDictionary(p => p.Name,
                    p => ChartPalette.ColorOf((Brush)p.GetValue(palette)!).ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
            return actual.Count == expected.Count && expected.All(e => actual.GetValueOrDefault(e.Key) == e.Value)
                && palette.MassBands.Select(b => ChartPalette.ColorOf(b).ToString(CultureInfo.InvariantCulture))
                    .SequenceEqual(new[] { "#FF10428C", "#FF1F8F5A", "#FFC2410C" });
        }

        /// <summary>
        /// The shared PNG exporter: that it produces the pixel size that was ASKED for (the default being
        /// a projector-sized 1920x1080 over a 960x540 layout), that it works with no window anywhere, and
        /// that the whole analytics figure set comes out under its documented names.
        /// </summary>
        private static void ChartImageExportTests(string workDir)
        {
            string folder = Path.Combine(workDir, "figures");
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

            // ---- the requested pixel size ------------------------------------------------------------
            Check("the exporter's default layout is the 960x540 slide it documents",
                ChartImageExporter.DefaultLayoutWidth == 960 && ChartImageExporter.DefaultLayoutHeight == 540
                && ChartImageExporter.DefaultScale == 2.0);

            var sweep = new List<IdentityScanRow>
            {
                new IdentityScanRow { Identity = 90, Groups = 500, TargetGroups = 260, MixedGroups = 0, Score = 0.000 },
                new IdentityScanRow { Identity = 70, Groups = 320, TargetGroups = 168, MixedGroups = 1, Score = 0.003 },
                new IdentityScanRow { Identity = 50, Groups = 170, TargetGroups = 92,  MixedGroups = 8, Score = 0.061 },
                new IdentityScanRow { Identity = 30, Groups = 60,  TargetGroups = 35,  MixedGroups = 21, Score = 0.402 },
            };

            string defaultPng = Path.Combine(folder, "exporter_default.png");
            ChartImageExporter.Save(new IdentityScanChart(sweep, currentIdentity: 70), defaultPng);

            var (w, h) = PngPixelSize(defaultPng);
            Check("the default export is 1920 by 1080 device pixels, crisp on a projector",
                w == 1920 && h == 1080, w + "x" + h);

            string customPng = Path.Combine(folder, "exporter_custom.png");
            ChartImageExporter.Save(new IdentityScanChart(sweep, currentIdentity: 70), customPng,
                                    layoutWidth: 640, layoutHeight: 360, scale: 1.5);
            var (cw, ch) = PngPixelSize(customPng);
            Check("and a caller's own layout and scale are honoured exactly",
                cw == 960 && ch == 540, cw + "x" + ch);

            Check("a scale of zero is a caller error and is refused, not silently clamped",
                Throws<ArgumentOutOfRangeException>(() =>
                    ChartImageExporter.Save(new IdentityScanChart(sweep), Path.Combine(folder, "never.png"),
                                            scale: 0)));

            // ---- the sweep figure through the window's own entry point -------------------------------
            string sweepPng = Path.Combine(folder, IdentityScanWindow.FigureFile);
            IdentityScanWindow.SaveFigure(sweep, sweepPng, currentIdentity: 70);
            var (sw, sh) = PngPixelSize(sweepPng);
            Check("the identity sweep saves as a figure of the same size, with no window opened",
                File.Exists(sweepPng) && sw == 1920 && sh == 1080, sw + "x" + sh);

            // The window offers it too, and the button explains itself like every other command.
            Window scanWindow = IdentityScanWindow.Create("Identity scan · test", sweep, "report text", 70);
            Button? saveButton = (scanWindow.Content as FrameworkElement) == null ? null
                : WalkTree((DependencyObject)scanWindow.Content).OfType<Button>()
                    .FirstOrDefault(b => b.Content is string s && s.Contains("PNG"));

            Check("the sweep window offers the figure export, with a tooltip that says what it writes",
                saveButton != null && saveButton.ToolTip is string tip && tip.Length > 100
                && tip.Contains("1920"), saveButton?.ToolTip as string);

            // Pressed off a display it must REFUSE in words, never open a dialog nothing can close.
            saveButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var scanStatus = WalkTree((DependencyObject)scanWindow.Content).OfType<TextBlock>()
                .FirstOrDefault(t => t.Text.Contains("not on a display"));
            Check("and pressed with no display it says so instead of opening a dialog",
                scanStatus != null, scanStatus?.Text);

            // ---- the whole analytics figure set ------------------------------------------------------
            var rows = SyntheticAnalyticsRows();
            var tic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                { ["ui_01"] = 5.0e8, ["ui_02"] = 4.0e8 };

            List<string> written = AnalyticsFigures.SaveFromProject(
                rows, new QuantManifest { PerRunTic = tic }, curation: null, folder: folder);

            Check("every analytics figure is written, under the documented names",
                written.Select(Path.GetFileName).SequenceEqual(AnalyticsFigures.FileNames),
                string.Join(", ", written.Select(Path.GetFileName)));

            Check("and each of them is a real PNG of the projector size, not a stub",
                written.All(p => new FileInfo(p).Length > 3000)
                && written.All(p => PngPixelSize(p) == (1920, 1080)),
                string.Join(", ", written.Select(p => new FileInfo(p).Length.ToString("N0", CultureInfo.InvariantCulture))));

            foreach (string p in written) Console.WriteLine("        figure written to " + p);

            // The screen's own export path is the same one, so what a talk gets is what the screen showed.
            var control = new AnalyticsControl(rows, new QuantManifest { PerRunTic = tic });
            control.ViewModel.ExportKind = ResultsExportKind.Figures;
            var figureButton = UiField<Button>(control, "ButtonExport");
            Check("the analytics screen offers the figure export, enabled once there is something to draw",
                figureButton != null && figureButton.IsEnabled
                && figureButton.ToolTip is string t && t.Contains("1920"), figureButton?.ToolTip as string);

            figureButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("and pressed off a display it refuses in the status line rather than opening a dialog",
                UiField<TextBlock>(control, "TextBlockStatus")!.Text.Contains("not on a display"),
                UiField<TextBlock>(control, "TextBlockStatus")!.Text);

            var emptyControl = new AnalyticsControl(new List<QuantRow>(), null);
            Check("with no quantitation the figure export is dark, like the two CSV exports",
                UiField<Button>(emptyControl, "ButtonExport")!.IsEnabled == false);
        }

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
