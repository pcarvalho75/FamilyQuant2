using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

/// <summary>Checks a frozen literal against resolved text in the same published Help member.</summary>
internal static class LedgerPublishedCopyComparison
{
    internal sealed record Publication(string FrozenMember, string CurrentPath, string Symbol, string Context, string Text);
    internal sealed record Result(bool Matched, string Expected, string Actual, string CurrentPath, string Symbol,
        string Context, string Reason);

    internal static Result Compare(FeatureLedger.Observation row, LedgerMemberFactComparison.Result binding,
        IReadOnlyList<Publication> publications)
    {
        Result No(string reason, string expected = "") => new(false, expected, "", "", "", "", reason);
        if (row.Kind != "help fact") return No("A resolved Help fact is required; no handler or result claim.");
        if (binding.Id != row.Id || binding.Before != row.Detail || binding.FrozenMember.Length == 0)
            return No("The shared member comparer did not establish this exact frozen literal/member anchor.");
        if (!TryDecode(row.Detail, out string literal)) return No("Unsupported literal syntax requires a specific proof.");
        string expected = Normalize(literal);
        if (expected.Length == 0) return No("An empty/whitespace literal is not a published fact.");
        foreach (var publication in publications.OrderBy(p => p.Context, StringComparer.Ordinal))
        {
            if (publication.FrozenMember != binding.FrozenMember || publication.CurrentPath.Length == 0
                || publication.Symbol.Length == 0 || publication.Context.Length == 0) continue;
            string actual = Normalize(publication.Text);
            bool found;
            if (!expected.Any(char.IsLetterOrDigit))
                found = publication.Text.Split('\n').Any(line => Normalize(line) == expected);
            else
            {
                found = false;
                for (int start = actual.IndexOf(expected, StringComparison.Ordinal); start >= 0;
                    start = actual.IndexOf(expected, start + 1, StringComparison.Ordinal))
                {
                    int end = start + expected.Length;
                    bool left = start == 0 || !char.IsLetterOrDigit(expected[0])
                        || (!char.IsLetterOrDigit(actual[start - 1]) && actual[start - 1] != '_'
                            && !(char.IsDigit(expected[0]) && actual[start - 1] is '.' or ',' or '+' or '-'));
                    bool right = end == actual.Length || !char.IsLetterOrDigit(expected[^1])
                        || (!char.IsLetterOrDigit(actual[end]) && actual[end] != '_');
                    if (left && right) { found = true; break; }
                }
            }
            if (found) return new(true, expected, expected, publication.CurrentPath, publication.Symbol,
                publication.Context, "Exact case-sensitive frozen literal is present in the same Help member's resolved text control; whitespace only is normalized.");
        }
        return No("The exact frozen fact is absent from its published Help member; changed wording needs a specific preservation proof.", expected);
    }

    internal static string Normalize(string value) => Regex.Replace(value, @"\s+", " ", RegexOptions.CultureInvariant).Trim();

    private static bool TryDecode(string token, out string value)
    {
        value = "";
        if (token.StartsWith("@\"", StringComparison.Ordinal) && token.EndsWith('"'))
        {
            value = token[2..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
            return true;
        }
        if (!token.StartsWith('"') || token.StartsWith("\"\"\"", StringComparison.Ordinal)) return false;
        try { value = JsonSerializer.Deserialize<string>(token) ?? ""; return true; }
        catch (JsonException) { return false; }
    }
}
