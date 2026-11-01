using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// One peptide of one run and one side, reduced from all the anchors that sampled it to a SINGLE
    /// representative area: the peptide's quantified peak.
    ///
    /// WHY THIS GRAIN AND NOT THE ANCHOR. FamilyQuant's own extraction (the Q2 store) holds one
    /// <see cref="QuantRow"/> per unique (scan, m/z, charge) request, i.e. one per MS2 ANCHOR. A
    /// precursor sampled by several MS2 scans inside one elution peak therefore appears as several
    /// rows, and <see cref="DeNovoBatchExtractor.DistinctPeaks"/> only collapses the BIT-IDENTICAL
    /// re-anchors, leaving most of the intra-peptide redundancy in place - which is why summing the
    /// stored rows straight gives 142% of the TIC on M+0 and 322% on the envelope. Collapsing to one
    /// area per run, side, canonical PTM-aware peptidoform and charge, with the peptide's LARGEST-M+0
    /// peak kept as the representative, removes that double counting exactly and restores the 11.87% the
    /// .xic2 stored quant reports. Fixed carbamidomethyl-C notation is canonicalized and genuinely
    /// different PTMs stay distinct, but anchors that differ only in precursor mass are the +-1 / +-2 Da
    /// monoisotopic-peak misassignments of the SAME peptide and collapse into that one representative.
    /// See <see cref="NativeQuantFractions"/>.
    /// </summary>
    public sealed class NativePeptideQuant
    {
        public string Run { get; set; } = string.Empty;

        /// <summary>The MS2 scan of the deterministic anchor selected to represent this peptide.
        /// This is retained so downstream audit tables can mark exactly one credited source row even
        /// when two anchors have bit-identical areas and precursor metadata.</summary>
        public int RepresentativeScan { get; set; }

        /// <summary>"identified" or "denovo", the two sides told apart, never merged.</summary>
        public string Side { get; set; } = string.Empty;

        public string Sequence { get; set; } = string.Empty;

        public int Charge { get; set; }

        public double PrecursorMz { get; set; }

        public double MonoisotopicMass => AnalyticsBuilder.MonoisotopicMass(PrecursorMz, Charge);

        /// <summary>The representative peak's M+0: the largest M+0 among this peptide's anchors, so the
        /// sum over peptides counts each elution peak once and not once per MS2 event.</summary>
        public double AreaM0 { get; set; }

        /// <summary>The SAME representative peak's isotope envelope. Taken from the max-M+0 anchor, not
        /// the max-envelope one, so the envelope total is not biased upward by cherry-picking.</summary>
        public double EnvelopeSum { get; set; }

        /// <summary>For de novo peptides, M+0 scaled by the identified envelope/M+0 ratio in the same
        /// fixed neutral-mass stratum. For identified peptides this equals the measured envelope.</summary>
        public double ReferenceCalibratedEnvelope { get; set; }

        /// <summary>How many anchors collapsed into this peptide. 1 for a peptide sampled once; larger
        /// for a peptide re-anchored by several MS2 scans in one elution peak.</summary>
        public int AnchorCount { get; set; }

        /// <summary>The de novo source claims unioned across every anchor that collapsed into this one
        /// representative, deduplicated by (source, sequence). It carries the peptide's source membership
        /// - PEAKS de-novo-only, DeepNovo, or both - onto the peptide grain, so a consumer that splits the
        /// de novo envelope by its calling source (see <see cref="FamilyEnvelopeQuant"/>) does not need the
        /// raw <see cref="QuantRow"/> back. Empty on identified peptides and on rows carrying no evidence.</summary>
        public List<DeNovoEvidence> DeNovoEvidence { get; set; } = new List<DeNovoEvidence>();

        /// <summary>The same representative peak's IDENTIFIED-BUT-NOT-QUANTIFIED ion current: the clusters
        /// that were found for this peptide's isotopes and that the minimum-points rule refused. Taken
        /// from the representative anchor, like the envelope, so it is on the peptide grain and cannot be
        /// counted once per MS2 event.</summary>
        public double UnderSampledArea { get; set; }

        /// <summary>True when any part of <see cref="UnderSampledArea"/> is the single-point ESTIMATE.</summary>
        public bool UnderSampledIsEstimate { get; set; }
    }

    /// <summary>One run's identified and de novo native quant against its TIC, on both bases, or the
    /// same over every run when <see cref="IsGrouped"/>.</summary>
    public sealed class NativeRunFraction
    {
        public string Run { get; set; } = string.Empty;

        public bool IsGrouped { get; set; }

        /// <summary>The run's total ion current, from <see cref="DeNovoXicExtractor.GetTotalIonCurrentArea"/>
        /// with no trim: the denominator every fraction here is taken over.</summary>
        public double Tic { get; set; }

        public double IdentifiedAreaM0 { get; set; }
        public double IdentifiedEnvelope { get; set; }
        public double DeNovoAreaM0 { get; set; }
        public double DeNovoEnvelope { get; set; }
        public double DeNovoEnvelopeReferenceCalibrated { get; set; }

        public int DeNovoSuppressedCrossSideOverlaps { get; set; }

        /// <summary>Distinct (sequence, charge) peptides on each side, after the collapse. It counts every
        /// peptide the store holds, quantified or not; how many of them were NOT quantified is
        /// <see cref="IdentifiedUnderSampledPeptides"/> / <see cref="DeNovoUnderSampledPeptides"/>, so the
        /// two numbers together say what the totals stand on.</summary>
        public int IdentifiedPeptides { get; set; }
        public int DeNovoPeptides { get; set; }

        /// <summary>
        /// The IDENTIFIED-BUT-NOT-QUANTIFIED ion current of each side: peptides whose clusters were found
        /// and whose point count fell below the minimum-points rule, summed on the peptide grain and in
        /// the same intensity-times-minutes units as the envelope and the TIC.
        ///
        /// IT IS ACCOUNTED FOR, NOT DISCARDED. An excluded cluster that simply vanished would be added to
        /// the unassigned remainder, and the decomposition would silently claim that identified signal was
        /// never detected at all. Zero when nothing was excluded, which is the case on run _01 at the
        /// production minimum of three points.
        /// </summary>
        public double IdentifiedUnderSampled { get; set; }

        public double DeNovoUnderSampled { get; set; }

        public int IdentifiedUnderSampledPeptides { get; set; }
        public int DeNovoUnderSampledPeptides { get; set; }

        /// <summary>True when any part of the under-sampled totals is the single-point ESTIMATE (intensity
        /// times the run's median MS1 scan spacing) rather than a trapezoid. Every surface that shows the
        /// number has to say so.</summary>
        public bool UnderSampledIsEstimate { get; set; }

        /// <summary>
        /// Q6: the run's resolved isotope-series diagnostic area, on the same intensity-vs-retention-time
        /// basis as <see cref="Tic"/> and the peptide XICs. It is independent of peptide assignment and has
        /// no peptide-level FDR calibration. Zero means the diagnostic was not supplied on this object.
        /// </summary>
        public double DetectedPeptideLikeArea { get; set; }

        /// <summary>The number of resolved isotope-series RT features integrated by the diagnostic.</summary>
        public int DetectedEnvelopeCount { get; set; }

        /// <summary>
        /// Q6, split by charge: the MULTIPLY CHARGED part of <see cref="DetectedPeptideLikeArea"/>
        /// (z &gt;= 2). More specific than z=1, but not peptide-FDR calibrated. Zero when no split was supplied.
        /// </summary>
        public double DetectedAreaMultiplyCharged { get; set; }

        /// <summary>
        /// Q6, split by charge: the SINGLY CHARGED part of <see cref="DetectedPeptideLikeArea"/>
        /// (z == 1). WEAKER evidence, since at charge 1 "has a C13 isotope" is true of essentially any
        /// organic molecule. It is NOT a non-peptide block: venom peptides are frequently singly charged
        /// and this dataset carries identified charge-1 peptides.
        /// </summary>
        public double DetectedAreaSinglyCharged { get; set; }

        public int DetectedCountMultiplyCharged { get; set; }
        public int DetectedCountSinglyCharged { get; set; }

        /// <summary>The detected area per charge, indexed BY CHARGE (index 0 unused), or an empty array
        /// when no split was supplied. The CSV writes it out column by column.</summary>
        public double[] DetectedAreaByCharge { get; set; } = Array.Empty<double>();

        /// <summary>The detected feature count per charge, indexed BY CHARGE like
        /// <see cref="DetectedAreaByCharge"/>.</summary>
        public int[] DetectedCountByCharge { get; set; } = Array.Empty<int>();

        public double IdentifiedFractionM0 => Tic > 0 ? IdentifiedAreaM0 / Tic : 0.0;
        public double IdentifiedFractionEnvelope => Tic > 0 ? IdentifiedEnvelope / Tic : 0.0;
        public double DeNovoFractionEnvelope => Tic > 0 ? DeNovoEnvelope / Tic : 0.0;
        public double DeNovoFractionReferenceCalibrated =>
            Tic > 0 ? DeNovoEnvelopeReferenceCalibrated / Tic : 0.0;
        public double DeNovoFractionM0 => Tic > 0 ? DeNovoAreaM0 / Tic : 0.0;

        /// <summary>What DDA and de novo together recover: the identified envelope plus the de novo
        /// envelope, deduped, over the TIC. Below 1 by construction, because no elution peak is counted
        /// twice on either side.</summary>
        public double RecoveredFractionEnvelope => Tic > 0 ? (IdentifiedEnvelope + DeNovoEnvelope) / Tic : 0.0;

        /// <summary>A model-based recovery estimate: measured identified envelope plus de novo M+0
        /// scaled by the identified envelope/M+0 relationship in fixed neutral-mass strata. It is not an
        /// observed de novo envelope or an empirical calibration of identification accuracy.</summary>
        public double RecoveredFractionReferenceCalibrated => Tic > 0
            ? (IdentifiedEnvelope + DeNovoEnvelopeReferenceCalibrated) / Tic : 0.0;

        public double RecoveredFractionM0 => Tic > 0 ? (IdentifiedAreaM0 + DeNovoAreaM0) / Tic : 0.0;

        /// <summary>Q6: area assigned by the resolved isotope-series detector as a fraction of TIC.
        /// This is a detector diagnostic, not a peptide-FDR-calibrated biological fraction. Zero when the
        /// detector has not been run for this row.</summary>
        public double DetectedFractionPeptideLike => Tic > 0 ? DetectedPeptideLikeArea / Tic : 0.0;

        /// <summary>True when a Q6 detector diagnostic has been supplied for this row.</summary>
        public bool HasDetectedPeptideLike => DetectedPeptideLikeArea > 0;

        /// <summary>Q6: the multiply charged (z &gt;= 2) share of TIC assigned by the detector. This is more
        /// specific than the z=1 subset but remains an uncalibrated diagnostic.</summary>
        public double DetectedFractionMultiplyCharged =>
            Tic > 0 ? DetectedAreaMultiplyCharged / Tic : 0.0;

        /// <summary>Q6: the singly charged (z == 1) share of the TIC - the part of the detected level whose
        /// evidence is weaker. Never to be presented as non-peptide.</summary>
        public double DetectedFractionSinglyCharged =>
            Tic > 0 ? DetectedAreaSinglyCharged / Tic : 0.0;

        /// <summary>Q6: the share of the TIC detected at exactly charge <paramref name="charge"/>, or 0
        /// when this row carries no per-charge breakdown or the charge is outside the detector's range.</summary>
        public double DetectedFractionForCharge(int charge) =>
            Tic > 0 && DetectedAreaByCharge != null
                && charge >= 0 && charge < DetectedAreaByCharge.Length
                ? DetectedAreaByCharge[charge] / Tic
                : 0.0;

        /// <summary>True when this row carries the charge split, so a surface can draw the detected level
        /// as two labelled parts instead of one. False on a store written before the split existed, and
        /// then every surface falls back to the single undivided detected block.</summary>
        public bool HasDetectedChargeSplit =>
            HasDetectedPeptideLike && (DetectedAreaMultiplyCharged > 0 || DetectedAreaSinglyCharged > 0);

        /// <summary>The identified-but-not-quantified share of TIC, both sides together, and an ESTIMATE
        /// wherever <see cref="UnderSampledIsEstimate"/> holds.</summary>
        public double UnderSampledFraction =>
            Tic > 0 ? (IdentifiedUnderSampled + DeNovoUnderSampled) / Tic : 0.0;

    }

    /// <summary>
    /// The honest fraction of the total ion current that FamilyQuant's OWN extraction explains, on the
    /// full-envelope basis, and the native envelope three-way partition - all read from the Q2 store
    /// (the <see cref="QuantRow"/> table plus the per-run TIC in the manifest), never from the .plp or
    /// the .xic2.
    ///
    /// THE METHOD, IN ONE SENTENCE A REVIEWER ACCEPTS: each identified peptide is counted once, as its
    /// XIC peak area, expressed as a fraction of the run's total ion current. Concretely, for each run
    /// and side, every identified precursor's stored areas are reduced to ONE area per (peptide
    /// sequence, charge) - the peptide's quantified peak, equivalently its largest-M+0 anchor - those
    /// areas are summed over all peptides, and the sum is divided by the run's
    /// <see cref="DeNovoXicExtractor.GetTotalIonCurrentArea"/> on the identical intensity-vs-retention-
    /// time trapezoid basis.
    ///
    /// WHY THE COLLAPSE IS THE WHOLE FIX. The stored rows are per MS2 ANCHOR. Summing them straight
    /// double-counts one elution peak once per MS2 scan that fired inside it, which on the motivating
    /// export gives 142% of the TIC on M+0 and 322% on the envelope - not a base or units artifact
    /// (numerator and denominator are the same intensity-vs-minutes trapezoid), but uncollapsed intra-
    /// peptide redundancy. The (sequence, charge) collapse removes it exactly: the M+0 sum lands on
    /// 11.89% for run _01, coincident with the .xic2 stored-quant 11.87% the ThreeWayPartition pins,
    /// and the 6-isotope envelope on 27.84% (envelope / M+0 = 2.34). Both are below 100% because no
    /// elution peak is summed twice. (At the earlier 4-isotope ceiling the envelope figure was 26.75%;
    /// the M+0 number did not move, and does not move with the ceiling.)
    /// </summary>
    public static class NativeQuantFractions
    {
        public const string SideIdentified = "identified";
        public const string SideDeNovo = "denovo";

        /// <summary>The token stamped on every derived number's provenance, naming the method so a
        /// reader of the manifest knows the collapse happened. The primary grain is one deterministic
        /// largest-M+0 representative per run, evidence side, canonical PTM-aware peptidoform and charge;
        /// it is not a precursor-mass cluster. Only a NEW computation stamps this: ProjectStore preserves
        /// whatever token a reopened project recorded.</summary>
        public const string Method = "per-peptidoform-charge-maxm0-over-tic-v2";

        /// <summary>One admitted store anchor before the deterministic per-peptidoform collapse.</summary>
        private sealed class DedupAnchor
        {
            public string Run { get; init; } = string.Empty;
            public string Side { get; init; } = string.Empty;
            public string Sequence { get; init; } = string.Empty;
            public int Scan { get; init; }
            public int Charge { get; init; }
            public double PrecursorMz { get; init; }
            public double AreaM0 { get; init; }
            public double EnvelopeSum { get; init; }
            public double UnderSampledArea { get; init; }
            public bool UnderSampledIsEstimate { get; init; }
            public List<DeNovoEvidence> Evidence { get; init; } = new List<DeNovoEvidence>();
        }

        public const string ProvenanceLabel =
            "measured:native-xic max-M+0 area per (sequence,charge,run) / measured:raw-tic";

        /// <summary>Q6: provenance of the non-FDR-calibrated resolved isotope-series diagnostic.</summary>
        public const string DetectedProvenanceLabel =
            "diagnostic:resolved-ms1-isotope-series (not peptide-FDR calibrated) / measured:raw-tic";

        /// <summary>Q6: provenance of the multiply charged (z &gt;= 2) diagnostic subset.</summary>
        public const string DetectedMultiplyChargedProvenanceLabel =
            "diagnostic:resolved-ms1-isotope-series z>=2 - " +
            Ms1EnvelopeDetector.StrongEvidenceLabel;

        /// <summary>Q6: provenance of the singly charged (z == 1) diagnostic subset.</summary>
        public const string DetectedSinglyChargedProvenanceLabel =
            "diagnostic:resolved-ms1-isotope-series z=1 - " +
            Ms1EnvelopeDetector.WeakEvidenceLabel + ". " + Ms1EnvelopeDetector.SinglyChargedCaveat;

        /// <summary>
        /// Collapses stored quant rows to EXACTLY ONE representative peptide per (run, side, canonical
        /// peptidoform, charge), keeping the LARGEST-M+0 anchor and its envelope. For a fixed peptidoform
        /// and charge the precursor neutral mass is determined, so anchors that differ in precursor mass
        /// are the +-1 / +-2 Da monoisotopic-peak misassignments of the SAME peptide, not distinct
        /// peptides; collapsing them to one is exactly what stops one elution peak being counted once per
        /// isotope-offset copy. Fixed C(+57.02) notation is treated as equivalent to the implicit fixed
        /// modification, while genuinely different PTMs stay under a different key and remain distinct.
        /// No-cluster sentinels are dropped: -1 is not an area. The result is returned in a fixed order so
        /// a downstream sum is reproducible.
        /// </summary>
        public static List<NativePeptideQuant> DedupToPeptides(IEnumerable<QuantRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var groups = new Dictionary<string, List<DedupAnchor>>(StringComparer.Ordinal);

            foreach (QuantRow r in rows)
            {
                if (r == null) continue;
                // A no-cluster sentinel carries no ion current; the store already drops it, but the
                // collapse must not be fooled by a row that reached here another way. A row whose M+0 is
                // the UNDER-SAMPLED sentinel is admitted when it carries under-sampled ion current: it is
                // a real detection of a real peptide, and dropping it here is exactly how it would end up
                // in the unassigned remainder.
                if (r.AreaM0 <= DeNovoXicExtractor.NoCluster && !(r.UnderSampledArea > 0)) continue;

                string run = DeNovoReconciler.NormalizeRunName(r.Run ?? string.Empty);
                string side = r.Side ?? string.Empty;
                string seq = r.Sequence ?? string.Empty;

                // Within a side I/L stays distinct. Fixed carbamidomethyl notation is canonicalized, but
                // other PTMs stay in the key and precursor mass is checked below, so different
                // peptidoforms with the same bare backbone are not collapsed.
                string canonical = PeptidoformIdentity.Canonicalize(seq, foldIsoleucineLeucine: false);
                string key = run + "" + side + "" + canonical + "" +
                             r.Charge.ToString(CultureInfo.InvariantCulture);

                // The sentinels are not areas. On every row an existing store holds this is AreaM0 itself,
                // bit for bit, so the collapse and every number it feeds are unchanged.
                double m0 = r.QuantifiedAreaM0;
                double under = r.UnderSampledArea > 0 ? r.UnderSampledArea : 0.0;

                if (!groups.TryGetValue(key, out List<DedupAnchor>? anchors))
                {
                    anchors = new List<DedupAnchor>();
                    groups[key] = anchors;
                }

                anchors.Add(new DedupAnchor
                {
                    Run = run,
                    Side = side,
                    Sequence = seq,
                    Scan = r.Scan,
                    Charge = r.Charge,
                    PrecursorMz = r.PrecursorMz,
                    AreaM0 = m0,
                    EnvelopeSum = r.EnvelopeSum,
                    UnderSampledArea = under,
                    UnderSampledIsEstimate = r.UnderSampledIsEstimate && under > 0,
                    Evidence = r.DeNovoEvidence ?? new List<DeNovoEvidence>(),
                });
            }

            var peptides = new List<NativePeptideQuant>();

            foreach (KeyValuePair<string, List<DedupAnchor>> group in
                     groups.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                // ONE representative per (run, side, canonical peptidoform, charge). The neutral mass is
                // determined by the peptidoform and charge, so anchors that differ in precursor mass are
                // the +-1 / +-2 Da monoisotopic-peak misassignments of the SAME peptide; keeping one per
                // group is what stops the same elution peak being counted once per isotope-offset copy.
                // The representative is chosen by DATA KEYS, not enumeration order, so the collapse is
                // permutation invariant. Keep the largest M+0 anchor; explicit secondary keys make even
                // an exact-area tie deterministic without altering the scientific primary rule: prefer a
                // measured under-sampled value to an estimate on a complete numeric tie, then use stable
                // precursor / sequence / scan provenance.
                DedupAnchor representative = group.Value
                    .OrderByDescending(a => a.AreaM0)
                    .ThenByDescending(a => a.EnvelopeSum)
                    .ThenByDescending(a => a.UnderSampledArea)
                    .ThenBy(a => a.UnderSampledIsEstimate)
                    .ThenBy(a => a.PrecursorMz)
                    .ThenBy(a => a.Sequence, StringComparer.Ordinal)
                    .ThenBy(a => a.Scan)
                    .First();

                peptides.Add(new NativePeptideQuant
                {
                    Run = representative.Run,
                    RepresentativeScan = representative.Scan,
                    Side = representative.Side,
                    Sequence = representative.Sequence,
                    Charge = representative.Charge,
                    PrecursorMz = representative.PrecursorMz,
                    AreaM0 = representative.AreaM0,
                    EnvelopeSum = representative.EnvelopeSum,
                    UnderSampledArea = representative.UnderSampledArea,
                    UnderSampledIsEstimate = representative.UnderSampledIsEstimate,
                    AnchorCount = group.Value.Count,
                    // Union the source claims of every anchor in the group, not only the representative's:
                    // one MS2 scan may carry the PEAKS call and another the DeepNovo call for the very same
                    // collapsed peptide, and the peptide's source membership must see both.
                    DeNovoEvidence = group.Value
                        .SelectMany(a => a.Evidence ?? (IEnumerable<DeNovoEvidence>)Array.Empty<DeNovoEvidence>())
                        .Where(e => e != null)
                        .GroupBy(e => (e.SourceId ?? string.Empty) + "" + (e.Sequence ?? string.Empty),
                            StringComparer.Ordinal)
                        .Select(g => g.First())
                        .ToList(),
                });
            }

            return peptides
                .OrderBy(p => p.Run, StringComparer.Ordinal)
                .ThenBy(p => p.Side, StringComparer.Ordinal)
                .ThenBy(p => p.Sequence, StringComparer.Ordinal)
                .ThenBy(p => p.Charge)
                .ThenBy(p => p.PrecursorMz)
                .ToList();
        }

        /// <summary>
        /// Per-run and grouped native fractions of the TIC, from the store's rows and the manifest's
        /// per-run TIC. A run present in <paramref name="perRunTic"/> but with no rows contributes a
        /// zero numerator (it was extracted and found nothing on that side), never a missing row.
        /// </summary>
        public static List<NativeRunFraction> Compute(
            IEnumerable<QuantRow> rows, IReadOnlyDictionary<string, double> perRunTic)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (perRunTic == null) throw new ArgumentNullException(nameof(perRunTic));

            List<NativePeptideQuant> peptides = DedupToPeptides(rows);
            ReferenceEnvelopeEstimator.Apply(peptides);

            // Prevent cross-side double credit only for the same peptidoform context. I/L is folded and
            // fixed C(+57.02) notation is canonicalized, but non-fixed PTMs remain explicit and precursor
            // neutral mass must agree within 20 ppm, isotope-error-aware (M, M+-1, M+-2). Thus a modified
            // species is not silently erased merely because it has the same bare sequence and charge as a
            // database-search representative, and an isotope-selection difference does not let a duplicate
            // through.
            Dictionary<string, List<NativePeptideQuant>> identifiedByIdentity = peptides
                .Where(p => string.Equals(p.Side, SideIdentified, StringComparison.Ordinal))
                .GroupBy(CrossSideKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            // Every run named by a TIC, plus any run a row mentions (defensive: a row for a run with no
            // recorded TIC would otherwise vanish silently).
            var runs = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var kv in perRunTic) runs.Add(DeNovoReconciler.NormalizeRunName(kv.Key));
            foreach (NativePeptideQuant p in peptides) runs.Add(p.Run);

            var ticByRun = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in perRunTic) ticByRun[DeNovoReconciler.NormalizeRunName(kv.Key)] = kv.Value;

            var result = new List<NativeRunFraction>();

            foreach (string run in runs)
            {
                ticByRun.TryGetValue(run, out double tic);

                var f = new NativeRunFraction { Run = run, Tic = tic };

                foreach (NativePeptideQuant p in peptides.Where(p => p.Run == run))
                {
                    if (string.Equals(p.Side, SideIdentified, StringComparison.Ordinal))
                    {
                        f.IdentifiedAreaM0 += p.AreaM0;
                        f.IdentifiedEnvelope += p.EnvelopeSum;
                        f.IdentifiedPeptides++;
                        if (p.UnderSampledArea > 0)
                        {
                            f.IdentifiedUnderSampled += p.UnderSampledArea;
                            f.IdentifiedUnderSampledPeptides++;
                            f.UnderSampledIsEstimate |= p.UnderSampledIsEstimate;
                        }
                    }
                    else if (string.Equals(p.Side, SideDeNovo, StringComparison.Ordinal))
                    {
                        if (IsCrossSideOverlap(p, identifiedByIdentity))
                        {
                            f.DeNovoSuppressedCrossSideOverlaps++;
                            continue;
                        }
                        f.DeNovoAreaM0 += p.AreaM0;
                        f.DeNovoEnvelope += p.EnvelopeSum;
                        f.DeNovoEnvelopeReferenceCalibrated += p.ReferenceCalibratedEnvelope;
                        f.DeNovoPeptides++;
                        if (p.UnderSampledArea > 0)
                        {
                            f.DeNovoUnderSampled += p.UnderSampledArea;
                            f.DeNovoUnderSampledPeptides++;
                            f.UnderSampledIsEstimate |= p.UnderSampledIsEstimate;
                        }
                    }
                }

                result.Add(f);
            }

            return result;
        }

        /// <summary>Canonical run + I/L-folded peptidoform + charge grouping used before the precursor
        /// mass check. Public so diagnostics and exports can use the exact production suppression rule.</summary>
        public static string CrossSideKey(NativePeptideQuant p) =>
            p.Run + "\u0001" +
            PeptidoformIdentity.Canonicalize(p.Sequence ?? string.Empty, foldIsoleucineLeucine: true) +
            "\u0001" + p.Charge.ToString(CultureInfo.InvariantCulture);

        /// <summary>True only for cross-side calls representing the same canonical peptidoform and a
        /// precursor neutral mass that agrees within the production 20-ppm tolerance, either directly or
        /// after removing a +-1/+-2 monoisotopic-peak selection error. The isotope-error awareness stops an
        /// identified M+0 and a de novo M+1 of the same peptidoform escaping the cross-side double-credit
        /// suppression merely because the two tools reported a different isotope of the one precursor.</summary>
        public static bool IsCrossSideEquivalent(NativePeptideQuant identified, NativePeptideQuant deNovo) =>
            string.Equals(CrossSideKey(identified), CrossSideKey(deNovo), StringComparison.Ordinal)
            && PeptidoformIdentity.PrecursorMassEquivalent(
                identified.MonoisotopicMass, deNovo.MonoisotopicMass, out _,
                PeptidoformIdentity.MaxIsotopeOffset,
                PeptidoformIdentity.DefaultPrecursorTolerancePpm);

        private static bool IsCrossSideOverlap(NativePeptideQuant deNovo,
            IReadOnlyDictionary<string, List<NativePeptideQuant>> identifiedByIdentity)
        {
            return identifiedByIdentity.TryGetValue(CrossSideKey(deNovo), out List<NativePeptideQuant>? candidates)
                && candidates.Any(p => IsCrossSideEquivalent(p, deNovo));
        }

        /// <summary>The three runs as one: numerators and TIC summed, fractions taken from the sums.
        /// The grouped fraction is the pooled numerator over the pooled TIC, not the mean of the
        /// per-run fractions, so a bigger run weighs more - which is what "over the whole experiment"
        /// means.</summary>
        public static NativeRunFraction Grouped(IReadOnlyList<NativeRunFraction> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var g = new NativeRunFraction { Run = ThreeWayPartition.GroupedRunName, IsGrouped = true };

            // The charge split pools like every other numerator. It is accumulated in its own arrays and
            // the pooled TOTAL is then re-formed from the pooled parts, so the identity
            // "detected = z>=2 part + z=1 part" survives pooling instead of drifting by a rounding step.
            // Only when a split is actually present: a store written before the split keeps the total it
            // already had, to the last bit.
            var areaByCharge = Ms1EnvelopeDetectionResult.NewChargeArray<double>();
            var countByCharge = Ms1EnvelopeDetectionResult.NewChargeArray<int>();
            bool anySplit = false;

            foreach (NativeRunFraction r in rows)
            {
                if (r.IsGrouped) continue;
                g.Tic += r.Tic;
                g.IdentifiedAreaM0 += r.IdentifiedAreaM0;
                g.IdentifiedEnvelope += r.IdentifiedEnvelope;
                g.DeNovoAreaM0 += r.DeNovoAreaM0;
                g.DeNovoEnvelope += r.DeNovoEnvelope;
                g.DeNovoEnvelopeReferenceCalibrated += r.DeNovoEnvelopeReferenceCalibrated;
                g.DeNovoSuppressedCrossSideOverlaps += r.DeNovoSuppressedCrossSideOverlaps;
                g.IdentifiedPeptides += r.IdentifiedPeptides;
                g.DeNovoPeptides += r.DeNovoPeptides;
                // The identified-but-not-quantified block pools like the numerators, and its estimate flag
                // pools by OR: one estimated run makes the pooled number an estimate too.
                g.IdentifiedUnderSampled += r.IdentifiedUnderSampled;
                g.DeNovoUnderSampled += r.DeNovoUnderSampled;
                g.IdentifiedUnderSampledPeptides += r.IdentifiedUnderSampledPeptides;
                g.DeNovoUnderSampledPeptides += r.DeNovoUnderSampledPeptides;
                g.UnderSampledIsEstimate |= r.UnderSampledIsEstimate;
                // The detected estimate pools like the numerators: the grouped detected fraction is the
                // summed detected area over the summed TIC, not the mean of the per-run fractions.
                g.DetectedPeptideLikeArea += r.DetectedPeptideLikeArea;
                g.DetectedEnvelopeCount += r.DetectedEnvelopeCount;

                g.DetectedAreaMultiplyCharged += r.DetectedAreaMultiplyCharged;
                g.DetectedAreaSinglyCharged += r.DetectedAreaSinglyCharged;
                g.DetectedCountMultiplyCharged += r.DetectedCountMultiplyCharged;
                g.DetectedCountSinglyCharged += r.DetectedCountSinglyCharged;

                if (r.HasDetectedChargeSplit) anySplit = true;
                if (r.DetectedAreaByCharge != null)
                {
                    for (int z = 0; z < r.DetectedAreaByCharge.Length && z < areaByCharge.Length; z++)
                        areaByCharge[z] += r.DetectedAreaByCharge[z];
                }
                if (r.DetectedCountByCharge != null)
                {
                    for (int z = 0; z < r.DetectedCountByCharge.Length && z < countByCharge.Length; z++)
                        countByCharge[z] += r.DetectedCountByCharge[z];
                }
            }

            if (anySplit)
            {
                g.DetectedAreaByCharge = areaByCharge;
                g.DetectedCountByCharge = countByCharge;
                g.DetectedPeptideLikeArea = g.DetectedAreaSinglyCharged + g.DetectedAreaMultiplyCharged;
                g.DetectedEnvelopeCount = g.DetectedCountSinglyCharged + g.DetectedCountMultiplyCharged;
            }

            return g;
        }

        /// <summary>
        /// The native three-way partitions, M+0 and envelope, built from the store. Both put the
        /// IDENTIFIED (DDA) native quant in the PatternLab slot and the de novo native quant beside it,
        /// TIC measured from the RAW, remainder derived - so unlike F6 (which reads the .xic2 stored
        /// quant for its identified slice) these are FamilyQuant's own numbers on both sides. The
        /// envelope partition is the go-forward one; the M+0 partition is kept as the labelled
        /// comparison against F6.
        /// </summary>
        public static (List<ThreeWayPartitionRow> m0, List<ThreeWayPartitionRow> envelope) BuildNativePartitions(
            IEnumerable<QuantRow> rows, IReadOnlyDictionary<string, double> perRunTic)
        {
            List<NativeRunFraction> fractions = Compute(rows, perRunTic);

            var tic = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            var idM0 = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            var dnM0 = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            var idEnv = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            var dnEnv = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);

            foreach (NativeRunFraction f in fractions)
            {
                if (!(f.Tic > 0)) continue;   // a run with no TIC cannot be a bar; the partition would divide by 0
                tic[f.Run] = PartitionValue.Measured(f.Tic, ThreeWayPartition.SourceRawTic);
                idM0[f.Run] = PartitionValue.Measured(f.IdentifiedAreaM0, ThreeWayPartition.SourceNativeIdentifiedM0);
                dnM0[f.Run] = PartitionValue.Measured(f.DeNovoAreaM0, ThreeWayPartition.SourceNativeDeNovoM0);
                idEnv[f.Run] = PartitionValue.Measured(f.IdentifiedEnvelope, ThreeWayPartition.SourceNativeIdentifiedEnvelope);
                dnEnv[f.Run] = PartitionValue.Measured(f.DeNovoEnvelope, ThreeWayPartition.SourceNativeDeNovoEnvelope);
            }

            List<ThreeWayPartitionRow> m0 = ThreeWayPartition.Build(tic, idM0, dnM0);
            List<ThreeWayPartitionRow> env = ThreeWayPartition.Build(tic, idEnv, dnEnv);
            return (m0, env);
        }

        /// <summary>
        /// The derived per-run and grouped fractions as the serialisable rows the manifest carries, so
        /// the numbers persist in the .fqproj beside the raw per-peptide quant that produced them.
        /// </summary>
        public static List<DerivedRunFraction> ToDerivedRows(
            IEnumerable<QuantRow> rows, IReadOnlyDictionary<string, double> perRunTic)
        {
            List<NativeRunFraction> fractions = Compute(rows, perRunTic);
            var derived = new List<DerivedRunFraction>();
            foreach (NativeRunFraction f in fractions) derived.Add(ToDerived(f));
            if (fractions.Count > 0) derived.Add(ToDerived(Grouped(fractions)));
            return derived;
        }

        private static DerivedRunFraction ToDerived(NativeRunFraction f) => new DerivedRunFraction
        {
            Run = f.Run,
            Grouped = f.IsGrouped,
            Tic = f.Tic,
            IdentifiedAreaM0 = f.IdentifiedAreaM0,
            IdentifiedEnvelope = f.IdentifiedEnvelope,
            DeNovoAreaM0 = f.DeNovoAreaM0,
            DeNovoEnvelope = f.DeNovoEnvelope,
            DeNovoEnvelopeReferenceCalibrated = f.DeNovoEnvelopeReferenceCalibrated,
            IdentifiedPeptides = f.IdentifiedPeptides,
            DeNovoPeptides = f.DeNovoPeptides,
            IdentifiedFractionM0 = f.IdentifiedFractionM0,
            IdentifiedFractionEnvelope = f.IdentifiedFractionEnvelope,
            DeNovoFractionEnvelope = f.DeNovoFractionEnvelope,
            DeNovoFractionReferenceCalibrated = f.DeNovoFractionReferenceCalibrated,
            RecoveredFractionEnvelope = f.RecoveredFractionEnvelope,
            RecoveredFractionReferenceCalibrated = f.RecoveredFractionReferenceCalibrated,
            RecoveredFractionM0 = f.RecoveredFractionM0,
            DeNovoSuppressedCrossSideOverlaps = f.DeNovoSuppressedCrossSideOverlaps,
            // The identified-but-not-quantified block, persisted beside the quantified fractions so a
            // stored quant carries the whole decomposition and not only the part that closed.
            IdentifiedUnderSampled = f.IdentifiedUnderSampled,
            DeNovoUnderSampled = f.DeNovoUnderSampled,
            IdentifiedUnderSampledPeptides = f.IdentifiedUnderSampledPeptides,
            DeNovoUnderSampledPeptides = f.DeNovoUnderSampledPeptides,
            UnderSampledFraction = f.UnderSampledFraction,
            UnderSampledIsEstimate = f.UnderSampledIsEstimate,
            // Q6 detected estimate, if it was injected onto this fraction; the Q3-only path leaves it 0
            // and the compute path fills it into the derived rows directly (see ApplyDetectedToDerived).
            DetectedPeptideLikeArea = f.DetectedPeptideLikeArea,
            DetectedFractionPeptideLike = f.DetectedFractionPeptideLike,
            DetectedEnvelopeCount = f.DetectedEnvelopeCount,
            // The charge split rides with the detected level it decomposes, so a stored project carries
            // the evidence grading and not only the merged total.
            DetectedAreaMultiplyCharged = f.DetectedAreaMultiplyCharged,
            DetectedAreaSinglyCharged = f.DetectedAreaSinglyCharged,
            DetectedFractionMultiplyCharged = f.DetectedFractionMultiplyCharged,
            DetectedFractionSinglyCharged = f.DetectedFractionSinglyCharged,
            DetectedCountMultiplyCharged = f.DetectedCountMultiplyCharged,
            DetectedCountSinglyCharged = f.DetectedCountSinglyCharged,
            DetectedAreaByCharge = f.DetectedAreaByCharge ?? Array.Empty<double>(),
            DetectedCountByCharge = f.DetectedCountByCharge ?? Array.Empty<int>(),
            Provenance = ProvenanceLabel,
        };

        /// <summary>
        /// Computes the derived fractions and writes them into the manifest's derived section, marking
        /// the method and the provenance of each new quantity. Leaves the raw per-peptide quant, the
        /// source of truth, untouched.
        /// </summary>
        public static void PopulateManifest(QuantManifest manifest, IEnumerable<QuantRow> rows)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            // A NEW computation stamps the current method token. A reopened project keeps the token
            // ProjectStore deserialized; nothing on the load path calls this, so a legacy grain is never
            // silently relabelled.
            manifest.DerivedMethod = Method;
            manifest.DerivedFractions = ToDerivedRows(rows, manifest.PerRunTic);

            manifest.Provenance["identified_fraction_m0"] = "derived";
            manifest.Provenance["identified_fraction_envelope"] = "derived";
            manifest.Provenance["denovo_fraction_envelope"] = "derived";
            manifest.Provenance["recovered_fraction_envelope"] = "derived";
            manifest.Provenance["denovo_envelope_reference_calibrated"] =
                ReferenceEnvelopeEstimator.ProvenanceLabel;
            manifest.Provenance["denovo_fraction_reference_calibrated"] = "derived";
            manifest.Provenance["recovered_fraction_reference_calibrated"] =
                "derived:model estimate: identified observed envelope + de novo M0 scaled by identified " +
                "envelope/M0 in fixed mass bins, over measured TIC; not an empirical de novo calibration";
            manifest.Provenance["recovered_fraction_m0"] = "derived:sensitivity reading, not a formal lower bound";
            manifest.Provenance["recovered_fraction_envelope_unadjusted"] =
                "derived:unadjusted six-window diagnostic, not the primary recovered estimate";

            // The under-sampled block is not simply "derived": part of it can be an ESTIMATE. Whether it
            // actually is, on this store, is recorded per row in UnderSampledIsEstimate, and the label
            // here says which of the two the numbers are.
            bool estimated = manifest.DerivedFractions.Any(d => d != null && d.UnderSampledIsEstimate);
            string underProvenance = estimated
                ? "derived:trapezoid areas of refused clusters, ESTIMATE where a cluster had one point " +
                  "(intensity x median MS1 scan spacing)"
                : "derived:trapezoid areas of refused clusters";

            manifest.Provenance["identified_under_sampled"] = underProvenance;
            manifest.Provenance["denovo_under_sampled"] = underProvenance;
            manifest.Provenance["under_sampled_fraction"] = "derived";
            manifest.Provenance["min_points_for_quant"] = manifest.MinPointsForQuant > 0
                ? manifest.MinPointsForQuant.ToString(CultureInfo.InvariantCulture)
                : "not recorded";
        }

        /// <summary>
        /// Q6: writes the detected peptide-like estimate onto the manifest's already-populated derived
        /// section, keyed by run, and records its provenance. The per-run detected AREA is MEASURED from
        /// the MS1 map; the fraction is derived from it and the run's measured TIC. The grouped row (if
        /// present) gets the pooled area and the pooled fraction, so the manifest's aggregate matches the
        /// per-run rows it summarises.
        ///
        /// Call this AFTER <see cref="PopulateManifest"/>: the derived rows must already exist. A run in
        /// the map with no derived row is ignored (it had no TIC and so no bar), and a derived row with no
        /// detected entry keeps its zero, which the chart reads as "not detected".
        /// </summary>
        public static void ApplyDetectedToManifest(
            QuantManifest manifest, IReadOnlyDictionary<string, Ms1EnvelopeDetectionResult> detectedByRun)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (detectedByRun == null) throw new ArgumentNullException(nameof(detectedByRun));

            // Normalize the map's keys once so a "x.raw" detection lands on the "x" derived row.
            var byRun = new Dictionary<string, Ms1EnvelopeDetectionResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in detectedByRun)
                byRun[DeNovoReconciler.NormalizeRunName(kv.Key)] = kv.Value;

            double groupedMulti = 0.0, groupedSingle = 0.0;
            int groupedMultiCount = 0, groupedSingleCount = 0;
            var groupedByCharge = Ms1EnvelopeDetectionResult.NewChargeArray<double>();
            var groupedCountByCharge = Ms1EnvelopeDetectionResult.NewChargeArray<int>();
            bool any = false;

            foreach (DerivedRunFraction d in manifest.DerivedFractions)
            {
                if (d.Grouped) continue;
                if (!byRun.TryGetValue(DeNovoReconciler.NormalizeRunName(d.Run), out Ms1EnvelopeDetectionResult? det)
                    || det == null) continue;

                d.DetectedPeptideLikeArea = det.DetectedPeptideLikeArea;
                d.DetectedEnvelopeCount = det.DetectedEnvelopeCount;
                d.DetectedFractionPeptideLike = d.Tic > 0 ? det.DetectedPeptideLikeArea / d.Tic : 0.0;

                // The charge split, measured beside the total it decomposes.
                d.DetectedAreaMultiplyCharged = det.DetectedAreaMultiplyCharged;
                d.DetectedAreaSinglyCharged = det.DetectedAreaSinglyCharged;
                d.DetectedCountMultiplyCharged = det.DetectedCountMultiplyCharged;
                d.DetectedCountSinglyCharged = det.DetectedCountSinglyCharged;
                d.DetectedAreaByCharge = det.DetectedAreaByCharge ?? Array.Empty<double>();
                d.DetectedCountByCharge = det.DetectedCountByCharge ?? Array.Empty<int>();
                d.DetectedFractionMultiplyCharged = d.Tic > 0 ? det.DetectedAreaMultiplyCharged / d.Tic : 0.0;
                d.DetectedFractionSinglyCharged = d.Tic > 0 ? det.DetectedAreaSinglyCharged / d.Tic : 0.0;

                groupedMulti += det.DetectedAreaMultiplyCharged;
                groupedSingle += det.DetectedAreaSinglyCharged;
                groupedMultiCount += det.DetectedCountMultiplyCharged;
                groupedSingleCount += det.DetectedCountSinglyCharged;
                if (det.DetectedAreaByCharge != null)
                {
                    for (int z = 0; z < det.DetectedAreaByCharge.Length && z < groupedByCharge.Length; z++)
                        groupedByCharge[z] += det.DetectedAreaByCharge[z];
                }
                if (det.DetectedCountByCharge != null)
                {
                    for (int z = 0; z < det.DetectedCountByCharge.Length && z < groupedCountByCharge.Length; z++)
                        groupedCountByCharge[z] += det.DetectedCountByCharge[z];
                }
                any = true;
            }

            DerivedRunFraction? grouped = manifest.DerivedFractions.FirstOrDefault(d => d.Grouped);
            if (grouped != null && any)
            {
                // The pooled total is re-formed from the pooled parts, so the manifest's aggregate row
                // carries the same identity every per-run row does: detected = z>=2 part + z=1 part.
                double groupedArea = groupedSingle + groupedMulti;
                int groupedCount = groupedSingleCount + groupedMultiCount;

                grouped.DetectedPeptideLikeArea = groupedArea;
                grouped.DetectedEnvelopeCount = groupedCount;
                grouped.DetectedFractionPeptideLike = grouped.Tic > 0 ? groupedArea / grouped.Tic : 0.0;

                grouped.DetectedAreaMultiplyCharged = groupedMulti;
                grouped.DetectedAreaSinglyCharged = groupedSingle;
                grouped.DetectedCountMultiplyCharged = groupedMultiCount;
                grouped.DetectedCountSinglyCharged = groupedSingleCount;
                grouped.DetectedAreaByCharge = groupedByCharge;
                grouped.DetectedCountByCharge = groupedCountByCharge;
                grouped.DetectedFractionMultiplyCharged = grouped.Tic > 0 ? groupedMulti / grouped.Tic : 0.0;
                grouped.DetectedFractionSinglyCharged = grouped.Tic > 0 ? groupedSingle / grouped.Tic : 0.0;
            }

            if (any)
            {
                manifest.Provenance["detected_peptidelike_area"] = "measured";
                manifest.Provenance["detected_fraction_peptidelike"] = "derived";
                manifest.Provenance["detected_peptidelike_area_z_ge_2"] = DetectedMultiplyChargedProvenanceLabel;
                manifest.Provenance["detected_peptidelike_area_z_eq_1"] = DetectedSinglyChargedProvenanceLabel;
                manifest.Provenance["detected_fraction_z_ge_2"] = "derived";
                manifest.Provenance["detected_fraction_z_eq_1"] = "derived";
            }
        }

        /// <summary>
        /// Q6: injects the detected peptide-like estimate stored in a manifest's derived section back onto
        /// the in-memory <see cref="NativeRunFraction"/> rows (and the grouped row), by run name, so a
        /// screen that only READS a loaded project can draw the three-way decomposition without re-running
        /// the detector. The detected area is copied as measured; the fraction each row exposes is taken
        /// over its own TIC.
        /// </summary>
        public static void ApplyDetectedToFractions(
            IEnumerable<NativeRunFraction> fractions, NativeRunFraction? grouped,
            IEnumerable<DerivedRunFraction> derived)
        {
            if (fractions == null) throw new ArgumentNullException(nameof(fractions));
            if (derived == null) return;

            var byRun = new Dictionary<string, DerivedRunFraction>(StringComparer.OrdinalIgnoreCase);
            foreach (DerivedRunFraction d in derived)
            {
                if (d == null || d.Grouped) continue;
                byRun[DeNovoReconciler.NormalizeRunName(d.Run)] = d;
            }

            double groupedArea = 0.0;
            int groupedCount = 0;
            double groupedMulti = 0.0, groupedSingle = 0.0;
            int groupedMultiCount = 0, groupedSingleCount = 0;
            var groupedByCharge = Ms1EnvelopeDetectionResult.NewChargeArray<double>();
            var groupedCountByCharge = Ms1EnvelopeDetectionResult.NewChargeArray<int>();
            bool anySplit = false;

            foreach (NativeRunFraction f in fractions)
            {
                if (byRun.TryGetValue(DeNovoReconciler.NormalizeRunName(f.Run), out DerivedRunFraction? d) && d != null)
                {
                    f.DetectedPeptideLikeArea = d.DetectedPeptideLikeArea;
                    f.DetectedEnvelopeCount = d.DetectedEnvelopeCount;
                    groupedArea += d.DetectedPeptideLikeArea;
                    groupedCount += d.DetectedEnvelopeCount;

                    // The charge split rides in with the level it decomposes. A derived row written before
                    // the split existed carries zeros, and HasDetectedChargeSplit then stays false, so the
                    // recovery chart draws the undivided detected block exactly as it did.
                    f.DetectedAreaMultiplyCharged = d.DetectedAreaMultiplyCharged;
                    f.DetectedAreaSinglyCharged = d.DetectedAreaSinglyCharged;
                    f.DetectedCountMultiplyCharged = d.DetectedCountMultiplyCharged;
                    f.DetectedCountSinglyCharged = d.DetectedCountSinglyCharged;
                    f.DetectedAreaByCharge = d.DetectedAreaByCharge ?? Array.Empty<double>();
                    f.DetectedCountByCharge = d.DetectedCountByCharge ?? Array.Empty<int>();

                    groupedMulti += d.DetectedAreaMultiplyCharged;
                    groupedSingle += d.DetectedAreaSinglyCharged;
                    groupedMultiCount += d.DetectedCountMultiplyCharged;
                    groupedSingleCount += d.DetectedCountSinglyCharged;
                    if (f.HasDetectedChargeSplit) anySplit = true;
                    if (d.DetectedAreaByCharge != null)
                    {
                        for (int z = 0; z < d.DetectedAreaByCharge.Length && z < groupedByCharge.Length; z++)
                            groupedByCharge[z] += d.DetectedAreaByCharge[z];
                    }
                    if (d.DetectedCountByCharge != null)
                    {
                        for (int z = 0; z < d.DetectedCountByCharge.Length && z < groupedCountByCharge.Length; z++)
                            groupedCountByCharge[z] += d.DetectedCountByCharge[z];
                    }
                }
            }

            // Only the DETECTED estimate is injected here. The under-sampled block is not: it is computed
            // from the store's own rows by Compute / Grouped, so re-reading it from the manifest could
            // only let a stale derived section override a fresh measurement.
            if (grouped != null)
            {
                grouped.DetectedPeptideLikeArea = groupedArea;
                grouped.DetectedEnvelopeCount = groupedCount;

                grouped.DetectedAreaMultiplyCharged = groupedMulti;
                grouped.DetectedAreaSinglyCharged = groupedSingle;
                grouped.DetectedCountMultiplyCharged = groupedMultiCount;
                grouped.DetectedCountSinglyCharged = groupedSingleCount;

                if (anySplit)
                {
                    grouped.DetectedAreaByCharge = groupedByCharge;
                    grouped.DetectedCountByCharge = groupedCountByCharge;
                    // Re-formed from the parts, so the pooled row carries the same identity the per-run
                    // rows do rather than a total that misses it by a rounding step.
                    grouped.DetectedPeptideLikeArea = groupedSingle + groupedMulti;
                    grouped.DetectedEnvelopeCount = groupedSingleCount + groupedMultiCount;
                }
            }
        }
    }
}
