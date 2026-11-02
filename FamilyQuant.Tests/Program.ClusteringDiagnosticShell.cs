using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ClusteringDiagnosticShellTests(string workDir)
    {
        EnsureApplication();
        Section("Read-only clustering diagnostic shell");
        string outputParent = Path.Combine(workDir, "diagnostic-output");
        Directory.CreateDirectory(outputParent);
        string path = Path.Combine(workDir, "diagnostic-source.fcf");
        var fixture = new FamilyClusterFile { ClusteringIdentity = 73, MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "diagnostic-A", Sequence = "PEPTIDEK", Description = "Synthetic diagnostic fixture", UserID = 4, USearchID = 4 },
            new() { SequenceIdentifier = "diagnostic-B", Sequence = "PEPTIDER", Description = "Synthetic diagnostic fixture", UserID = 7, USearchID = 7 }
        } };
        fixture.Save(path);
        byte[] inputBytes = File.ReadAllBytes(path);
        var oldSettings = MainWindow.ShellSettingsPathOverride;
        var oldFolder = ShellDialogs.FolderPresenter;
        var oldMessage = ShellMessages.Presenter;
        MainWindow.ShellSettingsPathOverride = Path.Combine(workDir, "diagnostic-preferences.json");
        var messages = new List<ShellMessage>();
        Action<ShellMessage> observe = messages.Add;
        ShellMessages.Reported += observe;
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        var main = new MainWindow { Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
        var menu = (MenuItem)main.FindName("MenuItemClusteringDiagnostic");
        var analysis = ((Menu)main.FindName("MainMenu")).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Analysis"));
        int runnerCalls = 0, reports = 0, folders = 0;
        ClusteringDiagnosticRequest? seen = null;
        string? shownText = null;
        main.ClusteringDiagnosticReportPresenterOverride = (_, text) => { reports++; shownText = text; };
        main.ClusteringDiagnosticFolderOpenerOverride = _ => folders++;
        Task<ClusteringDiagnosticShellResult> Succeed(ClusteringDiagnosticRequest request, OperationContext context)
        {
            runnerCalls++; seen = request;
            using var reader = new FileStream(request.DocumentPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Check("diagnostic input guard permits a concurrent read-only " + Path.GetExtension(request.DocumentPath) + " reader", reader.Length > 0);
            Check("diagnostic runner receives the shared busy operation with menu editing blocked",
                main.Operations.IsBusy && !((Menu)main.FindName("MainMenu")).IsEnabled);
            context.Report("fixture-diagnostic", "Comparing the saved clustering fixture.", 1, 2);
            Check("diagnostic progress reaches the shared overlay", main.Operations.Message == "Comparing the saved clustering fixture."
                && main.Operations.Percent == 50 && main.Operations.CancelCommand.CanExecute(null));
            Directory.CreateDirectory(request.OutputFolder);
            File.WriteAllText(Path.Combine(request.OutputFolder, "report.txt"), "Synthetic diagnostic result.");
            return Task.FromResult(new ClusteringDiagnosticShellResult("Synthetic diagnostic result.", request.OutputFolder));
        }
        main.ClusteringDiagnosticRunnerOverride = Succeed;
        bool Run(string? parent = null)
        {
            var task = main.RunClusteringDiagnosticAsync(parent ?? outputParent);
            FinishBusyTask(task);
            return task.Result;
        }
        void AwaitRunner(Func<bool> ready)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && deadline.Elapsed < TimeSpan.FromSeconds(10)) { PumpDispatcher(); Thread.Sleep(1); }
            if (!ready()) throw new TimeoutException("Diagnostic fixture did not reach its owned runner.");
        }
        try
        {
            Check("Analysis exposes a visible Clustering diagnostic action and disables it without a project",
                analysis.Items.Contains(menu) && Equals(menu.Header, "Clustering diagnostic") && menu.Visibility == Visibility.Visible && !menu.IsEnabled);
            Check("diagnostic refuses a missing project without invoking its runner", !Run() && runnerCalls == 0
                && main.StatusModel.ContextHelp.Contains("Open a saved", StringComparison.Ordinal));
            Check("diagnostic fixture opens its saved source", main.OpenProjectPath(path));
            var document = main.NavigationModel.Document!;
            string originalState = ShellDocumentState.Fingerprint(document);
            Check("a saved unchanged source enables the Analysis diagnostic", menu.IsEnabled);
            int choices = 0;
            main.ClusteringDiagnosticIdentityChoiceOverride = () => { choices++; return 91; };
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            Check("diagnostic succeeds with a read-only saved .fcf", Run());
            Check("diagnostic uses the saved identity and a fresh child while keeping optional analyses off",
                seen != null && seen.DocumentPath == Path.GetFullPath(path) && seen.IdentityPercent == 73
                && !seen.SmallExhaustive && seen.MatchingFasta == null && choices == 0
                && Path.GetDirectoryName(seen.OutputFolder) == Path.GetFullPath(outputParent)
                && Directory.EnumerateFileSystemEntries(seen.OutputFolder).Any());
            Check("successful diagnostic presents its exact returned text and output folder once", reports == 1 && folders == 1
                && shownText == "Synthetic diagnostic result." && main.StatusModel.ContextHelp.Contains("report written", StringComparison.Ordinal));
            Check("diagnostic preserves input bytes, open document identity, grouping and saved state",
                File.ReadAllBytes(path).SequenceEqual(inputBytes) && ReferenceEquals(main.NavigationModel.Document, document)
                && ShellDocumentState.Fingerprint(document) == originalState && !main.DocumentState.IsModified && !main.Operations.IsBusy);
            string firstOutput = seen!.OutputFolder;
            Check("each diagnostic receives another fresh output folder", Run() && seen!.OutputFolder != firstOutput);

            document.MyExtendedFastaItems[0].UserAnnotation = "unsaved diagnostic fixture";
            int beforeRefusal = runnerCalls;
            Check("diagnostic detects unsaved curation instead of silently examining an older save", !Run()
                && runnerCalls == beforeRefusal && main.DocumentState.IsModified && !menu.IsEnabled
                && File.ReadAllBytes(path).SequenceEqual(inputBytes) && main.StatusModel.ContextHelp.Contains("Save your changes", StringComparison.Ordinal));
            document.MyExtendedFastaItems[0].UserAnnotation = "";
            main.DocumentState.CheckForChanges(document);
            using (var busy = main.Operations.Begin("Other fixture operation"))
                Check("diagnostic refuses another active operation and leaves its lease active", !Run() && runnerCalls == beforeRefusal && main.Operations.IsBusy);
            var pathField = typeof(MainWindow).GetField("lastFcfPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
            pathField.SetValue(main, null);
            Check("diagnostic refuses an open document without a saved path", !Run() && runnerCalls == beforeRefusal);
            pathField.SetValue(main, Path.Combine(workDir, "missing-diagnostic.fcf"));
            Check("diagnostic refuses a missing saved input", !Run() && runnerCalls == beforeRefusal);
            pathField.SetValue(main, Path.GetFullPath(path));

            int priorReports = reports, priorFolders = folders;
            var waiting = new TaskCompletionSource<ClusteringDiagnosticShellResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            OperationContext? heldContext = null;
            main.ClusteringDiagnosticRunnerOverride = (request, context) => { seen = request; heldContext = context; return waiting.Task; };
            var cancelled = main.RunClusteringDiagnosticAsync(outputParent);
            AwaitRunner(() => heldContext != null);
            main.Operations.Cancel();
            Check("shared Cancel reaches the diagnostic runner token", heldContext!.IsCancellationRequested && main.Operations.IsCancelling);
            waiting.SetResult(new("A late result must not open.", seen!.OutputFolder));
            FinishBusyTask(cancelled);
            Check("cancelled diagnostic suppresses report and folder publication and retains input bytes", !cancelled.Result
                && reports == priorReports && folders == priorFolders && File.ReadAllBytes(path).SequenceEqual(inputBytes)
                && !main.Operations.IsBusy && !main.DocumentState.IsModified && main.StatusModel.ContextHelp.Contains("Cancelled", StringComparison.Ordinal));
            main.ClusteringDiagnosticRunnerOverride = (_, _) => Task.FromException<ClusteringDiagnosticShellResult>(new InvalidOperationException("diagnostic fixture failed"));
            Check("failed diagnostic reports the failure without opening a report or folder", !Run() && reports == priorReports && folders == priorFolders
                && File.ReadAllBytes(path).SequenceEqual(inputBytes) && !main.Operations.IsBusy && main.StatusModel.ContextHelp.Contains("diagnostic fixture failed", StringComparison.Ordinal));
            main.ClusteringDiagnosticRunnerOverride = (request, _) => Task.FromResult(new ClusteringDiagnosticShellResult("", request.OutputFolder));
            Check("an incomplete result cannot claim a completed report", !Run() && reports == priorReports && folders == priorFolders);

            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            main.ClusteringDiagnosticRunnerOverride = (request, context) =>
            {
                bool rejectedWrite = false;
                try { using var writer = new FileStream(request.DocumentPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }
                catch (IOException) { rejectedWrite = true; }
                Check("diagnostic read-share guard rejects a second writer even when input attributes allow writing", rejectedWrite);
                context.Commit(() =>
                {
                    Directory.CreateDirectory(request.OutputFolder);
                    File.WriteAllText(Path.Combine(request.OutputFolder, "report.txt"), "Committed diagnostic result.");
                    main.Operations.Cancel();
                });
                Check("the publication fixture requests cancellation during a successful final commit", context.HasCommitted && context.IsCancellationRequested);
                return Task.FromResult(new ClusteringDiagnosticShellResult("Committed diagnostic result.", request.OutputFolder));
            };
            Check("successful committed diagnostic publication wins a cancellation during its final publish", Run()
                && reports == priorReports + 1 && folders == priorFolders + 1 && shownText == "Committed diagnostic result."
                && File.ReadAllBytes(path).SequenceEqual(inputBytes) && !main.Operations.IsBusy
                && main.StatusModel.ContextHelp.Contains("report written", StringComparison.Ordinal));
            priorReports = reports; priorFolders = folders;
            main.ClusteringDiagnosticRunnerOverride = (request, context) =>
            {
                context.Commit(() => { Directory.CreateDirectory(request.OutputFolder); main.Operations.Cancel(); });
                return Task.FromException<ClusteringDiagnosticShellResult>(new OperationCanceledException(context.CancellationToken));
            };
            Check("an exception after output commit reports retained output rather than claiming cancellation", !Run()
                && reports == priorReports && folders == priorFolders && !main.Operations.IsBusy
                && main.StatusModel.ContextHelp.Contains("output was written", StringComparison.Ordinal)
                && !main.StatusModel.ContextHelp.Contains("Cancelled.", StringComparison.Ordinal));

            string unknownPath = Path.Combine(workDir, "diagnostic-unknown-identity.fcf");
            fixture.ClusteringIdentity = 0; fixture.Save(unknownPath);
            Check("unknown-identity fixture opens", main.OpenProjectPath(unknownPath));
            main.ClusteringDiagnosticRunnerOverride = Succeed;
            main.ClusteringDiagnosticIdentityChoiceOverride = () => null;
            beforeRefusal = runnerCalls;
            Check("unknown saved identity requires an explicit user choice", !Run() && runnerCalls == beforeRefusal);
            main.ClusteringDiagnosticIdentityChoiceOverride = () => 0;
            Check("an invalid chosen threshold never reaches the diagnostic", !Run() && runnerCalls == beforeRefusal);
            main.Show(); PumpDispatcher();
            int cancelledChoiceBrowses = 0;
            ShellDialogs.FolderPresenter = (_, _, dialog) => { cancelledChoiceBrowses++; dialog.FolderName = outputParent; return true; };
            main.ClusteringDiagnosticIdentityChoiceOverride = () => { main.Operations.Cancel(); return 81; };
            var cancelledChoice = main.RunClusteringDiagnosticAsync();
            FinishBusyTask(cancelledChoice);
            Check("cancellation during threshold choice stops before output browse and runner invocation", !cancelledChoice.Result
                && cancelledChoiceBrowses == 0 && runnerCalls == beforeRefusal && !main.Operations.IsBusy
                && main.StatusModel.ContextHelp.Contains("Cancelled.", StringComparison.Ordinal));
            var unknownDocument = main.NavigationModel.Document!;
            main.ClusteringDiagnosticIdentityChoiceOverride = () => { unknownDocument.MyExtendedFastaItems[0].UserAnnotation = "changed during choice"; return 81; };
            Check("a document change during threshold choice is refused before invoking the runner", !Run() && runnerCalls == beforeRefusal
                && unknownDocument.MyExtendedFastaItems[0].UserAnnotation == "changed during choice");
            unknownDocument.MyExtendedFastaItems[0].UserAnnotation = "";
            main.DocumentState.CheckForChanges(unknownDocument);
            main.ClusteringDiagnosticIdentityChoiceOverride = () => 81;
            Check("an explicit threshold choice reaches the diagnostic without changing saved metadata", Run() && seen!.IdentityPercent == 81
                && main.NavigationModel.Document!.ClusteringIdentity == 0 && FamilyClusterFile.Load(unknownPath).ClusteringIdentity == 0);

            string project = Path.Combine(workDir, "diagnostic-source.fqproj");
            fixture.ClusteringIdentity = 89;
            ProjectStore.WriteFqproj(project, fixture, Array.Empty<QuantRow>(), new QuantManifest { Provenance = QuantManifest.DefaultProvenance() });
            byte[] projectBytes = File.ReadAllBytes(project);
            Check("project-container diagnostic fixture opens", main.OpenProjectPath(project));
            Check("diagnostic reads the saved .fqproj identity and keeps the container bytes", Run() && seen!.IdentityPercent == 89
                && seen.DocumentPath == Path.GetFullPath(project) && File.ReadAllBytes(project).SequenceEqual(projectBytes));

            main.RememberFolder(outputParent);
            string? initialDirectory = null, dialogId = null;
            ShellDialogs.FolderPresenter = (_, id, dialog) => { dialogId = id; initialDirectory = dialog.InitialDirectory; dialog.FolderName = outputParent; return true; };
            main.Show(); PumpDispatcher();
            int beforeMenu = runnerCalls;
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            FinishBusyOperation(main.Operations);
            Check("the reachable Analysis menu action uses ShellDialogs and its remembered output parent", runnerCalls == beforeMenu + 1
                && dialogId == "clustering.diagnostic" && initialDirectory == Path.GetFullPath(outputParent));
            Check("diagnostic flow keeps the project saved and reports meaningful refusal, cancellation and failure messages",
                !main.DocumentState.IsModified && messages.Any(message => message.Text.Contains("Save your changes", StringComparison.Ordinal))
                && messages.Any(message => message.Text.StartsWith("Cancelled.", StringComparison.Ordinal))
                && messages.Any(message => message.Image == MessageBoxImage.Error));
        }
        finally
        {
            if (File.Exists(path)) File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            main.Close();
            ShellMessages.Reported -= observe;
            ShellMessages.Presenter = oldMessage; ShellDialogs.FolderPresenter = oldFolder;
            MainWindow.ShellSettingsPathOverride = oldSettings;
        }
    }
}
