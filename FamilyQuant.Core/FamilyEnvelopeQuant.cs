using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
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
    public static partial class FamilyEnvelopeQuant
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
            return BuildFromAttributedPeptides(
                AttributePeptides(peptides, identifiedFamilies, deNovoFamilies), describe);
        }

        /// <summary>Rolls up the assignment stage without searching or preparing peptides again.
        /// Accumulation follows prepared-peptide order so all existing export numbers are retained.</summary>
        public static FamilyEnvelopeQuantResult BuildFromAttributedPeptides(
            FamilyPeptideAttribution attribution, Func<int, string>? describe = null)
        {
            if (attribution == null) throw new ArgumentNullException(nameof(attribution));

            var byFamily = new Dictionary<int, FamilyEnvelopeQuantRow>();
            var result = new FamilyEnvelopeQuantResult();

            foreach (FamilyAttributedPeptide assigned in attribution.Peptides)
            {
                NativePeptideQuant p = assigned.Peptide;
                bool identified = string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal);

                // The measured basis is independent of attribution: each deduped peptide counted exactly
                // once whatever it maps to. The family sums below deliberately overlap; this one does not,
                // and it is what a share of the signal has to be divided by.
                if (identified) result.MeasuredIdentifiedEnvelope += p.EnvelopeSum;
                else
                {
                    result.MeasuredDeNovoEnvelope += p.EnvelopeSum;
                    result.MeasuredDeNovoEnvelopeReferenceCalibrated += p.ReferenceCalibratedEnvelope;
                }

                IReadOnlyList<int> families = assigned.FamilyUserIds;

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
                IReadOnlyList<int> distinct = families;
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
            => BuildFromAttribution(rows, attribution, null);

        /// <summary>Builds the same result while reporting the independent source census searches.</summary>
        public static FamilyEnvelopeQuantResult BuildFromAttribution(IEnumerable<QuantRow> rows,
                                                                     PeptideFamilyAttribution attribution,
                                                                     OperationContext? operation)
            => BuildFromAttributionCore(rows, attribution, operation, null, false);

        /// <summary>Builds report data using a separately keyed cache for the source census.</summary>
        public static FamilyEnvelopeQuantResult BuildFromAttribution(IEnumerable<QuantRow> rows,
                                                                     PeptideFamilyAttribution attribution,
                                                                     OperationContext? operation, string? censusCacheDirectory)
            => BuildFromAttributionCore(rows, attribution, operation, censusCacheDirectory, true);

        private static FamilyEnvelopeQuantResult BuildFromAttributionCore(IEnumerable<QuantRow> rows,
            PeptideFamilyAttribution attribution, OperationContext? operation, string? censusCacheDirectory, bool cacheCensus)
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
            FamilySourceSequenceSummary Census() => BuildSourceSequenceSummary(
                rowList, attribution.Database, attribution.TargetItems, attribution.EffectiveOptions,
                attribution.IdentifiedOptions, attribution.FamilyDescriptions, operation);
            result.SourceSequenceSummary = cacheCensus
                ? FamilySourceCensusCache.GetOrCompute(rowList, attribution.Database, attribution.TargetItems,
                    attribution.EffectiveOptions, attribution.IdentifiedOptions, attribution.FamilyDescriptions, censusCacheDirectory, operation, Census)
                : Census();
            return result;
        }

    }
}
