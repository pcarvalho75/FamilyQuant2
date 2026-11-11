using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class FamilyListImportViewModel : ScreenViewModel, IDisposable
{
    public static readonly string[] StepNames = { "Welcome", "File and sheet", "Columns", "Special rows", "Evidence matches", "Summary", "Done" };
    private static readonly string[] Titles = { "Import family list", "Choose your spreadsheet", "Choose what each column means",
        "Review special rows", "Match your list to stored evidence", "Review your family list", "Your family list is imported" };
    private static readonly string[] Explanations = {
        "Bring a spreadsheet of abbreviations and family names; review every choice before import.",
        "Choose a file and sheet, then check the preview against your spreadsheet.",
        "Check the suggested columns; an example from your sheet appears below each choice.",
        "Resolve each row marked Needs a choice before continuing.",
        "Confirm or skip the suggestions for each family; nothing is matched for you.",
        "Check what will be created; nothing has changed yet.",
        FamilyListImportHelp.DoneNext };
    private readonly IPanelSheetPreviewReader reader;
    private readonly IFamilyPanelSpreadsheetImporter importer;
    private readonly FamilyListImportTransaction transaction;
    private readonly IFamilyListImportMatches? matcher;
    private readonly Func<FamilyClusterFile, OperationContext, FamilyClusterFile>? copyProjectOverride;
    private readonly IFamilyListWorkbookSource? workbookSource;
    private readonly bool openWorkbookReview;
    private CancellationTokenSource? operation;
    private Task activeRead = Task.CompletedTask;
    private int readGeneration, page, headerRow = 1;
    private bool busy, updating, keepOtherSheets, reviewingDecisions, quantifyAllConfirmed;
    private string path = "", sheet = "", panelName = "", status = "Nothing has changed yet.";
    private string headerRowText = "1";
    private FamilyPanelImport choices = new() { ImportVersion = PanelImportRules.Current };
    private PanelSpreadsheetTable? table;
    private PanelImportCheck check = new();
    private string matchesFingerprint = "";
    private FamilyPanel? committedPanel;
    public PanelSheetPreview? Preview { get; private set; }
    public ObservableCollection<ImportColumnChoice> Columns { get; } = new();
    public IReadOnlyList<ImportColumnChoice> MainColumns => Columns.Where(c => c.Role != PanelImportFields.ColorCategory).ToArray();
    public ImportColumnChoice? ColorColumn => Columns.FirstOrDefault(c => c.Role == PanelImportFields.ColorCategory);
    public bool ReviewingDecisions { get => reviewingDecisions; set { if (Set(ref reviewingDecisions, value)) { Changed(nameof(Title)); Changed(nameof(Explanation));
        if (page == 2) Status = ColumnsStatus;
        Changed(nameof(DisabledReason)); Changed(nameof(ForwardToolTip)); } } }
    public ObservableCollection<ImportDecisionChoice> Decisions { get; } = new();
    public ObservableCollection<ImportColorChoice> SourceColorChoices { get; } = new();
    public ObservableCollection<ImportSpecialRow> SpecialRows { get; } = new();
    public ObservableCollection<ImportMatchFamily> MatchFamilies { get; } = new();
    public IReadOnlyList<string> Sheets => Preview?.SheetNames ?? (IReadOnlyList<string>)Array.Empty<string>();
    public IReadOnlyList<string> HiddenSheets => Preview?.HiddenSheetNames ?? (IReadOnlyList<string>)Array.Empty<string>();
    public IReadOnlyList<PanelImportProblem> Problems => check.Problems;
    public FamilyPanel? DraftPanel => check.Panel;
    public FamilyPanel? ImportedPanel => committedPanel;
    public bool Imported => transaction.Imported;
    public string DecisionHint { get; private set; } = "";
    public string DecisionHintReason { get; private set; } = "";
    public bool HasDecisionHint => DecisionHint.Length > 0 && Columns.FirstOrDefault(c => c.Role == PanelImportFields.Decision)?.HasChoice != true;
    public string DecisionHintQuestion => "Could " + DecisionHint + " hold the family actions?";
    public bool CanUndo => transaction.CanUndo;
    public string PreviousPanel { get; }
    public string WorkbookPath => path;
    public string WorkbookName => path.Length == 0 ? "Drop an Excel workbook here" : Path.GetFileName(path);
    public string Sheet => sheet;
    public int HeaderRow { get => headerRow; set => Set(ref headerRow, value); }
    public string HeaderRowText { get => headerRowText; set { if (Set(ref headerRowText, value ?? "")) Refresh(); } }
    public string PanelName { get => panelName; set { if (Set(ref panelName, value ?? "") && !updating) Recheck(); } }
    public bool KeepOtherSheets { get => keepOtherSheets; set => Set(ref keepOtherSheets, value); }
    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) Refresh(); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public int Page => page;
    public bool CanEdit => !IsBusy && !Imported;
    private string StepName(int index) => index == 1 && HasWorkbookMap ? "Workbook sheets" : StepNames[index];
    public IReadOnlyList<ImportWizardStep> Steps => StepNames.Select((name, index) => new ImportWizardStep(index,
        (index + 1).ToString(CultureInfo.CurrentCulture) + "   " + StepName(index), index < page, index == page,
        !IsBusy && index <= page && (!Imported || index >= 5))).ToArray();
    public string Title => reviewingWorkbook ? "Review your workbook" : reviewingSubclasses ? "Review subclass proposals"
        : page == 1 && HasWorkbookMap ? "Workbook sheets" : page == 2 && ReviewingDecisions ? "Review spreadsheet decisions" : Titles[page];
    public string Explanation => reviewingWorkbook ? "Review what was found, change any section, then accept the proposal as one import."
        : reviewingSubclasses ? "Check each proposed family, subclass and domain rule before keeping your choices."
        : page == 2 && ReviewingDecisions ? FamilyListImportHelp.DecisionsSummary
        : page == 1 && HasWorkbookMap ? "Check each sheet's role under Use as, and take another sheet as the family list if needed."
        : Explanations[page];
    public string StepLabel => reviewingWorkbook ? "Workbook proposal" : reviewingSubclasses ? "Subclass review"
        : "Step " + (page + 1).ToString(CultureInfo.CurrentCulture) + " of 7, " + StepName(page);
    public string ForwardLabel => reviewingWorkbook ? "Accept and import" : reviewingSubclasses ? "Keep subclass choices"
        : WorkbookReviewEnabled && page == 5 && !Imported ? "Review workbook" : page == 5 && !Imported ? "Import" : page == 6 ? "Family checklist" : "Next";
    public string CloseLabel => Imported ? "Close" : "Cancel";
    public bool CanBack => !IsBusy && page > 0 && !reviewingWorkbook;
    public string BackToolTip => reviewingWorkbook ? "Use a section's Review button to change its proposal; Cancel leaves the project unchanged."
        : reviewingSubclasses ? "Returns to Review your workbook and keeps your subclass draft choices; nothing changes in the project until Accept and import."
        : !CanBack ? IsBusy ? "Wait for the current step to finish before returning; Cancel stops it without importing."
        : "This is the first step; choose Next to continue, or Cancel to leave without changes."
        : "Returns to the previous step and keeps your choices; going Back does not change the project or repeat an import.";
    public string CloseToolTip => IsBusy ? "Cancels the current step and closes after it stops; no unfinished import is applied."
        : Imported ? "Closes the wizard and keeps the imported list; File > Save stores it with the project, and Undo is still available in the main window."
        : "Closes the wizard and discards its draft choices; the project and workbook are unchanged.";
    public string ForwardToolTip => reviewingWorkbook ? CanAcceptWorkbook
        ? "Accepts the exact workbook proposal shown and imports it as one project edit. Source and project are checked again; Undo restores the previous panel, and File > Save stores the change."
        : (WorkbookHasReviewProblems ? WorkbookReviewStatus : "Review the remaining workbook choices.") + " Accept and import is unavailable until required choices are resolved."
        : reviewingSubclasses ? "Keeps these selections in the workbook draft and returns to its review page. Rules remain unconfirmed until your explicit acceptance."
        : !CanNext ? DisabledReason + " Nothing is imported while this action is unavailable."
        : page == 5 && !Imported ? "Rechecks the workbook and project, then imports the reviewed list as one project edit; Undo restores the previous list, and File > Save stores the change."
        : page == 6 ? "Closes the wizard and opens Family checklist for separate protein review; no grouping or quantitation runs automatically."
        : Imported ? "Shows the imported list's Done page without importing again or adding an Undo step."
        : page == 2 && !quantifyAllConfirmed && Columns.FirstOrDefault(c => c.Role == PanelImportFields.Decision)?.HasChoice != true
            ? "Asks you to confirm Quantify for every family because no Decision column is chosen; No keeps you on Columns. Acceptance is remembered for this source until its Decision mapping changes, with no project edit until Import."
        : "Continues to the next review step with your choices kept; the project and workbook stay unchanged until Import.";
    public bool CanNext => reviewingWorkbook ? CanAcceptWorkbook : reviewingSubclasses ? !IsBusy && !Imported : !IsBusy && (Imported || page switch
    {
        0 => true,
        1 => !NeedsFamilySheetChoice && !WorkbookHasBlockingProblems && table != null && int.TryParse(HeaderRowText, out int editedHeader) && table.HeaderRowNumber == editedHeader && Preview != null
            && headerRow > 0 && !Preview.Problems.Any(p => p.Severity == PanelImportSeverity.Blocking),
        2 => MappingValid && Decisions.All(d => d.Confirmed && d.Decision != null),
        3 => check.CanImport && SpecialRows.All(r => !r.Blocking),
        4 => check.CanImport && MatchFamilies.All(f => f.Decided),
        5 => check.CanImport && PanelName.Trim().Length > 0,
        _ => false,
    });
    public static IReadOnlyList<ImportDecisionOption> DecisionOptions { get; } = new[] {
        new ImportDecisionOption("Quantify", FamilyPanelDecision.Quantify), new("Report under others", FamilyPanelDecision.ReportUnderOthers),
        new("Not a family", FamilyPanelDecision.NotAFamily), new("Merge into family", FamilyPanelDecision.MergeIntoAnotherFamily) };
    public static IReadOnlyList<ImportRowOption> RowOptions { get; } = new[] {
        new ImportRowOption("Keep or rename this row", FamilyPanelRowAction.Import), new("Skip this row", FamilyPanelRowAction.Skip),
        new("Merge into family", FamilyPanelRowAction.MergeInto), new("One family per name", FamilyPanelRowAction.Split),
        new("One group reported under others", FamilyPanelRowAction.ReportUnderOthers) };

    public FamilyListImportViewModel(FamilyListImportTransaction transaction, IPanelSheetPreviewReader? reader = null,
        IFamilyPanelSpreadsheetImporter? importer = null, IFamilyListImportMatches? matcher = null,
        Func<FamilyClusterFile, OperationContext, FamilyClusterFile>? copyProject = null,
        IFamilyListWorkbookSource? workbookSource = null, bool openWorkbookReview = false)
    {
        this.transaction = transaction; this.reader = reader ?? new PanelSpreadsheetReader();
        this.importer = importer ?? new FamilyPanelSpreadsheetImporter(); this.matcher = matcher ?? new FamilyListStoredEvidenceMatches(transaction.Project);
        this.copyProjectOverride = copyProject;
        this.workbookSource = workbookSource;
        this.openWorkbookReview = openWorkbookReview;
        PreviousPanel = transaction.PreviousPanel;
    }

    public Task LoadAsync(string workbookPath, string sheetName = "", int? selectedHeaderRow = null)
    {
        if (!string.Equals(path, workbookPath, StringComparison.OrdinalIgnoreCase)) workbookRoles.Clear();
        operation?.Cancel(); operation?.Dispose(); operation = new CancellationTokenSource();
        int generation = ++readGeneration;
        activeRead = LoadCoreAsync(workbookPath, sheetName, selectedHeaderRow, operation.Token, generation);
        return activeRead;
    }

    private async Task LoadCoreAsync(string workbookPath, string sheetName, int? selectedHeaderRow, CancellationToken token, int generation)
    {
        IsBusy = true; Status = workbookSource == null ? "Reading spreadsheet..." : "Reading workbook...";
        try
        {
            var roles = new Dictionary<string, PanelWorkbookSheetRole>(workbookRoles, StringComparer.Ordinal);
            var workbook = workbookSource == null ? null : await Task.Run(() => workbookSource.Read(workbookPath, roles, sheetName, token), token);
            token.ThrowIfCancellationRequested();
            string selectedSheet = sheetName.Length > 0 ? sheetName : workbook?.FamilySheet ?? "";
            var suppliedSheet = workbook?.Workbook.Sheets.FirstOrDefault(s => s.SheetName == selectedSheet);
            var preview = suppliedSheet?.Preview ?? await Task.Run(() => reader.ReadPreview(workbookPath, selectedSheet), token);
            token.ThrowIfCancellationRequested();
            int selectedRow = selectedHeaderRow ?? preview.ProposedHeaderRow;
            PanelSpreadsheetTable? loaded = selectedRow > 0 && !preview.Problems.Any(p => p.Severity == PanelImportSeverity.Blocking)
                ? suppliedSheet?.Table is { } supplied && supplied.HeaderRowNumber == selectedRow ? supplied
                    : await Task.Run(() => reader.ReadTable(workbookPath, preview.SheetName, selectedRow), token) : null;
            token.ThrowIfCancellationRequested();
            if (generation != readGeneration) return;
            if (loaded != null && preview.SourceSha256 != loaded.SourceSha256)
            { Status = "The workbook changed while it was being read. Choose the file again."; return; }
            if (workbook != null && workbook.Workbook.SourceSha256 != preview.SourceSha256)
            { Status = "The workbook changed while its sheets were being read. Choose the file again."; return; }
            updating = true;
            SetWorkbook(workbook);
            path = workbookPath; sheet = preview.SheetName; Preview = preview; table = loaded; check = new(); HeaderRow = selectedRow;
            HeaderRowText = selectedRow.ToString(CultureInfo.CurrentCulture);
            var proposal = loaded == null ? new PanelImportProposal { Choices = new FamilyPanelImport { ImportVersion = PanelImportRules.Current } }
                : workbook != null && loaded.SheetName == workbook.FamilySheet ? workbook.FamilyProposal : importer.Propose(loaded);
            choices = System.Text.Json.JsonSerializer.Deserialize<FamilyPanelImport>(System.Text.Json.JsonSerializer.Serialize(proposal.Choices))!; DecisionHint = proposal.DecisionHint; DecisionHintReason = proposal.DecisionHintReason; quantifyAllConfirmed = false;
            choices.RowChoices = new(); choices.ColorLabels = null;
            PanelName = Path.GetFileNameWithoutExtension(workbookPath);
            Columns.Clear(); Decisions.Clear(); SourceColorChoices.Clear(); MatchFamilies.Clear(); matchesFingerprint = ""; ReviewingDecisions = false;
            if (loaded != null) BuildColumns(loaded);
            updating = false;
            GoTo(1);
            Status = preview.Problems.FirstOrDefault(p => p.Severity == PanelImportSeverity.Blocking)?.Message
                ?? (selectedRow <= 0 ? "Choose the row containing the column names." : "Check the sheet and header row before continuing.");
            Recheck(); Changed(nameof(Preview)); Changed(nameof(Sheets)); Changed(nameof(HiddenSheets));
            Changed(nameof(HasSourceFills));
            Changed(nameof(WorkbookPath)); Changed(nameof(WorkbookName)); Changed(nameof(Sheet));
            if (WorkbookReviewEnabled && workbook != null)
            { reviewingWorkbook = true; Status = "Nothing has changed yet. Accept and import keeps one Undo step."; Refresh(); RefreshReviewHeading(); }
        }
        catch (OperationCanceledException) { if (generation == readGeneration) Status = "Reading cancelled."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { if (generation == readGeneration) { table = null; Status = "This workbook could not be read. " + error.Message; } }
        finally { updating = false; if (generation == readGeneration) IsBusy = false; }
    }

    public async Task CancelReadAsync()
    { if (IsBusy) Status = "Cancelling the current step..."; operation?.Cancel(); try { await activeRead; } catch (OperationCanceledException) { } }

    public void Back()
    {
        if (reviewingSubclasses) { ReturnToWorkbookReview(); return; }
        if (page == 2 && ReviewingDecisions) ReviewingDecisions = false;
        else if (CanBack) GoTo(page - 1);
    }

    public void Revisit(int target)
    { if (ShowingWizardPages && !IsBusy && target >= 0 && target < page && (!Imported || target == 5)) GoTo(target); }

    public async Task<bool> NextAsync(Func<string, bool>? confirmQuantifyAll = null)
    {
        if (reviewingWorkbook) return await AcceptWorkbookAsync(confirmQuantifyAll);
        if (reviewingSubclasses) { ReturnToWorkbookReview(); return true; }
        if (!CanNext) { Status = DisabledReason; return false; }
        if (page == 2 && !Imported && !quantifyAllConfirmed && Columns.FirstOrDefault(c => c.Role == PanelImportFields.Decision)?.HasChoice != true)
        {
            if (confirmQuantifyAll?.Invoke("No Decision column is chosen. Quantify every family for this import?") != true)
            { Status = "Choose a Decision column, or confirm Quantify for every family to continue."; return false; }
            quantifyAllConfirmed = true;
        }
        if (Imported) { if (page < 6) GoTo(page + 1); return page == 6; }
        if (page == 5) { if (WorkbookReviewEnabled) { ReturnToWorkbookReview(); return true; } var commit = CommitAsync(); activeRead = commit; return await commit; }
        if (page == 3)
        {
            var preparation = PrepareMatchesAsync(); activeRead = preparation;
            if (!await preparation) return false;
            GoTo(PfamQuestions.Any(family => !family.Decided) ? 4 : 5); return true;
        }
        GoTo(page + 1); return true;
    }

    private const string ColumnChoicesKept = "Your column choices are kept until you import.";
    private const string ConfirmDecisionActions = "Check the proposed actions and press Confirm these actions to continue.";
    private bool HasUnconfirmedColumnDecisions => MappingValid && Decisions.Any(decision => !decision.Confirmed);
    private string DecisionReviewInstruction => "Press Review decision values and confirm each action to continue"
        + (CanReturnToWorkbook ? ", or press Back to workbook review." : ".");
    private string ColumnsStatus => ReviewingDecisions ? ConfirmDecisionActions
        : HasUnconfirmedColumnDecisions ? DecisionReviewInstruction : ColumnChoicesKept;

    public string DisabledReason => IsBusy ? "Wait for the current step to finish, or cancel."
        : page == 1 ? "Choose a readable file, sheet and header row."
        : page == 2 ? HasUnconfirmedColumnDecisions ? ReviewingDecisions ? ConfirmDecisionActions : DecisionReviewInstruction
            : "Choose the required columns and confirm each decision value."
        : page == 3 ? SpecialRows.FirstOrDefault(r => r.Blocking)?.Message ?? check.Problems.FirstOrDefault(p => p.Severity == PanelImportSeverity.Blocking)?.Message ?? "Review the special rows."
        : page == 4 ? "Confirm or skip each family's evidence matches."
        : page == 5 ? "Name the panel and resolve every remaining row choice." : "Complete this page before continuing.";

    private void GoTo(int next)
    {
        page = Math.Clamp(next, 0, 6); if (page != 2) ReviewingDecisions = false;
        Status = page switch
        {
            0 => "Nothing has changed yet.",
            1 => "Check the sheet and header row before continuing.",
            2 => ColumnsStatus,
            3 => SpecialSummary + ".",
            4 => MatchingChoiceStatus,
            5 => Imported ? "This list is already imported." : WorkbookReviewEnabled ? "Nothing has changed yet. Accept and import keeps one Undo step." : "Nothing has changed yet. Import keeps one Undo step.",
            _ => "Family list imported. File > Save keeps it with the project."
        };
        Refresh(); Changed(nameof(Title)); Changed(nameof(Explanation)); Changed(nameof(StepLabel));
    }

    private void Refresh()
    {
        if (page == 2 && !ReviewingDecisions && !reviewingWorkbook && !reviewingSubclasses && !IsBusy && !Imported
            && (status == ColumnChoicesKept || status == DecisionReviewInstruction)) Status = ColumnsStatus;
        Changed(nameof(Page)); Changed(nameof(CanNext)); Changed(nameof(CanBack)); Changed(nameof(ForwardLabel));
        Changed(nameof(CloseLabel)); Changed(nameof(Imported)); Changed(nameof(CanUndo)); Changed(nameof(DisabledReason));
        Changed(nameof(DraftPanel)); Changed(nameof(Problems)); Changed(nameof(Summary)); Changed(nameof(SummaryFamilies)); Changed(nameof(ImportedFamilyCount));
        Changed(nameof(ImportedPanel)); Changed(nameof(CanUseRowChoice)); Changed(nameof(AvailableRowOptions)); Changed(nameof(CanConfirmMatches));
        Changed(nameof(SkipFamilyLabel)); Changed(nameof(SkipAllLabel));
        Changed(nameof(Steps)); Changed(nameof(CanEdit));
        Changed(nameof(BackToolTip)); Changed(nameof(CloseToolTip)); Changed(nameof(ForwardToolTip));
        Changed(nameof(MainColumns)); Changed(nameof(ColorColumn));
        Changed(nameof(CanChooseFamilySheet));
        Changed(nameof(DecisionHint)); Changed(nameof(DecisionHintReason)); Changed(nameof(HasDecisionHint)); Changed(nameof(DecisionHintQuestion));
        RefreshWorkbookReview();
    }

    public void ReportStatus(string message) => Status = message;
    public void Dispose() { operation?.Cancel(); operation?.Dispose(); operation = null; transaction.Dispose(); }
}
