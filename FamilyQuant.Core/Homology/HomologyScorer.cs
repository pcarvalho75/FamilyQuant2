using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FamilyQuant.Homology
{
    /// <summary>
    /// The two gapless scans PepExplorer runs over every protein, ported from PL51's
    /// <c>MAligner/Aligner.cs</c>: an identity prefilter (IDScoreArray, :215-256) and, for whatever
    /// survives it, a PAM30MS pass (Align, :115-152).
    ///
    /// Both are gapless by design, not by omission: a de novo tag is short, an insertion or deletion
    /// in it is a sequencing error rather than an evolutionary event, and a gapped local alignment of
    /// a 10-mer against a whole database mostly finds ways to be wrong. What is ported is the window
    /// scan; what is fixed on the way in is which windows it is allowed to score.
    ///
    /// THE C-TERMINUS DEFECT (W1). Align iterates its start offset to <c>protein.Length - 1</c> and
    /// breaks out of the inner loop when the window runs past the end (Aligner.cs:128-139), so the
    /// last few offsets score a TRUNCATED window - at the very last offset, a single residue. Under
    /// PAM30MS the off-diagonal cells are strongly negative (down to -17), so a full 12-residue window
    /// over a mediocre homolog can easily score below zero while a one-residue tail window scoring
    /// W/W = 13 wins the argmax outright. The caller then reports that offset and, via
    /// <c>Math.Min(peptide.Length, protein.Length - MaxScorePos)</c> (Aligner.cs:64, :97), a
    /// one-character "closest peptide in the database". Here the scan is bounded at
    /// <c>protein.Length - peptide.Length</c>: every window that can win is a window the peptide
    /// actually fits in. Proteins shorter than the peptide produce no window at all, which is what the
    /// identity prefilter already decided by returning null (Aligner.cs:218-221).
    ///
    /// THE UNDEFINED-CHARACTER DEFECT (W2). See <see cref="TryNormalizeQuery"/>.
    /// </summary>
    public static class HomologyScorer
    {
        /// <summary>
        /// Uppercases a query and refuses it if anything is left that PAM30MS has no column for.
        ///
        /// The ported scorer cannot survive such a character: MAligner looks the residue up in a
        /// 128-entry table of -1 defaults (Aligner.cs:45-55) and indexes the matrix with the result
        /// unguarded (Aligner.cs:141-143). '(' from PEAKS modification notation throws
        /// IndexOutOfRangeException; a character at or above 128 reads past the table. One such
        /// character in one de novo row kills the entire Parallel.ForEach in ProgramEngine.cs:216.
        ///
        /// So the query is checked once, up front, and a bad one becomes a result carrying a message
        /// that NAMES the offending characters. Nothing below this method throws on data.
        ///
        /// Lowercase is normalised rather than refused (the fix is "uppercase, then reject what is
        /// still undefined"), so "peptide" searches as "PEPTIDE"; 'O' and 'o' are both refused,
        /// because pyrrolysine has no column. Modification notation is NOT stripped here: silently
        /// deleting "(+57.02)" would search a sequence the caller did not ask for. Strip it first -
        /// FamilyQuant.DeNovo.DeNovoReconciler.NormalizeSequence does exactly that, though note it
        /// also folds I into L.
        /// </summary>
        /// <param name="query">The de novo sequence as it arrives.</param>
        /// <param name="normalized">The uppercased query, or an empty string when refused.</param>
        /// <param name="rejection">Why it was refused, naming the offending characters; empty when accepted.</param>
        /// <returns>True when the query can be searched.</returns>
        public static bool TryNormalizeQuery(string? query, out string normalized, out string rejection)
        {
            normalized = string.Empty;
            rejection = string.Empty;

            if (string.IsNullOrWhiteSpace(query))
            {
                rejection = "the query is empty.";
                return false;
            }

            string trimmed = query.Trim().ToUpperInvariant();

            List<char> offenders = new List<char>();

            foreach (char c in trimmed)
            {
                if (!PamMatrix.IsDefined(c) && !offenders.Contains(c))
                {
                    offenders.Add(c);
                }
            }

            if (offenders.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("'").Append(trimmed).Append("' carries ");
                sb.Append(offenders.Count == 1 ? "a character" : "characters");
                sb.Append(" PAM30MS does not define: ");

                for (int i = 0; i < offenders.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(Describe(offenders[i]));
                }

                sb.Append(". Strip modification notation and non-residue symbols before searching.");
                rejection = sb.ToString();
                return false;
            }

            normalized = trimmed;
            return true;
        }

        /// <summary>A character as a person can read it in an error message.</summary>
        private static string Describe(char c)
        {
            return c > 32 && c < 127
                ? "'" + c + "'"
                : "U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A 128-entry table that maps each residue onto the residue it is INDISTINGUISHABLE FROM for
        /// the purpose of the identity prefilter. Folding both sides through it and comparing for
        /// equality is exactly the pairwise test of Aligner.cs:235, written once instead of per
        /// position, and it extends to K/Q without a second branch.
        ///
        /// This affects the identity prefilter ONLY. PAM30MS is never folded.
        /// </summary>
        public static char[] BuildFoldMap(bool considerILSame, bool kqEquivalent)
        {
            char[] map = new char[128];

            for (int i = 0; i < map.Length; i++)
            {
                map[i] = (char)i;
            }

            if (considerILSame)
            {
                map['I'] = 'L';
            }

            if (kqEquivalent)
            {
                map['K'] = 'Q';
            }

            return map;
        }

        /// <summary>The fold map <paramref name="options"/> asks for.</summary>
        public static char[] BuildFoldMap(HomologyOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return BuildFoldMap(options.ConsiderILSame, options.KQEquivalent);
        }

        private static char Fold(char[] map, char c)
        {
            return c < 128 ? map[c] : c;
        }

        /// <summary>
        /// How many residues of the peptide are identical to the protein when the peptide starts at
        /// <paramref name="start"/>. The window is the peptide's whole length; a start that would run
        /// past the protein's end is not a window this module scores.
        /// </summary>
        public static int IdentityAt(string peptide, string protein, int start, char[] foldMap)
        {
            if (peptide == null)
            {
                throw new ArgumentNullException(nameof(peptide));
            }

            if (protein == null)
            {
                throw new ArgumentNullException(nameof(protein));
            }

            if (foldMap == null)
            {
                throw new ArgumentNullException(nameof(foldMap));
            }

            if (start < 0 || start + peptide.Length > protein.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start),
                    "The window must lie inside the protein: start " +
                    start.ToString(CultureInfo.InvariantCulture) + ", peptide " +
                    peptide.Length.ToString(CultureInfo.InvariantCulture) + ", protein " +
                    protein.Length.ToString(CultureInfo.InvariantCulture) + ".");
            }

            int identities = 0;

            for (int i = 0; i < peptide.Length; i++)
            {
                if (Fold(foldMap, peptide[i]) == Fold(foldMap, protein[start + i]))
                {
                    identities++;
                }
            }

            return identities;
        }

        /// <summary>
        /// The best identity any full window of the protein gives the peptide, and where it sits.
        /// PepExplorer's prefilter (IDScore over IDScoreArray, Aligner.cs:207-256) reduced to the one
        /// number ProgramEngine.cs:227 divides by the peptide's length.
        ///
        /// Two departures from the source, both consequences of W1: the scan stops at
        /// <c>protein.Length - peptide.Length</c>, and a protein shorter than the peptide scores -1
        /// with no window rather than 0 with a window (the source returns null, ProgramEngine then
        /// never sees it). Truncated C-terminal windows are therefore not counted, and cannot be: a
        /// tryptic peptide does not hang off the end of the protein it came from, so an "identity"
        /// earned by matching only the residues that happen to fit is an artefact of the loop bound.
        /// </summary>
        /// <returns>The number of identical residues, or -1 when the peptide does not fit.</returns>
        public static int BestIdentity(string peptide, string protein, char[] foldMap, out int bestStart)
        {
            if (peptide == null)
            {
                throw new ArgumentNullException(nameof(peptide));
            }

            if (protein == null)
            {
                throw new ArgumentNullException(nameof(protein));
            }

            bestStart = -1;

            int lastStart = protein.Length - peptide.Length;

            if (lastStart < 0 || peptide.Length == 0)
            {
                return -1;
            }

            int best = -1;

            for (int start = 0; start <= lastStart; start++)
            {
                int identities = IdentityAt(peptide, protein, start, foldMap);

                if (identities > best)
                {
                    best = identities;
                    bestStart = start;
                }
            }

            return best;
        }

        /// <summary>
        /// The identity fraction ProgramEngine.cs:227-228 gates on: best identical residues over the
        /// peptide's length. Zero when the peptide does not fit in the protein.
        /// </summary>
        public static double IdentityFraction(string peptide, string protein, HomologyOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            int identities = BestIdentity(peptide, protein, BuildFoldMap(options), out _);
            return identities <= 0 || peptide.Length == 0 ? 0.0 : identities / (double)peptide.Length;
        }

        /// <summary>
        /// The PAM30MS score of the peptide laid on the protein at <paramref name="start"/>, summed
        /// over the residues that OVERLAP - which at the C-terminus is fewer than the peptide's
        /// length.
        ///
        /// This is the primitive exactly as Aligner.cs:128-148 computes it, break included, and it is
        /// public for one reason: it is what makes W1 demonstrable. A test can score the truncated
        /// tail window that the unbounded loop would have chosen and show it beating every full
        /// window. Nothing in this module's search path calls it with a start that truncates.
        /// </summary>
        public static int ScoreWindow(string peptide, string protein, int start)
        {
            if (peptide == null)
            {
                throw new ArgumentNullException(nameof(peptide));
            }

            if (protein == null)
            {
                throw new ArgumentNullException(nameof(protein));
            }

            if (start < 0 || start >= protein.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start),
                    "The window must start inside the protein: start " +
                    start.ToString(CultureInfo.InvariantCulture) + ", protein " +
                    protein.Length.ToString(CultureInfo.InvariantCulture) + ".");
            }

            int overlap = Math.Min(peptide.Length, protein.Length - start);
            int sum = 0;

            for (int i = 0; i < overlap; i++)
            {
                sum += PamMatrix.Score(peptide[i], protein[start + i]);
            }

            return sum;
        }

        /// <summary>
        /// Where the peptide scores best on the protein, over FULL windows only (W1).
        ///
        /// Ties keep the lowest offset, which is what <c>Array.IndexOf(res, maxScore)</c> does in the
        /// source (Aligner.cs:62, :95); it matters because a peptide can align equally well to two
        /// repeats of the same motif and the answer must not depend on which one the loop saw first.
        /// </summary>
        /// <returns>The winning offset, or -1 when the peptide does not fit in the protein.</returns>
        public static int BestWindow(string peptide, string protein, out int score)
        {
            if (peptide == null)
            {
                throw new ArgumentNullException(nameof(peptide));
            }

            if (protein == null)
            {
                throw new ArgumentNullException(nameof(protein));
            }

            score = 0;

            int lastStart = protein.Length - peptide.Length;

            if (lastStart < 0 || peptide.Length == 0)
            {
                return -1;
            }

            int bestStart = 0;
            int bestScore = int.MinValue;

            for (int start = 0; start <= lastStart; start++)
            {
                int sum = 0;

                for (int i = 0; i < peptide.Length; i++)
                {
                    sum += PamMatrix.Score(peptide[i], protein[start + i]);
                }

                if (sum > bestScore)
                {
                    bestScore = sum;
                    bestStart = start;
                }
            }

            score = bestScore;
            return bestStart;
        }

        /// <summary>
        /// The longest run of consecutive identical residues that a window passing the identity gate
        /// is GUARANTEED to contain - the pigeonhole bound that decides whether the seed index can be
        /// trusted for a query of this length.
        ///
        /// A window of length L that keeps at least <c>ceil(L * minIdentity)</c> identities carries at
        /// most <c>e = L - ceil(L * minIdentity)</c> mismatches; those mismatches cut the window into
        /// at most <c>e + 1</c> runs of identity holding <c>L - e</c> residues between them, so the
        /// longest run is at least <c>ceil((L - e) / (e + 1))</c>. When that is at least the seed
        /// length, every protein that could pass the gate necessarily shares a seed with the query and
        /// the index loses nothing; when it is shorter, the index is a heuristic and
        /// <see cref="HomologySearchStrategy.Auto"/> declines it.
        ///
        /// At the default 0.80 the bound reaches 4 for most lengths a de novo tag actually has (8, 9,
        /// 12, 13, 14, 16 and up) and falls to 3 at 10, 11 and 15, where the extra allowed mismatch
        /// buys nothing. Those lengths are scanned exhaustively rather than approximately.
        /// </summary>
        public static int GuaranteedMatchRun(int peptideLength, double minIdentity)
        {
            if (peptideLength <= 0)
            {
                return 0;
            }

            // The 1e-9 keeps 10 * 0.8 = 8.000000000000000444 from demanding 9 identities. It can only
            // lower the requirement, which can only make the bound more conservative.
            int required = (int)Math.Ceiling((peptideLength * minIdentity) - 1e-9);

            if (required < 1)
            {
                required = 1;
            }

            if (required > peptideLength)
            {
                required = peptideLength;
            }

            int mismatches = peptideLength - required;

            // ceil(required / (mismatches + 1)), in integers.
            return (required + mismatches) / (mismatches + 1);
        }
    }
}
