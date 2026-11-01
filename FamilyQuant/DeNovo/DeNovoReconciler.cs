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

    /// <summary>
    /// Joins filtered de novo rows against PatternLab PSMs and assigns each row a verdict.
    ///
    /// TWO RULES THIS ENCODES, both measured before being written:
    ///
    /// 1. Runs are matched BY FILE NAME, never by position. The .xic2 that motivated this feature
    ///    stores its runs in an internal order PERMUTED relative to the file names (its pkg 1 is run
    ///    _02), and any index-based pairing would join every spectrum against the wrong run. Proven
    ///    by precursor m/z agreement: 82% on the name-matched pairing, about 0% on the others.
    ///
    /// 2. Reconciliation requires scan number, equal reported charge and precursor m/z within
    ///    tolerance, never scan alone. A different charge or m/z is an alternative precursor context,
    ///    not a sequence conflict.
    /// </summary>
    public static class DeNovoReconciler
    {
        /// <summary>Below this cross-correlation a PSM does not outrank the de novo sequence that
        /// matched it (the request's item 4).</summary>
        public const double DefaultWeakXCorr = 2.5;

        public const double DefaultPrecursorTolerancePpm = 20;

        public static List<ReconciledRow> Reconcile(
            IEnumerable<PeaksDeNovoRow> filteredDeNovo,
            IEnumerable<PatternLabPsm> psms,
            double weakXCorr = DefaultWeakXCorr,
            double precursorTolerancePpm = DefaultPrecursorTolerancePpm)
        {
            // Scans repeat across runs, so the lookup is per run name; and one scan can carry several
            // PSMs in principle, so the value is a list.
            var byRunAndScan = new Dictionary<string, Dictionary<int, List<PatternLabPsm>>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var p in psms)
            {
                string run = NormalizeRunName(p.FileName);
                if (!byRunAndScan.TryGetValue(run, out var byScan))
                    byRunAndScan[run] = byScan = new Dictionary<int, List<PatternLabPsm>>();
                if (!byScan.TryGetValue(p.ScanNumber, out var list))
                    byScan[p.ScanNumber] = list = new List<PatternLabPsm>();
                list.Add(p);
            }

            var result = new List<ReconciledRow>();

            foreach (var dn in filteredDeNovo)
            {
                var row = new ReconciledRow { DeNovo = dn, Verdict = ReconciliationVerdict.DeNovoOnly };

                if (byRunAndScan.TryGetValue(NormalizeRunName(dn.RunName), out var byScan)
                    && byScan.TryGetValue(dn.Scan, out var sameScan))
                {
                    row.AnyChargeScanCounterpartPsm = sameScan.OrderByDescending(p => p.XCorr).First();

                    List<PatternLabPsm> sameCharge = sameScan
                        .Where(p => p.ChargeState == dn.Charge)
                        .OrderByDescending(p => p.XCorr)
                        .ToList();
                    row.ScanCounterpartPsm = sameCharge.FirstOrDefault();

                    // Only equal-charge candidates can denote the same precursor-ion hypothesis. The
                    // m/z window then selects candidates centered on the same isolated ion.
                    //
                    // This gate is deliberately a DIRECT m/z match, NOT the isotope-error-aware
                    // equivalence used by the quant join and cross-side suppression. Here the purpose is
                    // the opposite: to tell a redundant same-precursor PSM apart from a genuinely
                    // different co-isolated peptide one or two daltons away. Merging isotope-offset
                    // precursors here would reclassify those legitimate co-isolated de novo calls as
                    // redundant and drop them from de novo, which was measured to move the de novo
                    // fraction by about 2.4 points. So the isotope-error awareness stays in the quant
                    // paths and the co-isolation classification stays a direct match.
                    var samePrecursor = sameCharge
                        .Where(p => Math.Abs(p.PrecursorMz - dn.Mz) / dn.Mz * 1e6 <= precursorTolerancePpm)
                        .OrderByDescending(p => p.XCorr)
                        .ToList();

                    if (samePrecursor.Count == 0)
                    {
                        row.Verdict = ReconciliationVerdict.CoIsolatedExtra;
                        row.CounterpartPsm = row.ScanCounterpartPsm ?? row.AnyChargeScanCounterpartPsm;
                    }
                    else
                    {
                        var best = samePrecursor[0];
                        row.CounterpartPsm = best;

                        if (SequencesEquivalent(dn.Peptide, best.Sequence))
                        {
                            row.Verdict = best.XCorr < weakXCorr
                                ? ReconciliationVerdict.EqualButWeakPsm
                                : ReconciliationVerdict.RedundantEqual;
                        }
                        else
                        {
                            row.Verdict = ReconciliationVerdict.Conflict;
                        }
                    }
                }

                result.Add(row);
            }

            return result;
        }

        /// <summary>
        /// Whether a de novo sequence and a search-engine sequence denote the same peptide.
        ///
        /// Fragment spectra cannot distinguish leucine from isoleucine (identical mass), so I and L
        /// are equated. PEAKS writes modifications inline as "(+57.02)" while SEPro sequences carry
        /// the plain residue, so modification annotations are stripped before comparing. This
        /// deliberately treats "same backbone, different claimed modification" as EQUAL: for the
        /// purpose of dropping a redundant de novo row that is correct, and the modification
        /// difference survives in the report where both strings are shown.
        /// </summary>
        public static bool SequencesEquivalent(string deNovo, string search)
        {
            bool deNovoValid = TryNormalizeSequenceIgnoringPtm(deNovo,
                foldIsoleucineLeucine: true, out string normalizedDeNovo);
            bool searchValid = TryNormalizeSequenceIgnoringPtm(search,
                foldIsoleucineLeucine: true, out string normalizedSearch);
            return deNovoValid && searchValid
                && string.Equals(normalizedDeNovo, normalizedSearch, StringComparison.Ordinal);
        }

        /// <summary>
        /// Strict amino-acid equality after removing PTM notation and SEPro flanking residues.
        /// Unlike <see cref="SequencesEquivalent"/>, this does NOT fold isoleucine into leucine.
        /// It is the literal answer to "same sequence, forgetting PTMs"; the I/L-folded result is
        /// reported beside it because ordinary tandem MS cannot distinguish those two residues by mass.
        /// </summary>
        public static bool SequencesEqualIgnoringPtm(string deNovo, string search)
        {
            bool deNovoValid = TryNormalizeSequenceIgnoringPtm(deNovo,
                foldIsoleucineLeucine: false, out string normalizedDeNovo);
            bool searchValid = TryNormalizeSequenceIgnoringPtm(search,
                foldIsoleucineLeucine: false, out string normalizedSearch);
            return deNovoValid && searchValid
                && string.Equals(normalizedDeNovo, normalizedSearch, StringComparison.Ordinal);
        }

        public static string NormalizeSequence(string s)
            => NormalizeSequenceIgnoringPtm(s, foldIsoleucineLeucine: true);

        /// <summary>
        /// Removes well-formed inline PTM annotations, optionally folds I/L, and returns uppercase
        /// standard amino-acid letters. Invalid or ambiguous input returns an empty string. In
        /// particular, unmatched, mismatched, empty or nested annotations and non-standard residue
        /// letters are rejected rather than partially stripped into a sequence that could match a
        /// different peptide.
        /// </summary>
        public static string NormalizeSequenceIgnoringPtm(string s, bool foldIsoleucineLeucine)
        {
            return TryNormalizeSequenceIgnoringPtm(s, foldIsoleucineLeucine,
                out string normalized) ? normalized : string.Empty;
        }

        /// <summary>
        /// Attempts the conservative PTM-clean normalization used for sequence-level comparisons.
        /// Only the twenty standard one-letter amino-acid codes are accepted outside a balanced,
        /// non-nested PTM annotation. A dash is permitted only as an unknown SEPro flanking residue.
        /// </summary>
        public static bool TryNormalizeSequenceIgnoringPtm(string? s, bool foldIsoleucineLeucine,
                                                            out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(s)) return false;

            string text = s.Trim();

            // SEPro sequences sometimes travel with flanking residues ("K.PEPTIDER.A"). Accept the
            // wrapper only when both flanks are standard residue letters (or '-' for an unknown flank),
            // so a decimal point or arbitrary punctuation cannot accidentally trigger substring removal.
            if (text.Length > 4 && text[1] == '.' && text[text.Length - 2] == '.')
            {
                if (!IsValidFlankingResidue(text[0]) || !IsValidFlankingResidue(text[text.Length - 1]))
                    return false;
                text = text.Substring(2, text.Length - 4);
            }

            var sequence = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (IsOpeningDelimiter(c))
                {
                    char expectedClose = ClosingDelimiter(c);
                    int contentStart = i + 1;
                    int end = contentStart;
                    for (; end < text.Length && text[end] != expectedClose; end++)
                    {
                        // Nested annotations, a mismatched closer, or an opener of another kind are
                        // ambiguous. Reject the whole sequence instead of leaking annotation letters.
                        if (IsOpeningDelimiter(text[end]) || IsClosingDelimiter(text[end]))
                            return false;
                    }
                    if (end >= text.Length || end == contentStart
                        || string.IsNullOrWhiteSpace(text.Substring(contentStart, end - contentStart)))
                        return false;
                    i = end;
                    continue;
                }

                if (IsClosingDelimiter(c)) return false;

                char residue = char.ToUpperInvariant(c);
                if (!IsStandardResidue(residue)) return false;
                if (foldIsoleucineLeucine && residue == 'I') residue = 'L';
                sequence.Append(residue);
            }

            if (sequence.Length == 0) return false;
            normalized = sequence.ToString();
            return true;
        }

        private const string StandardResidues = "ACDEFGHIKLMNPQRSTVWY";

        private static bool IsStandardResidue(char residue) =>
            residue <= 0x7f && StandardResidues.IndexOf(residue) >= 0;

        private static bool IsValidFlankingResidue(char residue) =>
            residue == '-' || IsStandardResidue(char.ToUpperInvariant(residue));

        private static bool IsOpeningDelimiter(char c) => c == '(' || c == '[' || c == '{';

        private static bool IsClosingDelimiter(char c) => c == ')' || c == ']' || c == '}';

        private static char ClosingDelimiter(char opener) => opener == '(' ? ')'
            : opener == '[' ? ']' : '}';

        /// <summary>"C:\runs\x.raw", "x.raw", "x.RAW", "x.sqt", "x.raw.yada", "x" all denote the run
        /// "x". SEPro PSMs carry the search-engine file name, which stacks extensions (.sqt, .yada) on
        /// top of the acquisition name, so stripping repeats until nothing known remains.</summary>
        public static string NormalizeRunName(string fileName)
        {
            string name = fileName;
            int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            if (slash >= 0) name = name.Substring(slash + 1);

            string[] tails = { ".raw", ".sepr2", ".sqt", ".yada", ".ms2", ".mzml" };
            bool stripped = true;
            while (stripped)
            {
                stripped = false;
                foreach (string t in tails)
                {
                    if (name.EndsWith(t, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(0, name.Length - t.Length);
                        stripped = true;
                    }
                }
            }

            return name;
        }
    }
}
