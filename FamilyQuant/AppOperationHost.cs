using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FamilyQuant;

public sealed record OperationLogEntry(DateTimeOffset Timestamp, string Stage, string Message, long? Completed, long? Total);

/// <summary>One visible operation per window. A lease owns cancellation until the actual work ends.</summary>
public sealed class AppOperationHost : ScreenViewModel
{
    private static WeakReference<AppOperationHost>? crashOperation;
    internal static string? ActiveCrashOperation
    {
        get
        {
            var current = Volatile.Read(ref crashOperation);
            return current != null && current.TryGetTarget(out var host) && host.IsBusy ? host.Title : null;
        }
    }
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
    private DateTimeOffset startedAt, lastStepAt;
    private DateTimeOffset? endedAt;
    private string? logPath;
    private string? logWriteError;
    private readonly List<OperationLogEntry> logEntries = new();
    private readonly StringBuilder logText = new();
    public bool IsBusy => active != null;
    public bool IsCancelling => active?.Context.IsCancellationRequested == true;
    public string Title => title;
    public string Message => message;
    public bool IsIndeterminate => !total.HasValue || total <= 0;
    public double Percent => IsIndeterminate ? 0 : Math.Clamp(100d * (completed ?? 0) / total!.Value, 0, 100);
    public ScreenCommand CancelCommand { get; }
    public string CancelHint => !IsBusy ? "Unavailable because no operation is running. Start an operation before requesting cancellation."
        : IsCancelling ? "Cancellation has already been requested. Wait for the current phase to stop; previously published results stay available."
        : active?.Context.HasCommitted == true ? "Unavailable because this operation has already published its change. Wait for it to finish; project Undo can reverse supported curation edits, but not files written to disk."
        : "Requests cancellation at the next cancellation point before the operation publishes its change. Previously published results stay available.";
    public event EventHandler<OperationProgress>? ProgressReported;
    public TimeSpan Elapsed { get { lock (lifetime) return startedAt == default ? TimeSpan.Zero : Nonnegative((endedAt ?? DateTimeOffset.UtcNow) - startedAt); } }
    public TimeSpan SinceLastStep { get { lock (lifetime) return lastStepAt == default ? TimeSpan.Zero : Nonnegative((endedAt ?? DateTimeOffset.UtcNow) - lastStepAt); } }
    public string ElapsedText => "Elapsed " + Clock(Elapsed);
    public string SinceLastStepText => "Since last step " + Clock(SinceLastStep);
    public string LogText { get { lock (lifetime) return logText.ToString() + (logWriteError == null ? "" : "\nFile log unavailable: " + logWriteError); } }
    public string? LogPath => logPath;
    public IReadOnlyList<OperationLogEntry> LogEntries { get { lock (lifetime) return logEntries.ToArray(); } }
    public static string CreateLogPath(string category) => Path.Combine(ApplicationPaths.LocalDataDirectory,
        "operation-logs", category + "-" + Guid.NewGuid().ToString("N") + ".log");
    public void LogMessage(string stage, string message)
    {
        lock (lifetime) Record(new(stage, message), DateTimeOffset.UtcNow);
    }
    public void LogFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (lifetime) Record(new("operation-failure", error.ToString()), DateTimeOffset.UtcNow);
    }
    private static TimeSpan Nonnegative(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
    private static string Clock(TimeSpan value) => string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (long)value.TotalHours, value.Minutes, value.Seconds);

    public AppOperationHost() => CancelCommand = new(Cancel, () => IsBusy && !IsCancelling && active?.Context.HasCommitted != true);
    public OperationLease Begin(string operation, DateTimeOffset? startedAtUtc = null, string? operationLogPath = null)
    {
        dispatcher.VerifyAccess();
        if (IsBusy) throw new InvalidOperationException("Wait for the current operation to finish.");
        title = message = operation;
        completed = total = null;
        lock (lifetime)
        {
            startedAt = startedAtUtc ?? DateTimeOffset.UtcNow;
            lastStepAt = DateTimeOffset.UtcNow; endedAt = null;
            if (operationLogPath == null || !string.Equals(operationLogPath, logPath, StringComparison.OrdinalIgnoreCase))
            {
                logEntries.Clear(); logText.Clear(); logPath = operationLogPath; logWriteError = null;
                if (logPath != null)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
                        File.WriteAllText(logPath, "", new UTF8Encoding(false));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { logWriteError = error.Message; }
                }
            }
            Record(new("operation-start", operation), lastStepAt);
        }
        var lease = new OperationLease(this);
        active = lease;
        Volatile.Write(ref crashOperation, new WeakReference<AppOperationHost>(this));
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
        lock (lifetime) Record(new("operation-cancel", message), DateTimeOffset.UtcNow);
        Notify();
    }
    private void Report(OperationLease lease, OperationProgress progress)
    {
        lock (lifetime)
        {
            if (!ReferenceEquals(active, lease) || IsCancelling) return;
            Record(progress, DateTimeOffset.UtcNow);
        }
        void Apply()
        {
            if (!ReferenceEquals(active, lease) || IsCancelling) return;
            message = progress.Message;
            completed = progress.Completed;
            total = progress.Total;
            Notify();
            ProgressReported?.Invoke(this, progress);
        }
        if (dispatcher.CheckAccess()) Apply();
        // Subscribers may update views or cancel before the worker's next cancellation point.
        else dispatcher.Invoke((Action)Apply);
    }
    // Caller holds lifetime. Raw file records remain timestamped even if UI painting is delayed.
    private void Record(OperationProgress progress, DateTimeOffset at)
    {
        lastStepAt = at;
        logEntries.Add(new(at, progress.Stage, progress.Message, progress.Completed, progress.Total));
        string Clean(string value) => value.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        string line = at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) + "\t" + Clean(progress.Stage) + "\t"
            + progress.Completed?.ToString(CultureInfo.InvariantCulture) + "\t" + progress.Total?.ToString(CultureInfo.InvariantCulture)
            + "\t" + Clean(progress.Message) + Environment.NewLine;
        logText.Append(line);
        if (logPath != null && logWriteError == null)
            try { File.AppendAllText(logPath, line, new UTF8Encoding(false)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { logWriteError = error.Message; }
    }
    private void End(OperationLease lease)
    {
        lock (lifetime)
        {
            if (!ReferenceEquals(active, lease)) return;
            endedAt = DateTimeOffset.UtcNow;
            Record(new("operation-end", "Operation ended"), endedAt.Value);
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
