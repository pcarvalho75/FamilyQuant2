using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// The one read-only text window: the identity-scan report, the alignment output and the embedded
    /// help all used to build their own, with different fonts, different scroll settings and, in one
    /// case, no way to select the text. Built in code rather than XAML so it cannot fail to parse in a
    /// harness that never opens a window.
    /// </summary>
    public static class TextReportWindow
    {
        public static Window Create(string title, string text, bool wrap = false)
        {
            return new Window
            {
                Title = title,
                Width = 860,
                Height = 640,
                // 1280x800 is the smallest screen this has to work on; keep the window inside it.
                MinWidth = 480,
                MinHeight = 320,
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x39, 0x49)),
                Content = new TextBox
                {
                    Text = text,
                    AcceptsReturn = true,
                    IsReadOnly = true,
                    IsReadOnlyCaretVisible = true,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    Padding = new Thickness(12),
                    BorderThickness = new Thickness(0),
                    Background = new SolidColorBrush(Color.FromRgb(0x33, 0x39, 0x49)),
                    Foreground = Brushes.White,
                    SelectionBrush = new SolidColorBrush(Color.FromRgb(0x61, 0xAE, 0xEE)),
                    TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
                    ToolTip = "Read-only. Select any part of this text and copy it with Ctrl+C."
                }
            };
        }

        public static void Show(string title, string text, Window? owner = null, bool wrap = false)
        {
            Window window = Create(title, text, wrap);

            // Owned windows stay above the main window and close with it, instead of being lost behind it.
            if (owner != null && owner.IsLoaded) window.Owner = owner;

            window.Show();
        }
    }
}
