using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// Canonical identity used when quantified peptide representatives are deduplicated or compared
    /// across the database-search and de novo sides. It deliberately keeps non-fixed modification
    /// annotations and precursor mass in the identity. Therefore two calls with the same residue
    /// backbone are not merged merely because their PTM text was stripped for a sequence-level study.
    ///
    /// PEAKS commonly writes the fixed cysteine alkylation as C(+57.02), while PatternLab reports the
    /// same peptidoform as plain C because carbamidomethylation was fixed in the search. That one notation
    /// difference is canonicalized. Other numeric or named annotations remain position-specific.
    /// </summary>
    public static class PeptidoformIdentity
    {
        public const double CarbamidomethylDeltaDa = 57.021464;
        public const double FixedModificationTextToleranceDa = 0.10;
        public const double DefaultPrecursorTolerancePpm = 20.0;

        /// <summary>The C13 minus C12 neutral-mass spacing in daltons. It is the same physical constant as
        /// <see cref="DeNovoXicExtractor.Carbon13Spacing"/> carried here to a couple more digits so the
        /// isotope-selection correction below is exact to the tolerance the join uses. One reported +-1 or
        /// +-2 monoisotopic-peak selection error shifts the neutral mass by this many daltons, or the m/z by
        /// this many daltons divided by the charge.</summary>
        public const double Carbon13SpacingDa = 1.00335483507;

        /// <summary>The isotope-selection error the equivalence rule is willing to correct, in C13 units.
        /// M, M+-1 and M+-2 of one precursor are treated as the same precursor; a genuine co-isolated
        /// neighbour further than this stays separate.</summary>
        public const int MaxIsotopeOffset = 2;

        private static readonly Regex FirstNumber = new Regex(
            @"[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Returns an invariant, position-aware peptidoform token. I/L folding is optional because it is
        /// appropriate for cross-side MS equivalence but not for within-side representative counting.
        /// </summary>
        public static string Canonicalize(string sequence, bool foldIsoleucineLeucine)
        {
            if (string.IsNullOrWhiteSpace(sequence)) return string.Empty;

            string text = sequence.Trim();
            // SEPro may carry flanking residues. Use the same unambiguous convention as the reconciler;
            // decimal points inside a modification do not occur at either of these fixed positions.
            if (text.Length > 4 && text[1] == '.' && text[text.Length - 2] == '.')
                text = text.Substring(2, text.Length - 4);

            var backbone = new StringBuilder(text.Length);
            var modifications = new List<string>();
            int residuePosition = 0;
            char lastResidue = '\0';

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(' || c == '[' || c == '{')
                {
                    char close = c == '(' ? ')' : c == '[' ? ']' : '}';
                    int end = text.IndexOf(close, i + 1);
                    if (end < 0) end = text.Length - 1;
                    string token = text.Substring(i + 1, Math.Max(0, end - i - 1)).Trim();
                    i = end;

                    Match number = FirstNumber.Match(token);
                    if (number.Success && double.TryParse(number.Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double delta))
                    {
                        if (lastResidue == 'C'
                            && Math.Abs(delta - CarbamidomethylDeltaDa) <= FixedModificationTextToleranceDa)
                            continue;

                        modifications.Add(residuePosition.ToString(CultureInfo.InvariantCulture) + ":" +
                            delta.ToString("+0.0000;-0.0000;0.0000", CultureInfo.InvariantCulture));
                    }
                    else if (token.Length > 0)
                    {
                        string named = Regex.Replace(token.ToUpperInvariant(), @"\s+", string.Empty);
                        modifications.Add(residuePosition.ToString(CultureInfo.InvariantCulture) + ":" + named);
                    }
                    continue;
                }

                if (!char.IsLetter(c)) continue;
                char residue = char.ToUpperInvariant(c);
                if (foldIsoleucineLeucine && residue == 'I') residue = 'L';
                backbone.Append(residue);
                lastResidue = residue;
                residuePosition++;
            }

            if (modifications.Count == 0) return backbone.ToString();
            return backbone + "|" + string.Join(";", modifications);
        }

        /// <summary>True when two positive neutral masses are within the symmetric ppm tolerance.</summary>
        public static bool PrecursorMassMatches(double firstNeutralMass, double secondNeutralMass,
                                                double tolerancePpm = DefaultPrecursorTolerancePpm)
        {
            if (!(firstNeutralMass > 0) || !(secondNeutralMass > 0) || !(tolerancePpm >= 0)) return false;
            double reference = (firstNeutralMass + secondNeutralMass) / 2.0;
            return Math.Abs(firstNeutralMass - secondNeutralMass) <= reference * tolerancePpm * 1e-6;
        }

        /// <summary>
        /// Isotope-selection-aware precursor equivalence on two positive NEUTRAL masses. Two masses are
        /// equivalent when either (a) they agree directly within <paramref name="tolerancePpm"/>, or (b)
        /// after correcting a +-1 or +-2 (up to <paramref name="maxIsotopeOffset"/>) monoisotopic-peak
        /// selection error, i.e. a neutral-mass shift of k times <see cref="Carbon13SpacingDa"/>, the
        /// residual agrees within the same tolerance. This is the JOIN-side companion to the within-peptide
        /// collapse: it pairs the M, M+-1 and M+-2 selections of ONE precursor across sources or sides while
        /// leaving a genuinely different co-isolated neighbour separate.
        ///
        /// <paramref name="selectedOffset"/> is the integer number of C13 units ADDED to
        /// <paramref name="secondNeutralMass"/> to line it up with <paramref name="firstNeutralMass"/>: 0 on
        /// a direct match, negative when the second mass sits one or two isotopes above the first, positive
        /// when it sits below. The search is deterministic: the smallest absolute offset that fits wins, and
        /// a positive offset is preferred over its negative twin on an exact tie, so the pairing is
        /// one-to-one regardless of input order.
        /// </summary>
        public static bool PrecursorMassEquivalent(
            double firstNeutralMass, double secondNeutralMass, out int selectedOffset,
            int maxIsotopeOffset = MaxIsotopeOffset, double tolerancePpm = DefaultPrecursorTolerancePpm)
        {
            selectedOffset = 0;
            if (!(firstNeutralMass > 0) || !(secondNeutralMass > 0) || !(tolerancePpm >= 0)) return false;
            if (maxIsotopeOffset < 0) maxIsotopeOffset = 0;

            foreach (int k in OffsetSearchOrder(maxIsotopeOffset))
            {
                double candidate = secondNeutralMass + k * Carbon13SpacingDa;
                if (!(candidate > 0)) continue;
                double reference = (firstNeutralMass + candidate) / 2.0;
                if (Math.Abs(firstNeutralMass - candidate) <= reference * tolerancePpm * 1e-6)
                {
                    selectedOffset = k;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Isotope-selection-aware precursor equivalence on two reported precursor m/z at one equal positive
        /// charge. It is the m/z form of <see cref="PrecursorMassEquivalent"/>: an M+-1 or M+-2 selection
        /// error shifts the m/z by k times <see cref="Carbon13SpacingDa"/> divided by the charge.
        /// <paramref name="selectedOffset"/> is the C13 units added to <paramref name="secondMz"/> to reach
        /// <paramref name="firstMz"/> (0 on a direct match). A non-positive charge falls back to the direct
        /// ppm test only. The raw source-reported m/z are never overwritten by this method; it only reports
        /// the offset a caller should record beside them.
        /// </summary>
        public static bool PrecursorMzEquivalent(
            double firstMz, double secondMz, int charge, out int selectedOffset,
            int maxIsotopeOffset = MaxIsotopeOffset, double tolerancePpm = DefaultPrecursorTolerancePpm)
        {
            selectedOffset = 0;
            if (!(firstMz > 0) || !(secondMz > 0) || !(tolerancePpm >= 0)) return false;
            if (maxIsotopeOffset < 0) maxIsotopeOffset = 0;
            if (charge <= 0) maxIsotopeOffset = 0;

            double step = charge > 0 ? Carbon13SpacingDa / charge : 0.0;
            foreach (int k in OffsetSearchOrder(maxIsotopeOffset))
            {
                double candidate = secondMz + k * step;
                if (!(candidate > 0)) continue;
                double reference = (firstMz + candidate) / 2.0;
                if (Math.Abs(firstMz - candidate) <= reference * tolerancePpm * 1e-6)
                {
                    selectedOffset = k;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Human-readable match basis for a selected offset: "direct" when the precursors agreed
        /// on the reported m/z within tolerance, "isotope-corrected" when a +-1/+-2 selection error was
        /// removed first. Kept beside the offset so an export stays auditable.</summary>
        public static string MatchBasis(int selectedOffset) =>
            selectedOffset == 0 ? "direct" : "isotope-corrected";

        /// <summary>Deterministic offset search order: the direct match first, then each magnitude in turn
        /// with the positive candidate ahead of its negative twin. This is what makes the smallest-error
        /// pairing single-valued and independent of the order the callers present their rows.</summary>
        private static IEnumerable<int> OffsetSearchOrder(int maxIsotopeOffset)
        {
            yield return 0;
            for (int m = 1; m <= maxIsotopeOffset; m++)
            {
                yield return m;
                yield return -m;
            }
        }
    }
}
