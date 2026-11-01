using System;
using System.Collections.Generic;
using System.Globalization;

namespace FamilyQuant.Homology
{
    /// <summary>
    /// PAM30MS: the scoring contract this whole module exists to apply.
    ///
    /// The literal below is copied character for character from PL51's
    /// <c>MAligner/SubstitutionMatrixProvider.cs:11-37</c>. It is NOT a general PAM30 - it is the
    /// mass-spectrometry variant, and two of its symbols are deliberately repurposed as wildcards
    /// rather than carrying their usual IUPAC meaning:
    ///
    ///   B scores 5 against both R and K, so here it stands for "R or K" (not the usual N/D).
    ///   Z scores 4 against both Q and K, so here it stands for "Q or K" (not the usual E/Q).
    ///
    /// Anything that changes a cell changes which protein a de novo tag is attributed to, so the
    /// literal is treated as data that must not be retyped: the parser below refuses a matrix whose
    /// header is not <see cref="Alphabet"/>, whose rows do not arrive in the header's order, or whose
    /// rows are not square, and HomologyTests spot-checks individual cells against the source.
    ///
    /// Two differences from the ported parser, both deliberate:
    ///
    ///   - The source writes <c>substitutionMatrix[i - 1, counter - 2]</c>
    ///     (SubstitutionMatrixProvider.cs:106), i.e. [column, row] - transposed. That is invisible
    ///     only because PAM30MS happens to be symmetric. This port stores [row, column] and
    ///     HomologyTests asserts the parsed matrix equals its transpose, so the orientation is settled
    ///     by measurement instead of by assumption.
    ///   - Every number is parsed with <see cref="CultureInfo.InvariantCulture"/>. The source uses
    ///     <c>int.Parse(cols[i])</c>, which reads the machine's NegativeSign; a culture whose negative
    ///     sign is U+2212 rather than '-' throws FormatException on the very first cell.
    /// </summary>
    public static class PamMatrix
    {
        /// <summary>
        /// The 26 symbols of the matrix, in the order of its header row. Index into
        /// <see cref="Scores"/> with <see cref="IndexOf"/>; the position of each symbol here IS its
        /// row and column index.
        /// </summary>
        public const string Alphabet = "ARNDCQEGHILKMFPSTWYVBZJXU*";

        /// <summary>The symbol every undefined residue is scored as: X, which scores 0 against
        /// everything except '*'. Used only for DATABASE sequences - a query carrying an undefined
        /// character is rejected by name instead (see <see cref="HomologyScorer.TryNormalizeQuery"/>),
        /// because a bad character in a de novo tag is a caller error worth reporting.</summary>
        public const char UnknownSymbol = 'X';

        /// <summary>Copied verbatim from MAligner/SubstitutionMatrixProvider.cs:11-37.</summary>
        private const string Literal = @"A R N D C Q E G H I L K M F P S T W Y V B Z J X U *
A 6 -7 -4 -3 -6 -4 -2 -2 -7 -5 -6 -7 -5 -8 -2 0 -1 -13 -8 -2 -7 -6 0 0 0 -17
R -7 8 -6 -10 -8 -2 -9 -9 -2 -5 -7 0 -4 -9 -4 -3 -6 -2 -10 -8 5 -1 0 0 0 -17
N -4 -6 8 2 -11 -3 -2 -3 0 -5 -6 -1 -9 -9 -6 0 -2 -8 -4 -8 -4 -2 0 0 0 -17
D -3 -10 2 8 -14 -2 2 -3 -4 -7 -10 -4 -11 -15 -8 -4 -5 -15 -11 -8 -7 -3 0 0 0 -17
C -6 -8 -11 -14 10 -14 -14 -9 -7 -6 -11 -14 -13 -13 -8 -3 -8 -15 -4 -6 -11 -14 0 0 0 -17
Q -4 -2 -3 -2 -14 8 1 -7 1 -8 -7 -3 -4 -13 -3 -5 -5 -13 -12 -7 -3 4 0 0 0 -17
E -2 -9 -2 2 -14 1 8 -4 -5 -5 -7 -4 -7 -14 -5 -4 -6 -17 -8 -6 -7 -2 0 0 0 -17
G -2 -9 -3 -3 -9 -7 -4 6 -9 -11 -11 -7 -8 -9 -6 -2 -6 -15 -14 -5 -8 -7 0 0 0 -17
H -7 -2 0 -4 -7 1 -5 -9 9 -9 -8 -6 -10 -6 -4 -6 -7 -7 -3 -6 -4 -3 0 0 0 -17
I -5 -5 -5 -7 -6 -8 -5 -11 -9 8 5 -6 -1 -2 -8 -7 -2 -14 -6 2 -6 -7 0 0 0 -17
L -6 -7 -6 -10 -11 -7 -7 -11 -8 5 5 -7 0 -3 -8 -8 -5 -10 -7 0 -7 -7 0 0 0 -17
K -7 0 -1 -4 -14 -3 -4 -7 -6 -6 -7 7 -2 -14 -6 -4 -3 -12 -9 -9 5 4 0 0 0 -17
M -5 -4 -9 -11 -13 -4 -7 -8 -10 -1 0 -2 11 -4 -8 -5 -4 -13 -11 -1 -3 -3 0 0 0 -17
F -8 -9 -9 -15 -13 -13 -14 -9 -6 -2 -3 -14 -4 9 -10 -6 -9 -4 2 -8 -12 -14 0 0 0 -17
P -2 -4 -6 -8 -8 -3 -5 -6 -4 -8 -8 -6 -8 -10 8 -2 -4 -14 -13 -6 -5 -5 0 0 0 -17
S 0 -3 0 -4 -3 -5 -4 -2 -6 -7 -8 -4 -5 -6 -2 6 0 -5 -7 -6 -4 -5 0 0 0 -17
T -1 -6 -2 -5 -8 -5 -6 -6 -7 -2 -5 -3 -4 -9 -4 0 7 -13 -6 -3 -5 -4 0 0 0 -17
W -13 -2 -8 -15 -15 -13 -17 -15 -7 -14 -10 -12 -13 -4 -14 -5 -13 13 -5 -15 -7 -13 0 0 0 -17
Y -8 -10 -4 -11 -4 -12 -8 -14 -3 -6 -7 -9 -11 2 -13 -7 -6 -5 10 -7 -10 -11 0 0 0 -17
V -2 -8 -8 -8 -6 -7 -6 -5 -6 2 0 -9 -1 -8 -6 -6 -3 -15 -7 7 -9 -8 0 0 0 -17
B -7 5 -4 -7 -11 -3 -7 -8 -4 -6 -7 5 -3 -12 -5 -4 -5 -7 -10 -9 5 1 0 0 0 -17
Z -6 -1 -2 -3 -14 4 -2 -7 -3 -7 -7 4 -3 -14 -5 -5 -4 -13 -11 -8 1 4 0 0 0 -17
J 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 -17
X 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 -17
U 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 -17
* -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 -17 1";

        private static readonly Lazy<int[,]> LazyScores = new Lazy<int[,]>(Parse);
        private static readonly Lazy<int[]> LazyIndex = new Lazy<int[]>(BuildCharToIndex);

        /// <summary>The parsed matrix, <c>[row symbol, column symbol]</c>, parsed once.</summary>
        public static int[,] Scores
        {
            get { return LazyScores.Value; }
        }

        /// <summary>
        /// The matrix index of a symbol, or -1 when the symbol is not one of the 26.
        ///
        /// This is the check the ported code lacked (W2): MAligner builds a 128-entry table filled
        /// with -1 (Aligner.cs:45-55) and then indexes the matrix with it unguarded
        /// (Aligner.cs:141-143), so one undefined character throws IndexOutOfRangeException - and a
        /// character at or above 128 walks off the table itself.
        /// </summary>
        public static int IndexOf(char symbol)
        {
            if (symbol >= 128)
            {
                return -1;
            }

            return LazyIndex.Value[symbol];
        }

        /// <summary>True when the symbol is one of the 26 the matrix defines.</summary>
        public static bool IsDefined(char symbol)
        {
            return IndexOf(symbol) >= 0;
        }

        /// <summary>
        /// The matrix index to score a DATABASE residue as: the symbol's own index when it is
        /// defined, X's index otherwise. Real FASTA files carry residues the matrix has no column for
        /// (O, pyrrolysine, is the common one), and a database entry must not be able to throw from
        /// the middle of a parallel search.
        /// </summary>
        public static int ScoringIndexOf(char symbol)
        {
            int index = IndexOf(symbol);
            return index >= 0 ? index : IndexOf(UnknownSymbol);
        }

        /// <summary>
        /// The substitution score of two residues. Never throws: an undefined symbol on either side is
        /// scored as X, which is 0 against everything but '*'.
        /// </summary>
        public static int Score(char a, char b)
        {
            return Scores[ScoringIndexOf(a), ScoringIndexOf(b)];
        }

        private static int[] BuildCharToIndex()
        {
            int[] table = new int[128];

            for (int i = 0; i < table.Length; i++)
            {
                table[i] = -1;
            }

            for (int i = 0; i < Alphabet.Length; i++)
            {
                table[Alphabet[i]] = i;
            }

            return table;
        }

        /// <summary>
        /// Parses <see cref="Literal"/> once. Ported from
        /// SubstitutionMatrixProvider.LoadSubstitutionMatrixFromString (:70-117), with the shape
        /// checks the source does not make - a matrix that has been retyped wrongly must fail loudly
        /// here rather than quietly mis-score every alignment.
        /// </summary>
        private static int[,] Parse()
        {
            string[] lines = Literal.Replace("\r", "").Split('\n');

            List<char> header = new List<char>();
            int[,]? matrix = null;
            int row = -1;

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (matrix == null)
                {
                    foreach (string col in cols)
                    {
                        header.Add(col[0]);
                    }

                    if (new string(header.ToArray()) != Alphabet)
                    {
                        throw new InvalidOperationException(
                            "PAM30MS header is not the expected alphabet: got '" +
                            new string(header.ToArray()) + "', expected '" + Alphabet + "'.");
                    }

                    matrix = new int[header.Count, header.Count];
                    continue;
                }

                row++;

                if (row >= header.Count)
                {
                    throw new InvalidOperationException("PAM30MS has more rows than the header has symbols.");
                }

                if (cols.Length != header.Count + 1)
                {
                    throw new InvalidOperationException("PAM30MS row '" + cols[0] + "' has " +
                        (cols.Length - 1).ToString(CultureInfo.InvariantCulture) + " values, expected " +
                        header.Count.ToString(CultureInfo.InvariantCulture) + ".");
                }

                if (cols[0][0] != header[row])
                {
                    throw new InvalidOperationException("PAM30MS row " +
                        row.ToString(CultureInfo.InvariantCulture) + " is '" + cols[0] +
                        "', expected '" + header[row] + "': the rows must arrive in the header's order.");
                }

                for (int i = 1; i < cols.Length; i++)
                {
                    matrix[row, i - 1] = int.Parse(cols[i], NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
            }

            if (matrix == null || row != header.Count - 1)
            {
                throw new InvalidOperationException("PAM30MS is not square: parsed " +
                    (row + 1).ToString(CultureInfo.InvariantCulture) + " rows for " +
                    header.Count.ToString(CultureInfo.InvariantCulture) + " symbols.");
            }

            return matrix;
        }
    }
}
