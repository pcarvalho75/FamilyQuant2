using System;
using System.Collections.Generic;
using System.Globalization;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// The theoretical neutral monoisotopic mass of an amino-acid sequence, summed from residue masses,
    /// explicit modification deltas and one water. A theoretical mass match is a composition-level
    /// observation; it does not establish that either sequence assignment is correct.
    /// </summary>
    public static class PeptideMass
    {
        /// <summary>One water, the mass added once for the peptide's terminal H and OH.</summary>
        public const double WaterMonoisotopic = 18.0105646;

        private static readonly Dictionary<char, double> ResidueMono = new Dictionary<char, double>
        {
            ['G'] = 57.02146,
            ['A'] = 71.03711,
            ['S'] = 87.03203,
            ['P'] = 97.05276,
            ['V'] = 99.06841,
            ['T'] = 101.04768,
            ['C'] = 103.00919,
            ['L'] = 113.08406,
            ['I'] = 113.08406,
            ['N'] = 114.04293,
            ['D'] = 115.02694,
            ['Q'] = 128.05858,
            ['K'] = 128.09496,
            ['E'] = 129.04259,
            ['M'] = 131.04049,
            ['H'] = 137.05891,
            ['F'] = 147.06841,
            ['R'] = 156.10111,
            ['Y'] = 163.06333,
            ['W'] = 186.07931,
            ['U'] = 150.95364,
            ['O'] = 237.14773,
        };

        /// <summary>True when the letter is a residue with a known monoisotopic mass.</summary>
        public static bool IsKnownResidue(char residue) => ResidueMono.ContainsKey(residue);

        /// <summary>The monoisotopic mass of one residue, or NaN when the letter is not a known residue.</summary>
        public static double ResidueMass(char residue) =>
            ResidueMono.TryGetValue(residue, out double m) ? m : double.NaN;

        /// <summary>
        /// The theoretical neutral monoisotopic mass of a bare sequence (letters only), residue masses
        /// plus one water. Returns NaN when the sequence is empty or carries a letter with no known
        /// residue mass, so a caller cannot silently compare an unknown as though it were mass-equal.
        /// </summary>
        public static double NeutralMonoisotopicMass(string bareSequence)
        {
            if (string.IsNullOrEmpty(bareSequence)) return double.NaN;
            double sum = WaterMonoisotopic;
            foreach (char c in bareSequence)
            {
                if (!ResidueMono.TryGetValue(c, out double m)) return double.NaN;
                sum += m;
            }
            return sum;
        }

        /// <summary>
        /// The theoretical neutral monoisotopic mass of an annotated peptide. Residue letters outside
        /// parentheses/brackets/braces contribute their standard residue masses. Signed numeric deltas in
        /// inline annotations are added exactly as exported, so <c>C(+57.02)</c> contributes 103.00919 +
        /// 57.02 Da. When the peptide itself has no numeric delta, signed numeric deltas in
        /// <paramref name="explicitPtmMasses"/> are used as a fallback. This precedence prevents the PEAKS
        /// peptide and PTM columns from double-counting the same modification.
        ///
        /// Returns NaN for an empty sequence, an unknown residue, an unbalanced annotation, or a
        /// non-finite numeric delta. Named PTMs without an explicit mass are deliberately not guessed.
        /// </summary>
        public static double NeutralMonoisotopicMassWithModifications(
            string annotatedPeptide, string? explicitPtmMasses = null)
        {
            if (string.IsNullOrWhiteSpace(annotatedPeptide)) return double.NaN;

            double sum = WaterMonoisotopic;
            int residues = 0;
            int inlineDeltaCount = 0;

            for (int i = 0; i < annotatedPeptide.Length; i++)
            {
                char c = annotatedPeptide[i];
                if (IsOpening(c))
                {
                    int close = FindClose(annotatedPeptide, i, c);
                    if (close < 0) return double.NaN;
                    if (!AddSignedDeltas(annotatedPeptide.Substring(i + 1, close - i - 1),
                            ref sum, ref inlineDeltaCount))
                        return double.NaN;
                    i = close;
                    continue;
                }

                if (!char.IsLetter(c)) continue;
                char residue = char.ToUpperInvariant(c);
                if (!ResidueMono.TryGetValue(residue, out double mass)) return double.NaN;
                sum += mass;
                residues++;
            }

            if (residues == 0) return double.NaN;

            if (inlineDeltaCount == 0 && !string.IsNullOrWhiteSpace(explicitPtmMasses))
            {
                int explicitCount = 0;
                if (!AddSignedDeltas(explicitPtmMasses, ref sum, ref explicitCount))
                    return double.NaN;
            }

            return sum;
        }

        private static bool IsOpening(char c) => c == '(' || c == '[' || c == '{';

        private static int FindClose(string text, int start, char opening)
        {
            char closing = opening == '(' ? ')' : opening == '[' ? ']' : '}';
            int depth = 0;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == opening) depth++;
                else if (text[i] == closing && --depth == 0) return i;
            }
            return -1;
        }

        /// <summary>Find and add signed decimal numbers. Unsigned numbers (for example a PTM name with
        /// a residue position) are ignored because they are not explicit mass deltas.</summary>
        private static bool AddSignedDeltas(string text, ref double sum, ref int count)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '+' && text[i] != '-') continue;
                int start = i;
                int j = i + 1;
                bool digit = false;
                while (j < text.Length && char.IsDigit(text[j])) { digit = true; j++; }
                if (j < text.Length && text[j] == '.')
                {
                    j++;
                    while (j < text.Length && char.IsDigit(text[j])) { digit = true; j++; }
                }
                if (!digit) continue;

                if (!double.TryParse(text.Substring(start, j - start), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double delta) || !double.IsFinite(delta))
                    return false;
                sum += delta;
                count++;
                i = j - 1;
            }
            return true;
        }
    }
}
