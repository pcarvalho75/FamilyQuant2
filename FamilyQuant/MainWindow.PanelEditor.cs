using System;
using System.Threading.Tasks;
using System.Windows;
using FamilyQuant.Panels;

namespace FamilyQuant;

public partial class MainWindow
{
    private async void MenuItemFamilyPanel_Click(object sender, RoutedEventArgs e)
    {
        var host = cgc;
        if (host == null) return;
        var previous = host.fcf.FamilyPanel == null ? null : FamilyPanelValidation.Clone(host.fcf.FamilyPanel);
        var window = new PanelEditorWindow(host.fcf.FamilyPanel) { Owner = this };
        if (window.ShowDialog() != true || window.Result == null || !host.ApplyPanel(window.Result)) return;
        bool subclassesChanged = PanelSubclassRefresh.DefinitionsChanged(previous, window.Result);
        bool FamilyRulesCurrent() => PanelDerivedDataCurrentness.Check(host.fcf).FamilyAssignments == PanelDerivedDataStatus.Current;
        bool familyStale = PanelDerivedDataCurrentness.Check(host.fcf).FamilyAssignments == PanelDerivedDataStatus.OutOfDate;
        try
        {
            bool updated = await PanelSubclassRefresh.ReviewThenRecomputeAsync(familyStale, subclassesChanged,
                FamilyRulesCurrent,
                () => ShellMessages.Report(this, "Family assignments need review after this panel change. Review them now?",
                    "Family panel", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes,
                host.ReviewEvidenceAsync, () => SubclassAssignmentRunner.RecomputeSubclassesAsync(host, combineWithPreviousUndo: true),
                () => SubclassAssignment.IsCurrent(host.fcf));
            if (subclassesChanged && !updated && !FamilyRulesCurrent()) host.ReportPanelRefreshPending();
        }
        catch (Exception ex) { host.ReportPanelRefreshFailure(ex.Message); }
    }
}

public partial class ClusterGenerationControl
{
    public bool ApplyPanel(FamilyPanel panel)
    {
        if (!ViewModel.ApplyFamilyPanel(panel)) return false;
        UpdateUndoButton();
        string notice = new PanelAssignmentReadiness().DescribeAfterChange(fcf);
        ShowCurationOperationResult("Family panel applied. Undo restores the previous panel." + (notice.Length == 0 ? "" : " " + notice));
        return true;
    }

    public void ReportPanelRefreshPending() => ShowCurationOperationResult(
        "Panel applied. Rebuild family assignments before recomputing subclass labels.");

    public void ReportPanelRefreshFailure(string reason) => ShowCurationOperationResult(
        "Panel applied, but subclass labels were not updated: " + reason);
}
