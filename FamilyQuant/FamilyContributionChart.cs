using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>What a bar of the family-contribution chart stands for. A folded bar and the
    /// unattributed residual are NOT families, and the chart says so rather than letting them pass for
    /// one.</summary>
    public enum FamilyContributionBarKind
    {
        /// <summary>One family of the current grouping, by its UserID.</summary>
        Family,

        /// <summary>Every family below the TopN cut, summed into one bar so nothing is silently dropped.</summary>
        OtherFamilies,

        /// <summary>Signal that reached no family at all. Drawn apart, because it is a residual and not
        /// a biological group.</summary>
        Unattributed,
    }

    /// <summary>
    /// One horizontal bar: its identified observed-envelope signal and its de novo reference-model
    /// estimate, plus what the bar is (a family, the fold below the cut, or the unattributed residual).
    /// </summary>
    public sealed class FamilyContributionBar
    {
        public FamilyContributionBarKind Kind { get; set; } = FamilyContributionBarKind.Family;

        /// <summary>The family's UserID, or null for the folded and unattributed bars.</summary>
        public int? FamilyUserId { get; set; }

        /// <summary>How many families this bar stands for: 1 for a family, the folded count for the
        /// "other families" bar, 0 for the unattributed residual.</summary>
        public int FamilyCount { get; set; }

        /// <summary>The bar's caption in the left gutter, already free of the description.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>The family's description, truncated only at draw time so the model stays the data.</summary>
        public string Description { get; set; } = string.Empty;

        public double Identified { get; set; }
        public double DeNovo { get; set; }

        public double Total => Identified + DeNovo;
    }

    /// <summary>
    /// The geometry and the folding of the family-contribution chart, kept apart from the drawing so the
    /// ordering and the arithmetic can be checked without opening a window - the same separation
    /// <see cref="IdentityScanPlot"/> makes for the identity sweep.
    /// </summary>
    public static class FamilyContributionPlot
    {
        /// <summary>How many families get a bar of their own before the rest are folded. Fifteen fits a
        /// slide without the bars becoming stripes.</summary>
        public const int DefaultTopN = 15;

        /// <summary>The band above the bars, holding the heading.</summary>
        public const double TopBand = 40;

        /// <summary>The band below the bars, holding the two legend lines.</summary>
        public const double LegendBand = 48;

        /// <summary>The space between two bars.</summary>
        public const double BarGap = 6;

        /// <summary>The shortest bar that is still a bar. Nothing is ever drawn thinner than this, and
        /// nothing is ever dropped to keep it: <see cref="FitToHeight"/> folds instead.</summary>
        public const double MinBarHeight = 6;

        /// <summary>The tallest a bar grows when there is room to spare.</summary>
        public const double MaxBarHeight = 30;

        /// <summary>
        /// Orders the families by total (identified plus de novo) envelope descending, keeps
        /// <paramref name="topN"/> of them as bars of their own, folds every remaining family into ONE
        /// "other families" bar, and appends the unattributed residual as its own bar when there is any.
        ///
        /// Nothing is dropped: the identified and the de novo sums over the returned bars equal the sums
        /// over the whole result. A <paramref name="topN"/> of zero or less means "no fold at all", which
        /// is what the interface's "all" choice asks for.
        /// </summary>
        public static List<FamilyContributionBar> Fold(FamilyEnvelopeQuantResult? result, int topN)
        {
            var bars = new List<FamilyContributionBar>();
            if (result == null) return bars;

            // Re-sorted here rather than trusted: FamilyEnvelopeQuant.Build already orders this way, but
            // a caller may hand in a result it assembled itself and the fold has to be the same either way.
            List<FamilyEnvelopeQuantRow> ordered = result.Families
                .Where(f => f != null)
                .OrderByDescending(f => f.TotalEnvelopeReferenceCalibrated)
                .ThenBy(f => f.FamilyUserId)
                .ToList();

            int keep = topN <= 0 ? ordered.Count : Math.Min(topN, ordered.Count);

            for (int i = 0; i < keep; i++)
            {
                FamilyEnvelopeQuantRow f = ordered[i];
                bars.Add(new FamilyContributionBar
                {
                    Kind = FamilyContributionBarKind.Family,
                    FamilyUserId = f.FamilyUserId,
                    FamilyCount = 1,
                    Label = FamilyFigureLabel.Format(f.FamilyUserId, f.Description, maximumNameLength: 34),
                    Description = string.Empty,
                    Identified = f.IdentifiedEnvelope,
                    DeNovo = f.DeNovoEnvelopePrimary,
                });
            }

            int folded = ordered.Count - keep;
            if (folded > 0)
            {
                bars.Add(new FamilyContributionBar
                {
                    Kind = FamilyContributionBarKind.OtherFamilies,
                    FamilyUserId = null,
                    FamilyCount = folded,
                    Label = "other families (" + folded.ToString("N0", CultureInfo.CurrentCulture) + ")",
                    Description = "every family below the cut, summed so none is silently dropped",
                    Identified = ordered.Skip(keep).Sum(f => f.IdentifiedEnvelope),
                    DeNovo = ordered.Skip(keep).Sum(f => f.DeNovoEnvelopePrimary),
                });
            }

            double unattributed = result.UnattributedIdentifiedEnvelope
                + result.UnattributedDeNovoEnvelopePrimary;
            if (unattributed > 0)
            {
                bars.Add(new FamilyContributionBar
                {
                    Kind = FamilyContributionBarKind.Unattributed,
                    FamilyUserId = null,
                    FamilyCount = 0,
                    Label = "unattributed",
                    Description = "signal that reached no family - not a family itself",
                    Identified = result.UnattributedIdentifiedEnvelope,
                    DeNovo = result.UnattributedDeNovoEnvelopePrimary,
                });
            }

            return bars;
        }

        /// <summary>
        /// The sum of the bars.
        ///
        /// NOT a denominator and NOT the measured signal: under the count-in-every-family policy a
        /// peptide belonging to two families is in both bars, so this overlaps and is at least the
        /// measured envelope. It is here so the chart can SAY by how much the bars overlap.
        /// </summary>
        public static double BarTotal(IEnumerable<FamilyContributionBar>? bars) =>
            bars == null ? 0 : bars.Sum(b => b.Total);

        /// <summary>
        /// The honest denominator: the whole measured envelope of the run after the per-peptide collapse,
        /// each peptide counted once - the same basis as the recovery numbers and the paper. Zero when
        /// there is no table, and the chart never divides by it without checking.
        /// </summary>
        public static double MeasuredTotal(FamilyEnvelopeQuantResult? result) =>
            result == null ? 0 : result.MeasuredTotalEnvelopeReferenceCalibrated;

        /// <summary>
        /// How many bars a control of this height can draw at <see cref="MinBarHeight"/> or taller. At
        /// least one, whatever the height, because a chart that can draw nothing still has to draw
        /// something rather than pretend.
        /// </summary>
        public static int BarCapacity(double controlHeight)
        {
            double area = Math.Max(1, controlHeight - TopBand - LegendBand);
            return Math.Max(1, (int)Math.Floor((area + BarGap) / (MinBarHeight + BarGap)));
        }

        /// <summary>
        /// Folds a bar list down to at most <paramref name="capacity"/> bars WITHOUT losing any signal:
        /// the families past the cut are summed into the "other families" bar, which already exists or is
        /// created, and the unattributed residual is never folded because it is not a family.
        ///
        /// This is what a chart too short for its own bars does instead of stopping halfway. The sums are
        /// preserved exactly, so the legend's totals and the percent labels are the same numbers either
        /// way, and the fold's caption says how many families it now stands for.
        /// </summary>
        public static List<FamilyContributionBar> FitToHeight(IEnumerable<FamilyContributionBar>? bars,
                                                              int capacity)
        {
            List<FamilyContributionBar> source =
                (bars ?? Enumerable.Empty<FamilyContributionBar>()).Where(b => b != null).ToList();

            if (capacity <= 0 || source.Count <= capacity) return source;

            List<FamilyContributionBar> families =
                source.Where(b => b.Kind == FamilyContributionBarKind.Family).ToList();
            List<FamilyContributionBar> others =
                source.Where(b => b.Kind == FamilyContributionBarKind.OtherFamilies).ToList();
            List<FamilyContributionBar> residual =
                source.Where(b => b.Kind == FamilyContributionBarKind.Unattributed).ToList();

            // One bar of the budget goes to the fold itself, and the residual keeps its own bars: it is
            // the number a reader takes as "what we still cannot explain" and it is never merged away.
            int room = capacity - 1 - residual.Count;
            room = Math.Max(1, Math.Min(room, families.Count));

            var kept = new List<FamilyContributionBar>(families.Take(room));
            List<FamilyContributionBar> foldedAway = families.Skip(room).Concat(others).ToList();

            int foldedCount = foldedAway.Sum(b => b.FamilyCount);
            kept.Add(new FamilyContributionBar
            {
                Kind = FamilyContributionBarKind.OtherFamilies,
                FamilyUserId = null,
                FamilyCount = foldedCount,
                Label = "other families (" + foldedCount.ToString("N0", CultureInfo.CurrentCulture) + ")",
                Description = "every family below the cut, summed so none is silently dropped",
                Identified = foldedAway.Sum(b => b.Identified),
                DeNovo = foldedAway.Sum(b => b.DeNovo),
            });

            kept.AddRange(residual);
            return kept;
        }
    }

    /// <summary>
    /// The talk's key figure: one horizontal stacked bar per family of the current grouping, split into
    /// the family's IDENTIFIED envelope signal and its DE NOVO envelope signal, so a reader sees at a
    /// glance which families the de novo work actually enriches rather than which families are merely big.
    ///
    /// Families are ordered by total envelope, the top N get a bar each, everything below the cut is
    /// folded into one "other families" bar, and the unattributed residual is drawn apart and labelled as
    /// not being a family. NOTHING IS DROPPED: a chart too short to hold every bar at a legible size folds
    /// more families into that one bar rather than leaving bars unpainted, which preserves the sums
    /// exactly and keeps the figure's claim of completeness true at any size.
    ///
    /// The percent mode divides by the MEASURED envelope of the run - every deduped peptide counted once,
    /// the same basis as the recovery numbers and the paper - and not by the sum of the bars, which
    /// double-counts a peptide belonging to several families. The shares therefore pass 100% exactly by
    /// the amount of that sharing, and the legend says so and prints both numbers.
    ///
    /// Widths are relative to the LARGEST bar in both modes, so a small family is still visible; the mode
    /// decides what the numbers say - the raw envelope signal, or each bar's share of the measurement.
    /// Drawn with OnRender for the same reason as the other charts, and renders headless.
    /// </summary>
    public class FamilyContributionChart : ChartDrawingBase, IPalettedChart
    {
        private readonly List<FamilyContributionBar> bars;
        private readonly bool asPercent;
        private readonly double measuredTotal;
        private readonly double identityThreshold;


        private const double NameGutter = 300;
        private const double ValueGutter = 104;

        /// <param name="result">The per-family envelope table plus the unattributed residual, or null.</param>
        /// <param name="topN">How many families get a bar of their own; zero or less folds nothing.</param>
        /// <param name="asPercentOfTotal">Label the values as each bar's share of the whole measured
        /// signal instead of as the raw envelope signal.</param>
        /// <param name="palette">The colours to paint in, or null for the screen palette.</param>
        public FamilyContributionChart(FamilyEnvelopeQuantResult? result,
                                       int topN = FamilyContributionPlot.DefaultTopN,
                                       bool asPercentOfTotal = false,
                                       ChartPalette? palette = null)
            : this(FamilyContributionPlot.Fold(result, topN), asPercentOfTotal,
                   FamilyContributionPlot.MeasuredTotal(result),
                   result?.IdentityThreshold ?? 0, palette)
        {
        }

        /// <summary>Draws bars that were folded elsewhere. The tests use this to draw a fold they built
        /// by hand, and the primary constructor routes through it.</summary>
        /// <param name="folded">The bars, already folded.</param>
        /// <param name="asPercentOfTotal">Label the values as shares of the measured envelope.</param>
        /// <param name="measuredEnvelope">The whole measured envelope of the run, each peptide counted
        /// ONCE (<see cref="FamilyEnvelopeQuantResult.MeasuredTotalEnvelope"/>). It is the denominator of
        /// the percent mode and the total the legend prints, and it is deliberately not derived from the
        /// bars, which overlap wherever a peptide belongs to more than one family. Zero means "not
        /// supplied", and then the percent mode falls back to the raw signal and says so rather than
        /// dividing by a number that is not the measurement.</param>
        /// <param name="identityThresholdFraction">The homology identity the attribution stood on, as a
        /// fraction (0.80 for the shipped default), or 0 for "not stated". It is PRINTED IN THE HEADING,
        /// because the de novo census can be run at a different identity and a figure that does not name
        /// its threshold can disagree with the census beside it without either looking wrong.</param>
        /// <param name="palette">The colours to paint in, or null for the screen palette.</param>
        public FamilyContributionChart(IEnumerable<FamilyContributionBar>? folded,
                                       bool asPercentOfTotal = false,
                                       double measuredEnvelope = 0,
                                       double identityThresholdFraction = 0,
                                       ChartPalette? palette = null)
        {
            this.palette = palette ?? ChartPalette.Dark;
            this.bars = (folded ?? Enumerable.Empty<FamilyContributionBar>()).Where(b => b != null).ToList();
            this.asPercent = asPercentOfTotal;
            this.measuredTotal = measuredEnvelope > 0 ? measuredEnvelope : 0;
            this.identityThreshold = identityThresholdFraction > 0 ? identityThresholdFraction : 0;

            MinHeight = 260;
            Margin = new Thickness(0);
            ToolTip = "One bar per family, largest first: blue is the signal of its identified peptides, " +
                      "green is what the de novo peptides add. Smaller families are combined into one " +
                      "\"other families\" bar, so nothing is dropped.";
        }

        /// <summary>The folded bars this chart draws, in drawing order, as the tests read them.</summary>
        public IReadOnlyList<FamilyContributionBar> Bars => bars;

        /// <summary>Whether the values are labelled as shares of the whole rather than as raw signal.</summary>
        public bool IsPercentOfTotal => asPercent;

        /// <summary>The whole measured envelope this chart divides by and prints, or zero when none was
        /// supplied. Never the sum of the bars.</summary>
        public double MeasuredEnvelope => measuredTotal;

        /// <summary>The homology identity the attribution behind these bars stood on, as a fraction, or 0
        /// when it was not stated. Printed in the heading; see the constructor.</summary>
        public double IdentityThreshold => identityThreshold;

        /// <summary>The heading exactly as it is drawn, so a test can read the claim the figure makes
        /// instead of re-deriving it from a rendered bitmap.</summary>
        public string Heading
        {
            get
            {
                string basis = PercentApplies
                    ? "Family contribution: share of the primary recovered signal"
                    : "Family contribution: identified observed + de novo calibrated estimate";

                // The threshold is part of the claim, not decoration: these bars exist because peptides
                // aligned to proteins at THIS identity, and at another identity they would be other bars.
                return identityThreshold > 0
                    ? basis + " - families reached at " +
                      identityThreshold.ToString("P0", CultureInfo.CurrentCulture) + " identity"
                    : basis;
            }
        }

        /// <summary>Whether the percent labels are really in force: the mode is on AND there is a measured
        /// envelope to divide by. With no denominator the values stay raw rather than becoming a share of
        /// something that is not the measurement.</summary>
        public bool PercentApplies => asPercent && measuredTotal > 0;

        /// <summary>The bars this chart would DRAW at the given height: the same bars, with the surplus
        /// families folded further when the height cannot hold them all. Never fewer sums than
        /// <see cref="Bars"/> - the fold preserves them exactly - and never a bar left out.</summary>
        public IReadOnlyList<FamilyContributionBar> BarsAtHeight(double height) =>
            FamilyContributionPlot.FitToHeight(bars, FamilyContributionPlot.BarCapacity(height));


        /// <summary>Text that is cut with an ellipsis rather than allowed to run into the bars. The
        /// layout system's own trimming, so no character count has to guess at the font's metrics.</summary>
        private static FormattedText Clipped(string s, Brush brush, double size, double maxWidth, bool bold = false)
        {
            FormattedText t = Text(s, brush, size, bold);
            t.MaxTextWidth = Math.Max(1, maxWidth);
            t.MaxLineCount = 1;
            t.Trimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        /// <summary>A value in the mode's own unit. The percent mode divides by the MEASURED envelope,
        /// never by the sum of the bars; with no measured envelope (or none above zero) it falls back to
        /// the raw number rather than dividing by something that is not the measurement.</summary>
        private string Value(double v, bool compact)
        {
            if (PercentApplies)
                return (v / measuredTotal).ToString(compact ? "P0" : "P1", CultureInfo.CurrentCulture);

            return v.ToString(compact ? "E1" : "E2", CultureInfo.CurrentCulture);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            ChartPalette pal = palette;
            Brush Backdrop = pal.Backdrop, Ink = pal.Ink, Muted = pal.Muted;

            DrawBackdrop(dc, Backdrop);

            var head = Text(Heading, Ink, 13, bold: true);
            head.MaxTextWidth = Math.Max(1, Math.Max(200, ActualWidth - 28));
            head.MaxLineCount = 1;
            head.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(head, new Point(14, 10));

            if (ActualWidth < 200 || ActualHeight < 140) return;

            if (bars.Count == 0)
            {
                var none = Text("Nothing to show: no family carries measured signal yet. A family needs a " +
                                "Family Cluster File open beside the computed full XIC.", Muted, 12);
                none.MaxTextWidth = Math.Max(1, ActualWidth - 28);
                dc.DrawText(none, new Point(14, 58));
                return;
            }

            double topBand = FamilyContributionPlot.TopBand;
            double legendBand = FamilyContributionPlot.LegendBand;

            double barLeft = Math.Min(NameGutter, ActualWidth * 0.42);
            double barWidth = Math.Max(1, ActualWidth - barLeft - ValueGutter);

            double area = Math.Max(1, ActualHeight - topBand - legendBand);

            // EVERY bar handed to this method is drawn. When the height cannot hold them all at a legible
            // size the surplus families are FOLDED into the "other families" bar - which preserves the
            // sums exactly - rather than being dropped off the bottom, so the figure can never claim a
            // completeness it does not have.
            List<FamilyContributionBar> drawn =
                FamilyContributionPlot.FitToHeight(bars, FamilyContributionPlot.BarCapacity(ActualHeight));

            // The geometry itself guarantees the last bar ends exactly at the legend band, whatever the
            // count: the gaps give up their room to the bars before any bar gives up its place.
            double step = (area + FamilyContributionPlot.BarGap) / drawn.Count;
            double gap = step < 1 + FamilyContributionPlot.BarGap
                ? Math.Max(0, step - 1)
                : FamilyContributionPlot.BarGap;
            double barH = Math.Min(FamilyContributionPlot.MaxBarHeight, Math.Max(1, step - gap));

            double barSum = FamilyContributionPlot.BarTotal(drawn);
            double maxTotal = drawn.Max(b => b.Total);

            double y = topBand;
            foreach (FamilyContributionBar bar in drawn)
            {
                DrawBar(dc, bar, new Rect(barLeft, y, barWidth, barH), maxTotal);
                y += step;
            }

            DrawLegend(dc, new Point(14, ActualHeight - legendBand + 6), drawn, barSum);
        }

        private void DrawBar(DrawingContext dc, FamilyContributionBar bar, Rect slot, double maxTotal)
        {
            ChartPalette pal = palette;
            Brush Ink = pal.Ink, Muted = pal.Muted, Grid = pal.Grid;
            Brush Identified = pal.Identified, DeNovo = pal.DeNovo, NotAFamily = pal.NotAFamily;
            Brush SegmentLabel = pal.SegmentLabel;

            bool isFamily = bar.Kind == FamilyContributionBarKind.Family;

            // ---- the left gutter: what the bar is, and (for a family) a truncated description
            double gutter = Math.Max(20, slot.Left - 10);
            var name = Clipped(bar.Label, isFamily ? Ink : NotAFamily, 11, gutter - 6, bold: !isFamily);
            dc.DrawText(name, new Point(4, slot.Top + (slot.Height - name.Height) / 2));

            double descLeft = 4 + name.Width + 8;
            if (bar.Description.Length > 0 && slot.Left - 10 - descLeft > 30 && slot.Height >= 11)
            {
                var desc = Clipped(bar.Description, Muted, 10, slot.Left - 10 - descLeft);
                dc.DrawText(desc, new Point(descLeft, slot.Top + (slot.Height - desc.Height) / 2));
            }

            // ---- the two segments. An all-zero input has no scale, so every bar is drawn empty rather
            // than divided by zero.
            double wId = maxTotal > 0 ? bar.Identified / maxTotal * slot.Width : 0;
            double wDn = maxTotal > 0 ? bar.DeNovo / maxTotal * slot.Width : 0;

            var segments = new (double X, double W, Brush Fill, double Value)[]
            {
                (slot.Left, wId, Identified, bar.Identified),
                (slot.Left + wId, wDn, DeNovo, bar.DeNovo),
            };

            foreach (var s in segments)
            {
                if (s.W <= 0.5) continue;
                dc.DrawRectangle(s.Fill, null, new Rect(s.X, slot.Top, s.W, slot.Height));

                // Labelled only where the segment is genuinely wide enough to hold the number; a label
                // that overflows its own segment is worse than no label.
                var label = Text(Value(s.Value, compact: true), SegmentLabel, 10, bold: true);
                if (label.Width + 10 <= s.W && label.Height + 2 <= slot.Height)
                    dc.DrawText(label, new Point(s.X + (s.W - label.Width) / 2,
                                                 slot.Top + (slot.Height - label.Height) / 2));
            }

            // The fold and the unattributed residual are outlined in the "not a family" colour, so they
            // read as different objects even when the caption is too small to be read from the back row.
            dc.DrawRectangle(null, new Pen(isFamily ? Grid : NotAFamily, isFamily ? 1.0 : 1.4), slot);

            // ---- the tail: the bar's whole value, in the mode's unit
            var tail = Text(Value(bar.Total, compact: false), isFamily ? Muted : NotAFamily, 10,
                            bold: !isFamily);
            dc.DrawText(tail, new Point(slot.Right + 8, slot.Top + (slot.Height - tail.Height) / 2));
        }

        /// <summary>
        /// Two lines: the colour keys, and underneath them the BASIS - what the numbers are shares of, and
        /// whether the bars overlap. The basis line is the honest counterpart of the old single "whole
        /// measured signal" caption, which printed the sum of the bars and so double-counted every shared
        /// peptide.
        /// </summary>
        /// <param name="drawn">The bars actually painted, after any fold to fit the height.</param>
        /// <param name="barSum">Their sum, which overlaps wherever a peptide reaches several families.</param>
        private void DrawLegend(DrawingContext dc, Point at, List<FamilyContributionBar> drawn, double barSum)
        {
            ChartPalette pal = palette;
            Brush Muted = pal.Muted, Identified = pal.Identified, DeNovo = pal.DeNovo, NotAFamily = pal.NotAFamily;

            var entries = new (Brush Fill, string Name)[]
            {
                (Identified, "identified envelope"),
                (DeNovo, "de novo reference-model estimate"),
            };

            double x = at.X;
            foreach (var e in entries)
            {
                dc.DrawRectangle(e.Fill, null, new Rect(x, at.Y + 2, 12, 12));
                var t = Text(e.Name, Muted, 11);
                dc.DrawText(t, new Point(x + 16, at.Y));
                x += 16 + t.Width + 18;
            }

            bool anyFolded = drawn.Any(b => b.Kind != FamilyContributionBarKind.Family);
            if (anyFolded)
            {
                dc.DrawRectangle(null, new Pen(NotAFamily, 1.4), new Rect(x, at.Y + 2, 12, 12));
                var t = Text("not one family: the fold and the unattributed residual", Muted, 11);
                if (x + 16 + t.Width < ActualWidth - 8) dc.DrawText(t, new Point(x + 16, at.Y));
            }

            // Overlap is a real property of the count-in-every-family policy, so it is stated rather than
            // hidden inside a denominator. The comparison is relative, so floating point noise from the
            // summing does not make a run without any shared peptide announce a sharing.
            bool overlaps = measuredTotal > 0 && barSum > measuredTotal * (1 + 1e-9);

            string basis = measuredTotal > 0
                ? "primary total, each peptide counted once: " +
                  measuredTotal.ToString("E2", CultureInfo.CurrentCulture) +
                  (overlaps
                      ? "   -   the bars sum to " + barSum.ToString("E2", CultureInfo.CurrentCulture) +
                        " because a peptide in several families counts in each, so the shares pass 100%"
                      : "   -   no peptide is shared here, so the bars sum to exactly that")
                : "no primary total was supplied with this table, so the values are raw areas and no " +
                  "share can be taken from them";

            dc.DrawText(Clipped(basis, Muted, 10, Math.Max(1, ActualWidth - at.X - 8)),
                        new Point(at.X, at.Y + 18));
        }
    }
}
