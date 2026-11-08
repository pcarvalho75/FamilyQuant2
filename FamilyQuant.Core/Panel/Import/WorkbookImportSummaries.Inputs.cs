using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Panels.Import;

public static class WorkbookImportSummaryFields
{
    public const string Subclasses = "subclasses";
}

public static partial class WorkbookImportSummaries
{
    private static List<PanelSubclassExpectedName> ExpectedNames(PanelWorkbookProposal proposal, FamilyPanelImport choices, PanelImportCheck keys,
        out List<PanelSubclassExpectedName> notes)
    {
        var result = new List<PanelSubclassExpectedName>();
        notes = new();
        string column = choices.ColumnHeaders.GetValueOrDefault(WorkbookImportSummaryFields.Subclasses, "");
        if (column.Length == 0 || proposal.FamilyTable is not { } table || !table.Headers.Contains(column, StringComparer.Ordinal)) return result;
        foreach (var row in table.Rows)
        {
            var names = ExpectedNameParts(row.Cells.GetValueOrDefault(column, "")).Distinct(StringComparer.Ordinal).ToArray();
            var familyKeys = keys.Rows.FirstOrDefault(value => value.RowNumber == row.RowNumber)?.FamilyKeys ?? new();
            string family = familyKeys.Count == 1 ? familyKeys[0] : "";
            var classNames = TableClassNames(proposal, family).ToArray();
            for (int part = 0; part < names.Length; part++)
            {
                string? canonical = ExpectedTableClass(names[part], classNames);
                var item = new PanelSubclassExpectedName { ExpectedNameId = JsonSerializer.Serialize(new[] { table.SheetName,
                    row.RowNumber.ToString(CultureInfo.InvariantCulture), column, part.ToString(CultureInfo.InvariantCulture) }),
                    FamilyKey = family, Key = canonical ?? names[part], DisplayName = canonical ?? names[part],
                    SourceRows = new() { new() { SheetName = table.SheetName, RowNumber = row.RowNumber, Cells = new(row.Cells, StringComparer.Ordinal) } } };
                if (canonical == null) { notes.Add(item); continue; }
                var existing = result.FirstOrDefault(value => value.FamilyKey == family && value.Key == canonical);
                if (existing == null) result.Add(item);
                else foreach (var origin in item.SourceRows)
                    if (!existing.SourceRows.Any(previous => previous.SheetName == origin.SheetName && previous.RowNumber == origin.RowNumber))
                        existing.SourceRows.Add(origin);
            }
        }
        return result;
    }

    private static string InferSubclassColumn(PanelWorkbookProposal proposal, FamilyPanelImport choices)
    {
        if (proposal.FamilyTable is not { } table) return "";
        var mapped = choices.ColumnHeaders.Values.ToHashSet(StringComparer.Ordinal);
        var classNames = TableClassNames(proposal).ToArray();
        var candidates = table.Headers.Where(header => !mapped.Contains(header)).Select(header =>
        {
            var cells = table.Rows.Select(row => row.Cells.GetValueOrDefault(header, "")).ToArray();
            int matched = cells.Sum(cell => ExpectedNameParts(cell).Count(name => ExpectedTableClass(name, classNames) != null));
            int lists = cells.Count(cell => ExpectedNameParts(cell).Distinct(StringComparer.Ordinal).Take(2).Count() == 2);
            return new { Header = header, Matched = matched, Lists = lists };
        }).Where(candidate => candidate.Matched > 0 || candidate.Lists > 0).OrderByDescending(candidate => candidate.Matched).ThenByDescending(candidate => candidate.Lists).ToArray();
        if (candidates.Length == 0 || candidates.Length > 1 && candidates[0].Matched == candidates[1].Matched && candidates[0].Lists == candidates[1].Lists) return "";
        return candidates[0].Header;
    }

    private static IEnumerable<string> TableClassNames(PanelWorkbookProposal proposal, string family = "") => proposal.SubclassTables
        .SelectMany(source => source.Table.Rows.Where(row => family.Length == 0
            || source.FamilyKeys.Contains(family, StringComparer.Ordinal) && (source.FamilyKeyColumn.Length == 0
                || row.Cells.GetValueOrDefault(source.FamilyKeyColumn, "").Trim() == family))
            .Select(row => row.Cells.GetValueOrDefault(source.ClassColumn, "").Trim()))
        .Where(name => name.Length > 0).Distinct(StringComparer.Ordinal);

    private static string? ExpectedTableClass(string entry, IReadOnlyList<string> classNames)
    {
        string text = entry.Trim().Normalize(NormalizationForm.FormC);
        var matches = classNames.Where(name =>
        {
            string prefix = name.Normalize(NormalizationForm.FormC);
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (text.Length == prefix.Length) return true;
            return !Rune.TryGetRuneAt(text, prefix.Length, out var next) || !(Rune.IsLetterOrDigit(next)
                || Rune.GetUnicodeCategory(next) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation);
        }).OrderByDescending(name => name.Normalize(NormalizationForm.FormC).Length).ThenBy(name => name, StringComparer.Ordinal).ToArray();
        if (matches.Length == 0) return null;
        int length = matches[0].Normalize(NormalizationForm.FormC).Length;
        var longest = matches.Where(name => name.Normalize(NormalizationForm.FormC).Length == length).ToArray();
        if (longest.Length == 1) return longest[0];
        var exactCase = longest.Where(name => text.StartsWith(name.Normalize(NormalizationForm.FormC), StringComparison.Ordinal)).ToArray();
        return exactCase.Length == 1 ? exactCase[0] : null;
    }

    // Only top-level separators split names; punctuation-only placeholders supply no name.
    private static IEnumerable<string> ExpectedNameParts(string text)
    {
        int start = 0, depth = 0;
        for (int index = 0; index <= text.Length; index++)
        {
            if (index < text.Length)
            {
                if (text[index] is '(' or '[') depth++;
                if (text[index] is ')' or ']') depth = Math.Max(0, depth - 1);
            }
            if (index != text.Length && (depth != 0 || text[index] is not (';' or ',' or '\r' or '\n'))) continue;
            string part = text[start..index].Trim(); start = index + 1;
            if (part.Any(char.IsLetterOrDigit)) yield return part;
        }
    }
}
