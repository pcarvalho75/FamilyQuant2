using FamilyQuant.Examples;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportWalkLayoutCommands() =>
    [new("--test-import-walk-layout", 21121, TestCommandPhase.Raw, args => args.Length is 1 or 2 && args[0] == "--test-import-walk-layout", (args, _) =>
    {
        ImportWalkLayoutTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "import-walk-" + Guid.NewGuid().ToString("N")));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static TextBlock? ImportWalkCell(DataGrid grid, object row, DataGridColumn column)
    {
        grid.ScrollIntoView(row, column); grid.UpdateLayout(); PumpDispatcher();
        var content = column.GetCellContent(row);
        return content is TextBlock text ? text : content == null ? null : WalkTree(content).OfType<TextBlock>().FirstOrDefault();
    }

    private static bool ImportWalkFullTip(TextBlock? text, string value) => text?.ToolTip is string tip && tip.Contains(value, StringComparison.Ordinal);

    private static void ImportWalkLayoutTests(string output)
    {
        Section("Z12p2f1 native import-page walk regression");
        EnsureApplication(); Directory.CreateDirectory(output);
        var spanMethod = typeof(FamilyListImportWindow).GetMethod("PreviewTitleSpan", BindingFlags.NonPublic | BindingFlags.Static)!;
        var heading = new PanelPreviewRow { RowNumber = 1, Cells = new()
        {
            new() { ColumnLetter = "A", Text = "Merged heading", MergeRange = "A1:C1" },
            new() { ColumnLetter = "B" }, new() { ColumnLetter = "C" }, new() { ColumnLetter = "D", Text = "Separate source value" }
        } };
        Check("Merged heading can span empty neighbours without hiding later source data", Equals(spanMethod.Invoke(null, new object[] { heading, 0 }), 3));
        heading.Cells[1].Text = "Occupied source cell";
        Check("Merged heading never covers an occupied neighbour", Equals(spanMethod.Invoke(null, new object[] { heading, 0 }), 1));
        heading.Cells[1].Text = ""; heading.Cells[0].MergeRange = "A1:C2";
        Check("Vertical merged source data keeps its ordinary preview cells", Equals(spanMethod.Invoke(null, new object[] { heading, 0 }), 1));
        string path = Path.Combine(output, "synthetic-workbook.xlsx"); ExampleFamilyList.Write(path);
        string hash = TestFileHash.Upper(path);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (W: 1366, H: 768), (W: 1120, H: 720) })
            {
                Themes.ThemeManager.Apply(theme);
                var project = WorkbookOverviewProject();
                project.MyExtendedFastaItems[0].Evidence!.Pfam.Add(new() { Accession = "PF00413", Name = "Peptidase_M10" });
                project.MyExtendedFastaItems[0].Evidence!.Pfam.Add(new() { Accession = "PF00045", Name = "Hemopexin" });
                string original = project.ToJsonText();
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(project)),
                    matcher: new FamilyListStoredEvidenceMatches(project), workbookSource: new ObservedWorkbookSource(), openWorkbookReview: true);
                var window = new FamilyListImportWindow(model) { Width = size.W, Height = size.H, WindowStyle = WindowStyle.None,
                    Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
                string label = theme + " " + size.W;
                try
                {
                    window.Show(); FinishReviewWork(() => model.LoadAsync(path)); PumpDispatcher();
                    FinishReviewWork(() => model.ReviewWorkbookSectionAsync("file"));
                    var tabs = (TabControl)window.FindName("WorkbookTabs"); tabs.SelectedIndex = 0; PumpDispatcher(); window.UpdateLayout();
                    var footer = (FrameworkElement)window.FindName("WizardFooter");
                    var help = WalkTree(footer).OfType<Button>().Single(button => Equals(button.Content, "Help"));
                    var returnButton = WalkTree(footer).OfType<Button>().Single(button => Equals(button.Content, "Back to workbook review"));
                    Check(label + " row1 footer actions share a line without a third row", help.IsVisible && returnButton.IsVisible
                        && Math.Abs(help.TranslatePoint(new Point(), footer).Y - returnButton.TranslatePoint(new Point(), footer).Y) < 2 && footer.ActualHeight <= 68);
                    var map = (DataGrid)window.FindName("WorkbookMapGrid");
                    Check(label + " row7 workbook text columns carry full-cell styles", map.Columns.OfType<DataGridTextColumn>().All(column => column.ElementStyle != null));
                    var longest = model.WorkbookSheets.OrderByDescending(sheet => sheet.Reason.Length).First();
                    Check(label + " row7 role reason is complete on hover", ImportWalkFullTip(ImportWalkCell(map, longest,
                        map.Columns.Single(column => Equals(column.Header, "Why this role"))), longest.Reason));
                    Check(label + " row7 choosing the already selected family sheet is disabled", model.SelectedWorkbookSheet?.IsFamilySheet == true && !model.CanChooseFamilySheet);
                    var other = model.WorkbookSheets.First(sheet => !sheet.IsFamilySheet);
                    model.SelectedWorkbookSheet = other;
                    Check(label + " row7 another family-sheet choice remains available", model.CanChooseFamilySheet);
                    model.SelectedWorkbookSheet = model.WorkbookSheets.First(sheet => sheet.IsFamilySheet); PumpDispatcher();
                    Check(label + " row7 kept-notes summary appears once on the map", WalkTree(window).OfType<TextBlock>()
                        .Count(text => text.IsVisible && text.Text == model.KeptNotesSummary) == 1);
                    var notesExpander = WalkTree(window).OfType<Expander>().Single(expander => Equals(expander.Header, "Kept notes preview"));
                    notesExpander.IsExpanded = true; PumpDispatcher();
                    var notesGrid = WalkTree(window).OfType<DataGrid>().Single(grid => AutomationProperties.GetName(grid) == "Notes sheets kept with the family list");
                    Check(label + " row7 kept-note columns carry full-cell styles", notesGrid.Columns.OfType<DataGridTextColumn>().All(column => column.ElementStyle != null));
                    notesExpander.IsExpanded = false;
                    PumpDispatcher(); window.UpdateLayout();
                    double bottomInset = WalkTree(map).OfType<ScrollBar>().Where(bar => bar.IsVisible && bar.Orientation == Orientation.Horizontal)
                        .Select(bar => bar.ActualHeight).DefaultIfEmpty(0).Max() + 1;
                    Check(label + " row1 closed notes leave all six map rows readable", !notesGrid.IsVisible && WalkTree(map).OfType<DataGridRow>()
                        .Count(row => row.IsVisible && row.TranslatePoint(new Point(), map).Y >= 0
                            && row.TranslatePoint(new Point(), map).Y + row.ActualHeight <= map.ActualHeight - bottomInset) >= 6,
                        "notes visible=" + notesGrid.IsVisible + "; map height=" + map.ActualHeight + "; rows="
                        + string.Join(",", WalkTree(map).OfType<DataGridRow>().Select(row => row.TranslatePoint(new Point(), map).Y + "/" + row.ActualHeight)));
                    CaptureFamilyListPage(window, output, theme, size.W, size.H, "01-workbook-map");

                    tabs.SelectedIndex = 1; PumpDispatcher(); window.UpdateLayout();
                    var preview = (DataGrid)window.FindName("SheetPreviewGrid");
                    Check(label + " row8 preview prefers 145 pixels and fits six columns in narrower windows", preview.Columns.Skip(1)
                        .All(column => size.W >= 1366 ? column.Width.Value == 145 : column.Width.Value >= 110 && column.Width.Value < 145)
                        && preview.Columns.Take(7).Sum(column => column.ActualWidth) <= preview.ActualWidth - SystemParameters.VerticalScrollBarWidth);
                    var title = ImportWalkCell(preview, model.Preview!.Rows[0], preview.Columns[1]);
                    if (window.FindName("PreviewMergedTitles") is Canvas titleLayer)
                    {
                        title = WalkTree(titleLayer).OfType<TextBlock>().FirstOrDefault(text => text.Text == model.Preview.Rows[0].Cells[0].Text && text.IsVisible);
                    }
                    Check(label + " row8 merged source heading spans its empty neighbours", title != null && title.ActualWidth > preview.Columns[1].ActualWidth * 2,
                        "title width=" + title?.ActualWidth + "; column=" + preview.Columns[1].ActualWidth);
                    CaptureFamilyListPage(window, output, theme, size.W, size.H, "02-sheet-preview");

                    FinishReviewWork(() => model.ReviewWorkbookSectionAsync("columns")); PumpDispatcher();
                    CaptureFamilyListPage(window, output, theme, size.W, size.H, "03-columns");
                    FinishReviewWork(() => model.ReviewWorkbookSectionAsync("decisions")); PumpDispatcher();
                    var decisions = (DataGrid)window.FindName("DecisionGrid");
                    var meaning = model.Decisions.OrderByDescending(row => row.Meaning.Length).First();
                    var rows = model.Decisions.OrderByDescending(row => row.Rows.Length).First();
                    Check(label + " row6 already-fixed legend meaning is complete on hover", ImportWalkFullTip(ImportWalkCell(decisions, meaning,
                        decisions.Columns.Single(column => Equals(column.Header, "Meaning from legend"))), meaning.Meaning));
                    Check(label + " row6 already-fixed source row list is complete on hover", ImportWalkFullTip(ImportWalkCell(decisions, rows,
                        decisions.Columns.Single(column => Equals(column.Header, "Source rows"))), rows.Rows));
                    CaptureFamilyListPage(window, output, theme, size.W, size.H, "04-decisions");

                    FinishReviewWork(() => model.ReviewWorkbookSectionAsync("subclasses")); PumpDispatcher(); window.UpdateLayout();
                    Check(label + " row2 keeps the correct editor action label", model.ForwardLabel == "Keep subclass choices");
                    Check(label + " row2 has no duplicate return button and names the final acceptance", !model.CanReturnToWorkbook
                        && !returnButton.IsVisible && model.Status.Contains("Accept and import", StringComparison.Ordinal)
                        && model.BackToolTip.Contains("Review your workbook", StringComparison.Ordinal));
                    var status = WalkTree(footer).OfType<TextBlock>().Single(text => text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path?.Path == "Status");
                    Check(label + " row2 shows only the subclass editor status", !status.IsVisible);
                    var subclassReview = WalkTree(window).OfType<FamilyListSubclassReview>().Single();
                    Check(label + " row2 visible subclass status names the final acceptance",
                        subclassReview.ValidationStatus.Contains("Accept and import", StringComparison.Ordinal));
                    var detail = (ScrollViewer)subclassReview.FindName("DetailScroll");
                    detail.ScrollToTop(); PumpDispatcher();
                    var rules = (ItemsControl)subclassReview.FindName("RulesList");
                    var firstCard = rules.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
                    Check(label + " row2 first rule card is complete at the initial scroll position", firstCard != null
                        && firstCard.TranslatePoint(new Point(0, firstCard.ActualHeight), detail).Y <= detail.ActualHeight + 1,
                        "card bottom=" + firstCard?.TranslatePoint(new Point(0, firstCard.ActualHeight), detail).Y + "; viewport=" + detail.ActualHeight);
                    CaptureFamilyListPage(window, output, theme, size.W, size.H, "05-subclasses");
                    Check(label + " layout inspection changes no project or source workbook", project.ToJsonText() == original && TestFileHash.Upper(path) == hash);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
