using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class DatabaseGapControl : UserControl
{
    public DatabaseGapViewModel ViewModel { get; }
    public DatabaseGapControl(DatabaseGapViewModel model)
    {
        ViewModel = model; InitializeComponent(); DataContext = model;
        TextBlockEmpty.Visibility = model.HasData ? Visibility.Collapsed : Visibility.Visible;
        if (model.HasData) ChartHost.Content = new DatabaseGapChart(model.ChartRows);
    }
}
