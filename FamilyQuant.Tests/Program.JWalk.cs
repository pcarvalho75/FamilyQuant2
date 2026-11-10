using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

// Mission J's end-to-end walk through the program's own screens, on a private desktop: Families fetches
// evidence and the family world, Quantify computes with the chosen de novo source, the project is saved
// with the shell's verified save, Families exports its tables and Results exports every kind it offers.
// Each stage is timed and resumes from its saved project. Only the cache and the spectrum CSV paths differ
// from the App's own Compute engine, so nothing is written beside the RAW acquisitions.
internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> JWalkCommands() =>
    [new("--j-walk", 10462, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--j-walk", (args, _) => JWalk(args))];

    private static int JWalk(string[] args)
    {
        if (args.Length != 10) throw new ArgumentException("Use --j-walk <project.fqproj> <raw-folder> <sepr2> <denovo-root> <output> <cache> <scope-or-dash> <denovo-mode> <peaks-min-alc>.");
        string output = Path.GetFullPath(args[5]), cache = Path.GetFullPath(args[6]);
        Directory.CreateDirectory(output); Directory.CreateDirectory(cache);
        string logPath = Path.Combine(output, "j-walk.log");
        if (!NativeProofDesktop.IsPrivate) return NativeProofDesktop.Run(args, logPath, 57_600_000);
        using var log = new StreamWriter(logPath, append: true) { AutoFlush = true };
        var previous = Console.Out; var previousError = Console.Error; var previousContext = SynchronizationContext.Current;
        Console.SetOut(log); Console.SetError(log);
        var timings = new List<string>();
        void Say(string text) => Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + text);
        void Timed(string stage, Stopwatch watch)
        {
            string line = stage + "\t" + watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
            timings.Add(line); File.AppendAllText(Path.Combine(output, "timings.tsv"), line + Environment.NewLine);
            Say("STAGE " + stage + " took " + watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");
        }
        Window? host = null;
        try
        {
            EnsureApplication();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            { if (sender is Window window) { window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = window.Top = -30000; } }), true);
            Say("J walk start. Inputs: project " + args[1] + " (" + TestFileHash.Upper(args[1]) + "), RAW " + args[2] + ", SEPro " + args[3]
                + " (" + TestFileHash.Upper(args[3]) + "), de novo " + args[4] + ", scope " + args[7] + ", source " + args[8] + ", ALC " + args[9]);
            host = new Window { Width = 1440, Height = 1000, ShowActivated = false, ShowInTaskbar = false, Left = -30000, Top = -30000 };
            host.Show(); PumpDispatcher();

            // Stage 2a: Families > Fetch evidence.
            string evidenceDir = Path.Combine(output, "s2a-evidence"), evidenceProject = Path.Combine(evidenceDir, "evidence.fqproj");
            Directory.CreateDirectory(evidenceDir);
            var watch = Stopwatch.StartNew();
            bool resumedEvidence = File.Exists(evidenceProject);
            var (file, rows, manifest) = ProjectStore.ReadFqproj(resumedEvidence ? evidenceProject : args[1]);
            var curation = new ClusterGenerationControl(file); host.Content = curation; PumpDispatcher();
            string lastMessage = "";
            curation.Operations.PropertyChanged += (_, _) =>
            { if (curation.Operations.Message != lastMessage) { lastMessage = curation.Operations.Message; Say("Families: " + lastMessage); } };
            if (!resumedEvidence)
            {
                WaitWorldWalk(curation.FetchProteinEvidenceAsync(), TimeSpan.FromHours(6));
                Say("Evidence status: " + JWalkStatus(curation));
                Say(ShellProjectOperations.WriteVerifiedProject(evidenceProject, file, rows, manifest));
                Timed("2a fetch evidence", watch);
            }
            else Say("Resumed evidence from " + evidenceProject);
            var targets = file.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)).ToList();
            Say("Evidence: " + targets.Count(p => p.Evidence != null) + " of " + targets.Count + " targets carry evidence; "
                + targets.Count(p => !string.IsNullOrWhiteSpace(p.Evidence?.Family)) + " carry a UniProt family; source "
                + file.EvidenceSource?.Provider + " " + file.EvidenceSource?.Release);
            JWalkImage(host, Path.Combine(evidenceDir, "families-evidence.png"));

            // Stage 2b: Families > Fetch family world, through the scope dialog.
            string worldDir = Path.Combine(output, "s2b-world"), worldProject = Path.Combine(worldDir, "world.fqproj");
            Directory.CreateDirectory(worldDir);
            watch.Restart();
            if (!File.Exists(worldProject))
            {
                var confirm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                confirm.Tick += (_, _) =>
                {
                    var dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "Confirm family-world scope");
                    if (dialog?.Content is not StackPanel panel) return;
                    confirm.Stop();
                    foreach (var text in panel.Children.OfType<TextBlock>()) Say("Scope dialog text: " + text.Text);
                    var box = panel.Children.OfType<TextBox>().Single();
                    Say("Scope dialog proposed: " + box.Text);
                    if (args[7] != "-") box.Text = args[7];
                    Say("Scope confirmed as: " + box.Text);
                    panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                confirm.Start();
                try
                {
                    ((Button)curation.FindName("ButtonFetchFamilyWorld")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    WaitWorldWalk(curation.FamilyWorldFetchCompletion, TimeSpan.FromHours(6));
                }
                finally { confirm.Stop(); }
                Say("Family world status: " + JWalkStatus(curation));
                Say(ShellProjectOperations.WriteVerifiedProject(worldProject, file, rows, manifest));
                Timed("2b fetch family world", watch);
            }
            else Say("Resumed family world from " + worldProject);
            var offline = ProjectStore.ReadFqproj(worldProject);
            try
            {
                var world = FamilyWorldFetcher.Stored(offline.fcf);
                Say("Stored world: release " + world.Release + "; entries " + world.Entries.Count + " (" + world.Entries.Count(e => e.FromReviewedPanel)
                    + " from the reviewed panel); families in scope " + world.Families.Count + "; hash " + world.ContentSha256);
                Say("World families: " + string.Join("; ", world.Families));
            }
            catch (Exception ex) { Say("No stored family world: " + ex.Message); }
            JWalkImage(host, Path.Combine(worldDir, "families-world.png"));

            // Stage 3: Quantify > Compute, with the App's view model and the de novo source a person picks.
            string computeDir = Path.Combine(output, "s3-compute"), computed = Path.Combine(computeDir, "computed.fqproj");
            Directory.CreateDirectory(computeDir);
            watch.Restart();
            if (!File.Exists(computed))
            {
                Func<ComputeXicRequest, IProgress<string>, CancellationToken, Task<FullXicComputeResult>> engine = (request, progress, token) =>
                    Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        request.Options.SpectrumComparisonCsvPath = Path.Combine(computeDir, FamilyQuant.DeNovo.SpectrumComparisonTable.DefaultFileName);
                        return FullXicComputer.ComputeFullXic(request.RawDirectory, request.SeproPackage, request.DeNovoDirectory, request.Options,
                            (side, run) => Path.Combine(cache, "fullxic_" + side + "_" + run + ".json"),
                            message => { token.ThrowIfCancellationRequested(); progress.Report(message); },
                            detectedCachePathFor: run => Path.Combine(cache, "ms1envelope_" + run + ".json"));
                    });
                var compute = new ComputeXicViewModel(AppVersionInfo.Current(), offline.manifest, offline.fcf, null, engine)
                { RawDirectory = args[2], SeproPackage = args[3], DeNovoDirectory = args[4] };
                Say("Quantify defaults: source " + compute.Filters.ModeTag + ", PEAKS ALC " + compute.Filters.PeaksMinimumAlc + ", length "
                    + compute.Filters.MinimumLength + ", stretch " + compute.Filters.StretchLength + " at " + compute.Filters.StretchFloor
                    + ", isotopes " + compute.IsotopeCount + ", points " + compute.MinPointsForQuant);
                compute.Filters.ModeTag = args[8]; compute.Filters.PeaksMinimumAlc = args[9];
                Say("Quantify chosen: source " + compute.Filters.ModeTag + " (" + string.Join(", ", compute.Filters.Sources.Select(s => s.DisplayName))
                    + "), PEAKS ALC " + compute.Filters.PeaksMinimumAlc);
                var quantify = new QuantifyControl(compute); host.Content = quantify; PumpDispatcher();
                string lastStatus = "";
                compute.PropertyChanged += (_, e) =>
                { if (e.PropertyName == nameof(compute.Status) && compute.Status != lastStatus) { lastStatus = compute.Status; Say("Compute: " + lastStatus); } };
                var button = (Button)quantify.FindName("ButtonCompute");
                button.Command.Execute(button.CommandParameter);
                while (compute.IsBusy && watch.Elapsed < TimeSpan.FromHours(8)) { PumpDispatcher(); Thread.Sleep(20); }
                JWalkImage(host, Path.Combine(computeDir, "quantify.png"));
                if (compute.IsBusy || compute.Result == null) { Say("Compute did not complete: " + compute.Status); Timed("3 compute (failed)", watch); return 2; }
                Say("Compute run summary: " + compute.RunSummary);
                Say("Compute details:" + Environment.NewLine + compute.Details);
                Timed("3 compute", watch);
                watch.Restart();
                Say(ShellProjectOperations.WriteVerifiedProject(computed, offline.fcf, compute.Result.Rows, compute.Result.Manifest));
                Timed("3b save computed project", watch);
            }
            else Say("Resumed computed project " + computed);

            // Stage 4: Families with the stored quantitation; its two table exports.
            string familiesDir = Path.Combine(output, "s4-families");
            Directory.CreateDirectory(familiesDir);
            watch.Restart();
            var reopened = ProjectStore.ReadFqproj(computed);
            var families = new ClusterGenerationControl(reopened.fcf); host.Content = families; PumpDispatcher();
            WaitWorldWalk(families.SetFullXicQuantAsync(reopened.quant, reopened.manifest.PerRunTic), TimeSpan.FromMinutes(60));
            WaitWorldWalk(families.FullXicReady, TimeSpan.FromMinutes(60));
            Say("Families with full XIC: " + families.HasFullXic + "; families " + families.FullXicFamilies?.Families.Count);
            Timed("4 families full XIC column", watch);
            JWalkImage(host, Path.Combine(familiesDir, "families-fullxic.png"));
            watch.Restart();
            JWalkButtonExport(families, "ButtonExportComposition", Path.Combine(familiesDir, "FamilyQuant_family_composition.csv"), Say);
            JWalkButtonExport(families, "ButtonSaveReport", Path.Combine(familiesDir, "FamilyQuant_Report.csv"), Say);
            Say("Families status: " + JWalkStatus(families));
            Timed("4b families exports", watch);

            // Stage 5: Results, built as the shell builds it, and every export kind it offers.
            string resultsDir = Path.Combine(output, "s5-results");
            Directory.CreateDirectory(resultsDir);
            watch.Restart();
            var held = families.HasFullXic ? families.CurrentFullXicAttribution() : null;
            var model = Task.Run(() => new AnalyticsViewModel(reopened.quant, reopened.manifest, reopened.fcf, held, null)).GetAwaiter().GetResult();
            var analytics = new AnalyticsControl(model); host.Content = analytics; PumpDispatcher();
            Timed("5 open Results", watch);
            JWalkImage(host, Path.Combine(resultsDir, "results.png"));
            foreach (var card in model.Cards) Say("Results card: " + card.Title + " = " + card.Value + " (" + card.Detail + ")");
            Say("Results details: " + model.Details);
            var gap = model.DatabaseGap;
            Say("Database gap: has data " + gap.HasData + "; rows " + gap.Rows.Count + "; " + gap.Rules + " " + gap.Scope);
            Say("Database gap FASTA: " + gap.FastaState);
            foreach (var row in gap.LengthRows) Say("Database gap length row: " + row.MinimumTagLength + " table " + row.HasTable);
            watch.Restart();
            foreach (var kind in Enum.GetValues<ResultsExportKind>().Where(kind => kind is not (ResultsExportKind.Figures or ResultsExportKind.EvidencePack)))
            {
                model.ExportKind = kind; PumpDispatcher();
                if (!model.CanExport) { Say("Results export " + kind + " UNAVAILABLE: " + model.ExportUnavailableReason); continue; }
                bool folder = kind is ResultsExportKind.Figures or ResultsExportKind.EvidencePack;
                string target = Path.Combine(resultsDir, kind switch
                {
                    ResultsExportKind.Summary => "familyquant_analytics_summary.csv",
                    ResultsExportKind.Peptides => "familyquant_analytics_peptides.csv",
                    ResultsExportKind.Homology => "FamilyQuant_DeNovo_Homology.csv",
                    ResultsExportKind.DatabaseGap => "familyquant_database_gap.csv",
                    ResultsExportKind.SuggestedAdditions => "familyquant_suggested_additions.fasta",
                    _ => kind.ToString().ToLowerInvariant()
                });
                var exportWatch = Stopwatch.StartNew();
                if (!folder) { using var driver = new NativeOwnedDialogDriver(target); ChooseResultsExport(model, kind); }
                else
                {
                    Directory.CreateDirectory(target);
                    var task = model.ExportAsync(target, AppOperationHost.For(analytics));
                    WaitWorldWalk(task, TimeSpan.FromMinutes(30));
                }
                Say("Results export " + kind + " (" + exportWatch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s): " + model.Status);
            }
            Timed("5b Results exports", watch);
            Say("Timings:" + Environment.NewLine + string.Join(Environment.NewLine, timings));
            Say("J walk finished.");
            return 0;
        }
        catch (Exception ex) { Say("FAILED " + ex); return 1; }
        finally { host?.Close(); Console.SetOut(previous); Console.SetError(previousError); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static string JWalkStatus(FrameworkElement screen) =>
        string.Join(" | ", JWalkTexts(screen).Where(t => t.Length > 0).Distinct().Take(6));

    private static IEnumerable<string> JWalkTexts(DependencyObject root)
    {
        foreach (string name in new[] { "TextBlockStatusLine", "TextBlockStatus", "TextBlockExportState" })
            if (root is FrameworkElement element && element.FindName(name) is TextBlock text) yield return text.Text;
    }

    private static void JWalkButtonExport(FrameworkElement screen, string buttonName, string path, Action<string> say)
    {
        using var driver = new NativeOwnedDialogDriver(path);
        if (screen is ClusterGenerationControl && Window.GetWindow(screen) is MainWindow shell)
        {
            ChooseExport(shell, buttonName == "ButtonExportComposition" ? ProjectExportKind.FamilyComposition : ProjectExportKind.VisibleProteins);
            FinishBusyOperation(shell.Operations); PumpDispatcher();
            say("Export " + buttonName + " to " + path + ": written " + File.Exists(path)); return;
        }
        var button = (Button)screen.FindName(buttonName);
        if (!button.IsEnabled) { say("Export " + buttonName + " UNAVAILABLE: " + button.ToolTip); return; }
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var watch = Stopwatch.StartNew();
        while (!File.Exists(path) && watch.Elapsed < TimeSpan.FromMinutes(10)) { PumpDispatcher(); Thread.Sleep(10); }
        while (AppOperationHost.For(screen).IsBusy && watch.Elapsed < TimeSpan.FromMinutes(10)) { PumpDispatcher(); Thread.Sleep(10); }
        say("Export " + buttonName + " to " + path + ": dialog " + driver.SawDialog + ", timed out " + driver.TimedOut + ", written " + File.Exists(path)
            + (File.Exists(path) ? " (" + new FileInfo(path).Length + " bytes)" : ""));
    }

    private static void JWalkImage(Window host, string path)
    {
        try { SaveWorldWalkImage(host, path); } catch (Exception ex) { Console.WriteLine("Capture failed for " + path + ": " + ex.Message); }
    }
}
