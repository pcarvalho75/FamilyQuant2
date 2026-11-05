using System;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

/// <summary>What Curate families does when no project is open.</summary>
public enum CurateWithoutProjectChoice { OpenProject, BuildFamilies, Cancel }

public partial class MainWindow
{
    /// <summary>Answers the no-project question without a window, for checks and automation.</summary>
    public static Func<CurateWithoutProjectChoice>? CurateWithoutProjectChoiceOverride { get; set; }

    /// <summary>
    /// Curate families works on an open project. With none open it asks whether to open one or to build
    /// families from a database first (Z4d). A shell that is not on screen keeps the former direct Open
    /// route, so headless callers see no dialog.
    /// </summary>
    internal CurateWithoutProjectChoice AskCurateWithoutProject()
    {
        if (CurateWithoutProjectChoiceOverride != null) return CurateWithoutProjectChoiceOverride();
        if (PresentationSource.FromVisual(this) == null) return CurateWithoutProjectChoice.OpenProject;
        var dialog = new Window { Title = WorkflowNames.CurateFamilies, Owner = this, Width = 560, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        SecondaryWindowView.Theme(dialog);
        var choice = CurateWithoutProjectChoice.Cancel;
        Button Choice(string text, CurateWithoutProjectChoice value, string tip)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), ToolTip = tip };
            button.Click += (_, _) => { choice = value; dialog.DialogResult = true; };
            return button;
        }
        var open = Choice("Open project", CurateWithoutProjectChoice.OpenProject,
            "Opens a FamilyQuant project or an older Family Cluster File to curate its families.");
        open.IsDefault = true;
        open.SetResourceReference(StyleProperty, "PrimaryButton");
        var build = Choice(WorkflowNames.BuildFamilies, CurateWithoutProjectChoice.BuildFamilies,
            "Opens Build families to group a protein database by sequence identity first.");
        var cancel = new Button { Content = "Cancel", IsCancel = true, ToolTip = "Closes this question and keeps the current screen." };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(open); buttons.Children.Add(build); buttons.Children.Add(cancel);
        var panel = new StackPanel();
        panel.Children.Add(SecondaryWindowView.Text("No project is open. Open a project to curate its families, or build families from a database first."));
        panel.Children.Add(buttons);
        dialog.Content = SecondaryWindowView.Frame(panel);
        return dialog.ShowDialog() == true ? choice : CurateWithoutProjectChoice.Cancel;
    }
}
