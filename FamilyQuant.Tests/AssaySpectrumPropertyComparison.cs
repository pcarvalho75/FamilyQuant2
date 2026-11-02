using System.Collections.Generic;
using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static class AssaySpectrumPropertyComparison
{
    internal sealed record Result(string Id, string Key, string Expected, string FrozenActual, string Actual, bool Passed, string Reason);

    private const string Literal = "\"(?:\\\\.|[^\"\\\\])*\"";
    private static readonly Regex Columns = new(@"grid\.Columns\.Add\s*\(\s*Column\s*\(\s*(?<header>" + Literal
        + @")\s*,\s*nameof\s*\(\s*SpectrumComparisonRow\.(?<binding>\w+)\s*\)\s*,\s*[0-9.]+\s*,\s*(?<tooltip>"
        + Literal + @"(?:\s*\+\s*" + Literal + @")*)\s*(?:,\s*(?<format>" + Literal + @"))?\s*\)\s*\)\s*;", RegexOptions.CultureInvariant);

    internal static Result Compare(FeatureLedger.Observation row, string frozenSource,
        IReadOnlyDictionary<string, string> frozen, IReadOnlyDictionary<string, string> current)
    {
        Result No(string reason) => new(row.Id, "", "", "", "", false, reason);
        if (row.Kind is not ("code fact" or "format/export") || row.Source != "FamilyQuant/SpectrumComparisonWindow.cs")
            return No("Only assigned column value/format observations are eligible; no action claim.");
        var anchors = new List<(string Key, string Expected)>();
        foreach (Match column in Columns.Matches(frozenSource))
        foreach (string property in new[] { "header", "tooltip", "format" })
        {
            Group argument = column.Groups[property];
            if (!argument.Success) continue;
            var tokens = Regex.Matches(argument.Value, Literal, RegexOptions.CultureInvariant).Cast<Match>().ToArray();
            foreach (Match token in tokens)
            {
                int offset = argument.Index + token.Index;
                string id = "FQ15-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Source + "|"
                    + offset.ToString(CultureInfo.InvariantCulture) + "|" + row.Kind)))[..12];
                if (id != row.Id || token.Value != row.Detail || 1 + frozenSource[..offset].Count(c => c == '\n') != row.Line) continue;
                try
                {
                    string expected = string.Concat(tokens.Select(part => JsonSerializer.Deserialize<string>(part.Value)));
                    anchors.Add(("columns/" + column.Groups["binding"].Value + "/" + property, expected));
                }
                catch (JsonException) { return No("Unsupported C# literal escape requires a dedicated check."); }
            }
        }
        if (anchors.Count != 1) return No("The frozen ID has no unique supported column argument anchor.");
        var anchor = anchors[0];
        bool beforeExists = frozen.TryGetValue(anchor.Key, out string? before), afterExists = current.TryGetValue(anchor.Key, out string? actual);
        return new(row.Id, anchor.Key, anchor.Expected, before ?? "<missing>", actual ?? "<missing>",
            beforeExists && afterExists && before == anchor.Expected && actual == before,
            "Compare the complete native property at its exact frozen column binding, never a same-text fallback.");
    }
}
