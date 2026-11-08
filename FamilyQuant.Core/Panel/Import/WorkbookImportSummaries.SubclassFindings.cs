using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Panels.Import;

internal sealed record WorkbookImportSubclassReviewFinding;

public static partial class WorkbookImportSummaries
{
    private static void AddSubclassReviewFindings(WorkbookImportPlan plan, IReadOnlyCollection<string> keys, AddFinding add)
    {
        WorkbookImportSource[] Origins(IEnumerable<PanelSubclassSourceRow> rows)
        {
            var origins = rows.GroupBy(row => row.SheetName, StringComparer.Ordinal)
                .Select(group => Source(group.Key, group.Select(row => row.RowNumber), group.SelectMany(row => row.Cells.Keys))).ToArray();
            return origins.Length > 0 ? origins : new[] { Source(plan.Proposal.FamilySheet) };
        }
        var definitions = new List<(PanelSubclassProposal Proposal, PanelSubclassDefinition Built)>();
        foreach (var proposal in plan.Subclasses)
        {
            var choices = plan.ReviewedSubclasses.Where(choice => choice.ProposalId == proposal.ProposalId).ToArray();
            if (choices.Length != 1 || !choices[0].Accepted || !Proposed(proposal.ProposalId, "")) continue;
            var choice = choices[0];
            if (choice.Rules.Where(rule => rule.Accepted).Any(rule => !Proposed(proposal.ProposalId, rule.RuleId))) continue;
            var built = PanelSubclassImport.BuildDefinitions(new[] { proposal }, new[] { choice }, keys);
            if (!built.CanBuild || built.Definitions.Count != 1) continue;
            var definition = built.Definitions[0];
            if (definition.Definition.RequiredPfam.Count + definition.Definition.AbsentPfam.Count > 0)
                definitions.Add((proposal, definition));
        }
        definitions = definitions.OrderBy(item => item.Built.FamilyKey, StringComparer.Ordinal)
            .ThenBy(item => item.Built.Definition.Key, StringComparer.Ordinal).ThenBy(item => item.Proposal.ProposalId, StringComparer.Ordinal).ToList();
        for (int first = 0; first < definitions.Count; first++)
        for (int second = first + 1; second < definitions.Count; second++)
        {
            var a = definitions[first]; var b = definitions[second];
            if (a.Built.FamilyKey != b.Built.FamilyKey) continue;
            var required = a.Built.Definition.RequiredPfam.Concat(b.Built.Definition.RequiredPfam).ToHashSet(StringComparer.Ordinal);
            var absent = a.Built.Definition.AbsentPfam.Concat(b.Built.Definition.AbsentPfam).ToHashSet(StringComparer.Ordinal);
            if (required.Overlaps(absent)) continue;
            string domainSet = required.Count > 0 ? " containing " + Domains(required) : " outside the domains they mark Absent";
            if (required.Count > 0 && absent.Count > 0) domainSet += " and lacking " + Domains(absent);
            add(WorkbookImportSectionKind.Subclasses, "Overlapping subclass rules", a.Built.FamilyKey + ": "
                + DefinitionName(a.Built.Definition) + " and " + DefinitionName(b.Built.Definition),
                "Both selected subclasses can match the same Pfam domain set" + domainSet
                    + ". Leave one out, or add the missing Absent rule in the source sheet and read the workbook again.",
                WorkbookImportItemState.Found, WorkbookImportPage.Subclasses, action: new WorkbookImportSubclassReviewFinding(),
                sources: Origins(a.Proposal.SourceRows.Concat(b.Proposal.SourceRows)));
        }

        foreach (var group in plan.SubclassNotes.GroupBy(note => (note.FamilyKey, Sheet: note.SourceRows.FirstOrDefault()?.SheetName ?? "",
            Row: note.SourceRows.FirstOrDefault()?.RowNumber ?? 0)))
            add(WorkbookImportSectionKind.Subclasses, "Subclass notes", (group.Key.FamilyKey.Length > 0 ? group.Key.FamilyKey + ": " : "")
                + string.Join("; ", group.Select(note => note.DisplayName)),
                "These entries do not identify one subclass in the source table. They remain notes; no subclass definition or rule is proposed.",
                WorkbookImportItemState.Found, WorkbookImportPage.Subclasses, action: new WorkbookImportSubclassReviewFinding(),
                sources: Origins(group.SelectMany(note => note.SourceRows)));
        foreach (var sheet in plan.SubclassNotes.SelectMany(note => note.SourceRows.Select(row => (Note: note, Row: row)))
            .GroupBy(item => item.Row.SheetName, StringComparer.Ordinal))
        {
            string column = plan.Choices.ColumnHeaders.GetValueOrDefault(WorkbookImportSummaryFields.Subclasses, "");
            var note = new PanelWorkbookSourceNote { SheetName = sheet.Key, Text = string.Join("\n", sheet.Select(item =>
                "Row " + item.Row.RowNumber.ToString(CultureInfo.InvariantCulture) + ", " + column + ", family " + item.Note.FamilyKey + ": " + item.Note.DisplayName)) };
            add(WorkbookImportSectionKind.Notes, "Subclass notes", sheet.Key,
                "Unmatched expected subclass text is kept verbatim with its source rows and family keys.", WorkbookImportItemState.Proposed,
                WorkbookImportPage.Subclasses, action: note, sources: Origins(sheet.Select(item => item.Row)));
        }

        bool Proposed(string proposal, string rule) => plan.Actions.Any(action => action.Value is WorkbookImportSubclass item
            && item.ProposalId == proposal && item.RuleId == rule && action.Item.State == WorkbookImportItemState.Proposed);
        string Domains(IEnumerable<string> accessions) => string.Join(" + ", accessions.Order(StringComparer.Ordinal).Select(accession =>
        {
            string name = plan.Vocabulary.FirstOrDefault(domain => domain.Accession == accession)?.Name ?? "";
            return name.Length > 0 ? name + " (" + accession + ")" : accession;
        }));
        static string DefinitionName(FamilyPanelSubclass definition) => definition.DisplayName == definition.Key
            ? definition.DisplayName : definition.DisplayName + " [" + definition.Key + "]";
    }
}
