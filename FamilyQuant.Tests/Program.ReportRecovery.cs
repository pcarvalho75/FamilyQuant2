using FamilyQuant.Evidence;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportRecoveryCommandTable() =>
    [new("--test-report-recovery", 432, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-report-recovery", (args, _) => ReportRecoveryTests(args))];

    private static int ReportRecoveryTests(string[] args)
    {
        EnsureApplication();
        string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.ReportRecovery", Guid.NewGuid().ToString("N"));
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a fresh report-recovery output folder.");
        Directory.CreateDirectory(output);
        ProteinEvidence Evidence(string accession, string family, string domain) => new()
        {
            Accession = accession, Reviewed = true, Family = family, Keywords = new() { "chosen" },
            Pfam = new() { new() { Accession = domain, Name = "Synthetic domain" } },
        };
        var file = new FamilyClusterFile
        {
            FamilyPanel = new() { Name = "Synthetic panel", Families = new()
            {
                new() { Key = "first", DisplayName = "First group", Pfam = new() { new() { Accession = "domain-first" } } },
                new() { Key = "second", DisplayName = "Second group", Pfam = new() { new() { Accession = "domain-second" } } },
            } },
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "sp|P80001|FIRST", Sequence = "TTACDEFGHIKGG", UserID = 7, USearchID = 7,
                    Description = "Synthetic first protein", Evidence = Evidence("P80001", "source first", "domain-first") },
                new() { SequenceIdentifier = "sp|P80002|SECOND", Sequence = "TTLMNPQRSTVKGG", UserID = 9, USearchID = 9,
                    Description = "Synthetic second protein", Evidence = Evidence("P80002", "source second", "domain-second") },
            },
            LockedUserIds = new() { 7, 9, 99 },
            EvidenceSource = new() { Provider = "UniProt", Release = "fixture", ReferencePanel = new()
                { Provider = "UniProt", Release = "fixture", Families = new() { "source first", "source second" } } },
        };
        var rows = new[] { Qr("identified", "recovery", 1, 2, "ACDEFGHIK", 100, 150), Qr("identified", "recovery", 2, 2, "LMNPQRSTVK", 50, 75) };
        var manifest = new QuantManifest { PerRunTic = new() { ["recovery"] = 100000 } };
        void RefreshShares(FamilyClusterFile current, QuantManifest destination)
        {
            var hits = FamilyEnvelopeQuant.SearchCuration(rows, current);
            var mapping = FamilyEnvelopeQuant.AttributeHits(hits, current);
            var assigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), mapping);
            FamilyCompositionStudy.StoreRunShares(destination, assigned);
        }
        RefreshShares(file, manifest);
        string input = Path.Combine(output, "legacy-shaped.fqproj"), report = Path.Combine(output, "recovered-report.docx");
        ProjectStore.WriteFqproj(input, file, rows, manifest);
        string inputHash = FileFingerprint.Sha256(input);
        var oldMessages = ShellMessages.Presenter;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldPath = MainWindow.SelectReportPathOverride;
        var oldPresenter = MainWindow.ReportExportPresenterOverride;
        var oldFactory = MainWindow.ReportDocumentFactoryOverride;
        var oldStager = MainWindow.ReportFileStagerOverride;
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        var oldTheme = Themes.ThemeManager.Current;
        var messages = new List<string>();
        ShellMessages.Presenter = (_, message) => { messages.Add(message.Text); return MessageBoxResult.OK; };
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        MainWindow.SelectReportPathOverride = () => report;
        MainWindow.ReportExportPresenterOverride = editor => editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        MainWindow.ReportDocumentFactoryOverride = null; MainWindow.ReportFileStagerOverride = null;
        MainWindow.ShellSettingsPathOverride = Path.Combine(output, "settings.json");
        var shell = new MainWindow { Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Width = 1024, Height = 768 };
        try
        {
            shell.Show(); PumpDispatcher(); shell.OpenProjectPath(input); WaitForReportFixture(shell);
            var control = ((Grid)shell.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
            var current = control.ViewModel.Document;
            Check("Z12p5l native report first names missing evidence and its ordered repair actions", !FinishReportCommand(shell.ExportReportAsync())
                && messages.Last().Contains(PanelAssignmentEvidenceStore.RebuildMessage, StringComparison.Ordinal) && !File.Exists(report));
            string beforeUnlock = current.ToJsonText();
            string quantities = JsonSerializer.Serialize(rows);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var session = new EvidenceGroupingSession(current, "fixture");
                var proposed = session.Propose("chosen");
                string proposalBefore = JsonSerializer.Serialize(session.Proposal);
                int undoBefore = control.ViewModel.UndoStack.Count;
                var preview = new SuperClusterPreview(current.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, current.LockedUserIds);
                var review = new SuperClusterPreviewWindow(preview, proposed, session.Propose, initialScope: "chosen", evidenceMode: true,
                    unlockEvidenceFamilies: scope => session.UnlockEvidenceFamilies(scope, families => control.ViewModel.UnlockFamilies(families)));
                var model = (GroupingReviewViewModel)review.DataContext;
                review.Left = -30000; review.Top = -30000; review.ShowActivated = false; review.ShowInTaskbar = false;
                review.WindowStartupLocation = WindowStartupLocation.Manual;
                review.Width = 1024; review.Height = 768;
                try
                {
                    review.Show(); PumpDispatcher();
                    Check("Z12p5l locked proposal names locks and offers one enabled unlock action", model.AllProposedProteinsLocked
                        && !model.CanConfirm && model.ApplyHint.Contains("Choose Unlock evidence families", StringComparison.Ordinal)
                        && ((Button)review.FindName("UnlockEvidenceFamiliesButton")) is { IsEnabled: true, Visibility: Visibility.Visible });
                    if (attempt == 0)
                    {
                        model.ScopeText = "changed";
                        model.UnlockEvidenceFamiliesAsync().GetAwaiter().GetResult();
                        Check("Z12p5l edited scope blocks unlock with its next action and leaves locks intact", !model.CanUnlockEvidenceFamilies
                            && model.UnlockEvidenceFamiliesHint.Contains("Use Update proposal", StringComparison.Ordinal) && current.ToJsonText() == beforeUnlock);
                        model.ScopeText = "chosen";
                        model.Evidence!.Families[0].Name = "Draft label";
                        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
                        { Themes.ThemeManager.Apply(theme); PumpDispatcher(); ScreenHarness.Save(review, Path.Combine(output, "locked-" + theme + ".png"), 1024, 768); }
                    }
                    ((Button)review.FindName("UnlockEvidenceFamiliesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpDispatcher();
                    Check("Z12p5l one press removes only represented locks and adds exactly one Undo", current.LockedUserIds.SetEquals(new[] { 99 })
                        && control.ViewModel.UndoStack.Count == undoBefore + 1);
                    Check("Z12p5l unlock preserves the calculated proposal, membership and stored signal", JsonSerializer.Serialize(session.Proposal) == proposalBefore
                        && current.MyExtendedFastaItems.Select(p => p.UserID).SequenceEqual(new[] { 7, 9 }) && JsonSerializer.Serialize(rows) == quantities);
                    Check("Z12p5l newly eligible review uses the proposal defaults", model.CanConfirm && model.Evidence!.SelectedCount == 2
                        && !model.ShowUnlockEvidenceFamilies);
                    if (attempt == 0)
                    {
                        Check("Z12p5l unlock retains the staged family label", model.Evidence!.Families[0].Name == "Draft label");
                        control.ViewModel.Undo();
                        Check("Z12p5l one Undo restores the exact pre-unlock project", current.ToJsonText() == beforeUnlock
                            && control.ViewModel.UndoStack.Count == undoBefore);
                    }
                    else
                    {
                        var apply = typeof(ClusterGenerationControl).GetMethod("ApplyEvidenceChoice", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        var operation = (Task)apply.Invoke(control, new object[] { session, model.Evidence!.CaptureSelection() })!;
                        FinishBusyTask(operation); operation.GetAwaiter().GetResult();
                    }
                }
                finally { review.Close(); PumpDispatcher(); }
            }
            Check("Z12p5l Apply grouping rebuilds actual current assignment facts", PanelAssignmentEvidenceStore.Current(current)?.Count == 2
                && current.EvidenceRecipe != null && current.LockedUserIds.Contains(99));
            Check("Z12p5l native report then names stale shares and Quantify before Export report", !FinishReportCommand(shell.ExportReportAsync())
                && messages.Last().Contains("Per-run family shares", StringComparison.Ordinal)
                && messages.Last().Contains("Open Quantify. Run Quantify again to refresh them, then choose Export report.", StringComparison.Ordinal) && !File.Exists(report));
            var loadedManifest = (QuantManifest)typeof(MainWindow).GetField("loadedManifest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            RefreshShares(current, loadedManifest);
            Check("Z12p5l production stored-row share calculation clears the membership refusal", FamilyStudyMembership.Problem(current, loadedManifest) == null);
            Check("Z12p5l native report exports after evidence repair and refreshed shares", FinishReportCommand(shell.ExportReportAsync())
                && File.Exists(report) && File.Exists(ReportSupplementWorkbook.PathForReport(report)));
            Check("Z12p5l recovery retains source input bytes and stored peptide quantities", FileFingerprint.Sha256(input) == inputHash && JsonSerializer.Serialize(rows) == quantities);
            File.WriteAllLines(Path.Combine(output, "messages.txt"), messages);
        }
        finally
        {
            shell.Close(); PumpDispatcher(); Themes.ThemeManager.Apply(oldTheme);
            ShellMessages.Presenter = oldMessages; MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            MainWindow.SelectReportPathOverride = oldPath; MainWindow.ReportExportPresenterOverride = oldPresenter;
            MainWindow.ReportDocumentFactoryOverride = oldFactory; MainWindow.ReportFileStagerOverride = oldStager;
            MainWindow.ShellSettingsPathOverride = oldSettings;
        }
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
