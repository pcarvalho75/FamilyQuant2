using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Panels.Import;

public static partial class WorkbookImportSummaries
{
    private delegate void AddFinding(WorkbookImportSectionKind section, string label, string value, string reason, WorkbookImportItemState state,
        WorkbookImportPage page, bool required = false, object? action = null, IEnumerable<WorkbookImportSource>? sources = null, string warning = "");

    private static void AddSubclasses(WorkbookImportPlan plan, IReadOnlyCollection<string> keys, AddFinding add)
    {
        WorkbookImportSource[] FromRows(IReadOnlyList<PanelSubclassSourceRow> rows) => rows.Count == 0
            ? new[] { Source(plan.Proposal.FamilySheet) }
            : rows.GroupBy(row => row.SheetName, StringComparer.Ordinal).Select(group => Source(group.Key,
                group.Select(row => row.RowNumber), group.SelectMany(row => row.Cells.Keys))).ToArray();
        foreach (var proposal in plan.Subclasses)
        {
            var selected = plan.ReviewedSubclasses.Where(choice => choice.ProposalId == proposal.ProposalId).ToArray();
            var choice = selected.Length == 1 ? selected[0] : null;
            bool accepted = choice?.Accepted == true;
            string family = Pick(choice?.FamilyKey, proposal.FamilyKey), key = Pick(choice?.Key, proposal.Key), name = Pick(choice?.DisplayName, proposal.DisplayName);
            bool placed = keys.Contains(family, StringComparer.Ordinal);
            bool explicitlyResolved = accepted && placed && !string.IsNullOrWhiteSpace(choice!.FamilyKey)
                && !string.IsNullOrWhiteSpace(choice.Key) && !string.IsNullOrWhiteSpace(choice.DisplayName);
            bool certain = (!proposal.IsUncertain || explicitlyResolved) && !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(name);
            var state = selected.Length > 1 || !placed ? WorkbookImportItemState.CouldNotPlace : !certain ? WorkbookImportItemState.Uncertain
                : accepted ? WorkbookImportItemState.Proposed : WorkbookImportItemState.Found;
            add(WorkbookImportSectionKind.Subclasses, "Subclass", family + ": " + key + " " + name,
                selected.Length > 1 ? "This subclass has repeated review choices." : proposal.IsUncertain && explicitlyResolved
                    ? "Explicit review resolved the family key, subclass key and name. Unparsed source text remains notes; only selected certain canonical rules can execute. " + proposal.Reason
                    : !accepted && certain && placed ? "This known subclass has no accepted review selection."
                    : proposal.Reason.Length > 0 ? proposal.Reason : "The subclass source and explicit review choice are retained.",
                state, WorkbookImportPage.Subclasses, accepted && state != WorkbookImportItemState.Proposed || selected.Length > 1,
                new WorkbookImportSubclass(proposal.ProposalId, ""), FromRows(proposal.SourceRows));
            foreach (var rule in proposal.Rules)
            {
                var selectedRules = choice?.Rules.Where(selection => selection.RuleId == rule.RuleId).ToArray() ?? Array.Empty<PanelSubclassRuleAcceptance>();
                var selection = selectedRules.Length == 1 ? selectedRules[0] : null;
                bool ruleAccepted = selection?.Accepted == true;
                var kind = selection?.Kind ?? rule.Kind;
                // Explicit review resolves polarity only; the canonical accession checks still apply.
                bool valid = CertainRule(rule, ruleAccepted && selection?.Kind != null) && Enum.IsDefined(kind);
                var ruleState = selectedRules.Length > 1 ? WorkbookImportItemState.CouldNotPlace
                    : !valid ? WorkbookImportItemState.Uncertain : ruleAccepted && accepted ? WorkbookImportItemState.Proposed : WorkbookImportItemState.Found;
                add(WorkbookImportSectionKind.Subclasses, "Subclass rule", family + ": " + key + ", " + kind + " " + rule.Accession,
                    ruleAccepted && !accepted ? "This known draft rule is inactive while its subclass is unselected." : selectedRules.Length > 1 ? "This rule has repeated review choices."
                        : rule.Reason.Length > 0 ? rule.Reason : valid && !ruleAccepted ? "This recognized rule is not selected." : "Resolve the rule's canonical accession before acceptance.",
                    ruleState, WorkbookImportPage.Subclasses, ruleAccepted && !valid || selectedRules.Length > 1,
                    new WorkbookImportSubclass(proposal.ProposalId, rule.RuleId), FromRows(rule.SourceRows.Count > 0 ? rule.SourceRows : proposal.SourceRows));
            }
            foreach (var unknown in choice?.Rules.Where(rule => !proposal.Rules.Any(known => known.RuleId == rule.RuleId)) ?? Enumerable.Empty<PanelSubclassRuleAcceptance>())
                add(WorkbookImportSectionKind.Subclasses, "Unknown subclass rule", unknown.RuleId, "This rule ID is not in the displayed subclass proposal.",
                    WorkbookImportItemState.CouldNotPlace, WorkbookImportPage.Subclasses, true, new WorkbookImportSubclass(proposal.ProposalId, unknown.RuleId), FromRows(proposal.SourceRows));
        }
        foreach (var unknown in plan.ReviewedSubclasses.Where(choice => !plan.Subclasses.Any(proposal => proposal.ProposalId == choice.ProposalId)))
            add(WorkbookImportSectionKind.Subclasses, "Unknown subclass selection", unknown.ProposalId, "This proposal ID is not in the displayed source findings.",
                WorkbookImportItemState.CouldNotPlace, WorkbookImportPage.Subclasses, true, new WorkbookImportSubclass(unknown.ProposalId, ""));
        if (plan.Subclasses.Count == 0)
            foreach (var table in plan.Proposal.SubclassTables)
            {
                foreach (var row in table.Table.Rows)
                    add(WorkbookImportSectionKind.Subclasses, "Unplaced subclass source", string.Join("; ", row.Cells.Select(cell => cell.Key + ": " + cell.Value)),
                        "A subclass proposal has not placed this source row. Open Subclasses to review it.", WorkbookImportItemState.CouldNotPlace,
                        WorkbookImportPage.Subclasses, sources: new[] { Source(table.SheetName, new[] { row.RowNumber }, table.Table.Headers) });
                if (table.Table.Rows.Count == 0)
                    add(WorkbookImportSectionKind.Subclasses, "Unplaced subclass source", table.SheetName, "This source table has no placed subclass rows.",
                        WorkbookImportItemState.CouldNotPlace, WorkbookImportPage.Subclasses, sources: new[] { Source(table.SheetName, columns: table.Table.Headers) });
            }
    }

    private static bool CertainRule(PanelSubclassRuleProposal rule, bool polarityResolved = false) => (!rule.IsUncertain || polarityResolved)
        && rule.Accession.Length > 0 && Enum.IsDefined(rule.Kind)
        && rule.CandidateAccessions.Count <= 1 && (rule.CandidateAccessions.Count == 0 || rule.CandidateAccessions.Contains(rule.Accession, StringComparer.Ordinal));
    private static List<PanelSubclassAcceptance> DraftSubclasses(IReadOnlyList<PanelSubclassProposal> proposals, IReadOnlyCollection<string> keys) => proposals.Select(proposal =>
        new PanelSubclassAcceptance { ProposalId = proposal.ProposalId,
            Accepted = !proposal.IsUncertain && keys.Contains(proposal.FamilyKey, StringComparer.Ordinal) && !string.IsNullOrWhiteSpace(proposal.Key) && !string.IsNullOrWhiteSpace(proposal.DisplayName),
            FamilyKey = proposal.FamilyKey, Key = proposal.Key, DisplayName = proposal.DisplayName,
            Rules = proposal.Rules.Select(rule => new PanelSubclassRuleAcceptance { RuleId = rule.RuleId, Accepted = CertainRule(rule), Kind = rule.Kind }).ToList() }).ToList();

    private static string Pick(string? chosen, string fallback) => string.IsNullOrEmpty(chosen) ? fallback : chosen;
}
