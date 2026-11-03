using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace FamilyQuant;

public sealed class ReportDetailsWindow : Window
{
    public ReportDetailsViewModel ViewModel { get; }
    public Button SaveButton { get; }
    public const string HelpText = "Report details are your own text, saved with the project. Title and authors identify the report; "
        + "sample description, workflow and kind of database describe your study. Written interpretation accepts several paragraphs "
        + "and is printed as written. Base numerical claims on the project's reported results. Save details applies all fields as one "
        + "Undo step. Cancel discards this draft. File > Save keeps the details in the project; report export does not save the project.";

    public ReportDetailsWindow(ReportDetailsViewModel model)
    {
        ViewModel = model; DataContext = model;
        Title = "Report details"; Width = 780; Height = 760; MinWidth = 520; MinHeight = 440;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SecondaryWindowView.Theme(this);
        var layout = new DockPanel();
        var heading = SecondaryWindowView.Text("Report details"); heading.SetResourceReference(StyleProperty, "PageTitle");
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var explanation = SecondaryWindowView.Text("Describe the study and write the interpretation for your report.", true);
        DockPanel.SetDock(explanation, Dock.Top); layout.Children.Add(explanation);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(SecondaryWindowView.Help(Title, HelpText, this));
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 8, 0), ToolTip = "Discard this draft." };
        cancel.Click += (_, _) => Close(); actions.Children.Add(cancel);
        SaveButton = new Button { Content = "Save details", IsDefault = true, ToolTip = "Apply these fields to the open project as one Undo step." };
        SaveButton.SetResourceReference(StyleProperty, "PrimaryButton");
        SaveButton.Click += (_, _) => { ViewModel.Save(); Close(); }; actions.Children.Add(SaveButton);
        DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        var fields = new StackPanel();
        void Field(string label, string property, double height = 0)
        {
            var box = new TextBox { Name = property + "Editor", AcceptsReturn = height > 0, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = height > 0 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
                Margin = new Thickness(0, 0, 0, 14), MinHeight = height > 0 ? height : 32,
                ToolTip = label == "Written interpretation" ? "Plain text; use blank lines to separate paragraphs." : label };
            box.SetBinding(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            fields.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0, 0, 0, 4) });
            fields.Children.Add(box);
        }
        Field("Title", nameof(model.Title)); Field("Authors", nameof(model.Authors));
        Field("Sample description", nameof(model.SampleDescription), 70); Field("Workflow", nameof(model.Workflow), 60);
        Field("Kind of database", nameof(model.DatabaseKind)); Field("Written interpretation", nameof(model.WrittenInterpretation), 210);
        layout.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = SecondaryWindowView.Frame(layout);
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (_, _) => TextReportWindow.Show(Title, HelpText, this, true)));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Help, Key.F1, ModifierKeys.None));
    }
}
