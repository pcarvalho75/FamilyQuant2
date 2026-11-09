using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        SensitivityHost.Content = new FamilySensitivityControl(ViewModel.Sensitivity);
        DatabaseGapHost.Content = ViewModel.DatabaseGap.HasStoredResult ? new DatabaseGapControl(ViewModel.DatabaseGap)
            : new TextBlock { Text = ViewModel.DatabaseGap.EmptyMessage, TextWrapping = TextWrapping.Wrap };
        foreach (ComboBoxItem item in ComboBoxExportKind.Items)
            if (string.Equals(item.Tag as string, ViewModel.ExportKind.ToString(), StringComparison.Ordinal)) ComboBoxExportKind.SelectedItem = item;
        TextBlockDetails.Text = ViewModel.Details;
        BorderEmpty.Visibility = Data == null && !ViewModel.DatabaseGap.HasStoredResult ? Visibility.Visible : Visibility.Collapsed;
        ComboBoxFamilyTopN.IsEnabled = ViewModel.HasData;
        ButtonExport.IsEnabled = ViewModel.CanExport;
        RefreshExportHint();
        ViewModel.PropertyChanged += (_, e) =>
        {
            TextBlockStatus.Text = ViewModel.Status;
            ButtonExport.IsEnabled = ViewModel.CanExport;
            RefreshExportHint();
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
    }
    private void DrawCharts()
    {
        PanelFamilyChart.Children.Clear();
        PanelCharts.Children.Clear();
        if (Data == null) return;
        foreach (var figure in AnalyticsFigures.Build(Data, Families, FamilyTopN, FamilyAsPercent)
            .Concat(ViewModel.BuildCompositionFigures()))
        {
            FrameworkElement plotted = figure.Chart is FamilyContributionChart
                ? new FamilyContributionChart(Families, FamilyTopN, FamilyAsPercent, sharedPolicy: ViewModel.FamilySharedPolicy)
                : figure.Chart;
            if (plotted is IPalettedChart chart) chart.Palette = ChartPalette.UiPalette;
            string text = CompositionChartCaptions.Build(figure, Data);
            var caption = new TextBlock { Text = text,
                ToolTip = "This line explains the chart and its recorded numbers. The complete explanation is included in the report.",
                Tag = "figure-caption:" + figure.FileName,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var group = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            group.Children.Add(new Border { Height = figure.ScreenHeight, Child = plotted });
            group.Children.Add(caption);
            if (plotted is FamilyContributionChart) PanelFamilyChart.Children.Add(group);
            else PanelCharts.Children.Add(group);
        }
    }
    private void ExportChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready && Enum.TryParse((ComboBoxExportKind.SelectedItem as ComboBoxItem)?.Tag as string, out ResultsExportKind kind))
            ViewModel.ExportKind = kind;
    }
    private void RefreshExportHint() => ButtonExport.ToolTip = !ViewModel.CanExport ? ViewModel.ExportKind switch {
        ResultsExportKind.Homology => "Export is disabled because no standalone homology snapshot is available. Run Search in De novo review to enable it; no file or project value changes while disabled.",
        ResultsExportKind.DatabaseGap => "Export is disabled because the selected tag length has no stored Database gap table. Choose a computed length in a project with stored family-world results to enable it.",
        ResultsExportKind.SuggestedAdditions => "Export is disabled without credited world proteins and their verified matching stored sequences. Load a project with that complete evidence and choose a computed tag length to enable it.",
        _ => "Export is disabled without a completed result and the project evidence required by this export. Open or compute those results and required studies to enable it; no file is written while disabled."
    } : ViewModel.ExportKind switch {
        ResultsExportKind.SuggestedAdditions => "Write exact recorded world-hit sequences to a suggested-additions FASTA without editing your database. Undo does not remove the file; review additions and search again in PatternLab yourself.",
        ResultsExportKind.DatabaseGap => "Write the selected stored Database gap table, all global buckets and recorded rules to CSV without changing the project. Undo does not remove this file.",
        ResultsExportKind.Figures => "Write stored-result PNG figures to the chosen folder, with standard figures at 1920 by 1080 pixels and complete data beside bounded figures. The project stays unchanged and Undo does not remove the files.",
        ResultsExportKind.EvidencePack => "Write figures, exact CSV and JSON values and the cutoff README when all required studies are present. The project stays unchanged and Undo does not remove the files.",
        _ => "Write the selected stored table to CSV with a decimal point for numeric values. The project stays unchanged and Undo does not remove the file." };
    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanExport) return;
        if (PresentationSource.FromVisual(this) == null)
        { ViewModel.ShowStatus("Nothing was exported, because this screen is not on a display. Export dialogs need an open application window; " +
            "open the complete project in the application to export from Results."); return; }
        string? path;
        if (ViewModel.ExportKind is ResultsExportKind.Figures or ResultsExportKind.EvidencePack)
        {
            var dialog = new OpenFolderDialog { Title = ViewModel.ExportKind == ResultsExportKind.Figures
                ? "Choose the folder for screen figures" : "Choose the folder for the presentation evidence pack" };
            path = ShellDialogs.ShowFolder(this, "results." + ViewModel.ExportKind, dialog) ? dialog.FolderName : null;
        }
        else
        {
            bool fasta = ViewModel.ExportKind == ResultsExportKind.SuggestedAdditions;
            var dialog = new SaveFileDialog { Filter = fasta ? "FASTA files (*.fasta)|*.fasta|All files (*.*)|*.*" : FileDialogFilters.Csv, AddExtension = true,
                DefaultExt = fasta ? ".fasta" : ".csv", FileName = ViewModel.ExportKind switch {
                    ResultsExportKind.Summary => "familyquant_analytics_summary.csv",
                    ResultsExportKind.Homology => "FamilyQuant_DeNovo_Homology.csv",
                    ResultsExportKind.DatabaseGap => "familyquant_database_gap.csv",
                    ResultsExportKind.SuggestedAdditions => "familyquant_suggested_additions.fasta",
                    _ => "familyquant_analytics_peptides.csv" }, Title = ViewModel.ExportKind switch {
                        ResultsExportKind.Peptides => "Export peptide table", ResultsExportKind.Summary => "Export run summary", _ => "Export results" } };
            path = ShellDialogs.ShowFile(this, "results." + ViewModel.ExportKind, dialog) ? dialog.FileName : null;
        }
        if (path != null) await ViewModel.ExportAsync(path, AppOperationHost.For(this));
    }
}
