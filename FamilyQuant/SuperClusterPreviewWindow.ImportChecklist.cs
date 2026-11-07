using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    private bool familyChecklistRequested;
    public void RequestFamilyChecklist()
    {
        if (familyChecklistRequested) return;
        familyChecklistRequested = true;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GroupingReviewViewModel.Evidence)) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(SelectRequestedChecklist)); };
        Loaded += (_, _) => SelectRequestedChecklist();
        SelectRequestedChecklist();
    }

    private void SelectRequestedChecklist()
    {
        if (!familyChecklistRequested) return;
        var target = evidenceTabs.Items.OfType<TabItem>().FirstOrDefault(tab => string.Equals(tab.Header as string, "Family checklist", StringComparison.Ordinal));
        if (target != null && target.Visibility == Visibility.Visible && model.Evidence != null)
        { evidenceTabs.SelectedItem = target; familyChecklistRequested = false; }
    }
}
