using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static Window Y7Host(UIElement content)
    {
        var window = new Window { Content = content, Width = 1000, Height = 700, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show(); PumpDispatcher(); return window;
    }

    private static void BusyLogoRouteTests()
    {
        Section("Y7 long-operation ownership gaps");
        foreach (UserControl control in new UserControl[] { new DeNovoControl(), new QuantifyControl() })
        {
            var window = Y7Host(control);
            try
            {
                var operations = control is DeNovoControl deNovo ? deNovo.ViewModel.Operations : ((QuantifyControl)control).ViewModel.Operations;
                using var operation = operations.Begin("Standalone operation"); PumpDispatcher();
                Check(control.GetType().Name + " standalone host displays the shared animated overlay", ReferenceEquals(AppOperationHost.GetScope(window), operations)
                    && Y7Visual<BusyOverlay>(window).Count(overlay => overlay.IsVisible) == 1);
                operations.Cancel();
                Check(control.GetType().Name + " standalone Cancel reaches its actual operation", operation.Context.IsCancellationRequested);
            }
            finally { window.Close(); PumpDispatcher(); }
        }
        var quantify = new ComputeXicWindow("Y7 synthetic fixture");
        quantify.Left = -30000; quantify.Top = -30000; quantify.ShowActivated = false; quantify.ShowInTaskbar = false;
        quantify.WindowStartupLocation = WindowStartupLocation.Manual;
        try
        {
            quantify.Show(); PumpDispatcher();
            Check("dedicated Quantify window retains one shared operation scope and one overlay", ReferenceEquals(AppOperationHost.GetScope(quantify), quantify.ViewModel.Operations)
                && Y7Visual<BusyOverlay>(quantify).Count() == 1);
        }
        finally { quantify.Close(); PumpDispatcher(); }
        var taskFacts = new EvidenceTaskScopeInput("Synthetic clade", Array.Empty<ReviewTaskTaxon>(), Array.Empty<ReviewTaskKeyword>(), 0,
            Array.Empty<string>(), "synthetic release");
        var pending = new TaskCompletionSource<EvidenceTaskScopeInput>(); CancellationToken observed = default;
        var preview = new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null);
        var review = new SuperClusterPreviewWindow(preview, null, null, evidenceMode: true, discoverTask: token => { observed = token; return pending.Task; });
        review.Left = -30000; review.Top = -30000; review.ShowActivated = false; review.ShowInTaskbar = false;
        review.WindowStartupLocation = WindowStartupLocation.Manual;
        try
        {
            review.Show(); PumpDispatcher();
            Check("grouping task discovery uses the shared visible overlay", review.Operations.IsBusy && Y7Visual<BusyOverlay>(review).Single().IsVisible);
            review.Operations.Cancel(); pending.SetResult(taskFacts); FinishBusyOperation(review.Operations);
            var model = (GroupingReviewViewModel)review.DataContext;
            Check("grouping discovery Cancel reaches HTTP token and refuses late scope facts", observed.IsCancellationRequested && model.TaskScope == null
                && !review.Operations.IsBusy && model.Status.StartsWith("Cancelled", StringComparison.Ordinal));
        }
        finally { pending.TrySetResult(taskFacts); review.Close(); PumpDispatcher(); }
        foreach (bool motif in new[] { false, true }) Y7ReviewWorker(motif);
    }

    private static void Y7ReviewWorker(bool motif)
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource();
        var previous = GroupingReviewScreens.Fixture(12);
        var preview = new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null);
        var review = new SuperClusterPreviewWindow(preview, previous,
            _ => { started.TrySetResult(); release.Wait(); return GroupingReviewScreens.Fixture(13); },
            _ => { started.TrySetResult(); release.Wait(); return "Synthetic motif result"; }, "manual", true);
        review.Left = -30000; review.Top = -30000; review.ShowActivated = false; review.ShowInTaskbar = false;
        review.WindowStartupLocation = WindowStartupLocation.Manual;
        try
        {
            review.Show(); PumpDispatcher();
            var model = (GroupingReviewViewModel)review.DataContext;
            var prior = model.Evidence;
            if (motif) model.MotifPattern = "PEP";
            typeof(SuperClusterPreviewWindow).GetMethod(motif ? "Motif_Click" : "UpdateProposal_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(review, new object[] { review, new RoutedEventArgs() });
            FinishBusyTask(started.Task);
            Check((motif ? "motif count" : "grouping proposal") + " worker has the shared visible overlay", review.Operations.IsBusy && Y7Visual<BusyOverlay>(review).Single().IsVisible);
            review.Operations.Cancel();
            Check((motif ? "motif count" : "grouping proposal") + " cancellation retains its lease until the worker finishes", review.Operations.IsBusy);
            release.Set(); FinishBusyOperation(review.Operations);
            Check((motif ? "motif count" : "grouping proposal") + " late work cannot replace the previous review", ReferenceEquals(prior, model.Evidence)
                && model.MotifReport.Length == 0 && model.Status.StartsWith("Cancelled", StringComparison.Ordinal));
        }
        finally { release.Set(); FinishBusyOperation(review.Operations); review.Close(); PumpDispatcher(); }
    }
}
