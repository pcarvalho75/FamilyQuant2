using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FamilyQuant.Report;

public static class InterpretationNumberTrace
{
    private static readonly Regex Tokens = new(@"(?<![\p{L}\p{N}_])(?<number>[+-]?(?:(?:\d{1,3}(?:,\d{3})+)|\d+)(?:\.\d+)?(?:[eE][+-]?\d+)?%?)(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Numeric = new(@"^(?<sign>[+-]?)(?<integer>(?:\d{1,3}(?:,\d{3})+)|\d+)(?:\.(?<fraction>\d+))?(?:[eE](?<exponent>[+-]?\d+))?(?<percent>%?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Unmatched(string text, IEnumerable<(string Column, string Value)> cells)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(cells);
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var percentages = new HashSet<string>(StringComparer.Ordinal);
        var fractions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (column, value) in cells)
        {
            if (!TryKey(value.Trim(), 0, out string key)) continue;
            exact.Add(key);
            if (column.Contains("fraction", StringComparison.OrdinalIgnoreCase)) fractions.Add(key);
            else percentages.Add(key);
        }
        var missing = new List<string>();
        foreach (Match token in Tokens.Matches(text))
        {
            string value = token.Value;
            bool percent = value.EndsWith('%');
            if (TryKey(value, 0, out string key) && (percent ? percentages : exact).Contains(key)) continue;
            if (percent && TryKey(value, -2, out key) && fractions.Contains(key)) continue;
            missing.Add(value);
        }
        return missing;
    }

    // Shared with the J18 audit: exact decimal equality, without binary rounding.
    public static bool TryKey(string text, int exponentAdjustment, out string key)
    {
        key = string.Empty;
        var match = Numeric.Match(text);
        if (!match.Success) return false;
        int exponent = 0;
        if (match.Groups["exponent"].Success && !int.TryParse(match.Groups["exponent"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return false;
        string fraction = match.Groups["fraction"].Value;
        string digits = (match.Groups["integer"].Value.Replace(",", "", StringComparison.Ordinal) + fraction).TrimStart('0');
        if (digits.Length == 0) { key = "0"; return true; }
        long power = (long)exponent - fraction.Length + exponentAdjustment;
        int length = digits.Length;
        while (length > 1 && digits[length - 1] == '0') { length--; power++; }
        key = (match.Groups["sign"].Value == "-" ? "-" : "") + digits.Substring(0, length) + "e" + power.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}
