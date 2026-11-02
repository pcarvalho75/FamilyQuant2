using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static readonly Regex UiHexColor = new(@"#[0-9a-fA-F]{3}(?:[0-9a-fA-F]|[0-9a-fA-F]{3}|[0-9a-fA-F]{5})?(?![0-9a-fA-F])", RegexOptions.CultureInvariant);
    private static readonly Regex UiNamedColor = new(@"\b(?:Brushes|Colors)\.[A-Z][A-Za-z]+", RegexOptions.CultureInvariant);
    private static readonly Regex UiColorFactory = new(@"\b(?:Color\.(?:FromRgb|FromArgb|FromScRgb)|FreezeRgb|MakeBrush)\s*\((?<args>[^()]{1,180})\)", RegexOptions.CultureInvariant);
    private static readonly Regex UiColorNumber = new(@"^[+-]?(?:0x[0-9a-fA-F]+|(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+))[fFdDuUmM]?$", RegexOptions.CultureInvariant);
    private static readonly Regex UiColorInitializer = new(@"\bnew\s*(?:(?:global::)?(?:System\.Windows\.Media\.)?Color\s*(?:\(\s*\))?|\(\s*\))\s*\{(?<members>[^{}]*)\}", RegexOptions.CultureInvariant);
    private static readonly Regex UiColorComponent = new(@"\b(?:[ARGB]|Sc[ARGB])\s*=\s*(?:\(\s*(?:byte|float|double)\s*\)\s*)?(?<value>[^,}]+)", RegexOptions.CultureInvariant);
    private static readonly Regex UiXamlNamedColor = new("(?:Foreground|Background|BorderBrush|Fill|Stroke|Color)\\s*=\\s*[\"'](?<name>[A-Za-z]+)[\"']|Property\\s*=\\s*[\"'](?:Foreground|Background|BorderBrush|Fill|Stroke|Color)[\"']\\s+Value\\s*=\\s*[\"'](?<name>[A-Za-z]+)[\"']", RegexOptions.CultureInvariant);

    private static IEnumerable<int> UiColorLiteralPositions(string source)
    {
        foreach (Match match in UiHexColor.Matches(source)) yield return match.Index;
        foreach (Match match in UiNamedColor.Matches(source)) yield return match.Index;
        foreach (Match match in UiColorFactory.Matches(source))
        {
            string[] args = match.Groups["args"].Value.Split(',');
            if (args.Length is 3 or 4 && args.All(p => UiColorNumber.IsMatch(p.Trim()))) yield return match.Index;
        }
        foreach (Match match in UiColorInitializer.Matches(source))
            if (UiColorComponent.Matches(match.Groups["members"].Value)
                .Any(component => UiColorNumber.IsMatch(component.Groups["value"].Value.Trim().Replace("_", "", StringComparison.Ordinal))))
                yield return match.Index;
        foreach (Match match in UiXamlNamedColor.Matches(source))
            if (typeof(System.Windows.Media.Colors).GetProperty(match.Groups["name"].Value) != null) yield return match.Index;
    }

    private static void UiColorTests()
    {
        Check("UI color guard rejects planted hex, named brushes, fixed RGB factories and XAML color names",
            new[] { "Foreground=\"#FF00FF\"", "Color.FromRgb(255, 0, 255)", "Brushes.White", "Colors.Magenta",
                "FreezeRgb(0x1F, 0x4E, 0x8C)", "Color.FromRgb(\n255,\n0,\n255)", "Foreground=\"White\"",
                "Property=\"Background\" Value=\"Black\"" }.All(s => UiColorLiteralPositions(s).Any()));
        Check("UI color guard allows token references and dynamic brush factories",
            !UiColorLiteralPositions("Foreground=\"{DynamicResource TextBrush}\"; Color.FromRgb(r, g, b); Palette.Axis; new Color { R = r, G = g, B = b, A = a }").Any());
        Check("UI color guard rejects fixed components in explicit and target-typed Color initializers",
            new[] { "new Color { A = 255, R = 17, G = 34, B = 51 }",
                "new System.Windows.Media.Color() { R = 0x11, G = 0x22, B = 0x33 }",
                "Color value = new() { A = 255, R = red, G = green, B = blue }",
                "new Color { ScA = 1f, ScR = .1f, ScG = .2f, ScB = .3f }",
                "new Color { R = (byte)17, G = green, B = blue }" }
            .All(s => UiColorLiteralPositions(s).Any()));
        string root = GoldenNumbers.FindRepository();
        // Orchestrator M33: roadmap rule 9 protects these export-only deck colors.
        const string protectedExport = "FamilyQuant/PresentationFigures.cs";
        Console.WriteLine("V3 listed exception: " + protectedExport + "; export-only palette protected by roadmap rule 9. File remains untouched.");
        var sites = new List<string>();
        foreach (string project in new[] { "FamilyQuant", "FamilyQuant.Core" })
        foreach (string file in UiSourceFiles(Path.Combine(root, project)))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative is "FamilyQuant/Themes/Tokens.xaml" or "FamilyQuant/ChartPalette.cs" || relative == protectedExport) continue;
            string source = File.ReadAllText(file);
            foreach (int index in UiColorLiteralPositions(source).Distinct().OrderBy(i => i))
            {
                int line = 1 + source.Take(index).Count(c => c == '\n');
                sites.Add(relative + ":" + line.ToString(CultureInfo.InvariantCulture));
            }
        }
        Check("UI colors use tokens/palette with protected export-only PresentationFigures.cs as the one listed exception",
            sites.Count == 0, string.Join(", ", sites));
        EnsureApplication();
        foreach (var mode in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        {
            Themes.ThemeManager.Apply(mode);
            var window = DeNovo.ThreeWayWindow.Create("Token fixture", Array.Empty<DeNovo.ThreeWayPartitionRow>());
            var caption = ((System.Windows.Controls.Grid)window.Content).Children.OfType<System.Windows.Controls.TextBlock>().Single();
            Check(mode + " retained partition window uses current surface and caption tokens",
                (window.Background as System.Windows.Media.SolidColorBrush)?.Color
                    == ((System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.FindResource("CanvasBrush")).Color
                && (caption.Foreground as System.Windows.Media.SolidColorBrush)?.Color
                    == ((System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.FindResource("MutedBrush")).Color);
            window.Close();
        }
        Themes.ThemeManager.Apply(Themes.AppTheme.Dark);
    }
}
