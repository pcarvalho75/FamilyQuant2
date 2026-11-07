using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    public Func<ManualFamilyViewModel, bool>? ManualFamilyDialogOverride { get; set; }
    private ReviewProteinRow[] ManualRows(object sender) => ((sender as FrameworkElement)?.Tag as string == "Unassigned" ? unassignedGrid : evidenceGrid)
        .SelectedItems.OfType<ReviewProteinRow>().ToArray();
    private void AddManualFamily_Click(object sender, RoutedEventArgs e)
    {
        if (!model.CanEditManual || model.Evidence == null) return;
        var rows = ManualRows(sender);
        if (rows.Length == 0) { model.ReportManualFailure("Highlight proteins before adding them to a family."); return; }
        var editor = new ManualFamilyViewModel(model.Evidence, rows);
        Evidence.EvidenceManualRequest? choice = null;
        if (ManualFamilyDialogOverride != null)
        {
            if (ManualFamilyDialogOverride(editor) && editor.CanConfirm) choice = editor.Capture();
        }
        else
        {
            var dialog = new ManualFamilyWindow(editor) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            if (dialog.ShowDialog() == true) choice = dialog.Choice;
        }
        if (choice == null) return;
        try { model.AddToFamily(choice); ShowManualProtein(); }
        catch (Exception error) { Operations.LogFailure(error); model.ReportManualFailure(error.Message); }
    }
    private void RemoveManualFamily_Click(object sender, RoutedEventArgs e)
    {
        if (!model.CanEditManual) return;
        var indices = ManualRows(sender).Select(p => p.ProteinIndex).ToArray();
        try { model.RemoveFromFamily(indices); ShowManualProtein(); }
        catch (Exception error) { Operations.LogFailure(error); model.ReportManualFailure(error.Message); }
    }
    private void ShowManualProtein()
    {
        evidenceTabs.SelectedItem = evidenceTabs.Items.OfType<TabItem>().First(t => (string)t.Header == "Proteins");
        if (model.Evidence?.SelectedProtein is { } row) { evidenceGrid.SelectedItem = row; evidenceGrid.ScrollIntoView(row); }
    }
}
