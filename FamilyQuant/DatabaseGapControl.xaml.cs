using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;

namespace FamilyQuant;

public partial class DatabaseGapControl : UserControl
{
    public DatabaseGapViewModel ViewModel { get; }
    public DatabaseGapControl(DatabaseGapViewModel model)
    {
        ViewModel = model; InitializeComponent(); DataContext = model;
        PropertyChangedEventManager.AddHandler(model, ModelChanged, string.Empty);
        RefreshResult();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    { if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(ViewModel.MinimumTagLength)) RefreshResult(); }
    private void RefreshResult()
    {
        TextBlockEmpty.Visibility = ViewModel.HasData ? Visibility.Collapsed : Visibility.Visible;
        ChartHost.Content = ViewModel.HasData ? new DatabaseGapChart(ViewModel.ChartRows) : null;
    }
}
