using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;

namespace FamilyQuant;

public partial class PanelEditorWindow : Window
{
    private readonly IPanelSpreadsheetReader reader;
    private readonly IFamilyPanelSpreadsheetImporter importer;
    private readonly IPanelPfamReview pfamReview;
    private string? workbookPath;
    private PanelSpreadsheetTable? sourceTable;
    private readonly string initialPanelJson;
    public PanelEditorDraft Draft { get; private set; }
    public FamilyPanel? Result { get; private set; }
    public ObservableCollection<PanelDecisionRow> DecisionMappings { get; } = new();

    public PanelEditorWindow(FamilyPanel? panel, IPanelSpreadsheetReader? reader = null, IFamilyPanelSpreadsheetImporter? importer = null, IPanelPfamReview? pfamReview = null)
    {
        InitializeComponent();
        this.reader = reader ?? new PanelSpreadsheetReader();
        this.importer = importer ?? new FamilyPanelSpreadsheetImporter();
        this.pfamReview = pfamReview ?? new PanelPfamReview();
        Draft = new PanelEditorDraft(panel);
        initialPanelJson = JsonSerializer.Serialize(panel ?? new FamilyPanel());
        DataContext = Draft;
        var options = new[]
        {
            new DecisionOption("Quantify", FamilyPanelDecision.Quantify),
            new DecisionOption("Report under others", FamilyPanelDecision.ReportUnderOthers),
            new DecisionOption("Not a family", FamilyPanelDecision.NotAFamily),
            new DecisionOption("Merge into family", FamilyPanelDecision.MergeIntoAnotherFamily),
        };
        DecisionColumn.ItemsSource = options;
        MappingDecisionColumn.ItemsSource = options;
        MappingGrid.ItemsSource = DecisionMappings;
    }

    private sealed record DecisionOption(string Label, FamilyPanelDecision Value);

    private void AddFamily_Click(object sender, RoutedEventArgs e)
    {
        var row = new PanelEditorRow();
        Draft.Families.Add(row);
        ClearPfamCheck();
        FamilyGrid.SelectedItem = row;
        FamilyGrid.ScrollIntoView(row);
    }

    private void RemoveFamily_Click(object sender, RoutedEventArgs e)
    {
        if (FamilyGrid.SelectedItem is PanelEditorRow row) { Draft.Families.Remove(row); ClearPfamCheck(); }
    }

    private void ClearPfamCheck()
    {
        PfamGrid.ItemsSource = null;
        PfamStatus.Text = "Not checked for the current panel.";
    }

    private void FamilyGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) => ClearPfamCheck();
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        FamilyGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FamilyGrid.CommitEdit(DataGridEditingUnit.Row, true);
        try { Result = Draft.Build(); DialogResult = true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { StatusText.Text = "The panel was not applied: " + ex.Message; }
    }

    private void SetDraft(FamilyPanel panel)
    {
        Draft = new PanelEditorDraft(panel);
        DataContext = Draft;
        ClearPfamCheck();
    }

    public void LoadTemplate(string path)
    {
        SetDraft(FamilyPanelTemplate.Read(path));
        ImportStatus.Text = "Template loaded. Review the families before applying the panel.";
    }

    public void LoadColumns(string path, string sheet, int headerRow)
    {
        SetColumns(path, reader.ReadTable(path, sheet, headerRow));
    }

    private void SetColumns(string path, PanelSpreadsheetTable table)
    {
        workbookPath = path;
        sourceTable = table;
        KeyColumn.ItemsSource = table.Headers;
        NameColumn.ItemsSource = table.Headers;
        SourceDecisionColumn.ItemsSource = table.Headers;
        PfamColumn.ItemsSource = new[] { string.Empty }.Concat(table.Headers).ToArray();
        DecisionMappings.Clear();
        ImportStatus.Text = table.Rows.Count.ToString("N0", CultureInfo.CurrentCulture) + " rows. Choose columns and map each decision.";
    }

    public void PreviewImportedPanel(FamilyPanelImport choices, IReadOnlyList<FamilyPanelDecisionMapping> mappings)
    {
        if (sourceTable == null) throw new InvalidOperationException("Preview spreadsheet columns first.");
        SetDraft(importer.Import(sourceTable, choices, mappings, Draft.Name));
        ImportStatus.Text = "Panel preview ready. Review the families before applying the panel.";
    }

    private bool ConfirmReplaceDraft()
    {
        bool changed;
        try { changed = JsonSerializer.Serialize(Draft.Build()) != initialPanelJson; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { changed = true; }
        return !changed || ShellMessages.Report(this, "Replace the changes in this panel draft?", "Replace panel draft",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }
    private async void ChooseWorkbook_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", Title = "Choose a family panel spreadsheet" };
        if (!ShellDialogs.ShowFile(this, "panel.spreadsheet-open", dialog) || !ConfirmReplaceDraft()) return;
        using var operation = AppOperationHost.For(this).Begin("Read panel spreadsheet");
        try
        {
            var loaded = await Task.Run(() => FamilyPanelTemplate.IsTemplate(dialog.FileName)
                ? (Panel: FamilyPanelTemplate.Read(dialog.FileName), Sheets: (IReadOnlyList<string>?)null)
                : (Panel: (FamilyPanel?)null, Sheets: reader.ReadSheetNames(dialog.FileName)), operation.Context.CancellationToken);
            operation.Context.ThrowIfCancellationRequested();
            if (loaded.Panel != null)
            {
                SetDraft(loaded.Panel);
                ImportStatus.Text = "Template loaded. Review the families before applying the panel.";
            }
            else
            {
                workbookPath = dialog.FileName; sourceTable = null;
                DecisionMappings.Clear();
                KeyColumn.ItemsSource = NameColumn.ItemsSource = SourceDecisionColumn.ItemsSource = PfamColumn.ItemsSource = null;
                SheetChoice.ItemsSource = loaded.Sheets;
                SheetChoice.SelectedIndex = loaded.Sheets?.Count > 0 ? 0 : -1;
                ImportStatus.Text = "Choose a sheet and header row, then preview its columns.";
            }
        }
        catch (OperationCanceledException) when (operation.Context.IsCancellationRequested) { ImportStatus.Text = "Import cancelled."; }
        catch (Exception ex) { ImportStatus.Text = "The spreadsheet could not be opened: " + ex.Message; }
    }

    private async void PreviewColumns_Click(object sender, RoutedEventArgs e)
    {
        if (workbookPath == null || SheetChoice.SelectedItem is not string sheet
            || !int.TryParse(HeaderRow.Text, NumberStyles.None, CultureInfo.CurrentCulture, out int row) || row < 1)
        { ImportStatus.Text = "Choose a spreadsheet, sheet and positive header row."; return; }
        string path = workbookPath;
        using var operation = AppOperationHost.For(this).Begin("Read panel columns");
        try
        {
            var table = await Task.Run(() => reader.ReadTable(path, sheet, row), operation.Context.CancellationToken);
            operation.Context.ThrowIfCancellationRequested();
            SetColumns(path, table);
        }
        catch (OperationCanceledException) when (operation.Context.IsCancellationRequested) { ImportStatus.Text = "Column preview cancelled."; }
        catch (Exception ex) { ImportStatus.Text = "Columns could not be read: " + ex.Message; }
    }

    private void SourceDecisionColumn_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DecisionMappings.Clear();
        if (sourceTable == null || SourceDecisionColumn.SelectedItem is not string header) return;
        foreach (string value in sourceTable.Rows.Select(row => row.Cells[header]).Distinct(StringComparer.Ordinal))
            DecisionMappings.Add(new PanelDecisionRow { Source = value });
    }

    private void PreviewPanel_Click(object sender, RoutedEventArgs e)
    {
        MappingGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        MappingGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (sourceTable == null || KeyColumn.SelectedItem is not string key || NameColumn.SelectedItem is not string name
            || SourceDecisionColumn.SelectedItem is not string decision || DecisionMappings.Any(x => x.Decision == null))
        { ImportStatus.Text = "Choose the key, name and decision columns and map every decision."; return; }
        var choices = new FamilyPanelImport { FamilySheet = sourceTable.SheetName, HeaderRowNumber = sourceTable.HeaderRowNumber,
            SourceSha256 = sourceTable.SourceSha256, ColumnHeaders = new()
            { [PanelImportFields.FamilyKey] = key, [PanelImportFields.DisplayName] = name, [PanelImportFields.Decision] = decision } };
        if (PfamColumn.SelectedItem is string pfam && pfam.Length > 0) choices.ColumnHeaders[PanelImportFields.Pfam] = pfam;
        var mappings = DecisionMappings.Select(x => new FamilyPanelDecisionMapping { SourceValue = x.Source,
            Decision = x.Decision!.Value, MergeTargetFamilyKey = x.MergeTarget }).ToList();
        if (!ConfirmReplaceDraft()) return;
        try { PreviewImportedPanel(choices, mappings); }
        catch (Exception ex) { ImportStatus.Text = "The panel could not be previewed: " + ex.Message; }
    }

    private async void CheckPfam_Click(object sender, RoutedEventArgs e)
    {
        FamilyGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FamilyGrid.CommitEdit(DataGridEditingUnit.Row, true);
        FamilyPanel panel;
        try { panel = Draft.Build(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { PfamStatus.Text = "The panel could not be checked: " + ex.Message; return; }
        using var operation = AppOperationHost.For(this).Begin("Check panel Pfam names");
        try
        {
            var rows = await pfamReview.CheckAsync(panel, operation.Context.CancellationToken);
            operation.Context.ThrowIfCancellationRequested();
            PfamGrid.ItemsSource = rows;
            PfamStatus.Text = rows.Count.ToString("N0", CultureInfo.CurrentCulture) + (rows.Count == 1 ? " check result. " : " check results. ") + "Panel data were not changed.";
        }
        catch (OperationCanceledException) when (operation.Context.IsCancellationRequested) { PfamStatus.Text = "Check cancelled."; }
        catch (Exception ex) { PfamStatus.Text = "The panel could not be checked: " + ex.Message; }
    }
    private void Help_Click(object sender, RoutedEventArgs e) =>
        TextReportWindow.Show("Family panel help", PanelReportHelp.Panel, this, wrap: true);

    private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        FamilyGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FamilyGrid.CommitEdit(DataGridEditingUnit.Row, true);
        FamilyPanel panel;
        try { panel = Draft.Build(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { StatusText.Text = "The spreadsheet was not saved: " + ex.Message; return; }
        var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true,
            Title = "Save family panel spreadsheet", FileName = "FamilyQuant_panel.xlsx" };
        if (!ShellDialogs.ShowFile(this, "panel.spreadsheet-save", dialog)) return;
        using var operation = AppOperationHost.For(this).Begin("Save family panel spreadsheet");
        try
        {
            await Task.Run(() => AppOperationFiles.WriteFile(dialog.FileName, path => FamilyPanelTemplate.Write(path, panel), operation.Context));
            StatusText.Text = "Spreadsheet saved to " + dialog.FileName + ".";
        }
        catch (OperationCanceledException) when (operation.Context.IsCancellationRequested) { StatusText.Text = "Save cancelled."; }
        catch (Exception ex) { StatusText.Text = "The spreadsheet was not saved: " + ex.Message; }
    }
}

public sealed class PanelDecisionRow
{
    public string Source { get; set; } = string.Empty;
    public FamilyPanelDecision? Decision { get; set; }
    public string MergeTarget { get; set; } = string.Empty;
}
