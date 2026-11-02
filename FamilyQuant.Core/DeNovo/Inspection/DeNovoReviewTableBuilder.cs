using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.DeNovo;

/// <summary>Shapes both review tables from the same decision rows. The legacy report and source-aware
/// inspection keep their existing column contracts and stable run/scan order.</summary>
public static class DeNovoReviewTableBuilder
{
    private static bool NeedsReview(ReconciledRow row) =>
        row.Verdict == ReconciliationVerdict.Conflict || row.Verdict == ReconciliationVerdict.EqualButWeakPsm;

    public static List<ReviewQueueRow> Build(IEnumerable<ReconciledRow> rows) =>
        rows.Where(NeedsReview).OrderBy(r => r.DeNovo.RunName, StringComparer.Ordinal)
            .ThenBy(r => r.DeNovo.Scan).Select(Shape).ToList();

    public static List<SourceAwareReviewQueueRow> BuildWithSources(IEnumerable<ReconciledRow> rows)
    {
        var queue = new List<SourceAwareReviewQueueRow>();
        foreach (ReconciledRow row in rows.Where(NeedsReview))
        {
            ReviewQueueRow q = Shape(row);
            queue.Add(new SourceAwareReviewQueueRow
            {
                SourceId = row.DeNovo.SourceId, Source = row.DeNovo.SourceId,
                ConfidenceField = row.DeNovo.ConfidenceField,
                Run = q.Run, Scan = q.Scan, Verdict = q.Verdict, DeNovoSequence = q.DeNovoSequence,
                Alc = q.Alc, MinResidueConfidence = q.MinResidueConfidence, DeNovoMz = q.DeNovoMz,
                Charge = q.Charge, PatternLabSequence = q.PatternLabSequence, XCorr = q.XCorr,
                PatternLabMz = q.PatternLabMz, PpmApart = q.PpmApart,
            });
        }
        return queue.OrderBy(q => q.Run, StringComparer.Ordinal).ThenBy(q => q.Scan).ToList();
    }

    private static ReviewQueueRow Shape(ReconciledRow row)
    {
        PeaksDeNovoRow dn = row.DeNovo;
        PatternLabPsm ps = row.CounterpartPsm!;
        return new ReviewQueueRow
        {
            Run = dn.RunName, Scan = dn.Scan, Verdict = row.Verdict, DeNovoSequence = dn.Peptide,
            Alc = dn.Alc, MinResidueConfidence = dn.LocalConfidence.Length > 0 ? dn.LocalConfidence.Min() : 0,
            DeNovoMz = dn.Mz, Charge = dn.Charge, PatternLabSequence = ps.Sequence,
            XCorr = ps.XCorr, PatternLabMz = ps.PrecursorMz,
            PpmApart = Math.Abs(ps.PrecursorMz - dn.Mz) / dn.Mz * 1e6,
        };
    }
}
