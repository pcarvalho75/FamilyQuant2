using System;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Print layout only. Numeric cells reserve their unbroken width; narrative columns
/// share the remaining space according to their content. No source cell is reformatted.</summary>
internal static class ReportTableLayout
{
    internal static int[] Widths(ReportTable table, int available)
    {
        var widths = new int[table.Columns.Count];
        var weights = new double[widths.Length];
        for (int column = 0; column < widths.Length; column++)
        {
            var cells = table.Rows.Select(row => row[column]).Where(cell => !string.IsNullOrWhiteSpace(cell)).ToArray();
            bool numeric = cells.Length > 0 && cells.All(cell => double.TryParse(cell.TrimEnd('%'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out _));
            int longest = cells.Select(cell => cell.Length).DefaultIfEmpty(0).Max();
            widths[column] = numeric ? Math.Clamp((longest + 2) * 110, 800, 2000) : 1100;
            weights[column] = numeric ? .15 : Math.Sqrt(Math.Clamp(Math.Max(longest, table.Columns[column].Length), 8, 100));
        }
        int remainder = available - widths.Sum();
        if (remainder < 0)
            return Enumerable.Range(0, widths.Length).Select(index => available / widths.Length + (index < available % widths.Length ? 1 : 0)).ToArray();
        double total = weights.Sum();
        for (int column = 0; column < widths.Length; column++)
            widths[column] += (int)Math.Floor(remainder * weights[column] / total);
        widths[^1] += available - widths.Sum();
        return widths;
    }
}
