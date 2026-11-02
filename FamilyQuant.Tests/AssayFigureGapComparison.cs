using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static class AssayFigureGapComparison
{
    internal static string? Anchor(FeatureLedger.Observation row, string frozenSource)
    {
        if (row.Source != "FamilyQuant/PresentationFigures.cs") return null;
        bool Owns(int offset) => row.Id == "FQ15-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            row.Source + "|" + offset.ToString(CultureInfo.InvariantCulture) + "|" + row.Kind)))[..12]
            && row.Line == 1 + frozenSource[..offset].Count(c => c == '\n');
        (int Start, int End) Class(string name)
        {
            var declarations = Regex.Matches(frozenSource, @"\bclass\s+" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant).Cast<Match>().ToArray();
            if (declarations.Length != 1) return (-1, -1);
            int start = declarations[0].Index;
            var next = Regex.Match(frozenSource[(start + declarations[0].Length)..], @"\b(?:public|internal)\s+(?:sealed\s+|static\s+)?class\b", RegexOptions.CultureInvariant);
            return (start, next.Success ? start + declarations[0].Length + next.Index : frozenSource.Length);
        }
        if (row.Kind == "shortcut")
        {
            var scope = Class("DeNovoConsolidationChart");
            if (scope.Start < 0) return null;
            foreach (Match member in Regex.Matches(frozenSource, @"\bKey\.(?<field>SourceDisplayName|ConfidenceField|MinimumConfidence)\b", RegexOptions.CultureInvariant))
            {
                if (member.Index < scope.Start || member.Index >= scope.End || !Owns(member.Index)) continue;
                int lineStart = frozenSource.LastIndexOf('\n', member.Index) + 1;
                int lineEnd = frozenSource.IndexOf('\n', member.Index); if (lineEnd < 0) lineEnd = frozenSource.Length;
                if (frozenSource[lineStart..lineEnd].Trim() == row.Detail) return member.Groups["field"].Value;
            }
        }
        if (row.Kind == "code fact")
        {
            var scope = Class("ReferenceComponentSummary");
            if (scope.Start < 0) return null;
            int method = frozenSource.IndexOf("ReferenceComponentSummary FromCuration(", scope.Start, StringComparison.Ordinal);
            if (method < scope.Start || method >= scope.End) return null;
            foreach (Match call in Regex.Matches(frozenSource, "\\.StartsWith\\(\\s*(?<literal>\"(?:\\\\.|[^\"\\\\])*\")", RegexOptions.CultureInvariant))
            {
                Group literal = call.Groups["literal"];
                if (literal.Index >= method && literal.Index < scope.End && Owns(literal.Index) && literal.Value == row.Detail) return "reference";
            }
        }
        return null;
    }
    internal static bool Matches(string key, IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        if (key == "reference")
        {
            var left = before.Where(pair => pair.Key.StartsWith("reference/", StringComparison.Ordinal)).ToArray();
            var right = after.Where(pair => pair.Key.StartsWith("reference/", StringComparison.Ordinal)).ToArray();
            return left.Length >= 3 && left.Length == right.Length && left.All(pair => after.TryGetValue(pair.Key, out string? value) && pair.Value == value);
        }
        return before.TryGetValue("dynamic/" + key, out string? oldValue) && oldValue == "True"
            && after.TryGetValue("dynamic/" + key, out string? newValue) && newValue == "True"
            && new[] { "base", key }.All(item => before.TryGetValue("render/" + item, out string? left)
                && after.TryGetValue("render/" + item, out string? right) && left == right);
    }
}
