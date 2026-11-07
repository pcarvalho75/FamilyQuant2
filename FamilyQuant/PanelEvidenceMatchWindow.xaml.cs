using FamilyQuant.Panels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class PanelEvidenceMatchWindow : Window
{
    private readonly PanelEvidenceMatchViewModel model;
    public PanelConfirmedEvidenceMatches? Matches { get; private set; }
    public Func<string, bool>? BroadMatchConfirmationOverride { get; set; }
    public PanelEvidenceMatchWindow(PanelEvidenceMatchViewModel model)
    {
        InitializeComponent(); this.model = model; DataContext = model;
    }
    private void AddLabel_Click(object sender, RoutedEventArgs e)
    {
        var row = model.AddLabel(); MatchLabelsGrid.SelectedItem = row;
        MatchLabelsGrid.ScrollIntoView(row); MatchLabelsGrid.CurrentCell = new DataGridCellInfo(row, MatchLabelsGrid.Columns[2]);
        MatchLabelsGrid.BeginEdit();
    }
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        MatchLabelsGrid.CommitEdit(DataGridEditingUnit.Cell, true); MatchLabelsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!model.TryConfirm(out var matches, warning => BroadMatchConfirmationOverride?.Invoke(warning)
            ?? new BroadEvidenceMatchWindow(warning) { Owner = this }.ShowDialog() == true)) return;
        Matches = matches; DialogResult = true;
    }
}
