using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.DeNovo;

public sealed class PatternLabConflictResult
{
    public List<QuantRow> Rows { get; init; } = new();
    public Dictionary<string, int> ExcludedByRun { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int ExcludedAnchors => ExcludedByRun.Values.Sum();
}

public static partial class SpectrumComparisonTable
{
    public const string PatternLabConflictRule = "PatternLab identification wins the same scan and precursor; de novo calls with a different sequence receive no credit (PTMs removed, I = L).";
    public const string KeepConflictingCallsRule = "Same-scan, same-precursor calls with different sequences keep both quantities; ordinary peptide collapse and cross-side suppression still apply.";

    /// <summary>Applies the Compute choice to measured anchors before peptide collapse. Input rows and
    /// extraction caches remain unchanged. The same claims, ranking and precursor clusters build the table.</summary>
    public static PatternLabConflictResult ApplyPatternLabConflictRule(IEnumerable<PeaksDeNovoRow> sourceRows,
        IEnumerable<PatternLabPsm> psms, IEnumerable<QuantRow> measuredRows, bool enabled,
        OperationContext? operation = null)
    {
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(psms);
        ArgumentNullException.ThrowIfNull(measuredRows);
        var rows = measuredRows.ToList();
        if (!enabled || !rows.Any(r => r.Side == NativeQuantFractions.SideDeNovo))
            return new PatternLabConflictResult { Rows = rows };
        var sources = sourceRows.ToList();
        operation?.Report("scan-conflict-rule", "Applying the PatternLab same-precursor rule", 0, rows.Count);
        var claims = CreateClaims(sources.Where(r => !IsDeepNovo(r)), sources.Where(IsDeepNovo), psms, rows, null);
        var excluded = new HashSet<QuantRow>();
        foreach (var cluster in BuildClusters(claims))
        {
            operation?.ThrowIfCancellationRequested();
            if (cluster.Charge <= 0 || !(cluster.OpeningMz > 0)) continue;
            var identification = Ranked(cluster.Claims.Where(c => c.Kind == ClaimKind.PatternLab)).FirstOrDefault();
            if (identification == null || string.IsNullOrWhiteSpace(identification.Sequence)
                || identification.Charge <= 0 || !(identification.Mz > 0)) continue;
            foreach (var claim in cluster.Claims)
            {
                var quant = claim.Quant;
                if (quant?.Side == NativeQuantFractions.SideDeNovo && quant.Charge > 0 && quant.PrecursorMz > 0
                    && !DeNovoReconciler.SequencesEquivalent(quant.Sequence, identification.Sequence))
                    excluded.Add(quant);
            }
        }
        var result = new PatternLabConflictResult
        {
            Rows = rows.Where(r => !excluded.Contains(r)).ToList(),
            ExcludedByRun = excluded.GroupBy(r => DeNovoReconciler.NormalizeRunName(r.Run), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
        };
        operation?.Report("scan-conflict-rule", "Excluded " + result.ExcludedAnchors.ToString(System.Globalization.CultureInfo.CurrentCulture)
            + " conflicting de novo anchors", rows.Count, rows.Count);
        return result;
    }
}
