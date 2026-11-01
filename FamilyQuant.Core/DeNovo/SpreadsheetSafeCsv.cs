using System;
using System.Globalization;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// Encodes one CSV cell for files intended to be opened in spreadsheet software. RFC-style quoting
    /// alone does not stop a user-derived cell beginning with =, +, -, or @ from being evaluated as a
    /// formula. Prefixing such text with an apostrophe makes it literal, while invariant signed numeric
    /// values remain numeric so scientific tables do not turn negative measurements into text.
    /// </summary>
    public static class SpreadsheetSafeCsv
    {
        public static string Encode(string? value, bool alwaysQuote = false)
        {
            value ??= string.Empty;
            if (LooksLikeFormula(value) && !IsInvariantNumber(value)) value = "'" + value;

            bool quote = alwaysQuote
                || value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private static bool LooksLikeFormula(string value)
        {
            int first = 0;
            while (first < value.Length && (value[first] == ' ' || value[first] == '\t')) first++;
            if (first >= value.Length) return false;
            return value[first] is '=' or '+' or '-' or '@' or '\r' or '\n';
        }

        private static bool IsInvariantNumber(string value) => double.TryParse(value,
            NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }
}
