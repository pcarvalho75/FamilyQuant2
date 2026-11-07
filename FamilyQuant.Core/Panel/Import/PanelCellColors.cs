using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Globalization;
using System.Linq;
using A = DocumentFormat.OpenXml.Drawing;

namespace FamilyQuant.Panels.Import;

/// <summary>Plain colour words for display and screen readers. A word describes the colour; it never says what
/// the colour means in the user's sheet.</summary>
public static class PanelColorNames
{
    public static string Describe(string color)
    {
        if (!TryParse(color, out int r, out int g, out int b)) return string.Empty;
        var (hue, saturation, lightness) = PanelCellColors.ToHsl(r, g, b);
        if (saturation < 0.12 || lightness > 0.97)
            return lightness > 0.95 ? "white" : lightness > 0.7 ? "light grey" : lightness > 0.35 ? "grey" : lightness > 0.12 ? "dark grey" : "black";
        if (lightness < 0.08) return "black";
        string word = hue switch
        {
            < 15 => "red", < 40 => "orange", < 70 => "yellow", < 160 => "green", < 200 => "turquoise",
            < 255 => "blue", < 290 => "purple", < 335 => "pink", _ => "red",
        };
        if (word == "orange" && lightness < 0.4) return "brown";
        return lightness > 0.75 ? "light " + word : lightness < 0.3 ? "dark " + word : word;
    }

    internal static bool TryParse(string color, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (color is not { Length: 7 } || color[0] != '#'
            || !int.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value)) return false;
        r = value >> 16 & 0xFF; g = value >> 8 & 0xFF; b = value & 0xFF;
        return true;
    }
}

/// <summary>Resolves a cell's direct pattern fill to #RRGGBB: explicit RGB, the workbook theme with tint, or the
/// indexed palette. Conditional formatting, table styles, gradients and row or column styles are not read.</summary>
internal sealed class PanelCellColors
{
    // The standard Excel indexed palette, used when the workbook does not define its own.
    private static readonly string[] DefaultIndexed =
    {
        "000000", "FFFFFF", "FF0000", "00FF00", "0000FF", "FFFF00", "FF00FF", "00FFFF",
        "000000", "FFFFFF", "FF0000", "00FF00", "0000FF", "FFFF00", "FF00FF", "00FFFF",
        "800000", "008000", "000080", "808000", "800080", "008080", "C0C0C0", "808080",
        "9999FF", "993366", "FFFFCC", "CCFFFF", "660066", "FF8080", "0066CC", "CCCCFF",
        "000080", "FF00FF", "FFFF00", "00FFFF", "800080", "800000", "008080", "0000FF",
        "00CCFF", "CCFFFF", "CCFFCC", "FFFF99", "99CCFF", "FF99CC", "CC99FF", "FFCC99",
        "3366FF", "33CCCC", "99CC00", "FFCC00", "FF9900", "FF6600", "666699", "969696",
        "003366", "339966", "003300", "333300", "993300", "993366", "333399", "333333",
    };
    // Office theme colours in Excel's theme index order (lt1, dk1, lt2, dk2, accent1 to 6, hlink, folHlink),
    // used only when the workbook carries no theme part.
    private static readonly string[] DefaultTheme =
        { "FFFFFF", "000000", "E7E6E6", "44546A", "4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47", "0563C1", "954F72" };

    private readonly CellFormat[] formats;
    private readonly Fill[] fills;
    private readonly string[] indexed;
    private readonly string[] theme;

    public PanelCellColors(WorkbookPart workbook)
    {
        var styles = workbook.WorkbookStylesPart?.Stylesheet;
        formats = styles?.CellFormats?.Elements<CellFormat>().ToArray() ?? Array.Empty<CellFormat>();
        fills = styles?.Fills?.Elements<Fill>().ToArray() ?? Array.Empty<Fill>();
        var custom = styles?.Colors?.IndexedColors?.Elements<RgbColor>().Select(c => Hex(c.Rgb?.Value)).ToArray();
        indexed = custom is { Length: > 0 } && custom.All(c => c.Length > 0) ? custom : DefaultIndexed;
        theme = Theme(workbook.ThemePart?.Theme?.ThemeElements?.ColorScheme) ?? DefaultTheme;
    }

    /// <summary>#RRGGBB for a cell with a visible pattern fill; empty otherwise.</summary>
    public string Fill(uint? styleIndex)
    {
        if (styleIndex is not { } s || s >= formats.Length) return string.Empty;
        uint fillId = formats[s].FillId?.Value ?? 0;
        if (fillId >= fills.Length || fills[fillId].PatternFill is not { } pattern) return string.Empty;
        var type = pattern.PatternType?.Value;
        if (type == null || type == PatternValues.None || type == PatternValues.Gray125) return string.Empty;
        return Resolve(pattern.ForegroundColor);
    }

    private string Resolve(ColorType? color)
    {
        if (color == null || color.Auto?.Value == true) return string.Empty;
        string rgb;
        if (color.Rgb?.Value is { Length: > 0 } value) rgb = Hex(value);
        else if (color.Theme?.Value is { } t) rgb = t < theme.Length ? theme[t] : string.Empty;
        else if (color.Indexed?.Value is { } i) rgb = i < indexed.Length ? indexed[i] : string.Empty;
        else return string.Empty;
        if (rgb.Length == 0) return string.Empty;
        double tint = color.Tint?.Value ?? 0;
        return "#" + (tint == 0 ? rgb : Tint(rgb, tint));
    }

    private static string Hex(string? argb)
    {
        if (argb == null) return string.Empty;
        string value = argb.Length == 8 ? argb[2..] : argb;
        return value.Length == 6 && value.All(char.IsAsciiHexDigit) ? value.ToUpperInvariant() : string.Empty;
    }

    private static string[]? Theme(A.ColorScheme? scheme)
    {
        if (scheme == null) return null;
        // Excel's theme index swaps the first two pairs of the scheme: 0 is lt1, 1 is dk1, 2 is lt2, 3 is dk2.
        A.Color2Type?[] order =
        {
            scheme.Light1Color, scheme.Dark1Color, scheme.Light2Color, scheme.Dark2Color, scheme.Accent1Color, scheme.Accent2Color,
            scheme.Accent3Color, scheme.Accent4Color, scheme.Accent5Color, scheme.Accent6Color, scheme.Hyperlink, scheme.FollowedHyperlinkColor,
        };
        var result = order.Select(c => Hex(c?.RgbColorModelHex?.Val?.Value ?? c?.SystemColor?.LastColor?.Value)).ToArray();
        return result.All(c => c.Length > 0) ? result : null;
    }

    /// <summary>ECMA-376 tint: darkens toward black for a negative tint and lightens toward white for a positive one,
    /// in HSL lightness. Rounding can differ from Excel's own rendering by one unit.</summary>
    private static string Tint(string rgb, double tint)
    {
        int value = int.Parse(rgb, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        var (h, s, l) = ToHsl(value >> 16 & 0xFF, value >> 8 & 0xFF, value & 0xFF);
        l = tint < 0 ? l * (1 + tint) : l * (1 - tint) + tint;
        var (r, g, b) = FromHsl(h, s, Math.Clamp(l, 0, 1));
        return r.ToString("X2", CultureInfo.InvariantCulture) + g.ToString("X2", CultureInfo.InvariantCulture) + b.ToString("X2", CultureInfo.InvariantCulture);
    }

    internal static (double Hue, double Saturation, double Lightness) ToHsl(int r, int g, int b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf)), l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        double d = max - min, s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == rf ? (gf - bf) / d + (gf < bf ? 6 : 0) : max == gf ? (bf - rf) / d + 2 : (rf - gf) / d + 4;
        return (h * 60, s, l);
    }

    private static (int R, int G, int B) FromHsl(double h, double s, double l)
    {
        if (s == 0) { int v = (int)Math.Round(l * 255, MidpointRounding.AwayFromZero); return (v, v, v); }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q, hk = h / 360;
        int Channel(double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            double c = t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
            return (int)Math.Round(c * 255, MidpointRounding.AwayFromZero);
        }
        return (Channel(hk + 1.0 / 3), Channel(hk), Channel(hk - 1.0 / 3));
    }
}
