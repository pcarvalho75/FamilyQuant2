using System;
using System.Collections.Generic;
using System.Linq;
using FamilyQuant.DeNovo;

namespace FamilyQuant
{
    /// <summary>Optional analyses over the existing prepared family attribution; no extraction or remapping.</summary>
    public static partial class FamilyCompositionStudy
    {
        /// <summary>Compute first, then store atomically. A failed resolver leaves a prior study intact.</summary>
        public static void StoreSubclasses(QuantManifest manifest, FamilyPeptideAttribution attribution,
            Func<string, string, int, IReadOnlyList<string?>> subclassKeys,
            SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique, Func<string, string>? describe = null)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            manifest.FamilySubclassParts = BuildSubclasses(attribution, subclassKeys, policy, describe);
        }

        /// <summary>The resolver returns the subclass key of EVERY candidate protein in a family;
        /// unknown labels must be retained as null. More than one label, no candidates, or a peptide
        /// shared between families cannot establish a unique subclass. Labels may be supplied from
        /// stored panel assignments or a stand-in map without changing the allocation rule.</summary>
        public static FamilySubclassPartsResult BuildSubclasses(FamilyPeptideAttribution attribution,
            Func<string, string, int, IReadOnlyList<string?>> subclassKeys,
            SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique,
            Func<string, string>? describe = null)
        {
            ArgumentNullException.ThrowIfNull(subclassKeys);
            return new FamilySubclassPartsResult
            {
                Policy = policy,
                Method = "All runs pooled. Credit to a subclass only when the peptide reaches one family and all candidate " +
                    "proteins have the same recorded subclass; otherwise subclass not resolved. " +
                    "Existing primary signal and shared-peptide policy retained; final-part rounding closes to the family total.",
                Families = BuildParts(attribution, policy, (assigned, family) =>
                {
                    string? key = null;
                    if (assigned.FamilyUserIds.Count == 1)
                    {
                        IReadOnlyList<string?> keys = subclassKeys(assigned.Peptide.Side,
                            DeNovoReconciler.NormalizeSequence(assigned.Peptide.Sequence), family);
                        if (keys.Count > 0 && keys.All(k => !string.IsNullOrWhiteSpace(k))
                            && keys.Distinct(StringComparer.Ordinal).Count() == 1) key = keys[0];
                    }
                    return new[] { new PartWeight(key, key == null ? "Subclass not resolved" : describe?.Invoke(key) ?? key, 1) };
                }, "Subclass not resolved"),
            };
        }

        internal sealed record PartWeight(string? Key, string Label, double Weight);

        internal static List<FamilySignalBreakdown> BuildParts(FamilyPeptideAttribution attribution,
            SharedPeptidePolicy policy, Func<FamilyAttributedPeptide, int, IReadOnlyList<PartWeight>> allocate,
            string unresolvedLabel)
        {
            ArgumentNullException.ThrowIfNull(attribution);
            if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
            var totals = FamilyEnvelopeQuant.BuildFromAttributedPeptides(attribution).Families
                .ToDictionary(f => f.FamilyUserId);
            var parts = totals.Keys.ToDictionary(f => f, _ => new Dictionary<string, FamilySignalPart>(StringComparer.Ordinal)
            {
                [string.Empty] = new() { IsUnresolved = true, Label = unresolvedLabel },
            });
            foreach (FamilyAttributedPeptide assigned in attribution.Peptides)
            {
                double factor = CreditFactor(assigned, policy);
                if (factor == 0) continue;
                foreach (int family in assigned.FamilyUserIds)
                {
                    double signal = PrimaryCredit(assigned, totals[family], policy, factor);
                    if (signal == 0) continue;
                    IReadOnlyList<PartWeight> weights = allocate(assigned, family);
                    foreach (PartWeight weight in weights)
                    {
                        string key = weight.Key ?? string.Empty;
                        if (!parts[family].TryGetValue(key, out FamilySignalPart? part))
                            parts[family][key] = part = new() { PartKey = weight.Key, Label = weight.Label };
                        part.Signal += signal * weight.Weight;
                    }
                }
            }
            return totals.Values.OrderBy(f => f.FamilyUserId).Select(f =>
                CloseParts(f.FamilyUserId, f.Signal(policy), parts[f.FamilyUserId].Values)).ToList();
        }

        internal static double CreditFactor(FamilyAttributedPeptide assigned, SharedPeptidePolicy policy) =>
            assigned.FamilyUserIds.Count == 0 || (policy == SharedPeptidePolicy.FamilyUnique && assigned.FamilyUserIds.Count != 1)
                ? 0 : policy == SharedPeptidePolicy.SplitEqually ? 1.0 / assigned.FamilyUserIds.Count : 1;

        private static double PrimaryCredit(FamilyAttributedPeptide assigned, FamilyEnvelopeQuantRow family,
            SharedPeptidePolicy policy, double factor)
        {
            NativePeptideQuant peptide = assigned.Peptide;
            if (string.Equals(peptide.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal))
                return peptide.EnvelopeSum * factor;
            double calibrated = policy switch
            {
                SharedPeptidePolicy.FamilyUnique => family.UniqueDeNovoEnvelopeReferenceCalibrated,
                SharedPeptidePolicy.SplitEqually => family.SplitDeNovoEnvelopeReferenceCalibrated,
                _ => family.DeNovoEnvelopeReferenceCalibrated,
            };
            // Preserve the existing aggregate's legacy fallback, including mixed zero/nonzero estimates.
            return (calibrated > 0 ? peptide.ReferenceCalibratedEnvelope : peptide.EnvelopeSum) * factor;
        }

        private static FamilySignalBreakdown CloseParts(int family, double total, IEnumerable<FamilySignalPart> source)
        {
            // Put the largest part last to absorb only floating-point regrouping error without making
            // a tiny unresolved bucket negative. The authoritative family total is never modified.
            List<FamilySignalPart> parts = source.OrderBy(p => p.Signal).ThenBy(p => p.PartKey, StringComparer.Ordinal).ToList();
            FamilySignalPart last = parts[^1];
            double preceding = parts.Take(parts.Count - 1).Sum(p => p.Signal);
            double remainder = total - preceding;
            if (remainder < 0 || !double.IsFinite(remainder)
                || Math.Abs(last.Signal - remainder) > Math.Max(total, 1) * 1e-12)
                throw new InvalidOperationException("Signal parts do not form a partition of the family.");
            last.Signal = remainder;
            foreach (FamilySignalPart part in parts) part.ShareOfFamily = total > 0 ? part.Signal / total : 0;
            if (total > 0) last.ShareOfFamily = 1 - parts.Take(parts.Count - 1).Sum(p => p.ShareOfFamily);
            return new() { FamilyUserId = family, FamilySignal = total, Parts = parts };
        }
    }
}
