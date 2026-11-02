using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ShellSaveScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        string source = Path.Combine(output, "Synthetic save fixture.fcf");
        new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new FastaItemExtended { SequenceIdentifier = "save_render_fixture", Description = "Synthetic save fixture",
                Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 }
        } }.Save(source);
        var oldSave = MainWindow.SelectSaveProjectPathOverride;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        try
        {
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
            {
                FamilyQuant.Themes.ThemeManager.Apply(theme);
                foreach (var size in new[] { (1024, 768), (1366, 820) })
                {
                    string folder = Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Item1 + "x" + size.Item2);
                    Directory.CreateDirectory(folder);
                    MainWindow.ShellSettingsPathOverride = Path.Combine(folder, "fixture-preferences.json");
                    MainWindow.SelectSaveProjectPathOverride = () => Path.Combine(folder, "Synthetic saved project.fqproj");
                    var main = new MainWindow { Width = size.Item1, Height = size.Item2, Left = -30000, Top = -30000,
                        WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
                    main.Show();
                    try
                    {
                        main.OpenProjectPath(source);
                        main.NavigationModel.Document!.MyExtendedFastaItems[0].UserAnnotation = "Unsaved fixture annotation";
                        main.DocumentState.CheckForChanges(main.NavigationModel.Document);
                        PumpDispatcher();
                        ScreenHarness.Save(main, Path.Combine(folder, "unsaved.png"), size.Item1, size.Item2);
                        var task = (Task)typeof(MainWindow).GetMethod("SaveProjectAsync",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(main, new object[] { false })!;
                        FinishBusyTask(task);
                        if (!task.IsCompleted) throw new InvalidOperationException("Fixture save did not finish.");
                        task.GetAwaiter().GetResult();
                        PumpDispatcher();
                        ScreenHarness.Save(main, Path.Combine(folder, "saved-checked.png"), size.Item1, size.Item2);
                    }
                    finally { main.Close(); }
                }
            }
            Console.WriteLine("Save UI: 8 native captures, unsaved and saved/checked, both themes and sizes: " + output);
            return 0;
        }
        finally
        {
            MainWindow.SelectSaveProjectPathOverride = oldSave;
            MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            MainWindow.ShellSettingsPathOverride = oldSettings;
        }
    }

    private static int ShellSaveSmoke(string input, string output)
    {
        string source = Path.GetFullPath(input), target = Path.GetFullPath(output);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Keep the source and project output separate.");
        string InputHash() => TestFileHash.Upper(source);
        string before = InputHash();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var curation = FamilyClusterFile.Load(source);
        string expected = curation.ToJsonText();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string check = ShellProjectOperations.WriteVerifiedProject(target, curation, new List<QuantRow>(),
            ShellProjectOperations.CreateManifest(AppVersionInfo.Current()));
        var reopened = ProjectStore.ReadFqproj(target);
        Check("real FCF migration preserves curation bytes and empty quant", reopened.fcf.ToJsonText() == expected && reopened.quant.Count == 0);
        Check("real FCF migration leaves the input byte-identical", before == InputHash());
        Console.WriteLine(check);
        Console.WriteLine("Input bytes: " + new FileInfo(source).Length + "; project bytes: " + new FileInfo(target).Length);
        Console.WriteLine("Elapsed seconds: " + watch.Elapsed.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static void ShellSavingTests(string workDir)
    {
        FamilyClusterFile Fixture(int id) => new() { MyExtendedFastaItems = new()
        {
            new FastaItemExtended { SequenceIdentifier = "saving_fixture", Description = "Save fixture", Sequence = "PEPTIDEK", UserID = id, USearchID = id }
        } };
        var curation = Fixture(1);
        var state = new ShellDocumentState();
        state.Open(curation, null);
        curation.MyExtendedFastaItems[0].UserAnnotation = "edited";
        Check("persisted curation fallback detects edits without a notification", state.CheckForChanges(curation));
        curation.MyExtendedFastaItems[0].UserAnnotation = "";
        state.MarkCurationChanged();
        Check("undoing curation to the saved bytes clears its unsaved state", !state.CheckForChanges(curation));
        state.MarkModified();
        Check("new quantitation remains unsaved even when curation is unchanged", state.CheckForChanges(curation));

        string directory = Path.Combine(workDir, "verified-save");
        Directory.CreateDirectory(directory);
        string saved = Path.Combine(directory, "saved.fqproj");
        var manifest = ShellProjectOperations.CreateManifest(AppVersionInfo.Current());
        string check = ShellProjectOperations.WriteVerifiedProject(saved, curation, new List<QuantRow>(), manifest);
        var read = ProjectStore.ReadFqproj(saved);
        Check("curation-only project round trip keeps exact serialized curation and no quantitation",
            read.fcf.ToJsonText() == curation.ToJsonText() && read.quant.Count == 0 && check.Contains("passed"));
        byte[] original = File.ReadAllBytes(saved);
        bool refused = false;
        try
        {
            ShellProjectOperations.WriteVerifiedProject(saved, curation, new List<QuantRow>(), manifest,
                staged => File.WriteAllText(staged, "corrupted fixture"));
        }
        catch (InvalidDataException) { refused = true; }
        Check("corrupt staged output cannot overwrite the previous project", refused && original.SequenceEqual(File.ReadAllBytes(saved)));
        Check("verified save removes its own failed staging files", Directory.GetFiles(directory, "*.saving-*").Length == 0);
        state.Saved(curation, saved, check);
        Check("verified Save clears the unsaved marker and records the check", !state.IsModified && state.ProjectPath == saved && state.SaveCheckText == check);
        string legacyExport = Path.Combine(directory, "legacy.fcf");
        curation.Save(legacyExport);
        byte[] legacyBefore = File.ReadAllBytes(legacyExport);
        bool wrongExtension = false;
        try { ShellProjectOperations.WriteVerifiedProject(legacyExport, curation, new List<QuantRow>(), manifest); }
        catch (ArgumentException) { wrongExtension = true; }
        Check("canonical project save cannot overwrite an FCF with a project container", wrongExtension
            && legacyBefore.SequenceEqual(File.ReadAllBytes(legacyExport)));

        string first = Path.Combine(workDir, "unsaved-first.fcf"), second = Path.Combine(workDir, "unsaved-second.fcf");
        Fixture(1).Save(first);
        Fixture(2).Save(second);
        string migrated = Path.Combine(workDir, "unsaved-first.fqproj");
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldSave = MainWindow.SelectSaveProjectPathOverride;
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(workDir, "unsaved-settings.json");
        var main = new MainWindow();
        bool Finish(Task<bool> task)
        {
            FinishBusyTask(task);
            Check("unsaved decision completes for the fixture", task.IsCompleted);
            return task.IsCompleted && task.GetAwaiter().GetResult();
        }
        try
        {
            Check("legacy FCF still opens through the canonical project shell", main.OpenProjectPath(first));
            var held = main.NavigationModel.Document!;
            held.MyExtendedFastaItems[0].UserAnnotation = "unsaved note";
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Cancel;
            Check("Cancel keeps the current edited document", !Finish(main.OpenProjectSafelyAsync(second))
                && ReferenceEquals(held, main.NavigationModel.Document) && held.MyExtendedFastaItems[0].UserAnnotation == "unsaved note");
            Check("unsaved state is visible in the window title", main.Title.EndsWith(" *", StringComparison.Ordinal));
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Save;
            MainWindow.SelectSaveProjectPathOverride = () => null;
            Check("cancelling the save picker also cancels replacement", !Finish(main.OpenProjectSafelyAsync(second))
                && ReferenceEquals(held, main.NavigationModel.Document));
            MainWindow.SelectSaveProjectPathOverride = () => migrated;
            Check("Save before replacement writes the edits to a project then opens the requested file", Finish(main.OpenProjectSafelyAsync(second))
                && ProjectStore.ReadFqproj(migrated).fcf.MyExtendedFastaItems[0].UserAnnotation == "unsaved note"
                && main.NavigationModel.Document!.MyExtendedFastaItems[0].UserID == 2);
            main.NavigationModel.Document!.MyExtendedFastaItems[0].UserAnnotation = "discard this";
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            Check("Discard opens the requested file without writing the old one", Finish(main.OpenProjectSafelyAsync(first))
                && FamilyClusterFile.Load(second).MyExtendedFastaItems[0].UserAnnotation == "");
            main.NavigationModel.Document!.MyExtendedFastaItems[0].UserAnnotation = "close decision";
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Cancel;
            bool closed = false;
            main.Closed += (_, _) => closed = true;
            main.Close();
            Check("Cancel in the close prompt leaves the window open", !closed);
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            main.Close();
            Check("Discard in the close prompt allows closing", closed);
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
