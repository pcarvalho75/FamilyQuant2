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
        Loaded += (_, _) => ViewModel.Operations = AppOperationHost.BindWindow(this, ViewModel.Operations);
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
        var dialog = new OpenFolderDialog { Title = "Choose the folder containing the RAW acquisitions" };
        if (ShellDialogs.ShowFolder(this, "quantify.raw", dialog)) ViewModel.RawDirectory = dialog.FolderName;
        else ViewModel.ShowStatus("No RAW folder was chosen. Type the folder containing the .raw acquisitions into RAW folder.");
    }
    private void BrowseSepro(object sender, RoutedEventArgs e)
    {
        if (!CanShowDialog()) return;
        var dialog = new OpenFileDialog { Title = "Choose a trusted SEPro package (legacy unsafe deserialization)", Filter = "SEPro package (*.sepr2)|*.sepr2" };
        if (ShellDialogs.ShowFile(this, "quantify.sepro", dialog)) ViewModel.SeproPackage = dialog.FileName;
        else ViewModel.ShowStatus("No SEPro package was chosen. Type the .sepr2 path into Identifications.");
    }
    private void BrowseDeNovo(object sender, RoutedEventArgs e)
    {
        if (!CanShowDialog()) return;
        var dialog = new OpenFolderDialog { Title = "Choose the de novo export root (optional)" };
        if (ShellDialogs.ShowFolder(this, "quantify.denovo", dialog)) ViewModel.DeNovoDirectory = dialog.FolderName;
        else ViewModel.ShowStatus("No de novo folder was chosen. Type the optional export root or leave it empty for identified peptides only.");
    }
    private void ViewSpectrum(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Result is not { } result || !CanShowDialog()) return;
        SpectrumComparisonWindow.Show(result.SpectrumComparisonRows, Window.GetWindow(this), result.SpectrumComparisonCsvPath,
            result.Manifest.DeNovoAcceptedScoreScatter);
    }
}
