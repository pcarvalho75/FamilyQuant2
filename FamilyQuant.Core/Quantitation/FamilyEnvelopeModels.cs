using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>Which calling source(s) sequenced a de novo peptide, after the search-over-de-novo
    /// (cross-side) suppression that already removed de novo peptides equal to a database identification.
    /// The three real regions are disjoint and mirror the disjoint sequence-set regions the source census
    /// counts; <see cref="UnknownSource"/> is the residual for a surviving de novo peptide that carries no
    /// PEAKS/DeepNovo source claim (a legacy row, or a source outside those two).</summary>
    public enum DeNovoSourceRegion
    {
        PeaksExclusive,
        Shared,
        DeepNovoExclusive,
        UnknownSource,
    }

    /// <summary>One de novo envelope total split into the four <see cref="DeNovoSourceRegion"/> buckets.
    /// The three real regions plus the unknown residual sum to the undivided de novo envelope, so a bar
    /// stacked from them equals the family's whole de novo signal exactly.</summary>
    public sealed class DeNovoEnvelopeBySource
    {
        public double PeaksExclusive { get; set; }
        public double Shared { get; set; }
        public double DeepNovoExclusive { get; set; }
        public double UnknownSource { get; set; }

        /// <summary>The four buckets summed: the whole de novo envelope this split partitions.</summary>
        public double Total => PeaksExclusive + Shared + DeepNovoExclusive + UnknownSource;

        public void Add(DeNovoSourceRegion region, double value)
        {
            switch (region)
            {
                case DeNovoSourceRegion.PeaksExclusive: PeaksExclusive += value; break;
                case DeNovoSourceRegion.Shared: Shared += value; break;
                case DeNovoSourceRegion.DeepNovoExclusive: DeepNovoExclusive += value; break;
                default: UnknownSource += value; break;
            }
        }
    }

    /// <summary>One family's native envelope quantitation: the identified (DDA) native envelope of the
    /// peptides attributed to it, plus the de novo envelope of the peptides homology-mapped to it.</summary>
    public sealed class FamilyEnvelopeQuantRow
    {
        public int FamilyUserId { get; set; }

        public string Description { get; set; } = string.Empty;

        public double IdentifiedM0 { get; set; }
        public double IdentifiedEnvelope { get; set; }
        public double DeNovoM0 { get; set; }
        public double DeNovoEnvelope { get; set; }
        public double DeNovoEnvelopeReferenceCalibrated { get; set; }

        /// <summary>The family's raw de novo envelope (<see cref="DeNovoEnvelope"/>) split by the calling
        /// source: PEAKS de-novo-only exclusive, shared PEAKS/DeepNovo, DeepNovo exclusive, plus the
        /// unknown-source residual. The four buckets sum to <see cref="DeNovoEnvelope"/>.</summary>
        public DeNovoEnvelopeBySource DeNovoEnvelopeRegions { get; } = new DeNovoEnvelopeBySource();

        /// <summary>The same split on the PRIMARY (reference-calibrated) basis the family chart draws and
        /// the recovery numbers use. Its four buckets sum to <see cref="DeNovoEnvelopePrimary"/>.</summary>
        public DeNovoEnvelopeBySource DeNovoEnvelopePrimaryRegions { get; } = new DeNovoEnvelopeBySource();

        /// <summary>Distinct (run, sequence, charge) identified peptides attributed here.</summary>
        public int IdentifiedPeptides { get; set; }

        /// <summary>Distinct (run, sequence, charge) de novo peptides mapped here by homology.</summary>
        public int DeNovoPeptides { get; set; }

        /// <summary>The family's whole native signal: identified plus de novo envelope. The value the
        /// per-family table ranks by.</summary>
        public double TotalEnvelope => IdentifiedEnvelope + DeNovoEnvelope;

        public double TotalEnvelopeReferenceCalibrated =>
            IdentifiedEnvelope + DeNovoEnvelopePrimary;

        public double DeNovoEnvelopePrimary => DeNovoEnvelopeReferenceCalibrated > 0 || DeNovoEnvelope == 0
            ? DeNovoEnvelopeReferenceCalibrated : DeNovoEnvelope;

        /// <summary>
        /// The part of the family's signal carried by peptides that reach THIS family and no other: the
        /// composition basis. A peptide shared by several families is not in any family's unique part; it
        /// goes to <see cref="FamilyEnvelopeQuantResult.SharedTotalEnvelopeReferenceCalibrated"/> instead,
        /// so unique parts, the shared bucket and the unattributed residual add up to the measured total
        /// (the users asked for shared peptides as their own slice, 2026-10-25). The fields above keep the
        /// count-in-every-family reading.
        /// </summary>
        public double UniqueIdentifiedEnvelope { get; set; }
        public double UniqueDeNovoEnvelope { get; set; }
        public double UniqueDeNovoEnvelopeReferenceCalibrated { get; set; }
        public int UniqueIdentifiedPeptides { get; set; }
        public int UniqueDeNovoPeptides { get; set; }

        public double UniqueDeNovoEnvelopePrimary =>
            UniqueDeNovoEnvelopeReferenceCalibrated > 0 || UniqueDeNovoEnvelope == 0
                ? UniqueDeNovoEnvelopeReferenceCalibrated : UniqueDeNovoEnvelope;

        public double UniqueTotalEnvelopeReferenceCalibrated => UniqueIdentifiedEnvelope + UniqueDeNovoEnvelopePrimary;

        /// <summary>The split-equally reading: the unique part plus, for every peptide shared by k
        /// families, 1/k of its signal. Families and the unattributed residual then add up to the
        /// measured total.</summary>
        public double SplitIdentifiedEnvelope { get; set; }
        public double SplitDeNovoEnvelope { get; set; }
        public double SplitDeNovoEnvelopeReferenceCalibrated { get; set; }

        public double SplitDeNovoEnvelopePrimary =>
            SplitDeNovoEnvelopeReferenceCalibrated > 0 || SplitDeNovoEnvelope == 0
                ? SplitDeNovoEnvelopeReferenceCalibrated : SplitDeNovoEnvelope;

        public double SplitTotalEnvelopeReferenceCalibrated => SplitIdentifiedEnvelope + SplitDeNovoEnvelopePrimary;

        /// <summary>The family's signal under a shared-peptide policy: unique part
        /// (<see cref="SharedPeptidePolicy.FamilyUnique"/>, shared peptides reported apart), split
        /// equally, or counted in every family it reaches (overlapping).</summary>
        public double Signal(SharedPeptidePolicy policy) => policy switch
        {
            SharedPeptidePolicy.SplitEqually => SplitTotalEnvelopeReferenceCalibrated,
            SharedPeptidePolicy.CountInEveryFamily => TotalEnvelopeReferenceCalibrated,
            _ => UniqueTotalEnvelopeReferenceCalibrated,
        };
    }

    /// <summary>The per-family table plus what could not be attributed to any family.</summary>
    public sealed class FamilyEnvelopeQuantResult
    {
        public List<FamilyEnvelopeQuantRow> Families { get; } = new List<FamilyEnvelopeQuantRow>();

        /// <summary>Optional source-resolved, pooled distinct-sequence census built from the same
        /// post-filter de novo claims attached to quantified precursors.  These are counts, not signal;
        /// alternate caller sequences do not receive a second XIC.  Family rows may overlap because one
        /// sequence can map to more than one homologous family.</summary>
        public FamilySourceSequenceSummary SourceSequenceSummary { get; set; } =
            new FamilySourceSequenceSummary();

        /// <summary>
        /// The homology identity threshold the attribution stood on, as a fraction (0.80 for the shipped
        /// default), or 0 when the caller supplied its own resolvers and no threshold applies.
        ///
        /// WHY THE TABLE CARRIES IT. The de novo census can be run at a different identity from the one
        /// the figure is built with, so a chart that does not name its threshold can silently disagree
        /// with the census beside it and neither number looks wrong. The chart prints it in its heading:
        /// the figure documents itself.
        /// </summary>
        public double IdentityThreshold { get; set; }

        public double UnattributedIdentifiedEnvelope { get; set; }
        public double UnattributedDeNovoEnvelope { get; set; }
        public double UnattributedDeNovoEnvelopeReferenceCalibrated { get; set; }
        public int UnattributedIdentifiedPeptides { get; set; }
        public int UnattributedDeNovoPeptides { get; set; }

        /// <summary>The unattributed de novo residual split by calling source, on the raw and primary
        /// (reference-calibrated) bases. Buckets sum to <see cref="UnattributedDeNovoEnvelope"/> and
        /// <see cref="UnattributedDeNovoEnvelopePrimary"/> respectively.</summary>
        public DeNovoEnvelopeBySource UnattributedDeNovoEnvelopeRegions { get; } = new DeNovoEnvelopeBySource();
        public DeNovoEnvelopeBySource UnattributedDeNovoEnvelopePrimaryRegions { get; } =
            new DeNovoEnvelopeBySource();

        /// <summary>
        /// The identified side of the MEASURED envelope: every deduped identified peptide counted ONCE,
        /// whatever it was attributed to. This is the basis the recovery numbers and the paper use, and it
        /// is NOT the sum of the family rows, which count a shared peptide in every family it reaches.
        /// </summary>
        public double MeasuredIdentifiedEnvelope { get; set; }

        /// <summary>The de novo side of the same measured envelope, each deduped peptide counted once.</summary>
        public double MeasuredDeNovoEnvelope { get; set; }
        public double MeasuredDeNovoEnvelopeReferenceCalibrated { get; set; }

        /// <summary>
        /// The whole measured envelope: every deduped peptide of both sides, counted once. The honest
        /// denominator for "what share of the signal does this family carry", and the honest total to
        /// print beside the bars.
        /// </summary>
        public double MeasuredTotalEnvelope => MeasuredIdentifiedEnvelope + MeasuredDeNovoEnvelope;

        public double MeasuredTotalEnvelopeReferenceCalibrated =>
            MeasuredIdentifiedEnvelope + MeasuredDeNovoEnvelopePrimary;

        public double MeasuredDeNovoEnvelopePrimary =>
            MeasuredDeNovoEnvelopeReferenceCalibrated > 0 || MeasuredDeNovoEnvelope == 0
                ? MeasuredDeNovoEnvelopeReferenceCalibrated : MeasuredDeNovoEnvelope;

        public double UnattributedDeNovoEnvelopePrimary =>
            UnattributedDeNovoEnvelopeReferenceCalibrated > 0 || UnattributedDeNovoEnvelope == 0
                ? UnattributedDeNovoEnvelopeReferenceCalibrated : UnattributedDeNovoEnvelope;

        public double UnattributedTotalEnvelopeReferenceCalibrated =>
            UnattributedIdentifiedEnvelope + UnattributedDeNovoEnvelopePrimary;

        /// <summary>Peptides that reach two or more families, each counted ONCE here and in no family's
        /// unique part (<see cref="FamilyEnvelopeQuantRow.UniqueTotalEnvelopeReferenceCalibrated"/>).</summary>
        public double SharedIdentifiedEnvelope { get; set; }
        public double SharedDeNovoEnvelope { get; set; }
        public double SharedDeNovoEnvelopeReferenceCalibrated { get; set; }
        public int SharedIdentifiedPeptides { get; set; }
        public int SharedDeNovoPeptides { get; set; }

        public double SharedDeNovoEnvelopePrimary =>
            SharedDeNovoEnvelopeReferenceCalibrated > 0 || SharedDeNovoEnvelope == 0
                ? SharedDeNovoEnvelopeReferenceCalibrated : SharedDeNovoEnvelope;

        public double SharedTotalEnvelopeReferenceCalibrated => SharedIdentifiedEnvelope + SharedDeNovoEnvelopePrimary;

        /// <summary>A family's share of the quantified signal on the composition basis: its unique part
        /// over the measured total. With <see cref="SharedShare"/> and <see cref="UnattributedShare"/>
        /// the family shares add up to 1.</summary>
        public double CompositionShare(FamilyEnvelopeQuantRow family) =>
            family == null || MeasuredTotalEnvelopeReferenceCalibrated <= 0 ? 0
                : family.UniqueTotalEnvelopeReferenceCalibrated / MeasuredTotalEnvelopeReferenceCalibrated;

        public double SharedShare => MeasuredTotalEnvelopeReferenceCalibrated <= 0 ? 0
            : SharedTotalEnvelopeReferenceCalibrated / MeasuredTotalEnvelopeReferenceCalibrated;

        public double UnattributedShare => MeasuredTotalEnvelopeReferenceCalibrated <= 0 ? 0
            : UnattributedTotalEnvelopeReferenceCalibrated / MeasuredTotalEnvelopeReferenceCalibrated;

        /// <summary>
        /// The sum of the family rows plus the unattributed residual.
        ///
        /// NOT the measured envelope: under the count-in-every-family policy a peptide that reaches two
        /// families is in both rows, so this OVERLAPS and is at least
        /// <see cref="MeasuredTotalEnvelope"/>. Use it to check the rollup against the rows; use
        /// <see cref="MeasuredTotalEnvelope"/> as a denominator.
        /// </summary>
        public double TotalEnvelope =>
            Families.Sum(f => f.TotalEnvelope) + UnattributedIdentifiedEnvelope + UnattributedDeNovoEnvelope;
    }

    /// <summary>One family's source-resolved distinct-sequence counts. PEAKS-exclusive, DeepNovo-
    /// exclusive and shared are disjoint exact PTM-cleaned sequence sets (I/L distinct); the two total
    /// columns include the shared set.</summary>
    public sealed class FamilySourceSequenceRow
    {
        public int FamilyUserId { get; set; }
        public string Description { get; set; } = string.Empty;
        public int SearchDistinctSequences { get; set; }
        public int PeaksDeNovoOnlyExclusiveDistinctSequences { get; set; }
        public int DeepNovoExclusiveDistinctSequences { get; set; }
        public int SharedPeaksDeepNovoDistinctSequences { get; set; }
        public int PeaksDeNovoOnlyTotalDistinctSequences { get; set; }
        public int DeepNovoTotalDistinctSequences { get; set; }
        public int DeNovoUnionDistinctSequences { get; set; }
    }

    /// <summary>Provenance and rows for the family source-sequence census. The accepted tier is stated
    /// explicitly because this is downstream of source filtering and precursor XIC admission, whereas
    /// the identification-only consolidation deliberately has no XIC requirement.</summary>
    public sealed class FamilySourceSequenceSummary
    {
        public const string CountingGrain =
            "pooled distinct PTM-cleaned residue sequence; technical run and charge ignored; I/L distinct";
        public const string AcceptedTier =
            "source-filtered claim attached to a quantified de novo precursor, with an exact PTM-cleaned same-run/charge/20-ppm search-equivalent claim suppressed per source; counts are not signal and alternate source claims do not create another XIC";

        public double MappingMinimumIdentity { get; set; }
        public int SearchInputDistinctSequences { get; set; }
        public int PeaksDeNovoOnlyInputDistinctSequences { get; set; }
        public int DeepNovoInputDistinctSequences { get; set; }
        public int UnknownSourceInputDistinctSequences { get; set; }
        public List<FamilySourceSequenceRow> Families { get; } =
            new List<FamilySourceSequenceRow>();
    }

    /// <summary>
    /// What a stored peptide-to-family mapping stood on: the exact protein set searched, the identity gate
    /// and the attribution method. Recorded at Compute, so the spectrum table's family columns are only
    /// ever re-joined onto new family numbers when the proteins, the gate and the method are still the
    /// ones that produced them (a regrouping), and never silently re-attributed.
    /// </summary>
    public sealed class FamilyMappingProvenance
    {
        public string ProteinSetSha256 { get; set; } = string.Empty;
        public double MinIdentity { get; set; }
        public string Method { get; set; } = string.Empty;
        /// <summary>Absent in legacy projects: retain their original OFF semantics on reopen.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public Homology.HomologyScienceOptions? Science { get; set; }
    }

    /// <summary>The homology hits of a quant's peptides against a curation's proteins, independent of the
    /// family numbers (<see cref="FamilyEnvelopeQuant.SearchCuration"/>).</summary>
    public sealed partial class CurationHomologyHits
    {
        public double IdentityThreshold { get; internal set; }
        internal Homology.HomologyOptions EffectiveOptions { get; set; } = new Homology.HomologyOptions();
        /// <summary>The options the IDENTIFIED side was searched with (<see cref="EffectiveOptions"/> plus the
        /// best-score rule when it applied), so every later search of identified sequences follows the same rule.</summary>
        internal Homology.HomologyOptions IdentifiedOptions { get; set; } = new Homology.HomologyOptions();
        internal Homology.HomologyDatabase? Database { get; set; }

        /// <summary>The distinct protein identifiers hit, indexed by the compact hit lists below.</summary>
        internal string[] ProteinIds { get; set; } = Array.Empty<string>();

        /// <summary>Per searched peptide (keyed as the mapper keys an assignment), the indices of every
        /// protein it hit after the target-decoy competition.</summary>
        internal List<(string Key, int[] Proteins)> Identified { get; set; } = new List<(string, int[])>();
        internal List<(string Key, int[] Proteins)> DeNovo { get; set; } = new List<(string, int[])>();
    }

    /// <summary>
    /// The explicit many-to-many peptide-to-family attribution produced by the same homology search as
    /// the family plots. Keys are the normalized sequences consumed by <see cref="FamilyEnvelopeQuant.Build"/>;
    /// values retain every matching curated User family rather than selecting an arbitrary first family.
    /// </summary>
    public sealed class PeptideFamilyAttribution
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? MembershipSha256 { get; internal set; }
        public Dictionary<string, IReadOnlyList<int>> IdentifiedBySequence { get; } =
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<int>> DeNovoBySequence { get; } =
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        public Dictionary<int, string> FamilyDescriptions { get; } = new Dictionary<int, string>();
        public double IdentityThreshold { get; internal set; }

        internal Homology.HomologyDatabase? Database { get; set; }
        internal List<FastaItemExtended> TargetItems { get; set; } = new List<FastaItemExtended>();
        internal Homology.HomologyOptions EffectiveOptions { get; set; } = new Homology.HomologyOptions();
        internal Homology.HomologyOptions IdentifiedOptions { get; set; } = new Homology.HomologyOptions();

        public IReadOnlyList<int> Resolve(string side, string sequence)
        {
            string normalized = DeNovoReconciler.NormalizeSequence(sequence ?? string.Empty);
            Dictionary<string, IReadOnlyList<int>> map = string.Equals(
                side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal)
                    ? IdentifiedBySequence : DeNovoBySequence;
            return normalized.Length > 0 && map.TryGetValue(normalized, out IReadOnlyList<int>? families)
                ? families : Array.Empty<int>();
        }
    }

}
