using FamilyQuant.Panels.Import;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FamilyQuant;

public partial class FamilyListImportWindow : Window
{
    public FamilyListImportViewModel Model { get; }
    public bool OpenChecklistRequested { get; private set; }
    public bool EditPanelRequested { get; private set; }
    public Func<string, bool>? BroadMatchConfirmationOverride { get; set; }
    private readonly Action<string>? templateWriter;
    private bool closing, loadingSheets, loadingRowActions, fittingSummaryRows;
    private double summaryFitParentHeight;
    public FamilyListImportWindow(FamilyListImportViewModel model, Action<string>? exampleWriter = null, Action<string>? templateWriter = null)
    {
        InitializeComponent(); Model = model; DataContext = model;
        Model.ConfirmWorkbookSheetChange = message => ShellMessages.Report(this, message, "Change family sheet",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        AlignSummaryCells();
        SizeChanged += (_, _) => FitSummaryRows();
        SheetPreviewGrid.LayoutUpdated += (_, _) => PositionPreviewTitles();
        SheetPreviewGrid.SizeChanged += (_, _) => FitPreviewColumns();
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FamilyListImportViewModel.Page))
            { summaryFitParentHeight = 0; if (model.Page == 5) Dispatcher.BeginInvoke(FitSummaryRows); }
        };
        this.templateWriter = templateWriter;
        TemplateButton.IsEnabled = templateWriter != null;
        if (templateWriter == null) TemplateButton.ToolTip = "Unavailable in this view; open File > Import family list to use the built-in template. It writes an empty sheet to fill and import, leaving the project unchanged.";
        DecisionActionColumn.ItemsSource = FamilyListImportViewModel.DecisionOptions;
        WorkbookRoleColumn.ItemsSource = FamilyListImportViewModel.SheetRoles;
        WorkbookOverview.ReviewRequested += async section => await Model.ReviewWorkbookSectionAsync(section);
        Model.WorkbookViewRequested += WorkbookView_Requested;
        Model.PropertyChanged += Model_Changed;
        RefreshRowActionChoice(updateOptions: true);
        PreviewKeyDown += Window_KeyDown;
        Closed += (_, _) => { Model.ConfirmWorkbookSheetChange = null; Model.WorkbookViewRequested -= WorkbookView_Requested; Model.PropertyChanged -= Model_Changed; Model.Dispose(); };
    }

    private void Model_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FamilyListImportViewModel.HasWorkbookMap))
        {
            WorkbookTabs.SelectedItem = Model.HasWorkbookMap ? WorkbookMapTab : SheetPreviewTab;
            DecisionMeaningColumn.Visibility = Model.HasWorkbookMap ? Visibility.Visible : Visibility.Collapsed;
        }
        if (e.PropertyName == nameof(FamilyListImportViewModel.Preview)) BuildPreview();
        if (e.PropertyName is nameof(FamilyListImportViewModel.SelectedSpecialRow) or nameof(FamilyListImportViewModel.AvailableRowOptions))
            RefreshRowActionChoice(updateOptions: true);
        else if (e.PropertyName == nameof(FamilyListImportViewModel.CanUseRowChoice)) RefreshRowActionChoice(updateOptions: false);
        if (e.PropertyName is nameof(FamilyListImportViewModel.Page) or nameof(FamilyListImportViewModel.ReviewingDecisions))
            Dispatcher.BeginInvoke(new Action(() => { if (!closing && IsLoaded) { PageHeading.Focus(); PageHeading.BringIntoView(); } }));
    }

    private void RefreshRowActionChoice(bool updateOptions)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshRowActionChoice(updateOptions)));
            return;
        }
        if (closing) return;
        loadingRowActions = true;
        try
        {
            if (updateOptions) RowActionChoice.ItemsSource = Model.AvailableRowOptions;
            RowActionChoice.SelectedValue = Model.SelectedSpecialRow?.Action;
        }
        finally { loadingRowActions = false; }
    }

    private void RowAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingRowActions || Model == null || !Model.CanEdit || Model.SelectedSpecialRow == null
            || RowActionChoice.SelectedValue is not FamilyPanelRowAction action
            || !Model.AvailableRowOptions.Any(option => option.Value == action)) return;
        Model.SelectedSpecialRow.Action = action;
    }

    private void BuildPreview()
    {
        loadingSheets = true;
        try
        {
            previewTitles.Clear(); PreviewMergedTitles.Children.Clear();
            SheetPreviewGrid.Columns.Clear(); SheetPreviewGrid.ItemsSource = null;
            var preview = Model.Preview;
            if (preview == null) return;
            var rowColumn = new DataGridTextColumn { Header = "Row", Binding = new Binding(nameof(PanelPreviewRow.RowNumber)), Width = 62, IsReadOnly = true,
                HeaderStyle = (Style)FindResource("WizardNumberHeader"), ElementStyle = (Style)FindResource("WizardNumberText") };
            Themes.TableAppearance.SetHeaderToolTip(rowColumn, "The original Excel row number, counted from 1; this read-only preview never changes the workbook or project.");
            SheetPreviewGrid.Columns.Add(rowColumn);
            for (int column = 0; column < preview.ColumnCount; column++)
            {
                var border = new FrameworkElementFactory(typeof(Border));
                border.SetBinding(Border.BackgroundProperty, new Binding("Cells[" + column + "]") { Converter = new ImportSourceFillConverter() });
                var text = new FrameworkElementFactory(typeof(TextBlock));
                var visibleText = new MultiBinding { Converter = new ImportSourceVisibleTextConverter() };
                visibleText.Bindings.Add(new Binding("Cells[" + column + "]")); visibleText.Bindings.Add(new Binding(nameof(PanelPreviewRow.RowNumber)));
                text.SetBinding(TextBlock.TextProperty, visibleText);
                text.SetBinding(TextBlock.ForegroundProperty, new Binding("Cells[" + column + "]") { Converter = new ImportSourceInkConverter() });
                text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
                text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                text.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2));
                text.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Cells[" + column + "]") { Converter = new ImportSourceDetailConverter() });
                border.AppendChild(text);
                string letter = preview.Rows.FirstOrDefault(r => r.Cells.Count > column)?.Cells[column].ColumnLetter ?? (column + 1).ToString(CultureInfo.CurrentCulture);
                var sourceColumn = new DataGridTemplateColumn { Header = letter, Width = 145, MinWidth = 110,
                    CellTemplate = new DataTemplate { VisualTree = border }, ClipboardContentBinding = new Binding("Cells[" + column + "].Text"), IsReadOnly = true };
                Themes.TableAppearance.SetHeaderToolTip(sourceColumn, "Original workbook column " + letter + "; read only, with complete cell text available in Cell details. Selecting a cell changes only the displayed details.");
                SheetPreviewGrid.Columns.Add(sourceColumn);
            }
            SheetPreviewGrid.ItemsSource = preview.Rows;
            BuildPreviewTitles(preview);
            SheetChoice.SelectedItem = Model.Sheet;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (closing || !ReferenceEquals(Model.Preview, preview) || preview.Rows.Count == 0) return;
                SheetPreviewGrid.ScrollIntoView(preview.Rows[0]);
                FitPreviewColumns();
            }));
        }
        finally { loadingSheets = false; }
    }

    private void Preview_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        var selected = SheetPreviewGrid.SelectedCells.FirstOrDefault();
        if (selected.Item is not PanelPreviewRow row || selected.Column == null || selected.Column.DisplayIndex == 0) return;
        int column = SheetPreviewGrid.Columns.IndexOf(selected.Column) - 1;
        if (column < 0 || column >= row.Cells.Count) return;
        var cell = row.Cells[column];
        CellDetailText.Text = cell.ColumnLetter + row.RowNumber.ToString(CultureInfo.CurrentCulture) + "\n" + ImportSourceDetailConverter.Describe(cell);
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", Title = "Choose your family list" };
        if (!Model.CanEdit || !ShellDialogs.ShowFile(this, "panel.wizard-open", dialog)) return;
        await Model.LoadAsync(dialog.FileName);
    }

    private async void Sheet_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingSheets || Model == null || Model.IsBusy || Model.Imported || SheetChoice.SelectedItem is not string name || name == Model.Sheet) return;
        if (Model.HasWorkbookMap) await Model.SelectWorkbookFamilySheetAsync(name);
        else await Model.LoadAsync(Model.WorkbookPath, name);
        if (SheetChoice.SelectedItem as string != Model.Sheet)
        { loadingSheets = true; try { SheetChoice.SelectedItem = Model.Sheet; } finally { loadingSheets = false; } }
    }

    private async void Header_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(Model.HeaderRowText, NumberStyles.Integer, CultureInfo.CurrentCulture, out int row) || row <= 0)
        { Model.ReportStatus("Enter the positive row number containing the column names."); HeaderRowBox.Focus(); return; }
        await Model.LoadAsync(Model.WorkbookPath, Model.Sheet, row);
    }

    private async void Forward_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdits();
        if (Model.Imported && Model.Page == 6) { OpenChecklistRequested = true; Close(); return; }
        await Model.NextAsync(message => ShellMessages.Report(this, message, "Import family list",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
        if (Model.Imported) Close();
    }
    private void Back_Click(object sender, RoutedEventArgs e) { CommitGridEdits(); Model.Back(); }
    private void Step_Click(object sender, RoutedEventArgs e) { CommitGridEdits(); if (sender is FrameworkElement { Tag: int index }) Model.Revisit(index); }
    private void ConfirmDecisions_Click(object sender, RoutedEventArgs e) { CommitGridEdits(); Model.ConfirmDecisions(); }
    private void ReviewDecisions_Click(object sender, RoutedEventArgs e) => Model.ReviewingDecisions = true;
    private void BackToColumns_Click(object sender, RoutedEventArgs e) => Model.ReviewingDecisions = false;
    private void UseDecisionHint_Click(object sender, RoutedEventArgs e)
    {
        if (Model.CanEdit && Model.HasDecisionHint && Model.Columns.FirstOrDefault(c => c.Role == PanelImportFields.Decision) is { } column)
            column.Header = Model.DecisionHint;
    }
    private void SourceNotes_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: ImportSummaryFamily family }) TextReportWindow.Show("Source notes", family.Name + "\nAbbreviation: " + family.Key + "\n\n" + family.Notes, this, wrap: true); }
    private void UseRow_Click(object sender, RoutedEventArgs e) { CommitGridEdits(); Model.UseRowChoice(); }
    private void ConfirmMatches_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdits();
        Model.ConfirmMatches(warning =>
        {
            if (BroadMatchConfirmationOverride != null) return BroadMatchConfirmationOverride(warning);
            var dialog = new BroadEvidenceMatchWindow(warning) { Owner = this };
            dialog.KeepButton.ToolTip = "Confirms these broad labels for the imported family draft. Import keeps the panel as one project change that Undo can reverse.";
            return dialog.ShowDialog() == true;
        });
    }
    private void SkipMatch_Click(object sender, RoutedEventArgs e) => Model.SkipMatch();
    private void SkipAllMatches_Click(object sender, RoutedEventArgs e) => Model.SkipAllMatches();
    private void Undo_Click(object sender, RoutedEventArgs e) => Model.UndoImport();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Editor_Click(object sender, RoutedEventArgs e) { EditPanelRequested = true; Close(); if (!closing) EditPanelRequested = false; }

    private void Template_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.CanEdit || templateWriter == null) return;
        var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = "family-list-template.xlsx", Title = "Save a family list template" };
        if (!ShellDialogs.ShowFile(this, "panel.wizard-template", dialog)) return;
        try { templateWriter(dialog.FileName); Model.ReportStatus("Template saved. Choose it from Browse after adding your families."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { Model.ReportStatus("The template could not be saved. " + error.Message); }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    { e.Effects = Model != null && Model.CanEdit && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!Model.CanEdit || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        if (files.Length != 1 || !string.Equals(Path.GetExtension(files[0]), ".xlsx", StringComparison.OrdinalIgnoreCase))
        { Model.ReportStatus("Choose one Excel workbook (.xlsx)."); return; }
        await Model.LoadAsync(files[0]);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        else if (e.Key == Key.F1) { e.Handled = true; ShowHelp(); }
    }
    private void Help_Click(object sender, RoutedEventArgs e) => ShowHelp();
    private void WorkbookNotes_Click(object sender, RoutedEventArgs e) => TextReportWindow.Show("Workbook source notes",
        string.Join("\n\n", Model.KeptNotes.Select(n => n.SheetName + "\n" + n.Text)), this, wrap: true);
    private async void WorkbookFamilySheet_Click(object sender, RoutedEventArgs e) => await Model.ChooseWorkbookFamilySheetAsync();
    private void ReturnWorkbook_Click(object sender, RoutedEventArgs e) => Model.ReturnToWorkbookReview();
    private void WorkbookView_Requested(string section)
    {
        if (section is "sheets" or "notes" or "problems") WorkbookTabs.SelectedItem = WorkbookMapTab;
        if (section == "subclasses" && Model.WorkbookHasReviewProblems) WorkbookSubclassReview.CheckChoices();
    }
    private void ShowHelp() => FamilyListImportGuideWindow.Show(this, HelpSectionKey());
    private string HelpSectionKey() => Model.Page switch
    {
        0 => "welcome",
        1 => "file",
        2 => Model.ReviewingDecisions ? "decisions" : "columns",
        3 => "special",
        4 => "matches",
        5 => "summary",
        _ => "done",
    };

    private void AlignSummaryCells()
    {
        var summaryText = new Style(typeof(TextBlock), (Style)FindResource("WizardCellText"));
        summaryText.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        foreach (var column in SummaryGrid.Columns.OfType<DataGridTextColumn>()) column.ElementStyle = summaryText;
    }

    private void SummaryGrid_SizeChanged(object sender, SizeChangedEventArgs e) => FitSummaryRows();

    private void FitSummaryRows()
    {
        if (fittingSummaryRows || Model?.Page != 5 || SummaryGrid.Parent is not FrameworkElement parent) return;
        double parentHeight = parent.ActualHeight;
        if (parentHeight <= 0 || (Math.Abs(parentHeight - summaryFitParentHeight) < 0.5 && !double.IsNaN(SummaryGrid.MaxHeight))) return;
        fittingSummaryRows = true;
        try
        {
            SummaryGrid.ClearValue(MaxHeightProperty);
            SummaryGrid.UpdateLayout();
            double limit = SummaryGrid.ActualHeight;
            if (limit <= 0) return;
            var scroll = FindDescendant<ScrollViewer>(SummaryGrid);
            if (scroll != null && scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible)
                limit -= SystemParameters.HorizontalScrollBarHeight;
            double lastFull = 0;
            for (int index = 0; index < SummaryGrid.Items.Count; index++)
            {
                if (SummaryGrid.ItemContainerGenerator.ContainerFromIndex(index) is not DataGridRow row || row.ActualHeight <= 0) continue;
                double bottom;
                try { bottom = row.TransformToAncestor(SummaryGrid).Transform(new Point(0, row.ActualHeight)).Y; }
                catch (InvalidOperationException) { break; }
                if (bottom <= limit + 0.5) lastFull = bottom;
                else break;
            }
            if (lastFull <= 0) return;
            summaryFitParentHeight = parentHeight;
            if (lastFull < SummaryGrid.ActualHeight - 1)
                SummaryGrid.MaxHeight = lastFull + SummaryGrid.BorderThickness.Bottom;
        }
        finally { fittingSummaryRows = false; }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T found) return found;
            var nested = FindDescendant<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private void CommitGridEdits()
    {
        foreach (var grid in new[] { DecisionGrid, SpecialRowsGrid, MatchSuggestionsGrid, AdditionalMatchSuggestionsGrid })
        { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        if (IsVisible && !Model.Imported && Model.WorkbookPath.Length > 0
            && ShellMessages.Report(this, "Closing discards this import draft and its column, row and matching choices. The project and workbook stay unchanged. Close without importing?",
                "Discard import draft", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        { e.Cancel = true; return; }
        if (Model.IsBusy) { e.Cancel = true; await Model.CancelReadAsync(); closing = true; Close(); return; }
        closing = true;
    }
}
