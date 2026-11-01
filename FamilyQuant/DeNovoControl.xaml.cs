using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using Microsoft.Win32;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using System.Windows.Data;

namespace FamilyQuant;

public partial class DeNovoControl : UserControl
{
    private bool ready;
    private DeNovoHomology? publishedHomology;
    public DeNovoViewModel ViewModel { get; }
    public event Action<DeNovoHomology>? HomologyCompleted;
    public DeNovoControl() : this(new DeNovoFilterViewModel()) { }
    public DeNovoControl(DeNovoFilterViewModel filters)
    {
        ViewModel = new(filters);
        ViewModel.Progress = new ScreenProgress(message => Dispatcher.BeginInvoke(new Action(() =>
        { if (ViewModel.IsBusy) ViewModel.ShowStatus(message); })));
        InitializeComponent();
        DataContext = ViewModel;
        ScreenBindings.Attach(this, ViewModel);
        ApplyReviewColumnTooltips(); ApplyHomologyColumnTooltips();
        ViewModel.PropertyChanged += (_, _) => RefreshPresentation();
        SelectSource();
        ready = true;
        UpdateSourceUi();
        RefreshPresentation();
    }
    private void SetStatus(string message) => ViewModel.ShowStatus(message);
    private void RefreshPresentation()
    {
        bool wasReady = ready;
        ready = false; SelectSource(); ready = wasReady;
        UpdateSourceUi();
        TextBlockDeNovoStatus.Text = ViewModel.Status;
        foreach (var group in new[] { GroupBoxImportAndFilter, GroupBoxReconcile, GroupBoxHomology })
            group.IsEnabled = ViewModel.CanEdit;
        UpdateLayerStates();
        if (ViewModel.LastHomology is { } homology && !ReferenceEquals(publishedHomology, homology))
        {
            publishedHomology = homology;
            HomologyCompleted?.Invoke(homology);
        }
        else if (ViewModel.LastHomology == null) publishedHomology = null;
    }
    private void UpdateLayerStates()
    {
        if (!ready) return;
        TextBlockDeNovoLayer1State.Text = "READY";
        TextBlockDeNovoLayer2State.Text = ViewModel.ReconcileState;
        TextBlockDeNovoLayer4State.Text = ViewModel.HomologyState;
        ButtonReconcile.IsEnabled = ViewModel.CanReconcile;
        ButtonExportSummary.IsEnabled = ButtonExportReviewQueue.IsEnabled = ButtonExportOverlapStudy.IsEnabled = LastReconciliation != null;
        ButtonSearchHomology.IsEnabled = ViewModel.CanSearch;
    }
    private void SelectSource()
    {
        string id = ViewModel.Filters.ModeTag.StartsWith("union", StringComparison.Ordinal)
            ? ViewModel.Filters.ModeTag : ViewModel.ActiveDeNovoSource.Id;
        foreach (ComboBoxItem item in ComboBoxDeNovoSource.Items)
            if (string.Equals(item.Tag as string, id, StringComparison.Ordinal)) { ComboBoxDeNovoSource.SelectedItem = item; break; }
    }
    private void ComboBoxDeNovoSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        string id = (ComboBoxDeNovoSource.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        ViewModel.Filters.ModeTag = id.StartsWith("union", StringComparison.Ordinal) ? id
            : id == DeNovoSource.DeepNovo.Id ? "deepnovo" : id == DeNovoSource.DeNovoAll.Id ? "peaks-all" : "peaks-only";
        UpdateSourceUi();
    }
    private void TextBoxPeaksDirectory_TextChanged(object sender, TextChangedEventArgs e) { if (ready) ViewModel.SourcePathChanged(); }

    private async void ButtonLoadAndFilter_Click(object sender, RoutedEventArgs e) { ViewModel.ImportOverride = ImportOverride; await ViewModel.ImportAsync(); }
    private async void ButtonReconcile_Click(object sender, RoutedEventArgs e) => await ViewModel.ReconcileAsync();

    private async void ButtonSearchHomology_Click(object sender, RoutedEventArgs e) => await ViewModel.SearchAsync();

        private void UpdateSourceUi()
        {
            if (LabelMinimumSourceConfidence == null || TextBlockNativeConfidence == null) return;
            DeNovoSource source = ActiveDeNovoSource;
            bool union = ViewModel.Filters.ModeTag.StartsWith("union", StringComparison.Ordinal);
            LabelDeepNovoCutoff.Visibility = TextBoxDeepNovoCutoff.Visibility = union ? Visibility.Visible : Visibility.Collapsed;
            string shortField = source.ConfidenceColumn.Replace(" (%)", string.Empty,
                StringComparison.OrdinalIgnoreCase);
            LabelMinimumSourceConfidence.Content = "Minimum " + shortField + " (%):";
            LabelMinimumSourceConfidence.ToolTip = "Lowest " + source.ConfidenceColumn +
                " kept for " + source.DisplayName + ". This score belongs to this program and is " +
                "not converted to any other program's scale.";
            TextBoxMinAlc.ToolTip = "A number from 0 to 100, decimals allowed. Spectra with a lower " +
                source.ConfidenceColumn + " are dropped. " +
                (source == DeNovoSource.DeepNovo
                    ? "DeepNovo SCORE is not PEAKS ALC."
                    : "PEAKS ALC is not a calibrated FDR.");
            TextBlockNativeConfidence.Text = "Native confidence: " + source.ConfidenceColumn;
            ButtonLoadAndFilter.ToolTip = "Reads every " + source.FileGlob + " file in the folder and " +
                "keeps the rows that pass the length, " + source.ConfidenceColumn + " and stretch filters.";

            if (ColumnQueueAlc != null) ColumnQueueAlc.Header = union ? "Native confidence (%)" : source.ConfidenceColumn;
            if (ColumnQueueAlc != null) ColumnQueueAlc.Width = new DataGridLength(union ? 180 : 110);
            ColumnQueueSource.Visibility = union || source == DeNovoSource.DeepNovo ? Visibility.Visible : Visibility.Collapsed;
            if (union)
            {
                LabelMinimumSourceConfidence.Content = "PEAKS minimum ALC (%):";
                TextBlockNativeConfidence.Text = "Each source keeps its native confidence";
                ButtonLoadAndFilter.ToolTip = "Reads every selected source with its own cutoff. These accepted spectrum-call counts precede quantitation and union resolution.";
            }
            ApplyReviewColumnTooltips();

        }
}
