using System.Windows.Controls;
using System.Windows;
using System.ComponentModel;
using System.Windows.Input;
using System;
using System.Windows.Threading;

namespace FamilyQuant;

public partial class BusyOverlay : UserControl
{
    private Window? keyWindow;
    private readonly DispatcherTimer clock;
    public BusyOverlay()
    {
        InitializeComponent();
        clock = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) => UpdateClocks();
        IsVisibleChanged += (_, _) => RefreshClock();
        DataContextChanged += (_, _) => RefreshClock();
        Loaded += (_, _) =>
        {
            keyWindow = Window.GetWindow(this);
            if (keyWindow != null) keyWindow.PreviewKeyDown += PreviewCancel;
            RefreshClock();
        };
        Unloaded += (_, _) =>
        {
            if (keyWindow != null) keyWindow.PreviewKeyDown -= PreviewCancel;
            keyWindow = null;
            clock.Stop();
        };
    }

    private void RefreshClock()
    {
        if (IsLoaded && IsVisible && DataContext is AppOperationHost { IsBusy: true }) clock.Start();
        else clock.Stop();
        UpdateClocks();
    }
    private void UpdateClocks()
    {
        OperationElapsed.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
        OperationSinceLastStep.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
    }
    private void OperationLog_Click(object sender, RoutedEventArgs args)
    {
        if (DataContext is AppOperationHost host) TextReportWindow.Show(host.Title + " log", host.LogText, Window.GetWindow(this), true);
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
