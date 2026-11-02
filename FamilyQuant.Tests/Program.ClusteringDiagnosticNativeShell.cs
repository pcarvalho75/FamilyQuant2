using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ClusteringDiagnosticNativeShellTests(string workDir)
    {
        Section("Default native diagnostic through the Analysis action");
        Directory.CreateDirectory(workDir);
        string outputParent = Path.Combine(workDir, "reports");
        Directory.CreateDirectory(outputParent);
        const string sequence = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHRQ";
        var fixture = new FamilyClusterFile { ClusteringIdentity = 70, LockedUserIds = new() { 71 }, MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "native-diagnostic-a", Sequence = sequence, UserID = 71, USearchID = 1 },
            new() { SequenceIdentifier = "native-diagnostic-b", Sequence = sequence, UserID = 72, USearchID = 2 }
        } };
        string fcf = Path.Combine(workDir, "saved-native.fcf"), project = Path.Combine(workDir, "saved-native.fqproj");
        fixture.Save(fcf);
        ProjectStore.WriteFqproj(project, fixture, Array.Empty<QuantRow>(), new QuantManifest { Provenance = QuantManifest.DefaultProvenance() });
        string? originalSettings = MainWindow.ShellSettingsPathOverride;
        var originalFolder = ShellDialogs.FolderPresenter;
        var originalMessage = ShellMessages.Presenter;
        MainWindow.ShellSettingsPathOverride = Path.Combine(workDir, "shell-settings.json");
        var reports = new List<string>();
        var folders = new List<string>();
        var main = new MainWindow { Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
        main.ClusteringDiagnosticReportPresenterOverride = (_, text) => reports.Add(text);
        main.ClusteringDiagnosticFolderOpenerOverride = folders.Add;
        main.ClusteringDiagnosticIdentityChoiceOverride = () => throw new InvalidOperationException("A saved identity must not ask for another threshold.");
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        ShellDialogs.FolderPresenter = (_, id, dialog) =>
        {
            Check("the native menu action uses the diagnostic output chooser", id == "clustering.diagnostic");
            dialog.FolderName = outputParent;
            return true;
        };
        try
        {
            main.Show(); PumpDispatcher();
            var menu = (MenuItem)main.FindName("MenuItemClusteringDiagnostic");
            foreach (string input in new[] { fcf, project })
            {
                string extension = Path.GetExtension(input);
                string before = FileFingerprint.Sha256(input);
                Check("the native diagnostic opens its saved " + extension + " fixture", main.OpenProjectPath(input));
                var document = main.NavigationModel.Document!;
                string state = ShellDocumentState.Fingerprint(document);
                File.SetAttributes(input, File.GetAttributes(input) | FileAttributes.ReadOnly);
                var pair = new List<string>();
                for (int run = 0; run < 2; run++)
                {
                    int priorReports = reports.Count, priorFolders = folders.Count;
                    menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    FinishBusyOperation(main.Operations);
                    bool completed = reports.Count == priorReports + 1 && folders.Count == priorFolders + 1;
                    Check("the Analysis action completes the default native " + extension + " diagnostic run " + (run + 1), completed,
                        main.StatusModel.ContextHelp);
                    if (!completed) continue;
                    string folder = folders[^1], text = reports[^1];
                    pair.Add(folder);
                    Check("the native " + extension + " report presenter receives report contents rather than its file path",
                        text == File.ReadAllText(Path.Combine(folder, "diagnostic.txt"))
                        && text.Contains("These comparisons do not change your saved families.", StringComparison.Ordinal)
                        && text.Contains("Identity: 70 percent. Input records: 2.", StringComparison.Ordinal));
                    Check("the native " + extension + " action opens its fresh complete packet without all-pairs output",
                        Path.GetDirectoryName(folder) == outputParent && File.Exists(Path.Combine(folder, "summary.json"))
                        && File.Exists(Path.Combine(folder, "input.fasta")) && File.Exists(Path.Combine(folder, "before.uc"))
                        && File.Exists(Path.Combine(folder, "native-coverage.uc"))
                        && !File.Exists(Path.Combine(folder, "small-allpairs.native.tsv")));
                }
                Check("two default native " + extension + " packets are byte-identical in different fresh folders",
                    pair.Count == 2 && pair[0] != pair[1]
                    && Directory.GetFiles(pair[0]).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal)
                        .SequenceEqual(Directory.GetFiles(pair[1]).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal))
                    && Directory.GetFiles(pair[0]).All(path => FileFingerprint.Sha256(path)
                        == FileFingerprint.Sha256(Path.Combine(pair[1], Path.GetFileName(path)))));
                Check("the native " + extension + " diagnostic preserves saved bytes, grouping, locks and saved state",
                    before == FileFingerprint.Sha256(input) && ReferenceEquals(main.NavigationModel.Document, document)
                    && ShellDocumentState.Fingerprint(document) == state && !main.DocumentState.IsModified && !main.Operations.IsBusy
                    && document.MyExtendedFastaItems.Select(item => item.UserID).SequenceEqual(new[] { 71, 72 })
                    && document.LockedUserIds.SetEquals(new[] { 71 }));
                File.SetAttributes(input, File.GetAttributes(input) & ~FileAttributes.ReadOnly);
            }
            Check("the actual diagnostic uses the default runner and presents four completed reports and folders",
                main.ClusteringDiagnosticRunnerOverride == null && reports.Count == 4 && folders.Count == 4);
        }
        finally
        {
            foreach (string input in new[] { fcf, project })
                if (File.Exists(input)) File.SetAttributes(input, File.GetAttributes(input) & ~FileAttributes.ReadOnly);
            main.Close();
            ShellDialogs.FolderPresenter = originalFolder;
            ShellMessages.Presenter = originalMessage;
            MainWindow.ShellSettingsPathOverride = originalSettings;
        }
    }
}
