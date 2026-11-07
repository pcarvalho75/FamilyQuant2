using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace FamilyQuant;

internal static class SecondaryWindowView
{
    internal static Border Frame(FrameworkElement content)
    {
        var frame = new Border { Padding = new Thickness(20), Child = content };
        frame.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        return frame;
    }
    internal static void Theme(Window window)
    {
        window.SetResourceReference(Control.BackgroundProperty, "CanvasBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
    }
    internal static TextBlock Text(string text, bool muted = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        block.SetResourceReference(FrameworkElement.StyleProperty, muted ? "CaptionText" : "BodyText");
        block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush");
        return block;
    }
    internal static TextBox Report(string text, bool wrap, string tooltip) => new()
    {
        Text = text, AcceptsReturn = true, IsReadOnly = true, IsReadOnlyCaretVisible = true,
        FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(12),
        BorderThickness = new Thickness(0), TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto, ToolTip = tooltip
    };
    internal static Style HeaderTip(string tooltip)
    {
        var style = new Style(typeof(DataGridColumnHeader), Application.Current?.TryFindResource(typeof(DataGridColumnHeader)) as Style);
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, tooltip));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding());
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        style.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = text }));
        return style;
    }
    internal static Style CellText()
    {
        var style = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new DynamicResourceExtension("TableCellPadding")));
        return style;
    }
    internal static Button Help(string title, string text, Window window)
    {
        var button = new Button { Content = "How to read this", ToolTip = "Open the complete explanation of this view and its commands." };
        button.Click += (_, _) => TextReportWindow.Show(title, text, window, true);
        return button;
    }
}
