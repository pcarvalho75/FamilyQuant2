using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

// Mission J's full run (J7a) through the real shell on a private desktop: File > Import family panel, the editor's
// Pfam check and the panel template for subclass rules, Families > Group by evidence with the user's link choices,
// File > Report details, Quantify > Compute, Results with Sensitivity, Save, reopen and a second Compute, then
// File > Export report. Every choice a person makes comes from the supplied JSON (user data), never from code.
// Each stage saves the project with the shell's Save As and resumes from it.
internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> J7aWalkCommands() =>
    [new("--j7a-walk", 10463, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--j7a-walk", (args, _) => J7aWalk(args)),
     new("--j7a-report-probe", 10464, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--j7a-report-probe", (args, _) => J7aReportProbe(args)),
     new("--j7b-walk", 10465, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--j7b-walk", (args, _) => J7bWalk(args))];

    // J7b through the shell: open the project, show Families and export its composition and protein table, then, when an
    // interpretation file is given, File > Report details writes it into the written-interpretation field (the other
    // fields kept) and File > Save as writes the new project.
    private static int J7bWalk(string[] args)
    {
        if (args.Length is not (3 or 5)) throw new ArgumentException("Use --j7b-walk <project.fqproj> <new-output> [interpretation.txt output.fqproj].");
        string output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
        string logPath = Path.Combine(output, "j7b-walk.log");
        if (!NativeProofDesktop.IsPrivate) return NativeProofDesktop.Run(args, logPath, 14_400_000);
        using var log = new StreamWriter(logPath, append: true) { AutoFlush = true };
        var previousOut = Console.Out; Console.SetOut(log); Console.SetError(log);
        void Say(string text) => Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + text);
        MainWindow? shell = null; string? savePath = null;
        try
        {
            EnsureApplication();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            MainWindow.ShellSettingsPathOverride = Path.Combine(output, "shell-preferences.json");
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            MainWindow.SelectSaveProjectPathOverride = () => savePath;
            ShellMessages.Presenter = (_, message) => { Say("Shell message: " + message.Text.Replace("\n", " | ")); return MessageBoxResult.OK; };
            Say("J7b walk: " + args[1] + " (" + TestFileHash.Upper(args[1]) + ")");
            shell = new MainWindow { Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Width = 1440, Height = 1000 };
            shell.Show(); PumpDispatcher();
            if (!shell.OpenProjectPath(args[1])) throw new InvalidOperationException("The shell did not open the project.");
            J7aWaitIdle(shell, TimeSpan.FromHours(2));
            var families = J7aFamilies(shell);
            J7aWait(() => families.FullXicReady.IsCompleted, TimeSpan.FromHours(2));
            shell.NavigationModel.RequestNavigate(ShellDestination.Families); PumpDispatcher();
            Say("Families displayed: " + (PresentationSource.FromVisual(families) != null) + "; status " + J7aStatus(shell));
            J7aButtonExport(families, "ButtonExportComposition", Path.Combine(output, "FamilyQuant_family_composition.csv"), Say);
            J7aButtonExport(families, "ButtonSaveReport", Path.Combine(output, "FamilyQuant_Report.csv"), Say);
            if (args.Length == 5)
            {
                string text = File.ReadAllText(args[3], Encoding.UTF8).Replace("\r\n", "\n");
                MainWindow.ReportDetailsPresenterOverride = editor =>
                {
                    editor.ViewModel.WrittenInterpretation = text;
                    editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                ((MenuItem)shell.FindName("MenuItemReportDetails")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                MainWindow.ReportDetailsPresenterOverride = null;
                var details = families.fcf.ReportDetails;
                Say("Report details now: title '" + details?.Title + "', interpretation " + (details?.WrittenInterpretation.Length ?? 0) + " characters, equal to the file "
                    + (details?.WrittenInterpretation == text));
                savePath = Path.GetFullPath(args[4]); Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                ApplicationCommands.SaveAs.Execute(null, shell);
                J7aWait(() => string.Equals(shell.DocumentState.ProjectPath, savePath, StringComparison.OrdinalIgnoreCase)
                    && !shell.DocumentState.IsModified && !shell.Operations.IsBusy, TimeSpan.FromMinutes(30));
                Say("Saved " + savePath + " (" + new FileInfo(savePath).Length + " bytes; " + shell.DocumentState.SaveCheckText + ")");
                var reread = ProjectStore.ReadFqproj(savePath).fcf.ReportDetails;
                Say("Reopened interpretation equal: " + (reread?.WrittenInterpretation == text));
            }
            Say("J7b walk finished.");
            return 0;
        }
        catch (Exception ex) { Say("FAILED " + ex); return 1; }
        finally { shell?.Close(); Console.SetOut(previousOut); }
    }

    // Runs the report preparation Export report uses (ReportProjectExporter.Build on an owned STA thread) on a saved
    // project, first with no window, then after the shell opened the project, and prints any exception in full.
    private static int J7aReportProbe(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Use --j7a-report-probe <project.fqproj> <new-output>.");
        string output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
        string logPath = Path.Combine(output, "report-probe.log");
        if (!NativeProofDesktop.IsPrivate) return NativeProofDesktop.Run(args, logPath, 14_400_000);
        using var log = new StreamWriter(logPath, append: true) { AutoFlush = true };
        var previousOut = Console.Out; Console.SetOut(log); Console.SetError(log);
        void Say(string text) => Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + text);
        int failures = 0;
        void Prepare(string label, IReadOnlyList<QuantRow> rows, QuantManifest manifest, FamilyClusterFile curation)
        {
            var snapshot = FamilyClusterFile.FromJsonText(curation.ToJsonText());
            string folder = Path.Combine(output, label);
            var watch = Stopwatch.StartNew();
            try
            {
                Report.ReportProjectExport? prepared = null;
                J7aWait(AppOperationFiles.RunSta(() => prepared = Report.ReportProjectExporter.Build(rows.ToArray(), manifest, snapshot, folder,
                    snapshot.ReportDetails, snapshot.ReportInputProvenance)), TimeSpan.FromHours(2));
                Say(label + ": prepared in " + watch.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s; sections "
                    + prepared!.Document.Sections.Count + ", supplement sheets " + prepared.SupplementSheets.Count + ", CSV exports " + prepared.CsvExports.Count);
            }
            catch (Exception ex) { failures++; Say(label + ": FAILED after " + watch.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s: " + ex); }
        }
        try
        {
            EnsureApplication();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var (file, rows, manifest) = ProjectStore.ReadFqproj(args[1]);
            Say("Probe: " + args[1] + " (" + TestFileHash.Upper(args[1]) + "), " + rows.Count + " quant rows");
            Prepare("a-no-window", rows, manifest, file);
            MainWindow.ShellSettingsPathOverride = Path.Combine(output, "shell-preferences.json");
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            ShellMessages.Presenter = (_, message) => { Say("Shell message: " + message.Text.Replace("\n", " | ")); return MessageBoxResult.OK; };
            var shell = new MainWindow { Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Width = 1440, Height = 1000 };
            shell.Show(); PumpDispatcher();
            if (!shell.OpenProjectPath(args[1])) throw new InvalidOperationException("The shell did not open the project.");
            J7aWaitIdle(shell, TimeSpan.FromHours(2));
            var families = J7aFamilies(shell);
            J7aWait(() => families.FullXicReady.IsCompleted, TimeSpan.FromHours(2));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var heldRows = (List<QuantRow>)typeof(MainWindow).GetField("loadedQuant", flags)!.GetValue(shell)!;
            var heldManifest = (QuantManifest)typeof(MainWindow).GetField("loadedManifest", flags)!.GetValue(shell)!;
            Prepare("b-after-shell-open", heldRows, heldManifest, families.fcf);
            shell.Close();
            Say("Probe finished; failures " + failures);
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Say("PROBE FAILED " + ex); return 1; }
        finally { Console.SetOut(previousOut); }
    }

    private sealed class J7aConfig
    {
        public string Start { get; set; } = "";
        public string Output { get; set; } = "";
        public string Cache { get; set; } = "";
        public string Raw { get; set; } = "";
        public string Sepr2 { get; set; } = "";
        public string DeNovo { get; set; } = "";
        public string DeNovoMode { get; set; } = "";
        public string PeaksMinimumAlc { get; set; } = "";
        public string Workbook { get; set; } = "";
        public string Sheet { get; set; } = "";
        public int HeaderRow { get; set; } = 1;
        public string PanelName { get; set; } = "";
        public Dictionary<string, string> Columns { get; set; } = new();
        public List<FamilyPanelDecisionMapping> Decisions { get; set; } = new();
        public List<J7aSubclass> Subclasses { get; set; } = new();
        public List<J7aLink> Links { get; set; } = new();
        public string? Scope { get; set; }
        public Report.ReportDetails Details { get; set; } = new();
        public bool UseOwnCache { get; set; } = true;
        public bool SkipRepeat { get; set; }
        public bool SkipReport { get; set; }
    }

    private sealed class J7aSubclass
    {
        public string Family { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> Required { get; set; } = new();
        public List<string> Absent { get; set; } = new();
        public List<string> Subfamilies { get; set; } = new();
        public string ReportAs { get; set; } = "";
    }

    private sealed class J7aLink
    {
        public string SourceFamily { get; set; } = "";
        public string Architecture { get; set; } = "*";
        public string? Key { get; set; }
    }

    private static readonly JsonSerializerOptions J7aJson = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private static int J7aWalk(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Use --j7a-walk <config.json>.");
        var config = JsonSerializer.Deserialize<J7aConfig>(File.ReadAllText(args[1]), J7aJson)!;
        string output = Path.GetFullPath(config.Output);
        Directory.CreateDirectory(output);
        string logPath = Path.Combine(output, "j7a-walk.log");
        if (!NativeProofDesktop.IsPrivate) return NativeProofDesktop.Run(args, logPath, 57_600_000);
        using var log = new StreamWriter(logPath, append: true) { AutoFlush = true };
        var previousOut = Console.Out; var previousError = Console.Error; var previousContext = SynchronizationContext.Current;
        Console.SetOut(log); Console.SetError(log);
        void Say(string text) => Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + text);
        var watch = new Stopwatch();
        void Timed(string stage)
        {
            string line = stage + "\t" + watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
            File.AppendAllText(Path.Combine(output, "timings.tsv"), line + Environment.NewLine);
            Say("STAGE " + stage + " took " + watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");
        }
        MainWindow? shell = null;
        string? savePath = null;
        try
        {
            EnsureApplication();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            { if (sender is Window window) { window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = window.Top = -30000; } }), true);
            MainWindow.ShellSettingsPathOverride = Path.Combine(output, "shell-preferences.json");
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            MainWindow.SelectSaveProjectPathOverride = () => savePath;
            ShellMessages.Presenter = (_, message) => { Say("Shell message: " + message.Text.Replace("\n", " | ")); return MessageBoxResult.OK; };
            Say("J7a walk start. Config " + Path.GetFullPath(args[1]) + " (" + TestFileHash.Upper(args[1]) + "); start project " + config.Start
                + " (" + TestFileHash.Upper(config.Start) + "); workbook " + config.Workbook + " (" + TestFileHash.Upper(config.Workbook) + ")");
            shell = new MainWindow { Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Width = 1440, Height = 1000 };
            shell.Show(); PumpDispatcher();
            string lastStatus = "";
            void Open(string path)
            {
                if (!shell.OpenProjectPath(path)) throw new InvalidOperationException("The shell did not open " + path);
                J7aWaitIdle(shell, TimeSpan.FromHours(2));
                var families = J7aFamilies(shell);
                J7aWait(() => families.FullXicReady.IsCompleted, TimeSpan.FromHours(2));
                families.Operations.PropertyChanged += (_, _) =>
                { if (families.Operations.Message != lastStatus && families.Operations.Message.Length > 0) { lastStatus = families.Operations.Message; Say("Families: " + lastStatus); } };
                Say("Opened " + path + ": " + families.fcf.MyExtendedFastaItems.Count + " rows, panel " + (families.fcf.FamilyPanel?.Families.Count.ToString(CultureInfo.InvariantCulture) ?? "none"));
            }
            void Save(string path)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                savePath = path;
                ApplicationCommands.SaveAs.Execute(null, shell);
                J7aWait(() => string.Equals(shell.DocumentState.ProjectPath, path, StringComparison.OrdinalIgnoreCase)
                    && !shell.DocumentState.IsModified && !shell.Operations.IsBusy, TimeSpan.FromMinutes(30));
                if (!File.Exists(path)) throw new IOException("Save As did not write " + path);
                Say("Saved " + path + " (" + new FileInfo(path).Length + " bytes; " + shell.DocumentState.SaveCheckText + ")");
            }
            string Stage(string name, string file) => Path.Combine(output, name, file);

            // Stage 1: File > Import family panel, the Pfam check, the subclass rules entered through the panel template.
            string panelProject = Stage("s1-panel", "panel.fqproj");
            watch.Restart();
            if (!File.Exists(panelProject))
            {
                Open(config.Start);
                string dir = Path.GetDirectoryName(panelProject)!; Directory.CreateDirectory(dir);
                bool handled = false; Exception? editorFailure = null;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += async (_, _) =>
                {
                    var editor = Application.Current.Windows.OfType<PanelEditorWindow>().FirstOrDefault();
                    if (editor == null || handled) return;
                    handled = true; timer.Stop();
                    try { await J7aEditPanel(editor, config, dir, Say); }
                    catch (Exception ex) { editorFailure = ex; Say("Panel editor failed: " + ex); editor.Close(); }
                };
                timer.Start();
                ((MenuItem)shell.FindName("MenuItemFamilyPanel")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                timer.Stop();
                if (editorFailure != null) throw editorFailure;
                J7aWaitIdle(shell, TimeSpan.FromMinutes(30));
                var panel = J7aFamilies(shell).fcf.FamilyPanel ?? throw new InvalidOperationException("No panel was applied.");
                Say("Panel applied: " + panel.Families.Count + " families; decisions " + string.Join(", ", panel.Families.GroupBy(f => f.Decision)
                    .Select(g => g.Key + " " + g.Count())) + "; subclasses " + string.Join(", ", panel.Families.Where(f => f.Subclasses.Count > 0)
                    .Select(f => f.Key + ": " + string.Join("/", f.Subclasses.Select(s => s.Key)))));
                Say("Families status: " + J7aStatus(shell));
                Timed("1 import panel, Pfam check, subclass rules");
                watch.Restart(); Save(panelProject); Timed("1b save panel project");
            }
            else Say("Resumed " + panelProject);

            // Stage 2: Families > Group by evidence with the user's link choices; subclasses follow in the same Undo step.
            string familiesProject = Stage("s2-families", "families.fqproj");
            watch.Restart();
            if (!File.Exists(familiesProject))
            {
                Open(panelProject);
                string dir = Path.GetDirectoryName(familiesProject)!; Directory.CreateDirectory(dir);
                bool handled = false; int outcome = 0;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += async (_, _) =>
                {
                    var review = Application.Current.Windows.OfType<SuperClusterPreviewWindow>().FirstOrDefault();
                    if (review == null || handled) return;
                    handled = true; timer.Stop();
                    try { outcome = await J7aReview(review, config, dir, Say); }
                    catch (Exception ex) { outcome = -1; Say("Evidence review failed: " + ex); review.Close(); }
                };
                timer.Start();
                var families = J7aFamilies(shell);
                var reviewTask = families.ReviewEvidenceAsync();
                J7aWait(reviewTask, TimeSpan.FromHours(4));
                timer.Stop();
                if (outcome != 1) { Say("Evidence review did not apply (outcome " + outcome + "); see links-proposed.json."); return 3; }
                J7aWaitIdle(shell, TimeSpan.FromHours(1));
                Say("Families status: " + J7aStatus(shell));
                var assignments = PanelAssignmentEvidenceStore.Current(families.fcf);
                Say("Panel assignments current: " + (assignments != null) + "; applied " + (families.fcf.PanelProteinAssignments?.Count(a => a.IsApplied) ?? 0)
                    + " of " + (families.fcf.PanelProteinAssignments?.Count ?? 0) + "; subclass labels assigned "
                    + (families.fcf.ProteinSubclassLabels?.Count(l => l.Status == ProteinSubclassStatus.Assigned) ?? 0) + "; subclasses current "
                    + SubclassAssignment.IsCurrent(families.fcf));
                Timed("2 families by evidence and subclasses");
                watch.Restart();
                J7aMenuExport(shell, "MenuItemExportPanelAudit", Path.Combine(dir, "panel-assignment-audit.csv"), Say);
                Save(familiesProject); Timed("2b audit export and save");
            }
            else Say("Resumed " + familiesProject);

            // Stage 3: File > Report details.
            string detailsProject = Stage("s3-details", "details.fqproj");
            watch.Restart();
            if (!File.Exists(detailsProject))
            {
                Open(familiesProject);
                MainWindow.ReportDetailsPresenterOverride = editor =>
                {
                    var model = editor.ViewModel;
                    model.Title = config.Details.Title; model.Authors = config.Details.Authors; model.SampleDescription = config.Details.SampleDescription;
                    model.Workflow = config.Details.Workflow; model.DatabaseKind = config.Details.DatabaseKind;
                    model.WrittenInterpretation = config.Details.WrittenInterpretation;
                    editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                ((MenuItem)shell.FindName("MenuItemReportDetails")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                MainWindow.ReportDetailsPresenterOverride = null;
                Say("Report details stored: " + JsonSerializer.Serialize(J7aFamilies(shell).fcf.ReportDetails));
                Save(detailsProject); Timed("3 report details");
            }
            else Say("Resumed " + detailsProject);

            // Stage 4: Quantify > Compute on the three runs with de novo and the stored family world.
            string computed = Stage("s4-compute", "computed.fqproj");
            if (!File.Exists(computed))
            {
                Open(detailsProject);
                watch.Restart(); J7aCompute(shell, config, Path.GetDirectoryName(computed)!, Say); Timed("4 compute");
                watch.Restart(); Save(computed); Timed("4b save computed project");
            }
            else Say("Resumed " + computed);

            // Stage 5: Results, Database gap, Sensitivity and every export.
            string results = Stage("s5-results", "results.fqproj");
            if (!File.Exists(results))
            {
                Open(computed);
                watch.Restart(); J7aResults(shell, Path.GetDirectoryName(results)!, Say); Timed("5 results, sensitivity and exports");
                watch.Restart(); Save(results); Timed("5b save results project");
            }
            else Say("Resumed " + results);

            // Stage 6: reopen, Compute a second time, Sensitivity again; every stored number must repeat.
            string repeat = Stage("s6-repeat", "repeat.fqproj");
            if (config.SkipRepeat) { Say("Stages 6 and 7 skipped by the configuration."); return 0; }
            if (!File.Exists(repeat))
            {
                Open(results);
                watch.Restart(); J7aCompute(shell, config, Path.GetDirectoryName(repeat)!, Say); Timed("6 second compute");
                watch.Restart(); J7aResults(shell, Path.GetDirectoryName(repeat)!, Say); Timed("6b second results and sensitivity");
                watch.Restart(); Save(repeat); Timed("6c save repeat project");
            }
            else Say("Resumed " + repeat);
            watch.Restart();
            int differences = J7aCompare(results, repeat, Path.Combine(output, "s6-repeat"), Say);
            Timed("6d compare stored numbers");

            // Stage 7: File > Export report, every section ticked, then save the project with its export choices.
            if (config.SkipReport) { Say("Stage 7 skipped by the configuration."); return differences == 0 ? 0 : 4; }
            string reportPath = Stage("s7-report", "Bothrops_jararaca_venom_report_draft.docx");
            string final = Stage("s7-report", "final.fqproj");
            if (!File.Exists(reportPath))
            {
                Open(results);
                watch.Restart();
                MainWindow.SelectReportPathOverride = () => reportPath;
                MainWindow.ReportExportPresenterOverride = editor =>
                {
                    editor.Left = -30000; editor.Top = -30000; editor.ShowActivated = false; editor.ShowInTaskbar = false;
                    editor.WindowStartupLocation = WindowStartupLocation.Manual; editor.Show(); PumpDispatcher();
                    var boxes = J7aDescendants<CheckBox>(editor).ToList();
                    foreach (var box in boxes) box.IsChecked = true;
                    Say("Report export screen: " + boxes.Count + " section and figure boxes, all ticked: " + string.Join("; ", boxes.Select(b => b.Content?.ToString())));
                    editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                ((MenuItem)shell.FindName("MenuItemExportReport")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                bool exported = shell.ReportExportTask != null && J7aWait(shell.ReportExportTask, TimeSpan.FromHours(1));
                Say("Report export: " + exported + "; " + (File.Exists(reportPath) ? new FileInfo(reportPath).Length + " bytes" : "no file")
                    + "; supplement " + Report.ReportSupplementWorkbook.PathForReport(reportPath) + " " + File.Exists(Report.ReportSupplementWorkbook.PathForReport(reportPath)));
                Timed("7 export report");
                watch.Restart(); Save(final); Timed("7b save final project");
            }
            else Say("Resumed report " + reportPath);
            Say("J7a walk finished; stored-number differences between the two Computes: " + differences);
            return differences == 0 ? 0 : 4;
        }
        catch (Exception ex) { Say("FAILED " + ex); return 1; }
        finally
        {
            MainWindow.SelectReportPathOverride = null; MainWindow.ReportExportPresenterOverride = null; MainWindow.QuantifyViewModelFactoryOverride = null;
            shell?.Close();
            Console.SetOut(previousOut); Console.SetError(previousError); SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    // The shell keeps its Families screen while another screen is shown; read the same field it uses.
    private static ClusterGenerationControl J7aFamilies(MainWindow shell) =>
        typeof(MainWindow).GetField("cgc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell)
            as ClusterGenerationControl ?? throw new InvalidOperationException("No project is open.");

    private static string J7aStatus(MainWindow shell) =>
        J7aFamilies(shell).FindName("TextBlockStatusLine") is TextBlock text ? text.Text : "";

    private static async Task J7aEditPanel(PanelEditorWindow editor, J7aConfig config, string dir, Action<string> say)
    {
        PumpDispatcher();
        editor.LoadColumns(config.Workbook, config.Sheet, config.HeaderRow);
        say("Panel editor: " + ((TextBlock)editor.FindName("ImportStatus")).Text);
        var choices = new FamilyPanelImport { FamilySheet = config.Sheet, HeaderRowNumber = config.HeaderRow, ColumnHeaders = new(config.Columns) };
        editor.PreviewImportedPanel(choices, config.Decisions);
        say("Panel editor: " + ((TextBlock)editor.FindName("ImportStatus")).Text + " " + editor.Draft.Families.Count + " families");
        if (config.PanelName.Length > 0) editor.Draft.Name = config.PanelName;
        // Save the panel as the program's spreadsheet, enter the subclass rows a person types into its Subclasses sheet, load it back.
        string template = Path.Combine(dir, "panel-template.xlsx"), edited = Path.Combine(dir, "panel-template-with-subclasses.xlsx");
        var saveButton = J7aDescendants<Button>(editor).Single(b => (b.Content as string) == "Save panel as spreadsheet");
        using (var driver = new NativeOwnedDialogDriver(template))
        {
            saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await J7aWaitAsync(() => File.Exists(template) && !AppOperationHost.For(editor).IsBusy, TimeSpan.FromMinutes(5));
            say("Panel template saved through the editor: " + template + " dialog " + driver.SawDialog);
        }
        var panel = FamilyPanelTemplate.Read(template);
        foreach (var rule in config.Subclasses)
        {
            var family = panel.Families.Single(f => f.Key == rule.Family);
            family.Subclasses.RemoveAll(s => s.Key == rule.Key);
            family.Subclasses.Add(new FamilyPanelSubclass { Key = rule.Key, DisplayName = rule.Name, RequiredPfam = rule.Required.ToList(),
                AbsentPfam = rule.Absent.ToList(), UniProtSubfamilies = rule.Subfamilies.ToList(), ReportAsSubclassKey = rule.ReportAs });
        }
        FamilyPanelTemplate.Write(edited, panel);
        editor.LoadTemplate(edited);
        say("Panel editor: " + ((TextBlock)editor.FindName("ImportStatus")).Text + " Subclass rows entered: " + config.Subclasses.Count);
        ((Button)editor.FindName("ButtonCheckPfam")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var status = (TextBlock)editor.FindName("PfamStatus");
        await J7aWaitAsync(() => !AppOperationHost.For(editor).IsBusy && status.Text.Contains("result", StringComparison.Ordinal), TimeSpan.FromMinutes(20));
        say("Pfam check: " + status.Text);
        var rows = ((IEnumerable?)((DataGrid)editor.FindName("PfamGrid")).ItemsSource)?.Cast<object>().ToList() ?? new List<object>();
        File.WriteAllText(Path.Combine(dir, "pfam-check-rows.json"), JsonSerializer.Serialize(rows, J7aJson));
        File.WriteAllText(Path.Combine(dir, "pfam-check-rows.csv"), J7aCsv(rows), new UTF8Encoding(true));
        ((Button)editor.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        say("Panel editor closed: " + (editor.Result != null) + "; " + ((TextBlock)editor.FindName("StatusText")).Text);
    }

    private static async Task<int> J7aReview(SuperClusterPreviewWindow window, J7aConfig config, string dir, Action<string> say)
    {
        PumpDispatcher();
        var model = (GroupingReviewViewModel)window.DataContext;
        // The window reads the database task itself when it opens; a person waits for it, or presses Read database and scope.
        await J7aWaitAsync(() => !model.IsBusy, TimeSpan.FromMinutes(60));
        if (!model.HasTaskScope && model.CanDiscoverTask) await model.DiscoverTaskAsync();
        await J7aWaitAsync(() => !model.IsBusy, TimeSpan.FromMinutes(60));
        say("Review: " + model.Status + " Proposed scope: " + model.ScopeText);
        if (!string.IsNullOrWhiteSpace(config.Scope)) model.ScopeText = config.Scope!;
        await model.UpdateProposalAsync();
        await J7aWaitAsync(() => !model.IsBusy, TimeSpan.FromHours(4));
        say("Review: " + model.Status + " " + model.Summary);
        var evidence = model.Evidence ?? throw new InvalidOperationException("No evidence proposal: " + model.Status);
        File.WriteAllText(Path.Combine(dir, "links-proposed.json"), JsonSerializer.Serialize(evidence.PanelLinks.Select(r => new
        { r.Source.Key, r.SourceFamily, r.Source.ArchitectureKey, r.Architecture, r.Candidates, r.State, r.SupportingAccessions, r.Source.NeedsConfirmation }), J7aJson));
        var missing = new List<string>();
        foreach (var row in evidence.PanelLinks.Where(r => r.Source.NeedsConfirmation))
        {
            var rule = config.Links.FirstOrDefault(l => l.SourceFamily == row.Source.SourceFamily && (l.Architecture == "*" || l.Architecture == row.Source.ArchitectureKey));
            if (rule == null) { missing.Add(row.SourceFamily + " | " + row.Source.ArchitectureKey + " | " + row.Candidates); continue; }
            row.SelectedOption = row.Options.Single(o => o.Key == rule.Key);
            say("Link choice: " + row.SourceFamily + " | " + row.Architecture + " -> " + (rule.Key ?? "outside the panel"));
        }
        if (missing.Count > 0)
        {
            say("Links without a user choice (" + missing.Count + "): " + string.Join("; ", missing));
            window.Close(); return 0;
        }
        if (model.CanUpdatePanelLinks) await model.UpdatePanelLinksAsync();
        await J7aWaitAsync(() => !model.IsBusy, TimeSpan.FromHours(4));
        say("Review: " + model.Status + " " + model.Summary + "; can apply " + model.CanConfirm);
        File.WriteAllText(Path.Combine(dir, "links-confirmed.json"), JsonSerializer.Serialize(model.Evidence!.PanelLinks.Select(r => new
        { r.Source.Key, r.SourceFamily, r.Source.ArchitectureKey, r.Architecture, r.Candidates, r.State, Selected = r.SelectedOption?.Key }), J7aJson));
        if (!model.CanConfirm) { window.Close(); return 2; }
        ((Button)window.FindName("createButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return 1;
    }

    private static void J7aCompute(MainWindow shell, J7aConfig config, string dir, Action<string> say)
    {
        Directory.CreateDirectory(dir);
        Func<ComputeXicRequest, IProgress<string>, CancellationToken, Task<FullXicComputeResult>>? engine = !config.UseOwnCache ? null :
            (request, progress, token) => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                request.Options.SpectrumComparisonCsvPath = Path.Combine(dir, FamilyQuant.DeNovo.SpectrumComparisonTable.DefaultFileName);
                return FullXicComputer.ComputeFullXic(request.RawDirectory, request.SeproPackage, request.DeNovoDirectory, request.Options,
                    (side, run) => Path.Combine(config.Cache, "fullxic_" + side + "_" + run + ".json"),
                    message => { token.ThrowIfCancellationRequested(); progress.Report(message); },
                    detectedCachePathFor: run => Path.Combine(config.Cache, "ms1envelope_" + run + ".json"));
            });
        MainWindow.QuantifyViewModelFactoryOverride = engine == null ? null
            : (manifest, curation) => new ComputeXicViewModel(AppVersionInfo.Current(), manifest, curation, null, engine);
        try
        {
            if (!shell.NavigationModel.RequestNavigate(ShellDestination.Quantify)) throw new InvalidOperationException("Quantify did not open.");
            PumpDispatcher();
            var quantify = J7aDescendants<QuantifyControl>(shell).Single();
            var compute = quantify.ViewModel;
            compute.RawDirectory = config.Raw; compute.SeproPackage = config.Sepr2; compute.DeNovoDirectory = config.DeNovo;
            compute.Filters.ModeTag = config.DeNovoMode; compute.Filters.PeaksMinimumAlc = config.PeaksMinimumAlc;
            say("Quantify: source " + compute.Filters.ModeTag + ", ALC " + compute.Filters.PeaksMinimumAlc + ", length " + compute.Filters.MinimumLength
                + ", stretch " + compute.Filters.StretchLength + " at " + compute.Filters.StretchFloor + ", isotopes " + compute.IsotopeCount + ", points "
                + compute.MinPointsForQuant + "; own cache " + config.UseOwnCache);
            string last = "";
            compute.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(compute.Status) && compute.Status != last) { last = compute.Status; say("Compute: " + last); } };
            var button = (Button)quantify.FindName("ButtonCompute");
            button.Command.Execute(button.CommandParameter);
            J7aWait(() => !compute.IsBusy, TimeSpan.FromHours(6));
            if (compute.Result == null) throw new InvalidOperationException("Compute did not complete: " + compute.Status);
            say("Compute run summary: " + compute.RunSummary);
            say("Compute details:" + Environment.NewLine + compute.Details);
            J7aWaitIdle(shell, TimeSpan.FromHours(2));
            var families = J7aFamilies(shell);
            J7aWait(() => families.FullXicReady.IsCompleted, TimeSpan.FromHours(2));
            SaveWorldWalkImage(shell, Path.Combine(dir, "quantify.png"));
        }
        finally { MainWindow.QuantifyViewModelFactoryOverride = null; }
    }

    private static void J7aResults(MainWindow shell, string dir, Action<string> say)
    {
        Directory.CreateDirectory(dir);
        if (!shell.NavigationModel.RequestNavigate(ShellDestination.Results)) throw new InvalidOperationException("Results did not open.");
        J7aWait(() => J7aDescendants<AnalyticsControl>(shell).Any() && !shell.Operations.IsBusy, TimeSpan.FromHours(1));
        var analytics = J7aDescendants<AnalyticsControl>(shell).Single();
        var model = analytics.ViewModel;
        foreach (var card in model.Cards) say("Results card: " + card.Title + " = " + card.Value + " (" + card.Detail + ")");
        say("Database gap: rows " + model.DatabaseGap.Rows.Count + "; " + model.DatabaseGap.Rules + " FASTA: " + model.DatabaseGap.FastaState);
        foreach (var expander in J7aDescendants<Expander>(analytics).Where(x => (x.Header as string) == "Sensitivity")) expander.IsExpanded = true;
        PumpDispatcher(); analytics.UpdateLayout(); PumpDispatcher();
        var sensitivity = J7aDescendants<FamilySensitivityControl>(analytics).FirstOrDefault();
        if (sensitivity != null)
        {
            // The section's buttons live in its own control template.
            sensitivity.ApplyTemplate();
            Button Part(string name) => (Button)sensitivity.Template.FindName(name, sensitivity);
            var calculate = Part("CalculateCurrent");
            J7aWait(() => calculate.IsEnabled || !shell.Operations.IsBusy, TimeSpan.FromMinutes(5));
            if (calculate.IsEnabled)
            {
                calculate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                J7aWait(() => !AppOperationHost.For(sensitivity).IsBusy && !shell.Operations.IsBusy, TimeSpan.FromHours(2));
            }
            say("Sensitivity: " + J7aProperty(sensitivity.ViewModel, "Status"));
            J7aButtonExport(sensitivity, Part("ExportTable"), Path.Combine(dir, "family_sensitivity.csv"), say);
        }
        else say("Sensitivity section not found in Results.");
        foreach (var kind in Enum.GetValues<ResultsExportKind>())
        {
            model.ExportKind = kind; PumpDispatcher();
            if (!model.CanExport) { say("Results export " + kind + " UNAVAILABLE: " + model.ExportUnavailableReason); continue; }
            bool folder = kind is ResultsExportKind.Figures or ResultsExportKind.EvidencePack;
            string target = Path.Combine(dir, kind switch
            {
                ResultsExportKind.Summary => "familyquant_analytics_summary.csv", ResultsExportKind.Peptides => "familyquant_analytics_peptides.csv",
                ResultsExportKind.Homology => "FamilyQuant_DeNovo_Homology.csv", ResultsExportKind.DatabaseGap => "familyquant_database_gap.csv",
                ResultsExportKind.SuggestedAdditions => "familyquant_suggested_additions.fasta", _ => kind.ToString().ToLowerInvariant()
            });
            if (!folder) J7aButtonExport(analytics, "ButtonExport", target, say);
            else { Directory.CreateDirectory(target); J7aWait(model.ExportAsync(target, AppOperationHost.For(analytics)), TimeSpan.FromMinutes(30)); }
            say("Results export " + kind + ": " + model.Status);
        }
        SaveWorldWalkImage(shell, Path.Combine(dir, "results.png"));
        J7aMenuExport(shell, "MenuItemExportComposition", Path.Combine(dir, "FamilyQuant_family_composition.csv"), say);
        J7aMenuExport(shell, "MenuItemExportPanelAudit", Path.Combine(dir, "panel-assignment-audit.csv"), say);
    }

    // Every stored number of the two computed projects: quant rows, spectrum table, manifest without its two clock fields.
    private static int J7aCompare(string first, string second, string dir, Action<string> say)
    {
        Directory.CreateDirectory(dir);
        int differences = 0;
        using var a = System.IO.Compression.ZipFile.OpenRead(first);
        using var b = System.IO.Compression.ZipFile.OpenRead(second);
        foreach (var entry in a.Entries.Select(e => e.FullName).Union(b.Entries.Select(e => e.FullName)).OrderBy(n => n, StringComparer.Ordinal))
        {
            byte[]? left = J7aEntry(a, entry), right = J7aEntry(b, entry);
            bool equal;
            if (entry == ProjectStore.ManifestEntryName && left != null && right != null)
                equal = J7aCanonicalManifest(left) == J7aCanonicalManifest(right);
            else equal = left != null && right != null && left.AsSpan().SequenceEqual(right);
            if (!equal) differences++;
            say("Repeat check " + entry + ": " + (equal ? "identical" : "DIFFERENT") + " (" + (left?.Length ?? -1) + " / " + (right?.Length ?? -1) + " bytes)");
        }
        return differences;
    }

    private static byte[]? J7aEntry(System.IO.Compression.ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry == null) return null;
        using var stream = entry.Open(); using var memory = new MemoryStream();
        if (name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        { using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress); gzip.CopyTo(memory); }
        else stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string J7aCanonicalManifest(byte[] bytes)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!.AsObject();
        foreach (string clock in new[] { nameof(QuantManifest.CreatedUtc), nameof(QuantManifest.UpdatedUtc) }) node.Remove(clock);
        return node.ToJsonString();
    }

    private static void J7aMenuExport(MainWindow shell, string menuName, string path, Action<string> say)
    {
        var menu = (MenuItem)shell.FindName(menuName);
        if (!menu.IsEnabled) { say("Export " + menuName + " UNAVAILABLE: " + menu.ToolTip); return; }
        using var driver = new NativeOwnedDialogDriver(path);
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (!J7aWaitOrGiveUp(() => driver.SawDialog || File.Exists(path), TimeSpan.FromSeconds(60)))
        {
            var families = J7aFamilies(shell);
            say("Export " + menuName + ": no file dialog appeared within 60 s; composition button enabled " + ((Button)families.FindName("ButtonExportComposition")).IsEnabled
                + " (" + ((Button)families.FindName("ButtonExportComposition")).ToolTip + "); has full XIC " + families.HasFullXic + "; status " + J7aStatus(shell));
            return;
        }
        J7aWait(() => File.Exists(path) && !shell.Operations.IsBusy || driver.TimedOut, TimeSpan.FromMinutes(20));
        say("Export " + menuName + " to " + path + ": dialog " + driver.SawDialog + ", written " + File.Exists(path)
            + (File.Exists(path) ? " (" + new FileInfo(path).Length + " bytes)" : ""));
    }

    private static void J7aButtonExport(FrameworkElement screen, string buttonName, string path, Action<string> say) =>
        J7aButtonExport(screen, (Button)screen.FindName(buttonName), path, say);

    private static void J7aButtonExport(FrameworkElement screen, Button button, string path, Action<string> say)
    {
        string buttonName = button.Name.Length > 0 ? button.Name : Convert.ToString(button.Content, CultureInfo.InvariantCulture) ?? "button";
        if (!button.IsEnabled) { say("Export " + buttonName + " UNAVAILABLE: " + button.ToolTip); return; }
        using var driver = new NativeOwnedDialogDriver(path);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!J7aWaitOrGiveUp(() => driver.SawDialog || File.Exists(path), TimeSpan.FromSeconds(60)))
        { say("Export " + buttonName + ": no file dialog appeared within 60 s."); return; }
        J7aWait(() => File.Exists(path) && !AppOperationHost.For(screen).IsBusy || driver.TimedOut, TimeSpan.FromMinutes(20));
        say("Export " + buttonName + " to " + path + ": dialog " + driver.SawDialog + ", written " + File.Exists(path)
            + (File.Exists(path) ? " (" + new FileInfo(path).Length + " bytes)" : ""));
    }

    private static string J7aCsv(IReadOnlyList<object> rows)
    {
        if (rows.Count == 0) return "";
        var properties = rows[0].GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0).ToArray();
        static string Field(object? value) => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture)?.Replace("\"", "\"\"") + "\"";
        var csv = new StringBuilder(string.Join(",", properties.Select(p => Field(p.Name))) + "\n");
        foreach (var row in rows) csv.Append(string.Join(",", properties.Select(p => Field(p.GetValue(row))))).Append('\n');
        return csv.ToString();
    }

    private static object? J7aProperty(object target, string name) => target.GetType().GetProperty(name)?.GetValue(target);

    private static IEnumerable<T> J7aDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>(); queue.Enqueue(root);
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!seen.Add(node)) continue;
            if (node is T match) yield return match;
            foreach (object child in LogicalTreeHelper.GetChildren(node)) if (child is DependencyObject d) queue.Enqueue(d);
            if (node is System.Windows.Media.Visual || node is System.Windows.Media.Media3D.Visual3D)
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++) queue.Enqueue(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
        }
    }

    private static bool J7aWait(Task<bool> task, TimeSpan timeout) { J7aWait(() => task.IsCompleted, timeout); return task.GetAwaiter().GetResult(); }
    private static void J7aWait(Task task, TimeSpan timeout) { J7aWait(() => task.IsCompleted, timeout); task.GetAwaiter().GetResult(); }

    private static void J7aWait(Func<bool> done, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > timeout) throw new TimeoutException("The J7a walk timed out waiting.");
            PumpDispatcher(); Thread.Sleep(20);
        }
        PumpDispatcher();
    }

    private static bool J7aWaitOrGiveUp(Func<bool> done, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed < timeout) { PumpDispatcher(); Thread.Sleep(20); }
        return done();
    }

    private static async Task J7aWaitAsync(Func<bool> done, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > timeout) throw new TimeoutException("The J7a walk timed out waiting.");
            await Task.Delay(50);
        }
    }

    private static void J7aWaitIdle(MainWindow shell, TimeSpan timeout) => J7aWait(() => !shell.Operations.IsBusy
        && !((typeof(MainWindow).GetField("cgc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(shell) as ClusterGenerationControl)?.Operations.IsBusy ?? false), timeout);
}
