using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class FamilyListImportViewModel
{
    private bool reviewingWorkbook, reviewingSubclasses, updatingSubclassDraft, evidencePagePrepared;
    private WorkbookImportSummary? workbookReview;
    private WorkbookImportAcceptResult? workbookAcceptance;
    private PanelWorkbookProposal? reviewedWorkbook;
    private PanelEvidenceMatching? workbookMatching;
    private IReadOnlyDictionary<string, IReadOnlyList<PanelEvidenceChoice>>? reviewedEvidence;
    private string subclassSourceKey = "", workbookReviewError = "", reviewInputKey = "";
    private ImportSubclassChoice? selectedSubclass;
    public ImportSubclassChoice? SelectedSubclass { get => selectedSubclass; set => Set(ref selectedSubclass, value); }
    public bool WorkbookReviewEnabled => openWorkbookReview && workbookSource != null;
    public bool ReviewingWorkbook => reviewingWorkbook;
    public bool ReviewingSubclasses => reviewingSubclasses;
    public bool ShowingWizardPages => !reviewingWorkbook && !reviewingSubclasses;
    public bool CanReturnToWorkbook => WorkbookReviewEnabled && HasWorkbookMap && !Imported && !IsBusy && !reviewingWorkbook && !reviewingSubclasses;
    public IReadOnlyCollection<string> WorkbookFamilyKeys => workbookReview?.FamilyKeys ?? Array.Empty<string>();
    public IReadOnlyList<ImportWorkbookFinding> WorkbookFindings => workbookReview?.Sections
        .Where(section => section.Kind != WorkbookImportSectionKind.Colors).SelectMany(section => section.Items).Select(item =>
        new ImportWorkbookFinding(item.Id, item.Label, item.Value, StateLabel(item.State), PlainWorkbookReason(item.Reason),
            string.Join("; ", item.Sources.Select(source => source.SheetName
                + (source.RowNumbers.Count > 0 ? ", rows " + string.Join(", ", source.RowNumbers) : "")
                + (source.Columns.Count > 0 ? ", " + string.Join(", ", source.Columns) : ""))), PageKey(item.Page))).ToArray() ?? Array.Empty<ImportWorkbookFinding>();
    public IReadOnlyList<ImportWorkbookFinding> SubclassReviewFindings
    {
        get
        {
            var ids = workbookReview?.SubclassReviewFindings.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            return ids == null ? Array.Empty<ImportWorkbookFinding>() : WorkbookFindings.Where(item => ids.Contains(item.Id)).ToArray();
        }
    }
    public bool HasSubclassReviewFindings => (ReviewingWorkbook || ReviewingSubclasses) && SubclassReviewFindings.Count > 0;
    public IReadOnlyList<string> WorkbookReviewProblems => (workbookReview?.Sections.SelectMany(section => section.Items)
        .Where(item => item.Required && item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace)
        .Select(item => item.Label + ": " + WorkbookProblemGuidance(item.Reason, item.Page)) ?? Array.Empty<string>())
        .Concat(workbookAcceptance?.Items.SelectMany(item => item.Problems).Where(problem => problem.Blocking).Select(problem =>
            problem.Message.Replace(" Open " + problem.Page + ".", " Review " + PageLabel(problem.Page) + ".", StringComparison.Ordinal)) ?? Array.Empty<string>())
        .Concat(workbookReviewError.Length > 0 ? new[] { workbookReviewError } : Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
    public bool CanAcceptWorkbook => !IsBusy && !Imported && workbookReview != null && !workbookReview.HasBlockingItems
        && !NeedsFamilySheetChoice && WorkbookReviewProblems.Count == 0;
    public bool WorkbookHasReviewProblems => WorkbookReviewProblems.Count > 0;
    public string WorkbookReviewStatus => WorkbookHasReviewProblems ? WorkbookReviewProblems.Count + " choices need review. " + WorkbookReviewProblems[0] : "";
    public string WorkbookReviewDetails => string.Join("\n", WorkbookReviewProblems);
    public IReadOnlyList<ImportWorkbookFamily> WorkbookFamilies => workbookReview?.FamilyRows.Select(row =>
        new ImportWorkbookFamily(row.Key, row.DisplayName, row.Decision is { } decision ? DecisionOptions.First(d => d.Value == decision).Label : "Choose an action",
            row.MergeTargetFamilyKey)).ToArray() ?? Array.Empty<ImportWorkbookFamily>();
    public IReadOnlyList<ImportWorkbookColumn> WorkbookColumns => Columns.Where(column => column.HasChoice)
        .Select(column => new ImportWorkbookColumn(column.Label, column.Header, column.Example)).ToArray();
    public IReadOnlyList<ImportWorkbookCategory> WorkbookLegend => (workbookSnapshot?.Colors ?? new()).Select(color =>
        new ImportWorkbookCategory(color.Name, SourceColorChoices.FirstOrDefault(choice => choice.Color.Equals(color.Color, StringComparison.OrdinalIgnoreCase))?.Label ?? color.Name,
            color.Meaning, color.IsUncertain ? "Needs your choice" : "Proposed, not confirmed")).ToArray();
    public IReadOnlyList<ImportWorkbookDecisionRow> WorkbookDecisionRows => Decisions.Select(decision => new ImportWorkbookDecisionRow(decision.Source, decision.Meaning,
        decision.Decision == null ? "Choose an action" : DecisionOptions.First(option => option.Value == decision.Decision).Label, decision.MergeTarget,
        decision.Decision == null ? "Needs your choice" : "Proposed, not confirmed")).ToArray();
    public IReadOnlyList<ImportWorkbookSpecialRow> WorkbookSpecialRows => (workbookSnapshot?.MultiFamilyRows ?? new()).Select(row =>
    {
        var chosen = workbookReview?.FamilyChoices.RowChoices?.FirstOrDefault(choice => choice.RowNumber == row.RowNumber);
        return new ImportWorkbookSpecialRow(row.RowNumber, string.Join("; ", chosen?.Parts?.Count > 0 ? chosen.Parts.Select(part => part.Name) : row.ProposedParts.Select(part => part.Name)),
            chosen == null ? "Needs your choice" : RowOptions.FirstOrDefault(option => option.Value == chosen.Action)?.Label ?? chosen.Action.ToString());
    }).ToArray();
    public IReadOnlyList<ImportWorkbookDomainRow> WorkbookDomainRows => (workbookSnapshot?.Pfam ?? new()).Select(hint =>
        new ImportWorkbookDomainRow(hint.FamilyKey, string.Join("; ", hint.Domains.Select(domain => domain.Accession + (domain.Name.Length > 0 ? " " + domain.Name : ""))),
            "Row " + hint.RowNumber + ", " + hint.Column)).ToArray();
    public IReadOnlyList<ImportWorkbookSection> WorkbookSections => workbookReview?.Sections
        .Where(section => section.Kind != WorkbookImportSectionKind.Colors
            && (section.Kind != WorkbookImportSectionKind.Decisions || HasCustomDecisions)).Select(section =>
    {
        int required = section.Items.Count(item => item.Required && item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace);
        int optional = section.Items.Count(item => !item.Required && item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace);
        string review = required > 0 ? required + " need your choice" : optional > 0 ? optional + " optional to check" : "no choices needed";
        if (required > 0 && optional > 0) review += "; " + optional + " optional to check";
        return new ImportWorkbookSection(SectionKey(section.Kind), SectionLabel(section.Kind),
            (section.Kind == WorkbookImportSectionKind.Sheets ? WorkbookSheets.Count + " sheets read; " : section.Items.Count + " findings; ")
                + section.Items.Count(item => item.State == WorkbookImportItemState.Proposed) + " proposed; " + review, required > 0,
            "Reviews " + SectionLabel(section.Kind).ToLowerInvariant() + " in the draft. Nothing changes in the project until Accept and import succeeds.");
    }).ToArray()
        ?? Array.Empty<ImportWorkbookSection>();

    private static string WorkbookProblemGuidance(string reason, WorkbookImportPage page)
    {
        string text = PlainWorkbookReason(reason).Trim();
        const string diagnostic = "Matched program words: ";
        if (text.StartsWith(diagnostic, StringComparison.Ordinal) && text.IndexOf('.', diagnostic.Length) is int stop && stop >= 0)
            text = text[(stop + 1)..].TrimStart();
        text = text.Replace("No program action word matched.", "No action is clear for this label.", StringComparison.Ordinal)
            .Replace("Bracketed qualifier:", "Extra condition:", StringComparison.Ordinal)
            .Replace("The text is optional, conditional or negated; review its action.", "This label is optional, conditional or says not to use an action.", StringComparison.Ordinal)
            .Replace("The text is optional or conditional; review its action.", "This label is optional or conditional.", StringComparison.Ordinal);
        const string instruction = "Review its action.";
        if (text.EndsWith(instruction, StringComparison.Ordinal)) text = text[..^instruction.Length].TrimEnd();
        return text + (text.Length > 0 ? " " : "") + (page == WorkbookImportPage.Decisions ? "Choose its action in " : "Review ") + PageLabel(page) + ".";
    }

    private static string StateLabel(WorkbookImportItemState state) => state switch
    { WorkbookImportItemState.Found => "Found", WorkbookImportItemState.Proposed => "Proposed", WorkbookImportItemState.Uncertain => "Uncertain", _ => "Could not place" };
    private static string PageKey(WorkbookImportPage page) => page switch
    { WorkbookImportPage.Columns => "columns", WorkbookImportPage.Colors => "colours", WorkbookImportPage.Decisions => "decisions",
        WorkbookImportPage.FamilyRows => "rows", WorkbookImportPage.EvidenceMatches => "matches", WorkbookImportPage.Subclasses => "subclasses",
        WorkbookImportPage.Notes => "notes", _ => "sheets" };
    private static string PageLabel(WorkbookImportPage page) => page switch
    { WorkbookImportPage.FileAndSheets => "Workbook sheets", WorkbookImportPage.Columns => "Family list and columns",
        WorkbookImportPage.Colors => "Source categories", WorkbookImportPage.Decisions => "Decision labels", WorkbookImportPage.FamilyRows => "Family rows",
        WorkbookImportPage.EvidenceMatches => "Pfam and evidence matches", WorkbookImportPage.Subclasses => "Subclass proposals", _ => "Source notes" };
    private static string SectionKey(WorkbookImportSectionKind kind) => kind switch
    { WorkbookImportSectionKind.Columns => "columns", WorkbookImportSectionKind.Colors => "colours", WorkbookImportSectionKind.Decisions => "decisions",
        WorkbookImportSectionKind.FamilyRows => "rows", WorkbookImportSectionKind.Pfam => "matches", WorkbookImportSectionKind.Subclasses => "subclasses",
        WorkbookImportSectionKind.Notes => "notes", _ => "sheets" };
    private static string SectionLabel(WorkbookImportSectionKind kind) => kind switch
    { WorkbookImportSectionKind.Sheets => "Workbook sheets", WorkbookImportSectionKind.Columns => "Family list and columns",
        WorkbookImportSectionKind.Colors => "Source categories", WorkbookImportSectionKind.Decisions => "Decision labels",
        WorkbookImportSectionKind.FamilyRows => "Family rows", WorkbookImportSectionKind.Pfam => "Pfam and evidence matches",
        WorkbookImportSectionKind.Subclasses => "Subclass proposals", _ => "Source notes" };

    private void ResetWorkbookReview()
    {
        workbookReview = null; workbookAcceptance = null; reviewedWorkbook = null; reviewedEvidence = null;
        subclassSourceKey = workbookReviewError = reviewInputKey = ""; evidencePagePrepared = false;
    }

    private void RefreshWorkbookReview()
    {
        if (!updating && !updatingSubclassDraft && WorkbookReviewEnabled && workbookSnapshot != null && !Imported)
        {
            try
            {
                reviewedWorkbook = CaptureWorkbookDraft();
                workbookMatching ??= matcher is FamilyListStoredEvidenceMatches stored ? stored.WorkbookSnapshot : new PanelEvidenceMatching(transaction.Project);
                var selected = subclassSourceKey.Length == 0 ? null : ImportSubclassChoice.Capture(SubclassChoices);
                var summary = WorkbookImportSummaries.Build(reviewedWorkbook, workbookMatching, selected, reviewedEvidence);
                if (ApplyWorkbookColumnDefaults(summary.FamilyChoices))
                {
                    updatingSubclassDraft = true;
                    try { Recheck(); } finally { updatingSubclassDraft = false; }
                    reviewedWorkbook = CaptureWorkbookDraft();
                    summary = WorkbookImportSummaries.Build(reviewedWorkbook, workbookMatching, selected, reviewedEvidence);
                }
                var proposals = summary.SubclassProposals;
                string sourceKey = JsonSerializer.Serialize(proposals);
                if (sourceKey != subclassSourceKey)
                {
                    var previous = selected ?? summary.DraftSubclassSelections;
                    updatingSubclassDraft = true;
                    try { SetSubclassProposals(proposals.Select(proposal => ImportSubclassChoice.FromCore(proposal,
                        previous.FirstOrDefault(choice => choice.ProposalId == proposal.ProposalId)))); }
                    finally { updatingSubclassDraft = false; }
                    subclassSourceKey = sourceKey;
                    summary = WorkbookImportSummaries.Build(reviewedWorkbook, workbookMatching, ImportSubclassChoice.Capture(SubclassChoices), reviewedEvidence);
                }
                string inputKey = JsonSerializer.Serialize(new { Choices = reviewedWorkbook.FamilyProposal.Choices, reviewedWorkbook.Decisions,
                    Subclasses = summary.ReviewedSubclassSelections, Evidence = summary.ProposedEvidenceSelections });
                if (inputKey != reviewInputKey) workbookAcceptance = null;
                reviewInputKey = inputKey; workbookReview = summary; workbookReviewError = "";
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            { workbookReviewError = error.Message; }
        }
        foreach (string property in new[] { nameof(WorkbookSections), nameof(WorkbookFamilies), nameof(WorkbookFamilyKeys), nameof(WorkbookFindings), nameof(SubclassReviewFindings), nameof(HasSubclassReviewFindings),
            nameof(WorkbookReviewProblems), nameof(WorkbookHasReviewProblems), nameof(WorkbookReviewStatus), nameof(WorkbookReviewDetails), nameof(CanAcceptWorkbook), nameof(WorkbookColumns), nameof(WorkbookLegend), nameof(WorkbookDecisionRows),
            nameof(WorkbookSpecialRows), nameof(WorkbookDomainRows), nameof(CanReturnToWorkbook), nameof(ReviewingWorkbook), nameof(ReviewingSubclasses), nameof(ShowingWizardPages) }) Changed(property);
    }

    public void ReturnToWorkbookReview()
    {
        if (!WorkbookReviewEnabled || !HasWorkbookMap || IsBusy || Imported) return;
        if (evidencePagePrepared) reviewedEvidence = MatchFamilies.ToDictionary(family => family.Key,
            family => (IReadOnlyList<PanelEvidenceChoice>)family.Suggestions.Where(choice => choice.Use)
                .Select(choice => PanelEvidenceMatching.ChoiceFromKey(choice.Key) with { Value = choice.Text }).ToArray(), StringComparer.Ordinal);
        reviewingWorkbook = true; reviewingSubclasses = false;
        Status = "Nothing has changed yet. Accept and import keeps one Undo step.";
        Refresh(); RefreshReviewHeading();
    }

    private bool ApplyWorkbookColumnDefaults(FamilyPanelImport proposed)
    {
        bool changed = false; updating = true;
        try
        {
            foreach (var column in Columns.Where(c => !c.HasChoice && !reviewedWorkbookColumns.Contains(c.Role)))
                if (proposed.ColumnHeaders.GetValueOrDefault(column.Role, "") is { Length: > 0 } header)
                { column.Header = header; changed = true; }
        }
        finally { updating = false; }
        return changed;
    }

    private void RefreshReviewHeading()
    { Changed(nameof(Title)); Changed(nameof(Explanation)); Changed(nameof(StepLabel)); Changed(nameof(ForwardLabel)); Changed(nameof(CanNext)); }

    public async Task ReviewWorkbookSectionAsync(string section)
    {
        if (IsBusy || Imported) return;
        if (section == "colours" || section == "decisions" && !HasCustomDecisions) return;
        if (section == "matches")
        {
            RefreshWorkbookReview();
            if (workbookReview == null || reviewedWorkbook?.FamilyTable == null)
            { Status = "Resolve family choices before reviewing evidence matches."; return; }
            var preview = importer.Check(reviewedWorkbook.FamilyTable, workbookReview.FamilyChoices, workbookReview.ProposedDecisions, PanelName);
            if (preview.Panel == null) { Status = "Resolve family choices before reviewing evidence matches."; return; }
            var preparation = PrepareMatchesAsync(preview.Panel); activeRead = preparation;
            if (!await preparation) return;
            evidencePagePrepared = true;
        }
        reviewingWorkbook = false; reviewingSubclasses = section == "subclasses";
        GoTo(section switch { "columns" or "colours" or "decisions" => 2, "rows" => 3, "matches" => 4, "subclasses" => 5, _ => 1 });
        if (section == "decisions") ReviewingDecisions = true;
        WorkbookViewRequested?.Invoke(section);
        Refresh(); RefreshReviewHeading();
    }
    public event Action<string>? WorkbookViewRequested;

    public void SetSubclassProposals(IEnumerable<ImportSubclassChoice> proposals)
    {
        if (Imported) return;
        SubclassChoices.Clear();
        foreach (var proposal in proposals)
        {
            proposal.Confirmed = false;
            proposal.PropertyChanged += (_, _) => Refresh();
            foreach (var rule in proposal.Rules) rule.PropertyChanged += (_, _) => Refresh();
            SubclassChoices.Add(proposal);
        }
        SelectedSubclass = SubclassChoices.FirstOrDefault();
    }

    private async Task<bool> AcceptWorkbookAsync(Func<string, bool>? confirmBroad)
    {
        RefreshWorkbookReview();
        if (!CanAcceptWorkbook || workbookReview == null)
        { Status = WorkbookReviewProblems.FirstOrDefault() ?? "Review the workbook choices before importing."; return false; }
        var summary = workbookReview;
        var subclasses = ImportSubclassChoice.Capture(SubclassChoices);
        var evidence = summary.ProposedEvidenceSelections;
        string warnings = string.Join("\n\n", summary.Sections.SelectMany(section => section.Items)
            .Where(item => item.State == WorkbookImportItemState.Proposed && item.ConfirmationWarning.Length > 0)
            .Select(item => item.ConfirmationWarning).Distinct(StringComparer.Ordinal));
        if (warnings.Length > 0 && confirmBroad?.Invoke(warnings) != true)
        { Status = "Broad matching was not confirmed. The project is unchanged."; return false; }
        if (!ReferenceEquals(workbookReview, summary))
        { Status = "The proposal changed. Review it again before accepting."; return false; }
        workbookAcceptance = WorkbookImportSummaries.AcceptAll(summary, importer, subclasses, evidence, PanelName, broadPfamConfirmed: true);
        if (!workbookAcceptance.CanImport || workbookAcceptance.Panel == null)
        { RefreshWorkbookReview(); Status = WorkbookReviewProblems.FirstOrDefault() ?? "Review the remaining workbook choices."; return false; }
        var commit = CommitAsync(workbookAcceptance.Panel); activeRead = commit;
        bool imported = await commit;
        if (imported)
        {
            foreach (var decision in Decisions) decision.Confirmed = true;
            foreach (var subclass in SubclassChoices.Where(choice => choice.Use)) subclass.Confirmed = true;
            reviewingWorkbook = reviewingSubclasses = false; Refresh(); RefreshReviewHeading();
        }
        return imported;
    }
}
