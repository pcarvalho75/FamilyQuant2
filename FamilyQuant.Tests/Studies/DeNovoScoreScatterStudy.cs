using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    public static class DeNovoScoreScatterStudy
    {
        /// <summary>
        /// Builds the score-vs-score scatter. <paramref name="deNovoAllRows"/> is the PEAKS side (confidence
        /// ALC), <paramref name="deepNovoRows"/> is the DeepNovo side (confidence SCORE). Each is collapsed
        /// to the max confidence per clean sequence, then split into shared and each tool's unique set.
        /// </summary>
        public static DeNovoScoreScatter Build(
            IEnumerable<PeaksDeNovoRow> deNovoAllRows, DeNovoSource deNovoAllSource,
            IEnumerable<PeaksDeNovoRow> deepNovoRows, DeNovoSource deepNovoSource)
        {
            if (deNovoAllRows == null) throw new ArgumentNullException(nameof(deNovoAllRows));
            if (deepNovoRows == null) throw new ArgumentNullException(nameof(deepNovoRows));
            if (deNovoAllSource == null) throw new ArgumentNullException(nameof(deNovoAllSource));
            if (deepNovoSource == null) throw new ArgumentNullException(nameof(deepNovoSource));

            Dictionary<string, double> peaks = MaxConfidenceByCleanSequence(deNovoAllRows);
            Dictionary<string, double> deep = MaxConfidenceByCleanSequence(deepNovoRows);

            var scatter = new DeNovoScoreScatter
            {
                PeaksField = deNovoAllSource.ConfidenceColumn,
                DeepNovoField = deepNovoSource.ConfidenceColumn,
            };

            foreach (KeyValuePair<string, double> kv in peaks)
            {
                if (deep.TryGetValue(kv.Key, out double score))
                    scatter.Shared.Add(new ScoreScatterPoint
                    {
                        CleanSequence = kv.Key,
                        PeaksAlc = kv.Value,
                        DeepNovoScore = score,
                    });
                else
                    scatter.PeaksUniqueAlc.Add(kv.Value);
            }
            foreach (KeyValuePair<string, double> kv in deep)
                if (!peaks.ContainsKey(kv.Key)) scatter.DeepNovoUniqueScore.Add(kv.Value);

            // A stable order so the CSV and the render are deterministic.
            scatter.Shared = scatter.Shared
                .OrderBy(p => p.CleanSequence, StringComparer.Ordinal).ToList();

            scatter.PearsonR = Pearson(scatter.Shared);
            scatter.Validate();
            return scatter;
        }

        /// <summary>Pearson r of ALC and SCORE over the shared points; 0 when there is nothing to correlate
        /// or either series is constant.</summary>
        private static double Pearson(IReadOnlyList<ScoreScatterPoint> points)
        {
            int n = points.Count;
            if (n < 2) return 0.0;
            double mx = points.Average(p => p.PeaksAlc);
            double my = points.Average(p => p.DeepNovoScore);
            double sxy = 0, sxx = 0, syy = 0;
            foreach (ScoreScatterPoint p in points)
            {
                double dx = p.PeaksAlc - mx, dy = p.DeepNovoScore - my;
                sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
            }
            double denom = Math.Sqrt(sxx * syy);
            return denom > 0 ? sxy / denom : 0.0;
        }

        /// <summary>The maximum confidence over all rows carrying a given clean sequence (I folded to L),
        /// collapsing the row grain to the distinct-sequence grain once.</summary>
        private static Dictionary<string, double> MaxConfidenceByCleanSequence(IEnumerable<PeaksDeNovoRow> rows)
        {
            var maxConf = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (PeaksDeNovoRow row in rows)
            {
                string clean = DeNovoReconciler.NormalizeSequence(row.Peptide);
                if (clean.Length == 0) continue;
                if (!maxConf.TryGetValue(clean, out double c) || row.Alc > c) maxConf[clean] = row.Alc;
            }
            return maxConf;
        }

        /// <summary>The shared points as CSV, so the scatter is inspectable beside the figure.</summary>
        public static string BuildSharedCsv(DeNovoScoreScatter scatter)
        {
            if (scatter == null) throw new ArgumentNullException(nameof(scatter));
            var b = new StringBuilder();
            b.Append("clean_sequence,peaks_alc,deepnovo_score\n");
            foreach (ScoreScatterPoint p in scatter.Shared)
                b.Append(p.CleanSequence).Append(',')
                 .Append(p.PeaksAlc.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                 .Append(p.DeepNovoScore.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
            return b.ToString();
        }

        /// <summary>The counts and correlation as Markdown, for a human-readable supplement.</summary>
        public static string BuildSummaryMarkdown(DeNovoScoreScatter scatter)
        {
            if (scatter == null) throw new ArgumentNullException(nameof(scatter));
            var b = new StringBuilder();
            b.Append("# PEAKS de novo vs DeepNovo: score-vs-score on the shared sequences\n\n");
            b.Append("Clean sequences (modifications stripped, letters only, I folded to L) found by both ")
             .Append("tools, each collapsed to the max confidence per tool. Pure sequence bookkeeping from ")
             .Append("the CSVs: no XIC extraction. PEAKS confidence is ").Append(scatter.PeaksField)
             .Append("; DeepNovo confidence is ").Append(scatter.DeepNovoField)
             .Append(" (a different scale).\n\n");
            b.Append("| Quantity | Count |\n|---|---|\n");
            b.Append("| Shared clean sequences | ")
             .Append(scatter.SharedCount.ToString("N0", CultureInfo.InvariantCulture)).Append(" |\n");
            b.Append("| PEAKS-unique clean sequences | ")
             .Append(scatter.PeaksUniqueCount.ToString("N0", CultureInfo.InvariantCulture)).Append(" |\n");
            b.Append("| DeepNovo-unique clean sequences | ")
             .Append(scatter.DeepNovoUniqueCount.ToString("N0", CultureInfo.InvariantCulture)).Append(" |\n");
            b.Append("| Pearson r (ALC vs SCORE, shared) | ")
             .Append(scatter.PearsonR.ToString("0.###", CultureInfo.InvariantCulture)).Append(" |\n\n");
            return b.ToString();
        }
    }
}
