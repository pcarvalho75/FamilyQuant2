using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant;

public sealed class SourceAwareReviewQueueRow : ReviewQueueRow
{
    public string SourceId { get; init; } = "";
    public string Source { get; init; } = "";
    public string ConfidenceField { get; init; } = "";
}

/// <summary>Native source labels beside inspection rows. Reconciliation decisions are unchanged.</summary>
public static class DeNovoInspectionAudit
{
    public static DeNovoReconciliation AttachSources(DeNovoReconciliation result, DeNovoImport? import)
    {
        if (import == null || import.SourceId is "peaks-denovo-only" or "peaks-denovo-all") return result;
        var sourceRows = new List<SourceAwareReviewQueueRow>();
        foreach (var row in result.Rows)
        {
            var queue = DeNovoReconciliationReport.BuildReviewQueue(new[] { row });
            if (queue.Count == 0) continue;
            var q = queue[0];
            sourceRows.Add(new SourceAwareReviewQueueRow {
                SourceId = row.DeNovo.SourceId, Source = row.DeNovo.SourceId, ConfidenceField = row.DeNovo.ConfidenceField,
                Run = q.Run, Scan = q.Scan, Verdict = q.Verdict, DeNovoSequence = q.DeNovoSequence, Alc = q.Alc,
                MinResidueConfidence = q.MinResidueConfidence, DeNovoMz = q.DeNovoMz, Charge = q.Charge,
                PatternLabSequence = q.PatternLabSequence, XCorr = q.XCorr, PatternLabMz = q.PatternLabMz, PpmApart = q.PpmApart });
        }
        result.SourceReviewQueue = sourceRows.OrderBy(q => q.Run, StringComparer.Ordinal).ThenBy(q => q.Scan).ToList();
        if (result.Overlap != null && import.Selections.Count > 0 && import.AppliedCriteria is { } criteria)
        {
            result.Overlap.SourceScopeLabel = import.SourceDisplayName;
            result.Overlap.SourceFilterDescription = "length >= " + criteria.MinLength + "; " +
                string.Join("; ", import.Selections.Select(s => s.Source.DisplayName + " " + s.Source.ConfidenceColumn +
                    " >= " + s.MinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture))) +
                "; >= " + criteria.StretchLength + " consecutive local confidences >= " + criteria.StretchFloor + "%";
        }
        return result;
    }

    public static void WriteReviewQueueCsv(string path, IReadOnlyList<SourceAwareReviewQueueRow> rows)
    {
        var csv = new StringBuilder("Run,Scan,Verdict,DeNovoSequence,NativeConfidence,MinResidueConfidence,DeNovoMz,Z,PatternLabSequence,XCorr,PatternLabMz,PpmApart,SourceId,ConfidenceField\n");
        foreach (var q in rows)
        {
            var values = new[] { q.Run, q.Scan.ToString(CultureInfo.InvariantCulture),
                DeNovoReconciliationReport.DisplayVerdict(q.Verdict), q.DeNovoSequence, q.Alc.ToString(CultureInfo.InvariantCulture),
                q.MinResidueConfidence.ToString(CultureInfo.InvariantCulture), q.DeNovoMz.ToString(CultureInfo.InvariantCulture),
                q.Charge.ToString(CultureInfo.InvariantCulture), q.PatternLabSequence, q.XCorr.ToString(CultureInfo.InvariantCulture),
                q.PatternLabMz.ToString(CultureInfo.InvariantCulture), q.PpmApart.ToString("F2", CultureInfo.InvariantCulture),
                q.SourceId, q.ConfidenceField };
            csv.AppendLine(string.Join(",", values.Select(v => SpreadsheetSafeCsv.Encode(v))));
        }
        File.WriteAllText(path, csv.ToString());
    }
}
