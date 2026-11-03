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
    public NumericUpDown ThresholdEditor { get; }
    public bool Accepted { get; private set; }
    public const string HelpText = "Choose the sections and figures to include in the report. All parts are selected by default. "
        + "Clearing a section omits that section and everything inside it. Clearing a figure omits only that figure. "
        + "Figure choices are retained when their section is cleared, and choices for currently unavailable parts remain stored. "
        + "Apply your choices and save the project to keep them for the next export. Cancel discards the checkbox draft. "
        + "The report threshold is a percentage of assigned signal, stored as a fraction from zero to one. When the composition study is available, families with a mean share below it "
        + "are grouped under Others, as are families marked Report under others. The default is 0.1 percent. "
        + "Report details hold the study description and your written interpretation. File > Export report writes a Word document from stored Results; "
        + "compute Results first when they are missing. Figures use the Light export palette. The shared wait screen provides Cancel before publication, "
        + "keeping the previous report file. Exporting does not save the project; use File > Save to keep changed details, choices and threshold."
        + "\n\n" + PanelReportHelp.Report;

    public ReportExportWindow(ReportExportViewModel model, Action<ReportSelection, double> apply, string actionText = "Save choices")
    {
        ViewModel = model; Title = "Export report"; Width = 720; Height = 700; MinWidth = 480; MinHeight = 400;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SecondaryWindowView.Theme(this);
        var layout = new DockPanel();
        var heading = SecondaryWindowView.Text(Title); heading.SetResourceReference(StyleProperty, "PageTitle");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var explanation = SecondaryWindowView.Text("Choose the sections and figures to include.", true);
        DockPanel.SetDock(explanation, Dock.Top); layout.Children.Add(explanation);
        ThresholdEditor = new NumericUpDown { Minimum = 0m, Maximum = 100m, Increment = 0.1m, Width = 120,
            ToolTip = "Mean assigned-signal percentage below which families are grouped under Others. The complete list stays in the supplement." };
        ThresholdEditor.SetBinding(NumericUpDown.ValueProperty, new Binding(nameof(ReportExportViewModel.ThresholdPercent))
            { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var thresholdRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        thresholdRow.Children.Add(new Label { Content = "Report threshold (%)", Target = ThresholdEditor, VerticalAlignment = VerticalAlignment.Center });
        thresholdRow.Children.Add(ThresholdEditor);
        DockPanel.SetDock(thresholdRow, Dock.Top); layout.Children.Add(thresholdRow);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(SecondaryWindowView.Help(Title, HelpText, this));
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 8, 0), ToolTip = "Discard the checkbox draft." };
        cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel);
        SaveButton = new Button { Content = actionText, IsDefault = true, ToolTip = "Apply these choices to the open project." };
        SaveButton.SetResourceReference(StyleProperty, "PrimaryButton");
        SaveButton.Click += (_, _) =>
        {
            if (!model.TryThreshold(out double threshold))
            {
                ShellMessages.Report(this, "Enter a report threshold from 0 to 100 percent.", Title);
                return;
            }
            apply(model.Selection(), threshold); Accepted = true; Close();
        }; buttons.Children.Add(SaveButton);
        DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        var choices = new StackPanel();
        foreach (var choice in model.Choices)
        {
            var checkbox = new CheckBox { DataContext = choice, Tag = choice.Id, Margin = new Thickness(choice.IsFigure ? 24 : 0, 5, 0, 5),
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
