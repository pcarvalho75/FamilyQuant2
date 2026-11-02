using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static class AssayBuildWorkflowComparison
{
    internal static AssaySpectrumPropertyComparison.Result Compare(FeatureLedger.Observation row, string source,
        IReadOnlyDictionary<string, string> actual)
    {
        AssaySpectrumPropertyComparison.Result No(string reason) => new(row.Id, "", "", "", "", false, reason);
        bool Owns(int offset) => row.Id == "FQ15-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            row.Source + "|" + offset.ToString(CultureInfo.InvariantCulture) + "|" + row.Kind)))[..12]
            && row.Line == 1 + source[..offset].Count(c => c == '\n');
        string? name = null;
        if (row.Kind == "handler" && row.Source == "FamilyQuant/USearch.xaml")
        {
            var match = Regex.Match(row.Detail, @"^(?<name>\w+)\.Click = (?<method>\w+_Click)$", RegexOptions.CultureInvariant);
            if (!match.Success) return No("No exact named Click binding.");
            name = match.Groups["name"].Value;
            var xml = XDocument.Parse(source);
            var controls = xml.Root!.DescendantsAndSelf().Where(element => element.Name.LocalName == "Button"
                && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name)).ToArray();
            if (controls.Length != 1 || controls[0].Attribute("Click")?.Value != match.Groups["method"].Value
                || match.Groups["method"].Value != name + "_Click") return No("Frozen binding does not identify this native action.");
        }
        else if (row.Kind == "handler definition" && row.Source == "FamilyQuant/USearch.xaml.cs")
        {
            var matches = Regex.Matches(source, @"\bprivate\s+(?:async\s+)?void\s+(?<name>\w+)_Click\s*\([^\r\n]*", RegexOptions.CultureInvariant)
                .Cast<Match>().Where(match => Owns(match.Index) && match.Value.Trim() == row.Detail).ToArray();
            if (matches.Length != 1) return No("No unique frozen native handler declaration.");
            name = matches[0].Groups["name"].Value;
        }
        if (name != null)
        {
            string[] branches = name switch
            {
                "ButtonCalculateFDR" => new[] { "missing", "success" },
                "ButtonDivideAndConquer" or "ButtonScanIdentities" => new[] { "missing", "failure" },
                "ButtonRemoveRedundancy" => new[] { "missing", "success" },
                _ => Array.Empty<string>()
            };
            if (branches.Length == 0) return No("Changed select/build flow needs Paulo's wording/flow record.");
            string[] keys = branches.Select(branch => "workflow/" + name + "/" + branch).ToArray();
            string observed = string.Join("/", keys.Select(key => actual.TryGetValue(key, out string? value) ? value : "<missing>"));
            return new(row.Id, string.Join(" + ", keys), "True/True", "frozen named action", observed,
                observed == "True/True", "Actual named routed Click executes this action's recorded branches; scan/search success is not claimed by an error-branch proof.");
        }
        if (row.Source != "FamilyQuant/USearch.xaml.cs" || row.Kind != "format/export") return No("No supported workflow/path observation.");
        string expected;
        try { expected = JsonSerializer.Deserialize<string>(row.Detail)!; }
        catch (JsonException) { return No("No simple frozen output suffix."); }
        string key = expected switch { ".fcf" => "output/fcfExtension", "_NO_REDUNDANCY.fasta" => "output/redundancySuffix", _ => "" };
        if (key.Length == 0) return No("Dialog filter or prose requires a separate exact comparison or wording ruling.");
        var offsets = new System.Collections.Generic.List<int>();
        for (int at = source.IndexOf(row.Detail, StringComparison.Ordinal); at >= 0; at = source.IndexOf(row.Detail, at + row.Detail.Length, StringComparison.Ordinal))
            if (Owns(at)) offsets.Add(at);
        if (offsets.Count != 1) return No("No unique frozen output suffix anchor.");
        string observedPath = actual.TryGetValue(key, out string? path) ? path : "<missing>";
        bool successful = actual.TryGetValue("native/frozenCurrent", out string? native) && native == "True"
            && actual.TryGetValue(expected == ".fcf" ? "workflow/build/success" : "workflow/ButtonRemoveRedundancy/success", out string? success) && success == "True";
        return new(row.Id, key, expected, expected, observedPath, successful && observedPath == expected,
            "Actual named screen action writes the frozen output suffix and its native contents match the frozen program fixture.");
    }
}
