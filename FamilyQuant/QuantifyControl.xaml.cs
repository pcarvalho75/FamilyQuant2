using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class QuantifyControl : UserControl
{
    public ComputeXicViewModel ViewModel { get; }
    public event Action<FullXicComputeResult>? Completed;
    public QuantifyControl() : this(new ComputeXicViewModel(AppVersionInfo.Current())) { }
    public QuantifyControl(ComputeXicViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        FilterControl.Attach(viewModel.Filters);
        viewModel.Completed += result => Completed?.Invoke(result);
    }
    private bool CanShowDialog()
    {
        if (PresentationSource.FromVisual(this) != null) return true;
        ViewModel.ShowStatus("Type the path into its box. File dialogs need a displayed application window.");
        return false;
    }
    private void BrowseRaw(object sender, RoutedEventArgs e)
    {
        if (!CanShowDialog()) return;
        var dialog = new OpenFolderDialog { Title = "Choose the RAW folder" };
        if (dialog.ShowDialog() == true) ViewModel.RawDirectory = dialog.FolderName;
    }
    private void BrowseSepro(object sender, RoutedEventArgs e)
    {
        if (!CanShowDialog()) return;
        var dialog = new OpenFileDialog { Title = "Choose a trusted SEPro package", Filter = "SEPro package (*.sepr2)|*.sepr2" };
        if (dialog.ShowDialog() == true) ViewModel.SeproPackage = dialog.FileName;
    }
    private void BrowseDeNovo(object sender, RoutedEventArgs e)
    {
        if (!CanShowDialog()) return;
        var dialog = new OpenFolderDialog { Title = "Choose the de novo export root" };
        if (dialog.ShowDialog() == true) ViewModel.DeNovoDirectory = dialog.FolderName;
    }
    private void ViewSpectrum(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Result is not { } result || !CanShowDialog()) return;
        SpectrumComparisonWindow.Show(result.SpectrumComparisonRows, Window.GetWindow(this), result.SpectrumComparisonCsvPath,
            result.Manifest.DeNovoAcceptedScoreScatter);
    }
}
