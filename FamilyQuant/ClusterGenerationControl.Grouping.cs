namespace FamilyQuant;

public partial class ClusterGenerationControl
{
    private void InitializeGroupingWorkflow()
    {
        ViewModel.DocumentChanged += (_, _) => RefreshGroupingStepStyles();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CurationViewModel.NextGroupingStep)) RefreshGroupingStepStyles();
        };
        RefreshGroupingStepStyles();
    }

    private void RefreshGroupingStepStyles()
    {
        var panel = fcf.FamilyPanel;
        TextBlockFamilyListStatus.Text = panel == null ? "No family list" : panel.Name + " (" + panel.Families.Count.ToString("N0")
            + (panel.Families.Count == 1 ? " family)" : " families)");
        TextBlockEvidenceGroupingStatus.Text = ViewModel.EvidenceGroupingStepStatus
            + (panel == null ? ". No family list; step 0 is optional." : "");
        var steps = new[] { ButtonImportFamilyList, ButtonFetchDomains, ButtonEvidenceGrouping, ButtonFetchFamilyWorld, ButtonComputeFullXic };
        for (int index = 0; index < steps.Length; index++)
        {
            if (ViewModel.NextGroupingStep == index) steps[index].SetResourceReference(StyleProperty, "PrimaryButton");
            else steps[index].ClearValue(StyleProperty);
        }
    }

    private void ButtonImportFamilyList_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (System.Windows.Window.GetWindow(this) is MainWindow window && window.MenuItemFamilyPanel.IsEnabled)
            window.MenuItemFamilyPanel.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
    }
}
