using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class ReportNumberTrace
{
    // Labels come only from the selected exports. Function words permit "families are above the
    // threshold" to match the recorded quantity "families above the threshold"; no domain names occur here.
    private static readonly HashSet<string> FunctionWords = new(StringComparer.Ordinal)
    { "a", "an", "the", "and", "or", "is", "are", "was", "were", "of", "in", "on", "for", "to", "with", "at", "by", "from", "as" };
    private static readonly Regex SubjectWords = new(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private sealed record Word(string Text, int Start, int End);
    private sealed class SourceRow
    {
        internal string Signature { get; }
        internal string[][] Labels { get; }
        internal SourceRow(string[] cells)
        {
            Signature = string.Join("\u001f", cells.Select(c => c.Trim().ToLowerInvariant()));
            Labels = cells.Where(c => !TryKey(c.Trim(), 0, out _)).Select(c => Words(c).Select(w => w.Text).ToArray())
                .Where(w => w.Any(word => word.Any(char.IsLetter))).ToArray();
        }
    }
    private sealed class SubjectCatalog
    {
        private readonly Dictionary<string, Dictionary<string, string[]>> labels = new(StringComparer.Ordinal);
        private readonly HashSet<SourceRow> rows = new();
        private readonly HashSet<string> completeSingleWords = new(StringComparer.Ordinal);
        internal void Add(SourceRow row)
        {
            if (!rows.Add(row)) return;
            foreach (var label in row.Labels)
            {
                if (!labels.TryGetValue(label[0], out var entries)) labels.Add(label[0], entries = new(StringComparer.Ordinal));
                entries.TryAdd(string.Join(" ", label), label);
                if (label.Length == 1 && label[0].Length >= 3) completeSingleWords.Add(label[0]);
            }
        }
        internal bool IsKnownSingleWord(string word) => completeSingleWords.Contains(word);
        internal SubjectMatch? Best(Word[] context, int token) => MatchLabels(context.Select(w => w.Text).Distinct(StringComparer.Ordinal)
            .Where(labels.ContainsKey).SelectMany(word => labels[word].Values), context, token, this);
        internal SubjectMatch? Containing(Word[] context, int token, int length) => MatchLabels(context.Select(w => w.Text).Distinct(StringComparer.Ordinal)
            .Where(labels.ContainsKey).SelectMany(word => labels[word].Values), context, token, this, token + length);
    }
    private sealed record SubjectMatch(string Subject, bool Precedes, int Distance, int Words);
    private sealed record Resolution(string Status, Candidate? Found, string Reason, string Subject);

    private static Word[] Words(string text) => SubjectWords.Matches(text).Cast<Match>()
        .Where(m => !FunctionWords.Contains(m.Value.ToLowerInvariant()))
        .Select(m => new Word(m.Value.ToLowerInvariant(), m.Index, m.Index + m.Length)).ToArray();

    private static bool IsRowReference(string heading) => heading.Equals("Source data row", StringComparison.OrdinalIgnoreCase);

    private static Resolution ResolveSubject(List<Candidate> candidates, string paragraph, int token, int length, TableCell? cell, SubjectCatalog catalog, Paragraph currentParagraph)
    {
        if (candidates.Count == 0) return new("NOT FOUND", null, "", "");
        string context, heading = "";
        if (cell?.Parent is TableRow row)
        {
            // A numeric cell's own paragraph does not contain its family/run/option. Include the row label.
            var paragraphs = row.Descendants<Paragraph>().ToArray();
            context = string.Join(" ", paragraphs.Select(p => p.InnerText));
            int offset = 0;
            foreach (var p in paragraphs) { if (ReferenceEquals(p, currentParagraph)) break; offset += p.InnerText.Length + 1; }
            token += offset;
            int column = cell.ElementsBefore().OfType<TableCell>().Count();
            heading = (row.Parent as Table)?.Elements<TableRow>().FirstOrDefault()?.Elements<TableCell>().ElementAtOrDefault(column)?.InnerText ?? "";
        }
        else (context, token) = Clause(paragraph, token, length);
        var words = Words(context);
        if (catalog.Containing(words, token, length) is { } embedded)
            return new("AMBIGUOUS", null, "numeric token is inside a recorded subject label", embedded.Subject);
        // A list's pointer back to a native row is not the measured value in that row. It is usable
        // only when the report explicitly discusses that structural field.
        var eligible = candidates.Where(c => !IsRowReference(c.Source.Column)
            || context.Contains(c.Source.Column, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (eligible.Length == 0) return new("AMBIGUOUS", null, "equal cells are structural source-row references", "");
        var scores = new Dictionary<SourceRow, SubjectMatch?>();
        SubjectMatch? best = null;
        var selected = new List<(Candidate Candidate, SubjectMatch Match)>();
        foreach (var candidate in eligible)
        {
            if (!scores.TryGetValue(candidate.Source.Row, out var score))
                scores[candidate.Source.Row] = score = MatchLabels(candidate.Source.Row.Labels, words, token, catalog);
            if (score == null) continue;
            int comparison = Compare(score, best);
            if (comparison > 0) { best = score; selected.Clear(); }
            if (comparison >= 0) selected.Add((candidate, score));
        }
        var named = catalog.Best(words, token);
        if (named != null && (best == null || Compare(named, best) > 0))
            return new("AMBIGUOUS", null, "the locally named subject has no equal candidate cell", named.Subject);
        if (selected.Count > 0)
        {
            if (selected.Select(s => s.Match.Subject).Distinct(StringComparer.Ordinal).Count() != 1)
                return new("AMBIGUOUS", null, "multiple equally close named subjects", "");
            var first = selected.OrderByDescending(s => RoleScore(s.Candidate.Source.Column, heading)).First();
            return new("MATCH", first.Candidate, "subject match; " + first.Candidate.Kind, first.Match.Subject);
        }
        // Duplicate views of one identical source row/column are related. Unrelated equal values never
        // acquire a preferred citation just because their file or sheet happened to be indexed first.
        if (eligible.Select(c => (c.Source.Row.Signature, c.Source.Column)).Distinct().Count() == 1)
            return new("MATCH", eligible[0], "no named subject; " + eligible[0].Kind, "");
        return new("AMBIGUOUS", null, "equal cells from unrelated source rows; no named subject match", "");
    }

    private static int RoleScore(string sourceHeading, string printedHeading)
    {
        // Once the subject agrees, a printed column can distinguish e.g. a protein estimate
        // from a coincidentally equal family ID in that same source row.
        string Stem(string word) => word.Length > 4 && word.EndsWith('s') ? word.Substring(0, word.Length - 1) : word;
        var printed = Words(printedHeading).Select(w => Stem(w.Text)).ToHashSet(StringComparer.Ordinal);
        return Words(sourceHeading).Select(w => Stem(w.Text)).Distinct(StringComparer.Ordinal).Count(printed.Contains);
    }

    private static SubjectMatch? MatchLabels(IEnumerable<string[]> labels, Word[] context, int token, SubjectCatalog catalog, int? containingEnd = null)
    {
        SubjectMatch? best = null;
        foreach (var label in labels)
            for (int start = 0; start < context.Length; start++)
            {
                int count = 0;
                while (count < label.Length && start + count < context.Length && label[count] == context[start + count].Text) count++;
                // A partial one-word overlap such as "protein" is a quantity, not a named subject.
                // Complete single-word user labels and multiword label prefixes remain usable.
                if (count == 0 || (count == 1 && label.Length != 1 && !catalog.IsKnownSingleWord(label[0]))) continue;
                int end = context[start + count - 1].End;
                if (containingEnd.HasValue && (context[start].Start > token || end < containingEnd.Value)) continue;
                bool precedes = end <= token;
                var score = new SubjectMatch(string.Join(" ", label.Take(count)), precedes,
                    precedes ? token - end : Math.Abs(context[start].Start - token), count);
                if (Compare(score, best) > 0) best = score;
            }
        return best;
    }

    private static int Compare(SubjectMatch left, SubjectMatch? right)
    {
        if (right == null) return 1;
        int order = left.Precedes.CompareTo(right.Precedes);
        if (order == 0) order = right.Distance.CompareTo(left.Distance);
        if (order == 0) order = left.Words.CompareTo(right.Words);
        return order;
    }

    private static (string Context, int Token) Clause(string text, int token, int length)
    {
        int start = 0, end = text.Length, depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '(' or '[') depth++;
            else if (text[i] is ')' or ']') depth = Math.Max(0, depth - 1);
            bool boundary = depth == 0 && (text[i] is ';' or '\n' || (text[i] == '.'
                && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]))));
            if (!boundary) continue;
            if (i < token) start = i + 1;
            else if (i >= token + length) { end = i; break; }
        }
        return (text.Substring(start, end - start), token - start);
    }
}
