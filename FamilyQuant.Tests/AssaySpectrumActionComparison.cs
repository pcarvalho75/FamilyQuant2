using System.Collections.Generic;
using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static class AssaySpectrumActionComparison
{
    private const string Literal = "\"(?:\\\\.|[^\"\\\\])*\"";
    private static readonly (string Method, string Button)[] Actions =
    {
        ("SaveCopy", "ButtonSaveSpectrumComparisonCsv"),
        ("SaveAcceptedScoreScatter", "ButtonSaveAcceptedScoreScatter")
    };

    internal static AssaySpectrumPropertyComparison.Result Compare(FeatureLedger.Observation row, string source,
        IReadOnlyDictionary<string, string> frozen, IReadOnlyDictionary<string, string> current)
    {
        AssaySpectrumPropertyComparison.Result No(string reason) => new(row.Id, "", "", "", "", false, reason);
        if (row.Source != "FamilyQuant/SpectrumComparisonWindow.cs" || row.Kind is not ("code fact" or "handler" or "code control"))
            return No("Only this view's literal refusal or actual Click wiring is supported.");
        bool Owns(int offset, string detail) => row.Id == "FQ15-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            row.Source + "|" + offset.ToString(CultureInfo.InvariantCulture) + "|" + row.Kind)))[..12]
            && row.Line == 1 + source[..offset].Count(c => c == '\n') && row.Detail == detail;
        var anchors = new List<(string Key, string Expected)>();
        foreach (Match declaration in Regex.Matches(source,
            @"\bvar\s+(?<variable>\w+)\s*=\s*(?<ctor>new\s+(?<type>Button|DataGrid))\s*\{(?<body>[\s\S]*?)\};", RegexOptions.CultureInvariant))
        {
            Group body = declaration.Groups["body"];
            var name = Regex.Match(body.Value, @"\bName\s*=\s*(?<value>" + Literal + ")", RegexOptions.CultureInvariant);
            if (!name.Success) continue;
            string controlName = JsonSerializer.Deserialize<string>(name.Groups["value"].Value)!;
            if (row.Kind == "code control" && Owns(declaration.Groups["ctor"].Index, declaration.Groups["ctor"].Value))
                anchors.Add(("named/" + controlName + "/type", declaration.Groups["type"].Value));
            if (row.Kind != "code fact") continue;
            foreach (Match property in Regex.Matches(body.Value, @"\b(?<property>Name|Content|ToolTip)\s*=\s*(?<value>"
                + Literal + @"(?:\s*\+\s*" + Literal + @")*)", RegexOptions.CultureInvariant))
            {
                Group value = property.Groups["value"];
                var tokens = Regex.Matches(value.Value, Literal, RegexOptions.CultureInvariant).Cast<Match>().ToArray();
                if (!tokens.Any(token => Owns(body.Index + value.Index + token.Index, token.Value))) continue;
                string expected = string.Concat(tokens.Select(token => JsonSerializer.Deserialize<string>(token.Value)));
                string member = property.Groups["property"].Value;
                anchors.Add(("named/" + controlName + "/" + (member == "ToolTip" ? "tooltip" : member), expected));
            }
        }
        if (row.Kind == "code control")
            foreach (Match constructor in Regex.Matches(source, @"\bnew\s+DataGridTextColumn\b", RegexOptions.CultureInvariant))
                if (Owns(constructor.Index, constructor.Value)) anchors.Add(("grid/columnTypes", "DataGridTextColumn"));
        foreach (var action in Actions)
        {
            string pattern = @"\bprivate\s+static\s+(?:async\s+)?void\s+" + action.Method
                + @"\s*\([^)]*\)\s*\{\s*if\s*\(\s*PresentationSource\.FromVisual\(button\)\s*==\s*null\s*\)\s*\{\s*status\.Text\s*=\s*(?<message>"
                + Literal + @"(?:\s*\+\s*" + Literal + @")*)\s*;\s*return\s*;";
            var methods = Regex.Matches(source, pattern, RegexOptions.CultureInvariant).Cast<Match>().ToArray();
            if (methods.Length != 1) continue;
            Group argument = methods[0].Groups["message"];
            var tokens = Regex.Matches(argument.Value, Literal, RegexOptions.CultureInvariant).Cast<Match>().ToArray();
            string expected;
            try { expected = string.Concat(tokens.Select(token => JsonSerializer.Deserialize<string>(token.Value))); }
            catch (JsonException) { continue; }
            bool owns = row.Kind == "code fact" && tokens.Any(token => Owns(argument.Index + token.Index, token.Value));
            if (row.Kind == "handler")
            {
                foreach (Match declaration in Regex.Matches(source, @"\bvar\s+(?<variable>\w+)\s*=\s*new\s+Button\s*\{(?<body>[\s\S]*?)\};", RegexOptions.CultureInvariant))
                {
                    if (!Regex.IsMatch(declaration.Groups["body"].Value, @"\bName\s*=\s*" + Regex.Escape(JsonSerializer.Serialize(action.Button)), RegexOptions.CultureInvariant)) continue;
                    string variable = declaration.Groups["variable"].Value;
                    string wire = @"\b" + Regex.Escape(variable) + @"\.Click\s*\+=\s*\([^)]*\)\s*=>\s*" + action.Method + @"\s*\(";
                    foreach (Match match in Regex.Matches(source, wire, RegexOptions.CultureInvariant))
                    {
                        int start = source.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
                        int end = source.IndexOf('\n', match.Index); if (end < 0) end = source.Length;
                        owns |= Owns(match.Index, source[start..end].Trim());
                    }
                }
            }
            if (owns) anchors.Add(("actions/" + action.Button + "/refusal", expected));
        }
        if (anchors.Count != 1) return No("No unique frozen refusal/action anchor for this observation.");
        var anchor = anchors[0];
        bool hasBefore = frozen.TryGetValue(anchor.Key, out string? before), hasAfter = current.TryGetValue(anchor.Key, out string? after);
        return new(row.Id, anchor.Key, anchor.Expected, before ?? "<missing>", after ?? "<missing>",
            hasBefore && hasAfter && before == anchor.Expected && after == before,
            anchor.Key.StartsWith("actions/", StringComparison.Ordinal)
                ? "Actual named routed Click updates the undisplayed-window refusal; no dialog, write or render is claimed."
                : "Actual constructed named control/property agrees; no handler or app-navigation claim for this anchor.");
    }
}
