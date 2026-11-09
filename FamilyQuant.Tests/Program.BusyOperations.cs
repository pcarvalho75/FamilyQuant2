using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FamilyQuant.DeNovo;
using FamilyQuant.Operations;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int BusyScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        {
            Themes.ThemeManager.Apply(theme);
            var main = new MainWindow { Width = 900, Height = 600, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            main.Show();
            try
            {
                using var operation = main.Operations.Begin("Synthetic operation fixture");
                operation.Context.Report("read", "Reading the selected file. The total is not known yet.");
                PumpDispatcher();
                Thread.Sleep(350);
                PumpDispatcher();
                ScreenHarness.Save(main, Path.Combine(output, $"busy-{theme}-unknown.png"), 900, 600);
                operation.Context.Report("read", "Reading sequences from the selected file.", 31777, 63554);
                PumpDispatcher();
                ScreenHarness.Save(main, Path.Combine(output, $"busy-{theme}-progress.png"), 900, 600);
                main.Operations.Cancel();
                PumpDispatcher();
                ScreenHarness.Save(main, Path.Combine(output, $"busy-{theme}-cancelling.png"), 900, 600);
            }
            finally { main.Close(); }
        }
        Console.WriteLine("Busy overlay: 6 synthetic captures, both themes, 900 by 600: " + output);
        return 0;
    }

    private static void BusyOperationTests(string workDir)
    {
        EnsureApplication();
        Section("Shared busy operation path");
        Check("shell exposes its shared operation host", typeof(MainWindow).GetProperty("Operations") != null);
        var build = new USearchLib();
        typeof(USearchLib).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(build, new object[] { "fixture" });
        try
        {
            var status = (System.Windows.Controls.TextBlock)typeof(USearchLib).GetField("TextBlockUSearchState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(build)!;
            Check("Build no longer tells users an operation cannot be interrupted", !status.Text.Contains("cannot be interrupted", StringComparison.Ordinal));
        }
        finally { typeof(USearchLib).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(build, null); }
        HostLifetimeTests(workDir);
        BusyBuildCancellationTest(workDir);
        BusyDocumentTests(workDir);
        BusyDeNovoTests(workDir);
        BusyExportTests(workDir);
        BusyResultsPreparationTest();
        QuantifyViewModelTests(workDir);
    }

    private static void FinishBusyTask(Task task)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            PumpDispatcher();
            Thread.Sleep(1);
        }
        if (!task.IsCompleted) throw new TimeoutException("Owned fixture operation did not finish.");
        task.GetAwaiter().GetResult();
        PumpDispatcher();
    }

    private static void FinishBusyOperation(AppOperationHost host)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (host.IsBusy && watch.Elapsed < TimeSpan.FromSeconds(30)) { PumpDispatcher(); Thread.Sleep(1); }
        if (host.IsBusy) throw new TimeoutException("Owned operation remained busy.");
        PumpDispatcher();
    }

    private static void HostLifetimeTests(string workDir)
    {
        var main = new MainWindow();
        var host = main.Operations;
        using (var lease = host.Begin("Fixture progress"))
        {
            lease.Context.Report("fixture", "Half complete", 1, 2);
            Check("one shared overlay displays determinate progress and blocks shell navigation", host.IsBusy && host.Percent == 50
                && !host.IsIndeterminate && !main.NavigationModel.CanNavigate(ShellDestination.Start));
            Check("operation host rejects an overlapping operation", Throws<InvalidOperationException>(() => host.Begin("overlap")));
            host.CancelCommand.Execute(null);
            lease.Report(new("stale", "Queued progress must not replace cancellation", 2, 2));
            Check("Cancel is idempotent and queued progress cannot replace its status", host.IsCancelling
                && host.Message.StartsWith("Cancelling") && !host.CancelCommand.CanExecute(null));
        }
        Check("busy state lasts until work ends and clears after disposal", !host.IsBusy && main.NavigationModel.CanNavigate(ShellDestination.Start));
        var old = host.Begin("Old operation");
        FinishBusyTask(Task.Run(() => old.Report(new("old", "Late old callback"))));
        old.Dispose();
        using (var next = host.Begin("New operation"))
        {
            old.Report(new("old", "Late old callback"));
            Check("a disposed lease cannot update a later operation", host.Message == "New operation");
            next.Context.Commit(() => { });
            host.Cancel();
            Check("successful atomic commit wins a late Cancel", next.Context.HasCommitted && !host.IsCancelling);
        }
        main.Close();
    }

    private static void BusyBuildCancellationTest(string workDir)
    {
        string input = Path.Combine(workDir, "busy-build.fasta"), output = Path.ChangeExtension(input, ".fcf");
        File.WriteAllText(input, ">fixture\nPEPTIDEK\n");
        File.WriteAllText(output, "previous curated file");
        byte[] before = File.ReadAllBytes(output);
        var oldBuild = USearchLib.BuildOverride;
        var oldConfirm = USearchLib.ConfirmOverwriteOverride;
        var pending = new TaskCompletionSource<FamilyClusterFile>();
        USearchLib.BuildOverride = (_, _, _) => pending.Task;
        USearchLib.ConfirmOverwriteOverride = _ => true;
        try
        {
            var view = new USearchLib();
            view.ViewModel.DatabasePath = input;
            ((Button)view.FindName("ButtonBuildFamilies")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var host = AppOperationHost.For(view);
            Check("the actual Build command enters the shared operation path", host.IsBusy && view.ViewModel.IsBusy);
            view.CancelOperation();
            pending.SetResult(new FamilyClusterFile());
            PumpDispatcher();
            Check("cancelling a running Build preserves prior file bytes and refuses attachment", File.ReadAllBytes(output).SequenceEqual(before)
                && view.fcf == null && !view.ViewModel.IsBusy && !host.IsBusy && view.ViewModel.Status.StartsWith("Cancelled"));
        }
        finally { USearchLib.BuildOverride = oldBuild; USearchLib.ConfirmOverwriteOverride = oldConfirm; }
    }

    private static void BusyDocumentTests(string workDir)
    {
        string path = Path.Combine(workDir, "busy-project.fqproj");
        File.WriteAllText(path, "previous project bytes");
        byte[] before = File.ReadAllBytes(path);
        var curation = new FamilyClusterFile();
        var manifest = new QuantManifest { FormatVersion = 1 };
        var host = new AppOperationHost();
        using (var lease = host.Begin("Save fixture"))
        {
            var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            bool cancelled = false;
            var save = Task.Run(() =>
            {
                try
                {
                    ShellProjectOperations.WriteVerifiedProject(path, curation, new(), manifest,
                        _ => ui.Invoke(host.Cancel), lease.Context);
                }
                catch (OperationCanceledException) { cancelled = true; }
            });
            FinishBusyTask(save);
            Check("cancel after staging preserves project bytes, manifest and save check", cancelled && File.ReadAllBytes(path).SequenceEqual(before)
                && manifest.FormatVersion == 1 && manifest.SpectrumComparisonRowCount == null && curation.LastSaveCheck == null);
            Check("cancelled verified save removes its owned staging files", Directory.GetFiles(workDir, "busy-project.fqproj.saving-*").Length == 0);
        }
        foreach (string action in new[] { "clustering", "identity scan", "identity search", "redundancy", "FCF export", "homology" })
        {
            using var lease = host.Begin(action);
            host.Cancel();
            bool stopped = Throws<OperationCanceledException>(() =>
            {
                switch (action)
                {
                    case "clustering": USearchTools.GenerateFCF(new FileInfo(path), 85, operation: lease.Context); break;
                    case "identity scan": FamilyClusterFile.ScanIdentities(path, operation: lease.Context); break;
                    case "identity search": FamilyClusterFile.DivideAndConquerDetailed(path, operation: lease.Context); break;
                    case "redundancy": RedundancyTools.RemoveRedundancy(new FileInfo(path), 100, path, lease.Context); break;
                    case "FCF export": curation.Save(path, lease.Context); break;
                    case "homology": DeNovoScreenOperations.SearchHomology(new[] { "PEPTIDEK" }, curation, new(), path, lease.Context); break;
                }
            });
            Check("shared Cancel reaches Core " + action + " without replacing the prior file", stopped && File.ReadAllBytes(path).SequenceEqual(before));
        }
        foreach (bool folder in new[] { false, true })
        {
            using var lease = host.Begin("Staged export");
            string target = folder ? Path.Combine(workDir, "busy-exports") : path;
            if (folder) { Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "result.csv"), "previous"); }
            bool stopped = Throws<OperationCanceledException>(() =>
            {
                void Write(string temporary)
                {
                    File.WriteAllText(folder ? Path.Combine(temporary, "result.csv") : temporary, "new result");
                    host.Cancel();
                }
                if (folder) AppOperationFiles.WriteFolder(target, Write, lease.Context);
                else AppOperationFiles.WriteFile(target, Write, lease.Context);
            });
            Check("cancelled " + (folder ? "folder" : "file") + " export leaves prior bytes and removes staging", stopped
                && (folder ? File.ReadAllText(Path.Combine(target, "result.csv")) == "previous" : File.ReadAllBytes(path).SequenceEqual(before))
                && !Directory.EnumerateFileSystemEntries(workDir, ".fq-export-*").Any());
        }
        var main = new MainWindow();
        main.Operations.PropertyChanged += (_, _) => { if (main.Operations.IsBusy) main.Operations.Cancel(); };
        Task<bool> open = main.OpenProjectSafelyAsync(path);
        FinishBusyTask(open);
        Check("cancelled shell Open retains its previous document and clears the overlay", !open.Result && main.NavigationModel.Document == null
            && !main.Operations.IsBusy && main.StatusModel.ContextHelp.Contains("Cancelled"));
        main.Close();
    }

    private static void BusyDeNovoTests(string workDir)
    {
        string proposalFolder = Path.Combine(Path.GetTempPath(), "FamilyQuant.Busy.DeNovo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(proposalFolder);
        File.WriteAllText(Path.Combine(proposalFolder, "fixture.denovo.csv"), "Filename-only proposal fixture; the import seam supplies rows.");
        var previous = new DeNovoImport { RowsRead = 1, Kept = new() { new PeaksDeNovoRow { Peptide = "PEPTIDEK" } } };
        var waiting = new TaskCompletionSource<DeNovoImport>();
        var model = new DeNovoViewModel { ImportDirectory = proposalFolder, ImportOverride = (_, _) => waiting.Task };
        model.ApplyImport(previous);
        Task running = model.ImportAsync();
        Check("De novo import uses the common busy path and whole-screen edit guard", model.Operations.IsBusy && model.IsBusy && !model.CanEdit);
        model.Operations.Cancel();
        waiting.SetResult(new DeNovoImport());
        FinishBusyTask(running);
        Check("cancelled De novo import preserves the exact previous evidence object", ReferenceEquals(model.LastImport, previous)
            && model.CanEdit && !model.Operations.IsBusy && model.Status.StartsWith("Cancelled"));
        string package = Path.Combine(workDir, "busy-sepro-marker.sepr2");
        File.WriteAllText(package, "Not parsed: cancellation must precede the vendor call.");
        model.SeproPackage = package;
        model.HomologyFcf = package;
        model.Operations.PropertyChanged += (_, _) => { if (model.Operations.IsBusy) model.Operations.Cancel(); };
        FinishBusyTask(model.ReconcileAsync());
        Check("De novo reconciliation observes shared Cancel before vendor I/O and keeps prior rows", model.LastReconciliation == null
            && ReferenceEquals(model.LastImport, previous) && model.Status.StartsWith("Cancelled") && model.CanEdit);
        FinishBusyTask(model.SearchAsync());
        Check("De novo homology observes shared Cancel and keeps prior evidence", model.LastHomology == null
            && ReferenceEquals(model.LastImport, previous) && model.Status.StartsWith("Cancelled") && model.CanEdit);
    }

    private static void BusyExportTests(string workDir)
    {
        var model = new AnalyticsViewModel(new[]
        {
            new QuantRow { Run = "synthetic", Side = "identified", Sequence = "PEPTIDEK", Charge = 2,
                PrecursorMz = 500, IsotopeAreas = new[] { 10d, 5d, 2d }, EnvelopeSum = 17 }
        }, new QuantManifest { DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method, PerRunTic = new() { ["synthetic"] = 100 } });
        var host = new AppOperationHost();
        foreach (var kind in new[] { ResultsExportKind.Summary, ResultsExportKind.Peptides, ResultsExportKind.Figures })
        {
            model.ExportKind = kind;
            string first = Path.Combine(workDir, "busy-export-first-" + kind), second = Path.Combine(workDir, "busy-export-second-" + kind);
            model.Export(first);
            FinishBusyTask(model.ExportAsync(second, host));
            bool equal = kind == ResultsExportKind.Figures
                ? Directory.GetFiles(first).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal)
                    .SequenceEqual(Directory.GetFiles(second).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal))
                    && Directory.GetFiles(first).All(file => File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(second, Path.GetFileName(file)))))
                : File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(second));
            Check("shared " + kind + " export preserves native bytes through staging and its owned worker", equal && !host.IsBusy
                && model.Status.StartsWith("Written to"), model.Status);
        }
    }

    private static void BusyResultsPreparationTest()
    {
        var main = new MainWindow();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("loadedQuant", flags)!.SetValue(main, new System.Collections.Generic.List<QuantRow>
        {
            new() { Run = "synthetic", Side = "identified", Sequence = "PEPTIDEK", Charge = 2,
                PrecursorMz = 500, IsotopeAreas = new[] { 10d, 5d, 2d }, EnvelopeSum = 17 }
        });
        try
        {
            typeof(MainWindow).GetMethod("PresentResults", flags)!.Invoke(main, null);
            FinishBusyOperation(main.Operations);
            var results = ((Grid)main.FindName("GridApp")).Children.OfType<AnalyticsControl>().SingleOrDefault();
            Check("headless Results preparation returns its stored rows to the WPF thread", results?.ViewModel.Data?.Peptides.Count == 1
                && !main.Operations.IsBusy, main.StatusModel.ContextHelp);
        }
        finally { main.Close(); }
    }
}
