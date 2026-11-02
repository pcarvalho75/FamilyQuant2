using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FamilyQuant;

public partial class LockManagerWindow
{
    private (Grid Layout, DataGrid Table, Button Remove) CreateView()
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.Children.Add(SecondaryWindowView.Text("Locks protect families from reclustering. Remove a lock here, then save the project to keep the change.", true));
        var table = new DataGrid { ItemsSource = Rows, AutoGenerateColumns = false, IsReadOnly = true,
            CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column,
            ToolTip = "One read-only row per locked family. Select rows with Ctrl or Shift, then Remove selected locks." };
        void Column(string header, string property, DataGridLength width, string tip) => table.Columns.Add(new DataGridTextColumn
        { Header = header, Binding = new Binding(property), Width = width, HeaderStyle = SecondaryWindowView.HeaderTip(tip),
            ElementStyle = SecondaryWindowView.CellText() });
        Column("Family number", nameof(LockedFamilyRow.UserId), new(130), "The locked User ID. Automatic reclustering never merges it while this lock exists.");
        Column("Proteins", nameof(LockedFamilyRow.Members), new(100), "Proteins carrying this family number now. Zero means the number is no longer in use.");
        Column("Still in use", nameof(LockedFamilyRow.Presence), new(140), "A merge can retire a family number; its lock stays and applies if another family adopts that number.");
        Column("Description", nameof(LockedFamilyRow.Description), new(1, DataGridLengthUnitType.Star), "User Annotation, or the most common protein description. Empty when no protein carries the number.");
        table.Columns[^1].MinWidth = 160;
        Grid.SetRow(table, 1); layout.Children.Add(table);
        var bar = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        bar.Children.Add(SecondaryWindowView.Help("Curation locks help", LockManagerViewModel.Explanation, this));
        var close = new Button { Content = "Close", IsCancel = true, ToolTip = "Close this window; removed locks are already removed from the open file. Save from the main window to keep lock changes on disk." };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); bar.Children.Add(close);
        var remove = new Button { Content = "Remove selected locks", IsEnabled = false,
            ToolTip = "Unlock selected families for future reclustering without saving to disk. Disabled until a row is selected." };
        remove.SetResourceReference(StyleProperty, "PrimaryButton");
        remove.Click += (_, _) => RemoveSelectedLocks(); DockPanel.SetDock(remove, Dock.Right); bar.Children.Add(remove);
        table.SelectionChanged += (_, _) => remove.IsEnabled = table.SelectedItems.Count > 0;
        Grid.SetRow(bar, 2); layout.Children.Add(bar);
        return (layout, table, remove);
    }
}
