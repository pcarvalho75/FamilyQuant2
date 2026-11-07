using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Panels.Import;

public sealed partial class FamilyPanelSpreadsheetImporter
{
    public PanelWorkbookProposal ProposeWorkbook(string path) => ProposeWorkbook(new PanelSpreadsheetReader().ReadWorkbook(path));

    public PanelWorkbookProposal ProposeWorkbook(PanelWorkbookRead workbook) => ProposeWorkbook(workbook, null, null);

    public PanelWorkbookProposal ProposeWorkbook(PanelWorkbookRead workbook, string? familySheet = null,
        IReadOnlyDictionary<string, PanelWorkbookSheetRole>? roleOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var sourceProblems = Copy(workbook.Problems);
        foreach (var source in workbook.Sheets)
        foreach (var problem in source.Preview.Problems.Concat(source.Table?.Problems ?? new List<PanelImportProblem>()))
        {
            var attributed = Copy(problem); attributed.SheetName ??= source.SheetName; sourceProblems.Add(attributed);
        }
        var result = new PanelWorkbookProposal { Workbook = workbook, Problems = sourceProblems
            .GroupBy(problem => JsonSerializer.Serialize(problem), StringComparer.Ordinal).Select(group => group.First()).ToList() };
        var overrides = roleOverrides?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, PanelWorkbookSheetRole>(StringComparer.Ordinal);
        foreach (var item in overrides)
            if (!workbook.Sheets.Any(sheet => sheet.SheetName == item.Key) || !Enum.IsDefined(item.Value))
                result.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.ChoiceInvalid, Severity = PanelImportSeverity.Blocking,
                    SheetName = item.Key, Message = "The sheet role choice does not name an available sheet and supported role." });
        foreach (var sheet in workbook.Sheets)
        {
            sheet.Role = overrides.GetValueOrDefault(sheet.SheetName, PanelWorkbookSheetRole.Notes);
            sheet.RoleReason = overrides.ContainsKey(sheet.SheetName) ? "This source role was chosen by the user."
                : "Source text is kept as notes unless a family, legend or subclass structure is found.";
            sheet.IsUncertain = sheet.Table == null || workbook.Problems.Any(problem => problem.SheetName == sheet.SheetName
                && problem.Severity == PanelImportSeverity.Blocking);
        }
        var candidates = workbook.Sheets.Where(sheet => sheet.Table != null && (!overrides.TryGetValue(sheet.SheetName, out var role)
            || role == PanelWorkbookSheetRole.FamilyList || sheet.SheetName == familySheet)).Select(sheet =>
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
        var ranked = candidates.Where(candidate => candidate.Eligible).OrderBy(candidate => candidate.Sheet.IsHidden)
            .ThenByDescending(candidate => candidate.Rows)
            .ThenByDescending(candidate => candidate.Sheet.HasFilterHeader).ThenByDescending(candidate => candidate.Names).ToList();
        var selected = ranked.FirstOrDefault();
        bool userSelected = !string.IsNullOrWhiteSpace(familySheet);
        var forced = candidates.Where(candidate => overrides.GetValueOrDefault(candidate.Sheet.SheetName) == PanelWorkbookSheetRole.FamilyList
            && overrides.ContainsKey(candidate.Sheet.SheetName)).ToList();
        if (userSelected) selected = candidates.FirstOrDefault(candidate => candidate.Sheet.SheetName == familySheet);
        else if (forced.Count == 1) { selected = forced[0]; userSelected = true; }
        else if (forced.Count > 1) selected = null;
        else if (candidates.Count == 1) selected = candidates[0]; // Legacy fallback includes a workbook with other blank sheets.
        else if (ranked.Count > 1 && ranked[0].Sheet.IsHidden == ranked[1].Sheet.IsHidden && ranked[0].Rows == ranked[1].Rows
            && ranked[0].Sheet.HasFilterHeader == ranked[1].Sheet.HasFilterHeader && ranked[0].Names == ranked[1].Names)
            selected = null;
        if (userSelected && familySheet != null && overrides.TryGetValue(familySheet, out var selectedRole)
            && selectedRole != PanelWorkbookSheetRole.FamilyList)
        {
            selected = null;
            result.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.ChoiceInvalid, Severity = PanelImportSeverity.Blocking,
                SheetName = familySheet, Message = "The chosen family sheet has a different explicit role. Choose consistent sheet roles before importing." });
        }
        if (selected == null)
        {
            result.NeedsFamilySheetChoice = true;
            result.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.NoFamilies, Severity = PanelImportSeverity.Blocking,
                SheetName = familySheet,
                Message = "No single family sheet is established by its short-code column, name columns and row count. Choose the family sheet." });
        }
        else
        {
            result.FamilySheet = selected.Sheet.SheetName;
            result.FamilyProposal = selected.Proposed;
            selected.Sheet.Role = PanelWorkbookSheetRole.FamilyList;
            selected.Sheet.IsUncertain |= !userSelected && (!selected.Eligible || ranked.Count > 1);
            selected.Sheet.RoleReason = userSelected ? "This family sheet was chosen by the user."
                : candidates.Count == 1 ? "The sole available table retains the existing single-sheet import proposal."
                : "The visible-first ranking proposes this short-code/name table with "
                    + selected.Rows.ToString(CultureInfo.InvariantCulture) + " data rows.";
            if (!userSelected && ranked.Count > 1)
                result.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.FamilySheetAlternative, Severity = PanelImportSeverity.Note,
                    SheetName = selected.Sheet.SheetName, Message = "Other sheets also have supported family-list structure: "
                        + string.Join(", ", ranked.Where(candidate => candidate != selected).Select(candidate => "\"" + candidate.Sheet.SheetName + "\""))
                        + ". Review the proposed family sheet before accepting." });
            ProposeWorkbookRelationships(result, overrides, ranked.Select(candidate => candidate.Sheet.SheetName).ToHashSet(StringComparer.Ordinal));
        }
        result.Notes = workbook.Sheets.Where(sheet => sheet.Role is PanelWorkbookSheetRole.Notes or PanelWorkbookSheetRole.ColorLegend
            || sheet.Role == PanelWorkbookSheetRole.FamilyList && sheet.SheetName != result.FamilySheet)
            .Select(sheet => new PanelWorkbookSourceNote { SheetName = sheet.SheetName, Text = WorkbookNoteText(sheet) }).ToList();
        if (selected != null && !result.Problems.Any(problem => problem.Severity == PanelImportSeverity.Blocking)
            && !workbook.Sheets.Any(sheet => sheet.Role == PanelWorkbookSheetRole.ColorLegend))
        {
            result.ColorsState = PanelWorkbookReadState.Read;
            if (!result.FamilyProposal.Choices.ColumnHeaders.ContainsKey(PanelImportFields.Decision)
                && result.FamilyProposal.DecisionHint.Length == 0) result.DecisionsState = PanelWorkbookReadState.Read;
        }
        PopulateWorkbookLegend(result);
        PopulateWorkbookPfam(result);
        return result;
    }

    private static List<PanelSpreadsheetRow> WorkbookDataRows(PanelSpreadsheetTable table) =>
        table.Rows.Where(row => DecorativeReason(table, row) == null).ToList();

    private void ProposeWorkbookRelationships(PanelWorkbookProposal proposal,
        IReadOnlyDictionary<string, PanelWorkbookSheetRole> overrides, IReadOnlySet<string> eligibleFamilySheets)
    {
        var table = proposal.FamilyTable!;
        var choices = proposal.FamilyProposal.Choices;
        var rows = WorkbookDataRows(table);
        string keyHeader = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.FamilyKey, "");
        var keys = rows.Select(row => Value(row, keyHeader).Trim()).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);
        var nameHeaders = new[] { PanelImportFields.DisplayName, PanelImportFields.OtherName }
            .Select(field => choices.ColumnHeaders.GetValueOrDefault(field, "")).Where(header => header.Length > 0).ToArray();
        var familyNames = rows.SelectMany(row => nameHeaders.Select(header => Value(row, header).Trim()))
            .Where(name => name.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fills = rows.Where(row => row.Fills.ContainsKey(keyHeader)).Select(row => row.Fills[keyHeader]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in proposal.Workbook.Sheets.Where(sheet => sheet.SheetName != proposal.FamilySheet && sheet.Table != null))
        {
            var other = sheet.Table!;
            var otherRows = WorkbookDataRows(other);
            bool chosenRole = overrides.TryGetValue(sheet.SheetName, out var role);
            if (chosenRole && role is PanelWorkbookSheetRole.Notes or PanelWorkbookSheetRole.FamilyList) continue;
            if (chosenRole && role == PanelWorkbookSheetRole.ColorLegend) continue;
            if (!chosenRole && other.Headers.Count == 2 && fills.Count > 0)
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
            // An alternate family list must add class names, not reuse imported family identities.
            bool reusesFamilyIdentity = eligibleFamilySheets.Contains(sheet.SheetName)
                && otherRows.Any(row => familyNames.Contains(Value(row, classColumn).Trim()) || keys.Contains(Value(row, classColumn).Trim()));
            if (chosenRole && role == PanelWorkbookSheetRole.SubclassTable
                || linked.Count > 0 && domainColumns.Count > 0 && classColumn.Length > 0 && !reusesFamilyIdentity)
            {
                sheet.Role = PanelWorkbookSheetRole.SubclassTable;
                sheet.IsUncertain |= linked.Count == 0 || domainColumns.Count == 0 || classColumn.Length == 0;
                sheet.RoleReason = chosenRole ? "This subclass source was chosen by the user; unrecognized fields remain source text."
                    : "Its title or source column names imported abbreviations and its class rows contain known Pfam accessions or domain names.";
                proposal.SubclassTables.Add(new PanelWorkbookSubclassTable { SheetName = sheet.SheetName,
                    FamilyKeys = linked.Order(StringComparer.Ordinal).ToList(), FamilyKeyColumn = keyColumn?.Header ?? "",
                    ClassColumn = classColumn, DomainColumns = domainColumns, Table = other });
            }
            else if (!chosenRole && eligibleFamilySheets.Contains(sheet.SheetName))
            {
                sheet.IsUncertain = true;
                sheet.RoleReason = "This source also fits a family list; it is retained as notes unless chosen as the family sheet.";
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
        var writtenMerges = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in sheet.SourceRows)
        foreach (var cell in row.Cells.Where(cell => cell.Text.Length > 0))
        {
            if (cell.MergeRange.Length > 0 && !writtenMerges.Add(cell.MergeRange)) continue;
            string address = cell.MergeRange.Length > 0 ? cell.MergeRange : cell.ColumnLetter + row.RowNumber.ToString(CultureInfo.InvariantCulture);
            text.Append(address).Append(": ").Append(cell.Text).Append('\n');
        }
        return text.ToString();
    }
}
