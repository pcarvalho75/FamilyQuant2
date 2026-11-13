using FamilyQuant.Panels;
using System;
using System.Windows;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    private readonly Func<string, PanelEvidenceMatchContext>? createMatchContext;
    public Func<PanelEvidenceMatchViewModel, bool>? PanelMatchesDialogOverride { get; set; }
    private void MatchPanelNames_Click(object sender, RoutedEventArgs e)
    {
        if (!model.CanMatchPanelNames || createMatchContext == null || model.Evidence?.SelectedChecklistFamily is not { } family) return;
        try
        {
            var editor = new PanelEvidenceMatchViewModel(createMatchContext(family.Key));
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
            if (matches != null) model.ConfirmPanelMatches(family.Key, matches);
        }
        catch (Exception error) { Operations.LogFailure(error); model.ReportManualFailure(error.Message); }
    }
    private void UnconfirmPanelMatches_Click(object sender, RoutedEventArgs e)
    {
        if (!model.CanUnconfirmPanelMatches) return;
        try { model.UnconfirmPanelMatches(); }
        catch (Exception error) { Operations.LogFailure(error); model.ReportManualFailure(error.Message); }
    }
}
