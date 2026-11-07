using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class PanelEditorWindow
{
    private readonly FamilyClusterFile? panelMatchEvidenceFile;
    private PanelEvidenceMatching? panelMatchSnapshot;
    public Func<PanelEvidenceMatchViewModel, bool>? PanelMatchesDialogOverride { get; set; }
    private void UpdateMatchButton() => MatchStoredNamesButton.IsEnabled = panelMatchEvidenceFile != null && FamilyGrid.SelectedItem is PanelEditorRow;
    private void MatchStoredNames_Click(object sender, RoutedEventArgs e)
    {
        if (panelMatchEvidenceFile == null || FamilyGrid.SelectedItem is not PanelEditorRow row)
        { StatusText.Text = "Select a panel family before matching its evidence labels."; return; }
        FamilyGrid.CommitEdit(DataGridEditingUnit.Cell, true); FamilyGrid.CommitEdit(DataGridEditingUnit.Row, true);
        try
        {
            var panel = Draft.Build();
            if (panelMatchSnapshot == null || !panelMatchSnapshot.IsCurrent(panelMatchEvidenceFile)) panelMatchSnapshot = new(panelMatchEvidenceFile);
            var snapshot = panelMatchSnapshot;
            var context = new PanelEvidenceMatchContext(snapshot, panel.Families.Single(family => family.Key == row.Key),
                panel.ConfirmedEvidenceMatches?.GetValueOrDefault(row.Key), () => snapshot.IsCurrent(panelMatchEvidenceFile));
            var editor = new PanelEvidenceMatchViewModel(context);
            PanelConfirmedEvidenceMatches? matches = null;
            if (PanelMatchesDialogOverride != null)
            {
                if (PanelMatchesDialogOverride(editor)) editor.TryConfirm(out matches);
            }
            else
            {
                var dialog = new PanelEvidenceMatchWindow(editor) { Owner = this };
                if (dialog.ShowDialog() == true) matches = dialog.Matches;
            }
            if (matches == null) return;
            Draft.SetConfirmedMatches(row, matches);
            StatusText.Text = "Confirmed labels are in this panel draft. Apply panel and File > Save keep them.";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { StatusText.Text = error.Message; }
    }
}
