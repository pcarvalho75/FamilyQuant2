using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// The confidence field a region's median is measured in. A region that touches a PEAKS de novo set
    /// is summarised by ALC; a DeepNovo-only region is summarised by SCORE, which is a different scale.
    /// </summary>
    public enum SetNoveltyConfidence
    {
        Alc,
        Score,
    }

    /// <summary>
    /// One intersection region of the three-set UpSet view: which of the three sets a clean sequence must
    /// be in (and out of), how many such sequences there are, and the median confidence of the region in
    /// its natural field.
    /// </summary>
    public sealed class SetNoveltyRegion
    {
        public SetNoveltyRegion(string label, bool inDeNovoOnly, bool inDeNovoAll, bool inDeepNovo,
                                int count, double medianConfidence, SetNoveltyConfidence confidence)
        {
            Label = label;
            InDeNovoOnly = inDeNovoOnly;
            InDeNovoAll = inDeNovoAll;
            InDeepNovo = inDeepNovo;
            Count = count;
            MedianConfidence = medianConfidence;
            Confidence = confidence;
        }

        public string Label { get; }
        public bool InDeNovoOnly { get; }
        public bool InDeNovoAll { get; }
        public bool InDeepNovo { get; }
        public int Count { get; }
        public double MedianConfidence { get; }
        public SetNoveltyConfidence Confidence { get; }

        public string ConfidenceLabel => Confidence == SetNoveltyConfidence.Score ? "SCORE" : "ALC";
    }

    /// <summary>
    /// The set-novelty (UpSet-style) view of three de novo sources by clean sequence (modifications
    /// stripped, letters only, I folded to L): de novo only, de novo all, and DeepNovo. It reports each
    /// set's size, the non-empty intersection regions in descending count order, and two headline deltas:
    /// what the full de novo export adds over the database-unassigned only subset, and how many sequences
    /// DeepNovo reports that PEAKS de novo never does. Pure sequence bookkeeping over the CSVs: no XIC.
    /// </summary>
    public sealed class SetNovelty
    {
        public int DeNovoOnlySize { get; set; }
        public int DeNovoAllSize { get; set; }
        public int DeepNovoSize { get; set; }

        /// <summary>|de novo all| - |de novo only|: the sequences the full export adds over the only subset.</summary>
        public int AddedByFullExportOverOnly { get; set; }

        /// <summary>Clean sequences DeepNovo reports that are in neither PEAKS de novo set.</summary>
        public int DeepNovoUnique { get; set; }

        /// <summary>The size of the union of the three sets: the partition denominator.</summary>
        public int UnionSize { get; set; }

        public List<SetNoveltyRegion> Regions { get; set; } = new List<SetNoveltyRegion>();

        public void Validate()
        {
            if (Regions.Sum(r => r.Count) != UnionSize)
                throw new InvalidOperationException(
                    "The set-novelty regions do not partition the union: " +
                    Regions.Sum(r => r.Count).ToString(CultureInfo.InvariantCulture) + " vs " +
                    UnionSize.ToString(CultureInfo.InvariantCulture) + " in the union.");

            // de novo only is the database-unassigned subset of de novo all, so every "only" clean
            // sequence must also be an "all" sequence (only subset-of all). The AddedByFullExportOverOnly
            // size subtraction and the DeepNovoUnique = deep\(only union all) figure are both correct only
            // under that invariant; if a future export violated it, an inOnly && !inAll region would appear.
            SetNoveltyRegion? orphan = Regions.FirstOrDefault(r =>
                r.InDeNovoOnly && !r.InDeNovoAll && r.Count > 0);
            if (orphan != null)
                throw new InvalidOperationException(
                    "de novo only is not a subset of de novo all: region '" + orphan.Label + "' holds " +
                    orphan.Count.ToString(CultureInfo.InvariantCulture) +
                    " sequences that are in only but not in all.");
        }
    }

    public static class SetNoveltyStudy
    {
        /// <summary>
        /// Builds the three-set view. Each source is collapsed to the max confidence per clean sequence
        /// (I folded to L). de novo only and de novo all are ALC; DeepNovo is SCORE.
        /// </summary>
        public static SetNovelty Build(
            IEnumerable<PeaksDeNovoRow> deNovoOnlyRows,
            IEnumerable<PeaksDeNovoRow> deNovoAllRows,
            IEnumerable<PeaksDeNovoRow> deepNovoRows)
        {
            if (deNovoOnlyRows == null) throw new ArgumentNullException(nameof(deNovoOnlyRows));
            if (deNovoAllRows == null) throw new ArgumentNullException(nameof(deNovoAllRows));
            if (deepNovoRows == null) throw new ArgumentNullException(nameof(deepNovoRows));

            Dictionary<string, double> only = MaxConfidenceByCleanSequence(deNovoOnlyRows);
            Dictionary<string, double> all = MaxConfidenceByCleanSequence(deNovoAllRows);
            Dictionary<string, double> deep = MaxConfidenceByCleanSequence(deepNovoRows);

            var union = new HashSet<string>(StringComparer.Ordinal);
            union.UnionWith(only.Keys);
            union.UnionWith(all.Keys);
            union.UnionWith(deep.Keys);

            // The seven possible membership triples over (only, all, deep). Empty regions are dropped from
            // the presented list; the header deltas and the union partition still count every sequence.
            var buckets = new Dictionary<(bool, bool, bool), List<string>>();
            foreach (string seq in union)
            {
                var key = (only.ContainsKey(seq), all.ContainsKey(seq), deep.ContainsKey(seq));
                if (!buckets.TryGetValue(key, out List<string>? list))
                    buckets[key] = list = new List<string>();
                list.Add(seq);
            }

            var result = new SetNovelty
            {
                DeNovoOnlySize = only.Count,
                DeNovoAllSize = all.Count,
                DeepNovoSize = deep.Count,
                AddedByFullExportOverOnly = all.Count - only.Count,
                DeepNovoUnique = union.Count(seq => deep.ContainsKey(seq)
                    && !only.ContainsKey(seq) && !all.ContainsKey(seq)),
                UnionSize = union.Count,
            };

            foreach (KeyValuePair<(bool, bool, bool), List<string>> kv in buckets)
            {
                (bool inOnly, bool inAll, bool inDeep) = kv.Key;
                // A region touching either PEAKS de novo set is summarised by ALC (from de novo all when
                // present, else de novo only); a DeepNovo-only region is summarised by SCORE.
                SetNoveltyConfidence field = inAll || inOnly
                    ? SetNoveltyConfidence.Alc : SetNoveltyConfidence.Score;
                Dictionary<string, double> source = inAll ? all : inOnly ? only : deep;

                double median = Median(kv.Value.Select(s => source[s]));
                result.Regions.Add(new SetNoveltyRegion(
                    RegionLabel(inOnly, inAll, inDeep), inOnly, inAll, inDeep,
                    kv.Value.Count, median, field));
            }

            // Descending count, so the UpSet bars read biggest-first like the mockup.
            result.Regions = result.Regions
                .OrderByDescending(r => r.Count).ThenBy(r => r.Label, StringComparer.Ordinal).ToList();

            result.Validate();
            return result;
        }

        private static string RegionLabel(bool inOnly, bool inAll, bool inDeep)
        {
            if (inOnly && inAll && inDeep) return "reported by all three";
            if (inOnly && inAll && !inDeep) return "shared PEAKS core (only and all)";
            if (!inOnly && inAll && !inDeep) return "added by the full export over only";
            if (!inOnly && inAll && inDeep) return "PEAKS all and DeepNovo (not only)";
            if (!inOnly && !inAll && inDeep) return "DeepNovo unique (never in PEAKS de novo)";
            if (inOnly && !inAll && inDeep) return "de novo only and DeepNovo (not all)";
            if (inOnly && !inAll && !inDeep) return "de novo only unique (not in all)";
            return "empty";
        }

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count == 0) return 0.0;
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

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

        /// <summary>The regions as CSV, so the UpSet view is inspectable beside the figure.</summary>
        public static string BuildRegionsCsv(SetNovelty set)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            var b = new StringBuilder();
            b.Append("region,in_de_novo_only,in_de_novo_all,in_deepnovo,count,median_confidence,confidence_field\n");
            foreach (SetNoveltyRegion r in set.Regions)
                b.Append('"').Append(r.Label.Replace("\"", "\"\"")).Append('"').Append(',')
                 .Append(r.InDeNovoOnly ? "yes" : "no").Append(',')
                 .Append(r.InDeNovoAll ? "yes" : "no").Append(',')
                 .Append(r.InDeepNovo ? "yes" : "no").Append(',')
                 .Append(r.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(r.MedianConfidence.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
                 .Append(r.ConfidenceLabel).Append('\n');
            return b.ToString();
        }

        /// <summary>The set sizes, deltas and regions as Markdown, for a human-readable supplement.</summary>
        public static string BuildSummaryMarkdown(SetNovelty set)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            var b = new StringBuilder();
            b.Append("# What each de novo source adds: set-novelty view\n\n");
            b.Append("Three sets by clean sequence (modifications stripped, letters only, I folded to L): ")
             .Append("de novo only, de novo all, and DeepNovo. Confidence is collapsed to the max per ")
             .Append("sequence. Pure sequence bookkeeping from the CSVs: no XIC extraction.\n\n");
            b.Append("| Set | Size |\n|---|---|\n");
            b.Append("| de novo only | ").Append(set.DeNovoOnlySize.ToString("N0", CultureInfo.InvariantCulture)).Append(" |\n");
            b.Append("| de novo all | ").Append(set.DeNovoAllSize.ToString("N0", CultureInfo.InvariantCulture)).Append(" |\n");
            b.Append("| DeepNovo | ").Append(set.DeepNovoSize.ToString("N0", CultureInfo.InvariantCulture)).Append(" |\n\n");
            b.Append("The full de novo export adds **")
             .Append(set.AddedByFullExportOverOnly.ToString("N0", CultureInfo.InvariantCulture))
             .Append("** clean sequences over the database-unassigned only subset. DeepNovo reports **")
             .Append(set.DeepNovoUnique.ToString("N0", CultureInfo.InvariantCulture))
             .Append("** clean sequences that PEAKS de novo never does.\n\n");
            b.Append("| Region | Count | Median | Field |\n|---|---|---|---|\n");
            foreach (SetNoveltyRegion r in set.Regions)
                b.Append("| ").Append(r.Label).Append(" | ")
                 .Append(r.Count.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                 .Append(r.MedianConfidence.ToString("0.#", CultureInfo.InvariantCulture)).Append("% | ")
                 .Append(r.ConfidenceLabel).Append(" |\n");
            b.Append('\n');
            return b.ToString();
        }
    }
}
