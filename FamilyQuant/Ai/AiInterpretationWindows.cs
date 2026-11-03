using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Ai;

public sealed class AiPreviewWindow : Window
{
    public TextBox InputText { get; } = new();
    public TextBlock ModelText { get; } = new();
    public Button SendButton { get; } = new();
    public AiPreviewWindow(string input, string model)
    {
        Title = "Review information sent to AI"; Width = 850; Height = 700; MinWidth = 520; MinHeight = 400;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SecondaryWindowView.Theme(this);
        var layout = new DockPanel();
        var heading = new StackPanel();
        heading.Children.Add(SecondaryWindowView.Text("Review everything below before sending it to Anthropic.", true));
        ModelText.Text = "Model: " + model; ModelText.Margin = new Thickness(0, 8, 0, 8); heading.Children.Add(ModelText);
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        SendButton.Content = "Send to AI"; SendButton.IsDefault = false;
        SendButton.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(SendButton); DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        InputText.Text = input; InputText.IsReadOnly = true; InputText.AcceptsReturn = true;
        InputText.TextWrapping = TextWrapping.Wrap; InputText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        InputText.ToolTip = "This is the exact text sent if you choose Send to AI."; layout.Children.Add(InputText);
        Content = SecondaryWindowView.Frame(layout);
    }
}

public sealed class AiDraftReviewWindow : Window
{
    public TextBox DraftText { get; } = new();
    public TextBox Warnings { get; } = new();
    public Button AcceptButton { get; } = new();
    public AiDraftReviewWindow(AiInterpretationDraft draft, IReadOnlyList<string> unmatched)
    {
        Title = "Review AI draft"; Width = 850; Height = 700; MinWidth = 520; MinHeight = 400;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SecondaryWindowView.Theme(this);
        var layout = new DockPanel();
        var heading = SecondaryWindowView.Text("Review the draft and every number before accepting it. Matching a number does not establish its meaning.", true);
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Discard", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        AcceptButton.Content = "Accept draft"; AcceptButton.IsDefault = false;
        AcceptButton.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(AcceptButton); DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        var content = new Grid(); content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        DraftText.Text = draft.Text; DraftText.IsReadOnly = true; DraftText.AcceptsReturn = true;
        DraftText.TextWrapping = TextWrapping.Wrap; DraftText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        content.Children.Add(DraftText);
        Warnings.Text = unmatched.Count == 0 ? "No unmatched numbers were found. Verify the meaning of every claim."
            : "Numbers not found in the program's tables (each occurrence):\n" + string.Join("\n", unmatched);
        Warnings.IsReadOnly = true; Warnings.AcceptsReturn = true; Warnings.TextWrapping = TextWrapping.Wrap;
        Warnings.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; Warnings.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(Warnings, 1); content.Children.Add(Warnings); layout.Children.Add(content);
        Content = SecondaryWindowView.Frame(layout);
    }
}
