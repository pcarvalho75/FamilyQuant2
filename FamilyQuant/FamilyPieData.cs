using System;
using System.Collections.Generic;
using System.Globalization;

namespace FamilyQuant;

public sealed record FamilyPieSlice(string Key, FamilyContributionBarKind Kind, string Label, double Percent,
    double IdentifiedPercent, double DeNovoPercent, string Description = "");

/// <summary>The Results bar buckets on their recorded pooled denominator, also used by the HTML pie.</summary>
public sealed record FamilyPieData(IReadOnlyList<FamilyPieSlice> Slices, string? Unavailable)
{
    public const string Basis = "All measured peptide-feature signal, identified and de novo, counted once; all runs pooled";
    public const string Caption = "Slices show pooled signal for the displayed families, other families, contaminants, shared between families and unattributed signal. "
        + "Each percentage uses all measured peptide-feature signal, identified and de novo, counted once, so the slices sum to 100%. "
        + "All runs are pooled, with no run-to-run whiskers.";
    public const string Legend = Caption + " This pie keeps that fixed base when Normalize to changes.";

    public static FamilyPieData Build(FamilyEnvelopeQuantResult? families, int topN = FamilyContributionPlot.DefaultTopN,
        SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique, IReadOnlyDictionary<int, string>? familyLabels = null,
        IReadOnlySet<int>? contaminantFamilyIds = null)
    {
        double measured = FamilyContributionPlot.MeasuredTotal(families);
        if (!(measured > 0) || !double.IsFinite(measured))
            return new(Array.Empty<FamilyPieSlice>(), "No pooled measured family signal is available. Run Quantify to enable Pie.");
        var slices = new List<FamilyPieSlice>();
        double total = 0;
        foreach (var bar in FamilyContributionPlot.Fold(families, topN, policy, familyLabels, contaminantFamilyIds))
        {
            if (!double.IsFinite(bar.Total) || bar.Identified < 0 || bar.DeNovo < 0)
                return new(Array.Empty<FamilyPieSlice>(), "The stored family buckets contain an unavailable or negative area. Run Quantify to enable Pie.");
            double percent = 100 * bar.Total / measured;
            slices.Add(new(bar.FamilyUserId?.ToString(CultureInfo.InvariantCulture) ?? bar.Kind.ToString(), bar.Kind,
                bar.FullLabel.Length > 0 ? bar.FullLabel : bar.Label, percent, 100 * bar.Identified / measured, 100 * bar.DeNovo / measured,
                bar.Kind == FamilyContributionBarKind.Contaminants ? bar.Description : ""));
            total += percent;
        }
        if (slices.Count == 0 || Math.Abs(total - 100) > 1e-8)
            return new(Array.Empty<FamilyPieSlice>(), "The stored family buckets do not close to the measured total. Compare the recorded bars or run Quantify again.");
        return new(slices, null);
    }
}
