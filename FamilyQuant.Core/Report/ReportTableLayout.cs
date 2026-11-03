using System;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Print layout only. Numeric cells reserve their unbroken width; narrative columns
/// share the remaining space according to their content. No source cell is reformatted.</summary>
internal static class ReportTableLayout
{
    internal sealed record Layout(int[] Widths, int FontHalfPoints);

    internal static int[] Widths(ReportTable table, int available) => Create(table, available).Widths;

    internal static Layout Create(ReportTable table, int available)
    {
        var widths = new int[table.Columns.Count];
        var weights = new double[widths.Length];
        var numericWidths = new int[widths.Length];
        var headingWidths = new int[widths.Length];
        var numericColumns = new bool[widths.Length];
        for (int column = 0; column < widths.Length; column++)
        {
            var cells = table.Rows.Select(row => row[column]).Where(cell => !string.IsNullOrWhiteSpace(cell)).ToArray();
            bool IsNumber(string text) => double.TryParse(text.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            bool numeric = cells.Length > 0 && cells.All(IsNumber);
            int longest = cells.Select(cell => cell.Length).DefaultIfEmpty(0).Max();
            int headingWord = table.Columns[column].Split(new[] { ' ', '_', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Length).DefaultIfEmpty(0).Max();
            // Calibri at 11 pt: reserve a conservative advance for every numeric character,
            // including the exponent and sign. A missing-value label must not hide this minimum.
            numericWidths[column] = cells.SelectMany(cell => cell.Replace('\r', '\n').Split('\n')).Where(IsNumber)
                .Select(line => line.Sum(character => character == '%' ? 200 : 115)).DefaultIfEmpty(0).Max();
            headingWidths[column] = headingWord * 130 + 60;
            numericColumns[column] = numeric;
            weights[column] = numeric ? .15 : Math.Sqrt(Math.Clamp(Math.Max(longest, table.Columns[column].Length), 8, 100));
        }
        int halfPoints = 22;
        void MinimumWidths()
        {
            for (int column = 0; column < widths.Length; column++)
            {
                int content = numericColumns[column] ? 600 : 900;
                if (numericWidths[column] > 0) content = Math.Max(content, Math.Max(numericWidths[column], headingWidths[column]));
                widths[column] = 200 + (int)Math.Ceiling(content * halfPoints / 22.0);
            }
        }
        MinimumWidths();
        // Reduce only a dense numeric table's type size, instead of discarding its numeric
        // reservations in an equal-width fallback. Text and exported values are untouched.
        while (widths.Sum() > available && halfPoints > 2 && numericWidths.Any(width => width > 0))
        {
            halfPoints--;
            MinimumWidths();
        }
        int remainder = available - widths.Sum();
        if (remainder < 0)
            return new(Enumerable.Range(0, widths.Length).Select(index => available / widths.Length + (index < available % widths.Length ? 1 : 0)).ToArray(), halfPoints);
        double total = weights.Sum();
        for (int column = 0; column < widths.Length; column++)
            widths[column] += (int)Math.Floor(remainder * weights[column] / total);
        widths[^1] += available - widths.Sum();
        return new(widths, halfPoints);
    }
}
