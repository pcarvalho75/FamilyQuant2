using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// One comparable precursor shaped for reading: overlapping de-novo-only alternatives and the
    /// equal-but-weak-PSM rows retained for review.
    ///
    /// The CSV and the on-screen review queue are the SAME rows. Shaping them here rather than in each
    /// consumer is what stops the two from drifting, which is how a table and its export end up
    /// disagreeing about what is in the queue.
    /// </summary>
    public class ReviewQueueRow
    {
        public string Run { get; set; } = string.Empty;
        public int Scan { get; set; }
        public ReconciliationVerdict Verdict { get; set; }
        public string VerdictLabel => DeNovoReconciliationReport.DisplayVerdict(Verdict);

        public string DeNovoSequence { get; set; } = string.Empty;
        public double Alc { get; set; }

        /// <summary>The weakest residue of the de novo call, 0 when the export carried no trace.</summary>
        public int MinResidueConfidence { get; set; }

        public double DeNovoMz { get; set; }
        public int Charge { get; set; }

        public string PatternLabSequence { get; set; } = string.Empty;
        public double XCorr { get; set; }
        public double PatternLabMz { get; set; }

        /// <summary>How far apart the two precursors are. Within the join tolerance by construction,
        /// so it is a measure of agreement rather than a filter.</summary>
        public double PpmApart { get; set; }
    }

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
        public static List<ReviewQueueRow> BuildReviewQueue(IEnumerable<ReconciledRow> rows)
        {
            var queue = new List<ReviewQueueRow>();

            foreach (var r in rows.Where(x => x.Verdict == ReconciliationVerdict.Conflict
                                           || x.Verdict == ReconciliationVerdict.EqualButWeakPsm)
                                  .OrderBy(x => x.DeNovo.RunName, StringComparer.Ordinal)
                                  .ThenBy(x => x.DeNovo.Scan))
            {
                var dn = r.DeNovo;
                var ps = r.CounterpartPsm!;

                queue.Add(new ReviewQueueRow
                {
                    Run = dn.RunName,
                    Scan = dn.Scan,
                    Verdict = r.Verdict,
                    DeNovoSequence = dn.Peptide,
                    Alc = dn.Alc,
                    MinResidueConfidence = dn.LocalConfidence.Length > 0 ? dn.LocalConfidence.Min() : 0,
                    DeNovoMz = dn.Mz,
                    Charge = dn.Charge,
                    PatternLabSequence = ps.Sequence,
                    XCorr = ps.XCorr,
                    PatternLabMz = ps.PrecursorMz,
                    PpmApart = Math.Abs(ps.PrecursorMz - dn.Mz) / dn.Mz * 1e6,
                });
            }

            return queue;
        }

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
