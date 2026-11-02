using System.Windows.Controls;
using System.Windows;
using System.ComponentModel;
using System.Windows.Input;

namespace FamilyQuant;

public partial class BusyOverlay : UserControl
{
    private Window? keyWindow;
    public BusyOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            keyWindow = Window.GetWindow(this);
            if (keyWindow != null) keyWindow.PreviewKeyDown += PreviewCancel;
        };
        Unloaded += (_, _) =>
        {
            if (keyWindow != null) keyWindow.PreviewKeyDown -= PreviewCancel;
            keyWindow = null;
        };
    }

    private void PreviewCancel(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || DataContext is not AppOperationHost { IsBusy: true } operations) return;
        operations.Cancel(); args.Handled = true;
    }

    internal static void AttachWindow(Window window, AppOperationHost operations)
    {
        if (window.Content is not UIElement content) return;
        window.Content = null;
        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(new BusyOverlay { DataContext = operations });
        window.Content = grid;
        PropertyChangedEventHandler changed = (_, _) => content.IsEnabled = !operations.IsBusy;
        CancelEventHandler closing = (_, args) =>
        {
            if (!operations.IsBusy) return;
            args.Cancel = true;
            operations.Cancel();
        };
        operations.PropertyChanged += changed;
        window.Closing += closing;
        window.Closed += (_, _) => { operations.PropertyChanged -= changed; window.Closing -= closing; };
        content.IsEnabled = !operations.IsBusy;
    }
}
