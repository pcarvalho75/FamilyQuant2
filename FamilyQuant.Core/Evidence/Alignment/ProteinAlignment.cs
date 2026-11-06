using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.Evidence.Alignment;

/// <summary>A Pfam span on one sequence, 1-based and inclusive, in its own residue numbering.</summary>
public sealed record AlignmentDomain(string Accession, string Name, int Start, int End);

/// <summary>One sequence offered to the alignment. <see cref="Domains"/> is empty when no positions are
/// stored for exactly this sequence.</summary>
public sealed record AlignmentSequence(string Label, string Accession, string Source, string Sequence,
    IReadOnlyList<AlignmentDomain> Domains);

/// <summary>A domain span on the alignment, as 0-based inclusive column indices.</summary>
public sealed record AlignedDomain(string Accession, string Name, int FirstColumn, int LastColumn);

/// <summary>One row of the multiple alignment. Identity is to the first sequence and null for the first.</summary>
public sealed record AlignedRow(AlignmentSequence Input, string Gapped, double? IdentityPercent, int IdenticalColumns,
    int ComparedColumns, int Cysteines, IReadOnlyList<AlignedDomain> Domains);

public sealed record MultipleAlignment(IReadOnlyList<AlignedRow> Rows, int Columns, string Method)
{
    /// <summary>The aligned sequences as FASTA, gaps included, one line per sequence.</summary>
    public string ToFasta()
    {
        var text = new StringBuilder();
        foreach (AlignedRow row in Rows)
            text.Append('>').Append(row.Input.Label).Append('\n').Append(row.Gapped).Append('\n');
        return text.ToString();
    }

    /// <summary>The alignment as blocks of <paramref name="width"/> columns with a name column and the
    /// residue number reached at the end of each line.</summary>
    public string ToAlignedText(int width = 60)
    {
        if (width < 10) throw new ArgumentOutOfRangeException(nameof(width));
        int nameWidth = Math.Min(30, Rows.Select(r => r.Input.Label.Length).DefaultIfEmpty(0).Max());
        var text = new StringBuilder();
        var reached = new int[Rows.Count];
        for (int start = 0; start < Columns; start += width)
        {
            int length = Math.Min(width, Columns - start);
            for (int r = 0; r < Rows.Count; r++)
            {
                string piece = Rows[r].Gapped.Substring(start, length);
                reached[r] += piece.Count(c => c != '-');
                string name = Rows[r].Input.Label.Length > nameWidth ? Rows[r].Input.Label[..nameWidth] : Rows[r].Input.Label;
                text.Append(name.PadRight(nameWidth)).Append("  ").Append(piece).Append("  ")
                    .Append(reached[r].ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            text.Append('\n');
        }
        return text.ToString();
    }
}

/// <summary>
/// A small multiple alignment for reading by eye: every sequence is aligned to the first one with a
/// global alignment whose end gaps are free (so a fragment sits inside a complete protein without
/// penalty), and the pairwise alignments are merged around the first sequence ("once a gap, always a
/// gap"). Read only: it changes no evidence, proposal or family. Not a replacement for a progressive
/// aligner on large or distant sets.
/// </summary>
public static class ProteinAlignment
{
    public const int GapOpen = 10;
    public const int GapExtend = 1;
    public const int MaximumSequences = 25;
    public const int MaximumLength = 5000;
    public const string Method = "Center star on the first sequence: each other sequence aligned to it by a global alignment with free end gaps "
        + "(BLOSUM62, gap opening 10, extension 1), merged keeping the first sequence's gaps; identity to the first sequence counts identical "
        + "columns over the columns between the first and last residue the two share (internal gaps count, end gaps do not); v1";

    private const string Alphabet = "ARNDCQEGHILKMFPSTWYVBZX*";
    // NCBI BLOSUM62, in the order of Alphabet.
    private static readonly int[,] Blosum62 =
    {
        { 4, -1, -2, -2, 0, -1, -1, 0, -2, -1, -1, -1, -1, -2, -1, 1, 0, -3, -2, 0, -2, -1, 0, -4 },
        { -1, 5, 0, -2, -3, 1, 0, -2, 0, -3, -2, 2, -1, -3, -2, -1, -1, -3, -2, -3, -1, 0, -1, -4 },
        { -2, 0, 6, 1, -3, 0, 0, 0, 1, -3, -3, 0, -2, -3, -2, 1, 0, -4, -2, -3, 3, 0, -1, -4 },
        { -2, -2, 1, 6, -3, 0, 2, -1, -1, -3, -4, -1, -3, -3, -1, 0, -1, -4, -3, -3, 4, 1, -1, -4 },
        { 0, -3, -3, -3, 9, -3, -4, -3, -3, -1, -1, -3, -1, -2, -3, -1, -1, -2, -2, -1, -3, -3, -2, -4 },
        { -1, 1, 0, 0, -3, 5, 2, -2, 0, -3, -2, 1, 0, -3, -1, 0, -1, -2, -1, -2, 0, 3, -1, -4 },
        { -1, 0, 0, 2, -4, 2, 5, -2, 0, -3, -3, 1, -2, -3, -1, 0, -1, -3, -2, -2, 1, 4, -1, -4 },
        { 0, -2, 0, -1, -3, -2, -2, 6, -2, -4, -4, -2, -3, -3, -2, 0, -2, -2, -3, -3, -1, -2, -1, -4 },
        { -2, 0, 1, -1, -3, 0, 0, -2, 8, -3, -3, -1, -2, -1, -2, -1, -2, -2, 2, -3, 0, 0, -1, -4 },
        { -1, -3, -3, -3, -1, -3, -3, -4, -3, 4, 2, -3, 1, 0, -3, -2, -1, -3, -1, 3, -3, -3, -1, -4 },
        { -1, -2, -3, -4, -1, -2, -3, -4, -3, 2, 4, -2, 2, 0, -3, -2, -1, -2, -1, 1, -4, -3, -1, -4 },
        { -1, 2, 0, -1, -3, 1, 1, -2, -1, -3, -2, 5, -1, -3, -1, 0, -1, -3, -2, -2, 0, 1, -1, -4 },
        { -1, -1, -2, -3, -1, 0, -2, -3, -2, 1, 2, -1, 5, 0, -2, -1, -1, -1, -1, 1, -3, -1, -1, -4 },
        { -2, -3, -3, -3, -2, -3, -3, -3, -1, 0, 0, -3, 0, 6, -4, -2, -2, 1, 3, -1, -3, -3, -1, -4 },
        { -1, -2, -2, -1, -3, -1, -1, -2, -2, -3, -3, -1, -2, -4, 7, -1, -1, -4, -3, -2, -2, -1, -2, -4 },
        { 1, -1, 1, 0, -1, 0, 0, 0, -1, -2, -2, 0, -1, -2, -1, 4, 1, -3, -2, -2, 0, 0, 0, -4 },
        { 0, -1, 0, -1, -1, -1, -1, -2, -2, -1, -1, -1, -1, -2, -1, 1, 5, -2, -2, 0, -1, -1, 0, -4 },
        { -3, -3, -4, -4, -2, -2, -3, -2, -2, -3, -2, -3, -1, 1, -4, -3, -2, 11, 2, -3, -4, -3, -2, -4 },
        { -2, -2, -2, -3, -2, -1, -2, -3, 2, -1, -1, -2, -1, 3, -3, -2, -2, 2, 7, -1, -3, -2, -1, -4 },
        { 0, -3, -3, -3, -1, -2, -2, -3, -3, 3, 1, -2, 1, -1, -2, -2, 0, -3, -1, 4, -3, -2, -1, -4 },
        { -2, -1, 3, 4, -3, 0, 1, -1, 0, -3, -4, 0, -3, -3, -2, 0, -1, -4, -3, -3, 4, 1, -1, -4 },
        { -1, 0, 0, 1, -3, 3, 4, -2, 0, -3, -3, 1, -1, -3, -1, 0, -1, -3, -2, -2, 1, 4, -1, -4 },
        { 0, -1, -1, -1, -2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -2, 0, 0, -2, -1, -1, -1, -1, -1, -4 },
        { -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, 1 },
    };

    private static readonly int[] Index = BuildIndex();

    private static int[] BuildIndex()
    {
        var index = new int[128];
        Array.Fill(index, Alphabet.IndexOf('X'));
        for (int i = 0; i < Alphabet.Length; i++) index[Alphabet[i]] = i;
        return index;
    }

    /// <summary>The BLOSUM62 score of two residues; a letter outside the matrix scores as X.</summary>
    public static int Score(char a, char b) => Blosum62[Code(a), Code(b)];

    private static int Code(char residue)
    {
        char upper = char.ToUpperInvariant(residue);
        return upper < 128 ? Index[upper] : Index['X'];
    }

    // States of the affine recurrence: a residue pair, a residue of the first string against a gap,
    // a residue of the second string against a gap.
    private const byte Pair = 0, GapInB = 1, GapInA = 2;

    /// <summary>A global alignment of <paramref name="a"/> and <paramref name="b"/> with free end gaps and
    /// affine internal gaps. Returns two strings of equal length with '-' for gaps. Ties prefer a pair,
    /// then a gap in <paramref name="b"/>, so the answer depends on nothing but the two inputs. Scores
    /// are kept for two rows only; the traceback keeps one byte per cell.</summary>
    public static (string A, string B) Pairwise(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        int n = a.Length, m = b.Length;
        if (n == 0 || m == 0) return (a + new string('-', m), new string('-', n) + b);
        const int Negative = int.MinValue / 4;
        int width = m + 1;
        var codeA = a.Select(Code).ToArray();
        var codeB = b.Select(Code).ToArray();
        // Per cell: bits 0-1 the state before a pair, bits 2-3 before a gap in b, bits 4-5 before a gap in a.
        var trace = new byte[(n + 1) * width];
        var previousPair = new int[width]; var previousGapB = new int[width]; var previousGapA = new int[width];
        var pair = new int[width]; var gapB = new int[width]; var gapA = new int[width];

        // Leading overhangs are free: row 0 is the second string's overhang, column 0 the first string's.
        previousPair[0] = 0; previousGapB[0] = Negative; previousGapA[0] = Negative;
        for (int j = 1; j <= m; j++)
        {
            previousPair[j] = Negative; previousGapB[j] = Negative; previousGapA[j] = 0;
            trace[j] = GapInA << 4;
        }
        // The alignment may end anywhere on the last row or the last column (trailing overhangs are free).
        var lastColumn = new (int Score, byte State)[n];
        lastColumn[0] = Best(previousPair[m], previousGapB[m], previousGapA[m]);
        (int Score, byte State, int J) lastRow = (Negative, Pair, 0);

        for (int i = 1; i <= n; i++)
        {
            int row = i * width;
            pair[0] = Negative; gapB[0] = 0; gapA[0] = Negative;
            trace[row] = GapInB << 2;
            int[] scores = Blosum62Row(codeA[i - 1]);
            for (int j = 1; j <= m; j++)
            {
                var (fromPair, pairState) = Best(previousPair[j - 1], previousGapB[j - 1], previousGapA[j - 1]);
                pair[j] = fromPair + scores[codeB[j - 1]];
                var (fromUp, upState) = Best(previousPair[j] - GapOpen, previousGapB[j] - GapExtend, previousGapA[j] - GapOpen);
                gapB[j] = fromUp;
                var (fromLeft, leftState) = Best(pair[j - 1] - GapOpen, gapB[j - 1] - GapOpen, gapA[j - 1] - GapExtend);
                gapA[j] = fromLeft;
                trace[row + j] = (byte)(pairState | upState << 2 | leftState << 4);
            }
            if (i < n) lastColumn[i] = Best(pair[m], gapB[m], gapA[m]);
            else
            {
                for (int j = 0; j <= m; j++)
                {
                    var end = Best(pair[j], gapB[j], gapA[j]);
                    if (end.Score > lastRow.Score) lastRow = (end.Score, end.State, j);
                }
            }
            (previousPair, pair) = (pair, previousPair);
            (previousGapB, gapB) = (gapB, previousGapB);
            (previousGapA, gapA) = (gapA, previousGapA);
        }

        // The last row first, then the last column from the top, keeping the first best: deterministic.
        int endI = n, endJ = lastRow.J; byte state = lastRow.State; int bestScore = lastRow.Score;
        for (int i = 0; i < n; i++)
            if (lastColumn[i].Score > bestScore) { bestScore = lastColumn[i].Score; endI = i; endJ = m; state = lastColumn[i].State; }

        var first = new StringBuilder(n + m);
        var second = new StringBuilder(n + m);
        // Trailing overhang, written backwards like the rest.
        for (int j = m; j > endJ; j--) { first.Append('-'); second.Append(b[j - 1]); }
        for (int i = n; i > endI; i--) { first.Append(a[i - 1]); second.Append('-'); }
        int x = endI, y = endJ;
        while (x > 0 || y > 0)
        {
            byte cell = trace[x * width + y];
            if (state == Pair)
            {
                first.Append(a[x - 1]); second.Append(b[y - 1]);
                state = (byte)(cell & 3); x--; y--;
            }
            else if (state == GapInB)
            {
                first.Append(a[x - 1]); second.Append('-');
                state = (byte)(cell >> 2 & 3); x--;
            }
            else
            {
                first.Append('-'); second.Append(b[y - 1]);
                state = (byte)(cell >> 4 & 3); y--;
            }
        }
        return (Reverse(first), Reverse(second));

        static string Reverse(StringBuilder text)
        {
            var chars = text.ToString().ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }
    }

    private static (int Score, byte State) Best(int ifPair, int ifGapB, int ifGapA) =>
        ifPair >= ifGapB && ifPair >= ifGapA ? (ifPair, Pair) : ifGapB >= ifGapA ? (ifGapB, GapInB) : (ifGapA, GapInA);

    private static readonly int[][] ScoreRows = Enumerable.Range(0, Alphabet.Length)
        .Select(r => Enumerable.Range(0, Alphabet.Length).Select(c => Blosum62[r, c]).ToArray()).ToArray();

    private static int[] Blosum62Row(int code) => ScoreRows[code];

    /// <summary>Aligns every sequence to the first and merges the pairwise alignments into one.</summary>
    public static MultipleAlignment CenterStar(IReadOnlyList<AlignmentSequence> sequences)
    {
        ArgumentNullException.ThrowIfNull(sequences);
        if (sequences.Count == 0) throw new ArgumentException("Choose at least one sequence.", nameof(sequences));
        if (sequences.Count > MaximumSequences)
            throw new ArgumentException("Choose at most " + MaximumSequences.ToString(CultureInfo.InvariantCulture) + " sequences.", nameof(sequences));
        foreach (AlignmentSequence sequence in sequences)
        {
            if (string.IsNullOrEmpty(sequence.Sequence) || sequence.Sequence.Contains('-'))
                throw new ArgumentException("Every sequence needs residues and no gap characters.", nameof(sequences));
            if (sequence.Sequence.Length > MaximumLength)
                throw new ArgumentException(sequence.Label + " is longer than " + MaximumLength.ToString(CultureInfo.InvariantCulture) + " residues.", nameof(sequences));
        }
        string center = sequences[0].Sequence;
        int length = center.Length;
        var pairs = new List<(string Center, string Other)>();
        // The widest run of gap columns any pairwise alignment opens in the center before center residue p
        // (p == length: after the last residue).
        var widest = new int[length + 1];
        for (int s = 1; s < sequences.Count; s++)
        {
            var pair = Pairwise(center, sequences[s].Sequence);
            pairs.Add(pair);
            var counts = new int[length + 1];
            int position = 0;
            foreach (char c in pair.A)
            {
                if (c == '-') counts[position]++;
                else position++;
            }
            for (int p = 0; p <= length; p++) widest[p] = Math.Max(widest[p], counts[p]);
        }
        int columns = length + widest.Sum();
        var rows = new List<string>();
        var centerRow = new StringBuilder(columns);
        for (int p = 0; p <= length; p++)
        {
            centerRow.Append('-', widest[p]);
            if (p < length) centerRow.Append(center[p]);
        }
        rows.Add(centerRow.ToString());
        foreach (var (pairCenter, other) in pairs)
        {
            var row = new StringBuilder(columns);
            int k = 0;
            for (int p = 0; p <= length; p++)
            {
                var insertion = new StringBuilder();
                while (k < pairCenter.Length && pairCenter[k] == '-') { insertion.Append(other[k]); k++; }
                int pad = widest[p] - insertion.Length;
                // Before the first center residue the overhang is right-aligned, so it meets the aligned part.
                if (p == 0) row.Append('-', pad).Append(insertion);
                else row.Append(insertion).Append('-', pad);
                if (p < length) { row.Append(other[k]); k++; }
            }
            rows.Add(row.ToString());
        }

        var aligned = new List<AlignedRow>();
        for (int s = 0; s < sequences.Count; s++)
        {
            string gapped = rows[s];
            double? identity = null;
            int identical = 0, compared = 0;
            if (s > 0) (identity, identical, compared) = Identity(pairs[s - 1].Center, pairs[s - 1].Other);
            aligned.Add(new AlignedRow(sequences[s], gapped, identity, identical, compared,
                CoherenceCheck.Cysteines(sequences[s].Sequence), MapDomains(sequences[s], gapped)));
        }
        return new MultipleAlignment(aligned, columns, Method);
    }

    /// <summary>Identity between two aligned strings: identical columns over the columns from the first to
    /// the last column where both hold a residue. Null when they share no column.</summary>
    public static (double? Percent, int Identical, int Compared) Identity(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Length != second.Length) throw new ArgumentException("Aligned strings must have equal length.");
        int start = -1, end = -1;
        for (int i = 0; i < first.Length; i++)
            if (first[i] != '-' && second[i] != '-') { if (start < 0) start = i; end = i; }
        if (start < 0) return (null, 0, 0);
        int identical = 0, compared = 0;
        for (int i = start; i <= end; i++)
        {
            if (first[i] == '-' && second[i] == '-') continue;
            compared++;
            if (first[i] != '-' && char.ToUpperInvariant(first[i]) == char.ToUpperInvariant(second[i])) identical++;
        }
        return (100.0 * identical / compared, identical, compared);
    }

    /// <summary>The columns where at least one row holds a cysteine, in order.</summary>
    public static IReadOnlyList<int> CysteineColumns(MultipleAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        var columns = new List<int>();
        for (int column = 0; column < alignment.Columns; column++)
            if (alignment.Rows.Any(r => char.ToUpperInvariant(r.Gapped[column]) == 'C')) columns.Add(column);
        return columns;
    }

    private static IReadOnlyList<AlignedDomain> MapDomains(AlignmentSequence sequence, string gapped)
    {
        if (sequence.Domains.Count == 0) return Array.Empty<AlignedDomain>();
        var columnOf = new int[sequence.Sequence.Length + 1];
        int residue = 0;
        for (int column = 0; column < gapped.Length; column++)
            if (gapped[column] != '-') columnOf[++residue] = column;
        return sequence.Domains.Where(d => d.Start >= 1 && d.End >= d.Start && d.End <= sequence.Sequence.Length)
            .Select(d => new AlignedDomain(d.Accession, d.Name, columnOf[d.Start], columnOf[d.End])).ToArray();
    }
}
