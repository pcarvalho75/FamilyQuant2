using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FamilyQuant.Homology;

public static partial class HomologySearch
{
    private static void ApplyLengthCalibration(List<(string Raw, string Normalized, string? Rejection)> queries,
        HomologyResult[] results, HomologyDatabase database, HomologyOptions exploration, double nominal,
        OperationContext? operation, PositionalIndex? index, ParallelOptions parallelOptions)
    {
        var observations = results.Where(r => !r.IsRejected).Select(r => new HomologyGateObservation(
            r.NormalizedPeptide.Length, r.ScienceAudit!.BestTargetGateIdentity, r.ScienceAudit.BestDecoyGateIdentity));
        int decoys = database.Proteins.Count(p => p.Id.StartsWith(exploration.DecoyLabel, StringComparison.Ordinal));
        var bins = HomologyDecoyCalibration.Fit(observations, database.Proteins.Count - decoys, decoys, nominal,
            exploration.Science!.DecoyGate!).ToDictionary(b => b.Length);
        // Freeze one option snapshot per distinct selected threshold, not a confidence-map clone
        // per query. Floor results can be reused; only genuinely tighter gates search again.
        var options = bins.Values.Where(b => b.IdentityGate.HasValue).Select(b => b.IdentityGate!.Value).Distinct()
            .ToDictionary(gate => gate, gate =>
            {
                var chosen = exploration.Clone(); chosen.MinIdentity = gate;
                if (index == null) chosen.Strategy = HomologySearchStrategy.BruteForce;
                return chosen;
            });
        operation?.Report("homology-calibration", "Applying supported per-length identity gates", 0, queries.Count);
        Parallel.For(0, queries.Count, parallelOptions, i =>
        {
            var query = queries[i];
            if (query.Rejection != null) return;
            HomologyLengthGate bin = bins[query.Normalized.Length];
            HomologyResult result = results[i];
            if (bin.IdentityGate.HasValue && bin.IdentityGate.Value != exploration.MinIdentity)
                result = SearchNormalized(query.Raw, query.Normalized, database, options[bin.IdentityGate.Value], operation, index);
            var audit = result.ScienceAudit! with
            {
                Method = HomologyDecoyCalibration.Method + "; " + result.ScienceAudit!.Method,
                NominalIdentityGate = nominal, AppliedIdentityGate = bin.IdentityGate, LengthCalibration = bin,
            };
            if (!bin.IdentityGate.HasValue)
            {
                audit = audit with { ProteinsBeforeTier = 0, ProteinsAfterTier = 0, ProteinsAfterTopCut = 0 };
                results[i] = new HomologyResult(query.Raw, query.Normalized, null, Array.Empty<HomologyHit>(), false, false,
                    result.CandidatesExamined, result.UsedSeedIndex, audit);
            }
            else results[i] = new HomologyResult(result.Peptide, result.NormalizedPeptide, result.Rejection, result.Hits,
                result.DecoysSuppressed, result.TargetsSuppressedByDecoyCompetition, result.CandidatesExamined, result.UsedSeedIndex, audit);
        });
        operation?.Report("homology-calibration", "Per-length identity gates applied", queries.Count, queries.Count);
    }
}
