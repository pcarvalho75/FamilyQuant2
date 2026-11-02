using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// The reconciliation as files a collaborator can open: a per-run verdict summary, and the review
    /// queue holding every comparable precursor requiring review (alternative sequence, plus the
    /// equal-but-weak-PSM rows the request said to keep). Numbers are InvariantCulture and fields are
    /// RFC 4180-quoted, for the same reasons as CsvReport.
    /// </summary>
    public static class DeNovoReconciliationReport
    {
        public static string DisplayVerdict(ReconciliationVerdict verdict) => verdict switch
        {
            ReconciliationVerdict.DeNovoOnly => "DeNovoOnly",
            ReconciliationVerdict.CoIsolatedExtra => "DifferentPrecursorContext",
            ReconciliationVerdict.RedundantEqual => "RedundantEqual",
            ReconciliationVerdict.EqualButWeakPsm => "EqualButWeakPsm",
            ReconciliationVerdict.Conflict => "OverlappingAlternativeSequence",
            _ => verdict.ToString(),
        };

        public static Dictionary<ReconciliationVerdict, int> CountByVerdict(IEnumerable<ReconciledRow> rows)
        {
            var counts = new Dictionary<ReconciliationVerdict, int>();
            foreach (ReconciliationVerdict v in Enum.GetValues<ReconciliationVerdict>()) counts[v] = 0;
            foreach (var r in rows) counts[r.Verdict]++;
            return counts;
        }

        /// <summary>
        /// The review queue: every row where both engines saw the same precursor, ordered by run and
        /// then by scan so the same reconciliation always lists the same way.
        /// </summary>
        public static List<ReviewQueueRow> BuildReviewQueue(IEnumerable<ReconciledRow> rows) =>
            DeNovoReviewTableBuilder.Build(rows);

        public static void WriteSummaryCsv(string path, IReadOnlyList<ReconciledRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Run,Verdict,Rows");

            foreach (var g in rows.GroupBy(r => (r.DeNovo.RunName, r.Verdict))
                                  .OrderBy(g => g.Key.RunName, StringComparer.Ordinal)
                                  .ThenBy(g => g.Key.Verdict))
            {
                sb.Append(Field(g.Key.RunName)).Append(',')
                  .Append(DisplayVerdict(g.Key.Verdict)).Append(',')
                  .Append(g.Count().ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>Every comparable precursor requiring review: an overlapping alternative or an
        /// equal-but-weak row. Side by side so a reviewer can
        /// judge without opening anything else.</summary>
        public static void WriteReviewQueueCsv(string path, IReadOnlyList<ReconciledRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Run,Scan,Verdict,DeNovoSequence,ALC,MinResidueConfidence,DeNovoMz,Z," +
                          "PatternLabSequence,XCorr,PatternLabMz,PpmApart");

            foreach (var q in BuildReviewQueue(rows))
            {
                sb.Append(Field(q.Run)).Append(',')
                  .Append(q.Scan.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(DisplayVerdict(q.Verdict)).Append(',')
                  .Append(Field(q.DeNovoSequence)).Append(',')
                  .Append(q.Alc.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(q.MinResidueConfidence.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(q.DeNovoMz.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(q.Charge.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(Field(q.PatternLabSequence)).Append(',')
                  .Append(q.XCorr.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(q.PatternLabMz.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(q.PpmApart.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
            }

            File.WriteAllText(path, sb.ToString());
        }

        private static string Field(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
