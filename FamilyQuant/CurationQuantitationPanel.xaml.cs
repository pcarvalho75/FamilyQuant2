using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class CurationQuantitationPanel : UserControl
{
    public event RoutedEventHandler? LoadRequested;
    public event RoutedEventHandler? RecalculateRequested;
    public event SelectionChangedEventHandler? ChoiceChanged;
    public event SelectionChangedEventHandler? BasisChanged;
    public CurationQuantitationPanel()
    {
        InitializeComponent();
        DataContextChanged += ContextChanged;
        UpdatePresentation();
    }
    private void ContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CurationQuantitationViewModel previous)
            PropertyChangedEventManager.RemoveHandler(previous, ModelChanged, string.Empty);
        if (e.NewValue is CurationQuantitationViewModel current)
            PropertyChangedEventManager.AddHandler(current, ModelChanged, string.Empty);
        UpdatePresentation();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e) => UpdatePresentation();
    private void UpdatePresentation()
    {
        var model = DataContext as CurationQuantitationViewModel;
        bool full = model?.HasFullXic == true, plp = model?.HasPatternLab == true;
        bool pending = model?.IsPending == true, failed = model?.HasFullXicError == true;
        static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
        FullXicSourceState.Text = model?.FullXicStateLabel ?? "Not computed";
        EmptyInstructions.Visibility = Show(!full && !plp && !pending && !failed);
        FullXicOptions.Visibility = Show(full && !pending);
        SharedOptions.Visibility = Show(full || plp);
        PatternLabOptions.Visibility = Show(plp);
        ButtonRecalculateQuantitation.Visibility = Show(plp);
        ButtonRecalculateQuantitation.IsEnabled = plp;
        if (plp && TextBlockPLPStatus.Text == "No PatternLab project loaded")
            TextBlockPLPStatus.Text = "PatternLab project loaded";
        if (!plp) TextBlockPLPStatus.Text = "No PatternLab project loaded";
        IconPLPStatus.Visibility = Show(plp);
        BadgeLayer3.Visibility = Show(full || plp || pending);
        FullXicDetails.Visibility = Show(full || pending || failed);
        FullXicFailureText.Visibility = Show(failed);
        FullXicFailureText.Text = model?.FullXicFailureReason ?? string.Empty;
        TicRefusalHint.Visibility = Show(full && model?.CanUseTic != true);
        TicRefusalHint.Text = model?.TicHelp ?? string.Empty;
        if (failed) FullXicDetails.IsExpanded = true;
    }
    private void Load_Click(object sender, RoutedEventArgs e) => LoadRequested?.Invoke(sender, e);
    private void Recalculate_Click(object sender, RoutedEventArgs e) => RecalculateRequested?.Invoke(sender, e);
    private void Choice_Changed(object sender, SelectionChangedEventArgs e) => ChoiceChanged?.Invoke(sender, e);
    private void Basis_Changed(object sender, SelectionChangedEventArgs e) => BasisChanged?.Invoke(sender, e);
}
