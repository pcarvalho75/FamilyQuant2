using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ReviewWorkWaitTests()
    {
        EnsureApplication();
        var previous = SynchronizationContext.Current;
        var completion = new TaskCompletionSource();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(6)
        };
        timer.Tick += (_, _) => { timer.Stop(); completion.SetResult(); };
        bool completed = false;
        try
        {
            timer.Start();
            FinishReviewWork(() => completion.Task);
            completed = true;
        }
        catch (TimeoutException) { }
        finally { timer.Stop(); }
        Check("review waits pump dispatcher work beyond the old five-second load limit",
            completed && completion.Task.IsCompletedSuccessfully);
        Check("review waits restore the caller synchronization context",
            ReferenceEquals(previous, SynchronizationContext.Current));

        var expected = new InvalidOperationException("Review wait fixture failure");
        var failure = new TaskCompletionSource();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => failure.SetException(expected)));
        Exception? observed = null;
        try { FinishReviewWork(() => failure.Task); }
        catch (Exception error) { observed = error; }
        Check("review waits propagate the original task failure and restore the caller context",
            ReferenceEquals(expected, observed) && ReferenceEquals(previous, SynchronizationContext.Current));

        bool cancelled = false;
        try { FinishReviewWork(() => Task.FromCanceled(new CancellationToken(true))); }
        catch (OperationCanceledException) { cancelled = true; }
        Check("review waits preserve task cancellation and restore the caller context",
            cancelled && ReferenceEquals(previous, SynchronizationContext.Current));
    }
}
