using System.Globalization;

namespace FamilyQuant;

internal static class CsvFormatting
{
    internal static string Guarded(string? value)
    {
        string text = value ?? string.Empty;
        if (text.Length > 0 && "=+-@".IndexOf(text[0]) >= 0) text = "'" + text;
        return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    internal static string AlwaysQuoted(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    internal static string RoundTrip(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
