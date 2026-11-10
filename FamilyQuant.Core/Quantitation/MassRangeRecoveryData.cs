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
    public sealed record Bin(string Label, double PeptideEnvelope, double DetectedEnvelope, double? Recovery,
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
        int last = Array.FindLastIndex(detected, area => area > 0);
        last = Math.Max(last, Array.FindLastIndex(measured, area => area > 0));
        var bins = Enumerable.Range(0, last + 1).Select(i =>
        {
            double? recovery = detected[i] > 0 ? measured[i] / detected[i] : null;
            return new Bin(NeutralMassRanges.Label(i), measured[i], detected[i], recovery,
                measured[i].ToString("G17", CultureInfo.InvariantCulture) + " intensity x minutes of measured peptide envelope",
                detected[i].ToString("G17", CultureInfo.InvariantCulture) + " intensity x minutes of detector envelope",
                recovery.HasValue ? (100 * recovery.Value).ToString("G17", CultureInfo.InvariantCulture) + "% of detector envelope signal in this mass range"
                    : "Unavailable: no detector envelope signal in this mass range");
        }).ToArray();
        double top = Math.Max(1, Math.Ceiling(bins.Where(b => b.Recovery.HasValue).Select(b => b.Recovery!.Value).DefaultIfEmpty(0).Max() * 4) / 4);
        string ceiling = data.IsotopeCeiling.HasValue ? "M+0 to M+" + (data.IsotopeCeiling.Value - 1).ToString(CultureInfo.InvariantCulture) : "the recorded isotope ceiling";
        string legend = "Each bar divides measured peptide envelope signal by the detector's isotope-envelope signal in the same 500 Da neutral-mass range, summed across "
            + fractions.Length.ToString(CultureInfo.InvariantCulture) + (fractions.Length == 1 ? " run." : " runs.") + " Identified and de novo peptide features are counted once, with search-equivalent de novo features excluded. Peptide envelopes use "
            + ceiling + "; the independent detector uses up to M+" + (Ms1EnvelopeDetector.IsotopeCeiling - 1).ToString(CultureInfo.InvariantCulture)
            + ". This is a ratio of pooled areas, not a mean of run percentages or a share of TIC. The diagnostic is not peptide-FDR calibrated and can be below peptide signal; ratios above 100% are shown. No whiskers are drawn.";
        return new(true, bins.Length == 0 ? "No detector or peptide envelope signal was recorded." : "", bins, top, fractions.Length, legend);
    }
}
