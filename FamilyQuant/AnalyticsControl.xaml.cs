using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class AnalyticsControl : UserControl
{
    private bool ready;
    public AnalyticsViewModel ViewModel { get; }
    public AnalyticsData? Data => ViewModel.Data;
    public FamilyEnvelopeQuantResult? Families => ViewModel.Families;
    public int FamilyTopN => ViewModel.FamilyTopN;
    public bool FamilyAsPercent => ViewModel.FamilyAsPercent;
    public AnalyticsControl(IReadOnlyList<QuantRow>? rows, QuantManifest? manifest) : this(rows, manifest, null) { }
    public AnalyticsControl(IReadOnlyList<QuantRow>? rows, QuantManifest? manifest, FamilyClusterFile? curation)
        : this(rows, manifest, curation, null) { }
    public AnalyticsControl(IReadOnlyList<QuantRow>? rows, QuantManifest? manifest, FamilyClusterFile? curation,
        PeptideFamilyAttribution? attribution, DeNovoHomology? homology = null)
        : this(new AnalyticsViewModel(rows, manifest, curation, attribution, homology)) { }
    public AnalyticsControl(AnalyticsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        foreach (ComboBoxItem item in ComboBoxExportKind.Items)
            if (string.Equals(item.Tag as string, ViewModel.ExportKind.ToString(), StringComparison.Ordinal)) ComboBoxExportKind.SelectedItem = item;
        TextBlockDetails.Text = ViewModel.Details;
        BorderEmpty.Visibility = Data == null ? Visibility.Visible : Visibility.Collapsed;
        ComboBoxFamilyTopN.IsEnabled = CheckBoxFamilyPercent.IsEnabled = ViewModel.HasData;
        ButtonExport.IsEnabled = ViewModel.CanExport;
        ViewModel.PropertyChanged += (_, e) =>
        {
            TextBlockStatus.Text = ViewModel.Status;
            ButtonExport.IsEnabled = ViewModel.CanExport;
            if (e.PropertyName is nameof(ViewModel.FamilyTopN) or nameof(ViewModel.FamilyAsPercent)) DrawCharts();
        };
        TextBlockStatus.Text = ViewModel.Status;
        ready = true;
        DrawCharts();
    }
    private void ChartChoiceChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        ViewModel.FamilyTopN = int.TryParse((ComboBoxFamilyTopN.SelectedItem as ComboBoxItem)?.Tag as string,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int top) ? top : FamilyContributionPlot.DefaultTopN;
        ViewModel.FamilyAsPercent = CheckBoxFamilyPercent.IsChecked == true;
    }
    private void DrawCharts()
    {
        PanelCharts.Children.Clear();
        if (Data == null) return;
        foreach (var figure in AnalyticsFigures.Build(Data, Families, FamilyTopN, FamilyAsPercent))
        {
            if (figure.Chart is IPalettedChart chart) chart.Palette = ChartPalette.For(Themes.ThemeManager.Effective == Themes.AppTheme.Light
                ? ChartPaletteVariant.Light : ChartPaletteVariant.Dark);
            PanelCharts.Children.Add(new Border { Height = figure.ScreenHeight, Margin = new Thickness(0, 0, 0, 12), Child = figure.Chart });
        }
    }
    private void ExportChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready && Enum.TryParse((ComboBoxExportKind.SelectedItem as ComboBoxItem)?.Tag as string, out ResultsExportKind kind))
            ViewModel.ExportKind = kind;
    }
    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanExport) return;
        if (PresentationSource.FromVisual(this) == null)
        { ViewModel.ShowStatus("Nothing was exported, because this screen is not on a display. Open the project in the application to export."); return; }
        string? path;
        if (ViewModel.ExportKind is ResultsExportKind.Figures or ResultsExportKind.EvidencePack)
        {
            var dialog = new OpenFolderDialog { Title = "Choose the export folder" };
            path = ShellDialogs.ShowFolder(this, "results." + ViewModel.ExportKind, dialog) ? dialog.FolderName : null;
        }
        else
        {
            var dialog = new SaveFileDialog { Filter = FileDialogFilters.Csv, AddExtension = true,
                DefaultExt = ".csv", FileName = ViewModel.ExportKind switch {
                    ResultsExportKind.Summary => "familyquant_analytics_summary.csv",
                    ResultsExportKind.Homology => "FamilyQuant_DeNovo_Homology.csv",
                    _ => "familyquant_analytics_peptides.csv" }, Title = "Export results" };
            path = ShellDialogs.ShowFile(this, "results." + ViewModel.ExportKind, dialog) ? dialog.FileName : null;
        }
        if (path != null) await ViewModel.ExportAsync(path, AppOperationHost.For(this));
    }
}
