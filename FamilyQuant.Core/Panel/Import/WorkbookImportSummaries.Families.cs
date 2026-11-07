using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FamilyQuant.Panels.Import;

public static partial class WorkbookImportSummaries
{
    private static void PrepareFamilyRows(WorkbookImportPlan plan, PanelImportCheck full, PanelImportCheck keyPreview)
    {
        if (plan.Proposal.FamilyTable is not { } table) return;
        string keyColumn = plan.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.FamilyKey, "");
        string nameColumn = plan.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.DisplayName, "");
        string decisionColumn = plan.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Decision, "");
        foreach (var row in table.Rows)
        {
            var outcome = full.Rows.FirstOrDefault(value => value.RowNumber == row.RowNumber) ?? keyPreview.Rows.FirstOrDefault(value => value.RowNumber == row.RowNumber);
            var rowKeys = outcome?.FamilyKeys.Count > 0 ? outcome.FamilyKeys : new List<string> { row.Cells.GetValueOrDefault(keyColumn, "").Trim() };
            foreach (string key in rowKeys)
            {
                var family = full.Panel?.Families.FirstOrDefault(value => value.Key == key);
                var keyFamily = keyPreview.Panel?.Families.FirstOrDefault(value => value.Key == key);
                string name = family?.DisplayName ?? keyFamily?.DisplayName ?? row.Cells.GetValueOrDefault(nameColumn, "").Trim();
                var selected = plan.Actions.FirstOrDefault(action => action.Value is FamilyPanelRowChoice choice && choice.RowNumber == row.RowNumber);
                bool unresolvedRow = plan.Actions.Any(action => action.Section == WorkbookImportSectionKind.FamilyRows && action.Item.Required
                    && action.Item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace
                    && action.Item.Sources.Any(source => source.RowNumbers.Contains(row.RowNumber)));
                FamilyPanelDecision? decision = family?.Decision;
                string merge = family?.MergeTargetFamilyKey ?? "";
                string reason = family != null ? "The existing family Check produced this detached proposed row." : "This source row is unresolved; no family decision is confirmed.";
                var page = WorkbookImportPage.FamilyRows;
                if (family == null)
                {
                    string label = row.Cells.GetValueOrDefault(decisionColumn, "");
                    var mappings = plan.Proposal.Decisions.Where(hint => hint.SourceValue == label && hint.ProposedMapping != null).Select(hint => hint.ProposedMapping!).ToArray();
                    if (mappings.Length == 1) { decision = mappings[0].Decision; merge = mappings[0].MergeTargetFamilyKey; }
                    else if (decisionColumn.Length == 0 && plan.Proposal.DecisionsState == PanelWorkbookReadState.Read) decision = FamilyPanelDecision.Quantify;
                    else page = WorkbookImportPage.Decisions;
                    if (selected?.Value is FamilyPanelRowChoice choice)
                    {
                        if (choice.Action == FamilyPanelRowAction.ReportUnderOthers) { decision = FamilyPanelDecision.ReportUnderOthers; merge = ""; }
                        if (choice.Action == FamilyPanelRowAction.MergeInto) { decision = FamilyPanelDecision.MergeIntoAnotherFamily; merge = choice.MergeTargetFamilyKey; }
                    }
                }
                var state = outcome?.Action == FamilyPanelRowAction.Skip ? WorkbookImportItemState.Found
                    : family != null && !unresolvedRow ? WorkbookImportItemState.Proposed : WorkbookImportItemState.Uncertain;
                if (outcome?.Action == FamilyPanelRowAction.Skip) reason = "This decorative or explicitly skipped source row is not imported.";
                plan.FamilyRows.Add(new(key, name, decision, merge, state, reason, page,
                    Freeze(new[] { Source(table.SheetName, new[] { row.RowNumber }, table.Headers) }),
                    new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(row.Cells, StringComparer.Ordinal))));
            }
        }
    }
}
