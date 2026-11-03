using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class FamilySensitivityControl : UserControl
{
    public FamilySensitivityViewModel ViewModel { get; }
    public FamilySensitivityControl(FamilySensitivityViewModel model)
    {
        ViewModel = model; InitializeComponent(); DataContext = model;
        PropertyChangedEventManager.AddHandler(model, Changed, string.Empty); RefreshHeaders();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) => RefreshHeaders();
    private void RefreshHeaders() { FirstColumn.Header = ViewModel.FirstHeader; SecondColumn.Header = ViewModel.SecondHeader; }
    private async void CalculateClicked(object sender, RoutedEventArgs args) => await ViewModel.CalculateAsync(null, AppOperationHost.For(this));
    private async void ComparisonClicked(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog { Filter = "FamilyQuant projects (*.fqproj)|*.fqproj", Title = "Choose a computed comparison project" };
        if (ShellDialogs.ShowFile(this, "sensitivity.comparison", dialog)) await ViewModel.CalculateAsync(dialog.FileName, AppOperationHost.For(this));
    }
    private async void ExportClicked(object sender, RoutedEventArgs args)
    {
        var dialog = new SaveFileDialog { Filter = FileDialogFilters.Csv, DefaultExt = ".csv", AddExtension = true,
            FileName = "familyquant_sensitivity.csv", Title = "Export sensitivity table" };
        if (!ShellDialogs.ShowFile(this, "sensitivity.export", dialog)) return;
        var host = AppOperationHost.For(this);
        if (host.IsBusy) return;
        using var operation = host.Begin("Export sensitivity table");
        try
        {
            string text = ViewModel.BuildCsv();
            await AppOperationFiles.RunSta(() => AppOperationFiles.WriteFile(dialog.FileName, path => File.WriteAllText(path, text), operation.Context));
            ViewModel.ShowStatus("Written to " + dialog.FileName + ".");
        }
        catch (OperationCanceledException) { ViewModel.ShowStatus("Cancelled. The previous export was kept."); }
        catch (Exception ex) { ViewModel.ShowStatus("The sensitivity export did not finish: " + ex.Message); }
    }
}
