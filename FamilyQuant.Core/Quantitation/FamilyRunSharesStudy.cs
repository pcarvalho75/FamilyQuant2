using System.Collections.Generic;
using System;
using System.Linq;
using FamilyQuant.DeNovo;

namespace FamilyQuant;

public static partial class FamilyCompositionStudy
{
    /// <summary>Group the existing prepared rows by normalized file name. Missing family signal is
    /// zero in a measured run; a zero assigned denominator is unavailable. Calibration and suppression
    /// have already run and are never refitted independently for each acquisition.</summary>
    public static FamilyRunSharesResult BuildRunShares(FamilyPeptideAttribution attribution,
        IEnumerable<string>? runs = null, SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique,
        IReadOnlyDictionary<string, double>? perRunTic = null)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        if (policy != SharedPeptidePolicy.FamilyUnique && policy != SharedPeptidePolicy.SplitEqually)
            throw new ArgumentException("Overlapping full counts cannot form a normalized family composition.", nameof(policy));
        var groups = attribution.Peptides.GroupBy(a => DeNovoReconciler.NormalizeRunName(a.Peptide.Run),
            StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var tic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in perRunTic ?? new Dictionary<string, double>())
        {
            string run = DeNovoReconciler.NormalizeRunName(value.Key);
            if (tic.TryGetValue(run, out double prior) && prior != value.Value)
                throw new ArgumentException("Conflicting TIC values for one normalized file name.", nameof(perRunTic));
            tic[run] = value.Value;
        }
        List<string> names = (runs ?? Array.Empty<string>()).Select(DeNovoReconciler.NormalizeRunName)
            .Concat(groups.Keys).Concat(tic.Keys).Where(n => n.Length > 0)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ThenBy(n => n, StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new FamilyRunSharesResult
        {
            Policy = policy,
            Method = "Per-file primary family signal / sum of credited family signal; measured-peptide and recorded " +
                "RAW TIC shares are separate. Family-world-only signal excluded. Arithmetic mean of available run " +
                "shares, sample SD (n-1), CV = SD / mean. Zero denominators and undefined statistics are unavailable.",
            Families = attribution.Peptides.SelectMany(a => a.FamilyUserIds).Distinct().OrderBy(id => id)
                .Select(id => new FamilyRunShareSummary { FamilyUserId = id }).ToList(),
        };
        foreach (string run in names)
        {
            var assigned = new FamilyPeptideAttribution(groups.TryGetValue(run, out var peptides) ? peptides : new());
            FamilyEnvelopeQuantResult table = FamilyEnvelopeQuant.BuildFromAttributedPeptides(assigned);
            var signals = table.Families.ToDictionary(f => f.FamilyUserId, f => f.Signal(policy));
            double assignedTotal = result.Families.Sum(f => signals.GetValueOrDefault(f.FamilyUserId));
            double measured = table.MeasuredTotalEnvelopeReferenceCalibrated;
            double? totalTic = tic.TryGetValue(run, out double rawTic) && rawTic > 0 && double.IsFinite(rawTic) ? rawTic : null;
            result.Runs.Add(new() { Run = run, AssignedSignal = assignedTotal, MeasuredSignal = measured, TotalIonCurrent = totalTic });
            foreach (FamilyRunShareSummary family in result.Families)
            {
                double signal = signals.GetValueOrDefault(family.FamilyUserId);
                family.Runs.Add(new()
                {
                    Run = run, Signal = signal,
                    ShareOfAssignedSignal = assignedTotal > 0 ? signal / assignedTotal : null,
                    ShareOfMeasuredSignal = measured > 0 ? signal / measured : null,
                    ShareOfTic = totalTic.HasValue ? signal / totalTic.Value : null,
                });
            }
        }
        var pooled = FamilyEnvelopeQuant.BuildFromAttributedPeptides(attribution).Families
            .ToDictionary(f => f.FamilyUserId, f => f.Signal(policy));
        double pooledTotal = result.Families.Sum(f => pooled.GetValueOrDefault(f.FamilyUserId));
        foreach (FamilyRunShareSummary family in result.Families)
        {
            double mean = 0, m2 = 0;
            int count = 0;
            foreach (double value in family.Runs.Where(r => r.ShareOfAssignedSignal.HasValue).Select(r => r.ShareOfAssignedSignal!.Value))
            {
                count++;
                double delta = value - mean;
                mean += delta / count;
                m2 += delta * (value - mean);
            }
            family.AvailableRunCount = count;
            family.MeanShare = count > 0 ? mean : null;
            family.StandardDeviation = count > 1 ? Math.Sqrt(Math.Max(0, m2 / (count - 1))) : null;
            family.CoefficientOfVariation = mean > 0 ? family.StandardDeviation / mean : null;
            family.PooledSignal = pooled.GetValueOrDefault(family.FamilyUserId);
            family.PooledShare = pooledTotal > 0 ? family.PooledSignal / pooledTotal : null;
            family.PooledMinusMeanShare = family.PooledShare - family.MeanShare;
        }
        return result;
    }

    /// <summary>Store only after the complete run study succeeds.</summary>
    public static void StoreRunShares(QuantManifest manifest, FamilyPeptideAttribution attribution,
        SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.FamilyRunShares = BuildRunShares(attribution, manifest.PerRunTic.Keys, policy, manifest.PerRunTic);
    }
}
