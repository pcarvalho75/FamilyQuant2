using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class ClusterGenerationControl
{
    private void InitializeGroupingWorkflow()
    {
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CurationViewModel.NextGroupingStep)) RefreshGroupingStepStyles();
        };
        RefreshGroupingStepStyles();
    }

    private void RefreshGroupingStepStyles()
    {
        var steps = new[] { ButtonFetchDomains, ButtonEvidenceGrouping, ButtonFetchFamilyWorld, ButtonComputeFullXic };
        for (int index = 0; index < steps.Length; index++)
        {
            if (ViewModel.NextGroupingStep == index + 1) steps[index].SetResourceReference(StyleProperty, "PrimaryButton");
            else steps[index].ClearValue(StyleProperty);
        }
    }
}
