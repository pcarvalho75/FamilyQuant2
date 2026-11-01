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

        // ============================================ F6, the three-way partition

        /// <summary>
        /// F6 without any file: the arithmetic, the by-name matching, the refusals, the CSV and the
        /// drawing, all on fixtures. This section always runs, so a broken partition is caught on a
        /// machine that has never seen a RAW.
        /// </summary>
        private static void ThreeWayPartitionTests(string workDir)
        {
            static Dictionary<string, PartitionValue> Map(PartitionProvenance kind, string source,
                                                          params (string Run, double Value)[] entries)
            {
                var map = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in entries) map[e.Run] = new PartitionValue(e.Value, kind, source);
                return map;
            }

            static Dictionary<string, PartitionValue> Tic(params (string Run, double Value)[] e) =>
                Map(PartitionProvenance.Measured, ThreeWayPartition.SourceRawTic, e);
            static Dictionary<string, PartitionValue> Pl(params (string Run, double Value)[] e) =>
                Map(PartitionProvenance.Recorded, ThreeWayPartition.SourceXic2Quant, e);
            static Dictionary<string, PartitionValue> Dn(params (string Run, double Value)[] e) =>
                Map(PartitionProvenance.Measured, ThreeWayPartition.SourceDeNovoM0, e);

            // ---- the de novo numerator counts PEAKS, not MS2 anchors -------------------------------
            // THE DEFECT THIS EXISTS TO CATCH. GetXIC4 finds the MS1 cluster present AT the anchor
            // scan and walks outwards from it, so two MS2 scans that fired inside one elution peak get
            // the SAME integrated area back. Summing per anchor adds that one area twice. It never
            // goes negative, never fails the closure identity and never looks wrong: it simply draws
            // the de novo segment longer than the ion current it stands for, while the .xic2 segment
            // beside it holds one area per peptide, charge and run.
            static DeNovoXicResult Xic(int scan, double mz, int charge, double m0,
                                       double m1 = 0, double m2 = 0) =>
                new DeNovoXicResult
                {
                    ScanNoMS2 = scan, PrecursorMz = mz, Charge = charge,
                    AreaM0 = m0, AreaM1 = m1, AreaM2 = m2, AreaMMinus1 = -1,
                };

            // The two areas are the real ones the shipped cache holds for m/z 630.84680, charge 2, of
            // run _01: one peak reached from two anchors 630 scans apart, and a second, different
            // integration at the same m/z.
            var anchors = new[]
            {
                Xic(1000, 630.84680, 2, 5627968.3, 100, 10),   // one peak,
                Xic(1006, 630.84683, 2, 5627968.3, 200, 20),   //   sampled three times,
                Xic(1012, 630.84677, 2, 5627968.3, 300, 30),   //   same trapezoid three times over
                Xic(2000, 630.84680, 2, 8612300.0),            // a different integration, kept
                Xic(3000, 630.84680, 3, 5627968.3),            // same area, other charge: another ion
                Xic(4000, 705.35394, 2, 5627968.3),            // same area, far away: another precursor
                Xic(5000, 800.00000, 2, DeNovoXicExtractor.NoCluster),
                new DeNovoXicResult
                {
                    ScanNoMS2 = 6000, PrecursorMz = 900, Charge = 2, AreaM0 = 1, Failure = "boom",
                },
            };

            double perAnchor = anchors
                .Where(a => a.Failure == null && a.AreaM0 > DeNovoXicExtractor.NoCluster)
                .Sum(a => a.AreaM0);

            var peaks = DeNovoBatchExtractor.DistinctPeaks(anchors, DeNovoXicExtractor.ProductionPpm);
            double perPeak = peaks.Sum(p => p.AreaM0);

            Check("anchors that returned one and the same integrated peak collapse to one peak",
                peaks.Count == 4, peaks.Count.ToString(CultureInfo.InvariantCulture));
            // Compared with a sub-milliunit tolerance, not bit equality: the two sums accumulate the
            // same terms in different orders, and the last bit is the order's, not the collapse's.
            Check("and the sum loses exactly the two extra copies of that one area",
                Math.Abs(perPeak - (perAnchor - 2 * 5627968.3)) < 1e-4,
                $"{perAnchor:R} anchor-wise vs {perPeak:R} peak-wise");
            // Not a vacuous check: the per-anchor sum is 44% larger than the ion current it claims.
            Check("the two sums really do differ, so the collapse is doing work",
                perAnchor > perPeak, (perAnchor / perPeak).ToString("F4", CultureInfo.InvariantCulture));

            DeNovoXicResult repeated = peaks.Single(p => p.Charge == 2 && p.AreaM0 == 5627968.3
                                                      && Math.Abs(p.PrecursorMz - 630.8468) < 1e-4);
            Check("the peak is represented by the FIRST anchor that sampled it, not an arbitrary one",
                repeated.ScanNoMS2 == 1000, repeated.ScanNoMS2.ToString(CultureInfo.InvariantCulture));
            Check("so the envelope follows that same representative rather than the largest of them",
                repeated.EnvelopeSum == 5627968.3 + 100 + 10,
                repeated.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));

            Check("a DIFFERENT integration at the same m/z survives - it is not the same peak",
                peaks.Any(p => p.AreaM0 == 8612300.0));
            Check("the same area at another charge is another ion and survives",
                peaks.Any(p => p.Charge == 3));
            Check("the same area outside the extraction window is another precursor and survives",
                peaks.Any(p => Math.Abs(p.PrecursorMz - 705.35394) < 1e-4));
            Check("no cluster is not a peak, and neither is a failed extraction",
                peaks.All(p => p.AreaM0 > DeNovoXicExtractor.NoCluster && p.Failure == null)
                && !peaks.Any(p => p.ScanNoMS2 == 5000 || p.ScanNoMS2 == 6000));

            // What is left over is REPORTED, not silently removed: two distinct integrations inside
            // one precursor window may be two elution events or one broad peak entered twice, and an
            // area cannot tell them apart without the peak's scan span, which is not extracted.
            var (sharedPeaks, sharedArea) = DeNovoBatchExtractor.SharedWindowResidual(
                peaks, DeNovoXicExtractor.ProductionPpm);
            Check("what the collapse cannot decide is reported as a bound instead of guessed away",
                sharedPeaks == 1 && Math.Abs(sharedArea - 5627968.3) < 1e-4,
                $"{sharedPeaks} peak(s), {sharedArea:R}");
            Check("and the bound is not subtracted from anything",
                Math.Abs(perPeak - (5627968.3 * 3 + 8612300.0)) < 1e-4,
                perPeak.ToString("R", CultureInfo.InvariantCulture));

            Check("the provenance token names the grain, so a reader cannot mistake it for a row count",
                ThreeWayPartition.SourceDeNovoM0.Contains("distinct-peaks"),
                ThreeWayPartition.SourceDeNovoM0);

            // ---- the arithmetic and the closure identity -----------------------------------------
            var rows = ThreeWayPartition.Build(
                Tic(("run_a", 1000.0), ("run_b", 2000.0)),
                Pl(("run_a", 400.0), ("run_b", 500.0)),
                Dn(("run_a", 100.0), ("run_b", 250.0)),
                new Dictionary<string, double> { ["run_a"] = 140.0, ["run_b"] = 300.0 });

            Check("one row per run, ordered by name and not by the order they were handed in",
                rows.Count == 2 && rows[0].RunName == "run_a" && rows[1].RunName == "run_b",
                string.Join(" | ", rows.Select(r => r.RunName)));

            ThreeWayPartitionRow a = rows[0];
            Check("the remainder is the TIC less both numerators", a.Remainder.Value == 500.0,
                a.Remainder.Value.ToString("R", CultureInfo.InvariantCulture));
            Check("the shares are each component over the TIC",
                a.SharePatternLab == 0.4 && a.ShareDeNovo == 0.1 && a.ShareRemainder == 0.5,
                $"{a.SharePatternLab:R} / {a.ShareDeNovo:R} / {a.ShareRemainder:R}");
            Check("and they close on 100%", rows.All(r => r.Closes),
                string.Join(" | ", rows.Select(r => r.ShareSum.ToString("R", CultureInfo.InvariantCulture))));
            Check("the remainder is always derived, never measured",
                rows.All(r => r.Remainder.Provenance == PartitionProvenance.Derived));
            Check("the de novo envelope rides along as a diagnostic and stays out of the partition",
                a.DeNovoEnvelope == 140.0 && a.Tic.Value - a.PatternLab.Value - a.DeNovo.Value == a.Remainder.Value);

            ThreeWayPartitionRow grouped = ThreeWayPartition.Grouped(rows);
            Check("the grouped row sums the components",
                grouped.Tic.Value == 3000.0 && grouped.PatternLab.Value == 900.0
                && grouped.DeNovo.Value == 350.0 && grouped.Remainder.Value == 1750.0,
                $"{grouped.Tic.Value:R} / {grouped.PatternLab.Value:R} / {grouped.DeNovo.Value:R} / {grouped.Remainder.Value:R}");
            Check("the grouped remainder equals the sum of the per-run remainders",
                grouped.Remainder.Value == rows.Sum(r => r.Remainder.Value));
            Check("the grouped row closes too", grouped.Closes,
                grouped.ShareSum.ToString("R", CultureInfo.InvariantCulture));
            Check("an aggregate keeps its parts' provenance when they all agree",
                grouped.Tic.Label == "measured:sum-of-runs:" + ThreeWayPartition.SourceRawTic,
                grouped.Tic.Label);

            var mixed = ThreeWayPartition.Grouped(new[]
            {
                rows[0],
                new ThreeWayPartitionRow
                {
                    RunName = "run_c",
                    Tic = PartitionValue.Recorded(1000, ThreeWayPartition.SourcePlpRecordedTic),
                    PatternLab = rows[1].PatternLab,
                    DeNovo = rows[1].DeNovo,
                    Remainder = PartitionValue.Derived(250, ThreeWayPartition.SourceRemainder),
                },
            });
            Check("but an aggregate of two different sources claims neither",
                mixed.Tic.Provenance == PartitionProvenance.Derived
                && mixed.Tic.Source == ThreeWayPartition.SourceSummedMixed, mixed.Tic.Label);

            // ---- the permutation trap -------------------------------------------------------------
            // Each side is fed in a DIFFERENT order and under a different spelling of the same runs.
            // This is the shape of the real data: the package order inside the .xic2 does not follow
            // the RAW file names, so anything that pairs by position pairs one run's TIC with another
            // run's quants.
            var byName = ThreeWayPartition.Build(
                Tic(("C:\\runs\\venom_03.raw", 3000.0), ("venom_01.RAW", 1000.0), ("venom_02.raw.sqt", 2000.0)),
                Pl(("venom_02", 200.0), ("D:/x/venom_03.raw.yada", 300.0), ("venom_01.sqt", 100.0)),
                Dn(("venom_01.ms2", 10.0), ("venom_03", 30.0), ("venom_02.RAW", 20.0)));

            Check("aliases of one run collapse to one row",
                byName.Count == 3 && byName.Select(r => r.RunName)
                    .SequenceEqual(new[] { "venom_01", "venom_02", "venom_03" }),
                string.Join(" | ", byName.Select(r => r.RunName)));
            Check("every run got ITS OWN three numbers, across three different feed orders",
                byName[0].Tic.Value == 1000 && byName[0].PatternLab.Value == 100 && byName[0].DeNovo.Value == 10
                && byName[1].Tic.Value == 2000 && byName[1].PatternLab.Value == 200 && byName[1].DeNovo.Value == 20
                && byName[2].Tic.Value == 3000 && byName[2].PatternLab.Value == 300 && byName[2].DeNovo.Value == 30,
                string.Join(" | ", byName.Select(r =>
                    $"{r.RunName}:{r.Tic.Value:R}/{r.PatternLab.Value:R}/{r.DeNovo.Value:R}")));
            // Not a vacuous check: had the sides been paired any other way, run _01 would have been
            // handed 200 or 300 instead of 100 and its share would have been 20% or 30%.
            Check("the trap is real - a mis-pairing would have shown a different share",
                Math.Abs(byName[0].SharePatternLabPercent - 10.0) < 1e-12
                && Math.Abs(300.0 / byName[0].Tic.Value * 100.0 - 30.0) < 1e-12,
                byName[0].SharePatternLabPercent.ToString("R", CultureInfo.InvariantCulture));

            // ---- a negative remainder survives with its sign ---------------------------------------
            var over = ThreeWayPartition.Build(
                Tic(("run_over", 1000.0)), Pl(("run_over", 700.0)), Dn(("run_over", 400.0)));
            Check("the numerators may exceed the TIC and the remainder keeps its sign",
                over[0].Remainder.Value == -100.0, over[0].Remainder.Value.ToString("R", CultureInfo.InvariantCulture));
            Check("the row is flagged rather than clamped",
                over[0].NegativeRemainder && over[0].ShareRemainderPercent == -10.0,
                over[0].ShareRemainderPercent.ToString("R", CultureInfo.InvariantCulture));
            Check("and the partition still closes on 100%", over[0].Closes,
                over[0].ShareSum.ToString("R", CultureInfo.InvariantCulture));

            // ---- refusals --------------------------------------------------------------------------
            string message = string.Empty;
            Check("a run present on the TIC side but absent from the PatternLab side is an error",
                Throws<ArgumentException>(() => ThreeWayPartition.Build(
                    Tic(("run_a", 1000.0), ("run_b", 2000.0)),
                    Pl(("run_a", 400.0)),
                    Dn(("run_a", 100.0), ("run_b", 250.0))), m => message = m));
            Check("and the error names the run and the side", message.Contains("run_b") && message.Contains("PatternLab"),
                message);

            Check("a run missing from the de novo side is an error too",
                Throws<ArgumentException>(() => ThreeWayPartition.Build(
                    Tic(("run_a", 1000.0)), Pl(("run_a", 400.0)), Dn(("run_z", 100.0))), m => message = m));
            Check("a run with quants but no TIC is an error rather than a silent zero denominator",
                Throws<ArgumentException>(() => ThreeWayPartition.Build(
                    Tic(("run_a", 1000.0)),
                    Pl(("run_a", 400.0), ("run_b", 400.0)),
                    Dn(("run_a", 100.0), ("run_b", 100.0))), m => message = m));
            Check("and it says which run has no total ion current", message.Contains("run_b"), message);

            Check("a non-positive TIC is refused instead of producing infinite shares",
                Throws<ArgumentException>(() => ThreeWayPartition.Build(
                    Tic(("run_a", 0.0)), Pl(("run_a", 1.0)), Dn(("run_a", 1.0)))));
            // Two DIFFERENT keys that denote the same run once the extensions are stripped. Whichever
            // one won would be a coin toss, so neither does.
            Check("two spellings of one run on the same side are refused, not silently merged",
                Throws<ArgumentException>(() => ThreeWayPartition.Build(
                    Tic(("run_a.raw", 1000.0), ("run_a.sqt", 900.0)),
                    Pl(("run_a", 400.0)), Dn(("run_a", 100.0))), m => message = m));
            Check("and that error names the run as well", message.Contains("run_a"), message);

            // ---- the CSV ---------------------------------------------------------------------------
            var forCsv = ThreeWayPartition.Build(
                Tic(("venom_01", 1000.0), ("venom_02", 2000.0), ("venom_03", 4000.0)),
                Pl(("venom_01", 400.0), ("venom_02", 1500.0), ("venom_03", 100.0)),
                Dn(("venom_01", 100.0), ("venom_02", 700.0), ("venom_03", 50.0)),
                new Dictionary<string, double> { ["venom_01"] = 130, ["venom_02"] = 910, ["venom_03"] = 62 });

            string csv = ThreeWayPartition.BuildCsv(forCsv);

            // Same numbers under a comma-decimal culture: the file is read by a program, so it must
            // not change with the machine's language.
            var original = CultureInfo.CurrentCulture;
            string csvPtBr;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
                csvPtBr = ThreeWayPartition.BuildCsv(forCsv);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
            Check("the CSV is byte-identical under a comma-decimal culture", csv == csvPtBr);

            var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Check("the CSV has a header, one row per run and the grouped row last",
                lines.Length == 5, lines.Length.ToString(CultureInfo.InvariantCulture));
            Check("the header is the agreed column list",
                lines[0] == string.Join(",", ThreeWayPartition.CsvHeaders.Select(h => "\"" + h + "\"")),
                lines[0]);

            var parsed = lines.Skip(1).Select(SplitCsvLine).ToList();
            Check("the grouped row is the last one", parsed[3][0] == ThreeWayPartition.GroupedRunName, parsed[3][0]);
            Check("the run rows keep their names", parsed[0][0] == "venom_01" && parsed[2][0] == "venom_03");
            Check("the provenance of every number is stated",
                parsed.All(p => p[2].StartsWith("measured:", StringComparison.Ordinal)
                             && p[4].StartsWith("recorded:", StringComparison.Ordinal)
                             && p[6].StartsWith("measured:", StringComparison.Ordinal)
                             && p[8].StartsWith("derived:", StringComparison.Ordinal)),
                string.Join(" | ", parsed[0].Skip(2).Take(7)));

            // Parsed back invariantly, the file must satisfy the same identity the objects do.
            bool parsedCloses = true;
            foreach (string[] p in parsed)
            {
                double tic = double.Parse(p[1], CultureInfo.InvariantCulture);
                double pl = double.Parse(p[3], CultureInfo.InvariantCulture);
                double dn = double.Parse(p[5], CultureInfo.InvariantCulture);
                double rem = double.Parse(p[7], CultureInfo.InvariantCulture);
                double sum = double.Parse(p[9], CultureInfo.InvariantCulture)
                           + double.Parse(p[10], CultureInfo.InvariantCulture)
                           + double.Parse(p[11], CultureInfo.InvariantCulture);
                if (tic - pl - dn != rem || Math.Abs(sum - 100.0) > 1e-10) parsedCloses = false;
            }
            Check("the numbers read back out of the file still close on 100%", parsedCloses);
            Check("the negative-remainder flag marks the run whose numerators overflow, and only it",
                parsed[0][12] == "false" && parsed[1][12] == "true" && parsed[2][12] == "false"
                && parsed[3][12] == "false",
                string.Join(",", parsed.Select(p => p[12])));
            Check("the de novo envelope is exported as its own diagnostic column",
                double.Parse(parsed[0][13], CultureInfo.InvariantCulture) == 130.0
                && double.Parse(parsed[3][13], CultureInfo.InvariantCulture) == 1102.0,
                parsed[3][13]);

            string csvPath = Path.Combine(workDir, "three_way_partition_fixture.csv");
            ThreeWayPartition.WriteCsv(csvPath, forCsv);
            Check("and the same text lands on disk", File.ReadAllText(csvPath) == csv);

            // ---- the drawing -----------------------------------------------------------------------
            EnsureApplication();

            // A set where nothing overflows, so "no red anywhere" is a meaningful assertion.
            var wellFormed = ThreeWayPartition.Build(
                Tic(("venom_01", 1000.0), ("venom_02", 2000.0), ("venom_03", 4000.0)),
                Pl(("venom_01", 400.0), ("venom_02", 800.0), ("venom_03", 1000.0)),
                Dn(("venom_01", 100.0), ("venom_02", 400.0), ("venom_03", 1000.0)));

            var normal = RenderThreeWay(wellFormed, Path.Combine(workDir, "three_way_chart.png"));
            Check("the chart draws the PatternLab slice", normal.PatternLab > 3000, normal.PatternLab.ToString());
            Check("and the de novo slice", normal.DeNovo > 1500, normal.DeNovo.ToString());
            Check("and the slice without an owner", normal.Remainder > 3000, normal.Remainder.ToString());
            Check("and nothing red, because no run overflowed its TIC",
                normal.Negative == 0, normal.Negative.ToString());

            var overflowing = ThreeWayPartition.Build(
                Tic(("venom_01", 1000.0), ("venom_02", 2000.0), ("venom_03", 4000.0)),
                Pl(("venom_01", 700.0), ("venom_02", 1500.0), ("venom_03", 100.0)),
                Dn(("venom_01", 400.0), ("venom_02", 200.0), ("venom_03", 50.0)));
            var negative = RenderThreeWay(overflowing, Path.Combine(workDir, "three_way_chart_negative.png"));
            Check("a negative remainder is drawn distinctly, in the colour reserved for it",
                negative.Negative > 500, negative.Negative.ToString());
            Check("and the slice without an owner is still drawn for the runs that have one",
                negative.Remainder > 2000, negative.Remainder.ToString());

            // ---- the window ------------------------------------------------------------------------
            Window window = ThreeWayWindow.Create("Three-way partition", forCsv,
                "PatternLab quants read from the .xic2.");
            Check("the window builds headless, with the chart inside it",
                window.Content is Grid g && g.Children.OfType<ThreeWayChart>().Any());
            Check("and it offers the CSV export",
                window.Content is Grid g2 && g2.Children.OfType<Button>()
                    .Any(b => b.Content is string s && s.Contains("CSV") && b.ToolTip != null));
        }

        /// <summary>Pixel census of a rendered three-way chart, by the colours the segments are drawn
        /// in. The PNG is written too, so a change of layout can be looked at rather than guessed at.</summary>
        private static (int PatternLab, int DeNovo, int Remainder, int Negative) RenderThreeWay(
            IReadOnlyList<ThreeWayPartitionRow> rows, string pngPath)
        {
            const int W = 900, H = 300;

            var chart = new ThreeWayChart(rows) { Width = W, Height = H };
            chart.Measure(new Size(W, H));
            chart.Arrange(new Rect(0, 0, W, H));
            chart.UpdateLayout();

            var bmp = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(chart);

            var pixels = new byte[W * H * 4];
            bmp.CopyPixels(pixels, W * 4, 0);

            int Count(Func<byte, byte, byte, bool> match)
            {
                int n = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                    if (pixels[i + 3] > 0 && match(pixels[i + 2], pixels[i + 1], pixels[i])) n++;
                return n;
            }

            using (var fs = File.Create(pngPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }
            Console.WriteLine("        chart rendered to " + pngPath);

            return (
                Count((r, g, b) => b > 200 && r < 140 && g > 130 && g < 200),          // 0x6FA8F0
                Count((r, g, b) => g > 190 && r < 110 && b > 130 && b < 190),          // 0x3FD39C
                Count((r, g, b) => r > 90 && r < 130 && g > 100 && g < 140 && b > 120 && b < 170),  // 0x6B738A
                Count((r, g, b) => r > 190 && g < 110 && b > 70 && b < 130));          // 0xE04F5F
        }

        /// <summary>RFC 4180 enough for the file this suite writes: quoted fields with doubled quotes,
        /// bare fields otherwise.</summary>
        private static string[] SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') { cell.Append(c); continue; }
                    if (i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; continue; }
                    quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(c);
            }

            cells.Add(cell.ToString());
            return cells.ToArray();
        }

        /// <summary>
        /// F6 on the real data: the three runs' ion current split three ways, with every number
        /// carrying where it came from.
        ///
        /// THE THREE SOURCES, AND WHY THEY ARE THESE. The denominator is MEASURED here from each RAW,
        /// because this project's .plp carries no ###Normalization record to read it from. The
        /// PatternLab numerator is READ from the .xic2, because the .plp merged the three technical
        /// replicates into one column and one averaged divisor, from which no arithmetic recovers the
        /// runs; the test asserts that refusal rather than assuming it. The de novo numerator is
        /// MEASURED, by the same extractor and the same production cuts that the .xic2's own quants
        /// were made with, which is what licenses putting them on one bar.
        ///
        /// ONE RUN AT A TIME. A 3 GB RAW held as an MS1 list dominates this process; two resident runs
        /// would end the suite rather than slow it. Each run is extracted, reduced to its totals, and
        /// released before the next is opened.
        /// </summary>
        /// <summary>Main has no enclosing try/catch, so an unguarded throw here (LiteDB on a locked
        /// .xic2, IOException on a OneDrive placeholder RAW) would abort every section after this
        /// one. A failure of this section is a failed check, never the death of the suite.</summary>
        private static void ThreeWayPartitionRealTests(string? peaksDir, string? desktopDir, string workDir)
        {
            try
            {
                ThreeWayPartitionRealTestsCore(peaksDir, desktopDir, workDir);
            }
            catch (Exception ex)
            {
                Check("the three-way partition section ran to completion", false,
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void ThreeWayPartitionRealTestsCore(string? peaksDir, string? desktopDir, string workDir)
        {
            string[] rawNames =
            {
                "20220624_Bjararaca_1ug_3step-240min_01.raw",
                "20220624_Bjararaca_1ug_3step-240min_02.raw",
                "20220624_Bjararaca_1ug_3step-240min_03.raw",
            };

            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");
            string? plpPath = desktopDir == null ? null
                : Path.Combine(desktopDir, "Bjararaca_TIC_Peptides_TechRep.plp");
            string? xic2Path = desktopDir == null ? null : Path.Combine(desktopDir, "FamilyQuant_XIC.xic2");

            if (peaksDir == null || desktopDir == null || !Directory.Exists(peaksDir)
                || sepr2 == null || !File.Exists(sepr2)
                || plpPath == null || !File.Exists(plpPath)
                || xic2Path == null || !File.Exists(xic2Path)
                || rawNames.Any(n => !File.Exists(Path.Combine(desktopDir, n))))
            {
                Console.WriteLine("  SKIP  three-way partition - needs the PEAKS directory, PLVI.sepr2, the .plp, "
                                  + "the .xic2 and the three RAW files");
                skipped++;
                return;
            }

            // ---- which side supplies the PatternLab numerator, and why ----------------------------
            var plp = new PatternLabProject(new FileInfo(plpPath));
            PatternLabRunQuants fromPlp = PatternLabRunQuants.FromPlp(plp);
            Check("the .plp cannot resolve PatternLab quants per run, and says why",
                !fromPlp.IsAvailable && !string.IsNullOrWhiteSpace(fromPlp.Refusal), fromPlp.Refusal);
            Console.WriteLine("        .plp refusal: " + fromPlp.Refusal);

            PatternLabRunQuants patternLab = PatternLabRunQuants.Preferred(plp, xic2Path, out string branchNote);
            Console.WriteLine("        " + branchNote);
            Check("the .xic2 answers per run instead",
                patternLab.IsAvailable && patternLab.AreaByRun.Count == 3,
                patternLab.Refusal ?? patternLab.AreaByRun.Count.ToString(CultureInfo.InvariantCulture));
            if (!patternLab.IsAvailable) return;

            Check("and the runs it names are the three acquisitions, matched by file name",
                rawNames.All(n => patternLab.AreaByRun.ContainsKey(DeNovoReconciler.NormalizeRunName(n))),
                string.Join(" | ", patternLab.AreaByRun.Keys));

            // ---- the kept de novo set, per run ------------------------------------------------------
            var psms = SeproPsmAdapter.Load(sepr2);
            var criteria = new DeNovoFilterCriteria();
            var filtered = new List<PeaksDeNovoRow>();
            foreach (string csv in Directory.GetFiles(peaksDir, "*.denovoOnly.csv", SearchOption.AllDirectories))
                filtered.AddRange(DeNovoFilter.Apply(PeaksCsvReader.ReadDeNovoOnly(csv), criteria));

            var keptByRun = DeNovoReconciler.Reconcile(filtered, psms)
                .Where(r => r.Verdict == ReconciliationVerdict.DeNovoOnly
                         || r.Verdict == ReconciliationVerdict.CoIsolatedExtra)
                .Select(r => r.DeNovo)
                .GroupBy(d => DeNovoReconciler.NormalizeRunName(d.RunName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            Check("all three runs contribute kept de novo rows",
                rawNames.All(n => keptByRun.ContainsKey(DeNovoReconciler.NormalizeRunName(n))),
                string.Join(" | ", keptByRun.Select(kv => kv.Key + "=" + kv.Value.Count.ToString("N0"))));
            if (!rawNames.All(n => keptByRun.ContainsKey(DeNovoReconciler.NormalizeRunName(n))))
                return;

            // ---- one run at a time -----------------------------------------------------------------
            var ticByRun = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            var deNovoByRun = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            var envelopeByRun = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var measuredDistinctPeaks = new List<int>();

            foreach (string rawName in rawNames)
            {
                string raw = Path.Combine(desktopDir, rawName);
                string run = DeNovoReconciler.NormalizeRunName(raw);

                // (0,0) is the trim proved correct for this project: the whole run, both ends.
                double tic = DeNovoXicExtractor.GetTotalIonCurrentArea(raw, 0, 0);
                ticByRun[run] = PartitionValue.Measured(tic, ThreeWayPartition.SourceRawTic);

                var options = new DeNovoXicOptions
                {
                    CachePath = Path.Combine(workDir, "denovo_xic_run" + run.Substring(run.Length - 2) + ".json"),
                    Progress = m => Console.WriteLine("        " + m),
                };

                DeNovoBatchStats stats = ExtractOneRun(raw, keptByRun[run], options);
                measuredDistinctPeaks.Add(stats.DistinctPeaks);
                deNovoByRun[run] = PartitionValue.Measured(stats.TotalAreaM0, ThreeWayPartition.SourceDeNovoM0);
                envelopeByRun[run] = stats.TotalEnvelope;

                Console.WriteLine($"        {run}: TIC {tic:E10} | de novo M+0 {stats.TotalAreaM0:E10} " +
                    $"| envelope {stats.TotalEnvelope:E10} | linhas {stats.RowsIn:N0} | com sinal {stats.WithSignal:N0} " +
                    $"| {stats.ElapsedSeconds:F1} s");
                Console.WriteLine($"        {run}: picos distintos {stats.DistinctPeaks:N0} | ancoras redundantes " +
                    $"{stats.RedundantAnchors:N0} (area nao somada 2x: {stats.RedundantAreaM0:E4}) | " +
                    $"limite janela compartilhada {stats.SharedWindowAreaM0:E4} em {stats.SharedWindowPeaks:N0} picos");

                // The MS1 list of the run just finished must be gone before the next RAW is opened.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            var patternLabByRun = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in patternLab.AreaByRun)
                patternLabByRun[kv.Key] = PartitionValue.Recorded(kv.Value, patternLab.Source);

            // ---- the partition ----------------------------------------------------------------------
            var partition = ThreeWayPartition.Build(ticByRun, patternLabByRun, deNovoByRun, envelopeByRun);
            ThreeWayPartitionRow grouped = ThreeWayPartition.Grouped(partition);
            distinctPeakCensus = measuredDistinctPeaks;
            partitionShareCensus = (grouped.SharePatternLabPercent, grouped.ShareDeNovoPercent,
                grouped.ShareRemainderPercent);

            Console.WriteLine();
            Console.WriteLine("        corrida                                    TIC          PatternLab              " +
                              "de novo               resto");
            foreach (ThreeWayPartitionRow row in partition.Concat(new[] { grouped }))
            {
                Console.WriteLine($"        {row.RunName,-38} {row.Tic.Value,14:E6} {row.PatternLab.Value,14:E6} " +
                    $"{row.DeNovo.Value,14:E6} {row.Remainder.Value,14:E6}");
                Console.WriteLine($"        {"",-38} {"100.0000%",14} {row.SharePatternLabPercent,13:F4}% " +
                    $"{row.ShareDeNovoPercent,13:F4}% {row.ShareRemainderPercent,13:F4}%" +
                    (row.NegativeRemainder ? "   [NEGATIVO]" : ""));
            }
            Console.WriteLine($"        procedencia: TIC {partition[0].Tic.Label} | PatternLab {partition[0].PatternLab.Label}" +
                              $" | de novo {partition[0].DeNovo.Label} | resto {partition[0].Remainder.Label}");
            Console.WriteLine();

            Check("the partition holds one row per acquisition run", partition.Count == 3,
                partition.Count.ToString(CultureInfo.InvariantCulture));
            Check("every run's three shares close on 100%", partition.All(r => r.Closes),
                string.Join(" | ", partition.Select(r => r.ShareSum.ToString("R", CultureInfo.InvariantCulture))));
            Check("and so does the grouped row", grouped.Closes,
                grouped.ShareSum.ToString("R", CultureInfo.InvariantCulture));
            Check("the grouped remainder is the sum of the per-run remainders",
                Math.Abs(grouped.Remainder.Value - partition.Sum(r => r.Remainder.Value))
                    <= Math.Abs(grouped.Remainder.Value) * 1e-12,
                grouped.Remainder.Value.ToString("R", CultureInfo.InvariantCulture));

            Check("the TIC is labelled as measured from the RAW",
                partition.All(r => r.Tic.Label == "measured:" + ThreeWayPartition.SourceRawTic),
                partition[0].Tic.Label);
            Check("the PatternLab slice is labelled as read from the .xic2",
                partition.All(r => r.PatternLab.Label == "recorded:" + ThreeWayPartition.SourceXic2Quant),
                partition[0].PatternLab.Label);
            Check("the de novo slice is labelled as measured by this module",
                partition.All(r => r.DeNovo.Label == "measured:" + ThreeWayPartition.SourceDeNovoM0),
                partition[0].DeNovo.Label);
            Check("the remainder is labelled as derived",
                partition.All(r => r.Remainder.Label == "derived:" + ThreeWayPartition.SourceRemainder),
                partition[0].Remainder.Label);

            // ---- the cross-source check on run _01 ---------------------------------------------------
            // Three independent routes to one number: this measurement, the divisor XQuant stored in
            // the .xic2 when it extracted, and the value quoted in the roadmap. They must agree, or
            // the denominator of the whole plot is not the denominator the stored quants were made
            // against.
            const double Xic2StoredTicRun01 = 875643650496.5421;
            string run01 = DeNovoReconciler.NormalizeRunName(rawNames[0]);
            double measured01 = ticByRun[run01].Value;
            patternLab.StoredTicByRun.TryGetValue(run01, out double stored01);
            double relative = stored01 > 0 ? Math.Abs(measured01 - stored01) / stored01 : double.NaN;

            Console.WriteLine($"        corrida _01, TIC por tres vias: medido do RAW {measured01:R} | " +
                $"gravado no .xic2 {stored01:R} | roteiro {Xic2StoredTicRun01:R} | diferenca relativa {relative:E3}");

            Check("the .xic2 stores for run _01 the divisor the roadmap quotes",
                stored01 > 0 && Math.Abs(stored01 - Xic2StoredTicRun01) / Xic2StoredTicRun01 <= 1e-12,
                stored01.ToString("R", CultureInfo.InvariantCulture));
            Check("and the TIC measured here from the _01 RAW agrees with it to 1e-6 relative",
                relative <= 1e-6, relative.ToString("E3", CultureInfo.InvariantCulture));

            // ---- the file ----------------------------------------------------------------------------
            string csvPath = Path.Combine(workDir, "denovo_three_way_partition.csv");
            ThreeWayPartition.WriteCsv(csvPath, partition);
            Check("the partition is exported with the grouped row last",
                File.ReadAllLines(csvPath).Length == 5, csvPath);
            Console.WriteLine("        relatorio: " + csvPath);
        }

        /// <summary>
        /// One run extracted and reduced to its totals. A separate method on purpose: the per-precursor
        /// result list is several megabytes and must not stay reachable while the next 3 GB RAW is
        /// being loaded.
        /// </summary>
        private static DeNovoBatchStats ExtractOneRun(string rawPath, IReadOnlyList<PeaksDeNovoRow> kept,
                                                      DeNovoXicOptions options)
        {
            var (_, stats) = DeNovoBatchExtractor.ExtractForRun(rawPath, kept, options);
            return stats;
        }
    }
}
