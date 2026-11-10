using FamilyQuant.Homology;
using FamilyQuant.DeNovo;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int DeNovoResultsExportProof(string[] args)
    {
        if (args.Length is not (5 or 6) || args.Length == 6 && args[5] != "--negative-control")
            throw new ArgumentException("Supply frozen source, frozen app, fresh output, date and optional --negative-control.");
        if (!NativeProofDesktop.IsPrivate)
            return IsolatedNativeProof(new[] { "--isolated-native-proof" }.Concat(args).ToArray());
        string root = FindLedgerRoot()!, frozen = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        foreach (var source in inventory.Files.Where(source => source.Path is "FamilyQuant/DeNovoControl.xaml" or "FamilyQuant/DeNovoControl.xaml.cs"))
            if (FeatureLedgerEvidence.Hash(Path.Combine(frozen, source.Path)) != source.Sha256)
                throw new InvalidDataException("Frozen source differs: " + source.Path);
        var oldAssembly = Assembly.LoadFrom(Path.Combine(Path.GetFullPath(args[2]), "FamilyQuant.dll"));
        var oldType = oldAssembly.GetType("FamilyQuant.DeNovoControl", true)!;
        EnsureApplication();
        var previousCulture = CultureInfo.CurrentCulture;
        var previousContext = SynchronizationContext.Current;
        var previousSettings = MainWindow.ShellSettingsPathOverride;
        var previousPresenter = ShellDialogs.FilePresenter;
        var oldConsole = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var observations = new Dictionary<string, (string Fact, string Path, string Symbol, string Note)>(StringComparer.Ordinal);
        var scenarios = new List<object>();
        var apiRefusals = new List<object>();
        var interactions = new List<object>();
        var gaps = new List<object>();
        try
        {
            Console.SetOut(new LedgerProofWriter(oldConsole, transcript));
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Check("Results export uses the exact frozen application commit", oldAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion.EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal));
            foreach (string culture in new[] { "en-US", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                MainWindow.ShellSettingsPathOverride = Path.Combine(output, culture + "-shell.json");
                var legacy = (UserControl)Activator.CreateInstance(oldType)!;
                var oldWindow = LedgerDeNovoWindow(legacy);
                var main = new MainWindow { Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    oldWindow.Show(); main.Show(); PumpDispatcher();
                    MainWindow.NavigateShellCommand.Execute(ShellDestination.DeNovo, main); PumpDispatcher();
                    var host = UiField<Grid>(main, "GridApp")!;
                    var current = host.Children.OfType<DeNovoControl>().Single();
                    var oldButton = (Button)legacy.FindName("ButtonExportHomologyCsv");
                    Check("No-snapshot assignments export remains unavailable " + culture, !oldButton.IsEnabled
                        && !main.NavigationModel.CanNavigate(ShellDestination.Results) && !UiField<MenuItem>(main, "MenuItemAnalytics")!.IsEnabled);
                    var emptyResults = new AnalyticsControl(Array.Empty<QuantRow>(), null);
                    var emptyItem = ResultsExportOptions(emptyResults.ViewModel).Single(option => option.Kind == ProjectExportKind.Homology);
                    Check("Results assignment choice cannot export an absent snapshot " + culture,
                        !emptyItem.Available && emptyItem.Hint.Contains("Search", StringComparison.Ordinal));
                    string oldRefusal = Refusal(() => oldType.GetMethod("ExportHomologyCsv")!.Invoke(legacy, [Path.Combine(output, "must-not-exist.csv")]));
                    string currentRefusal = Refusal(() => current.ExportHomologyCsv(Path.Combine(output, "must-not-exist.csv")));
                    Check("Absent-snapshot compatibility export refuses without creating a file " + culture,
                        oldRefusal.StartsWith("There is no homology search to export;", StringComparison.Ordinal)
                        && currentRefusal.StartsWith("There is no homology search to export;", StringComparison.Ordinal)
                        && !File.Exists(Path.Combine(output, "must-not-exist.csv")));
                    apiRefusals.Add(new { culture, frozen = oldRefusal, current = currentRefusal });

                    foreach (string fixture in new[] { "assigned", "empty" })
                    {
                        var snapshot = ResultsExportFixture(fixture == "empty");
                        var oldMetadata = JsonSerializer.SerializeToNode(snapshot, DeNovoBridgeJson)!;
                        oldMetadata["Assignments"] = new JsonArray();
                        object oldSnapshot = JsonSerializer.Deserialize(oldMetadata.ToJsonString(),
                            oldType.GetMethod("ApplyHomology")!.GetParameters()[0].ParameterType, DeNovoBridgeJson)!;
                        // FamilyAssignment has a nonpublic constructor, so populate its actual native list explicitly.
                        var oldList = (IList)oldSnapshot.GetType().GetProperty("Assignments")!.GetValue(oldSnapshot)!;
                        oldList.Clear();
                        foreach (var assignment in snapshot.Assignments)
                        {
                            var type = oldList.GetType().GetGenericArguments().Single();
                            oldList.Add(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                .Single(ctor => ctor.GetParameters().Length == 4).Invoke([assignment.Peptide, assignment.FamilyIds, assignment.ProteinIds, assignment.UnknownProteinIds]));
                        }
                        oldType.GetMethod("ApplyHomology")!.Invoke(legacy, [oldSnapshot]);
                        current.ApplyHomology(snapshot); PumpDispatcher();
                        Check("The export fixture has equal native frozen/current snapshot data " + culture + "/" + fixture,
                            LedgerLegacyJsonAgrees(JsonSerializer.SerializeToElement(oldSnapshot, oldSnapshot.GetType(), DeNovoBridgeJson),
                                JsonSerializer.SerializeToElement(snapshot, DeNovoBridgeJson)));
                        Check("Completed snapshot enables the actual shell route " + culture + "/" + fixture,
                            oldButton.IsEnabled && !main.NavigationModel.HasProject && main.NavigationModel.CanNavigate(ShellDestination.Results)
                            && UiField<MenuItem>(main, "MenuItemAnalytics")!.IsEnabled);
                        MainWindow.NavigateShellCommand.Execute(ShellDestination.Results, main);
                        Wait(() => host.Children.OfType<AnalyticsControl>().Any() && !main.Operations.IsBusy);
                        var results = host.Children.OfType<AnalyticsControl>().Single();
                        var selected = ExportOptions(main).Single(option => option.Kind == ProjectExportKind.Homology);
                        var export = (Button)results.FindName("ButtonExport");
                        Check("Shell passes its exact snapshot into the assignment export " + culture + "/" + fixture,
                            ReferenceEquals(results.ViewModel.Homology, snapshot) && results.ViewModel.Data == null && export.IsEnabled
                            && selected.Available && selected.Label == "Homology assignments"
                            && main.NavigationModel.Current == ShellDestination.Results);
                        string before = JsonSerializer.Serialize(snapshot, DeNovoBridgeJson);
                        string oldPath = Path.Combine(output, culture + "-" + fixture + "-frozen.csv");
                        string newPath = Path.Combine(output, culture + "-" + fixture + "-current.csv");
                        bool NativeWrite(Button button, string path, Action? wait = null)
                        {
                            using var driver = new NativeOwnedDialogDriver(path);
                            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            wait?.Invoke(); PumpDispatcher();
                            bool accepted = driver.SawDialog && driver.EnteredPath && !driver.TimedOut && File.Exists(path);
                            Check("Native assignments Save writes the selected file " + Path.GetFileName(path), accepted);
                            return accepted;
                        }
                        ShellDialogs.FilePresenter = null;
                        bool oldWritten = NativeWrite(oldButton, oldPath);
                        string? suggested = null, dialogId = null;
                        ShellDialogs.FilePresenter = (owner, id, dialog) =>
                        {
                            suggested = dialog.FileName; dialogId = id;
                            return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                        };
                        bool newWritten;
                        using (var driver = new NativeOwnedDialogDriver(newPath))
                        {
                            ChooseExport(main, ProjectExportKind.Homology); Wait(() => !main.Operations.IsBusy); PumpDispatcher();
                            newWritten = driver.SawDialog && driver.EnteredPath && !driver.TimedOut && File.Exists(newPath);
                            Check("Native assignments Save writes the selected file " + Path.GetFileName(newPath), newWritten);
                        }
                        Check("Results uses the assignments dialog and retained suggested filename " + culture + "/" + fixture,
                            dialogId == "results.Homology" && suggested == "FamilyQuant_DeNovo_Homology.csv");
                        byte[] oldBytes = File.ReadAllBytes(oldPath), newBytes = File.ReadAllBytes(newPath);
                        if (args.Length == 6) newBytes = newBytes.Concat(new byte[] { 33 }).ToArray();
                        bool same = oldWritten && newWritten && oldBytes.SequenceEqual(newBytes);
                        Check("Actual frozen button and Results chosen CSV bytes agree " + culture + "/" + fixture, same);
                        string expected = fixture == "empty" ? "peptide,family_ids,protein_ids,ambiguous" + Environment.NewLine :
                            "peptide,family_ids,protein_ids,ambiguous" + Environment.NewLine +
                            "\"PEPTIDEK\",\"11\",\"P,one\",false" + Environment.NewLine +
                            "\"AAAAAAAA\",\"11;22\",\"P\"\"two;P,three\",true" + Environment.NewLine +
                            "\"GGGGGGGG\",\"\",\"\",false" + Environment.NewLine;
                        Check("Chosen assignment CSV retains every header, delimiter, boolean and escaping fact " + culture + "/" + fixture,
                            File.ReadAllText(newPath) == expected && oldBytes.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(expected)));
                        Check("Assignments export leaves its held data unchanged " + culture + "/" + fixture,
                            before == JsonSerializer.Serialize(snapshot, DeNovoBridgeJson) && ReferenceEquals(results.ViewModel.Homology, snapshot)
                            && results.ViewModel.Status.StartsWith("Written to " + newPath + ":", StringComparison.Ordinal)
                            && ((TextBlock)results.FindName("TextBlockStatus")).Text == results.ViewModel.Status);
                        string status = results.ViewModel.Status;
                        using (var cancel = new NativeOwnedDialogDriver(null))
                        {
                            export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                            Check("Actual Results Save cancellation keeps the existing file and snapshot " + culture + "/" + fixture,
                                cancel.SawDialog && !cancel.TimedOut && File.ReadAllBytes(newPath).SequenceEqual(oldBytes)
                                && before == JsonSerializer.Serialize(snapshot, DeNovoBridgeJson) && results.ViewModel.Status == status);
                        }
                        using (var cancel = new NativeOwnedDialogDriver(null))
                        {
                            oldButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                            Check("Actual frozen Save cancellation keeps the existing file " + culture + "/" + fixture,
                                cancel.SawDialog && !cancel.TimedOut && File.ReadAllBytes(oldPath).SequenceEqual(oldBytes));
                        }
                        scenarios.Add(new { culture, fixture, legacyCsv = Path.GetFileName(oldPath), currentCsv = Path.GetFileName(newPath),
                            same, suggested, dialogId, snapshot = JsonSerializer.SerializeToElement(snapshot, DeNovoBridgeJson),
                            selected = selected.Label, status = results.ViewModel.Status.Replace(newPath, "{chosen-file}", StringComparison.Ordinal) });
                        MainWindow.NavigateShellCommand.Execute(ShellDestination.DeNovo, main); PumpDispatcher();
                        current = host.Children.OfType<DeNovoControl>().Single();
                        CheckSourceAndHelp(legacy, oldSnapshot, culture, fixture, "frozen");
                        CheckSourceAndHelp(current, snapshot, culture, fixture, "current");
                    }
                }
                finally { oldWindow.Close(); main.Close(); }
            }

            void Observe(string id, string fact, string path, string symbol, string note = "") => observations.Add(id, (fact, path, symbol, note));
            foreach (string id in new[] { "FQ15-19F09FA08309", "FQ15-5CC29C895FD3", "FQ15-2EDEA71A577B" })
                Observe(id, "Frozen assignment button success is reachable through shell Results, selected Homology assignments (CSV), and actual native Save; chosen CSV bytes agree in both cultures.",
                    "FamilyQuant/AnalyticsControl.xaml.cs", "FamilyQuant.AnalyticsControl.ExportClicked");
            Observe("FQ15-364119867EF1", "Before a completed snapshot the frozen button and current Results route/export are unavailable; completed and empty snapshots enable export.",
                "FamilyQuant/MainWindow.HomologyResults.cs", "FamilyQuant.MainWindow.KeepHomologyResult");
            Observe("FQ15-F719B5590714", "Actual selection events choose every legacy source ID and clear the prior homology snapshot in both cultures/versions.",
                "FamilyQuant/DeNovoControl.xaml.cs", "FamilyQuant.DeNovoControl.ComboBoxDeNovoSource_SelectionChanged");
            foreach (var item in new[] { ("FQ15-A59CA19463AD", DeNovoSource.DenovoOnly.Id), ("FQ15-C027EA198A78", DeNovoSource.DeNovoAll.Id), ("FQ15-FC55DD924EC1", DeNovoSource.DeepNovo.Id) })
                Observe(item.Item1, "The actual legacy source choice " + item.Item2 + " is visible, selectable and updates the active engine ID in both versions and cultures.",
                    "FamilyQuant/DeNovoControl.xaml", "FamilyQuant.DeNovoControl.ComboBoxDeNovoSource");
            foreach (string id in new[] { "FQ15-97BE9095ACA9", "FQ15-D6211E2A6653" })
                Observe(id, "The actual Help button opens exactly one owned visible window with a nonempty read-only report, and closes it without a clipboard operation.",
                    "FamilyQuant/DeNovoControl.Dialogs.cs", "FamilyQuant.DeNovoControl.ButtonDeNovoHelp_Click");
            foreach (var row in inventory.Observations.Where(row => row.Source == "FamilyQuant/DeNovoControl.xaml.cs" && row.Line is >= 1752 and <= 1769 && row.Kind == "code fact"))
                Observe(row.Id, "Actual chosen CSVs have exact four-column bytes: one row per assignment, semicolon IDs, true/false ambiguity, quoted fields and doubled embedded quotes. Empty snapshot has header only.",
                    "FamilyQuant.Core/HomologyCsv.cs", "FamilyQuant.HomologyCsv.Write");
            Observe("FQ15-0061551CF9D6", "A completed standalone snapshot reaches Results with no project/full XIC and its actual assignment export writes matching CSV bytes.",
                "FamilyQuant/MainWindow.HomologyResults.cs", "FamilyQuant.MainWindow.KeepHomologyResult",
                "Paulo ruling relayed in M57, 2026-11-02: the clause 'go on to Layer 5' retired with the partition screen on November 1; assignment export stays. This verifies that retained fact, not a whole-row DROP.");
            // These wording observations describe CSV fields in the old UI. Written bytes prove the
            // output contract, but do not prove that the current user can read those facts in the UI.
            foreach (string id in new[] { "FQ15-C52BCFB9A2DC", "FQ15-3261F961544B", "FQ15-BF6844C292B3", "FQ15-A872F13CDDF7" })
                gaps.Add(new { id, state = "OPEN", reason = "Current assignments choice and generic export status do not describe all four fields/one row per searched peptide. CSV bytes are verified separately; wording fact needs owner restoration or explicit ruling." });
            gaps.Add(new { id = "FQ15-065DCB10B3E7", state = "OPEN", reason = "The API guard refuses correctly, but its current guidance still says Step 4. The current screen uses Standalone homology (advanced). Refusal texts are retained for owner review; no exact-text or complete wording claim." });
            foreach (var observation in observations)
            {
                if (!inventory.Observations.Any(row => row.Id == observation.Key && !FeatureLedger.IsApprovedDrop(row)))
                    throw new InvalidDataException("Unknown or excluded observation: " + observation.Key);
            }
            if (failed == 0) Check("Whole De novo and Results screen interactions preserve source selection, Help, chosen CSV bytes, snapshot state and cancellation in both cultures", true);
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "results-export-data.json"), new { privateDesktop = NativeProofDesktop.IsPrivate, scenarios, interactions, apiRefusals, gaps });
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally
        {
            Console.SetOut(oldConsole); ShellDialogs.FilePresenter = previousPresenter;
            MainWindow.ShellSettingsPathOverride = previousSettings; CultureInfo.CurrentCulture = previousCulture;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        string log = Path.Combine(output, "execution.log"); File.WriteAllText(log, transcript.ToString());
        if (failed != 0) return 1;
        var sources = new[] { "FamilyQuant/AnalyticsControl.xaml", "FamilyQuant/AnalyticsControl.xaml.cs", "FamilyQuant/AnalyticsViewModel.cs",
            "FamilyQuant/MainWindow.HomologyResults.cs", "FamilyQuant/MainWindow.Presentation.cs", "FamilyQuant/MainWindow.Navigation.cs",
            "FamilyQuant/DeNovoControl.xaml.cs", "FamilyQuant/DeNovoSession.cs", "FamilyQuant.Core/HomologyCsv.cs", "FamilyQuant.Core/CsvFormatting.cs",
            "FamilyQuant/ShellDialogs.cs", "FamilyQuant/AppOperationFiles.cs", "FamilyQuant/AppOperationHost.cs", "FamilyQuant/TextReportWindow.cs",
            "FamilyQuant/SecondaryWindowView.cs", "FamilyQuant/SecondaryWindows.ViewModels.cs",
            "FamilyQuant.Core/HelpContent.cs", "FamilyQuant.Core/DeNovo/DeNovoSource.cs" }
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "DeNovo*.cs").Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal).ToArray();
        var artifacts = new List<string> { log, Path.Combine(output, "results-export-data.json") };
        artifacts.AddRange(Directory.GetFiles(output, "*.csv"));
        string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
        foreach (string source in sources.Concat(new[] { "FamilyQuant.Tests/Program.DeNovoResultsExport.cs", "FamilyQuant.Tests/NativeOwnedDialogDriver.cs",
            "FamilyQuant.Tests/Program.DeNovoLedgerWorkflow.cs", "FamilyQuant.Tests/Program.Commands.DeNovoLedger.cs", "FamilyQuant.Tests/NativeProofDesktop.cs" }))
        {
            string copy = Path.Combine(archive, source.Replace('/', '_')); File.Copy(Path.Combine(root, source), copy); artifacts.Add(copy);
        }
        foreach (string binary in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(ProjectStore).Assembly.Location, oldAssembly.Location })
        {
            string copy = Path.Combine(archive, (binary == oldAssembly.Location ? "frozen_" : "") + Path.GetFileName(binary));
            File.Copy(binary, copy); artifacts.Add(copy);
        }
        foreach (string source in new[] { "FamilyQuant/DeNovoControl.xaml", "FamilyQuant/DeNovoControl.xaml.cs" })
        {
            string copy = Path.Combine(archive, "frozen_" + source.Replace('/', '_')); File.Copy(Path.Combine(frozen, source), copy); artifacts.Add(copy);
        }
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD"); using var git = Process.Start(start)!;
        string commit = git.StandardOutput.ReadToEnd().Trim(); git.WaitForExit(); if (git.ExitCode != 0) throw new IOException("Cannot record HEAD.");
        const string step = "EV-denovo-results-export-1", runId = "EV-results-assignment-export";
        var run = new FeatureLedgerEvidence.Run(runId, Environment.CommandLine, args[4], commit,
            artifacts.Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList(), root);
        var rows = observations.Select(item => new FeatureLedgerEvidence.Row(item.Key,
            sources.Select(path => new FeatureLedgerEvidence.Implementation(path, path == item.Value.Path ? item.Value.Symbol : "Native Results route dependency", FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList(),
            new() { new(runId, "test", nameof(DeNovoResultsExportProof), "Whole De novo and Results screen interactions preserve source selection, Help, chosen CSV bytes, snapshot state and cancellation in both cultures", log, "") },
            "Synthetic held-snapshot and actual native Save fixture. No RAW/search accuracy claim. " + item.Value.Note, null,
            inventory.Observations.Single(row => row.Id == item.Key).Detail + " -> " + item.Value.Fact)).ToList();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"), new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { run }, rows));
        return 0;

        void Wait(Func<bool> completed)
        {
            var watch = Stopwatch.StartNew();
            while (!completed()) { PumpDispatcher(); Thread.Sleep(10); if (watch.Elapsed > TimeSpan.FromMinutes(1)) throw new TimeoutException("Owned Results operation did not finish."); }
            PumpDispatcher();
        }
        string Refusal(Action action)
        {
            try { action(); return ""; }
            catch (Exception exception) when (exception.GetBaseException() is InvalidOperationException)
            { return exception.GetBaseException().Message; }
        }
        void CheckSourceAndHelp(UserControl view, object snapshot, string culture, string fixture, string side)
        {
            var type = view.GetType(); var picker = (ComboBox)view.FindName("ComboBoxDeNovoSource");
            string ActiveId() => type.GetProperty("ActiveDeNovoSource")!.GetValue(view)!.GetType().GetProperty("Id")!
                .GetValue(type.GetProperty("ActiveDeNovoSource")!.GetValue(view))!.ToString()!;
            string first = ActiveId(); var selectedIds = new List<string>();
            foreach (var source in DeNovoSource.Presets.Where(source => source.Id != first).Concat(DeNovoSource.Presets.Where(source => source.Id == first)))
            {
                var import = LedgerDeNovoImport(DeNovoSource.FromId(ActiveId())!);
                var applyImport = type.GetMethod("ApplyImport")!;
                applyImport.Invoke(view, [LedgerLegacyValue(import, applyImport.GetParameters()[0].ParameterType)]);
                type.GetMethod("ApplyHomology")!.Invoke(view, [snapshot]);
                Check("Source event starts with a held snapshot " + culture + "/" + fixture + "/" + side + "/" + source.Id,
                    type.GetProperty("LastHomology")!.GetValue(view) != null);
                picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == source.Id); PumpDispatcher();
                Check("Native source choice updates engine and clears its old snapshot " + culture + "/" + fixture + "/" + side + "/" + source.Id,
                    picker.IsVisible && picker.IsEnabled && ActiveId() == source.Id && type.GetProperty("LastHomology")!.GetValue(view) == null,
                    "visible=" + picker.IsVisible + "; enabled=" + picker.IsEnabled + "; active=" + ActiveId()
                    + "; cleared=" + (type.GetProperty("LastHomology")!.GetValue(view) == null));
                selectedIds.Add(ActiveId());
            }
            var before = Application.Current.Windows.Cast<Window>().ToArray();
            var help = (Button)view.FindName("ButtonDeNovoHelp");
            Check("Native Help button is available " + culture + "/" + fixture + "/" + side, help.IsVisible && help.IsEnabled);
            help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            var opened = Application.Current.Windows.Cast<Window>().Except(before).ToArray();
            Check("Native Help opens one owned offscreen read-only report " + culture + "/" + fixture + "/" + side,
                opened.Length == 1 && opened[0].Owner == Window.GetWindow(view) && opened[0].IsVisible
                && !opened[0].ShowActivated && !opened[0].ShowInTaskbar && opened[0].Left == -30000 && opened[0].Top == -30000
                && WalkTree(opened[0]).OfType<TextBox>().Any(text => text.IsReadOnly && text.Text.Length > 0));
            string? title = opened.SingleOrDefault()?.Title;
            foreach (var window in opened) window.Close(); PumpDispatcher();
            Check("Native Help closes without leaving its window " + culture + "/" + fixture + "/" + side,
                Application.Current.Windows.Cast<Window>().All(before.Contains));
            interactions.Add(new { culture, fixture, side, selectedIds, helpTitle = title });
        }
    }

    private static DeNovoHomology ResultsExportFixture(bool empty)
    {
        var result = new DeNovoHomology { FcfPath = "native-export-fixture.fcf", PeptidesSearched = empty ? 0 : 3,
            MappedToTargets = empty ? 0 : 2, Unmatched = empty ? 0 : 1 };
        if (empty) return result;
        var constructor = typeof(FamilyAssignment).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(ctor => ctor.GetParameters().Length == 4);
        result.Assignments.Add((FamilyAssignment)constructor.Invoke(["PEPTIDEK", new[] { 11 }, new[] { "P,one" }, Array.Empty<string>()]));
        result.Assignments.Add((FamilyAssignment)constructor.Invoke(["AAAAAAAA", new[] { 11, 22 }, new[] { "P\"two", "P,three" }, Array.Empty<string>()]));
        result.Assignments.Add((FamilyAssignment)constructor.Invoke(["GGGGGGGG", Array.Empty<int>(), Array.Empty<string>(), Array.Empty<string>()]));
        result.Families = [new() { FamilyId = 11, PeptideCount = 2, Description = "Fixture one" }, new() { FamilyId = 22, PeptideCount = 1, Description = "Fixture two" }];
        return result;
    }
}
