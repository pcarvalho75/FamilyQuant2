using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> UiProgressAuditCommands() =>
    [new("--test-ui-progress-audit", 100, TestCommandPhase.Raw,
        args => args[0] == "--test-ui-progress-audit", (_, _) =>
        {
            UiProgressAuditTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private sealed class UiAuditHttpHandler(HttpMessageHandler inner, Dispatcher dispatcher) : DelegatingHandler(inner)
    {
        public int WorkerRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.Run(() =>
        {
            if (!dispatcher.CheckAccess()) Interlocked.Increment(ref WorkerRequests);
            return base.SendAsync(request, token);
        }, token);
    }

    private static void UiProgressAuditTests()
    {
        EnsureApplication();
        string root = Path.Combine(Path.GetTempPath(), "ui-progress-audit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        UiProgressBuildAudit(root);
        UiProgressEvidenceAudit();
        UiProgressFamilyWorldAudit();
        UiProgressReviewAudit();
        UiProgressExportAudit(root);
        UiProgressLifecycleAudit();
    }

    private static void UiProgressBuildAudit(string root)
    {
        string input = Path.Combine(root, "build.fasta"); File.WriteAllText(input, ">fixture\nPEPTIDEK\n");
        var view = new USearchLib(); var grid = new Grid(); grid.Children.Add(view);
        var host = AppOperationHost.For(view);
        int progress = 0, properties = 0, statuses = 0; bool worker = false;
        host.ProgressReported += (_, _) => { grid.Children.Contains(view); progress++; };
        view.ViewModel.PropertyChanged += (_, _) => { grid.Children.Contains(view); properties++; };
        view.ShellStatusChanged += (_, _) => { grid.Children.Contains(view); statuses++; };
        var oldBuild = USearchLib.BuildOverride;
        try
        {
            view.ViewModel.DatabasePath = input;
            USearchLib.BuildOverride = (_, _, _) => Task.Run(() =>
            {
                worker = !grid.Dispatcher.CheckAccess();
                var operation = (OperationContext)typeof(USearchLib).GetProperty("Operation", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
                operation.Report("build", "Synthetic build progress", 1, 1);
                return new FamilyClusterFile { MyExtendedFastaItems = new()
                { new() { SequenceIdentifier = "fixture", Description = "Fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 } } };
            });
            ((Button)view.FindName("ButtonBuildFamilies")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FinishBusyOperation(host);
            Check("QX3 Build worker progress, completion properties and shell status touch UI safely",
                worker && progress > 0 && properties > 0 && statuses > 0 && view.fcf != null && File.Exists(Path.ChangeExtension(input, ".fcf")));
        }
        finally { USearchLib.BuildOverride = oldBuild; }
    }

    private static void UiProgressEvidenceAudit()
    {
        var view = new ClusterGenerationControl(AtomicFetchFile()); var grid = new Grid(); grid.Children.Add(view);
        int progress = 0, previews = 0, changes = 0;
        view.Operations.ProgressReported += (_, _) => { grid.Children.Contains(view); progress++; };
        view.ViewModel.PropertyChanged += (_, args) =>
        {
            grid.Children.Contains(view);
            if (args.PropertyName == nameof(CurationViewModel.DomainPreview) && view.ViewModel.DomainPreview.Count > 0) previews++;
        };
        view.ViewModel.DocumentChanged += (_, _) => { grid.Children.Contains(view); changes++; };
        using var handler = new UiAuditHttpHandler(new EvidenceAndPanelHandler(), grid.Dispatcher);
        using var lease = view.Operations.Begin("Synthetic evidence audit");
        Task work = AppOperationHost.OnUi(view, () => view.ViewModel.FetchEvidenceAsync(handler, lease.Context,
            options: new UniProtFetchOptions { DelayAsync = (_, _) => Task.CompletedTask }));
        FinishBusyTask(work);
        Check("QX3 Fetch evidence worker reports, early domain preview and completion touch UI safely",
            handler.WorkerRequests > 0 && progress > 0 && previews > 0 && changes == 1 && lease.Context.HasCommitted);
    }

    private static void UiProgressFamilyWorldAudit()
    {
        var file = TaskFile();
        var view = new ClusterGenerationControl(file); var grid = new Grid(); grid.Children.Add(view);
        int progress = 0, properties = 0, changes = 0;
        view.Operations.ProgressReported += (_, _) => { grid.Children.Contains(view); progress++; };
        view.ViewModel.PropertyChanged += (_, _) => { grid.Children.Contains(view); properties++; };
        view.ViewModel.DocumentChanged += (_, _) => { grid.Children.Contains(view); changes++; };
        using var taxonomy = new TaskTaxonomyHandler();
        var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, taxonomy).GetAwaiter().GetResult();
        var choice = EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
        using var handler = new UiAuditHttpHandler(new WorldFetchHandler(), grid.Dispatcher);
        using var lease = view.Operations.Begin("Synthetic family world audit");
        var adapterType = typeof(ClusterGenerationControl).GetNestedType("FamilyWorldTaskProgress", BindingFlags.NonPublic)!;
        var adapter = (IProgress<FetchProgress>)Activator.CreateInstance(adapterType, lease.Context)!;
        FinishBusyTask(Task.Run(() => adapter.Report(new() { CompletedBatches = 1, TotalBatches = 1, Message = "Synthetic task progress" })));
        FinishBusyTask(AppOperationHost.OnUi(view, () => view.ViewModel.FetchFamilyWorldAsync(choice, lease.Context, handler)));
        Check("QX3 Fetch family world worker task progress and completed model touch UI safely",
            handler.WorkerRequests > 0 && progress > 1 && properties > 0 && changes == 1 && file.EvidenceSource?.FamilyWorld != null);
    }

    private static void UiProgressReviewAudit()
    {
        var file = TaskFile();
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
        bool worker = false; int progress = 0, properties = 0;
        var grid = new Grid(); var child = new Border(); grid.Children.Add(child);
        var window = new SuperClusterPreviewWindow(preview, null, null, evidenceMode: true,
            discoverTaskWithProgress: (sink, token) => Task.Run(() =>
            {
                worker = !grid.Dispatcher.CheckAccess();
                sink!.Report(new() { CompletedBatches = 1, TotalBatches = 1, Message = "Synthetic taxonomy progress" });
                return new EvidenceTaskScopeInput("Fixture clade", new[] { new ReviewTaskTaxon("Fixture taxon", 21) },
                    new[] { new ReviewTaskKeyword("Fixture scope", 2) }, 2, new[] { "Fixture scope" }, "fixture", "Fixture evidence", 3);
            }, token));
        var model = (GroupingReviewViewModel)window.DataContext;
        window.Operations.ProgressReported += (_, _) => { grid.Children.Contains(child); progress++; };
        model.PropertyChanged += (_, _) => { grid.Children.Contains(child); properties++; };
        try
        {
            var run = typeof(SuperClusterPreviewWindow).GetMethod("RunReviewAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            FinishBusyTask((Task)run.Invoke(window, new object[] { "Synthetic task audit", (Func<Task>)(() => Task.CompletedTask), true })!);
            Check("QX3 Review grouping worker taxonomy progress and model completion touch UI safely",
                worker && progress > 0 && properties > 0 && !model.IsBusy && model.TaskScope != null);
        }
        finally { window.Close(); }
    }

    private static void UiProgressExportAudit(string root)
    {
        var grid = new Grid(); var child = new Border(); grid.Children.Add(child);
        var host = new AppOperationHost(); int progress = 0, properties = 0; bool worker = false;
        host.ProgressReported += (_, _) => { grid.Children.Contains(child); progress++; };
        host.PropertyChanged += (_, _) => { grid.Children.Contains(child); properties++; };
        using var lease = host.Begin("Synthetic export audit");
        string output = Path.Combine(root, "export.txt");
        FinishBusyTask(AppOperationFiles.RunSta(() =>
        {
            worker = !grid.Dispatcher.CheckAccess();
            AppOperationFiles.WriteFile(output, path => File.WriteAllText(path, "Synthetic export"), lease.Context);
        }));
        bool published = worker && progress == 1 && properties > 0 && File.ReadAllText(output) == "Synthetic export";
        lease.Dispose();
        var model = new AnalyticsViewModel(new[] { Qr("identified", "fixture", 1, 2, "PEPTIDEK", 10, 20) },
            new QuantManifest { DerivedMethod = NativeQuantFractions.Method, PerRunTic = new() { ["fixture"] = 1000 } })
        { ExportKind = ResultsExportKind.Peptides };
        int completion = 0;
        model.PropertyChanged += (_, _) => { grid.Children.Contains(child); completion++; };
        string table = Path.Combine(root, "peptides.csv");
        FinishBusyTask(AppOperationHost.OnUi(grid, () => model.ExportAsync(table, host)));
        Check("QX3 Export STA worker progress, host properties and Results completion touch UI safely",
            published && completion > 0 && model.Status.StartsWith("Written to ", StringComparison.Ordinal) && File.Exists(table));
    }

    private static void UiProgressLifecycleAudit()
    {
        var grid = new Grid(); var child = new Border(); grid.Children.Add(child);
        var host = new AppOperationHost(); var seen = new List<long?>(); int properties = 0;
        host.PropertyChanged += (_, _) => { grid.Children.Contains(child); properties++; };
        var lease = host.Begin("Synthetic lifecycle audit");
        host.ProgressReported += (_, item) =>
        {
            grid.Children.Contains(child); seen.Add(item.Completed);
            if (item.Completed == 2) host.Cancel();
        };
        bool cancelledBeforeReturn = false;
        FinishBusyTask(Task.Run(() =>
        {
            try { for (int i = 0; i <= 3; i++) lease.Context.Report("audit", "Synthetic progress", i, 3); }
            catch (OperationCanceledException) { cancelledBeforeReturn = lease.Context.IsCancellationRequested; }
            finally { lease.Dispose(); }
        }));
        Check("QX3 host preserves order, synchronous cancellation and worker disposal UI notifications",
            cancelledBeforeReturn && seen.SequenceEqual(new long?[] { 0, 1, 2 }) && properties >= 5 && !host.IsBusy);
    }
}
