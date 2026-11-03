using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>One prepared peptide and every distinct current User family it reaches. The peptide
    /// keeps its run, charge, representative anchor, source claims and calibrated envelope.</summary>
    public sealed class FamilyAttributedPeptide
    {
        internal FamilyAttributedPeptide(NativePeptideQuant peptide, IReadOnlyList<int> families)
        {
            Peptide = peptide;
            FamilyUserIds = Array.AsReadOnly(families.Distinct().ToArray());
        }

        public NativePeptideQuant Peptide { get; }
        public IReadOnlyList<int> FamilyUserIds { get; }
    }

    /// <summary>The assignment stage output, in prepared-peptide order, after identified-equivalent
    /// de novo suppression. A second attribution pass consumes only UnattributedDeNovoPeptides and
    /// records its separate family-world results without changing the first-pass family table.</summary>
    public sealed class FamilyPeptideAttribution
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? MembershipSha256 { get; internal set; }
        internal FamilyPeptideAttribution(List<FamilyAttributedPeptide> peptides)
        {
            Peptides = peptides.AsReadOnly();
            UnattributedDeNovoPeptides = peptides
                .Where(p => string.Equals(p.Peptide.Side, NativeQuantFractions.SideDeNovo,
                    StringComparison.Ordinal) && p.FamilyUserIds.Count == 0)
                .Select(p => p.Peptide).ToList().AsReadOnly();
        }

        public IReadOnlyList<FamilyAttributedPeptide> Peptides { get; }
        public IReadOnlyList<NativePeptideQuant> UnattributedDeNovoPeptides { get; }
    }

    public static partial class FamilyEnvelopeQuant
    {
        /// <summary>Assigns prepared, calibrated peptides to the current first-pass families. This is
        /// the second-pass seam: its residual excludes identified-equivalent de novo peptides, mapped
        /// de novo peptides and all identified peptides. Inputs and quantitation values are only read.</summary>
        public static FamilyPeptideAttribution AttributePeptides(
            IReadOnlyList<NativePeptideQuant> peptides, PeptideFamilyAttribution attribution)
        {
            if (attribution == null) throw new ArgumentNullException(nameof(attribution));
            FamilyPeptideAttribution assigned = AttributePeptides(peptides,
                s => attribution.IdentifiedBySequence.TryGetValue(s, out IReadOnlyList<int>? families)
                    ? families : Array.Empty<int>(),
                s => attribution.DeNovoBySequence.TryGetValue(s, out IReadOnlyList<int>? families)
                    ? families : Array.Empty<int>());
            assigned.MembershipSha256 = attribution.MembershipSha256;
            return assigned;
        }

        internal static FamilyPeptideAttribution AttributePeptides(
            IReadOnlyList<NativePeptideQuant> peptides,
            Func<string, IReadOnlyList<int>> identifiedFamilies,
            Func<string, IReadOnlyList<int>> deNovoFamilies)
        {
            if (peptides == null) throw new ArgumentNullException(nameof(peptides));
            if (identifiedFamilies == null) throw new ArgumentNullException(nameof(identifiedFamilies));
            if (deNovoFamilies == null) throw new ArgumentNullException(nameof(deNovoFamilies));

            Dictionary<string, List<NativePeptideQuant>> identifiedByIdentity = peptides
                .Where(p => string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal))
                .GroupBy(NativeQuantFractions.CrossSideKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var assigned = new List<FamilyAttributedPeptide>(peptides.Count);
            foreach (NativePeptideQuant p in peptides)
            {
                bool identified = string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal);
                if (!identified
                    && identifiedByIdentity.TryGetValue(NativeQuantFractions.CrossSideKey(p),
                        out List<NativePeptideQuant>? candidates)
                    && candidates.Any(q => NativeQuantFractions.IsCrossSideEquivalent(q, p))) continue;
                string normalized = DeNovoReconciler.NormalizeSequence(p.Sequence ?? string.Empty);
                IReadOnlyList<int> families = identified
                    ? identifiedFamilies(normalized)
                    : deNovoFamilies(normalized);
                assigned.Add(new FamilyAttributedPeptide(p, families ?? Array.Empty<int>()));
            }
            return new FamilyPeptideAttribution(assigned);
        }
    }
}
