using FamilyQuant.Report;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace FamilyQuant;

public sealed class ReportExportWindow : Window
{
    public ReportExportViewModel ViewModel { get; }
    public Button SaveButton { get; }
    public bool Accepted { get; private set; }
    public const string HelpText = "Choose the sections and figures to include in the report. All parts are selected by default. "
        + "Clearing a section omits that section and everything inside it. Clearing a figure omits only that figure. "
        + "Figure choices are retained when their section is cleared, and choices for currently unavailable parts remain stored. "
        + "Apply your choices and save the project to keep them for the next export. Cancel discards the checkbox draft. "
        + "Report details hold the study description and your written interpretation. Reports use stored Results; compute Results first when they are missing.";

    public ReportExportWindow(ReportExportViewModel model, Action<ReportSelection> apply, string actionText = "Save choices")
    {
        ViewModel = model; Title = "Export report"; Width = 720; Height = 700; MinWidth = 480; MinHeight = 400;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SecondaryWindowView.Theme(this);
        var layout = new DockPanel();
        var heading = SecondaryWindowView.Text(Title); heading.SetResourceReference(StyleProperty, "PageTitle");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var explanation = SecondaryWindowView.Text("Choose the sections and figures to include.", true);
        DockPanel.SetDock(explanation, Dock.Top); layout.Children.Add(explanation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(SecondaryWindowView.Help(Title, HelpText, this));
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 8, 0), ToolTip = "Discard the checkbox draft." };
        cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel);
        SaveButton = new Button { Content = actionText, IsDefault = true, ToolTip = "Apply these choices to the open project." };
        SaveButton.SetResourceReference(StyleProperty, "PrimaryButton");
        SaveButton.Click += (_, _) => { apply(model.Selection()); Accepted = true; Close(); }; buttons.Children.Add(SaveButton);
        DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        var choices = new StackPanel();
        foreach (var choice in model.Choices)
        {
            var checkbox = new CheckBox { DataContext = choice, Margin = new Thickness(choice.IsFigure ? 24 : 0, 5, 0, 5),
                Content = new TextBlock { Text = choice.Label, TextWrapping = TextWrapping.Wrap },
                ToolTip = choice.IsFigure ? "Include this figure when its section is included." : "Include this section and its selected figures." };
            checkbox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(ReportChoice.IsIncluded)) { Mode = BindingMode.TwoWay });
            choices.Children.Add(checkbox);
        }
        layout.Children.Add(new ScrollViewer { Content = choices, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = SecondaryWindowView.Frame(layout);
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (_, _) => TextReportWindow.Show(Title, HelpText, this, true)));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Help, Key.F1, ModifierKeys.None));
    }
}
