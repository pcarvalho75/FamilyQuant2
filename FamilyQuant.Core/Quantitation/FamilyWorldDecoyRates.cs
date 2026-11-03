using System.Globalization;
using System;

namespace FamilyQuant;

public readonly record struct FamilyWorldDecoyRateEvidence(double? Rate, string Status, int? TargetPeptides, int? DecoyPeptides);

/// <summary>Presentation support for an envelope-derived rate, with its measured counts. An
/// unsupported ratio is unavailable, never clamped or interpreted as a probability above one.</summary>
public static class FamilyWorldDecoyRates
{
    public const string NoCreditedSignal = "no credited signal";
    public const string DecoyExceedsCredited = "decoy signal exceeds credited signal, no rate";
    public static double? Calculate(double credited, double decoy) => double.IsFinite(credited) && credited > 0
        && double.IsFinite(decoy) && decoy >= 0 && decoy <= credited ? decoy / credited : null;
    public static double? Recorded(double? value, double? credited = null, double? decoy = null)
    {
        if (value is not double rate || !double.IsFinite(rate) || rate < 0 || rate > 1) return null;
        if (credited is double c && (!double.IsFinite(c) || c <= 0)) return null;
        if (decoy is double d && (!double.IsFinite(d) || d < 0 || credited is double target && d > target)) return null;
        return rate;
    }
    public static string Status(double credited, double decoy, double? recorded)
    {
        if (!double.IsFinite(credited) || credited <= 0) return NoCreditedSignal;
        if (decoy > credited || recorded is > 1) return DecoyExceedsCredited;
        return Recorded(recorded, credited, decoy).HasValue ? "measured" : "Not recorded";
    }
    public static string RecordedStatus(double? recorded, string? reason) => Recorded(recorded).HasValue ? "measured"
        : recorded is > 1 ? DecoyExceedsCredited : !string.IsNullOrWhiteSpace(reason) ? reason : "Not recorded";
    public static FamilyWorldDecoyRateEvidence ForFamily(FamilyWorldFamilySummary family) =>
        new(family.DecoyRate, family.DecoyRateStatus, family.Peptides, family.DecoyPeptides);
    public static FamilyWorldDecoyRateEvidence ForComposition(FamilyWorldCompositionRow row, FamilyWorldFamilySummary? family = null)
    {
        if (row.Kind != FamilyWorldCompositionRow.UniProtKind) family = null;
        string status = row.WorldDecoyRateStatus;
        if (!row.WorldDecoyRate.HasValue && status == "Not recorded" && family != null && !family.DecoyRate.HasValue)
            status = family.DecoyRateStatus;
        return new(row.WorldDecoyRate, status, row.WorldTargetPeptides ?? family?.Peptides, row.WorldDecoyPeptides ?? family?.DecoyPeptides);
    }
    public static FamilyWorldDecoyRateEvidence ForEstimate(FamilyWorldLengthEstimate row) =>
        new(row.DecoyRate, row.DecoyRateStatus, row.TargetPeptides, row.DecoyPeptides);
    public static string Format(FamilyWorldDecoyRateEvidence evidence, CultureInfo culture, string format = "P2")
    {
        string Count(int? count) => count?.ToString("N0", culture) ?? "not recorded";
        string value = Recorded(evidence.Rate)?.ToString(format, culture) ?? RecordedStatus(evidence.Rate, evidence.Status);
        return value + " (target " + Count(evidence.TargetPeptides) + "; decoy " + Count(evidence.DecoyPeptides) + ")";
    }
}
