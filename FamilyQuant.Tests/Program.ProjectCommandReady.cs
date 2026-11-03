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
    private static IEnumerable<TestCommand> ProjectCommandReadyCommands() =>
    [new("--test-project-command-ready", 437, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-project-command-ready", (args, _) =>
        {
            ProjectCommandReadyTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.ProjectCommandReady"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ProjectCommandReadyTests(string folder)
    {
        EnsureApplication(); Directory.CreateDirectory(folder);
        var oldSettings = MainWindow.ShellSettingsPathOverride; var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldMessages = ShellMessages.Presenter; var oldCulture = CultureInfo.CurrentCulture; var oldContext = SynchronizationContext.Current;
        try
        {
            foreach (string language in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                string output = Path.Combine(folder, language); Directory.CreateDirectory(output);
                var file = new FamilyClusterFile { MyExtendedFastaItems = new()
                { new() { SequenceIdentifier = "ready_target", Sequence = "PEPTIDEK", Description = "Synthetic availability fixture", UserID = 1, USearchID = 1 } } };
                string computed = Path.Combine(output, "computed.fqproj"), empty = Path.Combine(output, "empty.fqproj");
                ProjectStore.WriteFqproj(computed, file, new[] { Qr("identified", "ready", 1, 2, "PEPTIDEK", 100, 120) },
                    new QuantManifest { PerRunTic = new() { ["ready"] = 1000 } });
                ProjectStore.WriteFqproj(empty, file, Array.Empty<QuantRow>(), new QuantManifest());
                MainWindow.ShellSettingsPathOverride = Path.Combine(output, "preferences.json");
                MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
                ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
                var main = OffscreenProofWindow(new MainWindow { Width = 1024, Height = 768 });
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
                    main.Show(); PumpDispatcher();
                    var open = main.OpenProjectSafelyAsync(computed); FinishBusyTask(open);
                    Check("J31 native computed project opens " + language, open.GetAwaiter().GetResult());
                    var screen = ((Grid)main.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
                    FinishBusyTask(screen.FullXicReady); FinishBusyOperation(main.Operations); PumpDispatcher();
                    // No menu is opened and no command is invoked between attribution and these assertions.
                    bool Enabled(string name) => ((MenuItem)main.FindName(name)).IsEnabled;
                    Check("J31 attribution ends with File available and Results attached " + language,
                        !main.Operations.IsBusy && ((Menu)main.FindName("MainMenu")).IsEnabled && screen.HasFullXic);
                    Check("J31 Export report enables after attribution without a menu detour " + language, Enabled("MenuItemExportReport"));
                    Check("J31 panel and report details enable after attribution without a menu detour " + language,
                        Enabled("MenuItemFamilyPanel") && Enabled("MenuItemReportDetails"));
                    Check("J31 File exports follow completed Results without a menu detour " + language,
                        Enabled("MenuItemExport") && Enabled("MenuItemExportComposition") && Enabled("MenuItemExportFamilyTable"));
                    using (var operation = main.Operations.Begin("Synthetic availability wait"))
                    {
                        PumpDispatcher();
                        Check("J31 a running shared operation still blocks File actions " + language,
                            main.Operations.IsBusy && !((Menu)main.FindName("MainMenu")).IsEnabled && !Enabled("MenuItemExportReport"));
                    }
                    PumpDispatcher();
                    Check("J31 another shared completion restores File actions without a menu detour " + language,
                        Enabled("MenuItemExportReport") && Enabled("MenuItemExportComposition") && Enabled("MenuItemExportFamilyTable"));
                    var noResults = main.OpenProjectSafelyAsync(empty); FinishBusyTask(noResults);
                    if (!noResults.GetAwaiter().GetResult()) throw new InvalidOperationException("Could not open the empty Results fixture.");
                    var emptyScreen = ((Grid)main.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
                    FinishBusyTask(emptyScreen.FullXicReady); FinishBusyOperation(main.Operations); PumpDispatcher();
                    Check("J31 opening without Results clears composition availability and keeps actionable report command " + language,
                        !emptyScreen.HasFullXic && !Enabled("MenuItemExportComposition") && Enabled("MenuItemExportReport") && Enabled("MenuItemExportFamilyTable"));
                }
                finally
                {
                    if (main.Operations.IsBusy) { main.Operations.Cancel(); FinishBusyOperation(main.Operations); }
                    main.Close();
                }
            }
        }
        finally
        {
            MainWindow.ShellSettingsPathOverride = oldSettings; MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            ShellMessages.Presenter = oldMessages; CultureInfo.CurrentCulture = oldCulture; SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }
}
