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
    }

    /// <summary>The homology hits of a quant's peptides against a curation's proteins, independent of the
    /// family numbers (<see cref="FamilyEnvelopeQuant.SearchCuration"/>).</summary>
    public sealed class CurationHomologyHits
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

    /// <summary>
    /// Envelope family quantitation from FamilyQuant's OWN extraction (the Q2 store), not the .plp.
    ///
    /// Each family's signal is its peptides' native isotope-envelope areas. The peptides reach a family
    /// two ways, and the caller supplies the mapping so this engine stays testable without a homology
    /// database: the identified (DDA) peptides through <paramref name="identifiedFamilies"/>, the de
    /// novo peptides through <paramref name="deNovoFamilies"/> (in the real pipeline, by
    /// <see cref="Homology.HomologyMapper"/>). A peptide is reduced to ONE deterministic largest-M+0
    /// representative per run, evidence side, canonical PTM-aware peptidoform and charge first - the same
    /// max-M+0 collapse the fraction-of-TIC method uses - so no elution peak is counted twice without
    /// collapsing distinct peptidoforms. A peptide that maps to several families is counted in EACH of them (the
    /// count-in-every-family policy: the family sums may then overlap and exceed the deduped run total,
    /// exactly as the shared-peptide policies in <see cref="FamilyClusterFile"/> document).
    /// </summary>
    public static class FamilyEnvelopeQuant
    {
        /// <summary>The attribution rule <see cref="SearchCuration"/> and <see cref="AttributeHits"/>
        /// implement, recorded with every stored mapping (<see cref="FamilyMappingProvenance.Method"/>).</summary>
        public const string AttributionMethod =
            "identified peptides: the target proteins tied at the best homology score, i.e. the proteins " +
            "containing the peptide (I/L folded); de novo peptides: every target protein passing the identity " +
            "gate; both after target-decoy competition, joined on the User IDs; v3 2026-10-28";

        /// <summary>SHA-256 over the sorted identifier and sequence of every protein of a curation, decoys
        /// included (they are in the searched database). Family numbers are not part of it: a regrouping
        /// keeps the fingerprint, a different database changes it.</summary>
        public static string ProteinSetFingerprint(FamilyClusterFile curation)
        {
            if (curation == null) throw new ArgumentNullException(nameof(curation));
            // Streamed into the hash, line by line, rather than joined into one database-sized string.
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (string line in (curation.MyExtendedFastaItems ?? new List<FastaItemExtended>())
                .Where(i => i != null)
                .Select(i => (i.SequenceIdentifier ?? string.Empty) + "\t" + (i.Sequence ?? string.Empty) + "\n")
                .OrderBy(l => l, StringComparer.Ordinal))
                sha.AppendData(System.Text.Encoding.UTF8.GetBytes(line));
            return Convert.ToHexString(sha.GetHashAndReset());
        }

        /// <summary>True when a protein identifier occurs more than once. The family join keeps the first
        /// occurrence, so the fingerprint, which is order-free, cannot vouch for such a curation.</summary>
        public static bool HasRepeatedProteinIdentifiers(FamilyClusterFile curation) =>
            (curation?.MyExtendedFastaItems ?? new List<FastaItemExtended>())
                .Where(i => i != null && !string.IsNullOrEmpty(i.SequenceIdentifier))
                .GroupBy(i => i.SequenceIdentifier, StringComparer.Ordinal)
                .Any(g => g.Count() > 1);

        /// <summary>The provenance to record for a mapping made on <paramref name="curation"/>, or null when
        /// the curation repeats a protein identifier (then no later re-join can be vouched for).</summary>
        public static FamilyMappingProvenance? ProvenanceFor(FamilyClusterFile curation, double minIdentity) =>
            HasRepeatedProteinIdentifiers(curation) ? null : new FamilyMappingProvenance
            {
                ProteinSetSha256 = ProteinSetFingerprint(curation),
                MinIdentity = minIdentity,
                Method = AttributionMethod,
            };

        /// <summary>
        /// Brings a stored spectrum table's family columns in line with the CURRENT family numbers, but only
        /// as a re-join: when the recorded provenance says the same proteins, gate and method produced
        /// them. Otherwise (a project computed before provenance was recorded, another database, another
        /// method) the stored mapping is kept and each row's status says why it was not refreshed. When the
        /// re-join itself fails, the family columns are emptied and marked unavailable rather than left
        /// looking mapped. Returns a sentence for the user, or null when the rows were refreshed cleanly.
        /// </summary>
        /// <param name="attributionAt">Builds the attribution at a given identity gate on the current curation.</param>
        /// <param name="currentFingerprint">The curation's <see cref="ProteinSetFingerprint"/> when the caller
        /// already holds it (computed once, off the interface thread), or null to compute it here.</param>
        public static string? RefreshSpectrumFamilies(List<DeNovo.SpectrumComparisonRow>? rows,
            FamilyMappingProvenance? provenance, FamilyClusterFile curation,
            Func<double, PeptideFamilyAttribution> attributionAt, string? currentFingerprint = null)
        {
            if (rows == null || rows.Count == 0 || curation == null || attributionAt == null) return null;

            string? keptBecause = provenance == null || string.IsNullOrEmpty(provenance.Method)
                ? "mapped at Compute by an earlier FamilyQuant that did not record what it searched, or on a " +
                  "curation that repeats a protein identifier"
                : !string.Equals(provenance.Method, AttributionMethod, StringComparison.Ordinal)
                    ? "mapped at Compute with an earlier attribution method"
                    : HasRepeatedProteinIdentifiers(curation)
                        ? "the open curation repeats a protein identifier, so the families cannot be re-joined unambiguously"
                        : !string.Equals(provenance.ProteinSetSha256, currentFingerprint ?? ProteinSetFingerprint(curation),
                            StringComparison.OrdinalIgnoreCase)
                            ? "mapped at Compute against a different protein database than the open curation"
                            : null;

            if (keptBecause != null)
            {
                DeNovo.SpectrumComparisonTable.MarkFamilyAssignmentsKept(rows, keptBecause);
                return "The spectrum table's family columns were kept as computed (" + keptBecause + "), so they " +
                       "may not follow regroupings made since. Compute / update full XIC again to align them.";
            }

            try
            {
                DeNovo.SpectrumComparisonTable.RefreshFamilyAssignments(rows, attributionAt(provenance!.MinIdentity));
                return null;
            }
            catch (Exception ex)
            {
                DeNovo.SpectrumComparisonTable.MarkFamilyAssignmentsUnavailable(rows, ex.Message);
                return "The spectrum table's family columns could not be refreshed for the current families and " +
                       "were marked unavailable. Reason given by the program: " + ex.Message;
            }
        }

        /// <summary>
        /// Builds the per-family table. The family resolvers take a normalized peptide sequence and
        /// return the family UserIDs it belongs to; an empty list (or a sequence neither resolver knows)
        /// sends the peptide to the unattributed residual rather than dropping it.
        /// </summary>
        /// <param name="rows">The store's per-peptide quant rows, both sides.</param>
        /// <param name="identifiedFamilies">Normalized identified sequence -> its family UserIDs.</param>
        /// <param name="deNovoFamilies">Normalized de novo sequence -> its family UserIDs.</param>
        /// <param name="describe">Family UserID -> a description for the row, or null for none.</param>
        public static FamilyEnvelopeQuantResult Build(
            IEnumerable<QuantRow> rows,
            Func<string, IReadOnlyList<int>> identifiedFamilies,
            Func<string, IReadOnlyList<int>> deNovoFamilies,
            Func<int, string>? describe = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            return BuildFromPeptides(PreparePeptides(rows), identifiedFamilies, deNovoFamilies, describe);
        }

        /// <summary>The deduplicated, reference-calibrated peptides <see cref="Build"/> works on. A caller
        /// that rebuilds the table many times over the same quant (the curation screen, after every
        /// regrouping) prepares them once and calls <see cref="BuildFromPeptides"/>.</summary>
        public static List<NativePeptideQuant> PreparePeptides(IEnumerable<QuantRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            List<NativePeptideQuant> peptides = NativeQuantFractions.DedupToPeptides(rows);
            ReferenceEnvelopeEstimator.Apply(peptides);
            return peptides;
        }

        /// <summary><see cref="Build"/> over peptides already prepared by <see cref="PreparePeptides"/>.
        /// The peptides are only read.</summary>
        public static FamilyEnvelopeQuantResult BuildFromPeptides(
            IReadOnlyList<NativePeptideQuant> peptides,
            Func<string, IReadOnlyList<int>> identifiedFamilies,
            Func<string, IReadOnlyList<int>> deNovoFamilies,
            Func<int, string>? describe = null)
        {
            if (peptides == null) throw new ArgumentNullException(nameof(peptides));
            if (identifiedFamilies == null) throw new ArgumentNullException(nameof(identifiedFamilies));
            if (deNovoFamilies == null) throw new ArgumentNullException(nameof(deNovoFamilies));

            Dictionary<string, List<NativePeptideQuant>> identifiedByIdentity = peptides
                .Where(p => string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal))
                .GroupBy(NativeQuantFractions.CrossSideKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var byFamily = new Dictionary<int, FamilyEnvelopeQuantRow>();
            var result = new FamilyEnvelopeQuantResult();

            foreach (NativePeptideQuant p in peptides)
            {
                bool identified = string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal);
                if (!identified
                    && identifiedByIdentity.TryGetValue(NativeQuantFractions.CrossSideKey(p),
                        out List<NativePeptideQuant>? candidates)
                    && candidates.Any(q => NativeQuantFractions.IsCrossSideEquivalent(q, p))) continue;
                string normalized = DeNovoReconciler.NormalizeSequence(p.Sequence ?? string.Empty);

                // The measured basis, taken BEFORE any attribution: each deduped peptide counted exactly
                // once whatever it maps to. The family sums below deliberately overlap; this one does not,
                // and it is what a share of the signal has to be divided by.
                if (identified) result.MeasuredIdentifiedEnvelope += p.EnvelopeSum;
                else
                {
                    result.MeasuredDeNovoEnvelope += p.EnvelopeSum;
                    result.MeasuredDeNovoEnvelopeReferenceCalibrated += p.ReferenceCalibratedEnvelope;
                }

                IReadOnlyList<int> families = identified
                    ? identifiedFamilies(normalized)
                    : deNovoFamilies(normalized);

                if (families == null || families.Count == 0)
                {
                    if (identified)
                    {
                        result.UnattributedIdentifiedEnvelope += p.EnvelopeSum;
                        result.UnattributedIdentifiedPeptides++;
                    }
                    else
                    {
                        result.UnattributedDeNovoEnvelope += p.EnvelopeSum;
                        result.UnattributedDeNovoEnvelopeReferenceCalibrated += p.ReferenceCalibratedEnvelope;
                        result.UnattributedDeNovoPeptides++;
                    }
                    continue;
                }

                // Count-in-every-family: one peptide reaching two families adds its area to both.
                // Distinct family ids only, so a peptide reaching a family through several of its
                // proteins is not counted more than once within that one family.
                List<int> distinct = families.Distinct().ToList();
                bool unique = distinct.Count == 1;

                // The composition basis: a peptide reaching several families is counted ONCE, in the
                // shared bucket, and in no family's unique part.
                if (!unique)
                {
                    if (identified)
                    {
                        result.SharedIdentifiedEnvelope += p.EnvelopeSum;
                        result.SharedIdentifiedPeptides++;
                    }
                    else
                    {
                        result.SharedDeNovoEnvelope += p.EnvelopeSum;
                        result.SharedDeNovoEnvelopeReferenceCalibrated += p.ReferenceCalibratedEnvelope;
                        result.SharedDeNovoPeptides++;
                    }
                }

                foreach (int family in distinct)
                {
                    if (!byFamily.TryGetValue(family, out FamilyEnvelopeQuantRow? row))
                    {
                        byFamily[family] = row = new FamilyEnvelopeQuantRow
                        {
                            FamilyUserId = family,
                            Description = describe != null ? (describe(family) ?? string.Empty) : string.Empty,
                        };
                    }

                    double part = 1.0 / distinct.Count;
                    if (identified)
                    {
                        row.IdentifiedM0 += p.AreaM0;
                        row.IdentifiedEnvelope += p.EnvelopeSum;
                        row.IdentifiedPeptides++;
                        row.SplitIdentifiedEnvelope += p.EnvelopeSum * part;
                        if (unique)
                        {
                            row.UniqueIdentifiedEnvelope += p.EnvelopeSum;
                            row.UniqueIdentifiedPeptides++;
                        }
                    }
                    else
                    {
                        row.DeNovoM0 += p.AreaM0;
                        row.DeNovoEnvelope += p.EnvelopeSum;
                        row.DeNovoEnvelopeReferenceCalibrated += p.ReferenceCalibratedEnvelope;
                        row.DeNovoPeptides++;
                        row.SplitDeNovoEnvelope += p.EnvelopeSum * part;
                        row.SplitDeNovoEnvelopeReferenceCalibrated += p.ReferenceCalibratedEnvelope * part;
                        if (unique)
                        {
                            row.UniqueDeNovoEnvelope += p.EnvelopeSum;
                            row.UniqueDeNovoEnvelopeReferenceCalibrated += p.ReferenceCalibratedEnvelope;
                            row.UniqueDeNovoPeptides++;
                        }
                    }
                }
            }

            result.Families.AddRange(byFamily.Values
                .OrderByDescending(f => f.TotalEnvelope)
                .ThenBy(f => f.FamilyUserId));

            return result;
        }

        /// <summary>
        /// The same table, over a curated Family Cluster File, with the peptide-to-family mapping supplied
        /// by the homology search rather than by the caller.
        ///
        /// It composes the pieces that already exist and adds no attribution rule of its own:
        /// <see cref="Homology.HomologyDatabase.Build(System.Collections.Generic.IEnumerable{PatternTools.FastaTools.FastaItem}, int, bool)"/>
        /// to index the curated proteins, <see cref="Homology.HomologyMapper.Search"/> to align every
        /// distinct sequence of a side, <see cref="Homology.HomologyMapper.MapToFamilies"/> to join the
        /// hits onto <see cref="FastaItemExtended.UserID"/> - the WORKING grouping, so a manual split is
        /// honoured - and then <see cref="Build"/> for the arithmetic. This is the very sequence the suite
        /// runs against the real venom file, in one place so a screen and a test cannot drift apart.
        ///
        /// TARGET hits only, exactly as the family census does: the join and the descriptions see the
        /// target members of the curation, never the decoys, so no bar is a reversed sequence and no
        /// decoy signal reaches the totals. The decoys remain in the searched database, so a peptide that
        /// matches only decoys stays unattributed rather than being pushed onto a weak target.
        ///
        /// COST. It is a homology search, not a table read: seconds on a real database, and it re-reads no
        /// RAW and recomputes no quantitation. A caller on the interface thread should say so before
        /// starting it.
        /// </summary>
        /// <param name="rows">The store's per-peptide quant rows, both sides.</param>
        /// <param name="curation">The curated family file whose UserIDs name the families.</param>
        /// <param name="options">Search options, or null for the shipped defaults.</param>
        public static FamilyEnvelopeQuantResult BuildFromCuration(
            IEnumerable<QuantRow> rows,
            FamilyClusterFile curation,
            Homology.HomologyOptions? options = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (curation == null) throw new ArgumentNullException(nameof(curation));

            List<QuantRow> rowList = rows.Where(r => r != null).ToList();
            return BuildFromAttribution(rowList, BuildAttributionFromCuration(rowList, curation, options));
        }

        /// <summary><see cref="BuildFromCuration"/> over an attribution the caller already holds, so a screen
        /// that needs both the table and the attribution (to refresh the spectrum table's family columns)
        /// runs the homology search once.</summary>
        public static FamilyEnvelopeQuantResult BuildFromAttribution(IEnumerable<QuantRow> rows,
                                                                     PeptideFamilyAttribution attribution)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (attribution == null) throw new ArgumentNullException(nameof(attribution));

            List<QuantRow> rowList = rows.Where(r => r != null).ToList();
            IReadOnlyList<int> none = Array.Empty<int>();

            // No proteins to align against: every peptide is honestly unattributed rather than a throw.
            // The threshold is still recorded: the figure must name what it stands on even when it is empty.
            if (attribution.Database == null)
            {
                FamilyEnvelopeQuantResult none2 = Build(rowList, _ => none, _ => none, null);
                none2.IdentityThreshold = attribution.IdentityThreshold;
                return none2;
            }

            FamilyEnvelopeQuantResult result = Build(rowList,
                s => attribution.IdentifiedBySequence.TryGetValue(s, out IReadOnlyList<int>? v) ? v : none,
                s => attribution.DeNovoBySequence.TryGetValue(s, out IReadOnlyList<int>? v) ? v : none,
                id => attribution.FamilyDescriptions.TryGetValue(id, out string? d) ? d : string.Empty);

            // The threshold this attribution stood on, so the chart's heading can state it and a reader can
            // tell whether the figure and a census run at another identity are comparable at all.
            result.IdentityThreshold = attribution.IdentityThreshold;
            result.SourceSequenceSummary = BuildSourceSequenceSummary(
                rowList, attribution.Database, attribution.TargetItems, attribution.EffectiveOptions,
                attribution.IdentifiedOptions, attribution.FamilyDescriptions);
            return result;
        }

        /// <summary>
        /// Runs the production peptide-to-curated-family homology mapping once and exposes the complete
        /// many-to-many assignment for other reproducible outputs such as the spectrum evidence table.
        /// It applies the same target/decoy and UserID rules as <see cref="BuildFromCuration"/>.
        /// </summary>
        public static PeptideFamilyAttribution BuildAttributionFromCuration(
            IEnumerable<QuantRow> rows,
            FamilyClusterFile curation,
            Homology.HomologyOptions? options = null,
            bool identifiedBestScoreOnly = IdentifiedBestScoreOnlyDefault)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (curation == null) throw new ArgumentNullException(nameof(curation));

            return AttributeHits(SearchCuration(rows, curation, options, identifiedBestScoreOnly), curation);
        }

        /// <summary>Whether an IDENTIFIED peptide reaches only the proteins tied at its best score (the
        /// proteins that contain it) rather than every protein past the identity gate. De novo peptides
        /// always keep every qualifying hit: their sequence is a tag, not an established identification.</summary>
        public const bool IdentifiedBestScoreOnlyDefault = true;

        /// <summary>
        /// The expensive half of <see cref="BuildAttributionFromCuration"/>: the homology search of every
        /// distinct peptide of each side against the curation's proteins. It depends on the proteins and
        /// the quant, NOT on the family numbers, so a screen that regroups the same proteins can keep it and
        /// redo only the cheap join (<see cref="AttributeHits"/>) after each regrouping.
        /// </summary>
        public static CurationHomologyHits SearchCuration(IEnumerable<QuantRow> rows, FamilyClusterFile curation,
                                                         Homology.HomologyOptions? options = null,
                                                         bool identifiedBestScoreOnly = IdentifiedBestScoreOnlyDefault)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (curation == null) throw new ArgumentNullException(nameof(curation));

            List<QuantRow> rowList = rows.Where(r => r != null).ToList();
            List<FastaItemExtended> items = (curation.MyExtendedFastaItems ?? new List<FastaItemExtended>()).ToList();

            // No cut at the mapper's top three, on either side: a peptide reaching a second family through a
            // fourth protein is shared, and cutting it there called it family-unique. An IDENTIFIED peptide
            // then keeps only the proteins tied at its best score, which are the proteins containing it (the
            // database search established its sequence, so a homolog that merely resembles it is not
            // evidence; on Ana's curation the every-past-gate rule made 218 of 4,720 identified sequences
            // shared through such homologs). A DE NOVO peptide keeps every protein past the identity gate,
            // because its sequence is a tag that may only resemble the database. The mapper's own default,
            // and every other caller of it, are unchanged.
            Homology.HomologyOptions effective = (options ?? new Homology.HomologyOptions()).Clone();
            effective.TopHitsPerPeptide = int.MaxValue;
            Homology.HomologyOptions identifiedOptions = effective.Clone();
            identifiedOptions.BestScoreOnly = identifiedBestScoreOnly;
            var hits = new CurationHomologyHits
            {
                IdentityThreshold = effective.MinIdentity,
                EffectiveOptions = effective,
                IdentifiedOptions = identifiedOptions,
            };
            if (items.Count == 0) return hits;

            Homology.HomologyDatabase database = Homology.HomologyDatabase.Build(items);
            hits.Database = database;

            // Kept COMPACT: per peptide only the indices of the proteins it hit (all a family join reads),
            // never the hit objects with their matched substrings, which with every qualifying hit kept
            // would grow with peptides x homologs on a redundant database and be walked at every regroup.
            var proteinIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var proteinIds = new List<string>();

            List<(string Key, int[] Proteins)> SearchSide(string side, Homology.HomologyOptions sideOptions)
            {
                List<string> sequences = rowList
                    .Where(r => string.Equals(r.Side, side, StringComparison.Ordinal))
                    .Select(r => DeNovoReconciler.NormalizeSequence(r.Sequence ?? string.Empty))
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var compact = new List<(string, int[])>();
                if (sequences.Count == 0) return compact;

                foreach (Homology.HomologyResult r in Homology.HomologyMapper.Search(sequences, database, sideOptions))
                {
                    int[] proteins = r.Hits.Select(h => h.ProteinId).Distinct(StringComparer.Ordinal).Select(id =>
                    {
                        if (!proteinIndex.TryGetValue(id, out int i))
                        {
                            proteinIndex[id] = i = proteinIds.Count;
                            proteinIds.Add(id);
                        }
                        return i;
                    }).ToArray();
                    // The same key MapToFamilies gives the assignment.
                    compact.Add((r.IsRejected ? r.Peptide : r.NormalizedPeptide, proteins));
                }
                return compact;
            }

            hits.Identified = SearchSide(NativeQuantFractions.SideIdentified, identifiedOptions);
            hits.DeNovo = SearchSide(NativeQuantFractions.SideDeNovo, effective);
            hits.ProteinIds = proteinIds.ToArray();
            return hits;
        }

        /// <summary>
        /// The cheap half: joins the search hits onto the curation's CURRENT User IDs (target members
        /// only). Same result as <see cref="BuildAttributionFromCuration"/> for the same proteins.
        /// </summary>
        public static PeptideFamilyAttribution AttributeHits(CurationHomologyHits hits, FamilyClusterFile curation)
        {
            if (hits == null) throw new ArgumentNullException(nameof(hits));
            if (curation == null) throw new ArgumentNullException(nameof(curation));

            Homology.HomologyOptions effective = hits.EffectiveOptions;
            var attribution = new PeptideFamilyAttribution
            {
                IdentityThreshold = hits.IdentityThreshold,
                EffectiveOptions = effective,
                IdentifiedOptions = hits.IdentifiedOptions,
            };
            List<FastaItemExtended> items = curation.MyExtendedFastaItems ?? new List<FastaItemExtended>();
            if (items.Count == 0 || hits.Database == null) return attribution;

            string decoyLabel = effective.DecoyLabel ?? string.Empty;
            List<FastaItemExtended> targets = decoyLabel.Length == 0
                ? items
                : items.Where(i => i != null
                                   && !(i.SequenceIdentifier ?? string.Empty)
                                       .StartsWith(decoyLabel, StringComparison.Ordinal))
                       .ToList();
            attribution.Database = hits.Database;
            attribution.TargetItems = targets;

            // The join HomologyMapper.MapToFamilies performs, over the compact hits: target members only,
            // the first occurrence of an identifier wins, family ids distinct and ascending.
            var byIdentifier = new Dictionary<string, FastaItemExtended>(StringComparer.Ordinal);
            foreach (FastaItemExtended item in targets)
            {
                if (item == null || string.IsNullOrEmpty(item.SequenceIdentifier)) continue;
                if (!byIdentifier.ContainsKey(item.SequenceIdentifier)) byIdentifier.Add(item.SequenceIdentifier, item);
            }

            void MapSide(List<(string Key, int[] Proteins)> sideHits, Dictionary<string, IReadOnlyList<int>> map)
            {
                foreach ((string key, int[] proteins) in sideHits)
                {
                    var families = new SortedSet<int>();
                    foreach (int p in proteins)
                        if (byIdentifier.TryGetValue(hits.ProteinIds[p], out FastaItemExtended? item)) families.Add(item.UserID);
                    map[key] = families.ToArray();
                }
            }

            MapSide(hits.Identified, attribution.IdentifiedBySequence);
            MapSide(hits.DeNovo, attribution.DeNovoBySequence);

            // The annotation, else the most frequent protein name (FamilyNaming), not the first member's.
            foreach (KeyValuePair<int, string> name in FamilyNaming.Names(targets))
                attribution.FamilyDescriptions[name.Key] = name.Value;
            return attribution;
        }

        private static FamilySourceSequenceSummary BuildSourceSequenceSummary(
            IReadOnlyList<QuantRow> rows,
            Homology.HomologyDatabase database,
            List<FastaItemExtended> targets,
            Homology.HomologyOptions options,
            Homology.HomologyOptions identifiedOptions,
            IReadOnlyDictionary<int, string> descriptions)
        {
            static string Clean(string value) =>
                DeNovoReconciler.NormalizeSequenceIgnoringPtm(value ?? string.Empty,
                    foldIsoleucineLeucine: false);

            var search = new HashSet<string>(StringComparer.Ordinal);
            var peaks = new HashSet<string>(StringComparer.Ordinal);
            var deep = new HashSet<string>(StringComparer.Ordinal);
            var unknown = new HashSet<string>(StringComparer.Ordinal);

            string SearchKey(QuantRow row, string sequence) =>
                DeNovoReconciler.NormalizeRunName(row.Run ?? string.Empty).ToUpperInvariant() + "\u001f" +
                row.Charge.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u001f" + sequence;
            var searchMzByKey = rows
                .Where(row => string.Equals(row.Side, NativeQuantFractions.SideIdentified,
                    StringComparison.Ordinal))
                .Select(row => new { Row = row, Sequence = Clean(row.Sequence) })
                .Where(x => x.Sequence.Length > 0)
                .GroupBy(x => SearchKey(x.Row, x.Sequence), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Row.PrecursorMz)
                    .Where(mz => mz > 0).OrderBy(mz => mz).ToList(), StringComparer.Ordinal);

            bool IsSearchEquivalent(QuantRow deNovoRow, string sequence)
            {
                if (!(deNovoRow.PrecursorMz > 0)) return false;
                if (!searchMzByKey.TryGetValue(SearchKey(deNovoRow, sequence),
                    out List<double>? candidates)) return false;
                return candidates.Any(mz => Math.Abs(deNovoRow.PrecursorMz - mz) / mz * 1e6 <=
                    DeNovoUnionResolver.PrecursorTolerancePpm);
            }

            foreach (QuantRow row in rows)
            {
                if (string.Equals(row.Side, NativeQuantFractions.SideIdentified,
                    StringComparison.Ordinal))
                {
                    string sequence = Clean(row.Sequence);
                    if (sequence.Length > 0) search.Add(sequence);
                    continue;
                }
                if (!string.Equals(row.Side, NativeQuantFractions.SideDeNovo,
                    StringComparison.Ordinal)) continue;

                List<DeNovoEvidence> evidence = row.DeNovoEvidence ?? new List<DeNovoEvidence>();
                if (evidence.Count == 0)
                {
                    string sequence = Clean(row.Sequence);
                    if (sequence.Length > 0) unknown.Add(sequence);
                    continue;
                }

                foreach (DeNovoEvidence claim in evidence)
                {
                    string sequence = Clean(claim.Sequence);
                    if (sequence.Length == 0) continue;
                    // Apply search-overlap suppression to EACH source claim, not only to the union
                    // winner.  Otherwise a DeepNovo alternate equal to search could be counted as added
                    // diversity merely because PEAKS supplied the primary label for the shared XIC.
                    if (IsSearchEquivalent(row, sequence)) continue;
                    if (string.Equals(claim.SourceId, DeNovoSource.DenovoOnly.Id,
                        StringComparison.OrdinalIgnoreCase)) peaks.Add(sequence);
                    else if (string.Equals(claim.SourceId, DeNovoSource.DeepNovo.Id,
                        StringComparison.OrdinalIgnoreCase)) deep.Add(sequence);
                    else unknown.Add(sequence);
                }
            }

            var shared = new HashSet<string>(peaks, StringComparer.Ordinal);
            shared.IntersectWith(deep);
            var peaksOnly = new HashSet<string>(peaks, StringComparer.Ordinal);
            peaksOnly.ExceptWith(deep);
            var deepOnly = new HashSet<string>(deep, StringComparer.Ordinal);
            deepOnly.ExceptWith(peaks);
            var deNovoUnion = new HashSet<string>(peaks, StringComparer.Ordinal);
            deNovoUnion.UnionWith(deep);

            // Each side under the rule the quantitation used for it (SearchCuration): an identified sequence
            // reaches the proteins containing it, a de novo tag every protein past the identity gate. Two
            // maps, because one sequence can be both a search identification and a de novo claim.
            Dictionary<string, IReadOnlyList<int>> FamiliesOf(IEnumerable<string> sequences,
                                                               Homology.HomologyOptions sideOptions) =>
                Homology.HomologyMapper.MapToFamilies(
                        Homology.HomologyMapper.Search(sequences, database, sideOptions), targets)
                    .ToDictionary(a => a.Peptide, a => a.FamilyIds, StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<int>> searchFamilies = FamiliesOf(search, identifiedOptions);
            Dictionary<string, IReadOnlyList<int>> deNovoFamilies = FamiliesOf(deNovoUnion, options);

            var familyIds = new SortedSet<int>(searchFamilies.Values.Concat(deNovoFamilies.Values).SelectMany(ids => ids));
            var summary = new FamilySourceSequenceSummary
            {
                MappingMinimumIdentity = options.MinIdentity,
                SearchInputDistinctSequences = search.Count,
                PeaksDeNovoOnlyInputDistinctSequences = peaks.Count,
                DeepNovoInputDistinctSequences = deep.Count,
                UnknownSourceInputDistinctSequences = unknown.Count,
            };

            int CountIn(Dictionary<string, IReadOnlyList<int>> map, int family, HashSet<string> set) =>
                set.Count(sequence => map.TryGetValue(sequence, out IReadOnlyList<int>? ids) && ids.Contains(family));
            int CountFor(int family, HashSet<string> set) => CountIn(deNovoFamilies, family, set);

            foreach (int family in familyIds)
            {
                summary.Families.Add(new FamilySourceSequenceRow
                {
                    FamilyUserId = family,
                    Description = descriptions.TryGetValue(family, out string? description)
                        ? description : string.Empty,
                    SearchDistinctSequences = CountIn(searchFamilies, family, search),
                    PeaksDeNovoOnlyExclusiveDistinctSequences = CountFor(family, peaksOnly),
                    DeepNovoExclusiveDistinctSequences = CountFor(family, deepOnly),
                    SharedPeaksDeepNovoDistinctSequences = CountFor(family, shared),
                    PeaksDeNovoOnlyTotalDistinctSequences = CountFor(family, peaks),
                    DeepNovoTotalDistinctSequences = CountFor(family, deep),
                    DeNovoUnionDistinctSequences = CountFor(family, deNovoUnion),
                });
            }

            summary.Families.Sort((a, b) =>
            {
                int byUnion = b.DeNovoUnionDistinctSequences.CompareTo(a.DeNovoUnionDistinctSequences);
                if (byUnion != 0) return byUnion;
                int bySearch = b.SearchDistinctSequences.CompareTo(a.SearchDistinctSequences);
                return bySearch != 0 ? bySearch : a.FamilyUserId.CompareTo(b.FamilyUserId);
            });
            return summary;
        }
    }
}
