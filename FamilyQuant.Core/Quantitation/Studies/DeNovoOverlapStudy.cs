using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// The audit trail behind the de-novo/search-engine overlap plot. Counts are row counts at the
    /// filtered de novo grain. The stages are deliberately nested and explicitly named: a shared scan
    /// can contain multiple species, and m/z proximity alone does not establish equal charge or peptide
    /// identity, so scan overlap is not automatically sequence disagreement.
    /// </summary>
    public sealed class DeNovoOverlapSummary
    {
        public int RawDeNovoRows { get; set; }
        public int FilteredDeNovoRows { get; set; }

        public int NoSearchPsmInFileAndScan { get; set; }
        public int SharedFileAndScan { get; set; }
        public int DifferentChargeInSharedScan { get; set; }
        public int SameFileScanAndCharge { get; set; }
        public int SameChargeButNoMzMatch { get; set; }
        public int DifferentPrecursorInSharedScan { get; set; }
        public int SamePrecursorWithinTolerance { get; set; }

        /// <summary>
        /// Literal same-file/same-scan comparison against the highest-XCorr PSM in that scan after
        /// removing PTM notation. These counts deliberately ignore precursor m/z and therefore answer
        /// the requested scan-number question directly.
        /// </summary>
        public int ScanLevelStrictSequenceMatchesIgnoringPtm { get; set; }
        public int ScanLevelAdditionalIleLeuEquivalentMatches { get; set; }
        public int ScanLevelSequenceDivergencesIgnoringPtmStrict { get; set; }
        public int ScanLevelSequenceDivergencesAfterIleLeuFold { get; set; }

        /// <summary>
        /// Literal residue-string equality inside the same-file, scan, reported-charge and m/z window
        /// after removing PTM notation. I and L remain distinct.
        /// </summary>
        public int StrictSequenceMatchesIgnoringPtm { get; set; }

        /// <summary>Additional matches obtained only by folding I/L, which tandem MS cannot distinguish by mass.</summary>
        public int AdditionalIleLeuEquivalentMatches { get; set; }

        public int SequenceDivergencesIgnoringPtmStrict { get; set; }
        public int SequenceDivergencesAfterIleLeuFold { get; set; }

        public int RedundantEqual { get; set; }
        public int EqualButWeakPsm { get; set; }
        public int Conflicts { get; set; }

        public int MinLength { get; set; }
        public double MinAverageLocalConfidence { get; set; }
        public int StretchLength { get; set; }
        public int StretchFloor { get; set; }
        public double PrecursorTolerancePpm { get; set; }
        public double WeakXCorr { get; set; }
        public string SourceScopeLabel { get; set; } = "selected de novo source(s)";
        public string SourceFilterDescription { get; set; } = string.Empty;
        public bool IsStrictDeNovoOnlyScope { get; set; }

        public int IleLeuEquivalentSequenceMatches =>
            StrictSequenceMatchesIgnoringPtm + AdditionalIleLeuEquivalentMatches;

        public int ScanLevelIleLeuEquivalentSequenceMatches =>
            ScanLevelStrictSequenceMatchesIgnoringPtm + ScanLevelAdditionalIleLeuEquivalentMatches;

        public bool HasScanLevelSequenceComparison => SameFileScanAndCharge == 0
            || ScanLevelStrictSequenceMatchesIgnoringPtm + ScanLevelSequenceDivergencesIgnoringPtmStrict > 0;

        public void Validate()
        {
            if (NoSearchPsmInFileAndScan + SharedFileAndScan != FilteredDeNovoRows)
                throw new InvalidOperationException("The scan-level overlap counts do not close to the filtered total.");
            if (DifferentChargeInSharedScan + SameFileScanAndCharge != SharedFileAndScan)
                throw new InvalidOperationException("The charge-level overlap counts do not close to the shared-scan total.");
            if (SameChargeButNoMzMatch + SamePrecursorWithinTolerance != SameFileScanAndCharge)
                throw new InvalidOperationException("The equal-charge m/z counts do not close to the equal-charge total.");
            if (DifferentPrecursorInSharedScan + SamePrecursorWithinTolerance != SharedFileAndScan)
                throw new InvalidOperationException("The precursor-level overlap counts do not close to the shared-scan total.");
            // Projects saved before the scan-level extension legitimately carry zero for all four new
            // fields. Accept that legacy state; freshly computed summaries must close exactly.
            if (HasScanLevelSequenceComparison)
            {
                if (ScanLevelStrictSequenceMatchesIgnoringPtm + ScanLevelSequenceDivergencesIgnoringPtmStrict
                    != SameFileScanAndCharge)
                    throw new InvalidOperationException("The strict scan-level sequence counts do not close to the shared-file/scan/charge total.");
                if (ScanLevelIleLeuEquivalentSequenceMatches + ScanLevelSequenceDivergencesAfterIleLeuFold
                    != SameFileScanAndCharge)
                    throw new InvalidOperationException("The I/L-equivalent scan-level sequence counts do not close to the shared-file/scan/charge total.");
            }
            if (StrictSequenceMatchesIgnoringPtm + SequenceDivergencesIgnoringPtmStrict != SamePrecursorWithinTolerance)
                throw new InvalidOperationException("The strict sequence counts do not close to the same-precursor total.");
            if (IleLeuEquivalentSequenceMatches + SequenceDivergencesAfterIleLeuFold != SamePrecursorWithinTolerance)
                throw new InvalidOperationException("The I/L-equivalent sequence counts do not close to the same-precursor total.");
        }
    }

    public static class DeNovoOverlapStudy
    {
        /// <summary>
        /// Builds the overlap audit from the exact rows and counterpart choices used by reconciliation.
        /// Sequence comparison is therefore against the highest-XCorr PSM inside the same file, scan,
        /// reported charge and reported-m/z-tolerance window. PTM notation is removed for both strict
        /// and I/L-folded counts.
        /// </summary>
        public static DeNovoOverlapSummary Build(
            int rawDeNovoRows,
            IReadOnlyList<ReconciledRow> reconciliation,
            DeNovoFilterCriteria? criteria = null,
            double precursorTolerancePpm = DeNovoReconciler.DefaultPrecursorTolerancePpm,
            double weakXCorr = DeNovoReconciler.DefaultWeakXCorr)
        {
            if (reconciliation == null) throw new ArgumentNullException(nameof(reconciliation));
            criteria ??= new DeNovoFilterCriteria();

            int noPsm = 0, sharedScan = 0, sameCharge = 0, sameChargeNoMz = 0, coisolated = 0, samePrecursor = 0;
            int scanStrictEqual = 0, scanIlEqual = 0;
            int strictEqual = 0, ilEqual = 0, redundant = 0, weak = 0, conflict = 0;

            foreach (ReconciledRow row in reconciliation)
            {
                if (row.AnyChargeScanCounterpartPsm != null) sharedScan++;
                if (row.ScanCounterpartPsm != null) sameCharge++;
                if (row.ScanCounterpartPsm != null && row.Verdict == ReconciliationVerdict.CoIsolatedExtra)
                    sameChargeNoMz++;
                switch (row.Verdict)
                {
                    case ReconciliationVerdict.DeNovoOnly:
                        noPsm++;
                        break;
                    case ReconciliationVerdict.CoIsolatedExtra:
                        coisolated++;
                        break;
                    case ReconciliationVerdict.RedundantEqual:
                        samePrecursor++;
                        redundant++;
                        break;
                    case ReconciliationVerdict.EqualButWeakPsm:
                        samePrecursor++;
                        weak++;
                        break;
                    case ReconciliationVerdict.Conflict:
                        samePrecursor++;
                        conflict++;
                        break;
                }

                PatternLabPsm? scanPsm = row.ScanCounterpartPsm;
                if (scanPsm != null)
                {
                    if (DeNovoReconciler.SequencesEqualIgnoringPtm(row.DeNovo.Peptide, scanPsm.Sequence))
                        scanStrictEqual++;
                    if (DeNovoReconciler.SequencesEquivalent(row.DeNovo.Peptide, scanPsm.Sequence))
                        scanIlEqual++;
                }

                if (row.Verdict != ReconciliationVerdict.RedundantEqual
                    && row.Verdict != ReconciliationVerdict.EqualButWeakPsm
                    && row.Verdict != ReconciliationVerdict.Conflict) continue;

                PatternLabPsm? psm = row.CounterpartPsm;
                if (psm == null) continue;
                if (DeNovoReconciler.SequencesEqualIgnoringPtm(row.DeNovo.Peptide, psm.Sequence)) strictEqual++;
                if (DeNovoReconciler.SequencesEquivalent(row.DeNovo.Peptide, psm.Sequence)) ilEqual++;
            }

            var summary = new DeNovoOverlapSummary
            {
                RawDeNovoRows = rawDeNovoRows,
                FilteredDeNovoRows = reconciliation.Count,
                NoSearchPsmInFileAndScan = noPsm,
                SharedFileAndScan = sharedScan,
                DifferentChargeInSharedScan = sharedScan - sameCharge,
                SameFileScanAndCharge = sameCharge,
                SameChargeButNoMzMatch = sameChargeNoMz,
                DifferentPrecursorInSharedScan = coisolated,
                SamePrecursorWithinTolerance = samePrecursor,
                ScanLevelStrictSequenceMatchesIgnoringPtm = scanStrictEqual,
                ScanLevelAdditionalIleLeuEquivalentMatches = Math.Max(0, scanIlEqual - scanStrictEqual),
                ScanLevelSequenceDivergencesIgnoringPtmStrict = sameCharge - scanStrictEqual,
                ScanLevelSequenceDivergencesAfterIleLeuFold = sameCharge - scanIlEqual,
                StrictSequenceMatchesIgnoringPtm = strictEqual,
                AdditionalIleLeuEquivalentMatches = Math.Max(0, ilEqual - strictEqual),
                SequenceDivergencesIgnoringPtmStrict = samePrecursor - strictEqual,
                SequenceDivergencesAfterIleLeuFold = samePrecursor - ilEqual,
                RedundantEqual = redundant,
                EqualButWeakPsm = weak,
                Conflicts = conflict,
                MinLength = criteria.MinLength,
                MinAverageLocalConfidence = criteria.MinAverageLocalConfidence,
                StretchLength = criteria.StretchLength,
                StretchFloor = criteria.StretchFloor,
                PrecursorTolerancePpm = precursorTolerancePpm,
                WeakXCorr = weakXCorr,
            };
            summary.Validate();
            return summary;
        }

        public static string BuildSummaryCsv(DeNovoOverlapSummary s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            s.Validate();

            var b = new StringBuilder();
            b.Append("stage,count,denominator,definition\n");
            Row(b, "raw_de_novo_rows", s.RawDeNovoRows, s.RawDeNovoRows,
                "raw imported rows from " + s.SourceScopeLabel + " before FamilyQuant filtering");
            Row(b, "passed_filter", s.FilteredDeNovoRows, s.RawDeNovoRows,
                string.IsNullOrWhiteSpace(s.SourceFilterDescription)
                    ? $"length >= {s.MinLength}; source-native confidence >= {Num(s.MinAverageLocalConfidence)}; >= {s.StretchLength} consecutive local confidences >= {s.StretchFloor}%"
                    : s.SourceFilterDescription);
            Row(b, "no_search_psm_same_file_scan", s.NoSearchPsmInFileAndScan, s.FilteredDeNovoRows,
                "no search-engine PSM shares normalized file name and scan number");
            Row(b, "shared_file_scan", s.SharedFileAndScan, s.FilteredDeNovoRows,
                "at least one search-engine PSM shares normalized file name and scan number");
            Row(b, "shared_file_scan_different_reported_charge", s.DifferentChargeInSharedScan, s.SharedFileAndScan,
                "same normalized file and scan, but no search PSM reports the de novo charge; alternative precursor/charge hypothesis, not a sequence conflict");
            Row(b, "shared_file_scan_same_reported_charge", s.SameFileScanAndCharge, s.SharedFileAndScan,
                "same normalized file and scan with at least one search PSM reporting the same precursor charge");
            Row(b, "scan_level_strict_sequence_match_ignoring_ptm",
                s.ScanLevelStrictSequenceMatchesIgnoringPtm, s.SameFileScanAndCharge,
                "same normalized file, scan and reported charge; compared with the highest-XCorr PSM in that charge stratum; identical residue letters after removing PTM notation; I and L remain distinct");
            Row(b, "scan_level_additional_il_equivalent_match",
                s.ScanLevelAdditionalIleLeuEquivalentMatches, s.SameFileScanAndCharge,
                "same normalized file, scan and reported charge; not strictly identical, but identical after I/L folding");
            Row(b, "scan_level_sequence_divergence_ignoring_ptm_strict",
                s.ScanLevelSequenceDivergencesIgnoringPtmStrict, s.SameFileScanAndCharge,
                "same normalized file, scan and reported charge; different residue letters after removing PTM notation; I and L remain distinct");
            Row(b, "scan_level_sequence_divergence_after_il_fold",
                s.ScanLevelSequenceDivergencesAfterIleLeuFold, s.SameFileScanAndCharge,
                "same normalized file, scan and reported charge; different sequence even after PTM removal and I/L folding");
            Row(b, "same_scan_charge_no_psm_within_mz_tolerance", s.SameChargeButNoMzMatch, s.SameFileScanAndCharge,
                $"same normalized file, scan and reported charge, but no search PSM reported m/z is within {Num(s.PrecursorTolerancePpm)} ppm; not a sequence comparison");
            Row(b, "same_scan_psm_within_mz_tolerance", s.SamePrecursorWithinTolerance, s.SharedFileAndScan,
                $"same normalized file, scan and reported charge; highest-XCorr search PSM among those whose reported m/z is within {Num(s.PrecursorTolerancePpm)} ppm");
            Row(b, "strict_sequence_match_ignoring_ptm", s.StrictSequenceMatchesIgnoringPtm, s.SamePrecursorWithinTolerance,
                "inside the same-file/scan/charge/mz comparable-precursor set; identical residue letters after removing PTM notation; I and L remain distinct");
            Row(b, "additional_il_equivalent_match", s.AdditionalIleLeuEquivalentMatches, s.SamePrecursorWithinTolerance,
                "not strictly identical, but identical after I/L folding");
            Row(b, "sequence_divergence_ignoring_ptm_strict", s.SequenceDivergencesIgnoringPtmStrict, s.SamePrecursorWithinTolerance,
                "inside the same-file/scan/charge/mz comparable-precursor set; different residue letters after removing PTM notation; I and L remain distinct; this is an alternative de novo hypothesis, not a database-search accuracy estimate");
            Row(b, "sequence_divergence_after_il_fold", s.SequenceDivergencesAfterIleLeuFold, s.SamePrecursorWithinTolerance,
                "different sequence even after PTM removal and I/L folding");
            return b.ToString();
        }

        private static void Row(StringBuilder b, string stage, int count, int denominator, string definition)
        {
            b.Append(stage).Append(',').Append(count.ToString(CultureInfo.InvariantCulture)).Append(',')
             .Append(denominator.ToString(CultureInfo.InvariantCulture)).Append(',')
             .Append('"').Append(definition.Replace("\"", "\"\"")).Append('"').Append('\n');
        }

        private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
