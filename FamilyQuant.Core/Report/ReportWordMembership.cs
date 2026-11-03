using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Projects the canonical report partition into Word without changing stored studies or exports.</summary>
public static class ReportWordMembership
{
    public static HashSet<int> HiddenIds(FamilyReportThresholdResult? grouping) => grouping == null ? new()
        : grouping.OtherFamilies.Concat(grouping.ExcludedFamilies).Select(f => f.FamilyUserId)
            .Concat(grouping.ConflictFamilies.Select(f => f.Family.FamilyUserId)).ToHashSet();

    public static ReportTable Filter(ReportTable table, string nativeCsv, FamilyReportThresholdResult? grouping)
    {
        if (table.Id == "others-members") return table with { SupplementOnly = true };
        if (grouping == null || table.Id == "assignment-conflicts-table") return table;
        var hidden = HiddenIds(grouping);
        if (hidden.Count == 0) return table;
        var full = ReportCsvTable.Build(table.Id, table.Title, nativeCsv, table.Caption);
        int index = full.Columns.ToList().FindIndex(c => c is "family_user_id" or "family_id");
        if (index < 0) return table;
        if (full.Rows.Count != table.Rows.Count) throw new InvalidOperationException("Word rows do not match their native family export.");
        return KeepRows(table, Enumerable.Range(0, table.Rows.Count).Where(row => !int.TryParse(full.Rows[row][index], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int id) || !hidden.Contains(id)).ToArray());
    }

    public static HashSet<string> HiddenWorldFamilies(FamilyWorldSummary world, FamilyReportThresholdResult? grouping)
    {
        var hidden = HiddenIds(grouping);
        return world.Composition.Where(r => r.CuratedUserIds.Any(hidden.Contains)).Select(r => r.Family).ToHashSet(StringComparer.Ordinal);
    }

    public static ReportTable FilterWorld(ReportTable table, FamilyWorldSummary world, FamilyReportThresholdResult? grouping)
    {
        var hidden = HiddenWorldFamilies(world, grouping);
        return KeepRows(table, Enumerable.Range(0, table.Rows.Count).Where(row => !hidden.Contains(table.Rows[row][0])).ToArray());
    }

    private static ReportTable KeepRows(ReportTable table, IReadOnlyList<int> indexes)
    {
        if (indexes.Count == table.Rows.Count) return table;
        var recipe = table.SupplementSource;
        if (recipe != null)
        {
            var full = ReportCsvTable.Build(table.Id, table.Title, recipe.Csv, "");
            if (full.Rows.Count != table.Rows.Count) throw new InvalidOperationException("Word rows do not match their supplement source.");
            string Row(IReadOnlyList<string> cells) => string.Join(",", cells.Select(CsvFormatting.AlwaysQuoted)) + "\n";
            recipe = recipe with { Csv = Row(full.Columns) + string.Concat(indexes.Select(i => Row(full.Rows[i]))) };
        }
        return table with { Rows = indexes.Select(i => table.Rows[i]).ToArray(), SupplementSource = recipe,
            ListColumns = ReportListSheets.KeepRows(table, indexes) };
    }
}
