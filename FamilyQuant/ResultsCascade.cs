using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

/// <summary>One line of the Results cascade, and where its numbers come from.</summary>
public sealed record ResultsCascadeLine(string Label, string Value, string Source);

/// <summary>RS1 (D138): from the recorded TIC to the signal credited to families, each line from a value the program
/// already holds; nothing here is estimated.</summary>
public static class ResultsCascade
{
    public static IReadOnlyList<ResultsCascadeLine> Build(AnalyticsData data, FamilyEnvelopeQuantResult? families, SharedPeptidePolicy policy)
    {
        var lines = new List<ResultsCascadeLine>();
        var pooled = data.Grouped;
        int runs = data.Fractions.Count(fraction => !fraction.IsGrouped);
        static string P(double fraction) => fraction.ToString("P1", CultureInfo.CurrentCulture);
        static string N(double count) => count.ToString("N0", CultureInfo.CurrentCulture);
        if (pooled is { Tic: > 0 })
        {
            double measured = pooled.IdentifiedEnvelope + pooled.DeNovoEnvelope;
            lines.Add(new("TIC", P(1), "The recorded RAW total ion current of all runs, pooled. It is the 100% base of the lines below."));
            if (pooled.HasDetectedPeptideLike)
                lines.Add(new("Isotope envelopes (measured)", P(pooled.DetectedFractionPeptideLike) + " of TIC; about "
                    + N(runs > 0 ? (double)pooled.DetectedEnvelopeCount / runs : 0) + " envelopes per injection, not FDR-calibrated",
                    "Every isotope envelope the program found in the RAW files, pooled over runs and divided by the TIC. The count is the pooled number of envelopes divided by the number of runs."));
            lines.Add(new("PSM and de novo", (pooled.HasDetectedPeptideLike ? P(measured / pooled.DetectedPeptideLikeArea) + " of the isotope envelopes (measured), " : "")
                + P(pooled.RecoveredFractionEnvelope) + " of TIC (PSM " + P(pooled.IdentifiedFractionEnvelope) + " + de novo " + P(pooled.DeNovoFractionEnvelope) + ")",
                "The measured isotope envelope of the identified (PSM) and de novo peptides, pooled over runs, divided by the isotope envelopes (measured) and by the TIC."));
            if (families is { MeasuredTotalEnvelope: > 0 })
            {
                bool split = policy == SharedPeptidePolicy.SplitEqually;
                double identified = families.Families.Sum(family => split ? family.SplitIdentifiedEnvelope : family.UniqueIdentifiedEnvelope);
                double deNovo = families.Families.Sum(family => split ? family.SplitDeNovoEnvelope : family.UniqueDeNovoEnvelope);
                lines.Add(new("Credited to families", P((identified + deNovo) / families.MeasuredTotalEnvelope) + " of PSM and de novo, "
                    + P((identified + deNovo) / pooled.Tic) + " of TIC (PSM " + P(families.MeasuredIdentifiedEnvelope > 0 ? identified / families.MeasuredIdentifiedEnvelope : 0)
                    + ", de novo " + P(families.MeasuredDeNovoEnvelope > 0 ? deNovo / families.MeasuredDeNovoEnvelope : 0) + (split
                        ? "; peptides reaching several families are split equally)" : "; peptides reaching several families are not credited)"),
                    "The signal of the peptides credited to a family, as in the family chart, divided by the measured envelope of the PSM and de novo peptides counted once each, and by the TIC."));
            }
        }
        int representatives = data.Peptides.Count;
        var distinct = data.Peptides.Select(peptide => (peptide.Side, Peptidoform: PeptidoformIdentity.Canonicalize(peptide.Sequence, false), peptide.Charge)).Distinct().ToArray();
        lines.Add(new("Peptide representatives", N(representatives) + " (sum of the " + N(runs) + " run lists), about " + N(runs > 0 ? (double)representatives / runs : 0)
            + " per injection (PSM about " + N(runs > 0 ? (double)data.IdentifiedCount / runs : 0) + " + de novo about " + N(runs > 0 ? (double)data.DeNovoCount / runs : 0) + "); "
            + N(distinct.Length) + " non-redundant (" + N(distinct.Count(peptide => peptide.Side == NativeQuantFractions.SideIdentified)) + " PSM + "
            + N(distinct.Count(peptide => peptide.Side == NativeQuantFractions.SideDeNovo)) + " de novo; "
            + N(data.Peptides.Select(peptide => DeNovoReconciler.NormalizeSequenceIgnoringPtm(peptide.Sequence, false)).Where(sequence => sequence.Length > 0)
                .Distinct(StringComparer.Ordinal).Count()) + " distinct sequences)",
            "One representative per run, side, peptidoform and charge, as in the card. Non-redundant counts each side, peptidoform and charge once across runs; distinct sequences count the amino-acid sequences without modifications over both sides."));
        if (families != null)
            lines.Add(new("Families reached", N(families.Families.Count) + " (" + N(families.Families.Count(family => family.Signal(policy) > 0)) + " with credited signal)",
                "Current families reached by homology, as in the card; those with credited signal are the families the chart can rank."));
        return lines;
    }
}
