using System;
using System.Collections.Generic;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// Reduces SEPro's PSM list to the five fields the reconciliation needs. The reconciler core
    /// stays free of SeproPckg2 on purpose, so it can be tested without a .sepr2; this adapter is the
    /// only place the two meet.
    ///
    /// The precursor m/z is derived from the PSM's measured MH via the same
    /// <c>pTools.ChargeAnMHPeak</c> and 5-decimal rounding the production XIC path uses, so the
    /// scan-plus-m/z join compares like with like.
    /// </summary>
    public static class SeproPsmAdapter
    {
        public const string TrustedInputWarning =
            "SEPro packages are deserialized by the legacy SEPro library with BinaryFormatter. " +
            "Open only .sepr2 files produced by a trusted SEPro installation and obtained through a trusted channel.";

        public static List<PatternLabPsm> Load(string sepr2Path)
        {
            if (string.IsNullOrWhiteSpace(sepr2Path))
                throw new ArgumentException("A SEPro package path is required.", nameof(sepr2Path));
            if (!sepr2Path.EndsWith(".sepr2", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only a trusted .sepr2 package can be loaded. " + TrustedInputWarning,
                    nameof(sepr2Path));
            var rp = SeproPckg2.ResultPackage.Load(sepr2Path);
            return FromResultPackage(rp);
        }

        public static List<PatternLabPsm> FromResultPackage(SeproPckg2.ResultPackage rp)
        {
            if (rp == null) throw new ArgumentNullException(nameof(rp));

            var list = new List<PatternLabPsm>();
            foreach (PatternTools.PSMSearchResult p in rp.MyProteins.AllPSMs)
            {
                list.Add(new PatternLabPsm
                {
                    FileName = p.FileName ?? string.Empty,
                    ScanNumber = p.ScanNumber,
                    // PrecursorMz is derived from the same charge, so the two are kept together: the
                    // isotope spacing of the identified-peptide XIC is Carbon13Spacing/z and z cannot
                    // be recovered from the m/z alone.
                    PrecursorMz = Math.Round(PatternTools.pTools.ChargeAnMHPeak(p.MeasuredMH, p.ChargeState), 5),
                    ChargeState = p.ChargeState,
                    Sequence = p.PeptideSequence ?? string.Empty,
                    XCorr = p.PrimaryScore,
                });
            }

            return list;
        }
    }
}
