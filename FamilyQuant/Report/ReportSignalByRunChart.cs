using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant.Report;

/// <summary>Report-only run pages. Native fractions retain their own run TIC and acquisition order.</summary>
public sealed class ReportSignalByRunChart : ReportPrintChart
{
    public const int RunsPerPage = 3;
    public IReadOnlyList<NativeRunFraction> Rows { get; }
    public int RunOffset { get; }

    public ReportSignalByRunChart(IEnumerable<NativeRunFraction> rows, int runOffset = 0)
    {
        Rows = rows.Where(row => !row.IsGrouped).ToArray();
        RunOffset = runOffset;
    }

    protected override void OnRender(DrawingContext dc)
    {
        Ground(dc, "Assigned signal by acquisition run", "Each bar uses its own run TIC; total = identified + calibrated de novo.");
        if (Rows.Count == 0)
        {
            Label(dc, "No acquisition run fractions are stored.", 20, 130);
            return;
        }
        double maximum = Math.Max(1, Rows.Where(row => row.Tic > 0)
            .Select(row => row.RecoveredFractionReferenceCalibrated).DefaultIfEmpty(0).Max());
        for (int index = 0; index < Rows.Count; index++)
        {
            var row = Rows[index];
            double y = 112 + index * 105;
            Label(dc, "Run " + (RunOffset + index + 1).ToString(CultureInfo.InvariantCulture), 20, y, bold: true);
            if (!(row.Tic > 0))
            {
                Label(dc, "total unavailable", 365, y, bold: true);
                Label(dc, "TIC unavailable; no fraction is estimated.", 20, y + 30, palette.Muted);
                continue;
            }
            Label(dc, "total " + Percent(row.RecoveredFractionReferenceCalibrated), 365, y, bold: true);
            dc.DrawRectangle(palette.Identified, null, new Rect(20, y + 34, 14, 14));
            Label(dc, "identified " + Percent(row.IdentifiedFractionEnvelope), 44, y + 30);
            dc.DrawRectangle(palette.DeNovo, null, new Rect(290, y + 34, 14, 14));
            Label(dc, "calibrated de novo " + Percent(row.DeNovoFractionReferenceCalibrated), 314, y + 30);
            double identifiedWidth = (ActualWidth - 40) * row.IdentifiedFractionEnvelope / maximum;
            double deNovoWidth = (ActualWidth - 40) * row.DeNovoFractionReferenceCalibrated / maximum;
            if (identifiedWidth > 0) dc.DrawRectangle(palette.Identified, null, new Rect(20, y + 63, identifiedWidth, 22));
            if (deNovoWidth > 0) dc.DrawRectangle(palette.DeNovo, null, new Rect(20 + identifiedWidth, y + 63, deNovoWidth, 22));
        }
        Label(dc, "Bar lengths share a scale; values are percentages of run TIC.", 20, 440, palette.Muted);
    }
}
