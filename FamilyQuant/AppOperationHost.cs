using FamilyQuant.Operations;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FamilyQuant;

/// <summary>One visible operation per window. A lease owns cancellation until the actual work ends.</summary>
public sealed class AppOperationHost : ScreenViewModel
{
    // Programmatic/headless callers do not necessarily have WPF's message-loop context.
    // Install it only while starting the async command, then restore the caller's context.
    public static System.Threading.Tasks.Task<T> OnUi<T>(DispatcherObject view, Func<System.Threading.Tasks.Task<T>> work)
    {
        if (!view.Dispatcher.CheckAccess()) return view.Dispatcher.InvokeAsync(() => OnUi(view, work)).Task.Unwrap();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(view.Dispatcher));
            return work();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    public static System.Threading.Tasks.Task OnUi(DispatcherObject view, Func<System.Threading.Tasks.Task> work)
        => OnUi(view, async () => { await work(); return true; });
    public static readonly DependencyProperty ScopeProperty = DependencyProperty.RegisterAttached(
        "Scope", typeof(AppOperationHost), typeof(AppOperationHost), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    public static void SetScope(DependencyObject view, AppOperationHost host) => view.SetValue(ScopeProperty, host);
    public static AppOperationHost? GetScope(DependencyObject view) => (AppOperationHost?)view.GetValue(ScopeProperty);
    public static AppOperationHost BindWindow(FrameworkElement view, AppOperationHost preferred)
    {
        var window = Window.GetWindow(view);
        if (window == null) return preferred;
        if (GetScope(window) is AppOperationHost inherited) return inherited;
        SetScope(window, preferred);
        BusyOverlay.AttachWindow(window, preferred);
        return preferred;
    }
    public static AppOperationHost For(DependencyObject view)
    {
        if (GetScope(view) is AppOperationHost host) return host;
        var window = view as Window ?? Window.GetWindow(view);
        if (window != null && GetScope(window) is AppOperationHost windowHost) return windowHost;
        host = new AppOperationHost();
        SetScope(window ?? view, host);
        if (window != null) BusyOverlay.AttachWindow(window, host);
        return host;
    }

    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly object lifetime = new();
    private OperationLease? active;
    private string title = "", message = "";
    private long? completed, total;
    public bool IsBusy => active != null;
    public bool IsCancelling => active?.Context.IsCancellationRequested == true;
    public string Title => title;
    public string Message => message;
    public bool IsIndeterminate => !total.HasValue || total <= 0;
    public double Percent => IsIndeterminate ? 0 : Math.Clamp(100d * (completed ?? 0) / total!.Value, 0, 100);
    public ScreenCommand CancelCommand { get; }

    public AppOperationHost() => CancelCommand = new(Cancel, () => IsBusy && !IsCancelling && active?.Context.HasCommitted != true);
    public OperationLease Begin(string operation)
    {
        dispatcher.VerifyAccess();
        if (IsBusy) throw new InvalidOperationException("Wait for the current operation to finish.");
        title = message = operation;
        completed = total = null;
        var lease = new OperationLease(this);
        active = lease;
        Notify();
        return lease;
    }
    public void Cancel()
    {
        dispatcher.VerifyAccess();
        lock (lifetime)
        {
            if (active == null || IsCancelling || active.Context.HasCommitted) return;
            active.Source.Cancel();
        }
        message = "Cancelling after the current phase...";
        Notify();
    }
    private void Report(OperationLease lease, OperationProgress progress)
    {
        void Apply()
        {
            if (!ReferenceEquals(active, lease) || IsCancelling) return;
            message = progress.Message;
            completed = progress.Completed;
            total = progress.Total;
            Notify();
        }
        if (dispatcher.CheckAccess()) Apply();
        else dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)Apply);
    }
    private void End(OperationLease lease)
    {
        lock (lifetime)
        {
            if (!ReferenceEquals(active, lease)) return;
            active = null;
            lease.Source.Dispose();
        }
        if (dispatcher.CheckAccess()) Notify();
        else dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)Notify);
    }
    private void Notify() { Changed(null); CancelCommand.Refresh(); }

    public sealed class OperationLease : IDisposable, IProgress<OperationProgress>
    {
        private readonly AppOperationHost host;
        private bool disposed;
        internal CancellationTokenSource Source { get; } = new();
        public OperationContext Context { get; }
        internal OperationLease(AppOperationHost host)
        {
            this.host = host;
            Context = new OperationContext(Source.Token, this);
        }
        public void Report(OperationProgress progress) => host.Report(this, progress);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            host.End(this);
        }
    }
}
