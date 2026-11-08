using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> TaskOrderCommands() =>
    [new("--test-task-order", 10462, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-task-order", (args, _) => TaskOrderRegression(args))];

    // Frozen pre-R4c task algorithm, deliberately independent of the current implementation.
    private static string LegacyTaskHash(FamilyClusterFile file)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
        }
        foreach (var p in file.MyExtendedFastaItems)
        {
            Append(p.SequenceIdentifier ?? ""); Append(p.Description ?? "");
            Append(p.Sequence ?? ""); Append(JsonSerializer.Serialize(p.Evidence));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void TaskOrderCoreChecks()
    {
        var file = TaskFile(); using var handler = new TaskTaxonomyHandler();
        var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
        var choice = EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
        file.MyExtendedFastaItems.Reverse();
        bool valid = true;
        try { EvidenceTaskDiscovery.Confirm(file, choice.Proposal, choice.ScopeTerms); }
        catch (InvalidOperationException) { valid = false; }
        Check("R4c task source is independent of protein row order", valid);
        using var repeat = new TaskTaxonomyHandler();
        Check("R4c discovery after reordering produces the same task fingerprint", proposal.SourceFingerprint ==
            EvidenceTaskDiscovery.DiscoverAsync(file, repeat).GetAwaiter().GetResult().SourceFingerprint);
        foreach (string change in new[] { "identifier", "sequence", "description", "evidence", "membership", "duplicate" })
        {
            var changed = FamilyClusterFile.FromJsonText(file.ToJsonText());
            switch (change)
            {
                case "identifier": changed.MyExtendedFastaItems[0].SequenceIdentifier += "changed"; break;
                case "sequence": changed.MyExtendedFastaItems[0].Sequence += "A"; break;
                case "description": changed.MyExtendedFastaItems[0].Description += " changed"; break;
                case "evidence": changed.MyExtendedFastaItems[0].Evidence!.Family += " changed"; break;
                case "membership": changed.MyExtendedFastaItems.RemoveAt(0); break;
                case "duplicate": changed.MyExtendedFastaItems.Add(changed.MyExtendedFastaItems[0]); break;
            }
            bool refused = false;
            try { EvidenceTaskDiscovery.Confirm(changed, choice.Proposal, choice.ScopeTerms); }
            catch (InvalidOperationException) { refused = true; }
            Check("R4c changed " + change + " still refuses the confirmed task", refused);
        }
        bool scopeRefused = false;
        try
        {
            typeof(EvidenceTaskDiscovery).GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [file, choice, new[] { "Different chosen scope" }]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { scopeRefused = true; }
        Check("R4c a different reviewed scope still refuses the task", scopeRefused);
        var legacy = TaskFile();
        var oldChoice = JsonSerializer.Deserialize<EvidenceTaskChoice>(JsonSerializer.Serialize(choice))!;
        oldChoice.Proposal.SourceFingerprint = LegacyTaskHash(legacy);
        legacy.EvidenceRecipe = new EvidenceRecipe { ConfirmedTask = oldChoice };
        string oldJson = legacy.ToJsonText();
        legacy = FamilyClusterFile.FromJsonText(oldJson);
        legacy.MyExtendedFastaItems.Reverse();
        bool legacyValid = true;
        try { EvidenceTaskDiscovery.Confirm(legacy, oldChoice.Proposal, oldChoice.ScopeTerms); }
        catch (InvalidOperationException) { legacyValid = false; }
        Check("R4c legacy recipe task remains valid after load and reorder", legacyValid);
        var stale = JsonSerializer.Deserialize<FamilyClusterFile>(oldJson)!;
        stale.MyExtendedFastaItems[0].Sequence += "A";
        stale = FamilyClusterFile.FromJsonText(stale.ToJsonText());
        bool staleRefused = false;
        try { EvidenceTaskDiscovery.Confirm(stale, oldChoice.Proposal, oldChoice.ScopeTerms); }
        catch (InvalidOperationException) { staleRefused = true; }
        Check("R4c load never binds a stale legacy task to changed proteins", staleRefused);
    }

    private static int TaskOrderRegression(string[] args)
    {
        if (args.Length is not (2 or 4)) throw new ArgumentException("Use --test-task-order <fresh-output> [<copied-legacy-project> <copied-plp>].");
        string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        EnsureApplication();
        TaskOrderCoreChecks();
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var priorFile = ShellDialogs.FilePresenter; var priorMessages = ShellMessages.Presenter;
        var priorSettings = MainWindow.ShellSettingsPathOverride;
        var priorUnsaved = MainWindow.UnsavedProjectChoiceOverride;
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        MainWindow.ShellSettingsPathOverride = Path.Combine(output, "preferences.json");
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        try
        {
            var file = TaskFile();
            using var taxonomy = new TaskTaxonomyHandler();
            var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, taxonomy).GetAwaiter().GetResult();
            var choice = EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
            // Produce a genuinely old-shaped task before writing the project.
            choice.Proposal.SourceFingerprint = LegacyTaskHash(file);
            using var fetch = new WorldFetchHandler();
            var fetched = CoreOperation.RunAsync("Legacy fixture", context => FamilyWorldFetcher.FetchAndStoreAsync(file,
                EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms), context, fetch)).GetAwaiter().GetResult();
            if (fetched.Status != OperationStatus.Succeeded) throw new InvalidOperationException("World fixture fetch failed.");
            var world = file.EvidenceSource!.FamilyWorld!;
            world.Task = choice; world.ContentSha256 = FamilyWorldFetcher.ContentHash(world);
            var summary = new FamilyWorldSummary { CompositionRules = FamilyWorldErrors.CompositionRule,
                MinimumTagLength = FamilyWorldOptions.DefaultMinimumTagLength, WorldFingerprint = world.ContentSha256,
                WorldRelease = world.Release, ScopeFamilies = world.Families.ToList() };
            string project = Path.Combine(output, "legacy.fqproj");
            file = FamilyClusterFile.FromJsonText(file.ToJsonText());
            ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest { FamilyWorldResult = summary });
            // Remove migration metadata to exercise a genuine pre-R4c container on the next read.
            EditStoredCuration(project, curation => curation.Remove("LegacyTaskSourceFingerprints"));
            Walk(project, WriteDenominatorFixture(output, "owned.plp", "NONE"), "fixture");
            if (args.Length == 4) Walk(args[2], args[3], "b6");
        }
        finally
        {
            ShellDialogs.FilePresenter = priorFile; ShellMessages.Presenter = priorMessages;
            MainWindow.ShellSettingsPathOverride = priorSettings; SynchronizationContext.SetSynchronizationContext(priorContext);
            MainWindow.UnsavedProjectChoiceOverride = priorUnsaved;
        }
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;

        void Walk(string project, string plp, string name)
        {
            string inputHash = TestFileHash.Upper(project), plpHash = TestFileHash.Upper(plp);
            var original = ProjectStore.ReadFqproj(project);
            string worldHash = FamilyWorldFetcher.Stored(original.fcf).ContentSha256;
            string table = JsonSerializer.Serialize(original.manifest.FamilyWorldResult);
            var main = OffscreenProofWindow(new MainWindow { Width = 1440, Height = 1000 });
            try
            {
                main.Show(); PumpDispatcher();
                Check("R4c " + name + " old project opens through the shell", main.OpenProjectPath(project));
                var screen = ((Grid)main.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
                WaitTaskOrderIdle(main.Operations);
                ShellDialogs.FilePresenter = (_, id, dialog) =>
                {
                    if (id != "curation.patternlab") throw new InvalidOperationException("Unexpected picker: " + id);
                    dialog.FileName = plp; return true;
                };
                void Click(string id)
                {
                    var button = UiField<Button>(screen, id) ?? throw new InvalidOperationException("Missing native button: " + id);
                    if (!button.IsEnabled) throw new InvalidOperationException("Disabled native button: " + id);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                    WaitTaskOrderIdle(screen.Operations);
                }
                Click("ButtonLoadQuantData");
                Check("R4c " + name + " actual PLP button loads quantitation", screen.ViewModel.Quantitation.HasPatternLab, screen.ShellStatusText);
                var grid = UiField<DataGrid>(screen, "DataGridResultGrid")!;
                var target = grid.Items.Cast<FastaItemExtended>().First(); int id = target.UserID;
                var column = UiField<DataGridTextColumn>(screen, "ColumnUserId")!;
                var editor = new TextBox { DataContext = target };
                BindingOperations.SetBinding(editor, TextBox.TextProperty, column.Binding);
                editor.SetCurrentValue(TextBox.TextProperty, "999999");
                var edit = new DataGridCellEditEndingEventArgs(column, new DataGridRow { Item = target }, editor, DataGridEditAction.Commit);
                typeof(DataGrid).GetMethod("OnCellEditEnding", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, [edit]);
                editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); PumpDispatcher();
                Check("R4c " + name + " native User ID edit takes effect", !edit.Cancel && target.UserID == 999999);
                Click("ButtonUndo");
                Check("R4c " + name + " native Undo restores User ID", target.UserID == id);
                grid.SelectedItems.Clear(); grid.SelectedItems.Add(target);
                UiField<TextBox>(screen, "TextBoxBatchAnnotation")!.Text = "R4c native annotation";
                UiField<TextBox>(screen, "TextBoxBatchUserId")!.Text = "";
                Click("ButtonApplyToSelected");
                Check("R4c " + name + " native batch annotates the selected row", target.UserAnnotation == "R4c native annotation");
                bool valid = true;
                try { FamilyWorldFetcher.Stored(screen.fcf); } catch (InvalidOperationException) { valid = false; }
                Check("R4c " + name + " unchanged proteins retain their confirmed task after exact walk", valid);
                main.NavigationModel.RequestNavigate(ShellDestination.Results);
                WaitTaskOrderIdle(main.Operations);
                var results = ((Grid)main.FindName("GridApp")).Children.OfType<AnalyticsControl>().SingleOrDefault();
                if (results != null) SelectStoredLength(results);
                Check("R4c " + name + " Results and Database gap remain reachable", results?.ViewModel.DatabaseGap.HasData == true);
                if (results != null) SaveWorldWalkImage(main, Path.Combine(output, name + "-results.png"));
                string saved = Path.Combine(output, name + "-resaved.fqproj");
                ProjectStore.WriteFqproj(saved, screen.fcf, original.quant, original.manifest);
                var reopened = ProjectStore.ReadFqproj(saved);
                bool reopenedValid = true;
                try { FamilyWorldFetcher.Stored(reopened.fcf); } catch (InvalidOperationException) { reopenedValid = false; }
                Check("R4c " + name + " reordered legacy task survives save and offline reopen", reopenedValid);
                Check("R4c " + name + " stored world hash and result numbers stay exact", reopened.fcf.EvidenceSource!.FamilyWorld!.ContentSha256 == worldHash
                    && JsonSerializer.Serialize(reopened.manifest.FamilyWorldResult) == table);
                // A real change must refuse only additions, while retaining the stored Results table.
                reopened.fcf.MyExtendedFastaItems[0].Sequence += "A";
                try
                {
                    var refused = new AnalyticsControl(original.quant, original.manifest, reopened.fcf);
                    var app = (Grid)main.FindName("GridApp"); app.Children.Clear(); app.Children.Add(refused);
                    SelectStoredLength(refused); PumpDispatcher();
                    var gapControl = (DatabaseGapControl)((ContentControl)refused.FindName("DatabaseGapHost")).Content;
                    var message = (TextBlock)gapControl.FindName("TextBlockFastaState");
                    string visible = message.Text;
                    Check("R4c " + name + " task refusal remains inside Database gap", refused.ViewModel.DatabaseGap.HasData
                        && !refused.ViewModel.DatabaseGap.CanExportFasta && visible.Contains("confirmed task"), visible);
                    message.BringIntoView(); PumpDispatcher();
                    SaveWorldWalkImage(main, Path.Combine(output, name + "-refusal.png"));
                }
                catch (InvalidOperationException) { Check("R4c " + name + " task refusal remains inside Database gap", false); }
                Check("R4c " + name + " copied inputs unchanged and native window remains offscreen", inputHash == TestFileHash.Upper(project)
                    && plpHash == TestFileHash.Upper(plp) && HasProofWindowPlacement(main) && !main.IsActive);
            }
            finally { main.Close(); PumpDispatcher(); }

            void SelectStoredLength(AnalyticsControl results)
            {
                PumpDispatcher();
                var gap = (DatabaseGapControl)((ContentControl)results.FindName("DatabaseGapHost")).Content;
                ((ComboBox)gap.FindName("ComboBoxMinimumTagLength")).SelectedItem = original.manifest.FamilyWorldResult!.MinimumTagLength;
                PumpDispatcher();
            }
        }
    }

    private static void WaitTaskOrderIdle(AppOperationHost host)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew(); int idle = 0;
        while (idle < 10 && watch.Elapsed < TimeSpan.FromMinutes(5))
        {
            PumpDispatcher(); idle = host.IsBusy ? 0 : idle + 1; Thread.Sleep(10);
        }
        if (idle < 10) throw new TimeoutException("R4c native operation did not settle.");
    }
}
