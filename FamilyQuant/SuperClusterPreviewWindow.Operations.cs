using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    public AppOperationHost Operations { get; } = new();

    private async Task RunReviewAsync(string title, Func<Task> work, bool taskDiscovery = false)
    {
        try
        {
            await AppOperationHost.OnUi(this, async () =>
            {
                if (Operations.IsBusy) return;
                using var operation = Operations.Begin(title, operationLogPath: AppOperationHost.CreateLogPath("grouping-review"));
                using var cancellation = operation.Context.CancellationToken.Register(() => Dispatcher.Invoke(model.CancelPending));
                PropertyChangedEventHandler progress = (_, args) =>
                {
                    if (args.PropertyName == nameof(GroupingReviewViewModel.Status) && !operation.Context.IsCancellationRequested)
                        operation.Context.Report("review", model.Status + (taskDiscovery ? "\nNeeds an internet connection" : ""));
                };
                model.PropertyChanged += progress;
                try
                {
                    if (taskDiscovery) await model.DiscoverTaskAsync(new GroupingTaskProgress(operation.Context));
                    else await work();
                }
                finally { model.PropertyChanged -= progress; }
            });
        }
        catch (Exception error)
        {
            Operations.LogFailure(error);
            model.ReportOperationFailure(error);
        }
    }

    private sealed class GroupingTaskProgress(OperationContext operation) : IProgress<FetchProgress>
    {
        public void Report(FetchProgress value) => operation.Report("taxonomy", value.Message.Length > 0 ? value.Message : "Asking UniProt about taxon "
            + value.CompletedBatches.ToString(CultureInfo.CurrentCulture) + " of "
            + value.TotalBatches.ToString(CultureInfo.CurrentCulture) + "\nNeeds an internet connection",
            value.CompletedBatches, value.TotalBatches);
    }
}
