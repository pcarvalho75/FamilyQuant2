using FamilyQuant.DeNovo;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant.Report;

/// <summary>Report-only drawings sized for a 6.5-inch page. Legacy slide exports stay intact.</summary>
public abstract class ReportPrintChart : ChartDrawingBase, IPalettedChart
{
    public const double LayoutWidth = 624;
    public const double LayoutHeight = 480;

    protected void Label(DrawingContext dc, string text, double x, double y, Brush? ink = null,
        bool bold = false, double size = 16, double? width = null)
    {
        var label = Text(text, ink ?? palette.Ink, size, bold);
        label.MaxTextWidth = Math.Max(1, width ?? ActualWidth - x - 20);
        dc.DrawText(label, new Point(x, y));
    }

    protected void Ground(DrawingContext dc, string title, string guidance)
    {
        DrawBackdrop(dc, palette.Backdrop);
        Label(dc, title, 20, 16, bold: true, size: 22);
        Label(dc, guidance, 20, 53, palette.Muted);
    }

    protected static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    protected static string Percent(double value) => (value * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
}

/// <summary>The same four stored precursor-triage counts, with labels outside narrow segments.</summary>
public sealed class ReportDeNovoTriageChart : ReportPrintChart
{
    private readonly DeNovoOverlapSummary overlap;

    public ReportDeNovoTriageChart(DeNovoOverlapSummary overlap)
    {
        this.overlap = overlap ?? throw new ArgumentNullException(nameof(overlap));
        overlap.Validate();
    }

    protected override void OnRender(DrawingContext dc)
    {
        Ground(dc, "De novo precursor triage", "Filtered calls are compared by file, scan, charge and precursor m/z.");
        var rows = new[]
        {
            (overlap.NoSearchPsmInFileAndScan, "no search PSM in the same scan", palette.Identified),
            (overlap.DifferentChargeInSharedScan, "different reported charge", palette.Tic),
            (overlap.SameChargeButNoMzMatch, "same charge, different precursor m/z", palette.Mixing),
            (overlap.SamePrecursorWithinTolerance, "same scan, charge and precursor m/z", palette.DeNovo),
        };
        double x = 20, width = ActualWidth - 40;
        for (int index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            double segment = overlap.FilteredDeNovoRows > 0 ? width * row.Item1 / overlap.FilteredDeNovoRows : 0;
            if (segment > 0) dc.DrawRectangle(row.Item3, null, new Rect(x, 112, segment, 26));
            x += segment;
            double y = 158 + index * 40;
            dc.DrawRectangle(row.Item3, null, new Rect(20, y + 4, 16, 16));
            Label(dc, Count(row.Item1) + " " + row.Item2, 48, y);
        }
        Label(dc, "After all precursor gates:", 20, 330, bold: true);
        Label(dc, Count(overlap.StrictSequenceMatchesIgnoringPtm) + " strict matches after PTM removal; "
            + Count(overlap.AdditionalIleLeuEquivalentMatches) + " additional I/L matches.", 20, 361);
        Label(dc, Count(overlap.SequenceDivergencesAfterIleLeuFold)
            + " alternative sequences. Search-overlapping precursors are excluded from additive de novo quantitation.", 20, 411);
    }
}

/// <summary>Stored pooled recovery values, with the uplift annotation in its own clear band.</summary>
public sealed class ReportSignalUpliftChart : ReportPrintChart
{
    private readonly NativeRunFraction? grouped;
    public NativeRunFraction? Grouped => grouped;
    public ReportSignalUpliftChart(NativeRunFraction? grouped) => this.grouped = grouped;

    public static double? RelativeIncrease(NativeRunFraction? row) => row is { Tic: > 0 } && row.IdentifiedFractionEnvelope > 0
        ? row.DeNovoFractionReferenceCalibrated / row.IdentifiedFractionEnvelope : null;

    protected override void OnRender(DrawingContext dc)
    {
        Ground(dc, "Pooled signal recovery", "Both bars use the same pooled TIC denominator and envelope basis.");
        if (grouped == null || !(grouped.Tic > 0))
        {
            Label(dc, "Recorded pooled TIC is unavailable.", 20, 140);
            return;
        }
        var values = new[] { grouped.IdentifiedFractionEnvelope, grouped.RecoveredFractionReferenceCalibrated };
        var labels = new[] { "Observed identified envelope", "Identified + measured de novo envelope" };
        var colors = new[] { palette.Identified, palette.Quantified };
        double maximum = Math.Max(.01, Math.Max(values[0], values[1]));
        for (int index = 0; index < values.Length; index++)
        {
            double y = 125 + index * 110;
            Label(dc, labels[index], 20, y, bold: true);
            double width = (ActualWidth - 155) * values[index] / maximum;
            dc.DrawRectangle(colors[index], null, new Rect(20, y + 34, Math.Max(0, width), 28));
            Label(dc, Percent(values[index]), 34 + width, y + 36, bold: true);
        }
        double addition = grouped.DeNovoFractionReferenceCalibrated;
        Label(dc, "+" + (100 * addition).ToString("0.00", CultureInfo.InvariantCulture) + " percentage points of pooled TIC", 20, 365, bold: true);
        Label(dc, RelativeIncrease(grouped) is double relative
            ? "+" + Percent(relative) + " relative to the identified baseline"
            : "Relative uplift is unavailable: no identified baseline.", 20, 406, palette.DeNovo);
    }
}
