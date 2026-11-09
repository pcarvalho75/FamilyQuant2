using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FamilyQuant.DeNovo;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ShellDialogTests(string workDir)
    {
        EnsureApplication();
        Section("Shell messages and remembered browse folders");
        Check("one shared service owns native file and folder dialogs", typeof(MainWindow).Assembly.GetType("FamilyQuant.ShellDialogs") != null);
        string root = FindLedgerRoot()!;
        Check("shell document messages all use the headless-safe message path", !File.ReadAllText(Path.Combine(root,
            "FamilyQuant/MainWindow.Documents.cs")).Contains("MessageBox.Show", StringComparison.Ordinal));
        string first = Path.Combine(workDir, "folder-first"), second = Path.Combine(workDir, "folder-second");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        var oldFile = ShellDialogs.FilePresenter;
        var oldFolder = ShellDialogs.FolderPresenter;
        var oldMessage = ShellMessages.Presenter;
        var oldOpen = MainWindow.SelectOpenPathOverride;
        var oldSave = MainWindow.SelectSaveProjectPathOverride;
        var oldSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(workDir, "dialog-preferences.json");
        MainWindow.SelectOpenPathOverride = MainWindow.SelectSaveProjectPathOverride = null;
        int nativeMessages = 0, nativeBrowses = 0;
        var messages = new List<ShellMessage>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        Action<ShellMessage> observe = messages.Add;
        ShellMessages.Reported += observe;
        var main = new MainWindow { Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
        const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        void Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, any)!.Invoke(target, args);
        void StaticCall(Type target, string method, params object?[] args) => target.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        void Host(UIElement view)
        {
            var grid = (Grid)main.FindName("GridApp");
            grid.Children.Clear(); grid.Children.Add(view);
            if (main.IsLoaded) PumpDispatcher();
        }
        ShellDialogs.FilePresenter = (_, id, dialog) => { nativeBrowses++; seen[id] = dialog.InitialDirectory; return false; };
        ShellDialogs.FolderPresenter = (_, id, dialog) => { nativeBrowses++; seen[id] = dialog.InitialDirectory; return false; };
        ShellMessages.Presenter = (_, _) => { nativeMessages++; return MessageBoxResult.OK; };
        try
        {
            main.RememberFolder(first);
            var noFile = new OpenFileDialog();
            var noFolder = new OpenFolderDialog();
            Check("headless file and folder requests never reach a native presenter", !ShellDialogs.ShowFile(main, "headless.file", noFile)
                && !ShellDialogs.ShowFolder(main, "headless.folder", noFolder) && nativeBrowses == 0);
            string fullMessage = "A refusal remains visible.\nIts complete reason and next step stay in Help.";
            Check("headless questions choose the safe refusal while preserving their complete text", ShellMessages.Report(main, fullMessage,
                "Question preserved", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No
                && nativeMessages == 0 && main.StatusModel.ContextHelp.Contains(fullMessage) && messages.Last().Title == "Question preserved");
            Call(main, "MenuItemSaveFCF_Click", main, new RoutedEventArgs());
            Call(main, "MenuItemReanotateSEPro_Click", main, new RoutedEventArgs());
            Call(main, "MenuItemSaveProject_Click", main, new RoutedEventArgs());
            Check("headless Save and SEPro refusals retain their text without a dialog", nativeMessages == 0
                && messages.Any(m => m.Text.Contains("no families on screen"))
                && messages.Any(m => m.Text.Contains("no project on screen"))
                && messages.Any(m => m.Title == "Load the families first"));
            var locks = new LockManagerWindow(new FamilyClusterFile());
            locks.ShowFor(main);
            Check("headless lock management never opens a modal window", !locks.IsVisible && nativeMessages == 0);
            locks.Close();

            string file = Path.Combine(workDir, "dialog-curation.fcf");
            var curation = new FamilyClusterFile { MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "synthetic", Description = "Synthetic browse fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 }
            } };
            curation.Save(file);
            void ExerciseBrowses()
            {
                Call(main, "ChooseOpenProjectPath");
                Call(main, "ChooseSaveProjectPath", true);
                var build = new USearchLib(); Host(build);
                ((Button)build.FindName("ButtonInput")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var quantify = new QuantifyControl(); Host(quantify);
                foreach (string method in new[] { "BrowseRaw", "BrowseSepro", "BrowseDeNovo" }) Call(quantify, method, quantify, new RoutedEventArgs());
                var deNovo = new DeNovoControl(); Host(deNovo);
                foreach (string method in new[] { "ButtonBrowsePeaksDirectory_Click", "ButtonBrowseSeproPackage_Click", "ButtonBrowseHomologyFcf_Click" })
                    Call(deNovo, method, deNovo, new RoutedEventArgs());
                deNovo.ApplyImport(new DeNovoImport());
                deNovo.ApplyReconciliation(new DeNovoReconciliation());
                foreach (string method in new[] { "ButtonExportSummary_Click", "ButtonExportReviewQueue_Click", "ButtonExportOverlapStudy_Click" })
                    Call(deNovo, method, deNovo, new RoutedEventArgs());
                var rows = new List<QuantRow> { new() { Run = "synthetic", Side = "identified", Sequence = "PEPTIDEK", Charge = 2,
                    PrecursorMz = 500, IsotopeAreas = new[] { 10d, 5d, 2d }, EnvelopeSum = 17 } };
                var results = new AnalyticsControl(new AnalyticsViewModel(rows,
                    new QuantManifest { DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method, PerRunTic = new() { ["synthetic"] = 100 } }, curation, homology: new DeNovoHomology()));
                Host(results);
                foreach (var kind in Enum.GetValues<ResultsExportKind>())
                {
                    results.ViewModel.ExportKind = kind;
                    Call(results, "ExportClicked", results, new RoutedEventArgs());
                }
                Check("browse fixture opens its curation before testing shell exports", main.OpenProjectPath(file), main.StatusModel.ContextHelp);
                main.RememberFolder(first);
                Call(main, "MenuItemSaveFCF_Click", main, new RoutedEventArgs());
                Call(main, "MenuItemReanotateSEPro_Click", main, new RoutedEventArgs());
                var button = new Button(); Host(button);
                var status = new TextBlock();
                StaticCall(typeof(IdentityScanWindow), "SaveFigureFromDialog", button, status, new List<IdentityScanRow>(), null);
                StaticCall(typeof(SpectrumComparisonWindow), "SaveCopy", button, status, new SpectrumComparisonViewModel(Array.Empty<SpectrumComparisonRow>(), null, null));
                StaticCall(typeof(SpectrumComparisonWindow), "SaveAcceptedScoreScatter", button, status, new DeNovoAcceptedScoreScatter(), null);
                StaticCall(typeof(ThreeWayWindow), "SaveCsv", button, new List<ThreeWayPartitionRow>());
            }

            ExerciseBrowses();
            Check("every non-curation browse path refuses headless native UI", nativeBrowses == 0 && nativeMessages == 0);
            main.Show(); PumpDispatcher();
            main.RememberFolder(first);
            ExerciseBrowses();
            string[] expected =
            {
                "project.open", "project.save", "build.database", "quantify.raw", "quantify.sepro", "quantify.denovo",
                "denovo.import", "denovo.sepro", "denovo.curation", "denovo.export.FamilyQuant_DeNovo_Summary.csv",
                "denovo.export.FamilyQuant_DeNovo_ReviewQueue.csv", "denovo.export.FamilyQuant_DeNovo_OverlapStudy.csv",
                "results.Summary", "results.Peptides", "results.Figures", "results.EvidencePack", "results.Homology",
                "families.export", "sepro.reannotate", "identity.figure", "spectrum.csv", "spectrum.score-plot", "partition.csv"
            };
            foreach (string id in expected)
                Check("browse uses the shared folder preference: " + id, seen.TryGetValue(id, out string? folder)
                    && string.Equals(folder, Path.GetFullPath(first), StringComparison.OrdinalIgnoreCase), seen.GetValueOrDefault(id));
            Check("executed browse inventory matches the declared S8 cases", seen.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .SequenceEqual(expected.OrderBy(k => k, StringComparer.Ordinal)));
            Check("cancelled browse keeps the prior folder", main.PreferredFolder == Path.GetFullPath(first));

            var ownedButton = new Button();
            var owned = new Window { Owner = main, Content = ownedButton, Left = -30000, Top = -30000,
                ShowInTaskbar = false, ShowActivated = false };
            owned.Show();
            try
            {
                Window? passedOwner = null;
                ShellDialogs.FilePresenter = (owner, _, dialog) =>
                { passedOwner = owner; dialog.FileName = Path.Combine(second, "chosen.sepr2"); return true; };
                Check("an owned secondary window shares the shell folder and records a successful file choice",
                    ShellDialogs.ShowFile(ownedButton, "owned.file", new OpenFileDialog()) && ReferenceEquals(passedOwner, owned)
                    && main.PreferredFolder == Path.GetFullPath(second)
                    && ShellSessionState.Load(MainWindow.ShellSettingsPathOverride).LastFolder == Path.GetFullPath(second));
            }
            finally { owned.Close(); }
            var q = new QuantifyControl(); Host(q);
            ShellDialogs.FolderPresenter = (_, _, dialog) => { dialog.FolderName = first; return true; };
            Call(q, "BrowseRaw", q, new RoutedEventArgs());
            Check("accepted Quantify folder updates both the input and the persisted preference", q.ViewModel.RawDirectory == first
                && main.PreferredFolder == Path.GetFullPath(first));
            ShellMessages.Report(main, fullMessage, "Displayed message", image: MessageBoxImage.Warning);
            Check("displayed messages use one presenter and retain their full status text", nativeMessages == 1
                && main.StatusModel.ContextHelp.Contains(fullMessage) && messages.Last().Image == MessageBoxImage.Warning);
        }
        finally
        {
            main.Close();
            ShellDialogs.FilePresenter = oldFile; ShellDialogs.FolderPresenter = oldFolder; ShellMessages.Presenter = oldMessage;
            ShellMessages.Reported -= observe;
            MainWindow.SelectOpenPathOverride = oldOpen; MainWindow.SelectSaveProjectPathOverride = oldSave;
            MainWindow.ShellSettingsPathOverride = oldSettings;
        }
    }
}
