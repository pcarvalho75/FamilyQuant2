using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Panels.Import;

public static class WorkbookImportSummaryFields
{
    public const string Subclasses = "subclasses";
}

public static partial class WorkbookImportSummaries
{
    private static List<PanelSubclassExpectedName> ExpectedNames(PanelWorkbookProposal proposal, FamilyPanelImport choices, PanelImportCheck keys)
    {
        var result = new List<PanelSubclassExpectedName>();
        string column = choices.ColumnHeaders.GetValueOrDefault(WorkbookImportSummaryFields.Subclasses, "");
        if (column.Length == 0 || proposal.FamilyTable is not { } table || !table.Headers.Contains(column, StringComparer.Ordinal)) return result;
        foreach (var row in table.Rows)
        {
            var names = ExpectedNameParts(row.Cells.GetValueOrDefault(column, "")).Distinct(StringComparer.Ordinal).ToArray();
            var familyKeys = keys.Rows.FirstOrDefault(value => value.RowNumber == row.RowNumber)?.FamilyKeys ?? new();
            string family = familyKeys.Count == 1 ? familyKeys[0] : "";
            for (int part = 0; part < names.Length; part++)
                result.Add(new() { ExpectedNameId = JsonSerializer.Serialize(new[] { table.SheetName, row.RowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), column,
                    part.ToString(System.Globalization.CultureInfo.InvariantCulture) }), FamilyKey = family, Key = names[part], DisplayName = names[part],
                    SourceRows = new() { new() { SheetName = table.SheetName, RowNumber = row.RowNumber, Cells = new(row.Cells, StringComparer.Ordinal) } } });
        }
        return result;
    }

    private static string InferSubclassColumn(PanelWorkbookProposal proposal, FamilyPanelImport choices)
    {
        if (proposal.FamilyTable is not { } table) return "";
        var mapped = choices.ColumnHeaders.Values.ToHashSet(StringComparer.Ordinal);
        var classNames = proposal.SubclassTables.SelectMany(source => source.Table.Rows.Select(row => row.Cells.GetValueOrDefault(source.ClassColumn, "").Trim()))
            .Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);
        var candidates = table.Headers.Where(header => !mapped.Contains(header)).Select(header =>
        {
            var cells = table.Rows.Select(row => row.Cells.GetValueOrDefault(header, "")).ToArray();
            int matched = cells.Sum(cell => ExpectedNameParts(cell).Count(classNames.Contains));
            int lists = cells.Count(cell => ExpectedNameParts(cell).Distinct(StringComparer.Ordinal).Take(2).Count() == 2);
            return new { Header = header, Matched = matched, Lists = lists };
        }).Where(candidate => candidate.Matched > 0 || candidate.Lists > 0).OrderByDescending(candidate => candidate.Matched).ThenByDescending(candidate => candidate.Lists).ToArray();
        if (candidates.Length == 0 || candidates.Length > 1 && candidates[0].Matched == candidates[1].Matched && candidates[0].Lists == candidates[1].Lists) return "";
        return candidates[0].Header;
    }

    // Separators and balanced grouped lists are source syntax, not domain or decision vocabulary.
    private static IEnumerable<string> ExpectedNameParts(string text)
    {
        int start = 0, depth = 0;
        var parts = new List<string>();
        for (int index = 0; index <= text.Length; index++)
        {
            if (index < text.Length)
            {
                if (text[index] is '(' or '[') depth++;
                if (text[index] is ')' or ']') depth = Math.Max(0, depth - 1);
            }
            if (index != text.Length && (depth != 0 || text[index] is not (';' or ',' or '\r' or '\n'))) continue;
            string part = text[start..index].Trim(); start = index + 1;
            if (part.Length > 0) parts.Add(part);
        }
        foreach (string part in parts)
        {
            int open = part.IndexOfAny(new[] { '(', '[' });
            if (open >= 0 && part.Length > open + 2 && (part[^1] is ')' or ']') && part[(open + 1)..^1].IndexOfAny(new[] { ',', ';', '\r', '\n' }) >= 0)
            {
                string parent = part[..open].Trim();
                if (parent.Length > 0) yield return parent;
                foreach (string child in ExpectedNameParts(part[(open + 1)..^1])) yield return child;
            }
            else yield return part;
        }
    }
}
