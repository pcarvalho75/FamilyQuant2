using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ExportWindowCommands() =>
    [
        new("--test-export-window", 70, TestCommandPhase.Raw,
            args => args.Contains("--test-export-window", StringComparer.Ordinal), (args, _) =>
            {
                ExportWindowTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "export-window"));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            })
    ];

    private static IReadOnlyList<ProjectExportOption> ExportOptions(MainWindow shell, AnalyticsViewModel? results = null)
    {
        IReadOnlyList<ProjectExportOption> options = Array.Empty<ProjectExportOption>();
        var previous = MainWindow.ExportPresenterOverride;
        MainWindow.ExportPresenterOverride = window => { options = window.Options; window.Close(); };
        try
        {
            if (results == null) ((MenuItem)shell.FindName("MenuItemExport")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            else typeof(MainWindow).GetMethod("OpenExportWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { results });
        }
        finally { MainWindow.ExportPresenterOverride = previous; }
        return options;
    }

    private static bool ChooseExport(MainWindow shell, ProjectExportKind kind, AnalyticsViewModel? results = null)
    {
        bool available = false;
        var previous = MainWindow.ExportPresenterOverride;
        MainWindow.ExportPresenterOverride = window =>
        {
            var option = window.Options.Single(item => item.Kind == kind);
            available = option.Available;
            ((ListBox)window.FindName("ExportChoices")).SelectedItem = option;
            ((Button)window.FindName("ExportButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Close();
        };
        try
        {
            if (results == null) ((MenuItem)shell.FindName("MenuItemExport")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            else typeof(MainWindow).GetMethod("OpenExportWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { results });
        }
        finally { MainWindow.ExportPresenterOverride = previous; }
        return available;
    }

    private static void ChooseResultsExport(AnalyticsViewModel model, ResultsExportKind kind)
    {
        var previousContext = SynchronizationContext.Current;
        var shell = OffscreenProofWindow(new MainWindow());
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(shell.Dispatcher));
            shell.Show(); PumpDispatcher();
            ChooseExport(shell, kind switch {
                ResultsExportKind.Summary => ProjectExportKind.RunSummary,
                ResultsExportKind.Peptides => ProjectExportKind.PeptideTable,
                ResultsExportKind.Homology => ProjectExportKind.Homology,
                ResultsExportKind.DatabaseGap => ProjectExportKind.DatabaseGap,
                ResultsExportKind.SuggestedAdditions => ProjectExportKind.SuggestedAdditions,
                _ => throw new InvalidOperationException("This export is an internal diagnostic, not a window choice.") }, model);
            FinishBusyOperation(shell.Operations); PumpDispatcher();
        }
        finally { shell.Close(); PumpDispatcher(); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static IReadOnlyList<ProjectExportOption> ResultsExportOptions(AnalyticsViewModel model)
    {
        var shell = OffscreenProofWindow(new MainWindow());
        try { shell.Show(); PumpDispatcher(); return ExportOptions(shell, model); }
        finally { shell.Close(); PumpDispatcher(); }
    }

    private static void ExportWindowTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var file = TaskFile();
        using (var taxonomy = new TaskTaxonomyHandler())
        using (var fetch = new WorldFetchHandler())
        {
            var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, taxonomy).GetAwaiter().GetResult();
            var task = EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
            var fetched = CoreOperation.RunAsync("Synthetic Export world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, task, context, fetch)).GetAwaiter().GetResult();
            if (fetched.Status != OperationStatus.Succeeded) throw new InvalidOperationException("Synthetic world fixture failed.");
        }
        var stored = FamilyWorldFetcher.Stored(file);
        var world = FamilyWorld.FromSnapshot(stored);
        var summary = new FamilyWorldSummary { CompositionRules = FamilyWorldErrors.CompositionRule,
            MinimumTagLength = FamilyWorldOptions.DefaultMinimumTagLength, ScopeFamilies = stored.Families.ToList(),
            WorldFingerprint = world.Fingerprint, WorldRelease = stored.Release, Rules = "Synthetic stored rules" };
        summary.Composition.Add(new FamilyWorldCompositionRow { Family = stored.Families[0], IdentifiedEnvelope = 10, WorldDeNovoEnvelope = 20 });
        summary.Families.Add(new FamilyWorldFamilySummary { Family = stored.Families[0], WorldProteins = stored.Entries.Select(entry => entry.Accession).ToList() });
        var rows = new[] { Qr("identified", "export_fixture", 1, 2, "ACDEFGHIK", 100, 150) };
        var manifest = new QuantManifest { DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method,
            PerRunTic = new() { ["export_fixture"] = 1000 }, FamilyWorldResult = summary,
            FamilyRunShares = new() { MembershipSha256 = FamilyStudyMembership.Fingerprint(file),
                Families = new() { new() { FamilyUserId = file.MyExtendedFastaItems[0].UserID, MeanShare = .125, AvailableRunCount = 1 } },
                Runs = new() { new() { Run = "export_fixture", AssignedSignal = 100, MeasuredSignal = 200 } } } };
        string input = Path.Combine(output, "fixture.fqproj");
        ProjectStore.WriteFqproj(input, file, rows, manifest);
        string sourceHash = TestFileHash.Upper(input);
        var previousFile = ShellDialogs.FilePresenter; var previousMessages = ShellMessages.Presenter;
        var previousChoice = MainWindow.UnsavedProjectChoiceOverride; var previousTheme = Themes.ThemeManager.Current;
        var previousReportPath = MainWindow.SelectReportPathOverride; var previousReport = MainWindow.ReportExportPresenterOverride;
        var previousFactory = MainWindow.ReportDocumentFactoryOverride;
        var previousContext = SynchronizationContext.Current;
        var shell = OffscreenProofWindow(new MainWindow { Width = 1366, Height = 820 });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(shell.Dispatcher));
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        try
        {
            shell.Show(); shell.OpenProjectPath(input); WaitForReportFixture(shell); FinishBusyOperation(shell.Operations);
            typeof(MainWindow).GetField("latestHomologyResult", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, new DeNovoHomology());
            var control = ((Grid)shell.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
            var options = ExportOptions(shell);
            Check("one Export window keeps the five sections in order and all eleven file choices", options.Count == 11
                && options.Select(option => option.Section).Distinct().SequenceEqual(new[] { "Report", "Tables (CSV)", "Suggested additions (FASTA)", "Families (.fcf)", "Interpretation input (Markdown)" })
                && options.All(option => option.Available) && options.Select(option => option.Kind).Distinct().Count() == 11);
            Check("File keeps Report details and AI draft beside one Export opener", shell.FindName("MenuItemExportReport") == null
                && !((MenuItem)shell.FindName("MenuItemExport")).HasItems && shell.FindName("MenuItemReportDetails") != null && shell.FindName("MenuItemAiDraft") != null);
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var window = OffscreenProofWindow(new ExportWindow(options));
                try { window.Show(); PumpDispatcher(); ScreenHarness.Save(window, Path.Combine(output, "export-" + theme + ".png"), 680, 720); }
                finally { window.Close(); }
            }
            string target = "";
            ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = target; return true; };
            var model = new AnalyticsViewModel(rows, manifest);
            var gap = new DatabaseGapViewModel(summary, control.fcf);
            var expected = new Dictionary<ProjectExportKind, byte[]>
            {
                [ProjectExportKind.RunSummary] = Encoding.UTF8.GetBytes(AnalyticsCsv.BuildSummaryCsv(model.Data!.Fractions, model.Data.Grouped)),
                [ProjectExportKind.PeptideTable] = Encoding.UTF8.GetBytes(AnalyticsCsv.BuildPeptideCsv(model.Data.Peptides)),
                [ProjectExportKind.PanelAudit] = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(PanelAuditCsv.BuildCsv(control.fcf))).ToArray(),
                [ProjectExportKind.DatabaseGap] = Encoding.UTF8.GetBytes(gap.BuildCsv()),
                [ProjectExportKind.SuggestedAdditions] = Encoding.UTF8.GetBytes(gap.BuildSuggestedFasta()),
                [ProjectExportKind.FamilyComposition] = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(control.FullXicCompositionCsv()!)).ToArray(),
                [ProjectExportKind.InterpretationInput] = Encoding.UTF8.GetBytes(ReportInterpretationInput.FromProject(control.fcf, manifest, AppVersionInfo.Current()))
            };
            string expectedFcf = Path.Combine(output, "reference.fcf"); control.fcf.Save(expectedFcf);
            expected[ProjectExportKind.Families] = File.ReadAllBytes(expectedFcf);
            string expectedHomology = Path.Combine(output, "reference-homology.csv"); HomologyCsv.Write(expectedHomology, new DeNovoHomology());
            expected[ProjectExportKind.Homology] = File.ReadAllBytes(expectedHomology);
            string before = control.fcf.ToJsonText();
            foreach (var pair in expected)
            {
                target = Path.Combine(output, pair.Key + ".out");
                ChooseExport(shell, pair.Key); FinishBusyOperation(shell.Operations); FinishBusyOperation(control.Operations); PumpDispatcher();
                Check(pair.Key + " writes the same bytes as its existing writer", File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(pair.Value));
            }
            target = Path.Combine(output, "visible.csv");
            ChooseExport(shell, ProjectExportKind.VisibleProteins); FinishBusyOperation(shell.Operations); FinishBusyOperation(control.Operations); PumpDispatcher();
            var mode = control.ViewModel.Quantitation.DisplayedMode;
            string visible = CsvReport.Build(control.ViewModel.Rows.ToList(), mode, control.ViewModel.Quantitation.FullXicCsvHeader());
            Check("Visible protein table keeps the existing filtered CSV and encoding", File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(visible))));
            Check("non-report exports preserve project values and the input project file", control.fcf.ToJsonText() == before && TestFileHash.Upper(input) == sourceHash);
            MainWindow.ReportDocumentFactoryOverride = (_, value, source, _) => ReportDocumentBuilder.Build(source, value, source.ReportDetails, source.ReportInputProvenance);
            MainWindow.ReportExportPresenterOverride = editor => editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            target = Path.Combine(output, "report.docx"); MainWindow.SelectReportPathOverride = () => target;
            ChooseExport(shell, ProjectExportKind.Report);
            Check("Report opens the existing writer and publishes Word, workbook and HTML", shell.ReportExportTask != null
                && FinishReportCommand(shell.ReportExportTask) && File.Exists(target)
                && File.Exists(ReportSupplementWorkbook.PathForReport(target)) && File.Exists(HtmlReportWriter.PathForReport(target)));
            int shortcuts = 0;
            var previousExport = MainWindow.ExportPresenterOverride;
            MainWindow.ExportPresenterOverride = window =>
            {
                shortcuts++;
                Check("export shortcut retains all kept choices", window.Options.Select(option => option.Kind).SequenceEqual(options.Select(option => option.Kind)));
                window.Close();
            };
            try
            {
                ((Button)control.FindName("ButtonSaveReport")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                MainWindow.NavigateShellCommand.Execute(ShellDestination.Results, shell); FinishBusyOperation(shell.Operations); PumpDispatcher();
                var results = ((Grid)shell.FindName("GridApp")).Children.OfType<AnalyticsControl>().Single();
                ((Button)results.FindName("ButtonExport")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("Curate and Results open the same Export window and Results has no format list", shortcuts == 2 && results.FindName("ComboBoxExportKind") == null);
            }
            finally { MainWindow.ExportPresenterOverride = previousExport; }
            target = Path.Combine(output, "visible-from-results.csv");
            ChooseExport(shell, ProjectExportKind.VisibleProteins); FinishBusyOperation(shell.Operations); FinishBusyOperation(control.Operations); PumpDispatcher();
            Check("Visible protein CSV stays reachable while Curate is detached in Results", Window.GetWindow(control) == null
                && File.Exists(target) && File.ReadAllText(target) == visible);
            var missing = ResultsExportOptions(new AnalyticsViewModel(Array.Empty<QuantRow>(), null));
            Check("empty export choices explain the missing data and expose no file action", missing.All(option => !option.Available && option.Hint.Length > 40));
        }
        finally
        {
            shell.Close(); PumpDispatcher();
            ShellDialogs.FilePresenter = previousFile; ShellMessages.Presenter = previousMessages;
            MainWindow.UnsavedProjectChoiceOverride = previousChoice; Themes.ThemeManager.Apply(previousTheme);
            MainWindow.SelectReportPathOverride = previousReportPath; MainWindow.ReportExportPresenterOverride = previousReport;
            MainWindow.ReportDocumentFactoryOverride = previousFactory;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
}
