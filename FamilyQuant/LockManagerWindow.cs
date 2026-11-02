using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class LockManagerWindow : Window
{
    private readonly DataGrid lockGrid;
    private readonly Button removeButton;
    public LockManagerViewModel ViewModel { get; }
    public IReadOnlyList<LockedFamilyRow> Rows => ViewModel.Rows;
    public LockManagerWindow(FamilyClusterFile familyClusterFile, Action? onLocksChanged = null, Action? onBeforeChange = null)
    {
        ViewModel = new(familyClusterFile, onLocksChanged, onBeforeChange);
        DataContext = ViewModel;
        Title = "Curation locks"; Width = 900; Height = 560; MinWidth = 520; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SecondaryWindowView.Theme(this);
        var view = CreateView();
        lockGrid = view.Table; removeButton = view.Remove;
        Content = SecondaryWindowView.Frame(view.Layout);
    }
    public void SelectFamilies(params int[] userIds)
    {
        lockGrid.SelectedItems.Clear();
        foreach (var row in Rows.Where(r => userIds.Contains(r.UserId))) lockGrid.SelectedItems.Add(row);
        removeButton.IsEnabled = lockGrid.SelectedItems.Count > 0;
    }
    public int RemoveSelectedLocks()
    {
        int count = ViewModel.Remove(lockGrid.SelectedItems.OfType<LockedFamilyRow>().Select(r => r.UserId));
        lockGrid.ItemsSource = Rows;
        removeButton.IsEnabled = lockGrid.SelectedItems.Count > 0;
        return count;
    }
    public void ShowFor(Window? owner)
    {
        if (!ShellDialogs.IsDisplayed(owner))
        {
            ShellMessages.Report(owner, "Lock management needs a displayed application window.", "Curation locks", showDialog: false);
            return;
        }
        if (owner != null && owner.IsLoaded) Owner = owner;
        ShowDialog();
    }
}
