using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    public AppOperationHost Operations { get; } = new();

    private Task RunReviewAsync(string title, Func<Task> work) => AppOperationHost.OnUi(this, async () =>
    {
        if (Operations.IsBusy) return;
        using var operation = Operations.Begin(title);
        using var cancellation = operation.Context.CancellationToken.Register(() => Dispatcher.Invoke(model.CancelPending));
        PropertyChangedEventHandler progress = (_, args) =>
        {
            if (args.PropertyName == nameof(GroupingReviewViewModel.Status) && !operation.Context.IsCancellationRequested)
                operation.Context.Report("review", model.Status);
        };
        model.PropertyChanged += progress;
        try { await work(); }
        finally { model.PropertyChanged -= progress; }
    });
}
