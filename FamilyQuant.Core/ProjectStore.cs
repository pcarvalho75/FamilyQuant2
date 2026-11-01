using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant
{
    /// <summary>
    /// One per-peptide quant row as it is STORED in a project: which side measured it, which run and
    /// precursor it belongs to, the credited isotope areas and their sum, and the family it was
    /// attributed to (null until an attribution pass sets it).
    ///
    /// This is the raw grain of phase Q2: one row per unique extracted precursor, isotope by isotope,
    /// with nothing derived from it. The fractions, partitions and family rollups that read these
    /// numbers are a later Q-phase and are deliberately NOT computed here.
    /// </summary>
    public sealed class QuantRow
    {
        /// <summary>"identified" for a peptide the database search found, "denovo" for one only PEAKS
        /// sequenced. The two are stored together and told apart by this, never merged.</summary>
        public string Side { get; set; } = string.Empty;

        /// <summary>The run this precursor was extracted from, normalized by
        /// <see cref="DeNovo.DeNovoReconciler.NormalizeRunName"/> so "x.raw" and "x" denote one run.</summary>
        public string Run { get; set; } = string.Empty;

        /// <summary>The MS2 scan that anchored the extraction.</summary>
        public int Scan { get; set; }

        /// <summary>The precursor m/z handed to the extractor: already rounded to
        /// <see cref="DeNovo.DeNovoXicExtractor.MzDecimals"/> decimals, so it is the exact key the
        /// window was selected by.</summary>
        public double PrecursorMz { get; set; }

        public int Charge { get; set; }

        public string Sequence { get; set; } = string.Empty;

        /// <summary>PatternLab/SEPro's primary PSM score (XCorr) for an identified precursor. Null on
        /// de novo rows and on projects written before this field was persisted. This is an
        /// identification score, never a quantitation value.</summary>
        public double? PatternLabXScore { get; set; }

        /// <summary>Source-aware evidence for a de novo precursor. The first item is the deterministic
        /// representative used to label the one XIC; later items are alternate source/sequence claims on
        /// that same precursor. Empty on identified rows and legacy projects.</summary>
        public List<DeNovo.DeNovoEvidence> DeNovoEvidence { get; set; } =
            new List<DeNovo.DeNovoEvidence>();

        public DeNovo.DeNovoEvidence? PrimaryDeNovoEvidence =>
            DeNovoEvidence != null && DeNovoEvidence.Count > 0 ? DeNovoEvidence[0] : null;

        /// <summary>The credited isotope areas, index 0 = M+0, up to the manifest's isotope count. A row
        /// extracted at the go-forward 6-isotope basis carries [M+0 .. M+5]; the
        /// <see cref="DeNovo.DeNovoXicExtractor.NoCluster"/> sentinel survives untouched, because -1 and
        /// 0 mean different things, and so does
        /// <see cref="DeNovo.DeNovoXicExtractor.UnderSampled"/> (-2), which means a third thing again: a
        /// cluster was found and the minimum-points rule declined to quantify it.</summary>
        public double[] IsotopeAreas { get; set; } = Array.Empty<double>();

        /// <summary>
        /// The ion current of this row's credited isotopes that a cluster WAS found for but that the
        /// minimum-points rule refused to quantify, in the same intensity-times-minutes units as the
        /// areas. Zero on every row of a store computed at
        /// <see cref="DeNovo.DeNovoXicExtractor.LegacyMinPointsForQuant"/>, and zero on a store written
        /// before this column existed.
        ///
        /// It is stored, and it keeps its row alive even when every isotope was refused, so that an
        /// identified peptide which was not quantified cannot disappear into the unassigned remainder.
        /// </summary>
        public double UnderSampledArea { get; set; }

        /// <summary>True when any part of <see cref="UnderSampledArea"/> is the single-point ESTIMATE
        /// (intensity times the run's median MS1 scan spacing) rather than a trapezoid. It rides with the
        /// number so nothing downstream can present an estimate as a measurement.</summary>
        public bool UnderSampledIsEstimate { get; set; }

        /// <summary>The sum of the credited isotopes with the no-cluster sentinel clamped, captured at
        /// extraction. Stored rather than recomputed so the table round-trips exactly what was measured,
        /// including for a reader that credits a different isotope count.</summary>
        public double EnvelopeSum { get; set; }

        /// <summary>The family (its UserID) this peptide was attributed to, or null when no attribution
        /// has been made. Q2 leaves it null: attributing a peptide to a family is a Q3 concern and this
        /// column exists so the store can already carry it round-trip.</summary>
        public int? FamilyUserId { get; set; }

        private double AreaAt(int i) => IsotopeAreas != null && i < IsotopeAreas.Length ? IsotopeAreas[i] : 0.0;

        /// <summary>M+0, the monoisotopic area, i.e. <see cref="IsotopeAreas"/>[0]. The number that
        /// reproduces PatternLab's stored quant bit for bit. It carries the sentinels through untouched,
        /// so a reader that means "the area that counts" wants <see cref="QuantifiedAreaM0"/>.</summary>
        public double AreaM0 => AreaAt(0);

        /// <summary>M+0 as a QUANTITY: the area when there is one, and 0 for either sentinel, because
        /// neither -1 (no cluster) nor -2 (under-sampled) is an area and summing them would subtract ion
        /// current that does not exist. On every row a pre-existing store holds, this is AreaM0 itself,
        /// bit for bit.</summary>
        public double QuantifiedAreaM0 => AreaM0 > 0 ? AreaM0 : 0.0;

        /// <summary>
        /// One row per extracted precursor of a run, on one side, built from the extractor's own results.
        /// Failures and rows carrying no ion current at all are dropped: a row that measured nothing is
        /// not a quantity to store, and the -1 sentinel is not an area.
        ///
        /// A row whose M+0 was UNDER-SAMPLED is kept when it carries under-sampled ion current, even
        /// though its quantified M+0 is zero. Dropping it would be the whole defect this accounting
        /// exists to prevent: the peptide was identified and detected, and the current it carries has to
        /// land in the identified-but-not-quantified block rather than migrate into the remainder.
        /// </summary>
        public static List<QuantRow> FromResults(string side, string run,
            IEnumerable<DeNovo.DeNovoXicResult> results, int? familyUserId = null)
        {
            if (side == null) throw new ArgumentNullException(nameof(side));
            if (results == null) throw new ArgumentNullException(nameof(results));

            var rows = new List<QuantRow>();
            foreach (DeNovo.DeNovoXicResult r in results)
            {
                if (r == null || r.Failure != null) continue;
                if (r.AreaM0 <= DeNovo.DeNovoXicExtractor.NoCluster && !(r.UnderSampledArea > 0)) continue;

                rows.Add(new QuantRow
                {
                    Side = side,
                    Run = string.IsNullOrEmpty(run) ? r.RunName : run,
                    Scan = r.ScanNoMS2,
                    PrecursorMz = r.PrecursorMz,
                    Charge = r.Charge,
                    Sequence = r.Tag ?? string.Empty,
                    // A copy, not the extractor's array: the result list is released after this and the
                    // stored row must not alias a buffer that will be collected with it.
                    IsotopeAreas = (double[])(r.IsotopeAreas ?? Array.Empty<double>()).Clone(),
                    EnvelopeSum = r.EnvelopeSum,
                    UnderSampledArea = r.UnderSampledArea,
                    UnderSampledIsEstimate = r.UnderSampledIsEstimate,
                    FamilyUserId = familyUserId,
                });
            }
            return rows;
        }

        /// <summary>Attaches source identity, confidence field/value and input provenance to de novo
        /// quant rows after extraction. Matching uses the exact extraction key, so alternate sequence
        /// claims never create a second XIC.</summary>
        public static void AttachDeNovoEvidence(IEnumerable<QuantRow> quantRows,
                                                 IEnumerable<DeNovo.PeaksDeNovoRow> observations)
        {
            if (quantRows == null) throw new ArgumentNullException(nameof(quantRows));
            if (observations == null) throw new ArgumentNullException(nameof(observations));

            var byKey = observations
                .GroupBy(r => DeNovo.DeNovoXicCache.KeyFor(r.Scan,
                    Math.Round(r.Mz, DeNovo.DeNovoXicExtractor.MzDecimals), r.Charge), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (QuantRow q in quantRows)
            {
                string key = DeNovo.DeNovoXicCache.KeyFor(q.Scan, q.PrecursorMz, q.Charge);
                if (!byKey.TryGetValue(key, out DeNovo.PeaksDeNovoRow? source)) continue;

                IEnumerable<DeNovo.DeNovoEvidence> evidence = source.SourceEvidence != null
                    && source.SourceEvidence.Count > 0
                    ? source.SourceEvidence
                    : new[]
                    {
                        new DeNovo.DeNovoEvidence
                        {
                            SourceId = source.SourceId,
                            SourceDisplayName = source.SourceDisplayName,
                            ConfidenceField = source.ConfidenceField,
                            ConfidenceValue = source.ConfidenceValue,
                            Sequence = source.Peptide,
                            InputFilePath = source.InputFilePath,
                            InputFileSha256 = source.InputFileSha256,
                        },
                    };

                q.DeNovoEvidence = evidence.Select(e => new DeNovo.DeNovoEvidence
                {
                    SourceId = e.SourceId,
                    SourceDisplayName = e.SourceDisplayName,
                    ConfidenceField = e.ConfidenceField,
                    ConfidenceValue = e.ConfidenceValue,
                    Sequence = e.Sequence,
                    InputFilePath = e.InputFilePath,
                    InputFileSha256 = e.InputFileSha256,
                }).ToList();
            }
        }

        /// <summary>Attaches PatternLab's primary PSM score to identified quant rows using the exact
        /// extraction key (scan, rounded precursor m/z and reported charge). Run matching is performed
        /// by the caller, so no score can cross from one acquisition into another.</summary>
        public static void AttachPatternLabScores(IEnumerable<QuantRow> quantRows,
                                                  IEnumerable<DeNovo.PatternLabPsm> psms)
        {
            if (quantRows == null) throw new ArgumentNullException(nameof(quantRows));
            if (psms == null) throw new ArgumentNullException(nameof(psms));

            var byKey = psms
                .GroupBy(p => DeNovo.DeNovoXicCache.KeyFor(p.ScanNumber,
                    Math.Round(p.PrecursorMz, DeNovo.DeNovoXicExtractor.MzDecimals), p.ChargeState),
                    StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.XCorr)
                    .ThenBy(p => p.Sequence, StringComparer.Ordinal).First(), StringComparer.Ordinal);

            foreach (QuantRow q in quantRows)
            {
                if (!string.Equals(q.Side, DeNovo.NativeQuantFractions.SideIdentified,
                    StringComparison.Ordinal)) continue;
                string key = DeNovo.DeNovoXicCache.KeyFor(q.Scan, q.PrecursorMz, q.Charge);
                if (byKey.TryGetValue(key, out DeNovo.PatternLabPsm? psm))
                    q.PatternLabXScore = psm.XCorr;
            }
        }
    }

    /// <summary>
    /// One run's (or the grouped) DERIVED fractions of the total ion current, stored in the manifest so
    /// the paper numbers persist beside the raw per-peptide quant that produced them. Every number here
    /// is DERIVED (see <see cref="QuantManifest.Provenance"/>): it is computed from the measured
    /// per-peptide areas and the measured TIC by the per-peptide-dedup method
    /// (<see cref="DeNovo.NativeQuantFractions"/>), never measured independently. The raw quant rows
    /// remain the source of truth; deleting this section loses nothing that cannot be recomputed.
    /// </summary>
    public sealed class DerivedRunFraction
    {
        /// <summary>The normalized run name, or the grouped-row name when <see cref="Grouped"/>.</summary>
        public string Run { get; set; } = string.Empty;

        /// <summary>True only for the aggregate over all runs.</summary>
        public bool Grouped { get; set; }

        /// <summary>The denominator: this run's total ion current (the sum over runs when grouped).</summary>
        public double Tic { get; set; }

        public double IdentifiedAreaM0 { get; set; }
        public double IdentifiedEnvelope { get; set; }
        public double DeNovoAreaM0 { get; set; }
        public double DeNovoEnvelope { get; set; }
        public double DeNovoEnvelopeReferenceCalibrated { get; set; }

        /// <summary>Distinct (sequence, charge) peptides on each side after the dedup collapse.</summary>
        public int IdentifiedPeptides { get; set; }
        public int DeNovoPeptides { get; set; }

        /// <summary>Identified M+0 over the TIC: the traditional monoisotopic fraction, ~11.9% on run
        /// _01, coincident with the .xic2 stored-quant number.</summary>
        public double IdentifiedFractionM0 { get; set; }

        /// <summary>Identified 6-isotope envelope over the TIC: the go-forward fraction, larger than
        /// M+0 by the ion current the higher isotopes carry.</summary>
        public double IdentifiedFractionEnvelope { get; set; }

        /// <summary>De novo 6-isotope envelope over the TIC.</summary>
        public double DeNovoFractionEnvelope { get; set; }
        public double DeNovoFractionReferenceCalibrated { get; set; }

        /// <summary>
        /// The IDENTIFIED-BUT-NOT-QUANTIFIED ion current on the identified side: peptides whose clusters
        /// were found but carried fewer than <see cref="QuantManifest.MinPointsForQuant"/> points, summed
        /// on the same per-peptide grain and in the same intensity-times-minutes units as the quantified
        /// envelope. Zero when nothing was excluded.
        /// </summary>
        public double IdentifiedUnderSampled { get; set; }

        /// <summary>The same quantity on the de novo side.</summary>
        public double DeNovoUnderSampled { get; set; }

        /// <summary>How many deduped peptides of each side carried under-sampled ion current.</summary>
        public int IdentifiedUnderSampledPeptides { get; set; }

        public int DeNovoUnderSampledPeptides { get; set; }

        /// <summary>(identified + de novo) under-sampled area over TIC.</summary>
        public double UnderSampledFraction { get; set; }

        /// <summary>True when any part of the under-sampled numbers is the single-point ESTIMATE rather
        /// than a trapezoid. It travels with them so a reader of the manifest cannot mistake an estimate
        /// for a measurement.</summary>
        public bool UnderSampledIsEstimate { get; set; }

        /// <summary>What DDA and de novo together recover: (identified + de novo) envelope over the TIC,
        /// below 1 by construction because no elution peak is counted twice.</summary>
        public double RecoveredFractionEnvelope { get; set; }

        /// <summary>Primary estimate: identified observed envelope plus de novo M+0 scaled by the
        /// identified envelope/M+0 relationship in fixed precursor-mass strata, over TIC.</summary>
        public double RecoveredFractionReferenceCalibrated { get; set; }

        public double RecoveredFractionM0 { get; set; }
        public int DeNovoSuppressedCrossSideOverlaps { get; set; }

        /// <summary>Q6: the resolved isotope-series diagnostic area measured from the MS1 map by
        /// <see cref="DeNovo.Ms1EnvelopeDetector"/>. It has no peptide-level FDR calibration. Zero on a
        /// store written before Q6, or where detection did not run.</summary>
        public double DetectedPeptideLikeArea { get; set; }

        /// <summary>Q6: the resolved isotope-series diagnostic area over TIC.</summary>
        public double DetectedFractionPeptideLike { get; set; }

        /// <summary>Q6: how many resolved isotope-series RT features the diagnostic integrated.</summary>
        public int DetectedEnvelopeCount { get; set; }

        /// <summary>
        /// Q6, split by charge: the MULTIPLY CHARGED part of <see cref="DetectedPeptideLikeArea"/>
        /// (z &gt;= 2), measured. More specific than z=1, but not peptide-FDR calibrated. Zero on a store before the
        /// split existed, and then every surface falls back to the undivided detected level.
        /// </summary>
        public double DetectedAreaMultiplyCharged { get; set; }

        /// <summary>
        /// Q6, split by charge: the SINGLY CHARGED part of <see cref="DetectedPeptideLikeArea"/>
        /// (z == 1), MEASURED. WEAKER evidence, since at charge 1 a C13 isotope is true of essentially any
        /// organic molecule. NOT a non-peptide block: venom peptides are frequently singly charged and
        /// this dataset carries identified charge-1 peptides.
        /// </summary>
        public double DetectedAreaSinglyCharged { get; set; }

        /// <summary>Q6: the multiply charged part over the TIC. Derived.</summary>
        public double DetectedFractionMultiplyCharged { get; set; }

        /// <summary>Q6: the singly charged part over the TIC. Derived.</summary>
        public double DetectedFractionSinglyCharged { get; set; }

        public int DetectedCountMultiplyCharged { get; set; }
        public int DetectedCountSinglyCharged { get; set; }

        /// <summary>Q6: the detected area per charge, indexed BY CHARGE (index 0 unused), MEASURED. Null
        /// or empty on a store written before the split existed.</summary>
        public double[] DetectedAreaByCharge { get; set; } = Array.Empty<double>();

        /// <summary>Q6: the detected feature count per charge, indexed like
        /// <see cref="DetectedAreaByCharge"/>.</summary>
        public int[] DetectedCountByCharge { get; set; } = Array.Empty<int>();

        /// <summary>How every number in this row was obtained, machine-readable.</summary>
        public string Provenance { get; set; } = string.Empty;
    }

    /// <summary>
    /// What a project's quant part was computed with and where it came from: the extraction parameters,
    /// each run's total ion current, the app version that wrote it, when it was created and last
    /// updated, and a per-quantity note of whether the number was measured or derived.
    ///
    /// The provenance map is not decoration. A reader that finds envelope_sum marked "derived" knows it
    /// is a sum of the measured isotope areas and not an independent measurement, which is exactly the
    /// distinction a downstream fraction must respect.
    /// </summary>
    public sealed class QuantManifest
    {
        /// <summary>The format version of the container, starting at 1. A reader refuses a version it
        /// does not understand rather than misreading a newer layout as an older one.</summary>
        public int FormatVersion { get; set; } = ProjectStore.CurrentFormatVersion;

        /// <summary>How many isotopes each row credits: the go-forward basis is
        /// <see cref="DeNovo.DeNovoXicExtractor.DefaultEnvelopeIsotopeCount"/> (6). A ceiling, not a
        /// floor: an absent isotope contributes 0.</summary>
        public int IsotopeCount { get; set; }

        /// <summary>
        /// The minimum points per peak the areas were quantified under. A stored quant says WHICH RULE
        /// PRODUCED IT, so a number can never be compared against one taken under a different rule
        /// without the difference being visible. The production default is
        /// <see cref="DeNovo.DeNovoXicExtractor.DefaultMinPointsForQuant"/> (3);
        /// <see cref="DeNovo.DeNovoXicExtractor.LegacyMinPointsForQuant"/> (1) is the legacy XQuant2 rule
        /// the parity gate runs at. A container written before this field existed reads back as 0, which
        /// honestly means "not recorded" rather than pretending to be either rule. Left without an
        /// initializer for exactly that reason, and for the same reason <see cref="IsotopeCount"/> has
        /// none: the writer states the rule, the type does not assume it.
        /// </summary>
        public int MinPointsForQuant { get; set; }

        /// <summary>The extraction window in ppm the areas were measured with. Must match what the
        /// stored quants used, or the M+0 column would not reproduce them.</summary>
        public double Ppm { get; set; }

        /// <summary>The MS1 clustering tolerance handed to the extractor.</summary>
        public int Ms1Tolerance { get; set; }

        /// <summary>Each run's total ion current, keyed by normalized run name: the denominator every
        /// fraction over these areas is taken against. Measured with no trim, the same chromatogram the
        /// rest of the project divides by.</summary>
        public Dictionary<string, double> PerRunTic { get; set; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The FamilyQuant version that wrote this quant part.</summary>
        public string AppVersion { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }

        /// <summary>The method the derived fractions were computed by, or empty when none were. Named
        /// so a reader of the manifest knows the intra-peptide collapse happened before the division.</summary>
        public string DerivedMethod { get; set; } = string.Empty;

        /// <summary>The derived per-run and grouped fractions of the TIC (Q3). Empty on a store that
        /// only measured and did not derive. A container written before this section existed reads back
        /// with an empty list, not a null, so consumers need no special case.</summary>
        public List<DerivedRunFraction> DerivedFractions { get; set; } = new List<DerivedRunFraction>();

        /// <summary>Per-quantity note: "measured" or "derived". Keyed by the column name the quantity
        /// appears under (area_m0, ..., envelope_sum, tic, family_userid).</summary>
        public Dictionary<string, string> Provenance { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The de-novo/search-engine overlap audit measured before XIC extraction: filter settings,
        /// shared file-and-scan counts, co-isolated different precursors, same-precursor meetings,
        /// exact sequence matches after PTM removal, and divergences. Stored so the presentation plot
        /// can be regenerated from a loaded .fqproj without reopening the SEPro package.
        /// Null on projects computed without a PEAKS input or written before this study was added.
        /// </summary>
        public DeNovo.DeNovoOverlapSummary? DeNovoOverlap { get; set; }

        /// <summary>
        /// Spectrum-row distribution of PEAKS ALC scores plus the subset retained by the complete
        /// de novo filter. Persisted so the score/cutoff figure is reproducible from a reopened project
        /// without reopening the original PEAKS CSV directory. Null on older projects or projects
        /// computed without de novo input.
        /// </summary>
        public DeNovo.DeNovoScoreDistribution? DeNovoScoreDistribution { get; set; }

        /// <summary>
        /// Identification-only consolidation of the accepted PEAKS calls before any XIC requirement:
        /// repeated spectra, charge states and run repeats collapsed to PTM-cleaned peptide sequences,
        /// with the reconciliation-carried subset stored separately. Null on older projects or projects
        /// computed without de novo input.
        /// </summary>
        public DeNovo.DeNovoIdentificationSummary? DeNovoIdentification { get; set; }

        /// <summary>
        /// Database-search concordance (PatternLab versus PEAKS db.psms) plus the separately named
        /// PEAKS de-novo-only/database comparison. Stored so the agreement and alternative-hypothesis
        /// plots can be regenerated from the loaded project without reopening the source exports.
        /// </summary>
        public DeNovo.IdentificationConcordanceSummary? IdentificationConcordance { get; set; }

        /// <summary>Every de novo source file used to compute this project, with stable source identity,
        /// its own confidence field/cutoff, canonical path, content fingerprint and row counts.</summary>
        public List<DeNovo.DeNovoInputManifest> DeNovoInputs { get; set; } =
            new List<DeNovo.DeNovoInputManifest>();

        /// <summary>One confidence distribution per source. These records are never pooled: PEAKS ALC and
        /// DeepNovo SCORE remain distinct fields/panels even if the user chose the same numeric cutoff.</summary>
        public List<DeNovo.DeNovoSourceScoreDistribution> DeNovoSourceScoreDistributions { get; set; } =
            new List<DeNovo.DeNovoSourceScoreDistribution>();

        /// <summary>The PEAKS-full versus DeepNovo same-file/same-scan precursor-pair audit, including
        /// its complete inspectable pair table. Persisting the result makes the caller-agreement PNG and
        /// CSV reproducible from a reopened project without silently rereading mutable source exports.</summary>
        public DeNovo.DeNovoAgreementResult? DeNovoCallerAgreement { get; set; }

        /// <summary>Exact PEAKS de-novo-only / DeepNovo sequence-set regions and both source-filter
        /// funnels from this same multi-source computation. Tier A precedes precursor union; tier B
        /// follows source claims attached to union rows carried past PatternLab reconciliation. Null on
        /// a one-source or legacy project.</summary>
        public DeNovo.DeNovoSourceVennSummary? DeNovoSourceVenn { get; set; }

        /// <summary>The accepted-call PEAKS-ALC versus DeepNovo-SCORE scatter. Each persisted point is
        /// one compatible precursor-hypothesis row from <see cref="SpectrumComparisonRows"/> for which
        /// both callers passed their complete source filters. Raw and PTM-cleaned sequences travel with
        /// every point so match/mismatch colours can be regenerated without reopening source files.</summary>
        public DeNovo.DeNovoAcceptedScoreScatter? DeNovoAcceptedScoreScatter { get; set; }

        /// <summary>The complete, provenance-matched source-variant collection used by the presentation.
        /// A normal one-variant project may leave this empty. The five-variant runner writes the same
        /// validated collection into every generated project only after all five current-generation
        /// summaries close, so a reopened project can reproduce the comparison without stale folders.</summary>
        public List<DeNovo.DeNovoVariantSummary> PresentationDeNovoVariants { get; set; } =
            new List<DeNovo.DeNovoVariantSummary>();

        /// <summary>Content fingerprints of scientific inputs read directly by the computation (for
        /// example RAW and SEPro files). De novo-source fingerprints remain in <see cref="DeNovoInputs"/>.
        /// Paths are descriptive keys; comparisons stand on the SHA-256 values.</summary>
        public Dictionary<string, string> ScientificInputFingerprints { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Support, leave-one-representative-out error and pooled-ratio sensitivity for the
        /// identified-reference envelope model used to estimate de novo envelope signal. Persisted so
        /// the defensibility figure and its CSV can be regenerated from a reopened project without
        /// refitting against an unstated input.</summary>
        public DeNovo.ReferenceEnvelopeDiagnostics? ReferenceEnvelopeModelDiagnostics { get; set; }

        /// <summary>The deterministic rule used when several sources claimed one precursor. Empty on a
        /// single-source or legacy project.</summary>
        public string DeNovoUnionResolutionMethod { get; set; } = string.Empty;

        /// <summary>What the spectrum table's family columns were mapped against at Compute (protein set,
        /// identity gate, method). Null on projects written before it was recorded, whose family columns
        /// are then kept as computed rather than re-attributed.</summary>
        public FamilyMappingProvenance? FamilyMapping { get; set; }

        /// <summary>The complete compute-time spectrum evidence table, including accepted source calls
        /// that did not yield a stored XIC and RAW-selected precursor metadata. Version-2 projects keep
        /// this collection in the streaming <c>spectrum_comparison.jsonl.gz</c> entry rather than in
        /// manifest.json; <see cref="ProjectStore.ReadFqproj"/> hydrates it here after reading that entry.
        /// The JSON property remains readable for backward compatibility with version-1 projects that
        /// embedded the rows directly in their manifest. Empty on older projects.</summary>
        public List<DeNovo.SpectrumComparisonRow> SpectrumComparisonRows { get; set; } =
            new List<DeNovo.SpectrumComparisonRow>();

        /// <summary>The exact number of JSON-Lines records expected in the version-2
        /// <c>spectrum_comparison.jsonl.gz</c> sidecar. Null is reserved for version-1 manifests that
        /// predate the sidecar. Recording zero as well as positive counts lets the reader distinguish a
        /// legitimately empty table from a missing or truncated scientific-evidence entry.</summary>
        public int? SpectrumComparisonRowCount { get; set; }

        /// <summary>
        /// Optional identity-sensitivity scan used by the presentation. The normal full-XIC workflow
        /// does not invent it; the USearch/talk workflow records the rows it actually measured. Storing
        /// the curve makes its figure reproducible after the complete scientific project is reopened.
        /// </summary>
        public List<IdentityScanRow> PresentationIdentitySweep { get; set; } = new List<IdentityScanRow>();

        /// <summary>The provenance map every measured extraction carries: the per-isotope areas and the
        /// TIC are measured, the envelope sum is derived from them, and the family attribution is unset
        /// until a later phase makes it.</summary>
        public static Dictionary<string, string> DefaultProvenance() => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["area_m0"] = "measured",
            ["area_m1"] = "measured",
            ["area_m2"] = "measured",
            ["area_m3"] = "measured",
            ["area_m4"] = "measured",
            ["area_m5"] = "measured",
            ["envelope_sum"] = "derived",
            ["tic"] = "measured",
            ["family_userid"] = "unset",
            // Measured where the refused cluster had two or more points (a real trapezoid); an ESTIMATE
            // where it had one and its width had to come from the run's median MS1 scan spacing. The row
            // itself says which, in under_sampled_estimate.
            ["under_sampled"] = "measured-or-estimated",
        };
    }

    /// <summary>
    /// The .fqproj container: a single-file ZIP holding a project's curation, its stored per-peptide
    /// quant table, and the manifest describing how that table was made.
    ///
    /// WHY A CONTAINER. Curation (the families, the locks, the flags) and quantitation (the extracted
    /// areas) used to live apart: the .fcf held the first and the numbers were recomputed on every load.
    /// Phase Q2 makes FamilyQuant its own quantitation engine, so the numbers are now expensive to
    /// produce and worth keeping. The container carries both, keyed together, so a saved project reopens
    /// with the areas it was computed with instead of recomputing them.
    ///
    /// FOUR ENTRIES, EACH A FORMAT A HUMAN OR A SCRIPT CAN READ:
    ///   * curation.json   - the FamilyClusterFile, in exactly the JSON a .fcf holds, so nothing about
    ///                       curation changes and an old .fcf migrates by being read and re-saved here.
    ///   * quant.tsv.gz    - the per-peptide quant table, a GZip-compressed tab-separated file. Readable
    ///                       in pandas with pd.read_csv(path, sep="\t", compression="gzip"). Invariant
    ///                       decimals, round-trippable doubles.
    ///   * manifest.json   - the parameters, the per-run TIC, the app version, the timestamps and the
    ///                       per-quantity provenance.
    ///   * spectrum_comparison.jsonl.gz - the optional compute-time spectrum evidence table, one JSON
    ///                       object per line in a GZip stream. Version 1 embedded this potentially huge
    ///                       table in manifest.json; version 2 streams it separately so opening a project
    ///                       never materialises a multi-hundred-megabyte manifest string.
    ///
    /// WHY TSV.GZ AND NOT PARQUET. Parquet was the preferred table format, but Parquet.Net pulls
    /// IronCompress, which ships native compression binaries in a runtimes/ folder, and Snappier, which
    /// carries a known high-severity advisory. A native dependency and a flagged transitive package are
    /// both unacceptable in a ClickOnce-bundled app, so the documented fallback is used: a GZip TSV that
    /// needs no new dependency, is fully deterministic under InvariantCulture, and pandas reads directly.
    /// </summary>
    public static class ProjectStore
    {
        /// <summary>Version 2 moves SpectrumComparisonRows from manifest.json to the optional streaming
        /// spectrum_comparison.jsonl.gz entry. The version-2 reader remains backward compatible with
        /// version-1 manifests that embedded that collection.</summary>
        public const int CurrentFormatVersion = 2;

        public const string Extension = ".fqproj";

        public const string CurationEntryName = "curation.json";

        /// <summary>The quant entry's name IS its format: a reader that extracts it gets a valid gzip
        /// stream whose decompressed content is the tab-separated table. Named .tsv.gz and not .parquet
        /// on purpose (see the type remarks).</summary>
        public const string QuantEntryName = "quant.tsv.gz";

        public const string ManifestEntryName = "manifest.json";

        /// <summary>The complete spectrum-evidence table, stored as a GZip-compressed JSON Lines stream.
        /// The ZIP entry itself is not compressed again, so extracting it produces a directly readable
        /// .jsonl.gz file. It is optional when a project has no spectrum-comparison rows.</summary>
        public const string SpectrumComparisonEntryName = "spectrum_comparison.jsonl.gz";

        /// <summary>The columns of the quant table, in order. The header written is this one array, and a
        /// reader resolves every column BY NAME from the header it finds, so a table written by an older
        /// build (which had no area_m4/area_m5 and no under-sampled columns) still opens.</summary>
        public static readonly string[] QuantColumns =
        {
            "side", "run", "scan", "precursor_mz", "charge", "sequence",
            "patternlab_xscore", "denovo_evidence_json",
            "area_m0", "area_m1", "area_m2", "area_m3", "area_m4", "area_m5",
            "envelope_sum", "under_sampled", "under_sampled_estimate", "family_userid",
        };

        /// <summary>The columns a version-1 container written before the 6-isotope basis carried. Kept so
        /// the reader can name what it is being lenient about instead of merely being lenient.</summary>
        public static readonly string[] LegacyQuantColumns =
        {
            "side", "run", "scan", "precursor_mz", "charge", "sequence",
            "area_m0", "area_m1", "area_m2", "area_m3", "envelope_sum", "family_userid",
        };

        private const int IsotopeColumnCount = 6;   // area_m0..area_m5

        private static readonly JsonSerializerOptions ManifestJsonOptions = CreateManifestJsonOptions();

        private static readonly JsonSerializerOptions SpectrumComparisonJsonOptions =
            new JsonSerializerOptions();

        /// <summary>
        /// Keeps the legacy SpectrumComparisonRows JSON property DESERIALISABLE while suppressing it on
        /// writes. A JsonIgnore attribute would also prevent old embedded version-1 rows from loading;
        /// ShouldSerialize affects only output, which is exactly the one-way migration needed here.
        /// </summary>
        private static JsonSerializerOptions CreateManifestJsonOptions()
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(typeInfo =>
            {
                if (typeInfo.Type != typeof(QuantManifest)) return;

                JsonPropertyInfo? rowsProperty = typeInfo.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, nameof(QuantManifest.SpectrumComparisonRows),
                        StringComparison.Ordinal));
                if (rowsProperty != null) rowsProperty.ShouldSerialize = (_, _) => false;
            });

            return new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = resolver,
            };
        }

        /// <summary>
        /// Writes a .fqproj: curation, quant table, manifest, and (when present) the streamed spectrum
        /// comparison table into one ZIP. The curation is serialised with the same options a .fcf uses,
        /// so a project saved as a container carries curation byte-identical to the plain file. Written
        /// to a temporary file and moved into place, so a process killed mid-write leaves the previous
        /// project intact rather than a truncated one.
        /// </summary>
        public static void WriteFqproj(string path, FamilyClusterFile fcf,
            IEnumerable<QuantRow> quantRows, QuantManifest manifest, OperationContext? operation = null)
        {
            operation?.Report("project-save", "Preparing project output");
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (fcf == null) throw new ArgumentNullException(nameof(fcf));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            if (manifest.FormatVersion > CurrentFormatVersion)
                throw new InvalidDataException(
                    "Cannot write a project whose FormatVersion is " +
                    manifest.FormatVersion.ToString(CultureInfo.InvariantCulture) +
                    ", because this FamilyQuant build writes at most version " +
                    CurrentFormatVersion.ToString(CultureInfo.InvariantCulture) +
                    ". Open and save it with a build that understands that newer format.");

            // The quant table carries a fixed area_m0..area_m5 column set (IsotopeColumnCount). A manifest
            // claiming more isotopes than there are columns would silently drop the M+6 and higher areas
            // while the pre-truncation EnvelopeSum survives, so the reloaded row's EnvelopeSum would no
            // longer equal the sum of its stored isotopes and the manifest would still claim the higher
            // count. Refuse it at write time rather than persist a lossy, self-inconsistent table.
            if (manifest.IsotopeCount > IsotopeColumnCount)
                throw new ArgumentOutOfRangeException(nameof(manifest),
                    "The quant table holds at most " + IsotopeColumnCount.ToString(CultureInfo.InvariantCulture) +
                    " isotope columns (area_m0..area_m" +
                    (IsotopeColumnCount - 1).ToString(CultureInfo.InvariantCulture) +
                    "), but the manifest requests IsotopeCount=" +
                    manifest.IsotopeCount.ToString(CultureInfo.InvariantCulture) +
                    "; storing it would silently drop the higher isotopes.");

            var rows = quantRows?.ToList() ?? new List<QuantRow>();
            List<DeNovo.SpectrumComparisonRow> spectrumRows = manifest.SpectrumComparisonRows
                ?? new List<DeNovo.SpectrumComparisonRow>();
            if (spectrumRows.Any(row => row == null))
                throw new InvalidDataException(
                    "SpectrumComparisonRows contains a null row. The project was not written because " +
                    "counting that row while omitting it from the sidecar would create an unreadable project.");

            int previousFormatVersion = manifest.FormatVersion;
            int? previousSpectrumCount = manifest.SpectrumComparisonRowCount;
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (manifest.FormatVersion <= CurrentFormatVersion) manifest.FormatVersion = CurrentFormatVersion;
                manifest.SpectrumComparisonRowCount = spectrumRows.Count;
                // A ZIP being built holds an open write handle; dispose it before the move, so the temporary
                // file is complete on disk when it replaces the target.
                using (var fileStream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
                {
                    WriteTextEntry(zip, CurationEntryName, fcf.ToJsonText(), CompressionLevel.Optimal, operation);
                    WriteQuantEntry(zip, rows, operation);
                    if (spectrumRows.Count > 0) WriteSpectrumComparisonEntry(zip, spectrumRows, operation);
                    WriteTextEntry(zip, ManifestEntryName,
                        JsonSerializer.Serialize(manifest, ManifestJsonOptions), CompressionLevel.Optimal, operation);
                }

                operation?.Report("project-save", "Publishing project output", 1, 1);
                void Publish() => File.Move(temp, path, overwrite: true);
                if (operation == null) Publish();
                else operation.Commit(Publish);
            }
            catch
            {
                if (operation != null)
                {
                    manifest.FormatVersion = previousFormatVersion;
                    manifest.SpectrumComparisonRowCount = previousSpectrumCount;
                }
                throw;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        /// <summary>
        /// Reads a .fqproj back into its three parts. The curation goes through
        /// <see cref="FamilyClusterFile.FromJsonText"/>, the same reader a .fcf uses, migration included.
        /// A container missing an entry, or carrying a format version this build does not understand, is
        /// refused rather than half-read.
        /// </summary>
        public static (FamilyClusterFile fcf, List<QuantRow> quant, QuantManifest manifest) ReadFqproj(string path, OperationContext? operation = null)
        {
            operation?.Report("project-read", "Opening project input");
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(fileStream, ZipArchiveMode.Read);

            string curationText = ReadTextEntry(zip, CurationEntryName, operation)
                ?? throw new InvalidDataException(
                    "\"" + path + "\" is not a valid FamilyQuant project: its " + CurationEntryName +
                    " entry is missing.");

            string manifestText = ReadTextEntry(zip, ManifestEntryName, operation)
                ?? throw new InvalidDataException(
                    "\"" + path + "\" is not a valid FamilyQuant project: its " + ManifestEntryName +
                    " entry is missing.");

            FamilyClusterFile fcf = FamilyClusterFile.FromJsonText(curationText);

            QuantManifest manifest = JsonSerializer.Deserialize<QuantManifest>(manifestText,
                ManifestJsonOptions)
                ?? throw new InvalidDataException(
                    "\"" + path + "\" carries an unreadable " + ManifestEntryName + " entry.");

            if (manifest.FormatVersion > CurrentFormatVersion)
                throw new InvalidDataException(
                    "\"" + path + "\" is a version " + manifest.FormatVersion.ToString(CultureInfo.InvariantCulture) +
                    " project, and this build understands up to version " +
                    CurrentFormatVersion.ToString(CultureInfo.InvariantCulture) +
                    ". Update FamilyQuant to open it.");

            // Concordance manifests written before precursor-qualified one-to-one matching carried raw
            // shared-scan counts but left every primary-denominator field at its JSON default of zero.
            // Treating those zeros as a measured 0% agreement would be scientifically false. Refuse the
            // stale audit and require recomputation; a dataset with no shared scans remains valid.
            DeNovo.IdentificationConcordanceSummary? concordance = manifest.IdentificationConcordance;
            if (concordance != null && concordance.SharedDatabaseSearchScanKeys > 0
                && concordance.PrecursorQualifiedDatabaseSearchScanKeys == 0
                && concordance.PrecursorQualifiedDatabaseSearchPairs == 0
                && concordance.PatternLabPrecursorHypothesesInSharedScans == 0
                && concordance.PeaksDatabasePrecursorHypothesesInSharedScans == 0)
                throw new InvalidDataException(
                    "\"" + path + "\" contains a legacy database-search concordance audit with " +
                    concordance.SharedDatabaseSearchScanKeys.ToString(CultureInfo.InvariantCulture) +
                    " shared scan(s) but no precursor-qualified one-to-one PSM fields. Recompute full " +
                    "XIC with the current FamilyQuant build; raw shared scans cannot be used as the " +
                    "sequence-agreement denominator.");

            List<QuantRow> quant = ReadQuantEntry(zip, operation);

            // Version 2 stores this very large table outside manifest.json and records its exact row
            // count. Without that manifest metadata a missing sidecar was indistinguishable from a
            // legitimately empty table; a project could therefore lose its scientific evidence and
            // still open silently. Version 1 remains compatible with its embedded collection, and a
            // transitional v1 sidecar (if present) still takes precedence.
            List<DeNovo.SpectrumComparisonRow>? spectrumRows = ReadSpectrumComparisonEntry(zip, operation);
            if (manifest.FormatVersion >= 2)
            {
                if (!manifest.SpectrumComparisonRowCount.HasValue)
                    throw new InvalidDataException(
                        "\"" + path + "\" is a version-2 project whose " + ManifestEntryName +
                        " does not declare SpectrumComparisonRowCount. The spectrum evidence sidecar " +
                        "cannot be verified; recompute or re-save it with a current FamilyQuant build.");

                int expected = manifest.SpectrumComparisonRowCount.Value;
                if (expected < 0)
                    throw new InvalidDataException(
                        "\"" + path + "\" declares a negative SpectrumComparisonRowCount (" +
                        expected.ToString(CultureInfo.InvariantCulture) + ").");
                if (expected > 0 && spectrumRows == null)
                    throw new InvalidDataException(
                        "\"" + path + "\" declares " +
                        expected.ToString(CultureInfo.InvariantCulture) +
                        " spectrum comparison row(s), but its required " +
                        SpectrumComparisonEntryName + " entry is missing.");

                int actual = spectrumRows?.Count ?? 0;
                if (actual != expected)
                    throw new InvalidDataException(
                        "\"" + path + "\" declares " +
                        expected.ToString(CultureInfo.InvariantCulture) +
                        " spectrum comparison row(s), but " + SpectrumComparisonEntryName +
                        " contains " + actual.ToString(CultureInfo.InvariantCulture) +
                        ". The sidecar is incomplete or does not belong to this manifest.");

                manifest.SpectrumComparisonRows = spectrumRows
                    ?? new List<DeNovo.SpectrumComparisonRow>();
            }
            else
            {
                if (spectrumRows != null) manifest.SpectrumComparisonRows = spectrumRows;
                else manifest.SpectrumComparisonRows ??= new List<DeNovo.SpectrumComparisonRow>();
            }

            operation?.ThrowIfCancellationRequested();
            return (fcf, quant, manifest);
        }

        /// <summary>True when the path names a container rather than a plain .fcf, by extension. The load
        /// flow branches on this so an old .fcf is still read exactly as before.</summary>
        public static bool IsProjectPath(string path) =>
            !string.IsNullOrEmpty(path) &&
            path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

        // ----------------------------------------------------------------- entries

        private static void WriteTextEntry(ZipArchive zip, string name, string content, CompressionLevel level, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = zip.CreateEntry(name, level);
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content);
        }

        private static string? ReadTextEntry(ZipArchive zip, string name, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            ZipArchiveEntry? entry = zip.GetEntry(name);
            if (entry == null) return null;

            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Streams the spectrum comparison table into a gzip JSON-Lines entry. Only one row-sized JSON
        /// string exists at a time, avoiding the former manifest-wide allocation. The enclosing ZIP uses
        /// NoCompression because these bytes already form a gzip file and double compression adds CPU
        /// without making the extracted entry directly useful.
        /// </summary>
        private static void WriteSpectrumComparisonEntry(ZipArchive zip,
            IEnumerable<DeNovo.SpectrumComparisonRow> rows, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = zip.CreateEntry(SpectrumComparisonEntryName,
                CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            using var gzip = new GZipStream(stream, CompressionMode.Compress);
            using var writer = new StreamWriter(gzip, new UTF8Encoding(false));

            foreach (DeNovo.SpectrumComparisonRow row in rows)
            {
                operation?.ThrowIfCancellationRequested();
                if (row == null) continue;
                writer.Write(JsonSerializer.Serialize(row, SpectrumComparisonJsonOptions));
                writer.Write('\n');
            }
        }

        /// <summary>
        /// Reads the optional version-2 spectrum comparison entry one line at a time. Null means there
        /// was no sidecar and lets the caller retain version-1 rows embedded in manifest.json; an empty
        /// list means the sidecar existed and was empty. Malformed rows are rejected with their physical
        /// line number rather than silently producing an incomplete scientific table.
        /// </summary>
        private static List<DeNovo.SpectrumComparisonRow>? ReadSpectrumComparisonEntry(ZipArchive zip, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            ZipArchiveEntry? entry = zip.GetEntry(SpectrumComparisonEntryName);
            if (entry == null) return null;

            var rows = new List<DeNovo.SpectrumComparisonRow>();
            using Stream stream = entry.Open();
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);

            string? line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null)
            {
                operation?.ThrowIfCancellationRequested();
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    DeNovo.SpectrumComparisonRow? row =
                        JsonSerializer.Deserialize<DeNovo.SpectrumComparisonRow>(line,
                            SpectrumComparisonJsonOptions);
                    if (row == null)
                        throw new JsonException("The row deserialised to null.");
                    rows.Add(row);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException(
                        "The " + SpectrumComparisonEntryName + " entry contains invalid JSON at line " +
                        lineNumber.ToString(CultureInfo.InvariantCulture) + ".", ex);
                }
            }

            return rows;
        }

        /// <summary>
        /// Writes the quant table as a gzip TSV into the container. The zip entry itself is stored
        /// uncompressed (NoCompression): its bytes ARE the gzip stream, so a reader that extracts
        /// quant.tsv.gz gets a file pandas reads directly, and double-compressing it would only cost CPU
        /// for no gain. InvariantCulture throughout, doubles in round-trippable "R" form.
        /// </summary>
        private static void WriteQuantEntry(ZipArchive zip, List<QuantRow> rows, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = zip.CreateEntry(QuantEntryName, CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            using var gzip = new GZipStream(stream, CompressionMode.Compress);
            using var writer = new StreamWriter(gzip, new UTF8Encoding(false));

            writer.Write(string.Join("\t", QuantColumns));
            writer.Write('\n');

            foreach (QuantRow r in rows)
            {
                operation?.ThrowIfCancellationRequested();
                writer.Write(TsvField(r.Side)); writer.Write('\t');
                writer.Write(TsvField(r.Run)); writer.Write('\t');
                writer.Write(r.Scan.ToString(CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.PrecursorMz.ToString("R", CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.Charge.ToString(CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(TsvField(r.Sequence)); writer.Write('\t');
                writer.Write(r.PatternLabXScore.HasValue
                    ? r.PatternLabXScore.Value.ToString("R", CultureInfo.InvariantCulture)
                    : string.Empty); writer.Write('\t');
                writer.Write(TsvField(r.DeNovoEvidence != null && r.DeNovoEvidence.Count > 0
                    ? JsonSerializer.Serialize(r.DeNovoEvidence)
                    : string.Empty)); writer.Write('\t');

                for (int i = 0; i < IsotopeColumnCount; i++)
                {
                    writer.Write(IsotopeCell(r.IsotopeAreas, i));
                    writer.Write('\t');
                }

                writer.Write(r.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.UnderSampledArea.ToString("R", CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.UnderSampledIsEstimate ? "1" : "0"); writer.Write('\t');
                writer.Write(r.FamilyUserId.HasValue
                    ? r.FamilyUserId.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
                writer.Write('\n');
            }
        }

        private static List<QuantRow> ReadQuantEntry(ZipArchive zip, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            var rows = new List<QuantRow>();

            ZipArchiveEntry? entry = zip.GetEntry(QuantEntryName);
            if (entry == null) return rows;

            using Stream stream = entry.Open();
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);

            string? header = reader.ReadLine();
            if (header == null) return rows;

            // Columns are resolved BY NAME from the header, not by position, so a container written
            // before the 6-isotope basis (LegacyQuantColumns: no area_m4/area_m5, no under-sampled
            // columns) still opens and simply carries zeros where those columns are absent. The six
            // structural columns are required: without them the file is not this table at all.
            string[] cols = header.Split('\t');
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < cols.Length; i++) index[cols[i].Trim()] = i;

            int Column(string name) => index.TryGetValue(name, out int i) ? i : -1;

            foreach (string required in new[] { "side", "run", "scan", "precursor_mz", "charge", "sequence" })
            {
                if (Column(required) < 0)
                    throw new InvalidDataException(
                        "The quant table's header is missing the \"" + required + "\" column, so this is " +
                        "not a FamilyQuant quant table. Found: " + header);
            }

            int iSide = Column("side"), iRun = Column("run"), iScan = Column("scan");
            int iMz = Column("precursor_mz"), iCharge = Column("charge"), iSeq = Column("sequence");
            int iPatternLabXScore = Column("patternlab_xscore");
            int iDeNovoEvidence = Column("denovo_evidence_json");
            int iEnvelope = Column("envelope_sum"), iFamily = Column("family_userid");
            int iUnder = Column("under_sampled"), iUnderEstimate = Column("under_sampled_estimate");

            var isotopeColumns = new List<int>(IsotopeColumnCount);
            for (int i = 0; i < IsotopeColumnCount; i++)
            {
                int at = Column("area_m" + i.ToString(CultureInfo.InvariantCulture));
                if (at < 0) break;   // the isotope columns are contiguous and trailing
                isotopeColumns.Add(at);
            }

            string Cell(string[] f, int at) => at >= 0 && at < f.Length ? f[at] : string.Empty;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                operation?.ThrowIfCancellationRequested();
                if (line.Length == 0) continue;

                string[] f = line.Split('\t');
                if (f.Length <= iSeq) continue;

                var isotopes = new List<double>(isotopeColumns.Count);
                foreach (int at in isotopeColumns)
                {
                    string cell = Cell(f, at);
                    if (cell.Length == 0) break;   // absent isotopes are trailing; stop at the first
                    isotopes.Add(double.Parse(cell, NumberStyles.Float, CultureInfo.InvariantCulture));
                }

                string familyCell = Cell(f, iFamily);
                string envelopeCell = Cell(f, iEnvelope);
                string underCell = Cell(f, iUnder);

                string evidenceCell = Cell(f, iDeNovoEvidence);
                List<DeNovo.DeNovoEvidence> evidence = evidenceCell.Length == 0
                    ? new List<DeNovo.DeNovoEvidence>()
                    : JsonSerializer.Deserialize<List<DeNovo.DeNovoEvidence>>(evidenceCell)
                        ?? new List<DeNovo.DeNovoEvidence>();

                rows.Add(new QuantRow
                {
                    Side = Cell(f, iSide),
                    Run = Cell(f, iRun),
                    Scan = int.Parse(Cell(f, iScan), NumberStyles.Integer, CultureInfo.InvariantCulture),
                    PrecursorMz = double.Parse(Cell(f, iMz), NumberStyles.Float, CultureInfo.InvariantCulture),
                    Charge = int.Parse(Cell(f, iCharge), NumberStyles.Integer, CultureInfo.InvariantCulture),
                    Sequence = Cell(f, iSeq),
                    PatternLabXScore = Cell(f, iPatternLabXScore).Length == 0
                        ? (double?)null
                        : double.Parse(Cell(f, iPatternLabXScore), NumberStyles.Float,
                            CultureInfo.InvariantCulture),
                    DeNovoEvidence = evidence,
                    IsotopeAreas = isotopes.ToArray(),
                    EnvelopeSum = envelopeCell.Length == 0
                        ? 0.0
                        : double.Parse(envelopeCell, NumberStyles.Float, CultureInfo.InvariantCulture),
                    UnderSampledArea = underCell.Length == 0
                        ? 0.0
                        : double.Parse(underCell, NumberStyles.Float, CultureInfo.InvariantCulture),
                    UnderSampledIsEstimate = Cell(f, iUnderEstimate) == "1",
                    FamilyUserId = familyCell.Length == 0
                        ? (int?)null
                        : int.Parse(familyCell, NumberStyles.Integer, CultureInfo.InvariantCulture),
                });
            }

            return rows;
        }

        private static string IsotopeCell(double[]? isotopes, int i) =>
            isotopes != null && i < isotopes.Length
                ? isotopes[i].ToString("R", CultureInfo.InvariantCulture)
                : string.Empty;

        /// <summary>Makes a value safe inside a tab-separated field: a tab, a newline or a carriage
        /// return in a sequence or a run name would split or truncate the row, so they are replaced with
        /// a space. Sequences and run names never legitimately contain them; this is a guard, not a
        /// transform.</summary>
        private static string TsvField(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOf('\t') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return value;

            var sb = new StringBuilder(value.Length);
            foreach (char c in value) sb.Append(c == '\t' || c == '\n' || c == '\r' ? ' ' : c);
            return sb.ToString();
        }
    }
}
