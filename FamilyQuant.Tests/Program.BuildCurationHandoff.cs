using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> BuildCurationHandoffCommands() =>
    [new("--test-build-curation-handoff", 100, TestCommandPhase.Raw,
        args => args[0] == "--test-build-curation-handoff", (_, _) =>
        {
            BuildCurationHandoffTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void BuildCurationHandoffTests()
    {
        EnsureApplication();
        string root = Path.Combine(Path.GetTempPath(), "build-curation-handoff", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string input = Path.Combine(root, "fixture.fasta"); File.WriteAllText(input, ">fixture\nPEPTIDEK\n");
        string before = File.ReadAllText(input);
        var oldBuild = USearchLib.BuildOverride;
        var oldMessages = ShellMessages.Presenter;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var main = OffscreenProofWindow(new MainWindow());
        try
        {
            ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            main.Show(); PumpDispatcher(); ApplicationCommands.New.Execute(null, main); PumpDispatcher();
            var grid = (Grid)main.FindName("GridApp");
            var build = grid.Children.OfType<USearchLib>().Single();
            build.ViewModel.DatabasePath = input;
            USearchLib.BuildOverride = (_, _, _) => Task.Run(() => new FamilyClusterFile { ClusteringIdentity = 85,
                MyExtendedFastaItems = new() { new() { SequenceIdentifier = "fixture", Description = "Fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 } } });
            ((Button)build.FindName("ButtonBuildFamilies")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FinishBusyOperation(main.Operations);
            var curation = grid.Children.OfType<ClusterGenerationControl>().SingleOrDefault();
            Check("SF1 successful Build opens the written file directly in Curate families", curation != null
                && main.NavigationModel.Current == ShellDestination.Families && !grid.Children.OfType<USearchLib>().Any());
            string output = Path.ChangeExtension(input, ".fcf");
            var saved = FamilyClusterFile.Load(output);
            var generated = build.fcf;
            Check("SF1 automatic open uses the saved file and preserves the source and grouping", curation != null
                && curation.fcf.ToJsonText() == saved.ToJsonText() && File.ReadAllText(input) == before);
            ApplicationCommands.New.Execute(null, main); PumpDispatcher();
            Check("SF1 returning to Build retains the last result and Check mixing action", ReferenceEquals(grid.Children.OfType<USearchLib>().Single(), build)
                && ((Button)build.FindName("ButtonCalculateFDR")).IsEnabled && generated != null && ReferenceEquals(build.fcf, generated));
            ((Button)build.FindName("ButtonCalculateFDR")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("SF1 the retained Check mixing action still runs", build.ViewModel.Details.Contains("Mixing score:", StringComparison.Ordinal));
            string previous = saved.ToJsonText();
            build.ViewModel.DatabasePath = Path.Combine(root, "failed.fasta"); File.WriteAllText(build.ViewModel.DatabasePath, ">fixture\nPEPTIDEK\n");
            USearchLib.BuildOverride = (_, _, _) => Task.FromException<FamilyClusterFile>(new IOException("Synthetic build failure"));
            ((Button)build.FindName("ButtonBuildFamilies")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FinishBusyOperation(main.Operations);
            Check("SF1 a failed Build keeps its screen and does not open or replace a family file", grid.Children.Contains(build)
                && !File.Exists(Path.ChangeExtension(build.ViewModel.DatabasePath, ".fcf")) && FamilyClusterFile.Load(output).ToJsonText() == previous);
        }
        finally
        {
            main.Close(); PumpDispatcher(); USearchLib.BuildOverride = oldBuild;
            ShellMessages.Presenter = oldMessages; MainWindow.UnsavedProjectChoiceOverride = oldChoice;
        }
    }
}
