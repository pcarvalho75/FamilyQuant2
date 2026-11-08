using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public sealed class BroadEvidenceMatchWindow : Window
{
    public Button KeepButton { get; } = new() { Content = "Keep matches", IsDefault = true };
    public BroadEvidenceMatchWindow(string message)
    {
        Title = "Confirm broad evidence matches"; Width = 600; SizeToContent = SizeToContent.Height;
        MaxHeight = 700; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SecondaryWindowView.Theme(this);
        KeepButton.ToolTip = "Confirm these broad labels for the panel draft. Apply panel keeps the draft as one change with Undo.";
        KeepButton.SetResourceReference(StyleProperty, "PrimaryButton"); KeepButton.Click += (_, _) => DialogResult = true;
        var back = new Button { Content = "Back", IsCancel = true, ToolTip = "Return to matching with every checkbox unchanged." };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(back); buttons.Children.Add(KeepButton);
        var content = new DockPanel(); DockPanel.SetDock(buttons, Dock.Bottom); content.Children.Add(buttons);
        content.Children.Add(new ScrollViewer { Content = SecondaryWindowView.Text(message), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = SecondaryWindowView.Frame(content);
    }
}
