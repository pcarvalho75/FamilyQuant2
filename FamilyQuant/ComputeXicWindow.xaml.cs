using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Windows;

namespace FamilyQuant;

/// <summary>Compatibility host. The shell uses QuantifyControl directly after C8 integration.</summary>
public partial class ComputeXicWindow : Window
{
    public QuantifyControl Screen { get; }
    public ComputeXicViewModel ViewModel => Screen.ViewModel;
    public List<QuantRow>? ResultRows => ViewModel.Result?.Rows;
    public QuantManifest? ResultManifest => ViewModel.Result?.Manifest;
    public bool Computed => ViewModel.Computed;
    public IReadOnlyList<SpectrumComparisonRow> SpectrumComparisonRows => ViewModel.Result?.SpectrumComparisonRows ?? new();
    public string? SpectrumComparisonCsvPath => ViewModel.Result?.SpectrumComparisonCsvPath;
    public string? SpectrumComparisonCsvError => ViewModel.Result?.SpectrumComparisonCsvError;
    public IReadOnlyList<string> TicShareSummary => ViewModel.Result?.TicShareSummary ?? new();
    public int IsotopeCount => ViewModel.IsotopeCount;
    public int MinPointsForQuant => ViewModel.MinPointsForQuant;

    public ComputeXicWindow(string appVersion, QuantManifest? existingManifest = null, FamilyClusterFile? familyCuration = null,
        DeNovoFilterViewModel? filters = null)
    {
        InitializeComponent();
        Screen = new QuantifyControl(new ComputeXicViewModel(appVersion, existingManifest, familyCuration, filters));
        ScreenHost.Content = Screen;
        OperationOverlay.DataContext = ViewModel.Operations;
        Closing += (_, e) => { if (ViewModel.IsBusy) { e.Cancel = true; ViewModel.CancelCommand.Execute(null); } };
    }

    private void ButtonClose_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = true; }
        catch (InvalidOperationException) { Close(); }
    }
}
