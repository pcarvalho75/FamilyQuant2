using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyQuant.Panels.Import;

public sealed partial class FamilyPanelSpreadsheetImporter
{
    public PanelWorkbookProposal ProposeWorkbook(string path) => ProposeWorkbook(new PanelSpreadsheetReader().ReadWorkbook(path));

    public PanelWorkbookProposal ProposeWorkbook(PanelWorkbookRead workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var result = new PanelWorkbookProposal { Workbook = workbook, Problems = Copy(workbook.Problems) };
        foreach (var sheet in workbook.Sheets)
        {
            sheet.Role = PanelWorkbookSheetRole.Notes;
            sheet.RoleReason = "Source text is kept as notes unless a family, legend or subclass structure is found.";
        }
        var candidates = workbook.Sheets.Where(sheet => sheet.Table != null).Select(sheet =>
        {
            var table = sheet.Table!;
            var proposed = Propose(table);
            var rows = WorkbookDataRows(table);
            string key = proposed.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.FamilyKey, "");
            string name = proposed.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.DisplayName, "");
            var shapes = table.Headers.Select(header => new ColumnShape(header, rows)).ToList();
            bool code = shapes.Any(shape => shape.Header == key && shape.IsCode);
            bool names = shapes.Any(shape => shape.Header == name && shape.IsNames);
            return new { Sheet = sheet, Proposed = proposed, Rows = rows.Count, Eligible = proposed.IsTemplate || code && names,
                Names = shapes.Count(shape => shape.IsNames && shape.Header != key) };
        }).ToList();
        var ranked = candidates.Where(candidate => candidate.Eligible).OrderByDescending(candidate => candidate.Rows)
            .ThenByDescending(candidate => candidate.Sheet.HasFilterHeader).ThenByDescending(candidate => candidate.Names).ToList();
        var selected = ranked.FirstOrDefault();
        if (workbook.Sheets.Count == 1 && candidates.Count == 1) selected = candidates[0];
        else if (ranked.Count > 1 && ranked[0].Rows == ranked[1].Rows
            && ranked[0].Sheet.HasFilterHeader == ranked[1].Sheet.HasFilterHeader && ranked[0].Names == ranked[1].Names)
            selected = null;
        if (selected == null)
        {
            result.NeedsFamilySheetChoice = true;
            result.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.NoFamilies, Severity = PanelImportSeverity.Blocking,
                Message = "No single family sheet is established by its short-code column, name columns and row count. Choose the family sheet." });
        }
        else
        {
            result.FamilySheet = selected.Sheet.SheetName;
            result.FamilyProposal = selected.Proposed;
            selected.Sheet.Role = PanelWorkbookSheetRole.FamilyList;
            selected.Sheet.IsUncertain |= !selected.Eligible;
            selected.Sheet.RoleReason = workbook.Sheets.Count == 1 ? "The only sheet retains the existing single-sheet import proposal."
                : "This sheet has short distinct codes, name columns and the largest supported family-row count ("
                    + selected.Rows.ToString(CultureInfo.InvariantCulture) + ").";
            ProposeWorkbookRelationships(result);
        }
        result.Notes = workbook.Sheets.Where(sheet => sheet.Role == PanelWorkbookSheetRole.Notes)
            .Select(sheet => new PanelWorkbookSourceNote { SheetName = sheet.SheetName, Text = WorkbookNoteText(sheet) }).ToList();
        PopulateWorkbookPfam(result);
        return result;
    }

    private static List<PanelSpreadsheetRow> WorkbookDataRows(PanelSpreadsheetTable table) =>
        table.Rows.Where(row => DecorativeReason(table, row) == null).ToList();

    private void ProposeWorkbookRelationships(PanelWorkbookProposal proposal)
    {
        var table = proposal.FamilyTable!;
        var choices = proposal.FamilyProposal.Choices;
        var rows = WorkbookDataRows(table);
        string keyHeader = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.FamilyKey, "");
        var keys = rows.Select(row => Value(row, keyHeader).Trim()).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);
        var fills = rows.Where(row => row.Fills.ContainsKey(keyHeader)).Select(row => row.Fills[keyHeader]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in proposal.Workbook.Sheets.Where(sheet => sheet.SheetName != proposal.FamilySheet && sheet.Table != null))
        {
            var other = sheet.Table!;
            var otherRows = WorkbookDataRows(other);
            if (other.Headers.Count == 2 && fills.Count > 0)
            {
                string first = other.Headers[0];
                int overlap = otherRows.Where(row => Value(row, first).Trim().Length > 0 && row.Fills.ContainsKey(first))
                    .Select(row => row.Fills[first]).Where(fills.Contains).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (overlap >= Math.Min(2, fills.Count))
                {
                    sheet.Role = PanelWorkbookSheetRole.ColorLegend;
                    sheet.RoleReason = "The first-column fills match the family abbreviation fills; the second column supplies their meaning text.";
                    continue;
                }
            }
            var byColumn = other.Headers.Select(header => new { Header = header,
                Values = otherRows.Select(row => Value(row, header).Trim()).Where(value => value.Length > 0).ToList() }).ToList();
            var keyColumn = byColumn.FirstOrDefault(column => column.Values.Count > 0
                && column.Values.Count(value => keys.Contains(value)) * 5 >= column.Values.Count * 4);
            var linked = keys.Where(key => ContainsWorkbookKey(sheet.SheetName, key)).ToHashSet(StringComparer.Ordinal);
            if (keyColumn != null) linked.UnionWith(keyColumn.Values.Where(keys.Contains));
            var domainNames = WorkbookPfam(proposal).SelectMany(row => row.Domains).Select(domain => domain.Name)
                .Where(name => name.Length >= 3).Distinct(StringComparer.Ordinal).ToArray();
            var domainColumns = other.Headers.Where(header => otherRows.Any(row => PfamAccession.IsMatch(Value(row, header))
                || domainNames.Any(name => ContainsWorkbookKey(Value(row, header), name)))).ToList();
            string classColumn = other.Headers.Where(header => header != keyColumn?.Header && !domainColumns.Contains(header))
                .FirstOrDefault(header => new ColumnShape(header, otherRows).IsNames || new ColumnShape(header, otherRows).IsCode) ?? "";
            if (linked.Count > 0 && domainColumns.Count > 0 && classColumn.Length > 0)
            {
                sheet.Role = PanelWorkbookSheetRole.SubclassTable;
                sheet.RoleReason = "Its title or source column names imported abbreviations and its class rows contain known Pfam accessions or domain names.";
                proposal.SubclassTables.Add(new PanelWorkbookSubclassTable { SheetName = sheet.SheetName,
                    FamilyKeys = linked.Order(StringComparer.Ordinal).ToList(), FamilyKeyColumn = keyColumn?.Header ?? "",
                    ClassColumn = classColumn, DomainColumns = domainColumns, Table = other });
            }
        }
        var shapeChoices = Copy(choices);
        shapeChoices.ColumnHeaders.Remove(PanelImportFields.Decision);
        shapeChoices.ColumnHeaders.Remove(PanelImportFields.ColorCategory);
        proposal.MultiFamilyRows = Check(table, shapeChoices, Array.Empty<FamilyPanelDecisionMapping>()).Rows
            .Where(row => row.ProposedParts.Count > 1).ToList();
    }

    private static bool ContainsWorkbookKey(string text, string key) => Regex.IsMatch(text,
        @"(?<![\p{L}\p{N}])" + Regex.Escape(key) + @"(?![\p{L}\p{N}])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static List<PanelWorkbookPfamHint> WorkbookPfam(PanelWorkbookProposal proposal)
    {
        var result = new List<PanelWorkbookPfamHint>();
        if (proposal.FamilyTable is not { } table) return result;
        string header = proposal.FamilyProposal.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Pfam, "");
        string key = proposal.FamilyProposal.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.FamilyKey, "");
        if (header.Length == 0) return result;
        foreach (var row in WorkbookDataRows(table))
        {
            string text = Value(row, header);
            var names = NamedPfam.Matches(text).ToDictionary(match => match.Groups["accession"].Index, match => match.Groups["name"].Value.Trim());
            var domains = PfamAccession.Matches(text).Select(match => new FamilyPanelDomain
                { Accession = match.Value, Name = names.GetValueOrDefault(match.Index, "") }).ToList();
            if (domains.Count > 0) result.Add(new PanelWorkbookPfamHint
                { RowNumber = row.RowNumber, FamilyKey = Value(row, key).Trim(), Column = header, Domains = domains });
        }
        return result;
    }

    private static string WorkbookNoteText(PanelWorkbookSheet sheet)
    {
        var text = new StringBuilder();
        foreach (var row in sheet.SourceRows)
        foreach (var cell in row.Cells.Where(cell => cell.Text.Length > 0))
            text.Append(cell.ColumnLetter).Append(row.RowNumber.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(cell.Text).Append('\n');
        return text.ToString();
    }
}
