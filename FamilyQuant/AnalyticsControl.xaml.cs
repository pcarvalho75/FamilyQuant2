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
        TextBlockDetails.Text = ViewModel.Details;
        BorderEmpty.Visibility = Data == null && !ViewModel.DatabaseGap.HasStoredResult ? Visibility.Visible : Visibility.Collapsed;
        ComboBoxFamilyTopN.IsEnabled = ViewModel.HasData;
        ViewModel.PropertyChanged += (_, e) =>
        {
            TextBlockStatus.Text = ViewModel.Status;
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
    private void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window) window.OpenExportWindow(ViewModel);
        else ViewModel.ShowStatus("Open the project window and use File > Export to choose a report, table or other file.");
    }
}
