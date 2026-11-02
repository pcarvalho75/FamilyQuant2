using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void UiIconTests()
    {
        Check("UI icon guard catches literal, escaped and entity emoji",
            new[] { "Text=\"\U0001F512\"", "Text=\"\\U0001F512\"", "\"\\uD83D\\uDD12\"",
                "Text=\"&#x1F512;\"", "Text=\"&#128274;\"", "\"\\u2713 Loaded\"", "1\uFE0F\u20E3" }
                .All(HasDecorativeEmoji));
        Check("UI icon guard allows scientific notation and Fluent private-use glyphs",
            !HasDecorativeEmoji("z >= 2; M+1; 80%; peptide → family; \uE73E; &#xE73E;"));

        string root = GoldenNumbers.FindRepository();
        var violations = new List<string>();
        foreach (string project in new[] { "FamilyQuant", "FamilyQuant.Core" })
        foreach (string path in UiSourceFiles(Path.Combine(root, project)))
        {
            int line = 0;
            foreach (string text in File.ReadLines(path))
            {
                line++;
                if (HasDecorativeEmoji(text))
                    violations.Add(Path.GetRelativePath(root, path) + ":" + line.ToString(CultureInfo.InvariantCulture));
            }
        }
        Check("UI source has no emoji or decorative text symbols; use Segoe Fluent Icons",
            violations.Count == 0, string.Join(", ", violations));

        EnsureApplication();
        foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
        {
            FamilyQuant.Themes.ThemeManager.Apply(theme);
            var icon = new TextBlock { Text = "\uE73E", Style = (Style)Application.Current.FindResource("FluentIcon") };
            var typeface = new Typeface(icon.FontFamily, icon.FontStyle, icon.FontWeight, icon.FontStretch);
            Check(theme + " Fluent icons use the Windows font, token ink and an available glyph",
                icon.FontFamily.Source == "Segoe Fluent Icons"
                && icon.Foreground == Application.Current.FindResource("TextBrush")
                && !icon.Focusable && !icon.IsHitTestVisible
                && typeface.TryGetGlyphTypeface(out var glyphs) && glyphs.CharacterToGlyphMap.ContainsKey(0xE73E));
        }
        FamilyQuant.Themes.ThemeManager.Apply(FamilyQuant.Themes.AppTheme.Dark);
    }

    // Scan product source, including shared Help strings. Generated and vendor sources are not UI copy.
    private static IEnumerable<string> UiSourceFiles(string directory)
    {
        foreach (string path in Directory.EnumerateFiles(directory).OrderBy(p => p, StringComparer.Ordinal))
            if ((path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
                && !path.EndsWith(".Designer.cs", StringComparison.Ordinal)) yield return path;
        foreach (string child in Directory.EnumerateDirectories(directory).OrderBy(p => p, StringComparer.Ordinal))
            if (Path.GetFileName(child) is not ("bin" or "obj" or "DLLs"))
                foreach (string path in UiSourceFiles(child)) yield return path;
    }

    private static bool HasDecorativeEmoji(string source)
    {
        string decoded = Regex.Replace(source, @"\\(?:u([0-9a-fA-F]{4})|U([0-9a-fA-F]{8}))", match =>
        {
            int value = int.Parse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value,
                NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return value <= char.MaxValue ? ((char)value).ToString() : char.ConvertFromUtf32(value);
        });
        decoded = WebUtility.HtmlDecode(decoded);
        foreach (Rune rune in decoded.EnumerateRunes())
            if (rune.Value is >= 0x1F000 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF
                or 0xFE0F or 0x200D or 0x20E3 or 0x00A9 or 0x00AE or 0x203C or 0x2049
                or 0x2122 or 0x2139 or >= 0x2194 and <= 0x2199 or 0x21A9 or 0x21AA
                or >= 0x231A and <= 0x231B or 0x2328 or 0x23CF or >= 0x23E9 and <= 0x23F3
                or >= 0x23F8 and <= 0x23FA or 0x24C2 or 0x25AA or 0x25AB or 0x25B6 or 0x25C0
                or >= 0x25FB and <= 0x25FE or 0x2934 or 0x2935 or >= 0x2B05 and <= 0x2B07
                or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 or 0x3030 or 0x303D or 0x3297 or 0x3299)
                return true;
        return false;
    }
}
