using System;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class FamilyListWorkbookOverview : UserControl
{
    public event Action<string>? ReviewRequested;
    public FamilyListWorkbookOverview() => InitializeComponent();
    private void Review_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: string section }) ReviewRequested?.Invoke(section); }
    private void Findings_Click(object sender, RoutedEventArgs e) => WorkbookDetailsTabs.SelectedIndex = WorkbookDetailsTabs.Items.Count - 1;
}
