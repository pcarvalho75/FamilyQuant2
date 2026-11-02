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
    /// The headline recovery picture, separating the primary estimate from two sensitivity readings.
    ///
    /// TOP - THREE DISTINCT READINGS, NEVER A RANGE OR PHYSICAL PARTITION. Each run shows the M+0-only
    /// sensitivity reading, the identified-reference mass-calibrated primary estimate, and the unadjusted
    /// six-window diagnostic. Their ordering is data-dependent; none is labelled a confidence bound.
    ///
    /// BOTTOM - THREE INDEPENDENT LEVELS AGAINST EACH RUN'S OWN TIC: raw TIC, the resolved isotope-series
    /// detector diagnostic, and the primary recovery estimate. The detector has no peptide-level FDR and
    /// therefore is not subtracted from TIC to manufacture biological residual classes. Normalising each
    /// run to its own TIC makes technical replicates comparable without letting the pooled row dominate.
    ///
    /// Drawn with OnRender for the same reason as the other charts, and renders headless.
    /// </summary>
    public class RecoveryChart : ChartDrawingBase, IPalettedChart
    {
        private readonly List<NativeRunFraction> rows;


        // The gutters are FLOORS and CEILINGS, not fixed widths. A fixed 150 DIP left gutter cut every
        // acquisition name in half at projector size, which left the three runs of a triplicate looking
        // identical. The left gutter is now measured from the labels that will actually be drawn, and only
        // when the measurement exceeds the ceiling is the name itself shortened.
        private const double MinNameGutter = 150;
        private const double MaxNameGutterShare = 0.30;
        private const double MinValueGutter = 96;
        private const double MaxValueGutterShare = 0.22;

        // Breathing room between a label and the bar it names, and between a bar and its trailing number.
        private const double LabelPad = 14;
        private const double ValuePad = 16;

        // Word boundaries in an acquisition name. Ordinal, and no knowledge of any naming scheme beyond
        // "names are made of separated parts".
        private const string LabelSeparators = "_-. ";
        private static readonly char[] LabelSeparatorChars = LabelSeparators.ToCharArray();

        // Set at the start of every render: the labels as they will be DRAWN (which may be shortened), and
        // the two gutter widths they and the trailing numbers need. Render is single threaded.
        private string[] labels = Array.Empty<string>();
        private double nameGutter = MinNameGutter;
        private double valueGutter = MinValueGutter;

        /// <param name="fractions">The per-run fractions (Q3), each already carrying its Q6 detected
        /// estimate when one was computed; the grouped row is appended here, so the caller passes the
        /// non-grouped rows and need not build the aggregate.</param>
        /// <param name="grouped">The pooled row, or null to have it built from the fractions.</param>
        /// <param name="palette">The colours to paint in, or null for the screen palette.</param>
        public RecoveryChart(IEnumerable<NativeRunFraction>? fractions, NativeRunFraction? grouped = null,
                             ChartPalette? palette = null)
        {
            this.palette = palette ?? ChartPalette.Dark;

            var given = (fractions ?? Enumerable.Empty<NativeRunFraction>()).Where(f => !f.IsGrouped).ToList();
            this.rows = new List<NativeRunFraction>(given);
            NativeRunFraction? agg = grouped ?? (given.Count > 0 ? NativeQuantFractions.Grouped(given) : null);
            if (agg != null) this.rows.Add(agg);

            MinHeight = 300;
            Margin = new Thickness(0);
            ToolTip = "Top: recovered ion current read three ways, M+0, the PRIMARY estimate and an " +
                      "unadjusted diagnostic. Bottom: TIC, isotope-series detector and primary estimate; " +
                      "the detector is not peptide-FDR calibrated and does not partition the TIC into " +
                      "biological classes.";
        }


        private static string Pct(double frac) =>
            (frac * 100.0).ToString("F1", CultureInfo.CurrentCulture) + "%";

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            ChartPalette pal = palette;
            Brush Backdrop = pal.Backdrop, Ink = pal.Ink, Muted = pal.Muted;

            DrawBackdrop(dc, Backdrop);

            var head = Text("Recovery of the total ion current", Ink, 13, bold: true);
            dc.DrawText(head, new Point(14, 10));

            if (rows.Count == 0 || ActualWidth < 320 || ActualHeight < 200)
            {
                if (rows.Count == 0 && ActualWidth >= 200)
                {
                    DrawEmpty(dc, "Nothing to show: compute the full XIC so the runs have a TIC to divide by.", Muted, new Point(14, 60));
                }
                return;
            }

            MeasureGutters();

            double barLeft = nameGutter;
            double barWidth = Math.Max(1, ActualWidth - nameGutter - valueGutter);

            // Deep enough that the panel caption, drawn 16 DIP above the first bar, clears the bold
            // heading above it. At 36 the two collided, and the four-way caption is longer than the
            // caption that collision was first noticed under.
            double topBand = 46;
            // Two legend rows: one for the shares of the top bar, one for the levels of the bottom panel.
            // A single row could not hold both without dropping entries, and a legend that drops entries
            // is a figure that cannot be read without its caption. A THIRD line is added when the detected
            // level is split by charge, carrying what the two parts mean in words - the split is a claim
            // about evidence, and a colour key alone would not state it.
            double legendBand = AnyChargeSplit ? 66 : 48;
            double usable = ActualHeight - topBand - legendBand;

            // The two panels split the height; the top (fraction) panel gets the larger share.
            double gap = 26;
            double topPanelH = (usable - gap) * 0.56;
            double bottomPanelH = (usable - gap) * 0.44;

            DrawFractionPanel(dc, new Rect(barLeft, topBand, barWidth, topPanelH));
            DrawAbsolutePanel(dc, new Rect(barLeft, topBand + topPanelH + gap, barWidth, bottomPanelH));

            DrawLegend(dc, new Point(14, ActualHeight - legendBand + 6));
        }

        /// <summary>Whether any row carries the detected level's charge split, so the bottom panel draws
        /// the detected bar as two labelled parts and the legend gains its third line. A store written
        /// before the split existed answers false, and the figure is then exactly what it always was.</summary>
        private bool AnyChargeSplit => rows.Any(r => r.HasDetectedChargeSplit);

        /// <summary>The number printed beside a bar of the TOP panel: the detected estimate when the run
        /// has one, else what was quantified. Shared with the gutter measurement so the space reserved is
        /// the space the text will need.</summary>
        private static string TopTailText(NativeRunFraction f) =>
            Pct(f.RecoveredFractionM0) + " / " + Pct(f.RecoveredFractionReferenceCalibrated) +
            " / " + Pct(f.RecoveredFractionEnvelope);

        /// <summary>The number printed beside a row of the BOTTOM panel. These are FRACTIONS of the run's
        /// own TIC and always were, which is why normalising the bars to that same TIC leaves them
        /// untouched.</summary>
        private static string BottomTailText(NativeRunFraction f, bool anyDetected) => anyDetected
            ? Pct(f.DetectedFractionPeptideLike) + " / " + Pct(f.RecoveredFractionReferenceCalibrated)
            : Pct(f.RecoveredFractionReferenceCalibrated);

        /// <summary>
        /// Works out what the labels say and how much room the two gutters need, before anything is drawn.
        ///
        /// The left gutter used to be a fixed 150 DIP, and a full acquisition name is wider than that at
        /// talk size, so every row was cut mid-word and a triplicate came out as three identical stubs. The
        /// order here is: try the WHOLE name; if the widest one will not fit inside the ceiling, drop the
        /// prefix that every run in the figure shares, which costs nothing because it is by definition the
        /// part that tells no two runs apart; whatever remains is fitted at draw time by losing the middle
        /// rather than the tail. None of the three steps knows anything about this dataset's naming.
        /// </summary>
        private void MeasureGutters()
        {
            labels = rows.Select(r => r.Run ?? string.Empty).ToArray();

            double capName = Math.Max(MinNameGutter, ActualWidth * MaxNameGutterShare);
            double need = Widest(labels) + LabelPad;
            if (need > capName)
            {
                labels = WithoutSharedPrefix(rows, labels);
                need = Widest(labels) + LabelPad;
            }
            nameGutter = Math.Min(capName, Math.Max(MinNameGutter, need));

            // The right gutter holds the trailing numbers of BOTH panels. Normalising the bottom panel
            // parks its numbers at the full-width end of every row instead of at a ragged edge, so the room
            // they need has to be reserved rather than borrowed from whatever the longest bar left over.
            bool anyDetected = rows.Any(r => r.HasDetectedPeptideLike);
            double needValue = Math.Max(Widest(rows.Select(TopTailText)),
                                        Widest(rows.Select(r => BottomTailText(r, anyDetected)))) + ValuePad;
            valueGutter = Math.Min(Math.Max(MinValueGutter, ActualWidth * MaxValueGutterShare),
                                   Math.Max(MinValueGutter, needValue));
        }

        /// <summary>The widest of a set of strings, measured bold so the reserved room also holds the
        /// grouped row, which is the one drawn bold.</summary>
        private double Widest(IEnumerable<string> texts) =>
            texts.Select(s => Text(s, palette.Ink, 11, bold: true).Width).DefaultIfEmpty(0).Max();

        /// <summary>
        /// The run labels with the leading part that EVERY non-grouped run shares removed. A shared prefix
        /// carries no information inside one figure, so it is the first thing a narrow gutter should spend.
        /// The cut is pulled back to a separator, so a word is never sliced in half, and is refused
        /// outright when it would be trivially short or would leave any row without a label. The grouped
        /// row keeps its own label untouched: it is not an acquisition name and shares nothing.
        /// </summary>
        private static string[] WithoutSharedPrefix(IReadOnlyList<NativeRunFraction> rows, string[] names)
        {
            var shortened = (string[])names.Clone();
            var runs = new List<int>();
            for (int i = 0; i < rows.Count && i < names.Length; i++)
                if (!rows[i].IsGrouped) runs.Add(i);
            if (runs.Count < 2) return shortened;

            var acquisitions = runs.Select(i => names[i]).ToList();
            int cut = SharedPrefixCut(acquisitions);
            if (cut <= 0) return shortened;

            foreach (int i in runs) shortened[i] = names[i].Substring(cut);
            return shortened;
        }

        /// <summary>How many leading characters every name shares, pulled back to just past the last
        /// separator inside that shared run so the surviving label starts on a word boundary. Ordinal:
        /// these are file names, not prose. Zero means "do not shorten".</summary>
        private static int SharedPrefixCut(IReadOnlyList<string> names)
        {
            if (names.Count < 2) return 0;

            int limit = names.Min(s => s.Length);
            int shared = 0;
            while (shared < limit && names.All(s => s[shared] == names[0][shared])) shared++;

            int cut = 0;
            for (int i = shared - 1; i >= 0; i--)
                if (LabelSeparators.IndexOf(names[0][i]) >= 0) { cut = i + 1; break; }

            // Too little to gain, or a row would be left with nothing to be called: keep the full names and
            // let the middle-eliding fit deal with them. An unlabelled row is worse than a long label.
            if (cut < 4) return 0;
            if (names.Any(s => s.Length - cut < 1)) return 0;
            return cut;
        }

        /// <summary>
        /// A label sized to the gutter. When the name is too long it loses its MIDDLE, never its tail: the
        /// tail is what tells one acquisition from the next (_01, _02, _03), so WPF's own trailing
        /// ellipsis would throw away precisely the part the reader needs. The head is kept only as far as
        /// the room allows, and is the first thing given up.
        /// </summary>
        private FormattedText FitLabel(string s, Brush brush, bool bold, double maxWidth)
        {
            FormattedText full = Text(s, brush, 11, bold);
            if (full.Width <= maxWidth || s.Length < 6) return full;

            string tail = DistinguishingTail(s);
            for (int head = s.Length - tail.Length; head > 0; head--)
            {
                FormattedText t = Text(s.Substring(0, head) + "…" + tail, brush, 11, bold);
                if (t.Width <= maxWidth) return t;
            }
            // Nothing but the tail fits. Drawn anyway: the tail IS the run's identity, and a row with no
            // label at all could not be read at any size.
            return Text("…" + tail, brush, 11, bold);
        }

        /// <summary>The end of a name that has to survive: its last separated segment when that segment is
        /// short enough to be a suffix rather than the whole name, else the last few characters. The
        /// separator is kept, so the elision reads as "..._01" and not as a mangled word.</summary>
        private static string DistinguishingTail(string s)
        {
            int i = s.LastIndexOfAny(LabelSeparatorChars);
            if (i >= 0 && i < s.Length - 1 && s.Length - i - 1 <= 12) return s.Substring(i);
            return s.Substring(Math.Max(0, s.Length - 8));
        }

        private void DrawFractionPanel(DrawingContext dc, Rect area)
        {
            Brush Muted = palette.Muted;

            var caption = Text(
                    "Recovery readings: M+0 sensitivity / PRIMARY mass-calibrated estimate / unadjusted six-window diagnostic",
                Muted, 11, bold: true);
            dc.DrawText(caption, new Point(area.Left - nameGutter + 4, area.Top - 16));

            double n = rows.Count;
            double barH = Math.Min(38, Math.Max(10, (area.Height - (n - 1) * 8) / n));
            double step = barH + 8;
            double y = area.Top;

            for (int i = 0; i < rows.Count; i++)
            {
                DrawStackedBar(dc, rows[i], labels[i], new Rect(area.Left, y, area.Width, barH));
                y += step;
            }
        }

        private void DrawStackedBar(DrawingContext dc, NativeRunFraction f, string label, Rect bar)
        {
            ChartPalette pal = palette;
            Brush Ink = pal.Ink, Muted = pal.Muted, Grid = pal.Grid;
            Brush Primary = pal.Quantified, M0 = pal.NotQuantified, Diagnostic = pal.Remainder;

            var name = FitLabel(label, f.IsGrouped ? Ink : Muted, f.IsGrouped, nameGutter - LabelPad);
            dc.DrawText(name, new Point(Math.Max(4, bar.Left - 10 - name.Width),
                                        bar.Top + (bar.Height - name.Height) / 2));

            double gap = 2;
            double h = Math.Max(1, (bar.Height - 2 * gap) / 3.0);
            var readings = new (double Share, Brush Fill)[]
            {
                (f.RecoveredFractionM0, M0),
                (f.RecoveredFractionReferenceCalibrated, Primary),
                (f.RecoveredFractionEnvelope, Diagnostic),
            };
            for (int i = 0; i < readings.Length; i++)
            {
                double width = Math.Max(0, Math.Min(1.0, readings[i].Share)) * bar.Width;
                dc.DrawRectangle(readings[i].Fill, null,
                    new Rect(bar.Left, bar.Top + i * (h + gap), width, h));
            }

            dc.DrawRectangle(null, new Pen(Grid, 1.0), bar);

            // The tail reports the detected estimate when there is one (the middle level), else what was
            // quantified, so the number beside the bar names its most informative level.
            var tail = Text(TopTailText(f), f.IsGrouped ? Ink : Muted, 10, bold: f.IsGrouped);
            dc.DrawText(tail, new Point(bar.Right + 8, bar.Top + (bar.Height - tail.Height) / 2));
        }

        private void DrawAbsolutePanel(DrawingContext dc, Rect area)
        {
            ChartPalette pal = palette;
            Brush Ink = pal.Ink, Muted = pal.Muted;
            Brush Identified = pal.Identified, DeNovo = pal.DeNovo, Tic = pal.Tic;
            Brush Detected = pal.Detected;
            Brush DetectedMulti = pal.DetectedMultiplyCharged, DetectedSingle = pal.DetectedSinglyCharged;

            bool anyDetected = rows.Any(r => r.HasDetectedPeptideLike);
            bool split = AnyChargeSplit;
            var caption = Text(anyDetected
                    ? (split
                        ? "MS1 TIC, resolved isotope-series diagnostic (z >= 2 then z = 1), and PRIMARY recovery estimate"
                        : "MS1 TIC, resolved isotope-series diagnostic, and PRIMARY recovery estimate")
                    : "MS1 TIC vs the identified-reference recovery model estimate",
                Muted, 11, bold: true);
            dc.DrawText(caption, new Point(area.Left - nameGutter + 4, area.Top - 16));

            double n = rows.Count;
            double groupH = Math.Min(52, Math.Max(15, (area.Height - (n - 1) * 8) / n));
            // Three stacked sub-bars when a detection exists (TIC, detected, assigned), else two.
            int subBars = anyDetected ? 3 : 2;
            double subH = (groupH - (subBars - 1) * 2) / subBars;
            double step = groupH + 8;
            double y = area.Top;

            for (int i = 0; i < rows.Count; i++)
            {
                NativeRunFraction f = rows[i];

                var name = FitLabel(labels[i], f.IsGrouped ? Ink : Muted, f.IsGrouped,
                                    nameGutter - LabelPad);
                dc.DrawText(name, new Point(Math.Max(4, area.Left - 10 - name.Width),
                                            y + (groupH - name.Height) / 2));

                double found = f.IdentifiedEnvelope + f.DeNovoEnvelopeReferenceCalibrated;

                // EVERY ROW AGAINST ITS OWN TOTAL ION CURRENT. Scaled to the largest row instead, the
                // pooled row (three runs of current) drew about three times any single run and squeezed
                // the per-run bars to a third of the panel, so the picture said "the grouped row is
                // bigger", which is trivially true and not the claim being made. Normalised, each MS1 bar
                // is full width and the levels under it are that row's own fractions, which is what makes
                // the rows comparable and the ladder TIC >= detected >= assigned visible.
                //
                // The denominator takes the largest of the three levels rather than the TIC alone only so
                // that a detection which somehow exceeded the TIC would stay INSIDE the panel and show
                // itself as a short TIC bar, instead of running off the figure. In every well formed row
                // that maximum IS the TIC, so the MS1 bar is full width.
                double denom = Math.Max(f.Tic, Math.Max(f.DetectedPeptideLikeArea, found));
                if (denom <= 0) denom = 1;

                // Row 1: the raw TIC, the row's own 100%.
                double wTic = f.Tic / denom * area.Width;
                dc.DrawRectangle(Tic, null, new Rect(area.Left, y, Math.Max(0, wTic), subH));

                double rowY = y + subH + 2;

                // Row 2 (only with a detection): the independent resolved isotope-series diagnostic.
                // Drawn as two parts when the charge split is present. z >= 2 is more specific than z == 1,
                // but neither is peptide-FDR calibrated and this row is not a biological partition of TIC.
                if (anyDetected)
                {
                    if (split && f.HasDetectedChargeSplit)
                    {
                        double wMulti = f.DetectedAreaMultiplyCharged / denom * area.Width;
                        double wSingle = f.DetectedAreaSinglyCharged / denom * area.Width;
                        dc.DrawRectangle(DetectedMulti, null,
                            new Rect(area.Left, rowY, Math.Max(0, wMulti), subH));
                        dc.DrawRectangle(DetectedSingle, null,
                            new Rect(area.Left + Math.Max(0, wMulti), rowY, Math.Max(0, wSingle), subH));
                    }
                    else
                    {
                        double wDet = f.DetectedPeptideLikeArea / denom * area.Width;
                        dc.DrawRectangle(Detected, null, new Rect(area.Left, rowY, Math.Max(0, wDet), subH));
                    }
                    rowY += subH + 2;
                }

                // Last row: the assigned envelope as identified then de novo, so its make-up shows.
                double wFound = found / denom * area.Width;
                double wFoundId = f.IdentifiedEnvelope / denom * area.Width;
                double wFoundDn = f.DeNovoEnvelopeReferenceCalibrated / denom * area.Width;
                dc.DrawRectangle(Identified, null, new Rect(area.Left, rowY, Math.Max(0, wFoundId), subH));
                dc.DrawRectangle(DeNovo, null, new Rect(area.Left + wFoundId, rowY, Math.Max(0, wFoundDn), subH));

                // The number sits past the longest level of the row, which normalising makes the same
                // place on every row: the right-hand end of the panel. The room for it is reserved in the
                // value gutter rather than borrowed from the bars.
                double wLabel = anyDetected
                    ? Math.Max(wTic, f.DetectedPeptideLikeArea / denom * area.Width)
                    : Math.Max(wTic, wFound);
                var pct = Text(BottomTailText(f, anyDetected), f.IsGrouped ? Ink : Muted, 10,
                               bold: f.IsGrouped);
                dc.DrawText(pct, new Point(area.Left + wLabel + 8, y + (groupH - pct.Height) / 2));

                y += step;
            }
        }

        private void DrawLegend(DrawingContext dc, Point at)
        {
            ChartPalette pal = palette;
            Brush Muted = pal.Muted;
            Brush Quantified = pal.Quantified, NotQuantified = pal.NotQuantified;
            Brush Identified = pal.Identified, DeNovo = pal.DeNovo, Tic = pal.Tic, Detected = pal.Detected;
            Brush Diagnostic = pal.Remainder;
            Brush DetectedMulti = pal.DetectedMultiplyCharged, DetectedSingle = pal.DetectedSinglyCharged;

            bool anyDetected = rows.Any(r => r.HasDetectedPeptideLike);
            bool split = AnyChargeSplit;

            // Row one names the shares of the TOP bar, row two the levels of the BOTTOM panel. Two rows
            // because one cannot hold both, and an entry silently dropped for want of width would leave a
            // block on the figure with no name.
            var shareRow = new (Brush Fill, string Name)[]
            {
                (NotQuantified, "M+0 sensitivity (not a formal lower bound)"),
                (Quantified, "identified-reference model estimate"),
                (Diagnostic, "unadjusted six-window diagnostic"),
            };

            // With the charge split the detected level takes TWO keys instead of one, kept short here
            // because the line underneath states what they mean in full. Without it, the single key the
            // figure has always carried.
            var levelRow = anyDetected
                ? (split
                    ? new (Brush Fill, string Name)[]
                    {
                        (Tic, "MS1 total ion current"),
                        (DetectedMulti, "resolved series z >= 2 (diagnostic)"),
                        (DetectedSingle, "resolved series z = 1 (diagnostic)"),
                        (Identified, "identified envelope"),
                        (DeNovo, "de novo reference-model estimate"),
                    }
                    : new (Brush Fill, string Name)[]
                    {
                        (Tic, "MS1 total ion current"),
                        (Detected, "resolved isotope-series diagnostic"),
                        (Identified, "identified envelope"),
                        (DeNovo, "de novo reference-model estimate"),
                    })
                : new (Brush Fill, string Name)[]
                {
                    (Tic, "MS1 total ion current"),
                    (Identified, "identified envelope"),
                    (DeNovo, "de novo reference-model estimate"),
                };

            DrawLegendRow(dc, at, shareRow);
            DrawLegendRow(dc, new Point(at.X, at.Y + 18), levelRow);

            if (split) DrawChargeSplitLine(dc, new Point(at.X, at.Y + 36));
        }

        /// <summary>
        /// What the two detected parts MEAN, in words, and what they measure on the pooled row. A colour
        /// key alone would say that the level has two parts without saying that they are two grades of
        /// evidence, which is the whole point of the split - and it would leave the reader free to take the
        /// singly charged part for "not a peptide", which it is not.
        /// </summary>
        private void DrawChargeSplitLine(DrawingContext dc, Point at)
        {
            NativeRunFraction? reference = rows.LastOrDefault(r => r.HasDetectedChargeSplit);
            string numbers = reference == null || !(reference.Tic > 0)
                ? string.Empty
                : "  " + Pct(reference.DetectedFractionMultiplyCharged) + " and " +
                  Pct(reference.DetectedFractionSinglyCharged) + " of the TIC" +
                  (reference.IsGrouped ? " over the runs together" : " on " + reference.Run) + ".";

            var line = Text(
                "z >= 2: " + Ms1EnvelopeDetector.StrongEvidenceLabel + ". z = 1: " +
                Ms1EnvelopeDetector.WeakEvidenceLabel + ", not a claim it is not a peptide." + numbers,
                palette.Muted, 10);

            // Trimmed rather than allowed to run off the figure: an ellipsis says there is more, a
            // half-drawn word says nothing.
            line.MaxTextWidth = Math.Max(1, ActualWidth - at.X - 8);
            line.MaxLineCount = 1;
            line.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(line, at);
        }

        private void DrawLegendRow(DrawingContext dc, Point at, (Brush Fill, string Name)[] entries)
        {
            double x = at.X;
            foreach (var e in entries)
            {
                dc.DrawRectangle(e.Fill, null, new Rect(x, at.Y + 2, 12, 12));
                var t = Text(e.Name, palette.Muted, 11);
                dc.DrawText(t, new Point(x + 16, at.Y));
                x += 16 + t.Width + 18;
                if (x > ActualWidth - 60) break;
            }
        }
    }
}
