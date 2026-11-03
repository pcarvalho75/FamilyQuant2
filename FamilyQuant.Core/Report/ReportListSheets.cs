using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Declared list columns become row-per-item tables. Prose is never inferred to be a list
/// from its punctuation. Neither the source CSV nor a native quantity is rewritten.</summary>
public static class ReportListSheets
{
    public static IReadOnlyList<ReportListColumn> Describe(string id, IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var result = new List<ReportListColumn>();
        for (int index = 0; index < columns.Count; index++)
        {
            string name = columns[index];
            string? separator = name switch
            {
                "selected_protein_identifiers" => id.Contains("effective_panel_", StringComparison.Ordinal) ? ";" : "; ",
                "unexplained_sequences" or "panel_keys" => "; ",
                "unexplained_peptides" when id.Contains("effective_panel_protein_counts", StringComparison.Ordinal) => ";",
                "source_user_ids" => ";",
                "curated_user_ids" => " ",
                "Evidence recorded" or "Family evidence used" => " | ",
                "Required domains" or "Absent domains" or "Source subfamilies" or "Recorded source cutoffs" or "Source User IDs" => "; ",
                _ => null,
            };
            if (separator == null) continue;
            result.Add(new(index, name, rows.Select(row => (IReadOnlyList<string>)(row[index].Length == 0 ? Array.Empty<string>()
                : row[index].Split(new[] { separator }, StringSplitOptions.None))).ToArray(),
                Enumerable.Range(1, rows.Count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray()));
        }
        return result;
    }

    private static IReadOnlyList<ReportListColumn> Columns(ReportTable table) => table.ListColumns.Count > 0
        ? table.ListColumns : Describe(table.Id, table.Columns, table.Rows);
    private static string Id(ReportTable table, ReportListColumn column) => "list:" + table.Id + ":" + column.Name;
    private static string Name(ReportTable table, ReportListColumn column) => ReportSupplementLayout.SheetName(Id(table, column), "List " + column.Name);

    public static ReportTable WordView(ReportTable table)
    {
        var columns = Columns(table);
        if (columns.Count == 0) return table;
        var rows = table.Rows.Select(r => r.ToArray()).ToArray();
        foreach (var column in columns)
        {
            Validate(table, column);
            for (int row = 0; row < rows.Length; row++)
                rows[row][column.ColumnIndex] = column.Items[row].Count.ToString(CultureInfo.InvariantCulture)
                    + " items; sheet '" + Name(table, column) + "'";
        }
        return table with { Rows = rows, ListColumns = columns };
    }

    public static IReadOnlyList<ReportSupplementSheet> ForTable(ReportTable table, string owner, string sourceSheet)
    {
        var result = new List<ReportSupplementSheet>();
        int Find(params string[] names) => Enumerable.Range(0, table.Columns.Count).FirstOrDefault(i => names.Contains(
            table.Columns[i].Split(new[] { " (Word:" }, StringSplitOptions.None)[0], StringComparer.Ordinal), -1);
        int familyId = Find("family_user_id", "family_id", "family_userid", "User ID", "Family ID", "effective_panel_key", "Panel family key");
        int familyName = Find("family", "description", "Family", "Family or global row", "stored_family_label", "Panel family");
        foreach (var column in Columns(table))
        {
            Validate(table, column);
            var rows = new List<IReadOnlyList<string>>();
            for (int row = 0; row < table.Rows.Count; row++)
                for (int item = 0; item < column.Items[row].Count; item++)
                    rows.Add(new[] { sourceSheet, table.Id, column.RowKeys?[row] ?? (row + 1).ToString(CultureInfo.InvariantCulture),
                        familyId >= 0 ? table.Rows[row][familyId] : owner.StartsWith(ReportSectionIds.FamilyPrefix, StringComparison.Ordinal)
                            ? owner[ReportSectionIds.FamilyPrefix.Length..] : "",
                        familyName >= 0 ? table.Rows[row][familyName] : "", (item + 1).ToString(CultureInfo.InvariantCulture), column.Items[row][item] });
            string id = Id(table, column);
            result.Add(new(id, owner, Name(table, column), new ReportTable(id, table.Title + " - " + column.Name + " items",
                new[] { "Source sheet", "Source table ID", "Source data row", "Family key / ID", "Family label", "Item number", "Item" }, rows,
                "One row per original item, in source order. Source data rows exclude the header. Counts and references replace joined lists; native CSV exports are unchanged."))
            { SourceTableId = table.Id });
        }
        return result;
    }

    public static IReadOnlyList<ReportSupplementSheet> Complete(IReadOnlyList<ReportSupplementSheet> sheets)
    {
        var result = new List<ReportSupplementSheet>();
        var lists = sheets.Where(s => s.Id.StartsWith("list:", StringComparison.Ordinal)).ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var sheet in sheets.Where(s => !s.Id.StartsWith("list:", StringComparison.Ordinal)))
        {
            foreach (var list in ForTable(sheet.Table, sheet.OwnerSectionId, sheet.Name))
            {
                // The complete native table is authoritative when Word has a filtered projection.
                lists[list.Id] = list;
            }
            result.Add(sheet with { Table = WordView(sheet.Table) });
        }
        result.AddRange(lists.Values.OrderBy(s => s.Id, StringComparer.Ordinal));
        return result;
    }

    public static IReadOnlyList<ReportListColumn> KeepRows(ReportTable table, IReadOnlyList<int> indexes) => Columns(table)
        .Select(c => c with { Items = indexes.Select(i => c.Items[i]).ToArray(),
            RowKeys = indexes.Select(i => c.RowKeys?[i] ?? (i + 1).ToString(CultureInfo.InvariantCulture)).ToArray() }).ToArray();

    private static void Validate(ReportTable table, ReportListColumn column)
    {
        if (column.ColumnIndex < 0 || column.ColumnIndex >= table.Columns.Count || column.Items.Count != table.Rows.Count
            || (column.RowKeys != null && column.RowKeys.Count != table.Rows.Count)
            || column.Items.Any(items => items == null || items.Any(item => item == null)))
            throw new InvalidDataException("A structured report list does not match its source table; no items were dropped.");
    }
}
