using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record ShellRouteDialog(string Id, string Type, string Filter, string Title,
        string DefaultExt, bool AddExtension, string FileName, bool Accepted);
    private sealed record ShellRouteCase(string Id, string Menu, string Header, string Dispatch, string Outcome,
        bool Passed, List<ShellRouteDialog> Dialogs, List<ShellMessage> Messages, string Status,
        string? ExceptionMessage = null, string? SavedCheck = null,
        Dictionary<string, string>? Bindings = null, Dictionary<string, bool>? Branches = null, string ConsoleText = "");

    private static int ShellRouteProbe(string output)
    {
        string root = Path.GetFullPath(output);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Native route output must be fresh.");
        Directory.CreateDirectory(root);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        EnsureApplication();
        var originalFile = ShellDialogs.FilePresenter;
        var originalMessage = ShellMessages.Presenter;
        var originalOpen = MainWindow.SelectOpenPathOverride;
        var originalSave = MainWindow.SelectSaveProjectPathOverride;
        var originalSettings = MainWindow.ShellSettingsPathOverride;
        var originalChoice = MainWindow.UnsavedProjectChoiceOverride;
        var originalCompute = MainWindow.ComputeDialogOverride;
        MainWindow.SelectOpenPathOverride = MainWindow.SelectSaveProjectPathOverride = null;
        MainWindow.ComputeDialogOverride = null;
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        MainWindow.ShellSettingsPathOverride = Path.Combine(root, "preferences.json");
        string input = Path.Combine(root, "input.fcf"), exported = Path.Combine(root, "export.fcf"),
            saved = Path.Combine(root, "saved.fqproj"), corrupt = Path.Combine(root, "corrupt.fcf");
        var fixture = new FamilyClusterFile { ClusteringIdentity = 70, LockedUserIds = new() { 7 }, MyExtendedFastaItems = new()
        { new() { SequenceIdentifier = "shell_route_fixture", Description = "Synthetic shell route fixture", Sequence = "PEPTIDEK",
            UserID = 7, USearchID = 1, UserAnnotation = "Native fixture annotation" } } };
        fixture.Save(input); File.WriteAllText(corrupt, "{controlled-invalid-fixture", new UTF8Encoding(false));
        string inputHash = FeatureLedgerEvidence.Hash(input);
        var cases = new List<ShellRouteCase>();
        var dialogs = new List<ShellRouteDialog>();
        var messages = new List<ShellMessage>();
        var messageThreads = new List<int>();
        int uiThread = Environment.CurrentManagedThreadId;
        string nextFile = input;
        bool accept = false;
        string consoleText = "";
        ShellDialogs.FilePresenter = (_, id, dialog) =>
        {
            dialog.FileName = nextFile;
            dialogs.Add(new(id, dialog.GetType().FullName!, dialog.Filter, dialog.Title, dialog.DefaultExt,
                dialog.AddExtension, dialog.FileName, accept));
            return accept;
        };
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        void Observe(ShellMessage message) { messages.Add(message); messageThreads.Add(Environment.CurrentManagedThreadId); }
        ShellMessages.Reported += Observe;
        var main = OffscreenProofWindow(new MainWindow { Width = 1024, Height = 768 });
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
        Grid Host() => (Grid)main.FindName("GridApp");
        string CurrentView() => Host().Children.Count == 1 ? Host().Children[0].GetType().FullName! : "ambiguous";
        string Dispatch(string menuName)
        {
            var console = Console.Out;
            using var capture = new StringWriter(CultureInfo.InvariantCulture);
            Console.SetOut(new LedgerProofWriter(console, capture));
            try
            {
            if (!main.Dispatcher.CheckAccess() || SynchronizationContext.Current is not DispatcherSynchronizationContext)
                throw new InvalidOperationException("Invoke native menu actions with their real dispatcher context.");
            if (menuName == "MenuItemSaveFCF")
            {
                ChooseExport(main, ProjectExportKind.Families); FinishBusyOperation(main.Operations); PumpDispatcher();
                return "File > Export > Families (.fcf)";
            }
            var item = (MenuItem)main.FindName(menuName);
            if (!item.IsEnabled) throw new InvalidOperationException("Native route is disabled: " + menuName);
            if (item.Command is RoutedCommand routed)
            {
                if (!routed.CanExecute(item.CommandParameter, main)) throw new InvalidOperationException("Native command cannot execute: " + menuName);
                routed.Execute(item.CommandParameter, main);
            }
            else if (item.Command is ICommand command)
            {
                if (!command.CanExecute(item.CommandParameter)) throw new InvalidOperationException("Native command cannot execute: " + menuName);
                command.Execute(item.CommandParameter);
            }
            else item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            FinishBusyOperation(main.Operations); PumpDispatcher();
            return item.Command is RoutedCommand route ? "RoutedCommand:" + route.Name : item.Command == null ? "native Click event" : "ICommand";
            }
            finally { Console.SetOut(console); consoleText = capture.ToString(); }
        }
        string InvokeGuard(string method)
        {
            typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, [main, new RoutedEventArgs()]);
            FinishBusyOperation(main.Operations); PumpDispatcher();
            return "native handler guard (disabled menu bypassed):" + method;
        }
        void Reset() { dialogs.Clear(); messages.Clear(); messageThreads.Clear(); consoleText = ""; }
        void Record(string id, string menu, string dispatch, string outcome, bool result, string? exception = null,
            Dictionary<string, string>? bindings = null, Dictionary<string, bool>? branches = null)
        {
            var item = (MenuItem)main.FindName(menu == "MenuItemSaveFCF" ? "MenuItemExport" : menu);
            bool preserved = FeatureLedgerEvidence.Hash(input) == inputHash && messageThreads.All(thread => thread == uiThread);
            Check("native shell route " + id + ": " + outcome, result && preserved);
            cases.Add(new(id, menu, menu == "MenuItemSaveFCF" ? "Families (.fcf)" : item.Header?.ToString() ?? "", dispatch, outcome, result && preserved,
                dialogs.ToList(), messages.ToList(), main.StatusModel.ContextHelp, exception,
                id == "project-success" ? main.DocumentState.SaveCheckText : null, bindings, branches, consoleText));
        }
        try
        {
            if (!HasProofWindowPlacement(main)) throw new InvalidOperationException("Native proof windows must be isolated before Show.");
            main.Show(); PumpDispatcher();
            Reset();
            var fileMenu = (MenuItem)main.FindName("MenuFile");
            Record("file-menu", "MenuFile", "attached native MenuItem", "File is the first attached menu and owns the project commands",
                ReferenceEquals(((Menu)main.FindName("MainMenu")).Items[0], fileMenu) && fileMenu.Header.ToString() == "File");
            foreach (var guard in new[]
            {
                ("fcf-empty", "MenuItemSaveFCF", "MenuItemSaveFCF_Click", "Nothing to save"),
                ("project-empty", "MenuItemSaveProject", "MenuItemSaveProject_Click", "Nothing to save"),
                ("sepro-empty", "MenuItemReanotateSEPro", "MenuItemReanotateSEPro_Click", "Load the families first"),
                ("spectrum-empty", "MenuItemSpectrumComparison", "MenuItemSpectrumComparison_Click", "No spectrum table yet")
            })
            {
                Reset();
                string guardDispatch = InvokeGuard(guard.Item3);
                Record(guard.Item1, guard.Item2, guardDispatch, "The actual no-project guard reports its refusal without a chooser or file write",
                    main.NavigationModel.Document == null && dialogs.Count == 0 && messages.Count == 1
                    && messages[0].Title == guard.Item4 && !File.Exists(saved) && !File.Exists(exported));
            }
            Reset();
            MainWindow.ComputeDialogOverride = _ => throw new InvalidOperationException("A no-project refusal must never create a compatibility window.");
            typeof(MainWindow).GetMethod("PresentCompute", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
            Record("compute-empty", "MenuItemComputeXic", "native compatibility presenter no-project guard; disabled navigation bypassed",
                "The retained compatibility presenter refuses a missing project before constructing a window",
                main.NavigationModel.Document == null && messages.Count == 1 && messages[0].Title == "Load a project first" && dialogs.Count == 0);
            MainWindow.ComputeDialogOverride = null;
            Reset(); accept = false;
            string dispatch = Dispatch("MenuItemLoadFQC");
            Record("open-cancel", "MenuItemLoadFQC", dispatch, "Open cancellation keeps the Start view and no document",
                dialogs.Count == 1 && dialogs[0].Id == "project.open" && main.NavigationModel.Document == null && CurrentView() == typeof(HomeControl).FullName);
            Reset(); accept = true;
            dispatch = Dispatch("MenuItemLoadFQC");
            var document = main.NavigationModel.Document;
            string documentBefore = document!.ToJsonText();
            // Save records the opened database bytes. Build the expectation from the
            // pre-Save snapshot so any other curation or provenance change still fails.
            string databaseFileName = Path.GetFileName(input), databaseFileSha256 = inputHash.ToLowerInvariant();
            var expectedSaved = FamilyClusterFile.FromJsonText(documentBefore);
            expectedSaved.ReportInputProvenance = new Report.ReportInputProvenance
            { DatabaseFileName = databaseFileName, DatabaseFileSha256 = databaseFileSha256 };
            string savedDocument = expectedSaved.ToJsonText();
            bool MatchesSavedDocument(FamilyClusterFile? candidate) =>
                candidate?.ReportInputProvenance?.DatabaseFileName == databaseFileName
                && candidate.ReportInputProvenance.DatabaseFileSha256 == databaseFileSha256
                && candidate.ToJsonText() == savedDocument;
            Record("open-fcf", "MenuItemLoadFQC", dispatch, "Open reads the selected FCF and retains its exact curation",
                dialogs.Count == 1 && documentBefore == fixture.ToJsonText() && CurrentView() == typeof(ClusterGenerationControl).FullName
                && ((FrameworkElement)Host().Children[0]).Name == "MyClusterGenerationControl");

            foreach (var route in new[] { ("home", "MenuItemHome", typeof(HomeControl)), ("denovo", "MenuItemDeNovo", typeof(DeNovoControl)),
                ("results", "MenuItemAnalytics", typeof(AnalyticsControl)) })
            {
                Reset(); dispatch = Dispatch(route.Item2);
                Record(route.Item1, route.Item2, dispatch, "Route reaches " + route.Item3.Name + " and preserves the same document",
                    CurrentView() == route.Item3.FullName && ReferenceEquals(document, main.NavigationModel.Document)
                    && document.ToJsonText() == documentBefore && dialogs.Count == 0);
            }
            Dispatch("MenuItemHome"); Reset();
            dispatch = Dispatch("MenuItemComputeXic");
            var quantify = Host().Children.OfType<QuantifyControl>().SingleOrDefault();
            Record("quantify", "MenuItemComputeXic", dispatch, "Compute opens the native Quantify screen without running the engine",
                quantify != null && !quantify.ViewModel.Computed && !quantify.ViewModel.IsBusy
                && ReferenceEquals(document, main.NavigationModel.Document) && document.ToJsonText() == documentBefore);
            var quantifyMenu = (MenuItem)main.FindName("MenuItemComputeXic");
            var analysis = ((Menu)main.FindName("MainMenu")).Items.OfType<MenuItem>().Single(item => item.Items.Contains(quantifyMenu));
            Reset();
            Record("analysis-quantify", "MenuItemComputeXic", "actual attached parent menu", analysis.Header.ToString()!, analysis.Header.ToString() == "View");
            Dispatch("MenuItemHome");

            // Exercise only the retained shell completion adapter. This seeded typed result is not
            // a computation or a secondary-window proof. The compatibility host is never shown.
            foreach (bool csvFailed in new[] { false, true })
            {
                var completion = new FullXicComputeResult { Rows = new(), Manifest = new(),
                    TicShareSummary = new() { "Owned synthetic TIC summary" } };
                bool csvWritten = completion.TryExportSpectrumComparisonCsv(csvFailed ? root : Path.Combine(root, "owned-spectrum.csv"));
                if (csvWritten == csvFailed) throw new InvalidOperationException("The owned optional CSV outcome differs from its intended branch.");
                MainWindow.ComputeDialogOverride = window =>
                {
                    OffscreenProofWindow(window);
                    typeof(ComputeXicViewModel).GetProperty(nameof(ComputeXicViewModel.Result))!.SetValue(window.ViewModel, completion);
                    return null;
                };
                Reset(); dispatch = Dispatch("MenuItemComputeXic");
                Record(csvFailed ? "compute-report-error" : "compute-report-csv", "MenuItemComputeXic", dispatch,
                    "The retained compatibility adapter reports the supplied typed result, optional CSV outcome and unsaved state without showing a window",
                    main.DocumentState.IsModified && ReferenceEquals(document, main.NavigationModel.Document)
                    && document.ToJsonText() == documentBefore && messages.Count == 1 && messages[0].Title == "Quantitation computed",
                    bindings: new()
                    {
                        ["window.ResultRows.Count.ToString(\"N0\",CultureInfo.CurrentCulture)"] = completion.Rows.Count.ToString("N0", CultureInfo.CurrentCulture),
                        ["string.Join(\"\\n\",window.TicShareSummary)"] = string.Join("\n", completion.TicShareSummary),
                        ["window.SpectrumComparisonCsvPath"] = completion.SpectrumComparisonCsvPath ?? "",
                        ["window.SpectrumComparisonCsvError"] = completion.SpectrumComparisonCsvError ?? ""
                    }, branches: new()
                    {
                        ["window.TicShareSummary.Count>0"] = completion.TicShareSummary.Count > 0,
                        ["string.IsNullOrWhiteSpace(window.SpectrumComparisonCsvPath)"] = string.IsNullOrWhiteSpace(completion.SpectrumComparisonCsvPath),
                        ["string.IsNullOrWhiteSpace(window.SpectrumComparisonCsvError)"] = string.IsNullOrWhiteSpace(completion.SpectrumComparisonCsvError)
                    });
                FinishBusyOperation(main.Operations);
            }
            MainWindow.ComputeDialogOverride = null;

            Reset(); nextFile = exported; accept = false;
            dispatch = Dispatch("MenuItemSaveFCF");
            Record("fcf-cancel", "MenuItemSaveFCF", dispatch, "FCF export cancellation leaves no file and unchanged curation",
                dialogs.Count == 1 && !File.Exists(exported) && document.ToJsonText() == documentBefore);
            Reset(); accept = true;
            dispatch = Dispatch("MenuItemSaveFCF");
            Record("fcf-success", "MenuItemSaveFCF", dispatch, "FCF export writes the exact IDs, annotations, locks and grouping",
                File.Exists(exported) && FamilyClusterFile.Load(exported).ToJsonText() == documentBefore
                && messages.Count == 1 && messages[0].Title == "Saved" && ReferenceEquals(document, main.NavigationModel.Document));
            byte[] originalExport = File.ReadAllBytes(exported);
            File.SetAttributes(exported, File.GetAttributes(exported) | FileAttributes.ReadOnly);
            try
            {
                string? expectedError = null;
                try { document.Save(exported); }
                catch (Exception ex) { expectedError = ex.Message; }
                Reset(); dispatch = Dispatch("MenuItemSaveFCF");
                Record("fcf-failure", "MenuItemSaveFCF", dispatch, "Failed FCF export keeps the previous bytes and document",
                    expectedError != null && originalExport.SequenceEqual(File.ReadAllBytes(exported)) && document.ToJsonText() == documentBefore
                    && messages.Count == 1 && messages[0].Title == "Could not save", expectedError);
            }
            finally { File.SetAttributes(exported, File.GetAttributes(exported) & ~FileAttributes.ReadOnly); }

            Reset(); nextFile = saved; accept = false;
            dispatch = Dispatch("MenuItemSaveProject");
            Record("project-cancel", "MenuItemSaveProject", dispatch, "Project save cancellation leaves no container",
                dialogs.Count == 1 && !File.Exists(saved) && document.ToJsonText() == documentBefore);
            Reset(); accept = true;
            dispatch = Dispatch("MenuItemSaveProject");
            var reopened = ProjectStore.ReadFqproj(saved);
            Record("project-success", "MenuItemSaveProject", dispatch, "Project save adds the exact opened database name and SHA256, preserves all other curation and the empty quantitation, and passes its saved-file check",
                MatchesSavedDocument(reopened.fcf) && MatchesSavedDocument(document) && reopened.quant.Count == 0 && !main.DocumentState.IsModified
                && main.DocumentState.SaveCheckText.Contains("passed", StringComparison.OrdinalIgnoreCase)
                && main.StatusModel.ContextHelp.Contains("The project was saved to", StringComparison.Ordinal));
            byte[] originalProject = File.ReadAllBytes(saved);
            File.SetAttributes(saved, File.GetAttributes(saved) | FileAttributes.ReadOnly);
            try
            {
                string? expectedError = null;
                try { ShellProjectOperations.WriteVerifiedProject(saved, document, new List<QuantRow>(), ShellProjectOperations.CreateManifest("fixture")); }
                catch (Exception ex) { expectedError = ex.Message; }
                Reset(); dispatch = Dispatch("MenuItemSaveProject");
                Record("project-failure", "MenuItemSaveProject", dispatch, "Failed project save keeps the previous container bytes and curation",
                    expectedError != null && originalProject.SequenceEqual(File.ReadAllBytes(saved)) && MatchesSavedDocument(document)
                    && messages.Count == 1 && messages[0].Title == "Could not save", expectedError);
            }
            finally { File.SetAttributes(saved, File.GetAttributes(saved) & ~FileAttributes.ReadOnly); }

            // A detached control has its own busy host. Its real queued empty attach publishes
            // FullXicReady now, while the shell host remains idle and can legitimately enter Save.
            // No readiness task is replaced by reflection, and no homology search is requested.
            var pendingControl = new ClusterGenerationControl(fixture);
            var detachedHost = pendingControl.Operations;
            using (var blocker = detachedHost.Begin("Owned pending-attach fixture"))
            {
                Task attach = pendingControl.SetFullXicQuantAsync(new List<QuantRow>(), null);
                void Field(string name, object? value) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, value);
                Field("cgc", pendingControl); Field("loadedCuration", fixture);
                Field("loadedQuant", new List<QuantRow> { new() });
                Field("loadedManifest", new QuantManifest { SpectrumComparisonRows = new() { new() } });
                Reset(); nextFile = saved; accept = true;
                var saveTask = (Task)typeof(MainWindow).GetMethod("SaveProjectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [true])!;
                bool actuallyWaited = !attach.IsCompleted && !pendingControl.FullXicReady.IsCompleted && !saveTask.IsCompleted && main.Operations.IsBusy;
                bool replaced = main.OpenProjectPath(input); // Existing raw compatibility seam bypasses UI busy navigation.
                blocker.Dispose(); FinishBusyTask(attach); FinishBusyTask(saveTask); PumpDispatcher();
                Record("project-stale", "MenuItemSaveProject", "native SaveProjectAsync(saveAs:true), real pending detached attach, raw OpenProjectPath replacement",
                    "Save waited for a real queued attach and refused the replaced bundle, preserving the previous project bytes",
                    actuallyWaited && replaced && attach.IsCompletedSuccessfully && saveTask.IsCompletedSuccessfully
                    && originalProject.SequenceEqual(File.ReadAllBytes(saved)) && main.NavigationModel.Document?.ToJsonText() == documentBefore
                    && messages.Count == 1 && messages[0].Title == "Nothing saved");
            }
            document = main.NavigationModel.Document!;

            // Prove the failure occurs in Build before any secondary-window call is reachable.
            var badRows = new List<QuantRow> { new() { DeNovoEvidence = new() { null! } } };
            string? buildError = null;
            try { DeNovo.SpectrumComparisonTable.Build(badRows); }
            catch (Exception ex) { buildError = ex.Message; }
            if (buildError == null) throw new InvalidOperationException("The owned spectrum fixture must fail before a window can be shown.");
            var quantField = typeof(MainWindow).GetField("loadedQuant", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var manifestField = typeof(MainWindow).GetField("loadedManifest", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var heldQuant = quantField.GetValue(main); var heldManifest = manifestField.GetValue(main);
            try
            {
                quantField.SetValue(main, badRows); manifestField.SetValue(main, null);
                typeof(MainWindow).GetMethod("RefreshProjectCommandAvailability", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(main, [true, false]);
                int windowCount = Application.Current.Windows.Count;
                Reset(); dispatch = Dispatch("MenuItemSpectrumComparison");
                Record("spectrum-failure", "MenuItemSpectrumComparison", dispatch,
                    "The actual shell catches the controlled Build failure before creating any spectrum window and preserves the document and file",
                    messages.Count == 1 && messages[0].Title == "Could not build spectrum table"
                    && Application.Current.Windows.Count == windowCount && document.ToJsonText() == documentBefore
                    && originalProject.SequenceEqual(File.ReadAllBytes(saved)), buildError);
            }
            finally { quantField.SetValue(main, heldQuant); manifestField.SetValue(main, heldManifest); }

            Reset(); nextFile = corrupt; accept = true;
            string? openError = null;
            try { ShellProjectOperations.Read(corrupt); }
            catch (Exception ex) { openError = ex.Message; }
            string previousView = CurrentView();
            dispatch = Dispatch("MenuItemLoadFQC");
            Record("open-failure", "MenuItemLoadFQC", dispatch, "Malformed input leaves the prior view, objects, curation and saved file intact",
                ReferenceEquals(document, main.NavigationModel.Document) && document.ToJsonText() == documentBefore && CurrentView() == previousView
                && openError != null && originalProject.SequenceEqual(File.ReadAllBytes(saved)) && messages.Count == 1
                && messages[0].Title == "Could not open the file", openError);
            Reset(); accept = false; nextFile = Path.Combine(root, "trusted-unused.sepr2");
            dispatch = Dispatch("MenuItemReanotateSEPro");
            Record("sepro-cancel", "MenuItemReanotateSEPro", dispatch, "SEPro chooser cancellation reads no package and retains curation",
                dialogs.Count == 1 && dialogs[0].Id == "sepro.reannotate" && document.ToJsonText() == documentBefore && messages.Count == 0);

            string packagePath = Path.Combine(root, "owned.sepr2"), regrouped = Path.Combine(root, "owned_regrouped.sepr2");
            CreateShellSeproFixture([("shell_route_fixture", 1)]).Save(packagePath);
            string packageHash = FeatureLedgerEvidence.Hash(packagePath);
            Reset(); nextFile = packagePath; accept = true;
            dispatch = Dispatch("MenuItemReanotateSEPro");
            Record("sepro-success", "MenuItemReanotateSEPro", dispatch, "SEPro writes only the separate regrouped copy with the curated family ID",
                File.Exists(regrouped) && FeatureLedgerEvidence.Hash(packagePath) == packageHash
                && ShellSeproProteinGroups(SeproPckg2.ResultPackage.Load(regrouped)).SequenceEqual(new[] { ("shell_route_fixture", 7) })
                && ShellSeproProteinGroups(SeproPckg2.ResultPackage.Load(packagePath)).SequenceEqual(new[] { ("shell_route_fixture", 1) })
                && document.ToJsonText() == documentBefore && messages.Count == 1 && messages[0].Title == "SEPro package reannotated");

            foreach (int missingCount in new[] { 1, 11 })
            {
                string missingPath = Path.Combine(root, "missing-" + missingCount + ".sepr2");
                var absent = Enumerable.Range(0, missingCount).Select(index => ("absent_" + index.ToString("D2", CultureInfo.InvariantCulture), 1)).ToArray();
                CreateShellSeproFixture(absent).Save(missingPath);
                string before = FeatureLedgerEvidence.Hash(missingPath);
                Reset(); nextFile = missingPath; accept = true;
                dispatch = Dispatch("MenuItemReanotateSEPro");
                bool ellipsis = messages.Count == 1 && messages[0].Text.EndsWith(", ...", StringComparison.Ordinal);
                Record("sepro-missing-" + missingCount, "MenuItemReanotateSEPro", dispatch, "Missing package loci prevent publication and preserve the input package",
                    FeatureLedgerEvidence.Hash(missingPath) == before && !File.Exists(Path.Combine(root, "missing-" + missingCount + "_regrouped.sepr2"))
                    && document.ToJsonText() == documentBefore && messages.Count == 1
                    && messages[0].Title == "The package and the database do not match" && ellipsis == (missingCount > 10)
                    && messages[0].Text.Contains(missingCount.ToString("N0", CultureInfo.InvariantCulture) + " protein(s)", StringComparison.Ordinal));
            }
            string invalidPackage = Path.Combine(root, "invalid.sepr2");
            File.WriteAllText(invalidPackage, "Owned invalid serialization fixture", new UTF8Encoding(false));
            string invalidHash = FeatureLedgerEvidence.Hash(invalidPackage);
            string? loadError = null;
            try { SeproPckg2.ResultPackage.Load(invalidPackage); }
            catch (Exception ex) { loadError = ex.Message; }
            Reset(); nextFile = invalidPackage; accept = true;
            dispatch = Dispatch("MenuItemReanotateSEPro");
            Record("sepro-failure", "MenuItemReanotateSEPro", dispatch, "Invalid controlled package reports the loader refusal without writing or changing curation",
                loadError != null && FeatureLedgerEvidence.Hash(invalidPackage) == invalidHash
                && !File.Exists(Path.Combine(root, "invalid_regrouped.sepr2")) && document.ToJsonText() == documentBefore
                && messages.Count == 1 && messages[0].Title == "Could not reannotate", loadError);

            Reset(); accept = true; nextFile = saved;
            dispatch = Dispatch("MenuItemLoadFQC");
            Record("open-project", "MenuItemLoadFQC", dispatch, "Open restores the exact saved database provenance and all other FQPROJ curation and its native curation view",
                MatchesSavedDocument(main.NavigationModel.Document) && CurrentView() == typeof(ClusterGenerationControl).FullName
                && ((FrameworkElement)Host().Children[0]).Name == "MyClusterGenerationControl");

            Reset(); dispatch = Dispatch("MenuItemRunUSearch");
            Record("new", "MenuItemRunUSearch", dispatch, "New reaches Build and clears the old in-memory bundle without changing either saved input",
                CurrentView() == typeof(USearchLib).FullName && main.NavigationModel.Document == null
                && originalProject.SequenceEqual(File.ReadAllBytes(saved)) && document.ToJsonText() == documentBefore);
            FeatureLedgerEvidence.WriteJson(Path.Combine(root, "native-cases.json"), cases);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }
        finally
        {
            main.Close(); ShellMessages.Reported -= Observe;
            ShellDialogs.FilePresenter = originalFile; ShellMessages.Presenter = originalMessage;
            MainWindow.SelectOpenPathOverride = originalOpen; MainWindow.SelectSaveProjectPathOverride = originalSave;
            MainWindow.ShellSettingsPathOverride = originalSettings; MainWindow.UnsavedProjectChoiceOverride = originalChoice;
            MainWindow.ComputeDialogOverride = originalCompute;
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }
}
