using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Homology;

/// <summary>One distinct de novo query's best identity on each side at the exploration floor.
/// Null means no protein of that side reached the floor. No known biological label is used.</summary>
public sealed record HomologyGateObservation(int Length, double? BestTargetIdentity, double? BestDecoyIdentity);

public sealed record HomologyLengthGate(int Length, int Queries, double? IdentityGate, string Status,
    int TargetWinners, int DecoyWinners, double? EmpiricalDecoyRatio, int TargetProteins, int DecoyProteins);

/// <summary>Experimental identity-level competition, per search and original query length.
/// Ties go to decoys; ratio=(decoy winners+1)/target winners * target/decoy database exposure.
/// This exploratory observed ratio is not an FDR guarantee.</summary>
public static class HomologyDecoyCalibration
{
    public const string Method = "Per-search original-length identity competition; ties to decoy; plus-one normalized decoy/target ratio; tighten only; unsupported bins abstain";

    public static IReadOnlyList<HomologyLengthGate> Fit(IEnumerable<HomologyGateObservation> observations,
        int targetProteins, int decoyProteins, double nominalGate, DecoyIdentityGateOptions options)
    {
        ArgumentNullException.ThrowIfNull(observations); ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!double.IsFinite(nominalGate) || nominalGate < 0 || nominalGate > 1) throw new ArgumentOutOfRangeException(nameof(nominalGate));
        if (targetProteins < 0 || decoyProteins < 0) throw new ArgumentOutOfRangeException(nameof(targetProteins));
        double floor = Math.Max(nominalGate, options.MinimumSearchIdentity);
        var result = new List<HomologyLengthGate>();
        foreach (var group in observations.GroupBy(o => o.Length).OrderBy(g => g.Key))
        {
            if (group.Key < 1 || group.Any(o => !Valid(o.BestTargetIdentity) || !Valid(o.BestDecoyIdentity)))
                throw new ArgumentException("Calibration observations require positive lengths and finite identity fractions.");
            var rows = group.ToArray();
            string? unavailable = rows.Length < options.MinimumLengthQueries ? "Low support"
                : targetProteins == 0 ? "No target exposure" : decoyProteins == 0 ? "No decoy exposure" : null;
            if (unavailable != null)
            {
                result.Add(new(group.Key, rows.Length, null, unavailable, 0, 0, null, targetProteins, decoyProteins));
                continue;
            }
            var gates = new List<double> { floor };
            for (int identities = 0; identities <= group.Key; identities++)
            {
                double gate = identities / (double)group.Key;
                if (gate > floor) gates.Add(gate);
            }
            HomologyLengthGate? selected = null;
            foreach (double gate in gates)
            {
                int targets = 0, decoys = 0;
                foreach (var row in rows)
                {
                    double t = row.BestTargetIdentity ?? -1, d = row.BestDecoyIdentity ?? -1;
                    if (d >= gate && d >= t) decoys++;
                    else if (t >= gate && t > d) targets++;
                }
                double ratio = targets == 0 ? double.PositiveInfinity
                    : (decoys + 1.0) / targets * (targetProteins / (double)decoyProteins);
                if (ratio <= options.MaximumDecoyRatio)
                {
                    selected = new(group.Key, rows.Length, gate, "Calibrated", targets, decoys, ratio, targetProteins, decoyProteins);
                    break;
                }
            }
            result.Add(selected ?? new(group.Key, rows.Length, null, "No supported gate", 0, 0, null, targetProteins, decoyProteins));
        }
        return result;
        static bool Valid(double? value) => value == null || double.IsFinite(value.Value) && value >= 0 && value <= 1;
    }
}
