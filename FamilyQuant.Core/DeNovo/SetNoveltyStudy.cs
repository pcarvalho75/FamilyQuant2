using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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

}
