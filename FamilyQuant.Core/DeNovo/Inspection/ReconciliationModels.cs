using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>What became of one filtered de novo row after meeting the PatternLab results.</summary>
    public enum ReconciliationVerdict
    {
        /// <summary>No PatternLab identification shares its spectrum: the genuinely new territory.
        /// Measured on the motivating data set against the COMPLETE PSM list, 25,437 of the 35,686
        /// filtered rows, about 71%. An earlier 97% came from the quantified subset only.</summary>
        DeNovoOnly,

        /// <summary>PatternLab identified the same scan but no PSM reported m/z is within tolerance of
        /// the de novo row. This is compatible with a second co-isolated species or with precursor-
        /// metadata disagreement. It is additional evidence, not a sequence conflict.</summary>
        CoIsolatedExtra,

        /// <summary>Same scan, similar reported m/z, and the same sequence once notation is normalised:
        /// the de novo row confirms the PSM sequence and adds nothing. Dropped from quantitation, kept
        /// in the report.</summary>
        RedundantEqual,

        /// <summary>Same spectrum, same sequence, but the PSM's cross-correlation is below the
        /// threshold: the request was explicit that the de novo row is NOT discarded in that case.</summary>
        EqualButWeakPsm,

        /// <summary>Same file, scan, reported charge and similar m/z, but a different sequence: an
        /// overlapping alternative hypothesis. The legacy enum name is retained for file/report
        /// compatibility; user-facing surfaces must not present it as database-search accuracy.</summary>
        Conflict,
    }

    /// <summary>The PatternLab side of the join, reduced to what the reconciliation needs. An adapter
    /// builds these from SeproPckg2's PSMSearchResult; the core stays free of that dependency so it
    /// can be tested without a .sepr2.</summary>
    public class PatternLabPsm
    {
        public string FileName { get; set; } = string.Empty;
        public int ScanNumber { get; set; }
        public double PrecursorMz { get; set; }
        public string Sequence { get; set; } = string.Empty;

        /// <summary>The precursor charge SEPro assigned. Carried alongside <see cref="PrecursorMz"/>
        /// (which was derived from it) because the isotope spacing of the identified-peptide XIC is
        /// Carbon13Spacing/z, and z cannot be recovered from the m/z alone. Zero on a PSM whose charge
        /// was not recorded.</summary>
        public int ChargeState { get; set; }

        /// <summary>SEPro's primary score, the cross-correlation of the request's item 4.</summary>
        public double XCorr { get; set; }
    }

    public class ReconciledRow
    {
        public PeaksDeNovoRow DeNovo { get; set; } = null!;
        public ReconciliationVerdict Verdict { get; set; }

        /// <summary>
        /// Highest-XCorr PSM carrying the same normalized file name and scan number AND the same
        /// reported precursor charge. A shared scan with a different charge is an alternative precursor
        /// hypothesis, not a sequence comparison.
        /// </summary>
        public PatternLabPsm? ScanCounterpartPsm { get; set; }

        /// <summary>Highest-XCorr PSM carrying the same normalized file name and scan number regardless
        /// of charge. Preserved only to count literal scan overlap; never used for sequence conflict.</summary>
        public PatternLabPsm? AnyChargeScanCounterpartPsm { get; set; }

        /// <summary>The highest-XCorr PSM inside the same-file, scan, reported-charge and m/z-tolerance
        /// window, when one exists.</summary>
        public PatternLabPsm? CounterpartPsm { get; set; }
    }

}
