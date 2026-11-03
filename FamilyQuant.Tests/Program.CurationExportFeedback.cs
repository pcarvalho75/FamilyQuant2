using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationExportFeedbackCommands() =>
    [new("--test-curation-export-feedback", 435, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-curation-export-feedback", (args, _) =>
        {
            string folder = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.Curation.ExportFeedback");
            CurationExportFeedbackTests(folder);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void CurationExportFeedbackTests(string folder)
    {
        EnsureApplication(); Directory.CreateDirectory(folder);
        var oldFile = ShellDialogs.FilePresenter; var oldMessages = ShellMessages.Presenter;
        var oldSettings = MainWindow.ShellSettingsPathOverride; var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldCulture = CultureInfo.CurrentCulture; var oldContext = SynchronizationContext.Current;
        string? firstStatus = null, firstBusy = null;
        try
        {
            foreach (string language in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                string output = Path.Combine(folder, language); Directory.CreateDirectory(output);
                string input = Path.Combine(output, "computed.fqproj"), csv = Path.Combine(output, "composition.csv");
                var document = new FamilyClusterFile { MyExtendedFastaItems = new()
                { new() { SequenceIdentifier = "j20_target", Sequence = "PEPTIDEK", Description = "Synthetic status fixture", UserID = 1, USearchID = 1 } } };
                var quant = new[] { Qr("identified", "feedback", 1, 2, "PEPTIDEK", 100, 120) };
                ProjectStore.WriteFqproj(input, document, quant, new QuantManifest { PerRunTic = new() { ["feedback"] = 1000 } });
                MainWindow.ShellSettingsPathOverride = Path.Combine(output, "preferences.json");
                MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
                ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
                var main = OffscreenProofWindow(new MainWindow { Width = 1024, Height = 768 });
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
                    main.Show(); PumpDispatcher();
                    bool opened = main.OpenProjectPath(input);
                    Check("J20 native computed project opens " + language, opened);
                    if (!opened) continue;
                    var host = (Grid)main.FindName("GridApp");
                    var screen = host.Children.OfType<ClusterGenerationControl>().Single();
                    FinishBusyTask(screen.FullXicReady); FinishBusyOperation(main.Operations); PumpDispatcher();
                    var status = (TextBlock)screen.FindName("TextBlockStatusLine");
                    var button = (Button)screen.FindName("ButtonExportComposition");
                    var menu = (MenuItem)main.FindName("MenuItemExportComposition");
                    Check("J20 native stored full-XIC shares are shown without PatternLab " + language,
                        screen.HasFullXic && screen.fcf.MyExtendedFastaItems.Any(item => item.FullXicShare > 0)
                        && screen.ViewModel.Quantitation.DisplayedMode == null && button.IsEnabled && menu.IsEnabled);
                    Check("J20 computed status names Full XIC without an empty/PatternLab hint " + language,
                        status.Text.Contains("Full XIC", StringComparison.Ordinal)
                        && screen.ShellStatusText.Contains("Full XIC", StringComparison.Ordinal)
                        && !status.Text.Contains("no quantitation", StringComparison.OrdinalIgnoreCase)
                        && !status.Text.Contains("Load PatternLab", StringComparison.Ordinal));
                    firstStatus ??= status.Text;
                    Check("J20 full-XIC status wording repeats across cultures " + language, firstStatus == status.Text);

                    MainWindow.NavigateShellCommand.Execute(ShellDestination.Results, main);
                    FinishBusyOperation(main.Operations); PumpDispatcher();
                    Check("J20 fixture leaves Families detached while Results is visible " + language,
                        host.Children.OfType<AnalyticsControl>().Any() && Window.GetWindow(screen) == null
                        && !screen.Operations.IsBusy && screen.HasFullXic && button.IsEnabled && menu.IsEnabled);
                    int choices = 0; Window? chooserOwner = null;
                    ShellDialogs.FilePresenter = (owner, id, dialog) =>
                    { if (id == "curation.composition-csv") { choices++; chooserOwner = owner; } return false; };
                    menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); PumpDispatcher();
                    Check("J20 idle File composition export opens a shell-owned chooser from Results " + language,
                        choices == 1 && ReferenceEquals(chooserOwner, main) && host.Children.OfType<AnalyticsControl>().Any());

                    MainWindow.NavigateShellCommand.Execute(ShellDestination.Families, main);
                    FinishBusyOperation(main.Operations); PumpDispatcher();
                    File.WriteAllText(csv, "owned previous export"); string before = screen.fcf.ToJsonText();
                    choices = 0;
                    ShellDialogs.FilePresenter = (_, id, dialog) => { choices++; dialog.FileName = csv; return true; };
                    using (var operation = screen.Operations.Begin("Owned running J20 operation"))
                    {
                        // The driver and programmatic routes can raise a Click even when the shared
                        // overlay disables physical input. The native handler must still explain it.
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                        string refusal = status.Text;
                        Check("J20 native busy composition click reports the existing refusal " + language,
                            refusal == "Wait for the current operation to finish." && screen.ShellStatusText == refusal);
                        firstBusy ??= refusal;
                        Check("J20 busy refusal wording repeats across cultures " + language, firstBusy == refusal);
                        Check("J20 busy click leaves chooser, existing output, project and operation untouched " + language,
                            choices == 0 && File.ReadAllText(csv) == "owned previous export" && before == screen.fcf.ToJsonText()
                            && screen.Operations.IsBusy && !screen.Operations.IsCancelling);
                    }
                    PumpDispatcher();
                    string expected = screen.FullXicCompositionCsv()!;
                    MainWindow.NavigateShellCommand.Execute(ShellDestination.Results, main);
                    FinishBusyOperation(main.Operations); PumpDispatcher();
                    choices = 0;
                    menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    FinishBusyOperation(main.Operations); PumpDispatcher();
                    Check("J20 idle File export from Results writes the unchanged production CSV " + language,
                        choices == 1 && File.ReadAllText(csv) == expected && !main.Operations.IsBusy
                        && host.Children.OfType<AnalyticsControl>().Any());
                    MainWindow.NavigateShellCommand.Execute(ShellDestination.Families, main);
                    FinishBusyOperation(main.Operations); PumpDispatcher();
                    screen.SetFullXicQuant(Array.Empty<QuantRow>(), null); PumpDispatcher();
                    Check("J20 empty project retains the PatternLab hint only without Full XIC " + language,
                        !screen.HasFullXic && status.Text.Contains("no quantitation", StringComparison.OrdinalIgnoreCase)
                        && status.Text.Contains("Load PatternLab", StringComparison.Ordinal));
                    string patternLab = ReportView.StatusLine(NormalizationMode.TotalChromatogram, SharedPeptidePolicy.SplitEqually, 1, 2);
                    string both = ReportView.FullXicStatusLine("the quantified signal", SharedPeptidePolicy.FamilyUnique, patternLab);
                    Check("J20 both-source status retains each source's own basis and rule " + language,
                        both.Contains("Full XIC", StringComparison.Ordinal) && both.Contains("PatternLab: ", StringComparison.Ordinal)
                        && both.Contains(NormalizationMode.TotalChromatogram.ShortName(), StringComparison.Ordinal)
                        && both.Contains(SharedPeptidePolicy.SplitEqually.ShortName(), StringComparison.Ordinal)
                        && both.Contains(SharedPeptidePolicy.FamilyUnique.ShortName(), StringComparison.Ordinal)
                        && !both.Contains("no quantitation", StringComparison.OrdinalIgnoreCase));
                }
                finally { main.Close(); PumpDispatcher(); }
            }
        }
        finally
        {
            ShellDialogs.FilePresenter = oldFile; ShellMessages.Presenter = oldMessages;
            MainWindow.ShellSettingsPathOverride = oldSettings; MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            CultureInfo.CurrentCulture = oldCulture; SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }
}
