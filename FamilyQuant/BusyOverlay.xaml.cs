using System.Windows.Controls;
using System.Windows;

namespace FamilyQuant;

public partial class BusyOverlay : UserControl
{
    public BusyOverlay() => InitializeComponent();

    internal static void AttachWindow(Window window, AppOperationHost operations)
    {
        if (window.Content is not UIElement content) return;
        window.Content = null;
        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(new BusyOverlay { DataContext = operations });
        window.Content = grid;
        operations.PropertyChanged += (_, _) => content.IsEnabled = !operations.IsBusy;
        window.Closing += (_, args) =>
        {
            if (!operations.IsBusy) return;
            args.Cancel = true;
            operations.Cancel();
        };
    }
}
