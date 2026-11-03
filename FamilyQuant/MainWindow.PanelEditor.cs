using System.Windows;
using FamilyQuant.Panels;

namespace FamilyQuant;

public partial class MainWindow
{
    private void MenuItemFamilyPanel_Click(object sender, RoutedEventArgs e)
    {
        if (cgc == null) return;
        var window = new PanelEditorWindow(cgc.fcf.FamilyPanel) { Owner = this };
        if (window.ShowDialog() == true && window.Result != null) cgc.ApplyPanel(window.Result);
    }
}

public partial class ClusterGenerationControl
{
    public void ApplyPanel(FamilyPanel panel)
    {
        if (!ViewModel.ApplyFamilyPanel(panel)) return;
        UpdateUndoButton();
        string notice = new PanelAssignmentReadiness().DescribeAfterChange(fcf);
        ShowCurationOperationResult("Family panel applied. Undo restores the previous panel." + (notice.Length == 0 ? "" : " " + notice));
    }
}
