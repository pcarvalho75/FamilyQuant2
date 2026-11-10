using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

/// <summary>Measured credited peptide envelopes over detector envelopes in the same neutral-mass range.</summary>
public sealed record MassRangeRecoveryData(bool Available, string Message, IReadOnlyList<MassRangeRecoveryData.Bin> Bins,
    double AxisMaximum, int RunCount, string Legend)
{
    public const string MissingMessage = "Recovery by mass range needs Quantify with this version: run Quantify again.";
    public double TickStep { get; init; } = .5;
    public sealed record Bin(int Index, string Label, double PeptideEnvelope, double DetectedEnvelope, double? Recovery,
        string PeptideText, string DetectorText, string RecoveryText);

    public static MassRangeRecoveryData Build(AnalyticsData data)
    {
        var fractions = data.Fractions.Where(f => !f.IsGrouped).ToArray();
        var runs = fractions.Select(f => f.Run).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fractions.Length == 0 || fractions.Any(f => !NeutralMassRanges.IsRecorded(f.DetectedAreaByNeutralMass, f.NeutralMassBinWidth))
            || data.Peptides.Any(p => !runs.Contains(p.Run)))
            return new(false, MissingMessage, Array.Empty<Bin>(), 1, fractions.Length, MissingMessage);

        var detected = new double[NeutralMassRanges.Count];
        foreach (var fraction in fractions)
            for (int i = 0; i < detected.Length; i++) detected[i] += fraction.DetectedAreaByNeutralMass![i];
        var peptides = data.Peptides.Select(p => new NativePeptideQuant
        {
            Run = p.Run, Side = p.Side, Sequence = p.Sequence, Charge = p.Charge, PrecursorMz = p.PrecursorMz,
            EnvelopeSum = p.Envelope,
        }).ToArray();
        var identified = peptides.Where(p => p.Side == NativeQuantFractions.SideIdentified)
            .GroupBy(NativeQuantFractions.CrossSideKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var measured = new double[NeutralMassRanges.Count];
        foreach (var peptide in peptides)
        {
            if (!runs.Contains(peptide.Run) || !(peptide.EnvelopeSum > 0)) continue;
            if (peptide.Side == NativeQuantFractions.SideDeNovo
                && identified.TryGetValue(NativeQuantFractions.CrossSideKey(peptide), out var candidates)
                && candidates.Any(p => NativeQuantFractions.IsCrossSideEquivalent(p, peptide))) continue;
            int index = NeutralMassRanges.Index(peptide.MonoisotopicMass);
            if (index >= 0) measured[index] += peptide.EnvelopeSum;
        }
        int first = Array.FindIndex(detected, area => area > 0);
        int last = Array.FindLastIndex(detected, area => area > 0);
        var bins = Enumerable.Range(Math.Max(0, first), first >= 0 ? last - first + 1 : 0).Select(i =>
        {
            double? recovery = detected[i] > 0 ? measured[i] / detected[i] : null;
            return new Bin(i, NeutralMassRanges.Label(i), measured[i], detected[i], recovery,
                measured[i].ToString("G17", CultureInfo.InvariantCulture) + " intensity x minutes of measured peptide envelope",
                detected[i].ToString("G17", CultureInfo.InvariantCulture) + " intensity x minutes of detector envelope",
                recovery.HasValue ? (100 * recovery.Value).ToString("G17", CultureInfo.InvariantCulture) + "% of detector envelope signal in this mass range"
                    : "Unavailable: no detector envelope signal in this mass range");
        }).ToArray();
        double maximum = Math.Max(1, bins.Where(b => b.Recovery.HasValue).Select(b => b.Recovery!.Value).DefaultIfEmpty(0).Max());
        double step = .5;
        if (maximum > 3)
        {
            double rough = maximum * 100 / 5;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double normalized = rough / magnitude;
            step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude / 100;
        }
        double top = Math.Ceiling(maximum / step) * step;
        string ceiling = data.IsotopeCeiling.HasValue ? "M+0 to M+" + (data.IsotopeCeiling.Value - 1).ToString(CultureInfo.InvariantCulture) : "the recorded isotope ceiling";
        string legend = "Each bar divides measured peptide envelope signal by the detector's isotope-envelope signal in the same 500 Da neutral-mass range, summed across "
            + fractions.Length.ToString(CultureInfo.InvariantCulture) + (fractions.Length == 1 ? " run." : " runs.") + " Identified and de novo peptide features are counted once, with search-equivalent de novo features excluded. Peptide envelopes use "
            + ceiling + "; the independent detector uses up to M+" + (Ms1EnvelopeDetector.IsotopeCeiling - 1).ToString(CultureInfo.InvariantCulture)
            + ". The view spans the first through last range with detector signal; blank ranges have no detector signal. This is a ratio of pooled areas, not a mean of run percentages or a share of TIC. The diagnostic is not peptide-FDR calibrated and can be below peptide signal; ratios above 100% are shown. No whiskers are drawn.";
        return new(true, bins.Length == 0 ? "No detector envelope signal was recorded." : "", bins, top, fractions.Length, legend) { TickStep = step };
    }
}
