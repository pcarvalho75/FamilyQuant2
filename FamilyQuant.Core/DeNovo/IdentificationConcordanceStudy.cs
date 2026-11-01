using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// Cross-engine identification audit at the normalized acquisition-file + MS2-scan grain.
    /// Database-search concordance is intentionally separate from PEAKS de-novo-only alternatives:
    /// the former tests whether two database search engines reproduce the same PSM, while the latter
    /// asks how an alternative de novo hypothesis relates to an identified spectrum.
    /// </summary>
    public sealed class IdentificationConcordanceSummary
    {
        public int PatternLabPsmRows { get; set; }
        public int PeaksDatabasePsmRows { get; set; }
        public int PatternLabScanKeys { get; set; }
        public int PeaksDatabaseScanKeys { get; set; }
        public int SharedDatabaseSearchScanKeys { get; set; }
        public int ExactDatabaseSearchSequenceScanKeys { get; set; }
        public int AdditionalIleLeuEquivalentDatabaseSearchScanKeys { get; set; }
        public int SameChargeDatabaseSearchScanKeys { get; set; }
        public int ExactSameChargeDatabaseSearchScanKeys { get; set; }

        /// <summary>Primary search-vs-search comparison: deterministic one-to-one PSM pairs inside the
        /// same normalized file + scan, with equal positive charge and precursor m/z within tolerance.</summary>
        public int PrecursorQualifiedDatabaseSearchPairs { get; set; }
        public int ExactPrecursorQualifiedDatabaseSearchPairs { get; set; }
        public int AdditionalIleLeuEquivalentPrecursorQualifiedPairs { get; set; }
        public int PrecursorQualifiedDatabaseSearchScanKeys { get; set; }
        public int ExactPrecursorQualifiedDatabaseSearchScanKeys { get; set; }
        public int PatternLabPrecursorHypothesesInSharedScans { get; set; }
        public int PeaksDatabasePrecursorHypothesesInSharedScans { get; set; }
        public double PrecursorTolerancePpm { get; set; } =
            IdentificationConcordanceStudy.DefaultPrecursorTolerancePpm;

        public int FilteredDeNovoRows { get; set; }
        public int DeNovoRowsSharingPeaksDatabaseScan { get; set; }
        public int DeNovoRowsSharingPeaksDatabaseScanAndCharge { get; set; }
        public int DeNovoRowsExactToPeaksDatabaseAtSameCharge { get; set; }

        public int PatternLabReportedChargeOneRows { get; set; }
        public int PeaksDatabaseReportedChargeOneRows { get; set; }
        public int DeNovoReportedChargeOneRows { get; set; }
        public string DeNovoSourceScopeLabel { get; set; } = "selected_de_novo_sources";
        public string DeNovoFilterDescription { get; set; } = string.Empty;
        public bool IsStrictDeNovoOnlyScope { get; set; }

        public int IleLeuEquivalentDatabaseSearchScanKeys =>
            ExactDatabaseSearchSequenceScanKeys + AdditionalIleLeuEquivalentDatabaseSearchScanKeys;

        public double StrictDatabaseSearchAgreement => SharedDatabaseSearchScanKeys > 0
            ? (double)ExactDatabaseSearchSequenceScanKeys / SharedDatabaseSearchScanKeys : 0;

        public double IleLeuEquivalentDatabaseSearchAgreement => SharedDatabaseSearchScanKeys > 0
            ? (double)IleLeuEquivalentDatabaseSearchScanKeys / SharedDatabaseSearchScanKeys : 0;

        public double ExactSameChargeDatabaseSearchAgreement => SameChargeDatabaseSearchScanKeys > 0
            ? (double)ExactSameChargeDatabaseSearchScanKeys / SameChargeDatabaseSearchScanKeys : 0;

        public int IleLeuEquivalentPrecursorQualifiedPairs =>
            ExactPrecursorQualifiedDatabaseSearchPairs +
            AdditionalIleLeuEquivalentPrecursorQualifiedPairs;

        public double PrecursorQualifiedStrictAgreement => PrecursorQualifiedDatabaseSearchPairs > 0
            ? (double)ExactPrecursorQualifiedDatabaseSearchPairs /
                PrecursorQualifiedDatabaseSearchPairs : 0;

        public double PrecursorQualifiedIleLeuEquivalentAgreement =>
            PrecursorQualifiedDatabaseSearchPairs > 0
                ? (double)IleLeuEquivalentPrecursorQualifiedPairs /
                    PrecursorQualifiedDatabaseSearchPairs : 0;

        public void Validate()
        {
            if (ExactDatabaseSearchSequenceScanKeys > SharedDatabaseSearchScanKeys
                || IleLeuEquivalentDatabaseSearchScanKeys > SharedDatabaseSearchScanKeys
                || SameChargeDatabaseSearchScanKeys > SharedDatabaseSearchScanKeys
                || ExactSameChargeDatabaseSearchScanKeys > SameChargeDatabaseSearchScanKeys)
                throw new InvalidOperationException("The database-search concordance counts do not nest correctly.");
            if (ExactPrecursorQualifiedDatabaseSearchPairs > PrecursorQualifiedDatabaseSearchPairs
                || IleLeuEquivalentPrecursorQualifiedPairs > PrecursorQualifiedDatabaseSearchPairs
                || ExactPrecursorQualifiedDatabaseSearchScanKeys > PrecursorQualifiedDatabaseSearchScanKeys
                || PrecursorQualifiedDatabaseSearchScanKeys > SharedDatabaseSearchScanKeys)
                throw new InvalidOperationException("The precursor-qualified database-search counts do not nest correctly.");
            if (DeNovoRowsSharingPeaksDatabaseScan > FilteredDeNovoRows
                || DeNovoRowsSharingPeaksDatabaseScanAndCharge > DeNovoRowsSharingPeaksDatabaseScan
                || DeNovoRowsExactToPeaksDatabaseAtSameCharge > DeNovoRowsSharingPeaksDatabaseScanAndCharge)
                throw new InvalidOperationException("The de-novo/PEAKS-database comparison counts do not nest correctly.");
        }
    }

    public static class IdentificationConcordanceStudy
    {
        public const double DefaultPrecursorTolerancePpm = 20.0;

        public static IdentificationConcordanceSummary Build(
            IReadOnlyCollection<PatternLabPsm> patternLab,
            IReadOnlyCollection<PeaksDatabasePsm> peaksDatabase,
            IReadOnlyCollection<PeaksDeNovoRow> filteredDeNovo,
            double precursorTolerancePpm = DefaultPrecursorTolerancePpm)
        {
            if (patternLab == null) throw new ArgumentNullException(nameof(patternLab));
            if (peaksDatabase == null) throw new ArgumentNullException(nameof(peaksDatabase));
            if (filteredDeNovo == null) throw new ArgumentNullException(nameof(filteredDeNovo));
            if (!(precursorTolerancePpm >= 0) || !double.IsFinite(precursorTolerancePpm))
                throw new ArgumentOutOfRangeException(nameof(precursorTolerancePpm));

            string Key(string file, int scan) => DeNovoReconciler.NormalizeRunName(file) + "\t" +
                scan.ToString(CultureInfo.InvariantCulture);

            Dictionary<string, List<PatternLabPsm>> plByScan = patternLab
                .GroupBy(p => Key(p.FileName, p.ScanNumber), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.XCorr).ToList(),
                    StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<PeaksDatabasePsm>> dbByScan = peaksDatabase
                .GroupBy(p => Key(p.FileName, p.ScanNumber), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Minus10LogP).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            int shared = 0, strict = 0, il = 0, sameCharge = 0, exactSameCharge = 0;
            int precursorPairs = 0, precursorExact = 0, precursorIl = 0;
            int precursorScans = 0, precursorExactScans = 0;
            int plHypotheses = 0, dbHypotheses = 0;
            foreach ((string key, List<PatternLabPsm> pl) in plByScan)
            {
                if (!dbByScan.TryGetValue(key, out List<PeaksDatabasePsm>? db)) continue;
                shared++;
                bool strictHere = pl.Any(p => db.Any(q =>
                    DeNovoReconciler.SequencesEqualIgnoringPtm(p.Sequence, q.Sequence)));
                bool ilHere = pl.Any(p => db.Any(q => DeNovoReconciler.SequencesEquivalent(p.Sequence, q.Sequence)));
                if (strictHere) strict++;
                if (ilHere) il++;
                if (pl.Any(p => db.Any(q => p.ChargeState == q.ChargeState))) sameCharge++;
                if (pl.Any(p => db.Any(q => p.ChargeState == q.ChargeState &&
                    DeNovoReconciler.SequencesEqualIgnoringPtm(p.Sequence, q.Sequence)))) exactSameCharge++;

                List<PatternLabPsm> plCalls = CollapsePatternLabPrecursors(pl);
                List<PeaksDatabasePsm> dbCalls = CollapsePeaksPrecursors(db);
                plHypotheses += plCalls.Count;
                dbHypotheses += dbCalls.Count;
                List<(PatternLabPsm PatternLab, PeaksDatabasePsm Peaks)> pairs =
                    MatchPrecursorsOneToOne(plCalls, dbCalls, precursorTolerancePpm);
                if (pairs.Count > 0) precursorScans++;
                bool exactInScan = false;
                foreach ((PatternLabPsm PatternLab, PeaksDatabasePsm Peaks) pair in pairs)
                {
                    precursorPairs++;
                    bool exact = DeNovoReconciler.SequencesEqualIgnoringPtm(
                        pair.PatternLab.Sequence, pair.Peaks.Sequence);
                    bool ilEquivalent = DeNovoReconciler.SequencesEquivalent(
                        pair.PatternLab.Sequence, pair.Peaks.Sequence);
                    if (exact) { precursorExact++; exactInScan = true; }
                    else if (ilEquivalent) precursorIl++;
                }
                if (exactInScan) precursorExactScans++;
            }

            int dnShared = 0, dnSameCharge = 0, dnExact = 0;
            foreach (PeaksDeNovoRow dn in filteredDeNovo)
            {
                if (!dbByScan.TryGetValue(Key(dn.RunName, dn.Scan), out List<PeaksDatabasePsm>? db)) continue;
                dnShared++;
                List<PeaksDatabasePsm> charge = db.Where(p => p.ChargeState == dn.Charge).ToList();
                if (charge.Count == 0) continue;
                dnSameCharge++;
                if (charge.Any(p => DeNovoReconciler.SequencesEqualIgnoringPtm(dn.Peptide, p.Sequence))) dnExact++;
            }

            var result = new IdentificationConcordanceSummary
            {
                PatternLabPsmRows = patternLab.Count,
                PeaksDatabasePsmRows = peaksDatabase.Count,
                PatternLabScanKeys = plByScan.Count,
                PeaksDatabaseScanKeys = dbByScan.Count,
                SharedDatabaseSearchScanKeys = shared,
                ExactDatabaseSearchSequenceScanKeys = strict,
                AdditionalIleLeuEquivalentDatabaseSearchScanKeys = Math.Max(0, il - strict),
                SameChargeDatabaseSearchScanKeys = sameCharge,
                ExactSameChargeDatabaseSearchScanKeys = exactSameCharge,
                PrecursorQualifiedDatabaseSearchPairs = precursorPairs,
                ExactPrecursorQualifiedDatabaseSearchPairs = precursorExact,
                AdditionalIleLeuEquivalentPrecursorQualifiedPairs = precursorIl,
                PrecursorQualifiedDatabaseSearchScanKeys = precursorScans,
                ExactPrecursorQualifiedDatabaseSearchScanKeys = precursorExactScans,
                PatternLabPrecursorHypothesesInSharedScans = plHypotheses,
                PeaksDatabasePrecursorHypothesesInSharedScans = dbHypotheses,
                PrecursorTolerancePpm = precursorTolerancePpm,
                FilteredDeNovoRows = filteredDeNovo.Count,
                DeNovoRowsSharingPeaksDatabaseScan = dnShared,
                DeNovoRowsSharingPeaksDatabaseScanAndCharge = dnSameCharge,
                DeNovoRowsExactToPeaksDatabaseAtSameCharge = dnExact,
                PatternLabReportedChargeOneRows = patternLab.Count(p => p.ChargeState == 1),
                PeaksDatabaseReportedChargeOneRows = peaksDatabase.Count(p => p.ChargeState == 1),
                DeNovoReportedChargeOneRows = filteredDeNovo.Count(p => p.Charge == 1),
            };
            result.Validate();
            return result;
        }

        private static List<PatternLabPsm> CollapsePatternLabPrecursors(
            IReadOnlyCollection<PatternLabPsm> rows)
        {
            var top = new Dictionary<string, PatternLabPsm>(StringComparer.Ordinal);
            foreach (PatternLabPsm row in rows)
            {
                string key = PrecursorKey(row.ChargeState, row.PrecursorMz);
                if (!top.TryGetValue(key, out PatternLabPsm? best)
                    || BetterPatternLab(row, best)) top[key] = row;
            }
            return top.Values.OrderBy(p => p.ChargeState).ThenBy(p => p.PrecursorMz)
                .ThenByDescending(p => p.XCorr)
                .ThenBy(p => DeNovoReconciler.NormalizeSequenceIgnoringPtm(
                    p.Sequence, foldIsoleucineLeucine: false), StringComparer.Ordinal)
                .ThenBy(p => p.Sequence, StringComparer.Ordinal).ToList();
        }

        private static List<PeaksDatabasePsm> CollapsePeaksPrecursors(
            IReadOnlyCollection<PeaksDatabasePsm> rows)
        {
            var top = new Dictionary<string, PeaksDatabasePsm>(StringComparer.Ordinal);
            foreach (PeaksDatabasePsm row in rows)
            {
                string key = PrecursorKey(row.ChargeState, row.PrecursorMz);
                if (!top.TryGetValue(key, out PeaksDatabasePsm? best)
                    || BetterPeaks(row, best)) top[key] = row;
            }
            return top.Values.OrderBy(p => p.ChargeState).ThenBy(p => p.PrecursorMz)
                .ThenByDescending(p => p.Minus10LogP)
                .ThenBy(p => DeNovoReconciler.NormalizeSequenceIgnoringPtm(
                    p.Sequence, foldIsoleucineLeucine: false), StringComparer.Ordinal)
                .ThenBy(p => p.Sequence, StringComparer.Ordinal).ToList();
        }

        private static string PrecursorKey(int charge, double mz) =>
            charge.ToString(CultureInfo.InvariantCulture) + "|" +
            Math.Round(mz, 5, MidpointRounding.AwayFromZero)
                .ToString("0.00000", CultureInfo.InvariantCulture);

        private static bool BetterPatternLab(PatternLabPsm candidate, PatternLabPsm best)
        {
            int c = CompareScoreDescending(candidate.XCorr, best.XCorr);
            if (c != 0) return c < 0;
            c = string.Compare(DeNovoReconciler.NormalizeSequenceIgnoringPtm(
                    candidate.Sequence, foldIsoleucineLeucine: false),
                DeNovoReconciler.NormalizeSequenceIgnoringPtm(
                    best.Sequence, foldIsoleucineLeucine: false), StringComparison.Ordinal);
            if (c != 0) return c < 0;
            return string.Compare(candidate.Sequence, best.Sequence, StringComparison.Ordinal) < 0;
        }

        private static bool BetterPeaks(PeaksDatabasePsm candidate, PeaksDatabasePsm best)
        {
            int c = CompareScoreDescending(candidate.Minus10LogP, best.Minus10LogP);
            if (c != 0) return c < 0;
            c = string.Compare(DeNovoReconciler.NormalizeSequenceIgnoringPtm(
                    candidate.Sequence, foldIsoleucineLeucine: false),
                DeNovoReconciler.NormalizeSequenceIgnoringPtm(
                    best.Sequence, foldIsoleucineLeucine: false), StringComparison.Ordinal);
            if (c != 0) return c < 0;
            return string.Compare(candidate.Sequence, best.Sequence, StringComparison.Ordinal) < 0;
        }

        private static int CompareScoreDescending(double a, double b)
        {
            bool af = double.IsFinite(a), bf = double.IsFinite(b);
            if (af != bf) return af ? -1 : 1;
            return af ? b.CompareTo(a) : 0;
        }

        private static List<(PatternLabPsm PatternLab, PeaksDatabasePsm Peaks)>
            MatchPrecursorsOneToOne(IReadOnlyList<PatternLabPsm> pl,
                IReadOnlyList<PeaksDatabasePsm> db, double tolerancePpm)
        {
            var options = new List<int>[pl.Count];
            for (int p = 0; p < pl.Count; p++)
            {
                options[p] = Enumerable.Range(0, db.Count)
                    .Where(d => pl[p].ChargeState > 0 && pl[p].ChargeState == db[d].ChargeState
                        && Ppm(pl[p].PrecursorMz, db[d].PrecursorMz) <= tolerancePpm)
                    .OrderBy(d => Ppm(pl[p].PrecursorMz, db[d].PrecursorMz))
                    .ThenBy(d => d).ToList();
            }

            var used = new bool[db.Count];
            var current = new List<(int P, int D)>();
            var best = new List<(int P, int D)>();
            double bestPpm = double.PositiveInfinity;
            string bestSignature = string.Empty;

            void Search(int p)
            {
                if (current.Count + (pl.Count - p) < best.Count) return;
                if (p == pl.Count)
                {
                    double ppm = current.Sum(x => Ppm(pl[x.P].PrecursorMz, db[x.D].PrecursorMz));
                    string signature = string.Join(";", current.Select(x =>
                        x.P.ToString("D3", CultureInfo.InvariantCulture) + ":" +
                        x.D.ToString("D3", CultureInfo.InvariantCulture)));
                    bool better = current.Count > best.Count
                        || (current.Count == best.Count && ppm < bestPpm - 1e-12)
                        || (current.Count == best.Count && Math.Abs(ppm - bestPpm) <= 1e-12
                            && (bestSignature.Length == 0
                                || string.Compare(signature, bestSignature,
                                    StringComparison.Ordinal) < 0));
                    if (better)
                    {
                        best = new List<(int P, int D)>(current);
                        bestPpm = ppm;
                        bestSignature = signature;
                    }
                    return;
                }
                foreach (int d in options[p])
                {
                    if (used[d]) continue;
                    used[d] = true;
                    current.Add((p, d));
                    Search(p + 1);
                    current.RemoveAt(current.Count - 1);
                    used[d] = false;
                }
                Search(p + 1);
            }

            Search(0);
            return best.Select(x => (pl[x.P], db[x.D])).ToList();
        }

        private static double Ppm(double referenceMz, double otherMz)
        {
            if (!(referenceMz > 0) || !double.IsFinite(referenceMz) || !double.IsFinite(otherMz))
                return double.NaN;
            return Math.Abs(referenceMz - otherMz) / referenceMz * 1_000_000.0;
        }

        public static string BuildCsv(IdentificationConcordanceSummary s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            s.Validate();
            var b = new StringBuilder("comparison,stage,count,denominator,definition\n");
            Row(b, "PatternLab_vs_PEAKS_database", "shared_file_scan", s.SharedDatabaseSearchScanKeys,
                s.PatternLabScanKeys, "RAW AUDIT: normalized acquisition file + MS2 scan present in both database-search outputs; not the primary sequence-agreement denominator");
            Row(b, "PatternLab_vs_PEAKS_database", "strict_sequence_match_ptm_removed",
                s.ExactDatabaseSearchSequenceScanKeys, s.SharedDatabaseSearchScanKeys,
                "at least one sequence pair is identical after removing PTM notation; I and L remain distinct");
            Row(b, "PatternLab_vs_PEAKS_database", "additional_il_equivalent_match",
                s.AdditionalIleLeuEquivalentDatabaseSearchScanKeys, s.SharedDatabaseSearchScanKeys,
                "not strictly identical, but identical after I/L folding");
            Row(b, "PatternLab_vs_PEAKS_database", "same_reported_charge",
                s.SameChargeDatabaseSearchScanKeys, s.SharedDatabaseSearchScanKeys,
                "at least one PatternLab and PEAKS database PSM pair reports the same charge");
            Row(b, "PatternLab_vs_PEAKS_database", "strict_sequence_match_same_charge",
                s.ExactSameChargeDatabaseSearchScanKeys, s.SameChargeDatabaseSearchScanKeys,
                "RAW SENSITIVITY: shared scan with same reported charge and strict PTM-removed sequence equality; precursor m/z not yet qualified");
            Row(b, "PatternLab_vs_PEAKS_database", "precursor_qualified_shared_scans",
                s.PrecursorQualifiedDatabaseSearchScanKeys, s.SharedDatabaseSearchScanKeys,
                "shared normalized file + scan with at least one deterministic one-to-one PSM pair having equal positive charge and precursor m/z within " +
                s.PrecursorTolerancePpm.ToString("0.###", CultureInfo.InvariantCulture) + " ppm");
            Row(b, "PatternLab_vs_PEAKS_database", "precursor_qualified_one_to_one_psm_pairs",
                s.PrecursorQualifiedDatabaseSearchPairs,
                Math.Min(s.PatternLabPrecursorHypothesesInSharedScans,
                    s.PeaksDatabasePrecursorHypothesesInSharedScans),
                "PRIMARY DENOMINATOR: top PSM per source-specific charge + m/z-rounded-to-5-decimals precursor key, then deterministic maximum-cardinality one-to-one pairing within " +
                s.PrecursorTolerancePpm.ToString("0.###", CultureInfo.InvariantCulture) + " ppm");
            Row(b, "PatternLab_vs_PEAKS_database", "strict_sequence_match_precursor_qualified",
                s.ExactPrecursorQualifiedDatabaseSearchPairs,
                s.PrecursorQualifiedDatabaseSearchPairs,
                "PRIMARY AGREEMENT: identical residue sequence after PTM notation removal within a precursor-qualified one-to-one PSM pair; I and L remain distinct");
            Row(b, "PatternLab_vs_PEAKS_database", "additional_il_equivalent_precursor_qualified",
                s.AdditionalIleLeuEquivalentPrecursorQualifiedPairs,
                s.PrecursorQualifiedDatabaseSearchPairs,
                "additional primary pairs that become equal only after I/L folding");
            string deNovoComparison = s.IsStrictDeNovoOnlyScope
                ? "PEAKS_denovoOnly_vs_PEAKS_database"
                : "selected_de_novo_sources_vs_PEAKS_database";
            Row(b, deNovoComparison, "shared_file_scan",
                s.DeNovoRowsSharingPeaksDatabaseScan, s.FilteredDeNovoRows,
                "filtered row from " + s.DeNovoSourceScopeLabel +
                " shares normalized file + scan with a PEAKS database PSM; " + s.DeNovoFilterDescription);
            Row(b, deNovoComparison, "same_reported_charge",
                s.DeNovoRowsSharingPeaksDatabaseScanAndCharge, s.DeNovoRowsSharingPeaksDatabaseScan,
                "shared scan with at least one PEAKS database PSM reporting the same charge");
            Row(b, deNovoComparison, "strict_sequence_match_same_charge",
                s.DeNovoRowsExactToPeaksDatabaseAtSameCharge, s.DeNovoRowsSharingPeaksDatabaseScanAndCharge,
                "same reported charge and sequence equality after removing PTM notation");
            return b.ToString();
        }

        private static void Row(StringBuilder b, string comparison, string stage, int count,
            int denominator, string definition) => b.Append(comparison).Append(',').Append(stage).Append(',')
            .Append(count.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(denominator.ToString(CultureInfo.InvariantCulture)).Append(',').Append('"')
            .Append(definition.Replace("\"", "\"\"")).Append('"').Append('\n');
    }
}
