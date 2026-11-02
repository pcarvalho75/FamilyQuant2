using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.DeNovo;

/// <summary>The additive de novo population used by extraction and homology inspection. Reconciliation
/// alternatives stay in their review table rather than receiving another quantitative contribution.</summary>
public static class DeNovoInspectionSelection
{
    public static bool IsRetained(ReconciliationVerdict verdict) =>
        verdict == ReconciliationVerdict.DeNovoOnly || verdict == ReconciliationVerdict.CoIsolatedExtra;

    public static Dictionary<string, List<PeaksDeNovoRow>> GroupRetainedByRun(IEnumerable<ReconciledRow> rows)
    {
        if (rows == null) throw new ArgumentNullException(nameof(rows));
        var byRun = new Dictionary<string, List<PeaksDeNovoRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (ReconciledRow row in rows)
        {
            if (!IsRetained(row.Verdict)) continue;
            string run = DeNovoReconciler.NormalizeRunName(row.DeNovo.RunName);
            if (run.Length == 0) continue;
            if (!byRun.TryGetValue(run, out List<PeaksDeNovoRow>? kept))
                byRun[run] = kept = new List<PeaksDeNovoRow>();
            kept.Add(row.DeNovo);
        }
        return byRun;
    }

    public static Dictionary<string, List<PeaksDeNovoRow>> KeptRowsByRun(DeNovoReconciliation? reconciliation) =>
        reconciliation == null
            ? new Dictionary<string, List<PeaksDeNovoRow>>(StringComparer.OrdinalIgnoreCase)
            : GroupRetainedByRun(reconciliation.Rows);

    public static List<string> KeptRunNames(DeNovoReconciliation? reconciliation) =>
        KeptRowsByRun(reconciliation).Keys.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();

    public static List<string> PeptidesForHomology(IReadOnlyList<PeaksDeNovoRow> filtered,
        DeNovoReconciliation? reconciliation)
    {
        IEnumerable<PeaksDeNovoRow> rows = reconciliation == null ? filtered
            : reconciliation.Rows.Where(r => IsRetained(r.Verdict)).Select(r => r.DeNovo);
        return rows.Select(r => DeNovoReconciler.NormalizeSequence(r.Peptide))
            .Where(p => p.Length > 0).Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
    }
}
