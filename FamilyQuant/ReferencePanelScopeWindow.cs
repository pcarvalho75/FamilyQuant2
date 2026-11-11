using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public sealed class ReferencePanelScopeWindow : Window
{
    public TextBox ScopeBox { get; } = new() { MinWidth = 380 };
    public CheckBox FullPanelBox { get; } = new() { Content = "Full panel", Margin = new Thickness(0, 12, 0, 12) };
    public Button FetchButton { get; } = new() { Content = "Fetch panel", IsDefault = true };
    public ReferencePanelRequest? Request { get; private set; }
    public TextBlock Explanation { get; }
    public ReferencePanelScopeWindow(IEnumerable<string> initialScope, bool hasSubclasses = false)
    {
        Title = "Choose reference panel scope"; Width = 600; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SecondaryWindowView.Theme(this);
        ScopeBox.Text = string.Join(", ", initialScope.Select(term => "\"" + term + "\""));
        FullPanelBox.IsChecked = hasSubclasses;
        ScopeBox.ToolTip = "Enter words or quoted phrases to match stored keywords and tissue annotations. Fetch panel downloads their missing entries; this field is disabled while Full panel is selected.";
        FullPanelBox.ToolTip = "Download every reviewed entry for the file's families. This is needed for subclass assignment, validation reports and Fetch UniProt families.";
        FetchButton.ToolTip = "Fetch reviewed entries for the chosen scope and the whole families needed for grouping checks. Enter scope words or choose Full panel to enable this.";
        FetchButton.SetResourceReference(StyleProperty, "PrimaryButton");
        void Validate() { ScopeBox.IsEnabled = FullPanelBox.IsChecked != true; FetchButton.IsEnabled = FullPanelBox.IsChecked == true || WordCluster.ParseKeywords(ScopeBox.Text).Any(term => System.Text.RegularExpressions.Regex.IsMatch(term, @"[\p{L}\p{Nd}]")); }
        ScopeBox.TextChanged += (_, _) => Validate(); FullPanelBox.Checked += (_, _) => Validate(); FullPanelBox.Unchecked += (_, _) => Validate();
        FetchButton.Click += (_, _) => { Request = new(WordCluster.ParseKeywords(ScopeBox.Text), FullPanelBox.IsChecked == true); DialogResult = true; };
        var cancel = new Button { Content = "Cancel", IsCancel = true, ToolTip = "Close without applying staged evidence or fetching a reference panel." };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(FetchButton);
        Explanation = SecondaryWindowView.Text(hasSubclasses ? ReferencePanelUseRequirements.SubclassChoice : "Choose the scope before downloading the reference panel.");
        var content = new StackPanel(); content.Children.Add(Explanation);
        content.Children.Add(new Label { Content = "Scope", Target = ScopeBox }); content.Children.Add(ScopeBox);
        content.Children.Add(FullPanelBox); content.Children.Add(buttons); Content = SecondaryWindowView.Frame(content);
        Validate();
    }
}

public partial class ClusterGenerationControl
{
    public Func<FamilyClusterFile, ReferencePanelRequest?>? ReferencePanelChoiceOverride { get; set; }
    private ReferencePanelRequest? ChooseReferencePanel(FamilyClusterFile snapshot)
    {
        if (ReferencePanelChoiceOverride != null) return ReferencePanelChoiceOverride(snapshot);
        if (FetchDomainsHandlerOverride != null) return new(Array.Empty<string>(), true);
        var initial = snapshot.EvidenceSource?.ReferencePanel?.Coverage?.ScopeTerms ?? snapshot.EvidenceRecipe?.Options.ScopeTerms ?? Array.Empty<string>();
        var dialog = new ReferencePanelScopeWindow(initial, ReferencePanelUseRequirements.HasSubclasses(snapshot.FamilyPanel)) { Owner = Window.GetWindow(this) };
        return dialog.ShowDialog() == true ? dialog.Request : null;
    }
}
