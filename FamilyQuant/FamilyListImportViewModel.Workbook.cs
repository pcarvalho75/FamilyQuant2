using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class FamilyListImportViewModel
{
    private readonly Dictionary<string, PanelWorkbookSheetRole> workbookRoles = new(StringComparer.Ordinal);
    private PanelWorkbookProposal? workbookSnapshot;
    private bool updatingWorkbook;
    public ObservableCollection<ImportWorkbookSheet> WorkbookSheets { get; } = new();
    public bool HasWorkbookMap => workbookSnapshot != null;
    public bool NeedsFamilySheetChoice => workbookSnapshot?.NeedsFamilySheetChoice == true;
    public bool WorkbookHasBlockingProblems => workbookSnapshot?.Problems.Any(p => p.Severity == PanelImportSeverity.Blocking) == true;
    public bool WorkbookHintsRead => workbookSnapshot != null && workbookSnapshot.ColorsState == PanelWorkbookReadState.Read && workbookSnapshot.DecisionsState == PanelWorkbookReadState.Read;
    public string WorkbookHintStatus => workbookSnapshot == null ? "" : "Legend names: " + (workbookSnapshot.ColorsState == PanelWorkbookReadState.Read ? "read" : "not read")
        + "; decision meanings: " + (workbookSnapshot.DecisionsState == PanelWorkbookReadState.Read ? "read" : "not read");
    public IReadOnlyList<string> WorkbookProblemMessages => WorkbookProblems.Select(DescribeWorkbookProblem).ToArray();
    public IReadOnlyList<PanelImportProblem> WorkbookProblems => workbookSnapshot?.Problems is { } problems ? problems : Array.Empty<PanelImportProblem>();
    public string WorkbookMapSummary => WorkbookSheets.Count + (WorkbookSheets.Count == 1 ? " sheet read" : " sheets read")
        + (NeedsFamilySheetChoice ? "; choose the family list" : "; family list: " + (workbookSnapshot?.FamilySheet ?? ""));
    public string KeptNotesSummary => workbookSnapshot?.Notes.Count > 0
        ? "Notes kept: " + string.Join(", ", workbookSnapshot.Notes.Select(n => n.SheetName)) : "No separate notes sheets";
    public IReadOnlyList<PanelWorkbookSourceNote> KeptNotes => workbookSnapshot?.Notes is { } notes ? notes : Array.Empty<PanelWorkbookSourceNote>();
    private ImportWorkbookSheet? selectedWorkbookSheet;
    public ImportWorkbookSheet? SelectedWorkbookSheet { get => selectedWorkbookSheet; set { if (Set(ref selectedWorkbookSheet, value)) Changed(nameof(CanChooseFamilySheet)); } }
    public bool CanChooseFamilySheet => CanEdit && SelectedWorkbookSheet != null;
    public static IReadOnlyList<ImportSheetRoleOption> SheetRoles { get; } = new[] {
        new ImportSheetRoleOption("Family list", PanelWorkbookSheetRole.FamilyList), new("Colour legend", PanelWorkbookSheetRole.ColorLegend),
        new("Subclass table", PanelWorkbookSheetRole.SubclassTable), new("Notes", PanelWorkbookSheetRole.Notes) };

    private void SetWorkbook(PanelWorkbookProposal? value)
    {
        updatingWorkbook = true;
        workbookSnapshot = value; WorkbookSheets.Clear();
        foreach (var sheet in value?.Workbook.Sheets ?? (IEnumerable<PanelWorkbookSheet>)Array.Empty<PanelWorkbookSheet>())
        {
            var row = new ImportWorkbookSheet { Name = sheet.SheetName, Role = sheet.Role, Reason = sheet.RoleReason,
                Visibility = sheet.IsHidden ? "Hidden sheet" : "Visible sheet", Review = sheet.IsUncertain ? "Review role" : "Proposed",
                IsFamilySheet = sheet.SheetName == value!.FamilySheet };
            row.PropertyChanged += async (_, change) =>
            {
                if (change.PropertyName != nameof(ImportWorkbookSheet.Role) || updatingWorkbook || !CanEdit) return;
                await ChangeWorkbookRoleAsync(row.Name, row.Role);
            };
            WorkbookSheets.Add(row);
        }
        updatingWorkbook = false;
        SelectedWorkbookSheet = WorkbookSheets.FirstOrDefault(r => r.IsFamilySheet) ?? WorkbookSheets.FirstOrDefault();
        Changed(nameof(HasWorkbookMap)); Changed(nameof(NeedsFamilySheetChoice)); Changed(nameof(WorkbookMapSummary));
        Changed(nameof(WorkbookHasBlockingProblems)); Changed(nameof(WorkbookProblems));
        Changed(nameof(WorkbookHintsRead)); Changed(nameof(WorkbookHintStatus)); Changed(nameof(WorkbookProblemMessages));
        Changed(nameof(KeptNotes)); Changed(nameof(KeptNotesSummary));
    }

    public Task ChangeWorkbookRoleAsync(string name, PanelWorkbookSheetRole role)
    {
        if (!CanEdit || !WorkbookSheets.Any(s => s.Name == name)) return Task.CompletedTask;
        updatingWorkbook = true;
        foreach (var row in WorkbookSheets)
        {
            if (row.Name == name) row.Role = role;
            workbookRoles[row.Name] = row.Role;
        }
        updatingWorkbook = false;
        return LoadAsync(path, role == PanelWorkbookSheetRole.FamilyList ? name : "");
    }

    public Task ChooseWorkbookFamilySheetAsync() => SelectedWorkbookSheet == null ? Task.CompletedTask
        : ChangeWorkbookRoleAsync(SelectedWorkbookSheet.Name, PanelWorkbookSheetRole.FamilyList);

    private PanelWorkbookDecisionHint? WorkbookDecision(string source) => workbookSnapshot?.Decisions.FirstOrDefault(d => d.SourceValue == source);
    private PanelWorkbookColorHint? WorkbookColor(string color) => workbookSnapshot?.Colors.FirstOrDefault(c => c.Color.Equals(color, StringComparison.OrdinalIgnoreCase));
    private static string DescribeWorkbookProblem(PanelImportProblem problem) => (problem.SheetName is { Length: > 0 } sheet ? sheet + ": " : "Workbook: ") + problem.Message;
    private string DefaultColorLabel(PanelImportColorUse color) => WorkbookColor(color.Color) is { IsUncertain: false, Name.Length: > 0 } legend
        ? legend.Name : string.IsNullOrWhiteSpace(color.Label) ? color.ColorName : color.Label;
}
