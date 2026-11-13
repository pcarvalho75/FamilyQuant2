using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Panels.Import;

public static partial class WorkbookImportSummaries
{
    public static IReadOnlyCollection<string> ProposedFamilyKeys(PanelWorkbookProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var source = Copy(proposal);
        return Freeze(KeyPreview(source, PrepareChoices(source)).Rows.SelectMany(row => row.FamilyKeys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    public static WorkbookImportSummary Build(PanelWorkbookProposal proposal, PanelEvidenceMatching matching,
        IReadOnlyList<PanelSubclassAcceptance>? reviewedSubclasses = null,
        IReadOnlyDictionary<string, IReadOnlyList<PanelEvidenceChoice>>? reviewedEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(proposal); ArgumentNullException.ThrowIfNull(matching);
        var source = Copy(proposal); var choices = PrepareChoices(source);
        var keyPreview = KeyPreview(source, choices);
        var keys = Freeze(keyPreview.Rows.SelectMany(row => row.FamilyKeys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var expected = ExpectedNames(source, choices, keyPreview, out var subclassNotes);
        var vocabulary = matching.RecognizedPfamDomains().Select(Copy).ToList();
        var subclassProposals = PanelSubclassImport.Propose(source.SubclassTables, keys, vocabulary, expected).Proposals;
        var plan = new WorkbookImportPlan { Proposal = source, Choices = choices, Matching = matching,
            Subclasses = Copy(subclassProposals), ExpectedNames = expected, SubclassNotes = subclassNotes, Vocabulary = vocabulary,
            ReviewedSubclasses = reviewedSubclasses == null ? DraftSubclasses(subclassProposals, keys) : Copy(reviewedSubclasses.ToList()),
            ReviewedEvidence = new(StringComparer.Ordinal) };
        var table = source.FamilyTable;
        WorkbookImportSource At(IEnumerable<int>? rows = null, IEnumerable<string>? columns = null, string? sheet = null) =>
            Source(sheet ?? source.FamilySheet, rows, columns);
        void Add(WorkbookImportSectionKind kind, string label, string value, string reason, WorkbookImportItemState state,
            WorkbookImportPage page, bool required = false, object? action = null, IEnumerable<WorkbookImportSource>? sources = null, string warning = "")
        {
            string id = kind.ToString().ToLowerInvariant() + ":" + plan.Actions.Count(row => row.Section == kind);
            var item = new WorkbookImportItem(id, label, value, string.IsNullOrWhiteSpace(reason) ? "Source finding retained for review." : reason,
                warning, state, page, required, Freeze(sources ?? new[] { At() }));
            plan.Actions.Add(new(kind, item, action));
        }
        foreach (var sheet in source.Workbook.Sheets)
        {
            bool selected = sheet.SheetName == source.FamilySheet;
            var state = selected ? source.NeedsFamilySheetChoice || sheet.IsUncertain ? WorkbookImportItemState.Uncertain : WorkbookImportItemState.Proposed
                : sheet.IsUncertain ? WorkbookImportItemState.CouldNotPlace : WorkbookImportItemState.Found;
            Add(WorkbookImportSectionKind.Sheets, selected ? "Family list" : "Source sheet", sheet.SheetName + ": " + sheet.Role
                + (selected && table != null ? ", " + table.Rows.Count + " rows; header row " + table.HeaderRowNumber : ""), sheet.RoleReason,
                state, WorkbookImportPage.FileAndSheets, selected, sources: new[] { At(sheet.SourceRows.Select(row => row.RowNumber), sheet: sheet.SheetName) });
        }
        if (table == null || source.NeedsFamilySheetChoice)
            Add(WorkbookImportSectionKind.Sheets, "Family list", source.FamilySheet, "Choose the family sheet before acceptance.",
                WorkbookImportItemState.Uncertain, WorkbookImportPage.FileAndSheets, true);

        foreach (string role in new[] { PanelImportFields.FamilyKey, PanelImportFields.DisplayName }.Concat(choices.ColumnHeaders.Keys).Distinct(StringComparer.Ordinal))
        {
            string header = choices.ColumnHeaders.GetValueOrDefault(role, "");
            bool required = role is PanelImportFields.FamilyKey or PanelImportFields.DisplayName;
            bool known = KnownRole(role), valid = table?.Headers.Contains(header, StringComparer.Ordinal) == true;
            var state = !known ? WorkbookImportItemState.CouldNotPlace : valid ? WorkbookImportItemState.Proposed
                : required || header.Length > 0 ? WorkbookImportItemState.Uncertain : WorkbookImportItemState.Found;
            Add(WorkbookImportSectionKind.Columns, RoleLabel(role), header.Length == 0 ? "Not used" : header,
                source.FamilyProposal.Reasons.GetValueOrDefault(role, valid ? "The selected source column is shown for acceptance." : "Choose a source column or leave this optional role unused."),
                state, WorkbookImportPage.Columns, required || header.Length > 0 && !valid, valid && known ? new WorkbookImportColumn(role, header) : null,
                new[] { At(columns: header.Length == 0 ? null : new[] { header }) });
        }
        if (table != null)
            foreach (string header in table.Headers.Except(choices.ColumnHeaders.Values, StringComparer.Ordinal))
                Add(WorkbookImportSectionKind.Columns, header, "Kept as source text", "This column has no classification role; its original cells remain source text.",
                    WorkbookImportItemState.Found, WorkbookImportPage.Columns, sources: new[] { At(columns: new[] { header }) });

        bool useColors = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.ColorCategory, "").Length > 0;
        if (source.ColorsState != PanelWorkbookReadState.Read)
            Add(WorkbookImportSectionKind.Colors, "Workbook colours", "Not read", "Read the workbook colours before acceptance.", WorkbookImportItemState.Uncertain, WorkbookImportPage.Colors, true);
        foreach (var color in source.Colors)
        {
            var explicitLabel = choices.ColorLabels?.FirstOrDefault(label => string.Equals(label.Color, color.Color, StringComparison.OrdinalIgnoreCase));
            string name = explicitLabel?.Label ?? color.Name;
            bool uncertain = name.Length == 0 || explicitLabel == null && color.IsUncertain;
            Add(WorkbookImportSectionKind.Colors, color.Color, (name.Length == 0 ? "Unnamed source fill" : name) + (color.Meaning.Length == 0 ? "" : ": " + color.Meaning),
                explicitLabel != null ? "The reviewed category label is used." : uncertain ? "The source legend does not establish one colour name." : "The colour name comes from the source legend.",
                !useColors ? WorkbookImportItemState.Found : uncertain ? WorkbookImportItemState.Uncertain : WorkbookImportItemState.Proposed,
                WorkbookImportPage.Colors, useColors && uncertain, new FamilyPanelColorLabel { Color = color.Color, Label = name },
                color.Entries.Count == 0 ? new[] { At() } : color.Entries.Select(entry => At(new[] { entry.RowNumber }, sheet: entry.SheetName)));
        }
        string decisionColumn = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Decision, "");
        if (source.DecisionsState != PanelWorkbookReadState.Read)
            Add(WorkbookImportSectionKind.Decisions, "Workbook decisions", "Not read", "Read the workbook decisions before acceptance.", WorkbookImportItemState.Uncertain, WorkbookImportPage.Decisions, true);
        var labels = table == null || decisionColumn.Length == 0 ? source.Decisions.Select(hint => hint.SourceValue)
            : table.Rows.Select(row => row.Cells.GetValueOrDefault(decisionColumn, "")).Where(label => !string.IsNullOrWhiteSpace(label));
        foreach (string label in labels.Distinct(StringComparer.Ordinal))
        {
            var hints = source.Decisions.Where(hint => hint.SourceValue == label).ToArray();
            var hint = hints.Length == 1 ? hints[0] : null;
            var mapping = hint?.ProposedMapping;
            bool valid = mapping != null && mapping.SourceValue == label && Enum.IsDefined(mapping.Decision);
            Add(WorkbookImportSectionKind.Decisions, label, valid ? mapping!.Decision + (mapping.MergeTargetFamilyKey.Length == 0 ? "" : ": " + mapping.MergeTargetFamilyKey) : "Needs your choice",
                hint?.Reason ?? "Each source decision needs one explicit mapping; no default is applied.",
                decisionColumn.Length == 0 ? WorkbookImportItemState.Found : valid ? WorkbookImportItemState.Proposed : WorkbookImportItemState.Uncertain,
                WorkbookImportPage.Decisions, decisionColumn.Length > 0 && !valid, valid ? Copy(mapping!) : null,
                new[] { At(hint?.RowNumbers ?? table?.Rows.Where(row => row.Cells.GetValueOrDefault(decisionColumn, "") == label).Select(row => row.RowNumber).ToList(), new[] { decisionColumn }) }
                    .Concat(hint?.LegendEntries.Select(entry => At(new[] { entry.RowNumber }, sheet: entry.SheetName)) ?? Enumerable.Empty<WorkbookImportSource>()));
        }
        var rowChoices = choices.RowChoices ?? new();
        foreach (int row in source.MultiFamilyRows.Select(row => row.RowNumber).Concat(rowChoices.Select(choice => choice.RowNumber)).Distinct())
        {
            var multi = source.MultiFamilyRows.FirstOrDefault(item => item.RowNumber == row);
            var selected = rowChoices.Where(choice => choice.RowNumber == row).ToArray();
            var chosen = selected.Length == 1 ? Copy(selected[0]) : selected.Length == 0 && multi?.SuggestedAction is { } action
                ? new FamilyPanelRowChoice { RowNumber = row, Action = action } : null;
            Add(WorkbookImportSectionKind.FamilyRows, "Family row " + row, chosen == null ? "Needs your choice" : chosen.Action.ToString(),
                chosen == null ? "Choose how to handle this multi-family row before acceptance." : "The shown row choice is applied exactly, including reviewed split parts and names.",
                chosen == null ? WorkbookImportItemState.Uncertain : WorkbookImportItemState.Proposed, WorkbookImportPage.FamilyRows, true, chosen,
                new[] { At(new[] { row }, multi == null ? null : new[] { multi.ListColumn }) });
        }
        var preview = table == null ? new PanelImportCheck() : new FamilyPanelSpreadsheetImporter().Check(table, choices,
            plan.Actions.Where(action => action.Item.State == WorkbookImportItemState.Proposed).Select(action => action.Value).OfType<FamilyPanelDecisionMapping>().ToArray());
        foreach (var hint in source.Pfam)
        {
            if (choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Pfam, "") != hint.Column) continue;
            string? key = keyPreview.Rows.FirstOrDefault(row => row.RowNumber == hint.RowNumber)?.FamilyKeys.SingleOrDefaultSafe();
            string text = hint.SourceText.Length > 0 ? hint.SourceText : string.Join("; ", hint.Domains.Select(domain => domain.Accession));
            bool active = key != null && (preview.Panel == null || FamilyPanelValidation.ResolveFamilyKey(preview.Panel, key) == key);
            bool needsChoice = active && FamilyPanelSpreadsheetImporter.NeedsPfamChoice(text);
            bool reviewed = key != null && reviewedEvidence?.ContainsKey(key) == true;
            if (needsChoice && !reviewed)
                Add(WorkbookImportSectionKind.Pfam, "Pfam for row " + hint.RowNumber + " (" + hint.FamilyKey + ")", text,
                    "Resolve the Pfam name without a code, or explicitly skip this row's matches.",
                    WorkbookImportItemState.Uncertain, WorkbookImportPage.EvidenceMatches, true,
                    sources: new[] { At(new[] { hint.RowNumber }, new[] { hint.Column }) });
            if (text.Length == 0 && active)
                Add(WorkbookImportSectionKind.Pfam, "Row " + hint.RowNumber + " (" + hint.FamilyKey + ")", "No Pfam code",
                    "This family gets no proteins until it has a Pfam code.", WorkbookImportItemState.Found,
                    WorkbookImportPage.EvidenceMatches, sources: new[] { At(new[] { hint.RowNumber }, new[] { hint.Column }) });
            foreach (var domain in hint.Domains)
            {
                bool selected = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Pfam, "").Length > 0;
                bool valid = key != null && domain.Accession.Length > 0;
                Add(WorkbookImportSectionKind.Pfam, "Source Pfam domain", domain.Accession + (domain.Name.Length == 0 ? "" : ": " + domain.Name),
                    !selected ? "The domain column is not selected; its cells remain source text." : !valid ? "Assign this source row to one family before assigning its domains." : "This domain definition is from the source family row, separate from evidence-match selections.",
                    !selected ? WorkbookImportItemState.Found : valid ? WorkbookImportItemState.Proposed : WorkbookImportItemState.Uncertain,
                    WorkbookImportPage.EvidenceMatches, false, valid ? new WorkbookImportDomain(key!, Copy(domain)) : null,
                    new[] { At(new[] { hint.RowNumber }, new[] { hint.Column }) });
            }
        }
        if (reviewedEvidence == null)
        {
            foreach (var group in plan.Actions.Where(action => action.Item.State == WorkbookImportItemState.Proposed).Select(action => action.Value)
                .OfType<WorkbookImportDomain>().GroupBy(domain => domain.FamilyKey, StringComparer.Ordinal))
                plan.ReviewedEvidence[group.Key] = Freeze(group.Select(domain => new PanelEvidenceChoice(PanelEvidenceMatchKind.Pfam, domain.Domain.Accession)).Distinct());
            foreach (string key in keys.Where(key => !plan.ReviewedEvidence.ContainsKey(key)
                && !source.Pfam.Any(hint => hint.FamilyKey == key && hint.Column == choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Pfam, "")
                    && (hint.SourceText.Length > 0 || hint.Domains.Count > 0))))
                plan.ReviewedEvidence[key] = Array.Empty<PanelEvidenceChoice>();
        }
        else foreach (var pair in reviewedEvidence) plan.ReviewedEvidence[pair.Key] = Freeze(pair.Value.Select(choice => choice with { }));
        foreach (var pair in plan.ReviewedEvidence)
        {
            var rows = keyPreview.Rows.Where(row => row.FamilyKeys.Contains(pair.Key, StringComparer.Ordinal)).Select(row => row.RowNumber);
            bool familyExists = keys.Contains(pair.Key, StringComparer.Ordinal);
            foreach (var choice in pair.Value.Cast<PanelEvidenceChoice?>().DefaultIfEmpty(null))
            {
                bool valid = familyExists && (choice == null || Enum.IsDefined(choice.Kind) && !string.IsNullOrWhiteSpace(choice.Value) && !choice.Value.Any(char.IsControl));
                var coverage = choice == null || !valid ? null : matching.Coverage(choice.Kind, choice.Value);
                bool suppliedCode = choice?.Kind == PanelEvidenceMatchKind.Pfam
                    && plan.Actions.Any(action => action.Value is WorkbookImportDomain domain && domain.FamilyKey == pair.Key
                        && domain.Domain.Accession.Equals(choice.Value, StringComparison.OrdinalIgnoreCase));
                string warning = choice == null || !valid || suppliedCode ? "" : matching.ConfirmationWarning(new[] { choice });
                Add(WorkbookImportSectionKind.Pfam, "Reviewed evidence for " + pair.Key, choice == null ? "No matches selected" : choice.Kind + ": " + choice.Value,
                    !valid ? "Choose a valid label and an existing proposed family." : choice == null ? "The empty selection is an explicit saved untick."
                        : coverage!.DatabaseTargets + " database targets and " + coverage.ReferenceEntries + " stored references; only this shown choice is selected.",
                    valid ? WorkbookImportItemState.Proposed : WorkbookImportItemState.CouldNotPlace, WorkbookImportPage.EvidenceMatches, !valid,
                    new WorkbookImportEvidence(pair.Key, choice), new[] { At(rows) }, warning);
            }
        }
        AddSubclasses(plan, keys, Add);
        foreach (var note in source.Notes)
            Add(WorkbookImportSectionKind.Notes, note.SheetName, "Source text kept with the panel", "Original cell addresses and text are kept with the panel, without interpretation.",
                WorkbookImportItemState.Proposed, WorkbookImportPage.Notes, action: Copy(note),
                sources: new[] { At(source.Workbook.Sheets.FirstOrDefault(sheet => sheet.SheetName == note.SheetName)?.SourceRows.Select(row => row.RowNumber), sheet: note.SheetName) });
        PrepareFamilyRows(plan, preview, keyPreview);
        foreach (var problem in source.Problems.Concat(preview.Problems).DistinctBy(problem => JsonSerializer.Serialize(problem)))
        {
            if (problem.Severity != PanelImportSeverity.Blocking) continue;
            bool decisionAlreadyShown = (problem.Kind is PanelImportProblemKind.DecisionNotMapped or PanelImportProblemKind.DecisionMappingInvalid)
                && !string.IsNullOrWhiteSpace(problem.Value)
                && (string.IsNullOrEmpty(problem.SheetName) || problem.SheetName == source.FamilySheet)
                && (string.IsNullOrEmpty(problem.Column) || problem.Column == decisionColumn)
                && plan.Actions.Any(action => action.Section == WorkbookImportSectionKind.Decisions && action.Item.Label == problem.Value
                    && action.Item.Required && (action.Item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace)
                    && (problem.RowNumber <= 0 || action.Item.Sources.Any(finding => finding.RowNumbers.Contains(problem.RowNumber))));
            if (decisionAlreadyShown) continue;
            Add(WorkbookImportSectionKind.FamilyRows, "Workbook problem", problem.Message, problem.Message, WorkbookImportItemState.CouldNotPlace,
                problem.Kind is PanelImportProblemKind.DecisionNotMapped or PanelImportProblemKind.DecisionMappingInvalid ? WorkbookImportPage.Decisions : WorkbookImportPage.FamilyRows,
                true, sources: new[] { At(problem.RowNumber > 0 ? new[] { problem.RowNumber } : null, new[] { problem.Column }, problem.SheetName) });
        }
        foreach (var kind in Enum.GetValues<WorkbookImportSectionKind>())
            if (!plan.Actions.Any(action => action.Section == kind))
                Add(kind, kind.ToString(), "None found", "No findings of this kind were supplied by the workbook proposal.", WorkbookImportItemState.Found,
                    kind switch { WorkbookImportSectionKind.Colors => WorkbookImportPage.Colors, WorkbookImportSectionKind.Decisions => WorkbookImportPage.Decisions,
                        WorkbookImportSectionKind.Pfam => WorkbookImportPage.EvidenceMatches, WorkbookImportSectionKind.Subclasses => WorkbookImportPage.Subclasses,
                        WorkbookImportSectionKind.Notes => WorkbookImportPage.Notes, _ => WorkbookImportPage.Columns });
        var sections = Freeze(Enum.GetValues<WorkbookImportSectionKind>().Select(kind => new WorkbookImportSection(kind,
            kind == WorkbookImportSectionKind.Pfam ? "Pfam and evidence matches" : kind.ToString(), Freeze(plan.Actions.Where(action => action.Section == kind).Select(action => action.Item)))));
        return new(plan, sections, keys);
    }

    private static FamilyPanelImport PrepareChoices(PanelWorkbookProposal proposal)
    {
        var choices = Copy(proposal.FamilyProposal.Choices);
        if (!choices.ColumnHeaders.ContainsKey(PanelImportFields.Decision) && proposal.FamilyProposal.DecisionHint.Length > 0)
            choices.ColumnHeaders[PanelImportFields.Decision] = proposal.FamilyProposal.DecisionHint;
        if (!choices.ColumnHeaders.ContainsKey(WorkbookImportSummaryFields.Subclasses))
        {
            string column = InferSubclassColumn(proposal, choices);
            if (column.Length > 0) choices.ColumnHeaders[WorkbookImportSummaryFields.Subclasses] = column;
        }
        return choices;
    }
    private static PanelImportCheck KeyPreview(PanelWorkbookProposal proposal, FamilyPanelImport choices)
    {
        if (proposal.FamilyTable == null) return new();
        var sourceChoices = Copy(choices); sourceChoices.ColumnHeaders.Remove(PanelImportFields.Decision); sourceChoices.ColumnHeaders.Remove(PanelImportFields.ColorCategory);
        return new FamilyPanelSpreadsheetImporter().Check(proposal.FamilyTable, sourceChoices, Array.Empty<FamilyPanelDecisionMapping>());
    }
    private static string? SingleOrDefaultSafe(this IReadOnlyList<string> values) => values.Count == 1 ? values[0] : null;
    private static bool KnownRole(string role) => role is PanelImportFields.FamilyKey or PanelImportFields.DisplayName or PanelImportFields.OtherName
        or PanelImportFields.Decision or PanelImportFields.Pfam or PanelImportFields.UniProtFamilies or PanelImportFields.Notes or PanelImportFields.ColorCategory or WorkbookImportSummaryFields.Subclasses;
    private static string RoleLabel(string role) => role switch { PanelImportFields.FamilyKey => "Family key", PanelImportFields.DisplayName => "Family name",
        PanelImportFields.OtherName => "Other name", PanelImportFields.UniProtFamilies => "UniProt family", PanelImportFields.ColorCategory => "Colour category", WorkbookImportSummaryFields.Subclasses => "Expected subclasses", _ => role };
    private static WorkbookImportSource Source(string sheet, IEnumerable<int>? rows = null, IEnumerable<string>? columns = null) =>
        new(sheet, Freeze(rows?.Distinct().Order() ?? Enumerable.Empty<int>()), Freeze(columns?.Where(column => column.Length > 0).Distinct(StringComparer.Ordinal) ?? Enumerable.Empty<string>()));
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
