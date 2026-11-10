using FamilyQuant.DeNovo;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void AppCommandReuseTests(string workDir)
    {
        EnsureApplication();
        Check("shared decoy predicate retains ordinal prefix and case rules", SequenceLabels.IsDecoy("Reverse_X")
            && SequenceLabels.IsDecoy("Reverse_") && !SequenceLabels.IsDecoy("reverse_X")
            && !SequenceLabels.IsDecoy("XReverse_") && !SequenceLabels.IsDecoy("") && !SequenceLabels.IsDecoy("R\u00adeverse_X"));
        string path = Path.Combine(workDir, "reuse-command-state.fcf");
        new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "command_fixture", Description = "Command fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 }
        } }.Save(path);
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldCompute = MainWindow.ComputeDialogOverride;
        var oldFactory = MainWindow.QuantifyViewModelFactoryOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(workDir, "reuse-command-settings.json");
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        MainWindow.ComputeDialogOverride = null;
        var main = new MainWindow();
        const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
        string[] names = { "MenuItemExport", "MenuItemSaveProject", "MenuItemComputeXic", "MenuItemSpectrumComparison", "MenuItemReanotateSEPro" };
        void Snapshot(string name, string expected)
        {
            PumpDispatcher();
            string actual = string.Concat(names.Select(n => ((MenuItem)main.FindName(n)).IsEnabled ? "1" : "0"));
            Console.WriteLine("HK5 commands " + name + ": " + actual);
            Check("command availability retains " + name, actual == expected, actual);
        }
        void Route(ShellDestination destination) { MainWindow.NavigateShellCommand.Execute(destination, main); PumpDispatcher(); }
        try
        {
            Snapshot("empty Start", "00000");
            ApplicationCommands.New.Execute(null, main); Snapshot("empty Build", "00000");
            Route(ShellDestination.DeNovo); Snapshot("empty De novo", "00000");
            typeof(MainWindow).GetMethod("PresentResults", any)!.Invoke(main, null); Snapshot("empty Results", "00000");
            Check("no-project Quantify route remains refused", !MainWindow.NavigateShellCommand.CanExecute(ShellDestination.Quantify, main));

            main.OpenProjectPath(path); Snapshot("Families without spectrum", "11101");
            Route(ShellDestination.Start); Snapshot("Start without spectrum", "11101");
            Route(ShellDestination.DeNovo); Snapshot("De novo without spectrum", "11000");
            Route(ShellDestination.Results); Snapshot("Results without spectrum", "11000");
            Route(ShellDestination.Families); Route(ShellDestination.Quantify); Snapshot("Quantify from Families", "11001");
            Route(ShellDestination.Results); Route(ShellDestination.Quantify); Snapshot("Quantify from Results", "11000");

            typeof(MainWindow).GetField("loadedManifest", any)!.SetValue(main, new QuantManifest
            {
                SpectrumComparisonRows = new() { new() { FileName = "fixture", ScanNumber = 1 } }
            });
            Route(ShellDestination.Families); Snapshot("Families with spectrum", "11111");
            Route(ShellDestination.Start); Snapshot("Start with spectrum", "11111");
            Route(ShellDestination.DeNovo); Snapshot("De novo with spectrum", "11010");
            Route(ShellDestination.Results); Snapshot("Results with spectrum", "11010");
            Route(ShellDestination.Quantify); Snapshot("Quantify from Results with spectrum", "11010");
            Route(ShellDestination.Families); Route(ShellDestination.Quantify); Snapshot("Quantify from Families with spectrum", "11011");

            main.OpenProjectPath(path);
            string package = Path.Combine(workDir, "reuse-commands.sepr2");
            File.WriteAllText(package, "Injected worker fixture, never parsed.");
            MainWindow.QuantifyViewModelFactoryOverride = (manifest, curation) => new ComputeXicViewModel("fixture", manifest, curation,
                compute: (_, _, _) => Task.FromResult(new FullXicComputeResult
                {
                    Rows = new(), Manifest = new(), SpectrumComparisonRows = new() { new() { FileName = "fixture", ScanNumber = 2 } }
                })) { RawDirectory = workDir, SeproPackage = package };
            Route(ShellDestination.Quantify);
            ((Grid)main.FindName("GridApp")).Children.OfType<QuantifyControl>().Single().ViewModel.ComputeAsync().GetAwaiter().GetResult();
            Snapshot("Quantify completion uses result table even without manifest rows", "11011");

            typeof(MainWindow).GetField("saveInProgress", any)!.SetValue(main, true);
            typeof(MainWindow).GetMethod("RefreshNavigationState", any)!.Invoke(main, null);
            Check("a held save still refuses project Save and navigation", !ApplicationCommands.Save.CanExecute(null, main)
                && !MainWindow.NavigateShellCommand.CanExecute(ShellDestination.Start, main));
            typeof(MainWindow).GetField("saveInProgress", any)!.SetValue(main, false);
            typeof(MainWindow).GetMethod("RefreshNavigationState", any)!.Invoke(main, null);
            Route(ShellDestination.Build); Snapshot("replacement Build clears project commands", "00000");
        }
        finally
        {
            typeof(MainWindow).GetField("saveInProgress", any)!.SetValue(main, false);
            main.Close();
            MainWindow.ShellSettingsPathOverride = oldSettings;
            MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            MainWindow.ComputeDialogOverride = oldCompute;
            MainWindow.QuantifyViewModelFactoryOverride = oldFactory;
        }
    }
}
