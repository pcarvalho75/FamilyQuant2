using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using FamilyQuant.Operations;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void CurationOperationTests()
    {
        EnsureApplication();
        var file = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>
        {
            new() { SequenceIdentifier = "curation_op_1", Description = "Shared fixture protein", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 },
            new() { SequenceIdentifier = "curation_op_2", Description = "Shared fixture protein", Sequence = "PEPTIDER", UserID = 2, USearchID = 2 }
        } };
        var screen = new ClusterGenerationControl(file);
        var property = typeof(ClusterGenerationControl).GetProperty("Operations");
        Check("curation exposes the shared operation host", property?.PropertyType == typeof(AppOperationHost));
        if (property == null) return;
        var host = (AppOperationHost)property.GetValue(screen)!;
        var parent = host.Begin("An existing project operation");
        Task queued = screen.SetFullXicQuantAsync(Array.Empty<QuantRow>(), null);
        Check("family attribution queues behind a parent operation and immediately exposes its pending task",
            host.IsBusy && !queued.IsCompleted && !screen.FullXicReady.IsCompleted);
        parent.Dispose(); FinishBusyTask(queued);
        Check("queued attribution completes through the host and releases it", !host.IsBusy && screen.FullXicReady.IsCompleted && screen.IsEnabled);

        string before = file.ToJsonText();
        bool sawAttribution = false;
        System.ComponentModel.PropertyChangedEventHandler cancel = (_, _) =>
        {
            if (host.IsBusy && !host.IsCancelling && host.Title == "Attribute full XIC to families")
            {
                sawAttribution = true; host.Cancel();
            }
        };
        host.PropertyChanged += cancel;
        try { FinishBusyTask(screen.SetFullXicQuantAsync(Array.Empty<QuantRow>(), null)); }
        finally { host.PropertyChanged -= cancel; }
        Check("cancelling family attribution keeps every persisted curation value and releases the host",
            sawAttribution && before == file.ToJsonText() && !host.IsBusy && screen.IsEnabled);

        bool sawWords = false;
        int changes = 0; screen.DocumentChanged += (_, _) => changes++;
        System.ComponentModel.PropertyChangedEventHandler cancelWords = (_, _) =>
        {
            if (host.IsBusy && !host.IsCancelling && host.Title.Contains("description words", StringComparison.Ordinal))
            {
                sawWords = true; host.Cancel();
            }
        };
        host.PropertyChanged += cancelWords;
        try
        {
            var method = typeof(ClusterGenerationControl).GetMethod("PerformClustering", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(double), typeof(List<string>) }, null)!;
            FinishBusyTask((Task)method.Invoke(screen, new object[] { 0.95, new List<string>() })!);
        }
        finally { host.PropertyChanged -= cancelWords; }
        Check("cancelling word grouping keeps IDs, locks, annotations, Undo and dirty state unchanged",
            sawWords && before == file.ToJsonText() && screen.ViewModel.UndoStack.Count == 0 && changes == 0 && !host.IsBusy);

        string folder = Path.Combine(Path.GetTempPath(), "FamilyQuant.Curation.Operation.Tests");
        Directory.CreateDirectory(folder);
        string plp = WriteDenominatorFixture(folder, "staged-load.plp", "NONE");
        var quantFile = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>
        {
            new() { SequenceIdentifier = "sp|X|PROT1", Description = "Fixture protein", Sequence = "PEPTIDEA", UserID = 1, USearchID = 1, XICUser = 0.25 }
        } };
        var model = new CurationQuantitationViewModel(quantFile);
        var prepare = typeof(CurationQuantitationViewModel).GetMethod("PreparePatternLabAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var publish = typeof(CurationQuantitationViewModel).GetMethod("PublishPatternLab", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object Prepare()
        {
            var task = (Task)prepare.Invoke(model, new object[] { plp, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique, CancellationToken.None })!;
            FinishBusyTask(task);
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }
        string quantBefore = quantFile.ToJsonText();
        object prepared = Prepare();
        Check("PatternLab preparation computes on a copy before publishing any project or share",
            quantBefore == quantFile.ToJsonText() && model.CurrentPLP == null && model.DisplayedMode == null);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool refused = Throws<OperationCanceledException>(() => new OperationContext(cancelled.Token).Commit(() => publish.Invoke(model, new[] { prepared })));
        Check("cancellation before PatternLab publication retains every prior curation byte",
            refused && quantBefore == quantFile.ToJsonText() && model.CurrentPLP == null);
        new OperationContext().Commit(() => publish.Invoke(model, new[] { prepared }));
        Check("accepted PatternLab publication attaches the completed shares and their actual mode",
            model.CurrentPLP != null && model.DisplayedMode == NormalizationMode.IdentifiedSignal
            && quantFile.MyExtendedFastaItems[0].XICUser == 1);
        object stale = Prepare();
        quantFile.MyExtendedFastaItems[0].UserID = 9;
        string edited = quantFile.ToJsonText();
        bool staleRefused = false;
        try { publish.Invoke(model, new[] { stale }); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { staleRefused = true; }
        Check("a stale PatternLab result refuses attachment after the family grouping changes",
            staleRefused && edited == quantFile.ToJsonText());

        var oldPresenter = ShellDialogs.FilePresenter;
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        var browse = new List<(string Id, string Folder)>();
        var window = new Window { Content = screen, Width = 1024, Height = 768, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            MainWindow.ShellSettingsPathOverride = Path.Combine(folder, "curation-browse.json");
            new ShellSessionState { LastFolder = folder }.Save(MainWindow.ShellSettingsPathOverride);
            ShellDialogs.FilePresenter = (_, id, dialog) => { browse.Add((id, dialog.InitialDirectory)); return false; };
            screen.SetFullXicQuant(new[] { new QuantRow { Side = "identified", Run = "r1", Sequence = "PEPTIDEK",
                Charge = 2, PrecursorMz = 500, EnvelopeSum = 100, IsotopeAreas = new[] { 100.0 } } }, new Dictionary<string, double> { ["r1"] = 1000 });
            window.Show(); PumpDispatcher(); window.UpdateLayout();
            void BrowseAll()
            {
                UiField<Button>(screen, "ButtonLoadQuantData")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                UiField<Button>(screen, "ButtonSaveReport")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                UiField<Button>(screen, "ButtonExportComposition")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpDispatcher();
            }
            BrowseAll();
            Check("all three curation browse actions use the shared folder preference and stable route IDs",
                browse.Select(x => x.Id).SequenceEqual(new[] { "curation.patternlab", "curation.proteins-csv", "curation.composition-csv" })
                && browse.All(x => Path.GetFullPath(x.Folder) == Path.GetFullPath(folder)));
            Check("standalone curation attaches exactly one shared busy overlay",
                WalkTree(window).OfType<BusyOverlay>().Count() == 1);

            string beforeLoad = file.ToJsonText();
            bool sawLoad = false;
            System.ComponentModel.PropertyChangedEventHandler cancelLoad = (_, _) =>
            {
                if (host.IsBusy && !host.IsCancelling && host.Title == "Load PatternLab quantitation")
                {
                    sawLoad = true; host.Cancel();
                }
            };
            ShellDialogs.FilePresenter = (_, id, dialog) => { dialog.FileName = plp; return id == "curation.patternlab"; };
            host.PropertyChanged += cancelLoad;
            try
            {
                UiField<Button>(screen, "ButtonLoadQuantData")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FinishBusyOperation(host);
            }
            finally { host.PropertyChanged -= cancelLoad; }
            Check("cancelling the native PatternLab load keeps the document and prior project attachment",
                sawLoad && beforeLoad == file.ToJsonText() && !host.IsBusy && screen.IsEnabled);
            ShellDialogs.FilePresenter = (_, id, dialog) => { browse.Add((id, dialog.InitialDirectory)); return false; };
            window.Content = null; PumpDispatcher();
            BrowseAll();
            Check("disconnected curation browse actions never call a native presenter", browse.Count == 3);
        }
        finally { window.Close(); ShellDialogs.FilePresenter = oldPresenter; MainWindow.ShellSettingsPathOverride = oldSettings; }

        string beforeReadonly = file.ToJsonText();
        void CancelReadonly(string title, Func<Task> start)
        {
            bool saw = false;
            System.ComponentModel.PropertyChangedEventHandler cancelAction = (_, _) =>
            {
                if (host.IsBusy && !host.IsCancelling && host.Title == title) { saw = true; host.Cancel(); }
            };
            host.PropertyChanged += cancelAction;
            try { FinishBusyTask(start()); }
            finally { host.PropertyChanged -= cancelAction; }
            Check("cancelled " + title + " leaves the persisted document unchanged",
                saw && beforeReadonly == file.ToJsonText() && !host.IsBusy && screen.IsEnabled);
        }
        CancelReadonly("Prepare keyword grouping review", () =>
            (Task)typeof(ClusterGenerationControl).GetMethod("RunSuperCluster", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(screen, null)!);
        CancelReadonly("Prepare evidence grouping review", screen.ReviewEvidenceAsync);
        var proteinsGrid = UiField<System.Windows.Controls.DataGrid>(screen, "DataGridResultGrid")!;
        foreach (var protein in file.MyExtendedFastaItems) proteinsGrid.SelectedItems.Add(protein);
        CancelReadonly("Align selected proteins", screen.AlignSelectedAsync);
        CancelReadonly("Fetch evidence", screen.FetchProteinEvidenceAsync);
    }
}
