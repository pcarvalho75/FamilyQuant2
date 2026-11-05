using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CloseProjectCommands() =>
    [new("--test-close-project", 10532, TestCommandPhase.Raw,
        args => args.Length == 1 && args[0] == "--test-close-project", (_, _) =>
        {
            EnsureApplication();
            CloseProjectTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void CloseProjectTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "close-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "synthetic.fcf"), saved = Path.Combine(root, "saved.fqproj");
        new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new FastaItemExtended { SequenceIdentifier = "close_fixture", Description = "Close fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 }
        } }.Save(source);
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldSave = MainWindow.SelectSaveProjectPathOverride;
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(root, "settings.json");
        var main = new MainWindow();
        bool Finish(Task<bool> task) { FinishBusyTask(task); return task.IsCompleted && task.GetAwaiter().GetResult(); }
        MenuItem CloseMenu() => (MenuItem)main.FindName("MenuItemCloseProject");
        try
        {
            Check("Z3a Close project is disabled without a project", !CloseMenu().IsEnabled && !Finish(main.CloseProjectAsync()));
            main.OpenProjectPath(source);
            var held = main.NavigationModel.Document!;
            held.MyExtendedFastaItems[0].UserAnnotation = "unsaved edit";
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Cancel;
            Check("Z3a Cancel keeps the edited project open", CloseMenu().IsEnabled && !Finish(main.CloseProjectAsync())
                && ReferenceEquals(held, main.NavigationModel.Document));
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Save;
            MainWindow.SelectSaveProjectPathOverride = () => null;
            Check("Z3a cancelling Save picker keeps the project", !Finish(main.CloseProjectAsync()) && ReferenceEquals(held, main.NavigationModel.Document));
            string blocker = Path.Combine(root, "blocked"); File.WriteAllText(blocker, "not a directory");
            MainWindow.SelectSaveProjectPathOverride = () => Path.Combine(blocker, "save.fqproj");
            Check("Z3a failed Save keeps the edited project", !Finish(main.CloseProjectAsync()) && ReferenceEquals(held, main.NavigationModel.Document));
            MainWindow.SelectSaveProjectPathOverride = () => saved;
            Check("Z3a Save writes edits then closes to Home", Finish(main.CloseProjectAsync()) && File.Exists(saved)
                && ProjectStore.ReadFqproj(saved).fcf.MyExtendedFastaItems[0].UserAnnotation == "unsaved edit"
                && main.NavigationModel.Document == null && main.NavigationModel.Current == ShellDestination.Start);
            Check("Z3a closed project clears dirty state and dependent actions", !main.DocumentState.IsModified && !CloseMenu().IsEnabled
                && !((MenuItem)main.FindName("MenuItemSaveProject")).IsEnabled
                && !main.NavigationModel.CanNavigate(ShellDestination.Quantify) && !main.NavigationModel.CanNavigate(ShellDestination.Results)
                && main.NavigationModel.Items.Where(item => !item.IsEnabled).All(item => item.StateText == "Open a project first"));
            Check("Z3a another project opens without restarting", main.OpenProjectPath(source) && main.NavigationModel.Document != null);
            using (main.Operations.Begin("Synthetic close guard"))
                Check("Z3a busy operation prevents Close project", !Finish(main.CloseProjectAsync()) && main.NavigationModel.Document != null && !CloseMenu().IsEnabled);
            main.NavigationModel.Document!.MyExtendedFastaItems[0].UserAnnotation = "discard this";
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            Check("Z3a Discard closes without writing the source", Finish(main.CloseProjectAsync())
                && FamilyClusterFile.Load(source).MyExtendedFastaItems[0].UserAnnotation == "" && main.NavigationModel.Document == null);
            Check("Z3a existing saved project reopens after discard", main.OpenProjectPath(saved)
                && main.NavigationModel.Document!.MyExtendedFastaItems[0].UserAnnotation == "unsaved edit");
            MainWindow.UnsavedProjectChoiceOverride = () => throw new InvalidOperationException("A clean close must not prompt.");
            CloseMenu().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check("Z3a File menu closes a clean project without prompting", main.NavigationModel.Document == null
                && main.NavigationModel.Current == ShellDestination.Start);
        }
        finally
        {
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            main.Close();
            MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            MainWindow.SelectSaveProjectPathOverride = oldSave;
            MainWindow.ShellSettingsPathOverride = oldSettings;
        }
    }
}
