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

    /// <summary>A global alignment of <paramref name="a"/> and <paramref name="b"/> with free end gaps and
    /// affine internal gaps. Returns two strings of equal length with '-' for gaps. Ties prefer a match,
    /// then a gap in <paramref name="b"/>, so the answer does not depend on anything but the two inputs.</summary>
    public static (string A, string B) Pairwise(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        int n = a.Length, m = b.Length;
        if (n == 0 || m == 0) return (a + new string('-', m), new string('-', n) + b);
        const int Negative = int.MinValue / 4;
        int width = m + 1;
        var match = new int[(n + 1) * width];
        var gapB = new int[(n + 1) * width];   // a residue against a gap
        var gapA = new int[(n + 1) * width];   // b residue against a gap
        var fromMatch = new byte[(n + 1) * width];
        var fromGapB = new byte[(n + 1) * width];
        var fromGapA = new byte[(n + 1) * width];
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= m; j++)
            {
                int cell = i * width + j;
                if (i == 0 && j == 0) { match[cell] = 0; gapB[cell] = Negative; gapA[cell] = Negative; continue; }
                if (j == 0) { match[cell] = Negative; gapB[cell] = 0; gapA[cell] = Negative; fromGapB[cell] = 1; continue; }
                if (i == 0) { match[cell] = Negative; gapB[cell] = Negative; gapA[cell] = 0; fromGapA[cell] = 2; continue; }
                int diagonal = (i - 1) * width + (j - 1);
                (match[cell], fromMatch[cell]) = Best(match[diagonal], gapB[diagonal], gapA[diagonal]);
                match[cell] += Score(a[i - 1], b[j - 1]);
                int up = (i - 1) * width + j;
                (gapB[cell], fromGapB[cell]) = Best(match[up] - GapOpen, gapB[up] - GapExtend, gapA[up] - GapOpen);
                int left = i * width + (j - 1);
                (gapA[cell], fromGapA[cell]) = Best(match[left] - GapOpen, gapB[left] - GapOpen, gapA[left] - GapExtend);
            }

        // Free end gaps: the alignment may end on the last row or the last column.
        int bestScore = Negative, endI = n, endJ = m, endState = 0;
        void Consider(int i, int j)
        {
            int cell = i * width + j;
            (int score, byte state) = Best(match[cell], gapB[cell], gapA[cell]);
            if (score > bestScore) { bestScore = score; endI = i; endJ = j; endState = state; }
        }
        for (int j = 0; j <= m; j++) Consider(n, j);
        for (int i = 0; i < n; i++) Consider(i, m);

        var left1 = new StringBuilder();
        var left2 = new StringBuilder();
        // Trailing overhang, read backwards.
        for (int j = m; j > endJ; j--) { left1.Append('-'); left2.Append(b[j - 1]); }
        for (int i = n; i > endI; i--) { left1.Append(a[i - 1]); left2.Append('-'); }
        int x = endI, y = endJ, state2 = endState;
        while (x > 0 || y > 0)
        {
            int cell = x * width + y;
            if (state2 == 0)
            {
                left1.Append(a[x - 1]); left2.Append(b[y - 1]);
                state2 = fromMatch[cell]; x--; y--;
            }
            else if (state2 == 1)
            {
                left1.Append(a[x - 1]); left2.Append('-');
                state2 = fromGapB[cell]; x--;
            }
            else
            {
                left1.Append('-'); left2.Append(b[y - 1]);
                state2 = fromGapA[cell]; y--;
            }
        }
        return (Reverse(left1), Reverse(left2));

        static (int, byte) Best(int ifMatch, int ifGapB, int ifGapA) =>
            ifMatch >= ifGapB && ifMatch >= ifGapA ? (ifMatch, (byte)0) : ifGapB >= ifGapA ? (ifGapB, (byte)1) : (ifGapA, (byte)2);
        static string Reverse(StringBuilder text)
        {
            var chars = text.ToString().ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }
    }

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
        // Gap columns each pairwise alignment opens in the center before center residue p (p == length: after the last).
        var inserted = new List<int[]>();
        var widest = new int[length + 1];
        for (int s = 1; s < sequences.Count; s++)
        {
            var (alignedCenter, alignedOther) = Pairwise(center, sequences[s].Sequence);
            var pair = (Center: alignedCenter, Other: alignedOther);
            pairs.Add(pair);
            var counts = new int[length + 1];
            int position = 0;
            foreach (char c in pair.Center)
            {
                if (c == '-') counts[position]++;
                else position++;
            }
            inserted.Add(counts);
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
        for (int s = 0; s < pairs.Count; s++)
        {
            var (pairCenter, other) = pairs[s];
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
            if (s > 0)
            {
                (identity, identical, compared) = Identity(pairs[s - 1].Center, pairs[s - 1].Other);
            }
            aligned.Add(new AlignedRow(sequences[s], gapped, identity, identical, compared,
                sequences[s].Sequence.Count(c => char.ToUpperInvariant(c) == 'C'), MapDomains(sequences[s], gapped)));
        }
        return new MultipleAlignment(aligned, columns, Method);
    }

    /// <summary>Identity between two aligned strings: identical columns over the columns from the first to
    /// the last column where both hold a residue. Null when they share no column.</summary>
    public static (double? Percent, int Identical, int Compared) Identity(string first, string second)
    {
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
