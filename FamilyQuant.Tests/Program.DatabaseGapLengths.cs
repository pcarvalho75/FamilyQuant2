using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void DatabaseGapLengthTests(string output)
    {
        Section("Database gap lengths - stored screen choice, exports and project roundtrip");
        EnsureApplication(); Directory.CreateDirectory(output);
        var file = TaskFile();
        using var taxonomy = new TaskTaxonomyHandler();
        var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, taxonomy).GetAwaiter().GetResult();
        var task = EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
        using var fetch = new WorldFetchHandler();
        var fetched = CoreOperation.RunAsync("Synthetic stored world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, task, context, fetch)).GetAwaiter().GetResult();
        if (fetched.Status != OperationStatus.Succeeded) throw new InvalidOperationException("Synthetic world unavailable.");
        var stored = FamilyWorldFetcher.Stored(file); var fingerprint = FamilyWorld.FromSnapshot(stored).Fingerprint;
        var tables = FamilyWorldOptions.LengthChoices.Select(length =>
        {
            var table = new FamilyWorldSummary { MinimumTagLength = length, MinIdentity = .8, MinimumKnownCredits = 10,
                CompositionRules = "Synthetic stored first-pass rule", Rules = "Synthetic stored rule at minimum tag length " + length,
                ScopeFamilies = stored.Families.ToList(), WorldRelease = stored.Release, WorldFingerprint = fingerprint,
                MeasuredIdentifiedEnvelope = 1000, MeasuredDeNovoEnvelope = 200, UnexplainedEnvelope = length };
            table.Composition.Add(new() { Family = stored.Families[0], IdentifiedEnvelope = 100, DatabaseDeNovoEnvelope = 10, WorldDeNovoEnvelope = length });
            table.Families.Add(new() { Family = stored.Families[0], WorldProteins = stored.Entries.Take(length == FamilyWorldOptions.LengthChoices[0] ? 2 : 1)
                .Select(entry => entry.Accession).ToList() });
            return table;
        }).ToList();
        var root = JsonSerializer.Deserialize<FamilyWorldSummary>(JsonSerializer.Serialize(tables.Single(table => table.MinimumTagLength == FamilyWorldOptions.DefaultMinimumTagLength)))!;
        root.Lengths = tables;
        root.LengthEstimates = FamilyWorldOptions.LengthChoices.Select(length => new FamilyWorldLengthEstimate
        { MinimumTagLength = length, LandedFraction = .125, DecoyRate = length == FamilyWorldOptions.LengthChoices[^1] ? null : .005,
            KnownRight = 12, KnownLanded = 13, RightRate = .923, FamiliesMeasured = 1 }).ToList();
        string originalTables = JsonSerializer.Serialize(root), originalWorld = file.ToJsonText();
        var manifest = new QuantManifest { FamilyWorldResult = root };
        var model = new AnalyticsViewModel(Array.Empty<QuantRow>(), manifest, file);
        int edits = 0; model.ResultsOptionsChanged += (_, _) => edits++;
        var control = new AnalyticsControl(model);
        var window = new Window { Content = control, Width = 1180, Height = 1000, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var previousContext = SynchronizationContext.Current;
        var previousPresenter = ShellDialogs.FilePresenter;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        try
        {
            window.Show(); PumpDispatcher(); window.UpdateLayout();
            var gap = (DatabaseGapControl)((ContentControl)control.FindName("DatabaseGapHost")).Content;
            var chooser = (ComboBox)gap.FindName("ComboBoxMinimumTagLength");
            var exportChoice = (ComboBox)control.FindName("ComboBoxExportKind"); var exportButton = (Button)control.FindName("ButtonExport");
            Check("length screen uses the Core default and every Core choice without recording an initial edit",
                model.DatabaseGap.MinimumTagLength == FamilyWorldOptions.DefaultMinimumTagLength && manifest.DatabaseGapMinimumTagLength == null
                && chooser.Items.Cast<int>().SequenceEqual(FamilyWorldOptions.LengthChoices) && edits == 0);
            Check("length grid displays exact stored estimates and explicit missing rates", ((DataGrid)gap.FindName("DataGridLengths")).Items.Count == FamilyWorldOptions.LengthChoices.Count
                && model.DatabaseGap.LengthRows.All(row => ReferenceEquals(row.Data, root.LengthEstimates.Single(value => value.MinimumTagLength == row.MinimumTagLength)))
                && model.DatabaseGap.LengthRows.Last().DecoyRate == "Not recorded");
            foreach (int length in new[] { FamilyWorldOptions.LengthChoices[0], FamilyWorldOptions.LengthChoices[^1], FamilyWorldOptions.DefaultMinimumTagLength })
            {
                chooser.SelectedItem = length; PumpDispatcher();
                var active = root.ForLength(length)!;
                Check("native choice switches result, rules, chart and export eligibility together at length " + length,
                    ReferenceEquals(model.DatabaseGap.Summary, active) && model.DatabaseGap.Rows[0].Data == active.Composition[0]
                    && ((DatabaseGapChart)((ContentControl)gap.FindName("ChartHost")).Content).Rows.SequenceEqual(model.DatabaseGap.ChartRows)
                    && model.DatabaseGap.Rules.Contains(length + " residues", StringComparison.Ordinal));
                foreach (var kind in new[] { ResultsExportKind.DatabaseGap, ResultsExportKind.SuggestedAdditions })
                {
                    exportChoice.SelectedItem = exportChoice.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == kind.ToString()); PumpDispatcher();
                    var paths = new List<string>();
                    for (int repeat = 1; repeat <= 2; repeat++)
                    {
                        string path = Path.Combine(output, length + "-" + kind + "-" + repeat); paths.Add(path);
                        ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = path; return true; };
                        exportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FinishBusyOperation(AppOperationHost.For(control));
                    }
                    string actual = File.ReadAllText(paths[0]);
                    bool agrees = kind == ResultsExportKind.DatabaseGap ? actual == active.BuildCompositionCsv()
                        : actual.Split('\n').Where(line => line.StartsWith('>')).Select(line => line[1..].Split(' ')[0]).ToHashSet(StringComparer.Ordinal)
                            .SetEquals(active.Families.SelectMany(family => family.WorldProteins));
                    Check("native " + kind + " follows selected length " + length + " and repeats bytes", agrees
                        && File.ReadAllBytes(paths[0]).SequenceEqual(File.ReadAllBytes(paths[1])));
                }
            }
            chooser.SelectedItem = FamilyWorldOptions.LengthChoices[0]; PumpDispatcher();
            string project = Path.Combine(output, "length-choice.fqproj");
            ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), manifest);
            var reopened = ProjectStore.ReadFqproj(project);
            var roundtrip = new AnalyticsViewModel(Array.Empty<QuantRow>(), reopened.manifest, reopened.fcf);
            Check("project roundtrip retains the chosen length and its CSV/FASTA without recomputation", roundtrip.DatabaseGap.MinimumTagLength == FamilyWorldOptions.LengthChoices[0]
                && roundtrip.DatabaseGap.BuildCsv() == model.DatabaseGap.BuildCsv() && roundtrip.DatabaseGap.BuildSuggestedFasta() == model.DatabaseGap.BuildSuggestedFasta() && edits > 0);
            var legacy = JsonSerializer.Deserialize<FamilyWorldSummary>(JsonSerializer.Serialize(tables[0]))!;
            var unavailable = new DatabaseGapViewModel(legacy, file, FamilyWorldOptions.LengthChoices[^1]);
            Check("uncomputed length is explicit and cannot retain a previous table or FASTA", !unavailable.HasData && !unavailable.CanExportFasta
                && unavailable.Rows.Count == 0 && unavailable.EmptyMessage.Contains("No stored table", StringComparison.Ordinal));
            unavailable.MinimumTagLength = legacy.MinimumTagLength;
            Check("a legacy project can choose its computed table again", unavailable.HasData && unavailable.BuildCsv() == legacy.BuildCompositionCsv());
            Check("length selection never modifies the stored computed tables or world bytes", originalTables == JsonSerializer.Serialize(root) && originalWorld == file.ToJsonText());
            DatabaseGapLengthShellRoundtrip(project, output);
        }
        finally { window.Close(); PumpDispatcher(); ShellDialogs.FilePresenter = previousPresenter; SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void DatabaseGapLengthShellRoundtrip(string project, string output)
    {
        string? oldSettings = MainWindow.ShellSettingsPathOverride; var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldSave = MainWindow.SelectSaveProjectPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(output, "shell-settings.json");
        MainWindow.SelectSaveProjectPathOverride = () => project;
        var main = new MainWindow { Width = 1180, Height = 1000, Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            main.Show(); FinishBusyTask(main.OpenProjectSafelyAsync(project));
            main.NavigationModel.RequestNavigate(ShellDestination.Results); FinishBusyOperation(main.Operations); PumpDispatcher();
            var results = ((Grid)main.FindName("GridApp")).Children.OfType<AnalyticsControl>().Single();
            results.ViewModel.DatabaseGap.MinimumTagLength = FamilyWorldOptions.LengthChoices[^1];
            Check("real shell marks a length choice dirty", main.DocumentState.IsModified);
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Cancel;
            var replacement = main.OpenProjectSafelyAsync(project); FinishBusyTask(replacement);
            Check("Cancel protects an unsaved length choice", !replacement.Result && main.DocumentState.IsModified
                && results.ViewModel.DatabaseGap.MinimumTagLength == FamilyWorldOptions.LengthChoices[^1]);
            FinishBusyTask((System.Threading.Tasks.Task)typeof(MainWindow).GetMethod("SaveProjectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(main, new object[] { false })!);
            Check("real shell Save persists the selected length and clears dirty state", !main.DocumentState.IsModified
                && ProjectStore.ReadFqproj(project).manifest.DatabaseGapMinimumTagLength == FamilyWorldOptions.LengthChoices[^1]);
        }
        finally { MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard; main.Close(); PumpDispatcher();
            MainWindow.UnsavedProjectChoiceOverride = oldChoice; MainWindow.ShellSettingsPathOverride = oldSettings; MainWindow.SelectSaveProjectPathOverride = oldSave; }
    }

    private static int DatabaseGapLengthScreens(string projectPath, string output, bool requireDefault = false)
    {
        EnsureApplication();
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output); string inputHash = FeatureLedgerEvidence.Hash(projectPath);
        var project = ProjectStore.ReadFqproj(projectPath);
        var root = project.manifest.FamilyWorldResult ?? throw new InvalidDataException("Project has no family-world result.");
        string storedTables = JsonSerializer.Serialize(root);
        var model = new AnalyticsViewModel(Array.Empty<QuantRow>(), project.manifest, project.fcf);
        var control = new AnalyticsControl(model);
        var window = new Window { Content = control, Width = 1180, Height = 1040, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var oldContext = SynchronizationContext.Current; var oldPresenter = ShellDialogs.FilePresenter; var oldTheme = Themes.ThemeManager.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        var transcript = new List<string>(); bool exportsAgree = true;
        try
        {
            window.Show(); PumpDispatcher(); window.UpdateLayout();
            var gap = (DatabaseGapControl)((ContentControl)control.FindName("DatabaseGapHost")).Content;
            var lengthChoice = (ComboBox)gap.FindName("ComboBoxMinimumTagLength");
            var exportChoice = (ComboBox)control.FindName("ComboBoxExportKind"); var button = (Button)control.FindName("ButtonExport");
            if (requireDefault)
            {
                Check("default proof uses Core length 8 and a freshly stored top-level table at 8",
                    FamilyWorldOptions.DefaultMinimumTagLength == 8 && root.MinimumTagLength == FamilyWorldOptions.DefaultMinimumTagLength);
                Check("real Results opens at the Core default before any length choice, without a stored override",
                    project.manifest.DatabaseGapMinimumTagLength == null && model.DatabaseGap.MinimumTagLength == FamilyWorldOptions.DefaultMinimumTagLength
                    && Equals(lengthChoice.SelectedItem, FamilyWorldOptions.DefaultMinimumTagLength)
                    && ReferenceEquals(model.DatabaseGap.Summary, root.ForLength(FamilyWorldOptions.DefaultMinimumTagLength)));
            }
            foreach (var row in model.DatabaseGap.LengthRows)
                transcript.Add(row.MinimumTagLength + " | " + row.LandedShare + " | " + row.DecoyRate + " | " + row.KnownAnswerRight + " | " + row.Availability);
            // Capture the initial default before export status introduces output paths.
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                foreach (int length in (requireDefault ? new[] { FamilyWorldOptions.DefaultMinimumTagLength }
                    : new[] { FamilyWorldOptions.DefaultMinimumTagLength, FamilyWorldOptions.LengthChoices[0] }).Distinct())
                {
                    if (!requireDefault) lengthChoice.SelectedItem = length;
                    PumpDispatcher(); window.UpdateLayout();
                    ScreenHarness.Save(window, Path.Combine(output, "lengths-" + theme + "-" + length + ".png"), 1180, 1040);
                    if (requireDefault)
                    {
                        var scroll = (ScrollViewer)control.FindName("ScrollAnalytics");
                        scroll.ScrollToBottom(); PumpDispatcher(); window.UpdateLayout();
                        ScreenHarness.Save(window, Path.Combine(output, "details-" + theme + "-" + length + ".png"), 1180, 1040);
                        scroll.ScrollToTop(); PumpDispatcher(); window.UpdateLayout();
                    }
                }
            }
            var lengths = requireDefault ? new[] { FamilyWorldOptions.DefaultMinimumTagLength } : FamilyWorldOptions.LengthChoices;
            foreach (int length in lengths)
            {
                if (!requireDefault) lengthChoice.SelectedItem = length;
                PumpDispatcher();
                var active = root.ForLength(length);
                bool ready = active != null && ReferenceEquals(model.DatabaseGap.Summary, active) && model.DatabaseGap.HasData && model.DatabaseGap.CanExportFasta;
                exportsAgree &= ready;
                if (!ready) { transcript.Add(length + " refused: " + model.DatabaseGap.FastaState); continue; }
                foreach (var kind in new[] { ResultsExportKind.DatabaseGap, ResultsExportKind.SuggestedAdditions })
                {
                    exportChoice.SelectedItem = exportChoice.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == kind.ToString()); PumpDispatcher();
                    string[] paths = Enumerable.Range(1, 2).Select(repeat => Path.Combine(output, length + "-" + kind + "-" + repeat)).ToArray();
                    foreach (string path in paths)
                    {
                        ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = path; return true; };
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FinishBusyOperation(AppOperationHost.For(control));
                    }
                    bool equal = File.Exists(paths[0]) && File.Exists(paths[1]) && File.ReadAllBytes(paths[0]).SequenceEqual(File.ReadAllBytes(paths[1]));
                    if (equal && kind == ResultsExportKind.DatabaseGap) equal = File.ReadAllText(paths[0]) == active!.BuildCompositionCsv();
                    if (equal && kind == ResultsExportKind.SuggestedAdditions)
                    {
                        var records = File.ReadAllLines(paths[0]).Where(line => line.StartsWith('>')).Select(line => line[1..].Split(' ')[0]).ToHashSet(StringComparer.Ordinal);
                        equal = records.SetEquals(active!.Families.SelectMany(family => family.WorldProteins));
                        transcript.Add(length + " FASTA records: " + records.Count);
                        // Reuse the real stored-world checker for exact sequences and headers,
                        // not only accession membership.
                        var stored = FamilyWorldFetcher.Stored(project.fcf);
                        var actual = new Dictionary<string, System.Text.StringBuilder>(StringComparer.Ordinal);
                        string? accession = null;
                        foreach (string line in File.ReadAllLines(paths[0]))
                            if (line.StartsWith('>'))
                            {
                                accession = line[1..].Split(' ')[0]; actual.Add(accession, new());
                                equal &= line.EndsWith("world_sha256=" + stored.ContentSha256, StringComparison.Ordinal);
                            }
                            else if (accession != null) actual[accession].Append(line);
                            else equal = false;
                        var expected = stored.Entries.ToDictionary(entry => entry.Accession, entry => entry.Sequence, StringComparer.Ordinal);
                        equal &= actual.All(record => expected.TryGetValue(record.Key, out string? sequence) && sequence == record.Value.ToString());
                    }
                    exportsAgree &= equal;
                }
            }
            Check(requireDefault ? "real default screen and both native exports agree with Core and repeat bytes without a length choice"
                : "real length screen and both native exports follow every stored choice and repeat bytes", exportsAgree);
            Check("length screen leaves the copied project and computed tables unchanged", inputHash == FeatureLedgerEvidence.Hash(projectPath)
                && storedTables == JsonSerializer.Serialize(root));
            if (requireDefault && model.DatabaseGap.Summary is FamilyWorldSummary activeDefault)
            {
                var estimate = root.LengthEstimates?.Single(row => row.MinimumTagLength == FamilyWorldOptions.DefaultMinimumTagLength);
                FeatureLedgerEvidence.WriteJson(Path.Combine(output, "default-values.json"), new
                {
                    inputSha256 = inputHash, defaultLength = FamilyWorldOptions.DefaultMinimumTagLength,
                    storedTopLevelLength = root.MinimumTagLength, selectedLength = model.DatabaseGap.MinimumTagLength,
                    storedOverride = project.manifest.DatabaseGapMinimumTagLength, activeDefault.WorldFingerprint, activeDefault.WorldProteins,
                    activeDefault.MeasuredIdentifiedEnvelope, activeDefault.MeasuredDeNovoEnvelope, activeDefault.MeasuredTotalEnvelope,
                    activeDefault.ResidualEnvelope, activeDefault.UnexplainedEnvelope, estimate
                });
                transcript.Add("Core default: " + FamilyWorldOptions.DefaultMinimumTagLength + "; stored top level: " + root.MinimumTagLength
                    + "; stored override: " + (project.manifest.DatabaseGapMinimumTagLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"));
            }
            File.WriteAllLines(Path.Combine(output, "printed-lengths.txt"), transcript);
            foreach (string line in transcript) Console.WriteLine(line);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }
        finally { window.Close(); PumpDispatcher(); ShellDialogs.FilePresenter = oldPresenter; SynchronizationContext.SetSynchronizationContext(oldContext); Themes.ThemeManager.Apply(oldTheme); }
    }
}
