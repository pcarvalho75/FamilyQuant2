using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Appends worksheets for printed tables without changing any existing worksheet or
/// Word/CSV cell. Percentage recipes name the native round-trip fields, never reverse a rounded value.</summary>
public static class ReportTableSupplements
{
    private static readonly IReadOnlyDictionary<string, string> FractionColumns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mean_pct"] = "mean_fraction", ["sample_sd_pct"] = "sample_sd_fraction", ["cv_pct"] = "cv_ratio",
        ["mean_measured_pct"] = "mean_measured_fraction", ["sample_sd_measured_pct"] = "sample_sd_measured_fraction",
        ["mean_tic_pct"] = "mean_tic_fraction", ["sample_sd_tic_pct"] = "sample_sd_tic_fraction",
        ["pooled_pct"] = "pooled_fraction", ["run_mean_pct"] = "run_mean_fraction",
        ["pooled_minus_mean_percentage_points"] = "pooled_minus_mean_fraction",
        ["assigned_signal_pct"] = "assigned_signal_fraction", ["measured_peptide_signal_pct"] = "measured_peptide_signal_fraction",
        ["raw_tic_pct"] = "raw_tic_fraction", ["within_family_pct"] = "within_family_fraction",
        ["decoy_ratio_pct"] = "decoy_ratio", ["right_rate_pct"] = "right_rate",
    };

    /// <summary>Capture a lightweight recipe only. Long tables moved out of Word keep their frozen
    /// CSV sheets and do not allocate a second unrounded table.</summary>
    public static ReportTable WithStoredPercentages(ReportTable table, string csv, IReadOnlyList<string> sourceColumns) =>
        table with { SupplementSource = new(csv, sourceColumns.ToArray()) };

    public static IReadOnlyList<ReportSupplementSheet> Complete(ReportDocument document, IReadOnlyList<ReportSupplementSheet> existing)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(existing);
        var result = existing.ToList();
        var ids = existing.Where(s => s != null).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var supplied = existing.Where(s => s != null).GroupBy(s => s.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var printedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in document.Sections)
            foreach (var table in section.Blocks.OfType<ReportTable>())
            {
                if (string.IsNullOrWhiteSpace(table.Id) || !printedIds.Add(table.Id))
                    throw new ArgumentException("Printed report table ids must be nonempty and unique.", nameof(document));
                if (ids.Contains(table.Id))
                {
                    if (supplied[table.Id].OwnerSectionId != section.Id)
                        throw new ArgumentException("A printed table and its supplement sheet have different owning sections.", nameof(existing));
                    continue;
                }
                var values = Values(table);
                result.Add(new(table.Id, section.Id, ReportSupplementLayout.SheetName(table.Id, table.Title), values));
                ids.Add(table.Id);
            }
        return result;
    }

    private static ReportTable Values(ReportTable table)
    {
        string[] headings = table.Columns.ToArray();
        if (headings.Length > 0) headings[0] += " (Word: source text unchanged)";
        var literal = table with { Columns = headings, SupplementSource = null };
        if (table.SupplementSource is not { } recipe) return literal;
        var native = ReportCsvTable.Build(table.Id, table.Title, recipe.Csv, "");
        if (recipe.Columns.Count != table.Columns.Count || native.Rows.Count != table.Rows.Count)
            throw new InvalidDataException("A printed table and its native supplement recipe have different dimensions.");
        var sourceColumns = native.Columns.ToList();
        var bindings = new List<(int Index, int Native)>();
        for (int index = 0; index < recipe.Columns.Count; index++)
            if (FractionColumns.TryGetValue(recipe.Columns[index], out string? column))
            {
                int nativeColumn = sourceColumns.IndexOf(column);
                if (nativeColumn < 0 || !sourceColumns.Contains(recipe.Columns[index]))
                    throw new InvalidDataException("A rounded report column has no native stored value in its supplement source.");
                bindings.Add((index, nativeColumn));
            }
        if (bindings.Count == 0) return literal;
        var columns = headings.ToList();
        foreach (var binding in bindings)
        {
            columns[binding.Index] = table.Columns[binding.Index] + " (unrounded; Word: 6 significant digits)";
            columns.Add("Stored value for " + table.Columns[binding.Index] + " (Word: multiply by 100; 6 significant digits)");
            columns.Add(table.Columns[binding.Index] + " (Word display)");
        }
        var rows = new List<IReadOnlyList<string>>();
        for (int row = 0; row < table.Rows.Count; row++)
        {
            if (table.Rows[row].Count != table.Columns.Count)
                throw new InvalidDataException("A printed table has inconsistent row widths.");
            var cells = table.Rows[row].ToList();
            foreach (var binding in bindings)
            {
                string stored = native.Rows[row][binding.Native], displayed = table.Rows[row][binding.Index];
                if (stored.Length > 0)
                {
                    if (!double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                        || !double.IsFinite(value) || !double.IsFinite(value * 100))
                        throw new InvalidDataException("A native supplement value is not finite.");
                    double percent = value * 100;
                    if (displayed != percent.ToString("G6", CultureInfo.InvariantCulture))
                        throw new InvalidDataException("A printed percentage does not match its native supplement value and rounding rule.");
                    cells[binding.Index] = percent.ToString("R", CultureInfo.InvariantCulture);
                }
                // Missing native values remain empty, never reconstructed from a display or made zero.
                cells.Add(stored); cells.Add(displayed);
            }
            rows.Add(cells);
        }
        return table with { Columns = columns, Rows = rows, SupplementSource = null };
    }
}
