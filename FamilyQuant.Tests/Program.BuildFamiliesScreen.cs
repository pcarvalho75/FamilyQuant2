using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int BuildFamiliesScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        string input = Path.Combine(output, "Synthetic database.fasta");
        File.WriteAllText(input, ">synthetic_one\nPEPTIDEK\n");
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        try
        {
            foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
            {
                FamilyQuant.Themes.ThemeManager.Apply(theme);
                foreach (var size in new[] { (1024, 768), (1366, 820) })
                {
                    string folder = Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Item1 + "x" + size.Item2);
                    Directory.CreateDirectory(folder);
                    MainWindow.ShellSettingsPathOverride = Path.Combine(folder, "fixture-preferences.json");
                    var main = new MainWindow { Width = size.Item1, Height = size.Item2, Left = -30000, Top = -30000,
                        WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
                    main.Show();
                    try
                    {
                        System.Windows.Input.ApplicationCommands.New.Execute(null, main);
                        var view = ((Grid)main.FindName("GridApp")).Children.OfType<USearchLib>().Single();
                        view.ViewModel.DatabasePath = input;
                        PumpDispatcher();
                        ScreenHarness.Save(main, Path.Combine(folder, "selected-database.png"), size.Item1, size.Item2);
                        AppearanceWalk(view).OfType<Expander>().Single().IsExpanded = true;
                        PumpDispatcher();
                        System.Threading.Thread.Sleep(400);
                        PumpDispatcher();
                        ScreenHarness.Save(main, Path.Combine(folder, "preparation.png"), size.Item1, size.Item2);
                    }
                    finally { main.Close(); }
                }
            }
            Console.WriteLine("Build: 8 native selected/preparation captures, dark/light at both sizes; no clustering run: " + output);
            return 0;
        }
        finally { MainWindow.ShellSettingsPathOverride = oldSettings; }
    }

    private static void BuildFamiliesScreenTests(string workDir)
    {
        var model = new BuildFamiliesViewModel();
        Check("Build defaults preserve identity, exhaustive pass, mixing and redundancy settings", model.Identity == 85
            && model.Exhaustive && model.MaximumMixing == 0 && model.RedundancyIdentity == 100 && !model.CanRun);
        string input = Path.Combine(workDir, "build-screen-" + Guid.NewGuid().ToString("N") + ".fasta");
        File.WriteAllText(input, ">build_one\nPEPTIDEK\n>build_two\nPEPTIDER\n");
        string before = File.ReadAllText(input);
        model.DatabasePath = input;
        Check("choosing a database enables the flow without starting work", model.CanRun && !model.IsBusy);
        model.TryBegin("fixture");
        Check("one Build busy gate prevents overlapping actions", !model.CanRun && !model.TryBegin("second"));
        model.End(true);
        Check("a completed Build enables scoring", model.CanScore && model.CanRun);
        model.DatabasePath = input + ".missing";
        Check("changing the database invalidates the previous result", !model.CanRun && !model.CanScore);

        var oldSelect = USearchLib.SelectDatabasePathOverride;
        var oldOverwrite = USearchLib.ConfirmOverwriteOverride;
        var oldBuild = USearchLib.BuildOverride;
        USearchLib.SelectDatabasePathOverride = () => input;
        USearchLib.ConfirmOverwriteOverride = _ => true;
        var held = new TaskCompletionSource<FamilyClusterFile>();
        string? receivedPath = null;
        int receivedIdentity = 0;
        bool receivedExhaustive = false;
        USearchLib.BuildOverride = (path, identity, exhaustive) =>
        {
            receivedPath = path; receivedIdentity = identity; receivedExhaustive = exhaustive;
            return held.Task;
        };
        try
        {
            var view = new USearchLib();
            view.Measure(new Size(800, 700)); view.Arrange(new Rect(0, 0, 800, 700));
            PumpDispatcher();
            var choose = (Button)view.FindName("ButtonInput");
            var build = (Button)view.FindName("ButtonBuildFamilies");
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("database selection does not generate a family file", receivedPath == null && view.fcf == null && build.IsEnabled);
            build.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("Build passes unchanged input, identity and exhaustive choice to its worker", receivedPath == input
                && receivedIdentity == 85 && receivedExhaustive && view.ViewModel.IsBusy);
            Check("all long Build actions disable together while running", !choose.IsEnabled && !build.IsEnabled
                && !((Button)view.FindName("ButtonDivideAndConquer")).IsEnabled
                && !((Button)view.FindName("ButtonScanIdentities")).IsEnabled
                && !((Button)view.FindName("ButtonRemoveRedundancy")).IsEnabled);
            Check("Build parameters stay fixed while their captured operation runs",
                !((CheckBox)view.FindName("CheckBoxExhaustiveClustering")).IsEnabled
                && !((Control)view.FindName("IntegerUpDownIdentity")).IsEnabled);
            var generated = new FamilyClusterFile { ClusteringIdentity = 85, MyExtendedFastaItems = new()
            {
                new FastaItemExtended { SequenceIdentifier = "build_one", Description = "Build fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 }
            } };
            held.SetResult(generated);
            PumpDispatcher();
            string output = Path.ChangeExtension(input, ".fcf");
            Check("Build keeps its generated file and enables the existing FDR action", File.Exists(output)
                && ReferenceEquals(view.fcf, generated) && !view.ViewModel.IsBusy && ((Button)view.FindName("ButtonCalculateFDR")).IsEnabled);
            Check("Build never changes its source database", File.ReadAllText(input) == before);
            ((Button)view.FindName("ButtonCalculateFDR")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("FDR remains reported with its interpretation", view.ViewModel.Details.Contains("Mixing score:")
                && view.ViewModel.Details.Contains("unrelated real families"));
            Check("Build Help retains score limitations, scan range and preparation facts", new[]
                { "95 to 10", "Divide and Conquer", "floor", "order", "_NO_REDUNDANCY.fasta", "10,000" }.All(view.BuildHelpText().Contains));
            CheckTooltipCoverage("Build families", view);
        }
        finally
        {
            USearchLib.SelectDatabasePathOverride = oldSelect;
            USearchLib.ConfirmOverwriteOverride = oldOverwrite;
            USearchLib.BuildOverride = oldBuild;
        }
    }
}
