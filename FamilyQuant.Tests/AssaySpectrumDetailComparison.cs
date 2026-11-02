using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static class AssaySpectrumDetailComparison
{
    private const string Literal = "\"(?:\\\\.|[^\"\\\\])*\"";
    internal static AssaySpectrumPropertyComparison.Result Compare(FeatureLedger.Observation row, string source,
        IReadOnlyDictionary<string, string> emptyBefore, IReadOnlyDictionary<string, string> fullBefore,
        IReadOnlyDictionary<string, string> emptyAfter, IReadOnlyDictionary<string, string> fullAfter)
    {
        AssaySpectrumPropertyComparison.Result No(string reason) => new(row.Id, "", "", "", "", false, reason);
        if (row.Kind != "code fact" || row.Source != "FamilyQuant/SpectrumComparisonWindow.cs") return No("Only this view's detail facts are eligible.");
        bool Owns(int at, string raw) => raw == row.Detail && row.Line == 1 + source[..at].Count(c => c == '\n')
            && row.Id == "FQ15-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Source + "|" + at.ToString(CultureInfo.InvariantCulture) + "|" + row.Kind)))[..12];
        var anchors = new List<(string Key, string Empty, string Full)>();
        foreach (Match declaration in Regex.Matches(source, @"\bvar\s+(?<role>explanation|count|path)\s*=\s*new\s+TextBlock\s*\{(?<body>[\s\S]*?)\};", RegexOptions.CultureInvariant))
        foreach (string property in new[] { "Text", "ToolTip" })
        {
            Group body = declaration.Groups["body"];
            var assignment = Regex.Match(body.Value, @"\b" + property + @"\s*=\s*", RegexOptions.CultureInvariant);
            if (!assignment.Success) continue;
            int start = assignment.Index + assignment.Length, end = start, depth = 0; bool quoted = false, escaped = false;
            for (; end < body.Length; end++)
            {
                char c = body.Value[end];
                if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
                if (c == '"') quoted = true; else if (c == '(') depth++; else if (c == ')') depth--; else if (c == ',' && depth == 0) break;
            }
            string expression = body.Value[start..end].Trim();
            var literals = Regex.Matches(expression, Literal, RegexOptions.CultureInvariant).Cast<Match>().ToArray();
            if (!literals.Any(token => Owns(body.Index + start + token.Index, token.Value))) continue;
            string role = declaration.Groups["role"].Value;
            if (property == "ToolTip" || role == "explanation")
            {
                if (!Regex.IsMatch(expression, "\\A" + Literal + @"(?:\s*\+\s*" + Literal + @")*\s*\z", RegexOptions.CultureInvariant)) continue;
                string text = string.Concat(literals.Select(token => JsonSerializer.Deserialize<string>(token.Value)));
                anchors.Add(("view/" + role + (property == "ToolTip" ? "Tooltip" : ""), text, text));
            }
            else if (role == "count")
            {
                var count = Regex.Match(expression, @"\AdisplayRows\.Count\.ToString\(\s*(?<format>" + Literal + @")\s*,\s*CultureInfo\.CurrentCulture\s*\)\s*\+\s*(?<suffix>" + Literal + @")\z", RegexOptions.CultureInvariant);
                if (!count.Success) continue;
                string format = JsonSerializer.Deserialize<string>(count.Groups["format"].Value)!, suffix = JsonSerializer.Deserialize<string>(count.Groups["suffix"].Value)!;
                anchors.Add(("view/count", 0.ToString(format, CultureInfo.InvariantCulture) + suffix, 1234.ToString(format, CultureInfo.InvariantCulture) + suffix));
            }
            else if (role == "path")
            {
                var path = Regex.Match(expression, @"\Astring\.IsNullOrWhiteSpace\(existingCsvPath\)\s*\?\s*(?<empty>" + Literal + @")\s*:\s*(?<prefix>" + Literal + @")\s*\+\s*existingCsvPath\z", RegexOptions.CultureInvariant);
                if (!path.Success) continue;
                anchors.Add(("view/path", JsonSerializer.Deserialize<string>(path.Groups["empty"].Value)!, JsonSerializer.Deserialize<string>(path.Groups["prefix"].Value)! + "fixture/comparison.csv"));
            }
        }
        if (anchors.Count != 1) return No("No unique complete frozen view-detail expression anchor.");
        var anchor = anchors[0];
        bool exists = emptyBefore.TryGetValue(anchor.Key, out string? eb) && fullBefore.TryGetValue(anchor.Key, out string? fb)
            && emptyAfter.TryGetValue(anchor.Key, out string? ea) && fullAfter.TryGetValue(anchor.Key, out string? fa);
        string Get(IReadOnlyDictionary<string, string> values) => values.TryGetValue(anchor.Key, out string? value) ? value : "<missing>";
        bool pass = exists && Get(emptyBefore) == anchor.Empty && Get(fullBefore) == anchor.Full && Get(emptyAfter) == anchor.Empty && Get(fullAfter) == anchor.Full;
        return new(row.Id, anchor.Key, anchor.Empty + " / " + anchor.Full, Get(emptyBefore) + " / " + Get(fullBefore), Get(emptyAfter) + " / " + Get(fullAfter), pass,
            "Complete native view detail agrees for zero/1234 rows and missing/present CSV path; help text is captured from the actual callback when relocated.");
    }
}
