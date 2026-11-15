using FamilyQuant.DeNovo;
using FamilyQuant.Panels;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SaveFamiliesCommands() =>
    [new("--test-save-families", 100, TestCommandPhase.Raw, args => args.Length is 1 or 2 && args[0] == "--test-save-families", (args, _) =>
    {
        SaveFamiliesTests(args.Length == 2 ? args[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "fq2", "FQDeNovo", "FS1", Guid.NewGuid().ToString("N")));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void SaveFamiliesTests(string output)
    {
        Section("FS1 separate curated families and results files");
        EnsureApplication(); Directory.CreateDirectory(output);
        var file = ManualChoiceFile();
        file.LockedUserIds.Add(1);
        file.DomainSource = "Synthetic stored evidence";
        file.EvidenceSource = new() { Provider = "UniProt", Release = "synthetic-release" };
        file.MyExtendedFastaItems[0].Evidence!.Pfam = new() { new() { Accession = "PF12345", Name = "Example domain" } };
        file.FamilyPanel = new FamilyPanel { Name = "Synthetic list", Families = new()
            { new() { Key = "one", DisplayName = "Curated family", Pfam = new() { new() { Accession = "PF12345", Name = "Example domain" } } } } };
        for (int i = 0; i < file.MyExtendedFastaItems.Count; i++)
        { file.MyExtendedFastaItems[i].XICUser = .25 * (i + 1); file.MyExtendedFastaItems[i].XIC = 100 + i; file.MyExtendedFastaItems[i].FullXicShare = .5; }
        file.ReportDetails = new() { Title = "Synthetic analysis", SampleDescription = "Run details" };
        file.ReportInputProvenance = new() { SeproFileName = "synthetic-identifications.sepr2" };
        string input = Path.Combine(output, "results.fqproj");
        var quant = new[] { Qr("identified", "synthetic-run", 1, 2, "ACDEFGHIK", 100, 120) };
        var manifest = new QuantManifest { DerivedMethod = NativeQuantFractions.Method, PerRunTic = new() { ["synthetic-run"] = 1000 } };
        ProjectStore.WriteFqproj(input, file, quant, manifest);
        string compatible = Path.Combine(output, "legacy-write.fcf"); file.Save(compatible);
        Check("FS1 default curation writer retains compatibility readings for existing callers",
            FamilyClusterFile.Load(compatible).MyExtendedFastaItems[0].XICUser == .25);
        var presenter = ShellDialogs.FilePresenter; var messages = ShellMessages.Presenter;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride; var oldSave = MainWindow.SelectSaveProjectPathOverride;
        var oldContext = SynchronizationContext.Current;
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        var main = OffscreenProofWindow(new MainWindow { Width = 1366, Height = 820 });
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
            main.Show(); PumpDispatcher();
            Check("FS1 families shortcut is unavailable without a project", main.FindName("MenuItemSaveFamiliesAs") is not MenuItem { IsEnabled: true });
            main.OpenProjectPath(input); WaitForReportFixture(main); FinishBusyOperation(main.Operations); PumpDispatcher();
            var control = WalkTree(main).OfType<ClusterGenerationControl>().Single();
            var live = control.fcf;
            live.MyExtendedFastaItems[0].UserAnnotation = "Edited family";
            main.DocumentState.CheckForChanges(live);
            string before = live.ToJsonText();
            byte[] initial = File.ReadAllBytes(input);
            var entry = main.FindName("MenuItemSaveFamiliesAs") as MenuItem;
            Check("FS1 File exposes Save families as without replacing Export", entry?.Header as string == "Save families as (.fcf)..."
                && entry.IsEnabled && main.FindName("MenuItemExport") is MenuItem);
            foreach (string route in new[] { "export", "menu" })
            {
                if (route == "menu" && entry == null) continue;
                string target = Path.Combine(output, route + ".fcf");
                ShellDialogs.FilePresenter = (_, id, dialog) => { if (id != "families.export") throw new InvalidOperationException(id); dialog.FileName = target; return true; };
                if (route == "export") ChooseExport(main, ProjectExportKind.Families);
                else entry!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                FinishBusyOperation(main.Operations); PumpDispatcher();
                var loaded = FamilyClusterFile.Load(target);
                Check("FS1 " + route + " restores grouping, annotations, evidence, list and locks",
                    loaded.MyExtendedFastaItems.Select(p => (p.UserID, p.USearchID, p.UserAnnotation)).SequenceEqual(live.MyExtendedFastaItems.Select(p => (p.UserID, p.USearchID, p.UserAnnotation)))
                    && JsonSerializer.Serialize(loaded.MyExtendedFastaItems.Select(p => p.Evidence)) == JsonSerializer.Serialize(live.MyExtendedFastaItems.Select(p => p.Evidence))
                    && JsonSerializer.Serialize(loaded.FamilyPanel) == JsonSerializer.Serialize(live.FamilyPanel)
                    && JsonSerializer.Serialize(loaded.EvidenceSource) == JsonSerializer.Serialize(live.EvidenceSource)
                    && loaded.LockedUserIds.SetEquals(live.LockedUserIds) && loaded.DomainSource == live.DomainSource);
                string stored = File.ReadAllText(target);
                Check("FS1 " + route + " holds no quantitation or analysis-only report facts",
                    loaded.MyExtendedFastaItems.All(p => p.XICUser == 0 && p.XIC == 0 && p.FullXicShare == null)
                    && loaded.ReportDetails == null && loaded.ReportInputProvenance == null
                    && !stored.Contains("XICUser", StringComparison.Ordinal) && !stored.Contains("synthetic-run", StringComparison.Ordinal)
                    && live.LastSaveCheck?.Checks.Any(check => check.Code == "families-only" && check.Passed) == true);
                Check("FS1 " + route + " preserves the open results data, dirty state and Save path",
                    live.ToJsonText() == before && main.DocumentState.IsModified && main.DocumentState.ProjectPath == Path.GetFullPath(input)
                    && initial.SequenceEqual(File.ReadAllBytes(input)));
            }
            if (entry != null)
            {
                int shown = 0;
                ShellDialogs.FilePresenter = (_, _, _) => { shown++; return false; };
                using (main.Operations.Begin("Synthetic busy save guard"))
                {
                    PumpDispatcher(); entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check("FS1 busy families shortcut refuses before opening a file dialog", !entry.IsEnabled && shown == 0);
                }
                string cancelled = Path.Combine(output, "cancelled.fcf"); File.WriteAllText(cancelled, "keep this file");
                ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = cancelled; return false; };
                entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); PumpDispatcher();
                Check("FS1 cancelling leaves both files and the results path intact", File.ReadAllText(cancelled) == "keep this file"
                    && initial.SequenceEqual(File.ReadAllBytes(input)) && main.DocumentState.ProjectPath == Path.GetFullPath(input));
                ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = input; return true; };
                entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); FinishBusyOperation(main.Operations); PumpDispatcher();
                Check("FS1 families output refuses the open results extension", initial.SequenceEqual(File.ReadAllBytes(input)));
                var menu = (MenuItem)main.FindName("MenuFile"); menu.IsSubmenuOpen = true; PumpDispatcher();
                var popup = (Popup)menu.Template.FindName("PART_Popup", menu);
                var content = (FrameworkElement)popup.Child;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output, "file-menu.png"))) encoder.Save(stream);
                menu.IsSubmenuOpen = false;
            }
            MainWindow.SelectSaveProjectPathOverride = () => throw new InvalidOperationException("Save must keep its results path.");
            var save = (Task)typeof(MainWindow).GetMethod("SaveProjectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, new object[] { false })!;
            FinishBusyTask(save); save.GetAwaiter().GetResult();
            var reopened = ProjectStore.ReadFqproj(input);
            Check("FS1 ordinary Save keeps results quantitation and report facts in the same fqproj",
                reopened.quant.Count == quant.Length && reopened.manifest.PerRunTic["synthetic-run"] == 1000
                && reopened.fcf.MyExtendedFastaItems[0].XICUser == .25 && reopened.fcf.ReportDetails?.Title == "Synthetic analysis"
                && reopened.fcf.ReportInputProvenance?.SeproFileName == "synthetic-identifications.sepr2"
                && reopened.fcf.MyExtendedFastaItems[0].UserAnnotation == "Edited family" && !main.DocumentState.IsModified);
        }
        finally
        {
            ShellDialogs.FilePresenter = presenter; ShellMessages.Presenter = messages;
            MainWindow.UnsavedProjectChoiceOverride = oldChoice; MainWindow.SelectSaveProjectPathOverride = oldSave;
            SynchronizationContext.SetSynchronizationContext(oldContext); main.Close();
        }
        Console.WriteLine("FS1 proof root: " + output);
    }
}
