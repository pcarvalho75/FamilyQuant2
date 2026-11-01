using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// Sequence-level relationship between the top PEAKS and DeepNovo calls for a qualified spectrum.
    /// Categories retain I/L as distinct letters. The mass category uses modification-aware theoretical
    /// peptide masses and is descriptive; it is not evidence that either proposed sequence is correct.
    /// </summary>
    public enum AgreementCategory
    {
        /// <summary>The PTM-stripped residue strings are letter-for-letter equal (I/L distinct).</summary>
        Identical,

        /// <summary>The residue strings differ, while their modification-aware theoretical neutral masses
        /// differ by no more than the configured tolerance. The legacy member name is retained for project
        /// compatibility; exported labels use "theoretical mass match" rather than "rearrangement".</summary>
        SameMassRearrangement,

        /// <summary>The residue strings differ and their modification-aware theoretical neutral masses
        /// differ by more than the configured tolerance (or one mass could not be calculated).</summary>
        DifferentMass,
    }

    /// <summary>
    /// One scan-only top-call pair. <see cref="IncludedInPrimaryComparison"/> is true only when both calls
    /// report the same positive charge, their reported precursor m/z values differ by no more than the
    /// configured ppm tolerance, and both residue strings are non-empty. Rows that fail this gate remain
    /// in the exported audit table but are not used in primary agreement percentages.
    /// </summary>
    public sealed class AgreementSpectrum
    {
        public string Replicate { get; set; } = string.Empty;
        public int Scan { get; set; }

        public string PeaksAnnotatedPeptide { get; set; } = string.Empty;
        public string DeepNovoAnnotatedPeptide { get; set; } = string.Empty;
        public string PeaksPtm { get; set; } = string.Empty;
        public string DeepNovoPtm { get; set; } = string.Empty;

        public string PeaksSequence { get; set; } = string.Empty;
        public string DeepNovoSequence { get; set; } = string.Empty;
        public double PeaksAlc { get; set; }
        public double DeepNovoScore { get; set; }

        /// <summary>Only a drawing aid. ALC and SCORE are different scales and this value is never treated
        /// as a calibrated joint confidence.</summary>
        public double Confidence => Math.Min(PeaksAlc, DeepNovoScore);

        public int PeaksCharge { get; set; }
        public int DeepNovoCharge { get; set; }
        public long PeaksFeatureId { get; set; } = -1;
        public long DeepNovoFeatureId { get; set; } = -1;
        public bool MatchedBySharedFeatureId { get; set; }
        public double PeaksMz { get; set; }
        public double DeepNovoMz { get; set; }
        public double PrecursorMzDeltaPpm { get; set; }
        public double PeaksRetentionTimeMin { get; set; }
        public double DeepNovoRetentionTimeMin { get; set; }
        public double RetentionTimeDeltaMin { get; set; }

        public bool SameReportedCharge { get; set; }
        public bool WithinPrecursorMzTolerance { get; set; }
        public bool WithinRtSensitivityWindow { get; set; }
        public bool HasComparableSequences { get; set; }
        public bool IncludedInPrimaryComparison { get; set; }

        /// <summary>PEAKS' reported neutral mass, used only as the chart X coordinate.</summary>
        public double MassDa { get; set; }
        public double PeaksTheoreticalMassDa { get; set; }
        public double DeepNovoTheoreticalMassDa { get; set; }
        public double DeltaMassDa { get; set; }
        public int EditDistance { get; set; }
        public double SequenceIdentityPercent { get; set; }
        public AgreementCategory Category { get; set; }
        public bool IsSingleIsobaricSwap { get; set; }
        public bool InDeNovoOnly { get; set; }
    }

    /// <summary>
    /// Agreement audit with two explicitly separate grains: all normalized-run + scan top-call pairs in
    /// <see cref="ScanOnlySpectra"/>, and the same-charge/ppm-qualified primary subset in
    /// <see cref="Spectra"/>. Primary percentages always use <see cref="Total"/>.
    /// </summary>
    public sealed class DeNovoAgreementResult
    {
        public List<AgreementSpectrum> ScanOnlySpectra { get; set; } = new List<AgreementSpectrum>();
        public List<AgreementSpectrum> Spectra { get; set; } = new List<AgreementSpectrum>();

        public double MassToleranceDa { get; set; } = DeNovoAgreementStudy.DefaultMassToleranceDa;
        public double PrecursorTolerancePpm { get; set; } = DeNovoAgreementStudy.DefaultPrecursorTolerancePpm;
        public double RtSensitivityToleranceMin { get; set; } = DeNovoAgreementStudy.DefaultRtSensitivityToleranceMin;
        public string RunKeyPolicy { get; set; } = DeNovoAgreementStudy.RunKeyPolicy;
        public string TopCallPolicy { get; set; } = DeNovoAgreementStudy.TopCallPolicy;
        public string PrimaryPairGate { get; set; } = DeNovoAgreementStudy.PrimaryPairGatePolicy;
        public string RtSensitivityRule { get; set; } = DeNovoAgreementStudy.RtSensitivityPolicy;
        public string PopulationDefinition { get; set; } =
            "all imported calls present in both source exports; downstream quantitation filters are not applied";
        public double PeaksObservedMinimumConfidence { get; set; }
        public double DeepNovoObservedMinimumConfidence { get; set; }

        public int ScanOnlyTotal { get; set; }
        public int SameChargeScanOnly { get; set; }
        public int DifferentChargeScanOnly { get; set; }
        public int SameChargeOutsideMzTolerance { get; set; }
        public int PrecursorQualifiedScanOnly { get; set; }
        public int PeaksPrecursorHypothesesInSharedScans { get; set; }
        public int DeepNovoPrecursorHypothesesInSharedScans { get; set; }
        public int InvalidSequencePairCount { get; set; }
        public int RtSensitivityQualifiedCount { get; set; }
        public int ScanOnlyIdentical { get; set; }
        public int ScanOnlySameMass { get; set; }
        public int ScanOnlyDifferentMass { get; set; }

        /// <summary>Primary, same-charge/ppm-qualified comparison count.</summary>
        public int Total { get; set; }
        public int Identical { get; set; }
        public int SameMassRearrangement { get; set; }
        public int DifferentMass { get; set; }
        public int SingleIsobaricSwaps { get; set; }
        public int InDeNovoOnlyCount { get; set; }
        public bool DeNovoOnlyMembershipAvailable { get; set; }

        public double IdenticalFraction => Total > 0 ? (double)Identical / Total : 0.0;
        public int Disagreements => SameMassRearrangement + DifferentMass;
        public double TheoreticalMassMatchedFractionOfDisagreements =>
            Disagreements > 0 ? (double)SameMassRearrangement / Disagreements : 0.0;

        /// <summary>Compatibility alias. New reports use the more precise theoretical-mass name.</summary>
        public double MassPreservedFractionOfDisagreements =>
            TheoreticalMassMatchedFractionOfDisagreements;

        public double InDeNovoOnlyFraction => Total > 0 ? (double)InDeNovoOnlyCount / Total : 0.0;
        public double RtSensitivityFraction => Total > 0 ? (double)RtSensitivityQualifiedCount / Total : 0.0;

        public int CountOf(AgreementCategory category) => category switch
        {
            AgreementCategory.Identical => Identical,
            AgreementCategory.SameMassRearrangement => SameMassRearrangement,
            AgreementCategory.DifferentMass => DifferentMass,
            _ => 0,
        };

        public void Validate()
        {
            if (ScanOnlySpectra.Count != ScanOnlyTotal)
                throw new InvalidOperationException("The scan-only audit table does not close to its total.");
            if (DifferentChargeScanOnly + SameChargeScanOnly != ScanOnlyTotal)
                throw new InvalidOperationException("The charge gate does not partition the scan-only pairs.");
            if (SameChargeOutsideMzTolerance + PrecursorQualifiedScanOnly != SameChargeScanOnly)
                throw new InvalidOperationException("The precursor-m/z gate does not partition the same-charge pairs.");
            if (ScanOnlyIdentical + ScanOnlySameMass + ScanOnlyDifferentMass + InvalidSequencePairCount
                != ScanOnlyTotal)
                throw new InvalidOperationException("The scan-only sequence categories do not close.");
            if (Identical + SameMassRearrangement + DifferentMass != Total)
                throw new InvalidOperationException("The primary agreement categories do not close.");
            if (Spectra.Count != Total)
                throw new InvalidOperationException("The primary per-spectrum table does not close to its total.");
            if (PrecursorQualifiedScanOnly > ScanOnlyTotal)
                throw new InvalidOperationException("The precursor-qualified scan count exceeds the scan audit.");
            if (RtSensitivityQualifiedCount > Total)
                throw new InvalidOperationException("The RT sensitivity count exceeds the primary comparison.");
            if (SingleIsobaricSwaps > SameMassRearrangement)
                throw new InvalidOperationException("The single-isobaric-swap count exceeds its parent category.");
            if (Spectra.Any(s => !s.IncludedInPrimaryComparison))
                throw new InvalidOperationException("A primary precursor-qualified pair lacks its inclusion flag.");
        }

        public string Headline()
        {
            if (ScanOnlyTotal == 0) return "No scan-only top-call pairs were found.";
            if (Total == 0)
                return "No scan-only top-call pair passed the same-charge and precursor-m/z gate.";
            return Total.ToString("N0", CultureInfo.InvariantCulture) +
                " one-to-one precursor-call pairs were qualified across " +
                PrecursorQualifiedScanOnly.ToString("N0", CultureInfo.InvariantCulture) + " of " +
                ScanOnlyTotal.ToString("N0", CultureInfo.InvariantCulture) +
                " shared scans (same charge/" +
                PrecursorTolerancePpm.ToString("0.###", CultureInfo.InvariantCulture) +
                " ppm); " + (IdenticalFraction * 100).ToString("F1", CultureInfo.InvariantCulture) +
                "% had identical PTM-stripped sequences. Of " +
                Disagreements.ToString("N0", CultureInfo.InvariantCulture) +
                " sequence disagreements, " + SameMassRearrangement.ToString("N0", CultureInfo.InvariantCulture) +
                " had modification-aware theoretical masses within " +
                MassToleranceDa.ToString("0.###", CultureInfo.InvariantCulture) + " Da";
        }
    }

    /// <summary>PEAKS-vs-DeepNovo top-call agreement from the CSV exports; no XIC or RAW extraction.</summary>
    public static class DeNovoAgreementStudy
    {
        public const double DefaultMassToleranceDa = 0.05;
        public const double DefaultPrecursorTolerancePpm = 20.0;
        public const double DefaultRtSensitivityToleranceMin = 0.02;
        public const string RunKeyPolicy =
            "DeNovoReconciler.NormalizeRunName, compared case-insensitively, plus scan number";
        public const string TopCallPolicy =
            "top call per source-specific precursor key (Feature Id + charge when available, otherwise charge + m/z rounded to 5 decimals); highest source-native confidence; deterministic ordinal/numeric ties";
        public const string PrimaryPairGatePolicy =
            "same normalized run and scan; same positive reported charge; reported precursor m/z within the configured ppm tolerance; deterministic one-to-one matching, preferring a shared non-negative Feature Id then smaller ppm error";
        public const string RtSensitivityPolicy =
            "absolute reported RT difference <= configured minutes; reported as a sensitivity count, not required by the primary gate";

        public static DeNovoAgreementResult Build(
            IReadOnlyList<PeaksDeNovoRow> peaksAllRows,
            IReadOnlyList<PeaksDeNovoRow> deepNovoRows,
            IReadOnlyList<PeaksDeNovoRow> deNovoOnlyRows,
            double massToleranceDa = DefaultMassToleranceDa,
            double precursorTolerancePpm = DefaultPrecursorTolerancePpm,
            double rtSensitivityToleranceMin = DefaultRtSensitivityToleranceMin)
        {
            if (peaksAllRows == null) throw new ArgumentNullException(nameof(peaksAllRows));
            if (deepNovoRows == null) throw new ArgumentNullException(nameof(deepNovoRows));
            if (deNovoOnlyRows == null) throw new ArgumentNullException(nameof(deNovoOnlyRows));
            if (!IsNonNegativeFinite(massToleranceDa))
                throw new ArgumentOutOfRangeException(nameof(massToleranceDa));
            if (!IsNonNegativeFinite(precursorTolerancePpm))
                throw new ArgumentOutOfRangeException(nameof(precursorTolerancePpm));
            if (!IsNonNegativeFinite(rtSensitivityToleranceMin))
                throw new ArgumentOutOfRangeException(nameof(rtSensitivityToleranceMin));

            Dictionary<(string Run, int Scan), List<PeaksDeNovoRow>> peaksByScan =
                GroupBySpectrum(peaksAllRows);
            Dictionary<(string Run, int Scan), List<PeaksDeNovoRow>> deepByScan =
                GroupBySpectrum(deepNovoRows);
            Dictionary<(string Run, int Scan), List<PeaksDeNovoRow>> deNovoOnlyByScan =
                GroupBySpectrum(deNovoOnlyRows);

            var result = new DeNovoAgreementResult
            {
                MassToleranceDa = massToleranceDa,
                PrecursorTolerancePpm = precursorTolerancePpm,
                RtSensitivityToleranceMin = rtSensitivityToleranceMin,
                PeaksObservedMinimumConfidence = peaksAllRows.Count == 0 ? 0
                    : peaksAllRows.Min(r => r.ConfidenceValue),
                DeepNovoObservedMinimumConfidence = deepNovoRows.Count == 0 ? 0
                    : deepNovoRows.Min(r => r.ConfidenceValue),
                DeNovoOnlyMembershipAvailable = deNovoOnlyRows.Count > 0,
            };

            foreach (KeyValuePair<(string Run, int Scan), List<PeaksDeNovoRow>> kv in peaksByScan)
            {
                if (!deepByScan.TryGetValue(kv.Key, out List<PeaksDeNovoRow>? deepRows)) continue;

                // The raw audit remains one independently selected top call per shared scan. It is not
                // used as the primary comparison because one MS2 scan can carry several precursor calls.
                PeaksDeNovoRow scanPeaksTop = TopCall(kv.Value);
                PeaksDeNovoRow scanDeepTop = TopCall(deepRows);
                AgreementSpectrum scanAudit = BuildSpectrum(scanPeaksTop, scanDeepTop, kv.Key.Scan,
                    deNovoOnlyByScan, massToleranceDa, precursorTolerancePpm,
                    rtSensitivityToleranceMin, included: false);
                result.ScanOnlySpectra.Add(scanAudit);
                result.ScanOnlyTotal++;
                if (!scanAudit.HasComparableSequences) result.InvalidSequencePairCount++;
                else IncrementScanOnlyCategory(result, scanAudit.Category);

                List<PeaksDeNovoRow> peaksCalls = CollapsePrecursorHypotheses(kv.Value);
                List<PeaksDeNovoRow> deepCalls = CollapsePrecursorHypotheses(deepRows);
                result.PeaksPrecursorHypothesesInSharedScans += peaksCalls.Count;
                result.DeepNovoPrecursorHypothesesInSharedScans += deepCalls.Count;

                bool anySameCharge = peaksCalls.Any(p => deepCalls.Any(d =>
                    p.Charge > 0 && p.Charge == d.Charge));
                if (anySameCharge) result.SameChargeScanOnly++;
                else result.DifferentChargeScanOnly++;

                List<(PeaksDeNovoRow Peaks, PeaksDeNovoRow Deep)> matches =
                    MatchCallsOneToOne(peaksCalls, deepCalls, precursorTolerancePpm);
                if (matches.Count > 0) result.PrecursorQualifiedScanOnly++;
                else if (anySameCharge) result.SameChargeOutsideMzTolerance++;

                foreach ((PeaksDeNovoRow Peaks, PeaksDeNovoRow Deep) match in matches)
                {
                    AgreementSpectrum spectrum = BuildSpectrum(match.Peaks, match.Deep, kv.Key.Scan,
                        deNovoOnlyByScan, massToleranceDa, precursorTolerancePpm,
                        rtSensitivityToleranceMin, included: true);
                    if (!spectrum.HasComparableSequences) continue;
                    result.Spectra.Add(spectrum);
                    result.Total++;
                    IncrementPrimaryCategory(result, spectrum.Category);
                    if (spectrum.IsSingleIsobaricSwap) result.SingleIsobaricSwaps++;
                    if (spectrum.InDeNovoOnly) result.InDeNovoOnlyCount++;
                    if (spectrum.WithinRtSensitivityWindow) result.RtSensitivityQualifiedCount++;
                }
            }

            result.ScanOnlySpectra = result.ScanOnlySpectra
                .OrderBy(s => s.Replicate, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Scan).ToList();
            result.Spectra = result.Spectra
                .OrderBy(s => s.Replicate, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Scan).ThenBy(s => s.PeaksCharge).ThenBy(s => s.PeaksMz)
                .ThenBy(s => s.PeaksSequence, StringComparer.Ordinal)
                .ThenBy(s => s.DeepNovoSequence, StringComparer.Ordinal).ToList();
            result.Validate();
            return result;
        }

        private static void IncrementScanOnlyCategory(DeNovoAgreementResult result, AgreementCategory category)
        {
            if (category == AgreementCategory.Identical) result.ScanOnlyIdentical++;
            else if (category == AgreementCategory.SameMassRearrangement) result.ScanOnlySameMass++;
            else result.ScanOnlyDifferentMass++;
        }

        private static void IncrementPrimaryCategory(DeNovoAgreementResult result, AgreementCategory category)
        {
            if (category == AgreementCategory.Identical) result.Identical++;
            else if (category == AgreementCategory.SameMassRearrangement) result.SameMassRearrangement++;
            else result.DifferentMass++;
        }

        private static Dictionary<(string Run, int Scan), List<PeaksDeNovoRow>> GroupBySpectrum(
            IReadOnlyList<PeaksDeNovoRow> rows)
        {
            var grouped = new Dictionary<(string Run, int Scan), List<PeaksDeNovoRow>>();
            foreach (PeaksDeNovoRow row in rows)
            {
                string run = DeNovoReconciler.NormalizeRunName(row.RunName ?? string.Empty)
                    .Trim().ToUpperInvariant();
                var key = (run, row.Scan);
                if (!grouped.TryGetValue(key, out List<PeaksDeNovoRow>? list))
                {
                    list = new List<PeaksDeNovoRow>();
                    grouped.Add(key, list);
                }
                list.Add(row);
            }
            return grouped;
        }

        private static PeaksDeNovoRow TopCall(IReadOnlyList<PeaksDeNovoRow> rows)
        {
            PeaksDeNovoRow best = rows[0];
            for (int i = 1; i < rows.Count; i++)
                if (IsPreferredTopCall(rows[i], best)) best = rows[i];
            return best;
        }

        /// <summary>Collapse repeated rows for the same source-side precursor hypothesis before cross-tool
        /// pairing. Feature Id is the preferred PEAKS feature key; files without it use the same rounded
        /// precursor key as the XIC machinery.</summary>
        private static List<PeaksDeNovoRow> CollapsePrecursorHypotheses(
            IReadOnlyList<PeaksDeNovoRow> rows)
        {
            var top = new Dictionary<string, PeaksDeNovoRow>(StringComparer.Ordinal);
            foreach (PeaksDeNovoRow row in rows)
            {
                string key = row.FeatureId >= 0
                    ? "F|" + row.FeatureId.ToString(CultureInfo.InvariantCulture) + "|" +
                        row.Charge.ToString(CultureInfo.InvariantCulture)
                    : "M|" + row.Charge.ToString(CultureInfo.InvariantCulture) + "|" +
                        Math.Round(row.Mz, 5, MidpointRounding.AwayFromZero)
                            .ToString("0.00000", CultureInfo.InvariantCulture);
                if (!top.TryGetValue(key, out PeaksDeNovoRow? best) || IsPreferredTopCall(row, best))
                    top[key] = row;
            }
            return top.Values.OrderBy(r => r.FeatureId < 0 ? 1 : 0)
                .ThenBy(r => r.FeatureId).ThenBy(r => r.Charge).ThenBy(r => r.Mz)
                .ThenByDescending(r => r.Alc)
                .ThenBy(r => CleanLetters(r.Peptide), StringComparer.Ordinal)
                .ThenBy(r => r.Peptide, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Deterministic maximum-cardinality one-to-one matching inside one normalized run + scan.
        /// Candidate edges require equal positive charge and the ppm gate. Among equal-cardinality
        /// matchings, shared Feature Id matches are maximized, total ppm error minimized, then a stable
        /// row-order signature breaks any remaining tie.
        /// </summary>
        private static List<(PeaksDeNovoRow Peaks, PeaksDeNovoRow Deep)> MatchCallsOneToOne(
            IReadOnlyList<PeaksDeNovoRow> peaks, IReadOnlyList<PeaksDeNovoRow> deep,
            double precursorTolerancePpm)
        {
            var options = new List<int>[peaks.Count];
            for (int p = 0; p < peaks.Count; p++)
            {
                options[p] = Enumerable.Range(0, deep.Count)
                    .Where(d => peaks[p].Charge > 0 && peaks[p].Charge == deep[d].Charge
                        && PrecursorMzDeltaPpm(peaks[p].Mz, deep[d].Mz) <= precursorTolerancePpm)
                    .OrderByDescending(d => SharedFeature(peaks[p], deep[d]))
                    .ThenBy(d => PrecursorMzDeltaPpm(peaks[p].Mz, deep[d].Mz))
                    .ThenBy(d => d).ToList();
            }

            var used = new bool[deep.Count];
            var current = new List<(int P, int D)>();
            var best = new List<(int P, int D)>();
            int bestFeatures = -1;
            double bestPpm = double.PositiveInfinity;
            string bestSignature = string.Empty;

            void Search(int p)
            {
                if (current.Count + (peaks.Count - p) < best.Count) return;
                if (p == peaks.Count)
                {
                    int features = current.Count(x => SharedFeature(peaks[x.P], deep[x.D]));
                    double ppm = current.Sum(x => PrecursorMzDeltaPpm(peaks[x.P].Mz, deep[x.D].Mz));
                    string signature = string.Join(";", current.Select(x =>
                        x.P.ToString("D3", CultureInfo.InvariantCulture) + ":" +
                        x.D.ToString("D3", CultureInfo.InvariantCulture)));
                    bool better = current.Count > best.Count
                        || (current.Count == best.Count && features > bestFeatures)
                        || (current.Count == best.Count && features == bestFeatures && ppm < bestPpm - 1e-12)
                        || (current.Count == best.Count && features == bestFeatures
                            && Math.Abs(ppm - bestPpm) <= 1e-12
                            && (bestSignature.Length == 0
                                || string.Compare(signature, bestSignature, StringComparison.Ordinal) < 0));
                    if (better)
                    {
                        best = new List<(int P, int D)>(current);
                        bestFeatures = features;
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
            return best.Select(x => (peaks[x.P], deep[x.D])).ToList();
        }

        private static bool SharedFeature(PeaksDeNovoRow peaks, PeaksDeNovoRow deep) =>
            peaks.FeatureId >= 0 && peaks.FeatureId == deep.FeatureId;

        private static AgreementSpectrum BuildSpectrum(
            PeaksDeNovoRow peaksRow, PeaksDeNovoRow deepRow, int scan,
            IReadOnlyDictionary<(string Run, int Scan), List<PeaksDeNovoRow>> deNovoOnlyByScan,
            double massToleranceDa, double precursorTolerancePpm,
            double rtSensitivityToleranceMin, bool included)
        {
            string peaksSeq = CleanLetters(peaksRow.Peptide);
            string deepSeq = CleanLetters(deepRow.Peptide);
            bool hasSequences = peaksSeq.Length > 0 && deepSeq.Length > 0;
            int edit = hasSequences ? Levenshtein(peaksSeq, deepSeq) : 0;
            int maxLen = Math.Max(peaksSeq.Length, deepSeq.Length);
            double identity = hasSequences && maxLen > 0
                ? (1.0 - (double)edit / maxLen) * 100.0 : double.NaN;
            double peaksTheo = PeptideMass.NeutralMonoisotopicMassWithModifications(
                peaksRow.Peptide, peaksRow.Ptm);
            double deepTheo = PeptideMass.NeutralMonoisotopicMassWithModifications(
                deepRow.Peptide, deepRow.Ptm);
            double deltaMass = double.IsFinite(peaksTheo) && double.IsFinite(deepTheo)
                ? Math.Abs(peaksTheo - deepTheo) : double.NaN;
            AgreementCategory category = hasSequences
                ? Classify(peaksSeq, deepSeq, edit, deltaMass, massToleranceDa)
                : AgreementCategory.DifferentMass;
            bool sameCharge = peaksRow.Charge > 0 && peaksRow.Charge == deepRow.Charge;
            double ppm = PrecursorMzDeltaPpm(peaksRow.Mz, deepRow.Mz);
            bool withinMz = sameCharge && double.IsFinite(ppm) && ppm <= precursorTolerancePpm;
            double rtDelta = ReportedRtDelta(peaksRow.RetentionTimeMin, deepRow.RetentionTimeMin);
            bool withinRt = withinMz && double.IsFinite(rtDelta)
                && rtDelta <= rtSensitivityToleranceMin;

            return new AgreementSpectrum
            {
                Replicate = DeNovoReconciler.NormalizeRunName(peaksRow.RunName),
                Scan = scan,
                PeaksAnnotatedPeptide = peaksRow.Peptide,
                DeepNovoAnnotatedPeptide = deepRow.Peptide,
                PeaksPtm = peaksRow.Ptm,
                DeepNovoPtm = deepRow.Ptm,
                PeaksSequence = peaksSeq,
                DeepNovoSequence = deepSeq,
                PeaksAlc = peaksRow.Alc,
                DeepNovoScore = deepRow.Alc,
                PeaksCharge = peaksRow.Charge,
                DeepNovoCharge = deepRow.Charge,
                PeaksFeatureId = peaksRow.FeatureId,
                DeepNovoFeatureId = deepRow.FeatureId,
                MatchedBySharedFeatureId = SharedFeature(peaksRow, deepRow),
                PeaksMz = peaksRow.Mz,
                DeepNovoMz = deepRow.Mz,
                PrecursorMzDeltaPpm = ppm,
                PeaksRetentionTimeMin = peaksRow.RetentionTimeMin,
                DeepNovoRetentionTimeMin = deepRow.RetentionTimeMin,
                RetentionTimeDeltaMin = rtDelta,
                SameReportedCharge = sameCharge,
                WithinPrecursorMzTolerance = withinMz,
                WithinRtSensitivityWindow = withinRt,
                HasComparableSequences = hasSequences,
                IncludedInPrimaryComparison = included && withinMz && hasSequences,
                MassDa = peaksRow.Mass,
                PeaksTheoreticalMassDa = peaksTheo,
                DeepNovoTheoreticalMassDa = deepTheo,
                DeltaMassDa = deltaMass,
                EditDistance = edit,
                SequenceIdentityPercent = identity,
                Category = category,
                IsSingleIsobaricSwap = hasSequences
                    && category == AgreementCategory.SameMassRearrangement
                    && IsSingleIsobaricSwap(peaksSeq, deepSeq, massToleranceDa),
                InDeNovoOnly = HasSamePrecursorDeNovoOnlyMembership(
                    peaksRow, peaksSeq, deNovoOnlyByScan, precursorTolerancePpm),
            };
        }

        private static bool HasSamePrecursorDeNovoOnlyMembership(
            PeaksDeNovoRow peaksRow, string cleanedSequence,
            IReadOnlyDictionary<(string Run, int Scan), List<PeaksDeNovoRow>> deNovoOnlyByScan,
            double precursorTolerancePpm)
        {
            if (cleanedSequence.Length == 0 || peaksRow.Charge <= 0) return false;
            string run = DeNovoReconciler.NormalizeRunName(peaksRow.RunName ?? string.Empty)
                .Trim().ToUpperInvariant();
            if (!deNovoOnlyByScan.TryGetValue((run, peaksRow.Scan),
                    out List<PeaksDeNovoRow>? candidates)) return false;
            return candidates.Any(row => row.Charge == peaksRow.Charge
                && CleanLetters(row.Peptide) == cleanedSequence
                && ((row.FeatureId >= 0 && peaksRow.FeatureId >= 0
                        && row.FeatureId == peaksRow.FeatureId)
                    || PrecursorMzDeltaPpm(peaksRow.Mz, row.Mz) <= precursorTolerancePpm));
        }

        private static bool IsPreferredTopCall(PeaksDeNovoRow candidate, PeaksDeNovoRow best)
        {
            int c = CompareFiniteDescending(candidate.Alc, best.Alc);
            if (c != 0) return c < 0;
            c = string.Compare(CleanLetters(candidate.Peptide), CleanLetters(best.Peptide),
                StringComparison.Ordinal);
            if (c != 0) return c < 0;
            c = string.Compare(candidate.Peptide ?? string.Empty, best.Peptide ?? string.Empty,
                StringComparison.Ordinal);
            if (c != 0) return c < 0;
            c = candidate.Charge.CompareTo(best.Charge);
            if (c != 0) return c < 0;
            c = CompareFiniteAscending(candidate.Mz, best.Mz);
            if (c != 0) return c < 0;
            c = CompareFiniteAscending(candidate.RetentionTimeMin, best.RetentionTimeMin);
            if (c != 0) return c < 0;
            c = CompareFiniteAscending(candidate.Mass, best.Mass);
            if (c != 0) return c < 0;
            return string.Compare(candidate.Ptm ?? string.Empty, best.Ptm ?? string.Empty,
                StringComparison.Ordinal) < 0;
        }

        private static int CompareFiniteDescending(double a, double b)
        {
            bool af = double.IsFinite(a), bf = double.IsFinite(b);
            if (af != bf) return af ? -1 : 1;
            return af ? b.CompareTo(a) : 0;
        }

        private static int CompareFiniteAscending(double a, double b)
        {
            bool af = double.IsFinite(a), bf = double.IsFinite(b);
            if (af != bf) return af ? -1 : 1;
            return af ? a.CompareTo(b) : 0;
        }

        internal static AgreementCategory Classify(string a, string b, int edit,
                                                   double deltaMassDa, double massToleranceDa)
        {
            if (edit == 0) return AgreementCategory.Identical;
            if (double.IsFinite(deltaMassDa) && deltaMassDa <= massToleranceDa)
                return AgreementCategory.SameMassRearrangement;
            return AgreementCategory.DifferentMass;
        }

        internal static bool IsSingleIsobaricSwap(string a, string b, double massToleranceDa)
        {
            if (a.Length != b.Length) return false;
            int mismatch = -1;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == b[i]) continue;
                if (mismatch >= 0) return false;
                mismatch = i;
            }
            if (mismatch < 0) return false;
            double ma = PeptideMass.ResidueMass(a[mismatch]);
            double mb = PeptideMass.ResidueMass(b[mismatch]);
            return double.IsFinite(ma) && double.IsFinite(mb) && Math.Abs(ma - mb) <= massToleranceDa;
        }

        internal static string CleanLetters(string peptide) =>
            DeNovoReconciler.NormalizeSequenceIgnoringPtm(peptide, foldIsoleucineLeucine: false);

        internal static int Levenshtein(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) previous[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1,
                        previous[j] + 1), previous[j - 1] + cost);
                }
                (previous, current) = (current, previous);
            }
            return previous[b.Length];
        }

        internal static double PrecursorMzDeltaPpm(double peaksMz, double deepNovoMz)
        {
            if (!(peaksMz > 0) || !double.IsFinite(peaksMz) || !double.IsFinite(deepNovoMz))
                return double.NaN;
            return Math.Abs(peaksMz - deepNovoMz) / peaksMz * 1_000_000.0;
        }

        private static double ReportedRtDelta(double peaksRt, double deepNovoRt)
        {
            if (!(peaksRt >= 0) || !(deepNovoRt >= 0)
                || !double.IsFinite(peaksRt) || !double.IsFinite(deepNovoRt))
                return double.NaN;
            return Math.Abs(peaksRt - deepNovoRt);
        }

        private static bool IsNonNegativeFinite(double value) => value >= 0 && double.IsFinite(value);

        /// <summary>CSV containing every raw scan-only pair plus explicit primary-gate fields.</summary>
        public static string BuildTableCsv(DeNovoAgreementResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Validate();
            var b = new StringBuilder();
            b.Append("comparison_grain,replicate,scan,peaks_annotated_peptide,deepnovo_annotated_peptide,")
             .Append("peaks_sequence_ptm_stripped,deepnovo_sequence_ptm_stripped,peaks_alc,deepnovo_score,")
             .Append("peaks_charge,deepnovo_charge,peaks_feature_id,deepnovo_feature_id,")
             .Append("matched_by_shared_feature_id,peaks_mz,deepnovo_mz,precursor_mz_delta_ppm,")
             .Append("same_reported_charge,within_precursor_mz_tolerance,included_primary_comparison,")
             .Append("peaks_rt_min,deepnovo_rt_min,rt_delta_min,within_rt_sensitivity_window,")
             .Append("peaks_reported_mass_da,peaks_theoretical_modified_mass_da,")
             .Append("deepnovo_theoretical_modified_mass_da,delta_theoretical_mass_da,")
             .Append("edit_distance,sequence_identity_percent,category,single_isobaric_swap,")
             .Append("in_de_novo_only,denovo_only_membership_available,")
             .Append("precursor_tolerance_ppm,mass_tolerance_da,")
             .Append("rt_sensitivity_tolerance_min,population_definition,")
             .Append("peaks_observed_min_alc,deepnovo_observed_min_score,")
             .Append("run_key_policy,top_call_policy,primary_pair_gate\n");
            IEnumerable<(string Grain, AgreementSpectrum Spectrum)> rows =
                result.ScanOnlySpectra.Select(s => ("scan_top_call_audit", s))
                .Concat(result.Spectra.Select(s => ("precursor_qualified_primary_pair", s)));
            foreach ((string Grain, AgreementSpectrum Spectrum) row in rows)
            {
                AgreementSpectrum s = row.Spectrum;
                Csv(b, row.Grain); b.Append(',');
                Csv(b, s.Replicate); b.Append(',');
                b.Append(s.Scan.ToString(CultureInfo.InvariantCulture)).Append(',');
                Csv(b, s.PeaksAnnotatedPeptide); b.Append(',');
                Csv(b, s.DeepNovoAnnotatedPeptide); b.Append(',');
                Csv(b, s.PeaksSequence); b.Append(',');
                Csv(b, s.DeepNovoSequence); b.Append(',');
                b.Append(Num(s.PeaksAlc)).Append(',').Append(Num(s.DeepNovoScore)).Append(',')
                 .Append(s.PeaksCharge.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(s.DeepNovoCharge.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(s.PeaksFeatureId.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(s.DeepNovoFeatureId.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(YesNo(s.MatchedBySharedFeatureId)).Append(',')
                 .Append(Num(s.PeaksMz)).Append(',').Append(Num(s.DeepNovoMz)).Append(',')
                 .Append(NumOrBlank(s.PrecursorMzDeltaPpm)).Append(',')
                 .Append(YesNo(s.SameReportedCharge)).Append(',')
                 .Append(YesNo(s.WithinPrecursorMzTolerance)).Append(',')
                 .Append(YesNo(s.IncludedInPrimaryComparison)).Append(',')
                 .Append(Num(s.PeaksRetentionTimeMin)).Append(',')
                 .Append(Num(s.DeepNovoRetentionTimeMin)).Append(',')
                 .Append(NumOrBlank(s.RetentionTimeDeltaMin)).Append(',')
                 .Append(YesNo(s.WithinRtSensitivityWindow)).Append(',')
                 .Append(Num(s.MassDa)).Append(',')
                 .Append(NumOrBlank(s.PeaksTheoreticalMassDa)).Append(',')
                 .Append(NumOrBlank(s.DeepNovoTheoreticalMassDa)).Append(',')
                 .Append(NumOrBlank(s.DeltaMassDa)).Append(',')
                 .Append(s.HasComparableSequences ? s.EditDistance.ToString(CultureInfo.InvariantCulture) : string.Empty)
                 .Append(',').Append(s.HasComparableSequences ? Num(s.SequenceIdentityPercent) : string.Empty)
                 .Append(',').Append(s.HasComparableSequences ? CategoryLabel(s.Category) : "not_compared")
                 .Append(',').Append(YesNo(s.IsSingleIsobaricSwap))
                 .Append(',').Append(YesNo(s.InDeNovoOnly)).Append(',')
                 .Append(YesNo(result.DeNovoOnlyMembershipAvailable)).Append(',')
                 .Append(Num(result.PrecursorTolerancePpm)).Append(',')
                 .Append(Num(result.MassToleranceDa)).Append(',')
                 .Append(Num(result.RtSensitivityToleranceMin)).Append(',');
                Csv(b, result.PopulationDefinition); b.Append(',');
                b.Append(Num(result.PeaksObservedMinimumConfidence)).Append(',')
                 .Append(Num(result.DeepNovoObservedMinimumConfidence)).Append(',');
                Csv(b, result.RunKeyPolicy); b.Append(',');
                Csv(b, result.TopCallPolicy); b.Append(',');
                Csv(b, result.PrimaryPairGate); b.Append('\n');
            }
            return b.ToString();
        }

        public static string BuildSummaryMarkdown(DeNovoAgreementResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Validate();
            var b = new StringBuilder();
            b.Append("# PEAKS vs DeepNovo per-spectrum agreement\n\n")
             .Append("This is a top-call comparison from CSV exports; it does not use XIC or RAW extraction. ")
             .Append("PEAKS ALC and DeepNovo SCORE remain source-native, non-equated confidence scales. ")
             .Append("The population is all imported export rows (observed floors: PEAKS ALC ")
             .Append(Num(result.PeaksObservedMinimumConfidence)).Append(", DeepNovo SCORE ")
             .Append(Num(result.DeepNovoObservedMinimumConfidence))
             .Append("); the downstream >=80/length/local-confidence quantitation tier is not applied.\n\n")
             .Append("## Provenance and gates\n\n")
             .Append("- Population: ").Append(result.PopulationDefinition).Append(".\n")
             .Append("- Run key: ").Append(result.RunKeyPolicy).Append(".\n")
             .Append("- Top-call selection: ").Append(result.TopCallPolicy).Append(".\n")
             .Append("- Primary pair gate: ").Append(result.PrimaryPairGate).Append("; tolerance ")
             .Append(Num(result.PrecursorTolerancePpm)).Append(" ppm.\n")
             .Append("- Sequence comparison: PTM notation removed, I/L kept distinct.\n")
             .Append("- de-novo-only membership: ")
             .Append(result.DeNovoOnlyMembershipAvailable
                ? "same normalized file + scan, same positive charge, same cleaned sequence, and shared Feature Id or precursor m/z within " + Num(result.PrecursorTolerancePpm) + " ppm; sequence occurrence elsewhere does not count."
                : "not evaluated because de-novo-only rows were not loaded as an audit input; absence is never reported as zero membership.")
             .Append("\n")
             .Append("- Theoretical mass comparison: numeric inline PTM deltas included; explicit PTM mass ")
             .Append("deltas are a fallback when the peptide has no inline numeric delta; tolerance ")
             .Append(Num(result.MassToleranceDa)).Append(" Da. A theoretical mass match does not validate ")
             .Append("either sequence call.\n")
             .Append("- RT sensitivity (not a primary gate): ").Append(result.RtSensitivityRule)
             .Append("; tolerance ").Append(Num(result.RtSensitivityToleranceMin)).Append(" min.\n\n")
             .Append("## Nested pair audit\n\n")
             .Append("| Stage | Count | Denominator |\n|---|---:|---:|\n");
            AuditRow(b, "Same normalized run + scan (raw scan-only audit)", result.ScanOnlyTotal,
                result.ScanOnlyTotal);
            AuditRow(b, "Shared scan with at least one same-charge candidate", result.SameChargeScanOnly,
                result.ScanOnlyTotal);
            AuditRow(b, "Shared scan with at least one one-to-one precursor-qualified pair",
                result.PrecursorQualifiedScanOnly, result.SameChargeScanOnly);
            AuditRow(b, "PEAKS precursor hypotheses in shared scans",
                result.PeaksPrecursorHypothesesInSharedScans,
                result.PeaksPrecursorHypothesesInSharedScans);
            AuditRow(b, "DeepNovo precursor hypotheses in shared scans",
                result.DeepNovoPrecursorHypothesesInSharedScans,
                result.DeepNovoPrecursorHypothesesInSharedScans);
            AuditRow(b, "Primary one-to-one precursor-call pairs", result.Total,
                Math.Min(result.PeaksPrecursorHypothesesInSharedScans,
                    result.DeepNovoPrecursorHypothesesInSharedScans));
            AuditRow(b, "Primary pairs also within RT sensitivity window",
                result.RtSensitivityQualifiedCount, result.Total);

            b.Append("\n## Primary agreement (same charge + precursor-m/z qualified)\n\n")
             .Append("Headline: ").Append(result.Headline()).Append(".\n\n")
             .Append("| Category | Count | Share of primary pairs |\n|---|---:|---:|\n");
            CategoryRow(b, "Identical PTM-stripped sequence (I/L distinct)", result.Identical, result.Total);
            CategoryRow(b, "Different sequence; modification-aware theoretical mass within tolerance",
                result.SameMassRearrangement, result.Total);
            CategoryRow(b, "Different sequence; theoretical mass outside tolerance or unavailable",
                result.DifferentMass, result.Total);
            CategoryRow(b, "Total primary pairs", result.Total, result.Total);
            b.Append("\nAmong the ").Append(result.Disagreements.ToString("N0", CultureInfo.InvariantCulture))
             .Append(" primary sequence disagreements, ")
             .Append(result.SameMassRearrangement.ToString("N0", CultureInfo.InvariantCulture))
             .Append(" have modification-aware theoretical masses within ")
             .Append(Num(result.MassToleranceDa)).Append(" Da. This is a mass-consistency descriptor, not ")
             .Append("an accuracy estimate or an instrument-resolution claim.\n\n")
             .Append("The CSV retains all ").Append(result.ScanOnlyTotal.ToString("N0", CultureInfo.InvariantCulture))
             .Append(" scan-level audit rows and all ")
             .Append(result.Total.ToString("N0", CultureInfo.InvariantCulture))
             .Append(" one-to-one primary precursor-call pairs, with comparison_grain and every gate field ")
             .Append("made explicit.\n");
            return b.ToString();
        }

        private static void AuditRow(StringBuilder b, string label, int count, int denominator)
        {
            b.Append("| ").Append(label).Append(" | ")
             .Append(count.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
             .Append(denominator.ToString("N0", CultureInfo.InvariantCulture)).Append(" |\n");
        }

        private static void CategoryRow(StringBuilder b, string label, int count, int total)
        {
            double share = total > 0 ? (double)count / total * 100.0 : 0.0;
            b.Append("| ").Append(label).Append(" | ")
             .Append(count.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
             .Append(share.ToString("F1", CultureInfo.InvariantCulture)).Append("% |\n");
        }

        private static void Csv(StringBuilder b, string value) =>
            b.Append('"').Append((value ?? string.Empty).Replace("\"", "\"\"")).Append('"');
        private static string Num(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
        private static string NumOrBlank(double value) => double.IsFinite(value) ? Num(value) : string.Empty;
        private static string YesNo(bool value) => value ? "yes" : "no";

        public static string CategoryLabel(AgreementCategory category) => category switch
        {
            AgreementCategory.Identical => "identical",
            AgreementCategory.SameMassRearrangement => "theoretical_mass_match",
            AgreementCategory.DifferentMass => "different_theoretical_mass",
            _ => "unknown",
        };
    }
}
