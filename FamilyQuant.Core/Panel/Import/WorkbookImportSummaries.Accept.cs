using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Panels.Import;

public static partial class WorkbookImportSummaries
{
    public static WorkbookImportAcceptResult AcceptAll(WorkbookImportSummary summary, IFamilyPanelSpreadsheetImporter importer,
        IReadOnlyList<PanelSubclassAcceptance> reviewedSubclasses, IReadOnlyDictionary<string, IReadOnlyList<PanelEvidenceChoice>> reviewedEvidence,
        string panelName = "", bool broadPfamConfirmed = false,
        Func<IReadOnlyList<PanelSubclassProposal>, IReadOnlyList<PanelSubclassAcceptance>, IReadOnlyCollection<string>, PanelSubclassBuildResult>? buildDefinitions = null)
    {
        ArgumentNullException.ThrowIfNull(summary); ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(reviewedSubclasses); ArgumentNullException.ThrowIfNull(reviewedEvidence);
        var plan = summary.Plan;
        var problems = new List<WorkbookImportProblem>();
        var first = plan.Actions.First().Item;
        void Problem(WorkbookImportItem item, string message, bool blocking, string detail = "")
        {
            var source = item.Sources.First();
            problems.Add(new(item.Id, detail, source.SheetName, item.Label + ": " + message + " Open " + item.Page + ".",
                source.RowNumbers, blocking, item.Page));
        }
        WorkbookImportAcceptResult Result(FamilyPanel? panel) => new(panel, Freeze(plan.Actions.Select(action => new WorkbookImportItemResult(action.Item.Id,
            panel != null && action.Item.State == WorkbookImportItemState.Proposed,
            Freeze(problems.Where(problem => problem.ItemId == action.Item.Id))))));
        foreach (var action in plan.Actions.Where(action => action.Item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace))
            Problem(action.Item, action.Item.Reason, action.Item.Required);
        if (SubclassSelectionKey(reviewedSubclasses) != SubclassSelectionKey(plan.ReviewedSubclasses))
            Problem(plan.Actions.FirstOrDefault(action => action.Section == WorkbookImportSectionKind.Subclasses)?.Item ?? first,
                "Reviewed subclass selections changed. Rebuild the summary before acceptance.", true);
        if (EvidenceSelectionKey(reviewedEvidence) != EvidenceSelectionKey(plan.ReviewedEvidence))
            Problem(plan.Actions.FirstOrDefault(action => action.Value is WorkbookImportEvidence)?.Item ?? first,
                "Reviewed evidence selections changed. Rebuild the summary before acceptance.", true);
        if (!broadPfamConfirmed)
            foreach (var action in plan.Actions.Where(action => action.Item.State == WorkbookImportItemState.Proposed && action.Item.ConfirmationWarning.Length > 0))
                Problem(action.Item, action.Item.ConfirmationWarning, true);
        if (problems.Any(problem => problem.Blocking)) return Result(null);

        var proposal = Copy(plan.Proposal); var choices = AcceptedChoices(plan);
        proposal.Notes = plan.Actions.Where(action => action.Item.State == WorkbookImportItemState.Proposed).Select(action => action.Value)
            .OfType<PanelWorkbookSourceNote>().Select(Copy).ToList();
        var decisions = plan.Actions.Where(action => action.Item.State == WorkbookImportItemState.Proposed).Select(action => action.Value)
            .OfType<FamilyPanelDecisionMapping>().Select(Copy).ToArray();
        PanelImportCheck check;
        try { check = importer.CheckConfirmedWorkbook(proposal, choices, decisions, panelName); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        { Problem(first, error.Message, true); return Result(null); }
        foreach (var problem in check.Problems)
        {
            var item = plan.Actions.FirstOrDefault(action => action.Item.Sources.Any(source => (problem.SheetName == null || source.SheetName == problem.SheetName)
                && (problem.RowNumber == 0 || source.RowNumbers.Contains(problem.RowNumber))))?.Item ?? first;
            problems.Add(new(item.Id, "", problem.SheetName ?? proposal.FamilySheet, item.Label + ": " + problem.Message + " Open " + item.Page + ".",
                Freeze(problem.RowNumber > 0 ? new[] { problem.RowNumber }.Concat(problem.OtherRowNumbers) : problem.OtherRowNumbers),
                problem.Severity == PanelImportSeverity.Blocking, item.Page));
        }
        if (check.Panel == null || problems.Any(problem => problem.Blocking))
        {
            if (!problems.Any(problem => problem.Blocking)) Problem(first, "The family check returned no panel.", true);
            return Result(null);
        }
        var panel = FamilyPanelValidation.Clone(check.Panel);
        // Imported source domains and confirmed evidence choices are distinct, including saved unticks.
        foreach (var family in panel.Families) { family.Pfam.Clear(); family.Subclasses.Clear(); }
        foreach (var group in plan.Actions.Where(action => action.Item.State == WorkbookImportItemState.Proposed).Select(action => action.Value)
            .OfType<WorkbookImportDomain>().GroupBy(domain => domain.FamilyKey, StringComparer.Ordinal))
        {
            var family = panel.Families.FirstOrDefault(family => family.Key == group.Key);
            if (family == null) { Problem(first, "A displayed domain's family is missing from the checked import.", true); continue; }
            family.Pfam.AddRange(group.Select(domain => Copy(domain.Domain)).DistinctBy(domain => (domain.Accession, domain.Name)));
        }
        foreach (var pair in reviewedEvidence)
        {
            var family = panel.Families.FirstOrDefault(family => family.Key == pair.Key);
            if (family == null) { Problem(first, "A reviewed evidence family is missing from the checked import.", true); continue; }
            try { panel = PanelEvidenceMatching.WithConfirmed(panel, pair.Key,
                PanelEvidenceMatching.Confirm(family.Names.Select(name => name.Text).Prepend(family.DisplayName), pair.Value)); }
            catch (ArgumentException error) { Problem(first, error.Message, true); }
        }
        if (problems.Any(problem => problem.Blocking)) return Result(null);
        if (plan.Subclasses.Count > 0 || reviewedSubclasses.Count > 0)
        {
            buildDefinitions ??= PanelSubclassImport.BuildDefinitions;
            var built = buildDefinitions(Copy(plan.Subclasses), Copy(reviewedSubclasses.ToList()), Freeze(panel.Families.Select(family => family.Key)));
            foreach (var problem in built.Problems)
            {
                var item = plan.Actions.FirstOrDefault(action => action.Value is WorkbookImportSubclass subclass && subclass.ProposalId == problem.ProposalId
                    && subclass.RuleId == problem.RuleId)?.Item ?? plan.Actions.FirstOrDefault(action => action.Section == WorkbookImportSectionKind.Subclasses)?.Item ?? first;
                Problem(item, problem.Message, true, problem.RuleId);
            }
            if (problems.Any(problem => problem.Blocking)) return Result(null);
            var accepted = reviewedSubclasses.Where(choice => choice.Accepted).ToArray();
            if (built.Definitions.Count != accepted.Length)
                Problem(first, "The subclass builder did not return exactly the displayed accepted definitions.", true);
            foreach (var definition in built.Definitions)
            {
                var selection = accepted.FirstOrDefault(choice => plan.Subclasses.Any(proposal => proposal.ProposalId == choice.ProposalId
                    && Pick(choice.FamilyKey, proposal.FamilyKey) == definition.FamilyKey && Pick(choice.Key, proposal.Key) == definition.Definition.Key
                    && Pick(choice.DisplayName, proposal.DisplayName) == definition.Definition.DisplayName));
                if (selection == null) { Problem(first, "The subclass builder returned a definition outside the displayed selections.", true); continue; }
                var original = plan.Subclasses.Single(proposal => proposal.ProposalId == selection.ProposalId);
                var selectedRules = selection.Rules.Where(rule => rule.Accepted).Select(rule => (Rule: original.Rules.Single(value => value.RuleId == rule.RuleId),
                    Kind: rule.Kind ?? original.Rules.Single(value => value.RuleId == rule.RuleId).Kind)).ToArray();
                bool sameRules = definition.Definition.RequiredPfam.ToHashSet(StringComparer.Ordinal).SetEquals(selectedRules.Where(rule => rule.Kind == PanelSubclassRuleKind.RequiredPfam).Select(rule => rule.Rule.Accession))
                    && definition.Definition.AbsentPfam.ToHashSet(StringComparer.Ordinal).SetEquals(selectedRules.Where(rule => rule.Kind == PanelSubclassRuleKind.AbsentPfam).Select(rule => rule.Rule.Accession))
                    && definition.Definition.UniProtSubfamilies.Count == 0 && definition.Definition.ReportAsSubclassKey.Length == 0;
                if (!sameRules) { Problem(first, "The subclass builder returned rules outside the displayed selections.", true); continue; }
                panel.Families.Single(family => family.Key == definition.FamilyKey).Subclasses.Add(Copy(definition.Definition));
            }
        }
        try { FamilyPanelValidation.Validate(panel); }
        catch (ArgumentException error) { Problem(first, error.Message, true); }
        return Result(problems.Any(problem => problem.Blocking) ? null : panel);
    }

    private static string EvidenceSelectionKey(IReadOnlyDictionary<string, IReadOnlyList<PanelEvidenceChoice>> values) => JsonSerializer.Serialize(values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => new { pair.Key, Choices = pair.Value.Distinct().OrderBy(choice => choice.Kind).ThenBy(choice => choice.Value, StringComparer.Ordinal).ToArray() }));
    private static string SubclassSelectionKey(IReadOnlyList<PanelSubclassAcceptance> values) => JsonSerializer.Serialize(values.OrderBy(choice => choice.ProposalId, StringComparer.Ordinal)
        .Select(choice => new { choice.ProposalId, choice.Accepted, choice.FamilyKey, choice.Key, choice.DisplayName, Rules = choice.Rules.OrderBy(rule => rule.RuleId, StringComparer.Ordinal).ToArray() }));

    internal static FamilyPanelImport AcceptedChoices(WorkbookImportPlan plan)
    {
        var choices = Copy(plan.Choices);
        choices.ColumnHeaders = plan.Actions.Where(action => action.Item.State == WorkbookImportItemState.Proposed).Select(action => action.Value)
            .OfType<WorkbookImportColumn>().ToDictionary(column => column.Role, column => column.Header, StringComparer.Ordinal);
        choices.RowChoices = plan.Actions.Where(action => action.Item.State == WorkbookImportItemState.Proposed).Select(action => action.Value)
            .OfType<FamilyPanelRowChoice>().Select(Copy).ToList();
        choices.ColorLabels = plan.Actions.Where(action => action.Item.State == WorkbookImportItemState.Proposed).Select(action => action.Value)
            .OfType<FamilyPanelColorLabel>().Select(Copy).ToList();
        return choices;
    }
}
