using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant.Evidence;

public static partial class EvidenceTaskDiscovery
{
    // These are grammatical and reporting words, not a list of tissues or scope terms.
    private static readonly HashSet<string> TissueBreakWords = new(
        "a an and are as at be been by can could detected detectable expression expressed expresses for found from had has have highly in into is it its level levels located localised localized mainly may might moderate moderately more most mostly of on only or other predominantly present protein proteins reported restricted strongly that the their there these this those through to transcript transcripts very was were where which while with".Split(' '),
        StringComparer.OrdinalIgnoreCase);
    private static readonly Regex TissueWords = new(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TissueClauses = new(@"[.;!?\r\n]+|\b(?:but|whereas)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Parenthetical = new(@"\([^)]*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NegatedClause = new(@"\b(?:no|not|neither|without|absent|undetectable|lack|lacks|lacking)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // One accession contributes at most one vote to a phrase, even if a line or annotation repeats it.
    // Phrases are verbatim contiguous words, so the scope matcher can find them in their source lines.
    private static string? CommonTissuePhrase(IEnumerable<IGrouping<string, FastaItemExtended>> reviewed)
    {
        var candidates = reviewed.SelectMany(group =>
            {
                var observations = group.Select(protein => (protein.Evidence?.TissueSpecificity ?? new List<string>())
                    .SelectMany(TissuePhrases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()).ToArray();
                // Conflicting duplicate records do not get to vote for a phrase absent from another copy.
                return observations.SelectMany(phrases => phrases).GroupBy(phrase => phrase, StringComparer.OrdinalIgnoreCase)
                    .Select(phrases => phrases.OrderBy(phrase => phrase, StringComparer.Ordinal).First())
                    .Where(phrase => observations.All(phrases => phrases.Contains(phrase, StringComparer.OrdinalIgnoreCase)));
            })
            .GroupBy(phrase => phrase, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Phrase = group.OrderBy(value => value, StringComparer.Ordinal).First(), Count = group.Count() })
            .OrderByDescending(candidate => candidate.Count)
            .ThenByDescending(candidate => TissueWords.Matches(candidate.Phrase).Count)
            .ThenBy(candidate => candidate.Phrase, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Phrase, StringComparer.Ordinal)
            .FirstOrDefault();
        return candidates?.Phrase;
    }

    private static IEnumerable<string> TissuePhrases(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) yield break;
        // Keep a boundary where a parenthetical was removed, so suggested words are contiguous
        // in the original text; check negation before splitting comma-separated phrases.
        string text = Parenthetical.Replace(source, ",");
        foreach (string clause in TissueClauses.Split(text))
        {
            if (NegatedClause.IsMatch(clause)) continue;
            foreach (string fragment in clause.Split(','))
            {
                var words = new List<string>();
                foreach (Match match in TissueWords.Matches(fragment))
                {
                    string word = match.Value;
                    if (TissueBreakWords.Contains(word) || word.All(char.IsDigit))
                    {
                        if (words.Count > 0) { yield return string.Join(" ", words); words.Clear(); }
                    }
                    else words.Add(word);
                }
                if (words.Count > 0) yield return string.Join(" ", words);
            }
        }
    }
}
