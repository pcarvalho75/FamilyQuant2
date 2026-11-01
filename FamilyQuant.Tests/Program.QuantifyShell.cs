using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int QuantifyShellScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        string input = Path.Combine(output, "synthetic-shell.fcf");
        new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "synthetic", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 } } }.Save(input);
        string package = Path.Combine(output, "synthetic-shell.sepr2");
        File.WriteAllText(package, "Synthetic UI fixture; never parsed.");
        var oldFactory = MainWindow.QuantifyViewModelFactoryOverride;
        var oldDialog = MainWindow.ComputeDialogOverride;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        try
        {
            MainWindow.ComputeDialogOverride = null;
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            MainWindow.QuantifyViewModelFactoryOverride = (manifest, curation) => new ComputeXicViewModel("synthetic fixture", manifest, curation,
                compute: (_, _, _) => Task.FromResult(new FullXicComputeResult { Rows = new(), Manifest = new(),
                    Notes = new() { "Synthetic fixture. No RAW file was read." } })) { RawDirectory = output, SeproPackage = package };
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (1024, 768), (1366, 820) })
            {
                Themes.ThemeManager.Apply(theme);
                MainWindow.ShellSettingsPathOverride = Path.Combine(output, $"preferences-{theme}-{size.Item1}.json");
                var main = new MainWindow { Width = size.Item1, Height = size.Item2, Left = -30000, Top = -30000,
                    ShowInTaskbar = false, ShowActivated = false, WindowStyle = System.Windows.WindowStyle.None,
                    ResizeMode = System.Windows.ResizeMode.NoResize };
                main.Show();
                try
                {
                    main.OpenProjectPath(input);
                    MainWindow.NavigateShellCommand.Execute(ShellDestination.Quantify, main);
                    PumpDispatcher();
                    ScreenHarness.Save(main, Path.Combine(output, $"shell-{theme}-{size.Item1}-inputs.png"), size.Item1, size.Item2);
                    ((Grid)main.FindName("GridApp")).Children.OfType<QuantifyControl>().Single().ViewModel.ComputeAsync().GetAwaiter().GetResult();
                    PumpDispatcher();
                    ScreenHarness.Save(main, Path.Combine(output, $"shell-{theme}-{size.Item1}-complete.png"), size.Item1, size.Item2);
                }
                finally { main.Close(); }
            }
            Console.WriteLine("Quantify shell: 8 native captures, both themes/sizes; injected worker, no RAW computation: " + output);
            return 0;
        }
        finally
        {
            MainWindow.QuantifyViewModelFactoryOverride = oldFactory;
            MainWindow.ComputeDialogOverride = oldDialog;
            MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            MainWindow.ShellSettingsPathOverride = oldSettings;
        }
    }

    private static void QuantifyShellTests(string workDir)
    {
        string first = Path.Combine(workDir, "quantify-shell-first.fcf"), second = Path.Combine(workDir, "quantify-shell-second.fcf");
        FamilyClusterFile Fixture(int id) => new() { MyExtendedFastaItems = new()
        {
            new FastaItemExtended { SequenceIdentifier = "quantify_shell", Description = "Quantify shell fixture", Sequence = "PEPTIDEK", UserID = id, USearchID = id }
        } };
        Fixture(1).Save(first); Fixture(2).Save(second);
        string package = Path.Combine(workDir, "quantify-shell.sepr2");
        File.WriteAllText(package, "Synthetic input marker; injected worker does not parse this file.");
        var oldFactory = MainWindow.QuantifyViewModelFactoryOverride;
        var oldDialog = MainWindow.ComputeDialogOverride;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(workDir, "quantify-shell-preferences.json");
        MainWindow.ComputeDialogOverride = null;
        var main = new MainWindow();
        try
        {
            main.OpenProjectPath(first);
            var originalCuration = main.NavigationModel.Document;
            var completed = new FullXicComputeResult { Rows = new(), Manifest = new() { PerRunTic = new() { ["fixture_run"] = 1 } },
                Notes = new() { "Synthetic shell completion detail" } };
            MainWindow.QuantifyViewModelFactoryOverride = (manifest, curation) => new ComputeXicViewModel(AppVersionInfo.Current(), manifest, curation,
                compute: (_, _, _) => Task.FromResult(completed)) { RawDirectory = workDir, SeproPackage = package };
            MainWindow.NavigateShellCommand.Execute(ShellDestination.Quantify, main);
            var host = (Grid)main.FindName("GridApp");
            var control = host.Children.OfType<QuantifyControl>().Single();
            Check("Quantify is hosted in the shell rather than a modal window", main.NavigationModel.Current == ShellDestination.Quantify
                && ReferenceEquals(originalCuration, main.NavigationModel.Document));
            control.ViewModel.ComputeAsync().GetAwaiter().GetResult();
            Check("successful Quantify attaches to the same open project and marks it unsaved", main.DocumentState.IsModified
                && main.NavigationModel.HasStoredResults == false && ReferenceEquals(originalCuration, main.NavigationModel.Document)
                && main.StatusModel.ContextHelp.Contains("Synthetic shell completion detail"));
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            main.OpenProjectPath(first);
            var held = new TaskCompletionSource<FullXicComputeResult>();
            MainWindow.QuantifyViewModelFactoryOverride = (manifest, curation) => new ComputeXicViewModel(AppVersionInfo.Current(), manifest, curation,
                compute: (_, _, _) => held.Task) { RawDirectory = workDir, SeproPackage = package };
            MainWindow.NavigateShellCommand.Execute(ShellDestination.Quantify, main);
            var waiting = host.Children.OfType<QuantifyControl>().Single();
            Task running = waiting.ViewModel.ComputeAsync();
            Check("running Quantify blocks new navigation and replacement", main.NavigationModel.IsBusy
                && !MainWindow.NavigateShellCommand.CanExecute(ShellDestination.Start, main)
                && !System.Windows.Input.ApplicationCommands.Open.CanExecute(null, main));
            main.OpenProjectPath(second); // raw compatibility seam simulates an external project replacement
            var replacement = main.NavigationModel.Document;
            held.SetResult(completed);
            running.GetAwaiter().GetResult();
            Check("a stale Quantify completion cannot overwrite the replacement project", ReferenceEquals(replacement, main.NavigationModel.Document)
                && replacement!.MyExtendedFastaItems[0].UserID == 2 && !main.DocumentState.IsModified);
        }
        finally
        {
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            main.Close();
            MainWindow.QuantifyViewModelFactoryOverride = oldFactory;
            MainWindow.ComputeDialogOverride = oldDialog;
            MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            MainWindow.ShellSettingsPathOverride = oldSettings;
        }
    }
}
