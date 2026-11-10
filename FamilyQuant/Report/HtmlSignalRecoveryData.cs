using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace FamilyQuant.Report;

/// <summary>Snapshots the native prototype already made for report captions. Fractions come from
/// the native chart's program model; selection and Word rendering do not change these readings.</summary>
public static class HtmlSignalRecoveryData
{
    public static HtmlSignalChart? Read(FrameworkElement chart, string id, string title) => chart switch
    {
        RecoveryChart recovery => Recovery(recovery, id, title),
        ReportSignalByRunChart runs => ByRun(runs, id, title),
        ReportSignalUpliftChart uplift => Uplift(uplift, id, title),
        DatabaseGapChart gap => Gap(gap, id, title),
        _ => null,
    };

    public static bool Owns(string id) => id.Contains("recovery", StringComparison.Ordinal)
        || id.Contains("signal_by_run", StringComparison.Ordinal) || id.Contains("signal_uplift", StringComparison.Ordinal)
        || id.Contains("database_gap", StringComparison.Ordinal);

    private static HtmlSignalValue Number(string key, string label, double? value, string unit, string basis,
        double? area = null, double? reference = null, string? reason = null)
    {
        bool available = value.HasValue && double.IsFinite(value.Value);
        return new()
        {
            Key = key, Label = label, Value = available ? value : null, Unit = unit, Basis = basis,
            Text = available ? value!.Value.ToString(unit is "%" or "pp" ? "0.0" : "G5", CultureInfo.InvariantCulture)
                + (unit == "%" ? "%" : unit == "pp" ? " pp" : "") : "not available",
            Exact = available ? value!.Value.ToString("G17", CultureInfo.InvariantCulture) + (unit == "%" ? "%" : unit == "pp" ? " pp" : "") : "not available",
            Area = area.HasValue && double.IsFinite(area.Value) ? area : null,
            AreaText = area.HasValue && double.IsFinite(area.Value) ? area.Value.ToString("G17", CultureInfo.InvariantCulture) : null,
            Reference = reference.HasValue && double.IsFinite(reference.Value) ? reference : null,
            ReferenceText = reference.HasValue && double.IsFinite(reference.Value) ? reference.Value.ToString("G17", CultureInfo.InvariantCulture) : null,
            UnavailableReason = available ? null : reason ?? "This reading is not recorded in the stored chart data.",
        };
    }

    private static HtmlSignalValue Share(string key, string label, NativeRunFraction row, double fraction, double area,
        string? reason = null) => Number(key, label, row.Tic > 0 && reason == null ? fraction * 100 : null,
            "%", row.IsGrouped ? "pooled recorded TIC" : "this run's recorded TIC", reason == null ? area : null, row.Tic,
            reason ?? "The run TIC is unavailable, so no percentage can be shown.");

    private static double Maximum(IEnumerable<HtmlSignalRow> rows, double floor) => Math.Max(floor,
        rows.SelectMany(row => row.Parts.Concat(row.Total == null ? Array.Empty<HtmlSignalValue>() : new[] { row.Total }))
            .Where(value => value.Value.HasValue).Select(value => value.Value!.Value).DefaultIfEmpty(0).Max());

    private static HtmlSignalChart Recovery(RecoveryChart chart, string id, string title)
    {
        var rows = chart.Rows.Select(row => new HtmlSignalRow
        {
            Name = row.IsGrouped ? "All runs pooled" : row.Run, IsPooled = row.IsGrouped,
            Parts = new[]
            {
                Share("m0", "M+0 only", row, row.RecoveredFractionM0, row.IdentifiedAreaM0 + row.DeNovoAreaM0),
                Share("measured", chart.EnvelopeLabel, row, row.RecoveredFractionEnvelope, row.IdentifiedEnvelope + row.DeNovoEnvelope),
                Share("detector", "Detector isotope envelopes", row, row.DetectedFractionPeptideLike, row.DetectedPeptideLikeArea,
                    row.HasDetectedPeptideLike ? null : "A detector total is not recorded for this run; this is not a zero measurement."),
                Share("tic", "TIC", row, 1, row.Tic),
            },
            Details = new[]
            {
                Share("identified", "Identified measured envelope", row, row.IdentifiedFractionEnvelope, row.IdentifiedEnvelope),
                Share("denovo", "De novo measured envelope", row, row.DeNovoFractionEnvelope, row.DeNovoEnvelope),
                Share("detector-multi", "Detector envelopes, charge 2 or higher", row, row.DetectedFractionMultiplyCharged,
                    row.DetectedAreaMultiplyCharged, row.HasDetectedChargeSplit ? null : "The detector charge split is not recorded for this run."),
                Share("detector-single", "Detector envelopes, charge 1", row, row.DetectedFractionSinglyCharged,
                    row.DetectedAreaSinglyCharged, row.HasDetectedChargeSplit ? null : "The detector charge split is not recorded for this run."),
            },
        }).ToArray();
        return new() { Id = id, Kind = "recovery", Title = title, Rows = rows, Maximum = Maximum(rows, 100),
            IsotopeLabel = chart.EnvelopeLabel, Basis = "Percent of recorded TIC",
            Legend = "Each reading uses its own run TIC. All runs pooled uses pooled signal and TIC, not a mean of run percentages. Envelope parts and detector charge readings are in All exact readings; no whiskers are drawn." };
    }

    private static HtmlSignalChart ByRun(ReportSignalByRunChart chart, string id, string title)
    {
        var rows = chart.Rows.Select((row, index) => new HtmlSignalRow
        {
            Name = "Run " + (chart.RunOffset + index + 1).ToString(CultureInfo.InvariantCulture) + ": " + row.Run,
            Parts = new[]
            {
                Share("identified", "Identified envelope", row, row.IdentifiedFractionEnvelope, row.IdentifiedEnvelope),
                Share("denovo", "De novo measured envelope", row, row.DeNovoFractionReferenceCalibrated, row.DeNovoEnvelope),
            },
            Total = Share("total", "Total measured envelope", row, row.RecoveredFractionReferenceCalibrated,
                row.IdentifiedEnvelope + row.DeNovoEnvelope),
        }).ToArray();
        return new() { Id = id, Kind = "by-run", Title = title, Rows = rows, Maximum = Maximum(rows, 100),
            Basis = "Percent of each run's recorded TIC",
            Legend = "Identified and de novo envelope signal share one bar, divided by that run's TIC. Runs remain separate; these bars are not means and have no whiskers." };
    }

    private static HtmlSignalChart Uplift(ReportSignalUpliftChart chart, string id, string title)
    {
        var row = chart.Grouped;
        if (row == null) return new() { Id = id, Kind = "uplift", Title = title, Basis = "Percent of pooled recorded TIC",
            Legend = "Pooled signal data are not recorded. No value is substituted." };
        var values = new[]
        {
            Share("identified", "Identified envelope", row, row.IdentifiedFractionEnvelope, row.IdentifiedEnvelope),
            Share("combined", "Identified + de novo measured envelope", row, row.RecoveredFractionReferenceCalibrated,
                row.IdentifiedEnvelope + row.DeNovoEnvelope),
        };
        var rows = new[] { new HtmlSignalRow { Name = "All runs pooled", IsPooled = true, Parts = values } };
        return new() { Id = id, Kind = "uplift", Title = title, Rows = rows, Maximum = Maximum(rows, 1),
            Basis = "Percent of pooled recorded TIC",
            Notes = new[]
            {
                Number("added", "Added de novo signal", row.Tic > 0 ? row.DeNovoFractionReferenceCalibrated * 100 : null,
                    "pp", "percentage points of pooled recorded TIC", row.DeNovoEnvelope, row.Tic,
                    "Pooled TIC is unavailable."),
                Number("relative", "Relative increase", ReportSignalUpliftChart.RelativeIncrease(row) is double relative ? relative * 100 : null,
                    "%", "identified envelope baseline", row.DeNovoEnvelope, row.IdentifiedEnvelope,
                    "The identified envelope baseline is unavailable or zero."),
            },
            Legend = "Both bars use the same pooled TIC. Added percentage points use TIC; relative increase instead uses the identified envelope baseline. These are pooled values, not run means; no whiskers are drawn." };
    }

    private static HtmlSignalValue GapPart(string key, string label, double amount, double total)
    {
        var area = Number(key, label, amount, "area", "stored pooled signal area", amount, total);
        var share = Number(key + "-family-share", label + " share of family total", total > 0 && double.IsFinite(total) ? amount / total * 100 : null,
            "%", "this family's displayed total signal", amount, total, "This family's total signal is zero or unavailable.");
        return area with { FamilyShare = share, Text = area.Text + " area; " + (share.Value.HasValue
            ? share.Text + " of family total" : "family share not available") };
    }

    private static HtmlSignalChart Gap(DatabaseGapChart chart, string id, string title)
    {
        var rows = chart.Rows.OrderByDescending(row => row.TotalEnvelope).Select(row => new HtmlSignalRow
        {
            Name = DatabaseGapDisplayRow.Label(row), IsPooled = true,
            Parts = new[]
            {
                GapPart("identified", "Identified", row.IdentifiedEnvelope, row.TotalEnvelope),
                GapPart("denovo", "De novo in the database", row.DatabaseDeNovoEnvelope, row.TotalEnvelope),
                GapPart("world", "De novo only in the family world", row.WorldDeNovoEnvelope, row.TotalEnvelope),
            },
            Total = Number("total", "Total family signal shown", row.TotalEnvelope, "area", "stored pooled signal area"),
        }).ToArray();
        return new() { Id = id, Kind = "gap", Title = title, Rows = rows, Maximum = Maximum(rows, 0),
            Basis = "Stored pooled signal area",
            Legend = "Bars compare pooled signal area, with each part's percentage of that family's displayed total. Signal found only in the family world stays outside the database family composition; shared and unexplained global signal stays outside every family bar. These are pooled values, not run means; no whiskers are drawn." };
    }
}
