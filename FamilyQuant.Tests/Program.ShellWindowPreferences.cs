using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ShellWindowPreferenceScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        try
        {
            foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
            {
                FamilyQuant.Themes.ThemeManager.Apply(theme);
                string folder = Path.Combine(output, theme.ToString().ToLowerInvariant());
                Directory.CreateDirectory(folder);
                string preferences = Path.Combine(folder, "fixture-preferences.json");
                new ShellSessionState { WindowWidth = 1000, WindowHeight = 650, LastFolder = folder }.Save(preferences);
                MainWindow.ShellSettingsPathOverride = preferences;
                var main = new MainWindow { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
                main.Show();
                try
                {
                    PumpDispatcher();
                    ScreenHarness.Save(main, Path.Combine(folder, "restored-size.png"), (int)main.Width, (int)main.Height);
                    Console.WriteLine(theme + " restored client size: " + main.Width + "x" + main.Height);
                }
                finally { main.Close(); }
            }
            return 0;
        }
        finally { MainWindow.ShellSettingsPathOverride = oldSettings; }
    }

    private static void ShellWindowPreferenceTests(string workDir)
    {
        Check("window dimensions reject invalid persisted values and fit the available display",
            MainWindow.FitWindowDimension(double.NaN, 1024, 900, 1200) == 1024
            && MainWindow.FitWindowDimension(5000, 1024, 900, 1200) == 1200
            && MainWindow.FitWindowDimension(1, 1024, 900, 1200) == 900);
        string settings = Path.Combine(workDir, "window-preferences.json");
        var state = new ShellSessionState { WindowWidth = 1000, WindowHeight = 650, WindowMaximized = false, LastFolder = workDir };
        Check("window and folder preferences save", state.Save(settings) == null);
        var restored = ShellSessionState.Load(settings);
        Check("window dimensions and last folder survive preference reload", restored.WindowWidth == 1000 && restored.WindowHeight == 650
            && restored.LastFolder == Path.GetFullPath(workDir));
        string first = Path.Combine(workDir, "drop-first.fcf"), second = Path.Combine(workDir, "drop-second.fcf");
        FamilyClusterFile Fixture(int id) => new() { MyExtendedFastaItems = new()
        {
            new FastaItemExtended { SequenceIdentifier = "drop_fixture", Description = "Drop fixture", Sequence = "PEPTIDEK", UserID = id, USearchID = id }
        } };
        Fixture(1).Save(first); Fixture(2).Save(second);
        Check("drop accepts one existing supported file and preserves its full path",
            MainWindow.TryGetDroppedProject(new[] { first }, out string? selected) && selected == Path.GetFullPath(first));
        Check("drop refuses multiple, missing, unsupported files and directories",
            !MainWindow.TryGetDroppedProject(new[] { first, second }, out _)
            && !MainWindow.TryGetDroppedProject(new[] { first + ".missing" }, out _)
            && !MainWindow.TryGetDroppedProject(new[] { settings }, out _)
            && !MainWindow.TryGetDroppedProject(new[] { workDir }, out _));

        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        MainWindow.ShellSettingsPathOverride = settings;
        var main = new MainWindow();
        bool Finish(Task<bool> task)
        {
            for (int i = 0; i < 100 && !task.IsCompleted; i++) PumpDispatcher();
            Check("guarded drop decision completes", task.IsCompleted);
            return task.IsCompleted && task.GetAwaiter().GetResult();
        }
        try
        {
            Check("new shell restores its persisted normal size", main.Width == MainWindow.FitWindowDimension(1000, 1024, main.MinWidth, SystemParameters.WorkArea.Width)
                && main.Height == MainWindow.FitWindowDimension(650, 768, main.MinHeight, SystemParameters.WorkArea.Height));
            var dialog = new OpenFileDialog();
            main.PrepareFileDialog(dialog);
            Check("project dialogs start in the remembered folder", dialog.InitialDirectory == Path.GetFullPath(workDir));
            Check("file drop opens through the existing project loader", Finish(main.OpenDroppedProjectAsync(new[] { first }))
                && main.NavigationModel.Document!.MyExtendedFastaItems[0].UserID == 1);
            var held = main.NavigationModel.Document!;
            held.MyExtendedFastaItems[0].UserAnnotation = "keep this edit";
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Cancel;
            Check("file drop respects the unsaved Cancel decision", !Finish(main.OpenDroppedProjectAsync(new[] { second }))
                && ReferenceEquals(held, main.NavigationModel.Document));
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            Check("file drop respects Discard and opens the requested file", Finish(main.OpenDroppedProjectAsync(new[] { second }))
                && main.NavigationModel.Document!.MyExtendedFastaItems[0].UserID == 2);
            main.Width = 1010; main.Height = 660;
            main.Close();
            var after = ShellSessionState.Load(settings);
            Check("normal close persists the final window size and file folder", after.WindowWidth == 1010 && after.WindowHeight == 660
                && after.LastFolder == Path.GetFullPath(workDir));
            var restart = new MainWindow();
            try { Check("a new shell instance restores the closed window size", restart.Width == 1010 && restart.Height == 660); }
            finally { restart.Close(); }
        }
        finally
        {
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            main.Close();
            MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            MainWindow.ShellSettingsPathOverride = oldSettings;
        }
    }
}
