using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
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
        "Open the family checklist to review which proteins belong in each family." };
    private readonly IPanelSheetPreviewReader reader;
    private readonly IFamilyPanelSpreadsheetImporter importer;
    private readonly FamilyListImportTransaction transaction;
    private readonly IFamilyListImportMatches? matcher;
    private CancellationTokenSource? operation;
    private Task activeRead = Task.CompletedTask;
    private int readGeneration, page, headerRow = 1;
    private bool busy, updating, keepOtherSheets, reviewingDecisions;
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
    public bool ReviewingDecisions { get => reviewingDecisions; set { if (Set(ref reviewingDecisions, value)) { Changed(nameof(Title)); Changed(nameof(Explanation)); } } }
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
    public IReadOnlyList<ImportWizardStep> Steps => StepNames.Select((name, index) => new ImportWizardStep(index,
        (index + 1).ToString(CultureInfo.CurrentCulture) + "   " + name, index < page, index == page,
        !IsBusy && index <= page && (!Imported || index >= 5))).ToArray();
    public string Title => page == 2 && ReviewingDecisions ? "Review spreadsheet decisions" : Titles[page];
    public string Explanation => page == 2 && ReviewingDecisions ? FamilyListImportHelp.DecisionsSummary : Explanations[page];
    public string StepLabel => "Step " + (page + 1).ToString(CultureInfo.CurrentCulture) + " of 7, " + StepNames[page];
    public string ForwardLabel => page == 5 && !Imported ? "Import" : page == 6 ? "Family checklist" : "Next";
    public string CloseLabel => Imported ? "Close" : "Cancel";
    public bool CanBack => !IsBusy && page > 0;
    public bool CanNext => !IsBusy && (Imported || page switch
    {
        0 => true,
        1 => table != null && int.TryParse(HeaderRowText, out int editedHeader) && table.HeaderRowNumber == editedHeader && Preview != null
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
        IFamilyPanelSpreadsheetImporter? importer = null, IFamilyListImportMatches? matcher = null)
    {
        this.transaction = transaction; this.reader = reader ?? new PanelSpreadsheetReader();
        this.importer = importer ?? new FamilyPanelSpreadsheetImporter(); this.matcher = matcher ?? new FamilyListStoredEvidenceMatches(transaction.Project);
        PreviousPanel = transaction.PreviousPanel;
    }

    public Task LoadAsync(string workbookPath, string sheetName = "", int? selectedHeaderRow = null)
    {
        operation?.Cancel(); operation?.Dispose(); operation = new CancellationTokenSource();
        int generation = ++readGeneration;
        activeRead = LoadCoreAsync(workbookPath, sheetName, selectedHeaderRow, operation.Token, generation);
        return activeRead;
    }

    private async Task LoadCoreAsync(string workbookPath, string sheetName, int? selectedHeaderRow, CancellationToken token, int generation)
    {
        IsBusy = true; Status = "Reading spreadsheet...";
        try
        {
            var preview = await Task.Run(() => reader.ReadPreview(workbookPath, sheetName), token);
            token.ThrowIfCancellationRequested();
            int selectedRow = selectedHeaderRow ?? preview.ProposedHeaderRow;
            PanelSpreadsheetTable? loaded = selectedRow > 0 && !preview.Problems.Any(p => p.Severity == PanelImportSeverity.Blocking)
                ? await Task.Run(() => reader.ReadTable(workbookPath, preview.SheetName, selectedRow), token) : null;
            token.ThrowIfCancellationRequested();
            if (generation != readGeneration) return;
            if (loaded != null && preview.SourceSha256 != loaded.SourceSha256)
            { Status = "The workbook changed while it was being read. Choose the file again."; return; }
            updating = true;
            path = workbookPath; sheet = preview.SheetName; Preview = preview; table = loaded; check = new(); HeaderRow = selectedRow;
            HeaderRowText = selectedRow.ToString(CultureInfo.CurrentCulture);
            choices = loaded == null ? new FamilyPanelImport { ImportVersion = PanelImportRules.Current }
                : importer.Propose(loaded).Choices;
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
        }
        catch (OperationCanceledException) { if (generation == readGeneration) Status = "Reading cancelled."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { if (generation == readGeneration) { table = null; Status = "This workbook could not be read. " + error.Message; } }
        finally { updating = false; if (generation == readGeneration) IsBusy = false; }
    }

    public async Task CancelReadAsync()
    { operation?.Cancel(); try { await activeRead; } catch (OperationCanceledException) { } }

    public void Back()
    { if (page == 2 && ReviewingDecisions) ReviewingDecisions = false; else if (CanBack) GoTo(page - 1); }

    public void Revisit(int target)
    { if (!IsBusy && target >= 0 && target < page && (!Imported || target == 5)) GoTo(target); }

    public async Task<bool> NextAsync()
    {
        if (!CanNext) { Status = DisabledReason; return false; }
        if (Imported) { if (page < 6) GoTo(page + 1); return page == 6; }
        if (page == 5) { var commit = CommitAsync(); activeRead = commit; return await commit; }
        if (page == 3)
        {
            try { PrepareMatches(); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            { Status = error.Message; return false; }
        }
        GoTo(page + 1); return true;
    }

    public string DisabledReason => IsBusy ? "Wait for the spreadsheet to finish reading, or cancel."
        : page == 1 ? "Choose a readable file, sheet and header row."
        : page == 2 ? "Choose the required columns and confirm each decision value."
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
            2 => "Your column choices are kept until you import.",
            3 => SpecialSummary + ".",
            4 => MatchingChoiceStatus,
            5 => Imported ? "This list is already imported." : "Nothing has changed yet. Import keeps one Undo step.",
            _ => "Family list imported. File > Save keeps it with the project."
        };
        Refresh(); Changed(nameof(Title)); Changed(nameof(Explanation)); Changed(nameof(StepLabel));
    }

    private void Refresh()
    {
        Changed(nameof(Page)); Changed(nameof(CanNext)); Changed(nameof(CanBack)); Changed(nameof(ForwardLabel));
        Changed(nameof(CloseLabel)); Changed(nameof(Imported)); Changed(nameof(CanUndo)); Changed(nameof(DisabledReason));
        Changed(nameof(DraftPanel)); Changed(nameof(Problems)); Changed(nameof(Summary)); Changed(nameof(SummaryFamilies));
        Changed(nameof(ImportedPanel)); Changed(nameof(CanUseRowChoice)); Changed(nameof(AvailableRowOptions)); Changed(nameof(CanConfirmMatches));
        Changed(nameof(SkipFamilyLabel)); Changed(nameof(SkipAllLabel));
        Changed(nameof(Steps)); Changed(nameof(CanEdit));
        Changed(nameof(MainColumns)); Changed(nameof(ColorColumn));
    }

    public void ReportStatus(string message) => Status = message;
    public void Dispose() { operation?.Cancel(); operation?.Dispose(); }
}
