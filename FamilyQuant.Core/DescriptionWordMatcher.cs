using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant
{
    /// <summary>One description tokenizer and matcher for word grouping and keyword previews.
    /// TF-IDF keeps punctuation; user matching trims punctuation at word edges.</summary>
    internal sealed class DescriptionWordMatcher
    {
        static readonly string[] StopWords = new string[]
        {
            "protein", "fragment", "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any", "are",
            "aren't", "as", "at", "be", "because", "been", "before", "being", "below", "between", "both", "but", "by", "can't",
            "cannot", "could", "couldn't", "did", "didn't", "do", "does", "doesn't", "doing", "don't", "down", "during", "each",
            "few", "for", "from", "further", "had", "hadn't", "has", "hasn't", "have", "haven't", "having", "he", "he'd",
            "he'll", "he's", "her", "here", "here's", "hers", "herself", "him", "himself", "his", "how", "how's", "i", "i'd",
            "i'll", "i'm", "i've", "if", "in", "into", "is", "isn't", "it", "it's", "its", "itself", "let's", "me", "more",
            "most", "mustn't", "my", "myself", "no", "nor", "not", "of", "off", "on", "once", "only", "or", "other", "ought",
            "our", "ours", "ourselves", "out", "over", "own", "same", "shan't", "she", "she'd", "she'll", "she's", "should",
            "shouldn't", "so", "some", "such", "than", "that", "that's", "the", "their", "theirs", "them", "themselves",
            "then", "there", "there's", "these", "they", "they'd", "they'll", "they're", "they've", "this", "those", "through",
            "to", "too", "under", "until", "up", "very", "was", "wasn't", "we", "we'd", "we'll", "we're", "we've", "were",
            "weren't", "what", "what's", "when", "when's", "where", "where's", "which", "while", "who", "who's", "whom",
            "why", "why's", "with", "won't", "would", "wouldn't", "you", "you'd", "you'll", "you're", "you've", "your",
            "yours", "yourself", "yourselves"
        };

        private readonly HashSet<string> wordsToExclude;

        public DescriptionWordMatcher(IEnumerable<string> additionalWordsToExclude)
        {
            wordsToExclude = new HashSet<string>(StopWords, StringComparer.OrdinalIgnoreCase);
            foreach (string word in additionalWordsToExclude)
                if (!string.IsNullOrWhiteSpace(word)) wordsToExclude.Add(word.Trim());
        }

        public (string[] Terms, string[] RawTokens, string[] MatchWords) Read(string? description)
        {
            string text = (description ?? string.Empty).Split(new[] { " OS=" }, StringSplitOptions.None)[0];
            string[] words = Retained(Tokenize(text));
            string[] rawTokens = MatchTokens(text);
            return (words.Concat(Bigrams(words)).ToArray(), rawTokens, Retained(rawTokens));
        }

        private string[] Retained(string[] words) =>
            words.Where(w => w.Length > 1 && !wordsToExclude.Contains(w)).ToArray();

        private static IEnumerable<string> Bigrams(string[] words)
        {
            for (int i = 0; i < words.Length - 1; i++) yield return $"{words[i]}_{words[i + 1]}";
        }

        // Current-culture lowercasing is part of the frozen grouping behavior.
        private static string[] Tokenize(string text) =>
            Regex.Split((text ?? string.Empty).ToLower(), @"[\s-_/]+").Where(w => w.Length > 0).ToArray();

        private static readonly char[] WordPunctuation = { '(', ')', '[', ']', '{', '}', ',', ';', ':', '.', '\'', '"' };

        private static string[] MatchTokens(string text) =>
            Tokenize(text).Select(w => w.Trim(WordPunctuation)).Where(w => w.Length > 0).ToArray();

        // Phrases and exclusions see every word, including stop words. Single keywords
        // see only retained words. Both use the same tokenization and whole-word rule.
        private static bool Matches(FastaClusterElement element, string[] entry, bool fullDescription) =>
            fullDescription || entry.Length > 1
                ? ContainsPhrase(element.RawTokens, entry)
                : entry.Length == 1 && Array.IndexOf(element.MatchWords, entry[0]) >= 0;

        public static List<string> ParseKeywords(string? text)
        {
            var keywords = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return keywords;

            string normalized = text.Replace('“', '"').Replace('”', '"');
            string[] parts = normalized.Split('"');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1)
                {
                    // Inside quotes: a phrase (or a single word, if that is all it holds).
                    string phrase = string.Join(" ", MatchTokens(parts[i]));
                    if (phrase.Length > 0) keywords.Add(phrase);
                }
                else
                {
                    keywords.AddRange(Regex.Split(parts[i], @"[\s,;]+")
                        .Select(k => k.Trim().ToLower())
                        .Where(k => k.Length > 0));
                }
            }
            return keywords.Distinct(StringComparer.Ordinal).ToList();
        }

        private static bool ContainsPhrase(string[] tokens, string[] phrase)
        {
            if (phrase.Length == 0 || phrase.Length > tokens.Length) return false;
            for (int start = 0; start + phrase.Length <= tokens.Length; start++)
            {
                int j = 0;
                while (j < phrase.Length && string.Equals(tokens[start + j], phrase[j], StringComparison.Ordinal)) j++;
                if (j == phrase.Length) return true;
            }
            return false;
        }

        public static KeywordFamilies MatchKeywords(IReadOnlyList<FastaClusterElement> elements,
            List<string> keywords, KeywordMatch match, List<string>? excludeWords)
        {
            var found = new KeywordFamilies();

            // A keyword that stands for several words, quoted or hyphenated as descriptions are split, is
            // a PHRASE: consecutive words of the full description. A single word is looked up among the
            // element's words, so a stop word alone still matches nothing.
            List<string[]> keywordWords = keywords.Select(MatchTokens).Where(w => w.Length > 0).ToList();

            // MatchTokens lower-cases, so an exclude word typed in any case removes the family regardless.
            var excludeEntries = new List<(string[] Tokens, HashSet<int> Families, HashSet<int> Proteins)>();
            if (excludeWords != null)
            {
                foreach (string w in excludeWords.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim().ToLower()).Distinct())
                {
                    string[] tokens = MatchTokens(w);
                    if (tokens.Length == 0) continue;
                    var families = new HashSet<int>();
                    var proteins = new HashSet<int>();
                    found.ByExcludeEntry[w] = families;
                    found.ProteinsByExcludeEntry[w] = proteins;
                    excludeEntries.Add((tokens, families, proteins));
                }
            }

            for (int position = 0; position < elements.Count; position++)
            {
                FastaClusterElement element = elements[position];

                // Record both protein and family positions. The preview decides which members join.
                foreach (var exclude in excludeEntries)
                {
                    if (!Matches(element, exclude.Tokens, fullDescription: true)) continue;
                    exclude.Families.Add(element.FamilyId);
                    exclude.Proteins.Add(position);
                }

                bool Has(string[] keyword) => Matches(element, keyword, fullDescription: false);

                bool matches = match == KeywordMatch.All
                    ? keywordWords.Count > 0 && keywordWords.All(Has)
                    : keywordWords.Any(Has);

                if (matches)
                {
                    found.Matched.Add(element.FamilyId);
                    found.MatchedProteins.Add(position);
                }
            }

            return found;
        }

    }
}
