using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public static class TextReportWindow
{
    public static Window Create(string title, string text, bool wrap = false)
    {
        var model = new TextReportViewModel(title, text, wrap);
        var window = new Window { Title = model.Title, DataContext = model, Width = 860, Height = 640, MinWidth = 480, MinHeight = 320 };
        SecondaryWindowView.Theme(window);
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var heading = SecondaryWindowView.Text(model.Title);
        heading.SetResourceReference(FrameworkElement.StyleProperty, "SectionTitle");
        var report = SecondaryWindowView.Report(model.Text, model.Wrap, "Read-only. Select any part of this text and copy it with Ctrl+C.");
        Grid.SetRow(report, 1);
        layout.Children.Add(heading); layout.Children.Add(report); window.Content = SecondaryWindowView.Frame(layout);
        return window;
    }
    public static void Show(string title, string text, Window? owner = null, bool wrap = false)
    {
        Window window = Create(title, text, wrap);
        if (owner != null && owner.IsLoaded) window.Owner = owner;
        window.Show();
    }
}
