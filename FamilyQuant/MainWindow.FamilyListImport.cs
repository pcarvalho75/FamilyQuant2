using FamilyQuant.Examples;
using FamilyQuant.Panels.Import;
using Microsoft.Win32;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FamilyQuant;

public partial class MainWindow
{
    private async void MenuItemFamilyPanel_Click(object sender, RoutedEventArgs e)
    {
        var host = cgc;
        if (host == null) return;
        var file = host.fcf;
        var transaction = new FamilyListImportTransaction(host.ViewModel,
            () => ReferenceEquals(cgc, host) && ReferenceEquals(host.fcf, file), host.RefreshFamilyListImport);
        var model = new FamilyListImportViewModel(transaction, matcher: new FamilyListStoredEvidenceMatches(file));
        var window = new FamilyListImportWindow(model, path => ExampleFamilyList.Write(path), PanelImportTemplate.Write) { Owner = this };
        window.ShowDialog();
        if (window.EditPanelRequested && ReferenceEquals(cgc, host)) await OpenFamilyPanelEditorAsync();
        if (window.OpenChecklistRequested && ReferenceEquals(cgc, host) && ReferenceEquals(host.fcf, file))
        {
            ShowCurationOrOpen();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await host.OpenImportedFamilyChecklistAsync();
        }
    }

    private void MenuSaveFamilyListTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = "family-list-template.xlsx", Title = "Save a family list template" };
        if (!ShellDialogs.ShowFile(this, "panel.template-new", dialog)) return;
        try { PanelImportTemplate.Write(dialog.FileName); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        { ShellMessages.Report(this, "The template could not be saved: " + error.Message, "Family list template", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}

public partial class ClusterGenerationControl
{
    private bool importedFamilyChecklistRequested;
    public void RefreshFamilyListImport()
    {
        UpdateUndoButton();
        ShowCurationOperationResult("Family list updated. Review protein choices in Family checklist; File > Save keeps the panel.");
    }
    public async Task OpenImportedFamilyChecklistAsync()
    {
        importedFamilyChecklistRequested = true;
        try { await ReviewEvidenceAsync(); }
        finally { importedFamilyChecklistRequested = false; }
    }
}
