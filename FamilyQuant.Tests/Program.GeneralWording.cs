using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    // Product-authored copy is database independent. Loaded database names are user data.
    private static readonly Regex DatabaseSpecificCopy = new(
        @"\b(?:venoms?|toxins?|snakes?|vipers?|Bothrops|jararaca|Viperidae|Homo\s+sapiens|Mus\s+musculus)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string DecodeCopy(string text) => WebUtility.HtmlDecode(Regex.Replace(text,
        @"\\(?:u([0-9a-fA-F]{4})|U([0-9a-fA-F]{8}))", match =>
        char.ConvertFromUtf32(Convert.ToInt32(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, 16))));

    private static string[] GeneralCopyLiterals(string source) => Regex.Matches(source,
        "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|(?<copy>@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\")")
        .Where(match => match.Groups["copy"].Success).Select(match => DecodeCopy(match.Value)).ToArray();

    private static void GeneralWordingTests()
    {
        Check("database-independent copy guard catches organism, venom and toxin names, including escapes",
            new[] { "snake", "venom", "toxin", "Bothrops jararaca", "Homo sapiens", "\\u0076enom" }
                .All(text => DatabaseSpecificCopy.IsMatch(DecodeCopy(text))));
        Check("database-independent copy guard excludes comments and permits general examples",
            !GeneralCopyLiterals("// venom\n/* snake */ const string Copy = \"In a redundant database\";")
                .Any(DatabaseSpecificCopy.IsMatch));
        string root = GoldenNumbers.FindRepository();
        var violations = new System.Collections.Generic.List<string>();
        foreach (string project in new[] { "FamilyQuant", "FamilyQuant.Core" })
        foreach (string path in UiSourceFiles(Path.Combine(root, project)))
        {
            // Protected export/deck producer. Its fixed historical text is outside product UI.
            if (Path.GetFileName(path) == "PresentationFigures.cs") continue;
            var copy = path.EndsWith(".xaml", StringComparison.Ordinal)
                ? XDocument.Load(path).Descendants().SelectMany(element => element.Attributes().Select(a => a.Value)
                    .Concat(element.Nodes().OfType<XText>().Select(t => t.Value))).ToArray()
                : GeneralCopyLiterals(File.ReadAllText(path));
            violations.AddRange(copy.Where(text => DatabaseSpecificCopy.IsMatch(text))
                .Select(text => Path.GetRelativePath(root, path) + ": " + text));
        }
        Check("no user-visible text may name an organism, a venom or a toxin",
            violations.Count == 0, string.Join("; ", violations));
    }
}
