using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FeedbackFactCommands() =>
    [new("--test-feedback-facts", 11110, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-feedback-facts", (args, _) => FeedbackFactTests(args))];

    private static int FeedbackFactTests(string[] args)
    {
        string output = args.Length > 1 ? Path.GetFullPath(args[1])
            : Path.Combine(Path.GetTempPath(), "FamilyQuant.FeedbackFacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        string logPath = Path.Combine(output, "native-feedback.log");
        if (!NativeProofDesktop.IsPrivate) return NativeProofDesktop.Run(new[] { args[0], output }, logPath);
        using var log = new StreamWriter(logPath) { AutoFlush = true };
        var oldOut = Console.Out; var oldError = Console.Error;
        Console.SetOut(log); Console.SetError(log);
        try { return FeedbackFactWorker(output); }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
        finally { Console.SetOut(oldOut); Console.SetError(oldError); }
    }

    private static int FeedbackFactWorker(string output)
    {
        EnsureApplication();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        { if (sender is Window window) { window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = window.Top = -30000; } }), true);
        var observations = new Dictionary<string, (string Text, string Note)>(StringComparer.Ordinal);
        void Capture(string ids, string text, string note)
        {
            foreach (string id in ids.Split(' ', StringSplitOptions.RemoveEmptyEntries)) observations.Add("FQ15-" + id, (text, note));
        }
        var screen = new ClusterGenerationControl(TaskFile());
        FinishBusyTask(screen.AlignSelectedAsync());
        string alignment = UiField<TextBlock>(screen, "TextBlockStatusLine")!.Text;
        Check("Y9 normal alignment refusal names selected row count", alignment.Contains("0 row(s) are selected", StringComparison.Ordinal));
        Capture("3B5AD0429E7C", alignment, "Actual AlignSelectedAsync refusal with zero selected rows; normal visible status.");
        string locus = HeaderTooltipOf(UiField<DataGridTextColumn>(screen, "ColumnLocus")!) ?? "";
        Check("Y9 visible Locus header explains both identifier markers", locus.Contains("Reverse_", StringComparison.Ordinal)
            && locus.Contains("contaminant", StringComparison.Ordinal));
        Check("Y9 marker tooltip stays within two sentences", TooltipSentenceCount(locus) <= 2);
        Capture("76C287F5EE63", locus, "Actual Locus DataGrid header tooltip, not Help prose.");
        var recalculate = UiField<Button>(screen,"ButtonRecalculateQuantitation")!;
        string prerequisite = recalculate.ToolTip.ToString()!;
        Check("Y9 normal disabled recalculation tooltip names current load command", !recalculate.IsEnabled
            && prerequisite.Contains("Load PatternLab project",StringComparison.Ordinal));
        Capture("3980BE03A0C9",prerequisite,"Normal initial disabled Recalculate tooltip; no forced click through a disabled guard.");
        var deNovo = new DeNovoControl();
        PumpDispatcher();
        string setup = UiField<TextBlock>(deNovo, "TextBlockDeNovoStatus")!.Text;
        Check("Y9 normal de novo setup permits repeated filter changes", setup.Contains("reapply", StringComparison.OrdinalIgnoreCase));
        Capture("29D6899AC930", setup, "Initial De novo status on the normal constructed screen.");
        Check("Y9 initial per-run view explains its empty state", UiField<TextBlock>(deNovo, "TextBlockPerRunBreakdown")!.Text.Contains("No import yet", StringComparison.Ordinal));
        Check("Y9 disabled reconciliation explains the real next command", UiField<TextBlock>(deNovo, "TextBlockDeNovoLayer2State")!.Text.Contains("Load and filter", StringComparison.Ordinal));
        Check("Y9 disabled search explains the real next command", UiField<TextBlock>(deNovo, "TextBlockDeNovoLayer4State")!.Text.Contains("Load and filter", StringComparison.Ordinal));
        Capture("4548B7997209", UiField<TextBlock>(deNovo, "TextBlockPerRunBreakdown")!.Text, "Initial bound per-run TextBlock, before any source change.");
        Capture("334D1CDAA36B", UiField<TextBlock>(deNovo, "TextBlockDeNovoLayer2State")!.Text, "Visible reconciliation readiness badge while the button is disabled.");
        Capture("BF64203D3C6E", UiField<TextBlock>(deNovo, "TextBlockDeNovoLayer4State")!.Text, "Visible search readiness badge while the button is disabled.");
        var analytics = new AnalyticsViewModel(null, null) { ExportKind = ResultsExportKind.Homology };
        analytics.Export(Path.Combine(Path.GetTempPath(), "Y9-refused.csv"));
        Check("Y9 normal Results homology refusal names missing search and next route", analytics.Status.Contains("homology search", StringComparison.OrdinalIgnoreCase)
            && analytics.Status.Contains("Search", StringComparison.Ordinal) && analytics.Status.Contains("De novo", StringComparison.Ordinal));
        var results = new AnalyticsControl(analytics); LayoutAt(results, new Size(1366, 900), -1);
        var export = UiField<Button>(results, "ButtonExport")!;
        var homologyExport = ResultsExportOptions(analytics).Single(option => option.Kind == ProjectExportKind.Homology);
        Check("Y9 shared Export keeps the unavailable homology reason on its own choice", export.IsEnabled
            && export.ToolTip.ToString()!.Contains("shared Export window", StringComparison.Ordinal)
            && !homologyExport.Available
            && homologyExport.Hint.Contains("No standalone homology snapshot", StringComparison.Ordinal)
            && homologyExport.Hint.Contains("Run Search in De novo review", StringComparison.Ordinal));
        Capture("B84C0E820934 AFA02F4AA37B", homologyExport.Hint, "The shared Export window's unavailable Homology choice explains its prerequisite; Results keeps the parent Export action reachable.");
        var main = new MainWindow(); PumpDispatcher();
        string start = UiField<TextBlock>(main, "TextBlockGuide")!.Text;
        Check("Y9 normal Home status includes next action", start.Contains("Open project", StringComparison.Ordinal) && start.Contains(WorkflowNames.BuildFamilies, StringComparison.Ordinal));
        Capture("18DC9493B812", start, "Actual normal Home status-bar TextBlock with no project loaded.");
        main.Close();
        string help = HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
        Check("Y9 displayed Help restores legacy TIC recovery instructions", help.Contains("per-run totals", StringComparison.Ordinal)
            && help.Contains("Re-export", StringComparison.Ordinal) && help.Contains("XQuant", StringComparison.Ordinal));
        FeedbackFailureRoutes(output, observations);
        RestoreCompletionAndBuildFacts(output);
        PrefixDefinitionTests();
        if (failed == 0) WriteFeedbackFactRows(output, observations);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
