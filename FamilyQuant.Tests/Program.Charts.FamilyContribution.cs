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
                UniqueDeNovoEnvelope = 4.2e6,
                SplitDeNovoEnvelope = 4.2e6,
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
            // Two peptides of 1000 each: one reaches two families, one reaches none. The unique chart
            // keeps the shared peptide in its own bar and sums to the measured 2000.
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
                && Math.Abs(FamilyContributionPlot.BarTotal(sharedChart.Bars) - 2000) < 1e-9
                && sharedChart.Bars.Single(b => b.Kind == FamilyContributionBarKind.Shared).Identified == 1000,
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
            Check("and its TopN selector opens on the documented default with percent labels",
                wired.FamilyTopN == FamilyContributionPlot.DefaultTopN && wired.FamilyAsPercent,
                wired.FamilyTopN.ToString(CultureInfo.InvariantCulture));
        }

    }
}
