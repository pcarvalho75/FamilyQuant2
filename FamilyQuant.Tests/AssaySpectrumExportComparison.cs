using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static class AssaySpectrumExportComparison
{
    private const string Literal = "\"(?:\\\\.|[^\"\\\\])*\"";
    internal static AssaySpectrumPropertyComparison.Result Compare(FeatureLedger.Observation row, string source, IReadOnlyDictionary<string, string> actuals)
    {
        AssaySpectrumPropertyComparison.Result No(string reason) => new(row.Id, "", "", "", "", false, reason);
        if (row.Source != "FamilyQuant/SpectrumComparisonWindow.cs" || row.Kind is not ("code fact" or "format/export")) return No("Unsupported export observation.");
        bool Owns(int offset, string raw) => row.Detail == raw && row.Line == 1 + source[..offset].Count(c => c == '\n')
            && row.Id == "FQ15-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Source + "|" + offset.ToString(CultureInfo.InvariantCulture) + "|" + row.Kind)))[..12];
        var candidates = new List<AssaySpectrumPropertyComparison.Result>();
        foreach (var method in new[] { (Name: "SaveCopy", Kind: "csv"), (Name: "SaveAcceptedScoreScatter", Kind: "plot") })
        {
            var header = Regex.Matches(source, @"(?m)^        private static void " + method.Name + @"\s*\(", RegexOptions.CultureInvariant).Cast<Match>().ToArray();
            if (header.Length != 1) continue;
            int end = source.IndexOf("\n        }", header[0].Index, StringComparison.Ordinal); if (end < 0) continue;
            int begin = header[0].Index; string body = source[begin..end];
            foreach (Match dialog in Regex.Matches(body, @"new SaveFileDialog\s*\{(?<body>[\s\S]*?)\};", RegexOptions.CultureInvariant))
            foreach (Match property in Regex.Matches(dialog.Groups["body"].Value, @"\b(?<property>Title|Filter|DefaultExt)\s*=\s*(?<value>" + Literal + ")", RegexOptions.CultureInvariant))
            {
                Group value = property.Groups["value"];
                int offset = begin + dialog.Groups["body"].Index + value.Index;
                if (!Owns(offset, value.Value)) continue;
                string propertyName = property.Groups["property"].Value;
                string key = method.Kind + "/dialog-cancel/" + (propertyName == "DefaultExt" ? "defaultExt" : propertyName.ToLowerInvariant());
                string expected = JsonSerializer.Deserialize<string>(value.Value)!;
                if (propertyName == "DefaultExt") expected = new Microsoft.Win32.SaveFileDialog { DefaultExt = expected }.DefaultExt;
                bool exists = actuals.TryGetValue(key, out string? actual);
                candidates.Add(new(row.Id, key, expected, expected, actual ?? "<missing>", exists && actual == expected,
                    "Actual routed action constructs the frozen native dialog property before cancellation."));
            }
            foreach (Match status in Regex.Matches(body, @"status\.Text\s*=\s*(?<value>(?:" + Literal + @"|[^"";])+)\s*;", RegexOptions.CultureInvariant))
            {
                Group expression = status.Groups["value"];
                var literals = Regex.Matches(expression.Value, Literal, RegexOptions.CultureInvariant).Cast<Match>();
                if (!literals.Any(token => Owns(begin + expression.Index + token.Index, token.Value))) continue;
                string branch = expression.Value.Contains("ex.Message", StringComparison.Ordinal) ? "failure"
                    : expression.Value.Contains("dialog.FileName", StringComparison.Ordinal) ? "success" : "";
                if (branch.Length == 0) continue;
                string key = method.Kind + "/" + branch + "/status";
                if (!TryTemplate(expression.Value, method.Kind + "/" + branch, actuals, out string pattern)) continue;
                bool exists = actuals.TryGetValue(key, out string? actual);
                candidates.Add(new(row.Id, key, pattern, pattern, actual ?? "<missing>", exists && Regex.IsMatch(actual!, "\\A" + pattern + "\\z", RegexOptions.CultureInvariant | RegexOptions.Singleline),
                    "Frozen complete status expression matches the actual native export status; only the dynamic exception message is a wildcard."));
            }
            foreach (Match companion in Regex.Matches(body, @"Path\.ChangeExtension\(dialog\.FileName,\s*(?<value>" + Literal + @")\)", RegexOptions.CultureInvariant))
            {
                Group value = companion.Groups["value"];
                if (!Owns(begin + value.Index, value.Value)) continue;
                string expected = JsonSerializer.Deserialize<string>(value.Value)!;
                string key = method.Kind + "/success/companion";
                bool exists = actuals.TryGetValue(key, out string? path);
                string actual = exists ? System.IO.Path.GetExtension(path!) : "<missing>";
                candidates.Add(new(row.Id, key, expected, expected, actual, exists && actual == expected,
                    "Successful actual routed plot export wrote the native companion CSV at the frozen extension."));
            }
        }
        return candidates.Count == 1 ? candidates[0] : No("No unique supported frozen export argument/status anchor.");
    }

    private static bool TryTemplate(string expression, string scenario, IReadOnlyDictionary<string, string> values, out string pattern)
    {
        pattern = ""; var parts = new List<string>(); int start = 0, depth = 0; bool quoted = false, escaped = false;
        for (int i = 0; i < expression.Length; i++)
        {
            char c = expression[i];
            if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
            if (c == '"') quoted = true; else if (c == '(') depth++; else if (c == ')') depth--;
            else if (c == '+' && depth == 0) { parts.Add(expression[start..i].Trim()); start = i + 1; }
        }
        parts.Add(expression[start..].Trim());
        foreach (string part in parts)
        {
            if (Regex.IsMatch(part, "\\A" + Literal + "\\z", RegexOptions.CultureInvariant))
                pattern += Regex.Escape(JsonSerializer.Deserialize<string>(part)!);
            else if (part == "ex.Message") pattern += ".+";
            else if (part is "dialog.FileName" or "csvPath")
            {
                string key = scenario + (part == "dialog.FileName" ? "/selected" : "/companion");
                if (!values.TryGetValue(key, out string? value)) return false;
                pattern += Regex.Escape(value);
            }
            else
            {
                var number = Regex.Match(part, @"\A(?<value>rows\.Count|scatter\.Total|scatter\.Matches|scatter\.Mismatches)\.ToString\(\s*\""(?<format>[^\""\\]+)\""\s*,\s*CultureInfo\.CurrentCulture\s*\)\z", RegexOptions.CultureInvariant);
                if (!number.Success) return false;
                string name = number.Groups["value"].Value switch { "rows.Count" => "rows", "scatter.Total" => "total", "scatter.Matches" => "matches", _ => "mismatches" };
                if (!values.TryGetValue("inputs/" + name, out string? value) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)) return false;
                pattern += Regex.Escape(count.ToString(number.Groups["format"].Value, CultureInfo.InvariantCulture));
            }
        }
        return true;
    }
}
