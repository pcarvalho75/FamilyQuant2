using FamilyQuant.Homology;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int HomologyResultScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "synthetic_homology",
            Description = "Synthetic standalone family", Sequence = "AAAPEPTIDEKAAA", UserID = 7, USearchID = 7 } } };
        var result = DeNovoControl.SearchHomology(new[] { "PEPTIDEK" }, file, new HomologyOptions(), "synthetic-standalone.fcf");
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (1024, 768), (1366, 820) })
            {
                Themes.ThemeManager.Apply(theme);
                MainWindow.ShellSettingsPathOverride = Path.Combine(output, $"preferences-{theme}-{size.Item1}.json");
                var main = new MainWindow { Width = size.Item1, Height = size.Item2, Left = -30000, Top = -30000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
                main.Show();
                try
                {
                    MainWindow.NavigateShellCommand.Execute(ShellDestination.DeNovo, main);
                    var host = UiField<Grid>(main, "GridApp")!;
                    host.Children.OfType<DeNovoControl>().Single().ApplyHomology(result);
                    MainWindow.NavigateShellCommand.Execute(ShellDestination.Results, main);
                    PumpDispatcher();
                    System.Threading.Thread.Sleep(350);
                    PumpDispatcher();
                    ScreenHarness.Save(main, Path.Combine(output, $"homology-results-{theme}-{size.Item1}.png"), size.Item1, size.Item2);
                }
                finally { main.Close(); }
            }
        }
        finally { MainWindow.ShellSettingsPathOverride = oldSettings; }
        Console.WriteLine("Standalone homology Results: four synthetic no-project/no-quant shell captures: " + output);
        return 0;
    }
}
