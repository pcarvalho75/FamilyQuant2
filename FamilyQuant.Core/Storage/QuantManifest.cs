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

        /// <summary>The family world's second pass with the error beside every family (B6). Null on projects that
        /// never ran it, and then left out of manifest.json altogether, so their bytes do not change.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public FamilyWorldSummary? FamilyWorldResult { get; set; }

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
}
