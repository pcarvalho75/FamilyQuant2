using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant;

public partial class ClusterGenerationControl
{
    private AppOperationHost.OperationLease? curationLease;
    private object? enabledBeforeCuration;
    private int projectionGeneration;
    private TaskCompletionSource<bool>? projectionDone;
    private Action? releaseQueuedProjection;
    private bool projectionRunning;

    public AppOperationHost Operations
    {
        get
        {
            var window = Window.GetWindow(this);
            if (window != null && AppOperationHost.GetScope(window) is AppOperationHost inherited)
            {
                AppOperationHost.SetScope(this, inherited); return inherited;
            }
            if (window != null && AppOperationHost.GetScope(this) is AppOperationHost detached)
            {
                AppOperationHost.SetScope(window, detached);
                BusyOverlay.AttachWindow(window, detached);
                return detached;
            }
            return AppOperationHost.For(this);
        }
    }
    private OperationContext CurationOperation => curationLease?.Context
        ?? throw new InvalidOperationException("No curation operation is running.");

    private void BeginSharedWait(string title)
    {
        if (curationLease != null || Operations.IsBusy)
            throw new InvalidOperationException("Wait for the current operation to finish.");
        if (!DataGridResultGrid.CommitEdit(DataGridEditingUnit.Cell, true)
            || !DataGridResultGrid.CommitEdit(DataGridEditingUnit.Row, true))
            throw new InvalidOperationException("Finish or cancel the current cell edit before starting an operation.");
        enabledBeforeCuration = ReadLocalValue(IsEnabledProperty);
        curationLease = Operations.Begin(title);
        IsEnabled = false;
    }
    private void EndSharedWait()
    {
        var lease = curationLease;
        if (lease == null) return;
        var enabled = enabledBeforeCuration;
        curationLease = null; enabledBeforeCuration = null;
        if (ReferenceEquals(enabled, DependencyProperty.UnsetValue)) ClearValue(IsEnabledProperty);
        else if (enabled != null) SetValue(IsEnabledProperty, enabled);
        lease.Dispose();
    }
    private Task<bool> RunCurationAsync(string title, Func<OperationContext, Task> work) =>
        AppOperationHost.OnUi(this, async () =>
        {
            if (Operations.IsBusy) { ShowCurationOperationResult("Wait for the current operation to finish."); return false; }
            bool began = false;
            try
            {
                BeginSharedWait(title); began = true;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                await work(CurationOperation);
                CurationOperation.ThrowIfCancellationRequested();
                return true;
            }
            catch (OperationCanceledException) when (curationLease?.Context.IsCancellationRequested == true)
            {
                ShowCurationOperationResult("Cancelled. The previous document was kept."); return false;
            }
            catch (Exception ex)
            {
                if (title == "Recalculate family shares") lastRecalculationFailed = true;
                ShowCurationOperationResult(CurationFeedback.Failure(title, ex)); return false;
            }
            finally { if (began) EndSharedWait(); }
        });

    private Task QueueFullXicAsync(IReadOnlyList<QuantRow>? rows, IReadOnlyDictionary<string, double>? perRunTic) =>
        AppOperationHost.OnUi(this, () => QueueFullXicCore(rows, perRunTic));

    private Task QueueFullXicCore(IReadOnlyList<QuantRow>? rows, IReadOnlyDictionary<string, double>? perRunTic)
    {
        int generation = ++projectionGeneration;
        releaseQueuedProjection?.Invoke();
        projectionDone?.TrySetResult(false);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        projectionDone = done;
        var rowCopy = rows?.ToList();
        var ticCopy = perRunTic?.ToDictionary(p => p.Key, p => p.Value);
        var host = Operations;
        if (projectionRunning) host.Cancel();

        async void Start()
        {
            if (generation != projectionGeneration) { done.TrySetResult(false); return; }
            bool began = false, succeeded = false;
            try
            {
                BeginSharedWait("Attribute full XIC to families"); began = true; projectionRunning = true;
                SyncQuantitationChoices();
                succeeded = await Quantitation.AttachWithOperationAsync(rowCopy, ticCopy, CurationOperation);
                if (!succeeded && generation == projectionGeneration) ShowCurationOperationResult(Quantitation.Summary);
            }
            catch (Exception ex)
            {
                if (generation == projectionGeneration) ShowCurationOperationResult("Family attribution could not finish: " + ex.Message);
            }
            finally
            {
                projectionRunning = false;
                if (began) EndSharedWait();
                done.TrySetResult(succeeded);
            }
        }
        if (!host.IsBusy) Start();
        else
        {
            PropertyChangedEventHandler? handler = null;
            Action? release = null;
            release = () =>
            {
                host.PropertyChanged -= handler;
                if (ReferenceEquals(releaseQueuedProjection, release)) releaseQueuedProjection = null;
            };
            handler = (_, _) =>
            {
                if (host.IsBusy) return;
                release(); Start();
            };
            releaseQueuedProjection = release;
            host.PropertyChanged += handler;
        }
        return done.Task;
    }
}
