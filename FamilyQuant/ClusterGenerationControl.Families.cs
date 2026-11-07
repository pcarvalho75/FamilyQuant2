using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant;

public partial class ClusterGenerationControl
{
    private void ApplyFamilyHeaderTooltips()
    {
        DataGridFamilies.Columns[0].HeaderStyle = HeaderTip("Protect the family from automatic grouping, or remove that protection. Undo restores the lock state; families without target proteins cannot be edited here.");
        var idHeader = HeaderTip("The current curated family number shared by these proteins. Edit protein rows to join or split membership.");
        idHeader.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        ColumnFamilyId.HeaderStyle = idHeader;
        ColumnFamilyName.HeaderStyle = HeaderTip("Family name starts from target annotations or database descriptions and accepts free text, including empty text, with no unit or fixed length limit. Editing annotates every target member, including filtered members, and Undo restores their previous text.");
        ColumnFamilyProteins.HeaderStyle = HeaderTip("Target proteins in the whole family, including filtered members. The cell tooltip also gives the visible row count.");
        ColumnFamilyFullXic.HeaderStyle = HeaderTip("The current production family share from full XIC, shown once for the family. The heading names its denominator.");
        ColumnFamilyPatternLab.HeaderStyle = HeaderTip("The current PatternLab family share, shown once for the family. This column appears after PatternLab shares are calculated.");
    }

    private void InitializeFamilyView()
    {
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CurationViewModel.SelectedFamilyProteins)) UpdateFamilyProteins();
        };
    }

    private void UpdateFamilyProteins()
    {
        if (ViewModel == null || DataGridResultGrid == null) return;
        DataGridResultGrid.ItemsSource = ViewModel.SelectedFamilyProteins;
    }

    private void AllFamilyProteins_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedFamily = null;

    private void FamilySelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;
        DataGridResultGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        DataGridResultGrid.CommitEdit(DataGridEditingUnit.Row, true);
        UpdateFamilyProteins();
    }

    private void FamilyName_EditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || !ReferenceEquals(e.Column, ColumnFamilyName)
            || e.Row.Item is not CurationFamilyRow row || e.EditingElement is not TextBox editor
            || editor.Text == row.Name) return;
        string name = editor.Text;
        int family = row.FamilyId;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ViewModel.RenameFamily(family, name))
            {
                UpdateGUI(); ShowCurationOperationResult("Family name changed. Undo restores the previous annotations.");
            }
        }), DispatcherPriority.Background);
    }

    private void FamilyLock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: CurationFamilyRow row } check) return;
        if (ViewModel.SetFamilyLock(row.FamilyId, check.IsChecked == true)) UpdateGUI();
    }
}
