using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> WorkbookWizardCommands() =>
    [new("--test-workbook-wizard", 100, TestCommandPhase.Raw, args => args.Contains("--test-workbook-wizard", StringComparer.Ordinal), (args, _) =>
    {
        WorkbookWizardTests(WorkbookScreenOutput(args));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static string? WorkbookScreenOutput(string[] args)
    {
        int screens = Array.FindIndex(args, value => value.Equals("--screens", StringComparison.OrdinalIgnoreCase));
        if (screens >= 0)
        {
            if (screens + 1 >= args.Length || args[screens + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("--screens requires an output directory.");
            return args[screens + 1];
        }
        return args.Length == 2 ? args[1] : null;
    }

    private sealed class ObservedWorkbookSource : IFamilyListWorkbookSource
    {
        private readonly FamilyListWorkbookCoreSource inner = new();
        internal PanelWorkbookProposal Last = new();
        internal int Reads, Completes;
        internal bool Blocking;
        public PanelWorkbookProposal Read(string path, IReadOnlyDictionary<string, PanelWorkbookSheetRole> roles, string familySheet, CancellationToken cancellation)
        {
            Reads++; Last = inner.Read(path, roles, familySheet, cancellation);
            if (Blocking) Last.Problems.Add(new() { Severity = PanelImportSeverity.Blocking, Kind = PanelImportProblemKind.SheetMissing,
                Message = "Supplied sheet could not be read." });
            return Last;
        }
        public FamilyPanel CompletePanel(FamilyPanel panel, PanelWorkbookProposal workbook)
        { Completes++; return inner.CompletePanel(panel, workbook); }
    }

    private static void WorkbookWizardTests(string? output = null)
    {
        Section("Z16a3 workbook map, explicit roles, legend meanings and retained source notes");
        EnsureApplication();
        string work = Path.Combine(Path.GetTempPath(), "fq-workbook-wizard-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        string path = Path.Combine(work, "workbook.xlsx"); ExampleFamilyList.Write(path); string inputHash = TestFileHash.Upper(path);
        var prior = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (W: 1220, H: 780), (W: 1366, H: 768), (W: 1920, H: 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var project = ManualChoiceFile(); string before = project.ToJsonText(); var curation = new CurationViewModel(project);
                var source = new ObservedWorkbookSource();
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(curation), workbookSource: source);
                var window = new FamilyListImportWindow(model) { Left = -30000, Top = -30000, Width = size.W, Height = size.H,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                string label = theme + " " + size.W;
                try
                {
                    window.Show(); FinishReviewWork(() => model.LoadAsync(path)); PumpDispatcher();
                    Check(label + " every source sheet and its structural role is shown", model.HasWorkbookMap && model.WorkbookSheets.Count == 6
                        && model.WorkbookSheets.Count(r => r.Role == PanelWorkbookSheetRole.FamilyList) == 1
                        && model.WorkbookSheets.Count(r => r.Role == PanelWorkbookSheetRole.Notes) == 3
                        && ((TabControl)window.FindName("WorkbookTabs")).SelectedItem == window.FindName("WorkbookMapTab"));
                    Check(label + " the map names the kept notes without publishing them", model.KeptNotes.Count == source.Last.Notes.Count && model.KeptNotesSummary.Contains(source.Last.Notes[0].SheetName, StringComparison.Ordinal)
                        && source.Completes == 0 && project.ToJsonText() == before);
                    CaptureFamilyListPage(window, output, theme, size.W, size.H, "02-workbook-map");
                    ((TabControl)window.FindName("WorkbookTabs")).SelectedItem = window.FindName("SheetPreviewTab"); PumpDispatcher();
                    CaptureFamilyListPage(window, output, theme, size.W, size.H, "02-source-preview");
                    var previewGrid = (DataGrid)window.FindName("SheetPreviewGrid");
                    double scrollbar = WalkTree(previewGrid).OfType<System.Windows.Controls.Primitives.ScrollBar>()
                        .Where(bar => bar.IsVisible && bar.Orientation == Orientation.Vertical).Select(bar => bar.ActualWidth).DefaultIfEmpty(0).Max();
                    double available = previewGrid.ActualWidth - scrollbar - previewGrid.BorderThickness.Left - previewGrid.BorderThickness.Right;
                    double columnsWidth = previewGrid.Columns.Take(7).Sum(c => c.ActualWidth);
                    Check(label + " source preview keeps its first six columns inside the viewport", columnsWidth <= available + 1,
                        "columns=" + columnsWidth + "; available=" + available + "; scrollbar=" + scrollbar);
                    FinishReviewWork(async () => { await model.NextAsync(); });
                    model.ColorColumn!.Header = model.Columns.Single(c => c.Role == PanelImportFields.FamilyKey).Header;
                    Check(label + " source fills start with the real Core legend names and meanings", model.SourceColorChoices.Count > 0
                        && source.Last.Colors.Any(h => !h.IsUncertain && h.Name.Length > 0)
                        && model.SourceColorChoices.All(c => source.Last.Colors.Any(h => h.Color.Equals(c.Color, StringComparison.OrdinalIgnoreCase)
                            && c.Label == (!h.IsUncertain && h.Name.Length > 0 ? h.Name : c.ColorName) && h.Meaning == c.Meaning)),
                        string.Join("; ", model.SourceColorChoices.Select(c => c.Color + "=" + c.Label + " / " + c.Meaning)));
                    model.Columns.Single(c => c.Role == PanelImportFields.Decision).Header = source.Last.FamilyProposal.DecisionHint; PumpDispatcher();
                    Check(label + " decision meanings are displayed without accepting uncertain mappings", model.Decisions.Count > 0
                        && model.Decisions.All(d => source.Last.Decisions.Any(h => h.SourceValue == d.Source && h.LegendMeaning == d.Meaning))
                        && model.Decisions.Any(d => d.Decision == null) && model.Decisions.All(d => !d.Confirmed) && !model.CanNext
                        && ((DataGridColumn)window.FindName("DecisionMeaningColumn")).Visibility == Visibility.Visible);
                    CaptureFamilyListPage(window, output, theme, size.W, size.H, "03-legend-decisions");
                    var decisionGrid = (DataGrid)window.FindName("DecisionGrid");
                    var longest = model.Decisions.OrderByDescending(d => d.Rows.Length).First();
                    var cells = WalkTree(decisionGrid).OfType<FrameworkElement>().Where(e => e.IsVisible && ReferenceEquals(e.DataContext, longest)).ToArray();
                    Check(label + " truncated source row lists remain readable from the actual cell", cells.OfType<TextBlock>()
                        .Any(e => e.Text == longest.Rows && e.ToolTip is string tip && tip.Contains(longest.Rows, StringComparison.Ordinal)));
                    Check(label + " inner action controls retain the complete row explanation", cells.Where(e => e is ComboBox or CheckBox or TextBox)
                        .Any(e => e.ToolTip is string tip && tip.Contains(longest.Reason, StringComparison.Ordinal)));
                    foreach (var decision in model.Decisions.Where(d => d.Decision == null)) decision.Decision = FamilyPanelDecision.Quantify;
                    model.ConfirmDecisions(); FinishReviewWork(async () => { await model.NextAsync(); });
                    for (int attempt = 0; attempt < 100 && model.SpecialRows.FirstOrDefault(r => r.Blocking) is { } row; attempt++)
                    { model.SelectedSpecialRow = row; row.Action = row.SeveralNames ? FamilyPanelRowAction.Split : FamilyPanelRowAction.Import; model.UseRowChoice(); }
                    FinishReviewWork(async () => { await model.NextAsync(); }); model.SkipAllMatches();
                    FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); });
                    Check(label + " accepted import retains source notes once and creates one Undo", model.Imported && source.Completes == 1 && curation.UndoStack.Count == 1
                        && project.FamilyPanel!.SourceNotes.Count == source.Last.Notes.Count);
                    string bundle = Path.Combine(work, label + ".fqproj"); ProjectStore.WriteFqproj(bundle, project, new List<QuantRow>(), new QuantManifest());
                    Check(label + " workbook notes survive a real project save and reopen", ProjectStore.ReadFqproj(bundle).fcf.ToJsonText() == project.ToJsonText());
                    model.UndoImport(); Check(label + " one Undo restores the entire pre-import project", project.ToJsonText() == before);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(prior); }
        var rolesSource = new ObservedWorkbookSource(); using var rolesModel = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())), workbookSource: rolesSource);
        FinishReviewWork(() => rolesModel.LoadAsync(path)); string legendSheet = rolesModel.WorkbookSheets.Single(r => r.Role == PanelWorkbookSheetRole.ColorLegend).Name;
        FinishReviewWork(() => rolesModel.ChangeWorkbookRoleAsync(legendSheet, PanelWorkbookSheetRole.Notes));
        Check("an explicit sheet-role change is rederived by the real Core provider", rolesSource.Reads == 2 && rolesModel.WorkbookSheets.Single(s => s.Name == legendSheet).Role == PanelWorkbookSheetRole.Notes);
        rolesSource.Blocking = true; FinishReviewWork(() => rolesModel.LoadAsync(path));
        Check("blocking workbook problems stay visible and prevent Next", rolesModel.WorkbookProblems.Any(p => p.Message == "Supplied sheet could not be read.") && !rolesModel.CanNext);
        Check("whole-workbook UI tests never change source bytes", TestFileHash.Upper(path) == inputHash);
    }
}
