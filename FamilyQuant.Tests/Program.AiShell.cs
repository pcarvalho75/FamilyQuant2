using FamilyQuant.Ai;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class AiBlockingTransport : IAiTransport
    {
        public bool Started { get; private set; }
        public async Task<AiResult> SendAsync(string key, string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            Started = true;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new(AiOutcome.Incomplete);
        }
    }
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiShellCommands() =>
    [new("--test-ai-shell", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-shell", (_, _) =>
        {
            AiShellTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void AiShellTests()
    {
        EnsureApplication();
        var shell = new MainWindow();
        try
        {
            Check("J46 the live File menu exposes an optional AI draft command", shell.FindName("MenuItemAiDraft") is MenuItem);
            Check("J46 no open project cannot start a draft", !shell.DraftInterpretationAsync().GetAwaiter().GetResult());
        }
        finally { shell.Close(); }
        AiShellProjectTests();
    }

    private static void AiShellProjectTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.AiShell", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "fixture.fqproj");
        var project = new FamilyClusterFile { ReportDetails = new ReportDetails { Title = "User study", WrittenInterpretation = "Original interpretation" },
            MyExtendedFastaItems = new() { new() { SequenceIdentifier = "AI_FIXTURE", Sequence = "TTACDEFGHIKGG", Description = "Fixture", UserID = 1, USearchID = 1 } } };
        var manifest = new QuantManifest { PerRunTic = new() { ["fixture"] = 100000 }, FamilyMapping = new() { MinIdentity = .99, Method = "Fixture mapping" } };
        manifest.FamilyRunShares = new FamilyRunSharesResult { MembershipSha256 = FamilyStudyMembership.Fingerprint(project),
            Families = new() { new() { FamilyUserId = 1, MeanShare = .125, AvailableRunCount = 1 } },
            Runs = new() { new() { Run = "fixture", AssignedSignal = 100, MeasuredSignal = 200 } } };
        ProjectStore.WriteFqproj(path, project, new[] { Qr("identified", "fixture", 1, 2, "ACDEFGHIK", 100, 150) }, manifest);
        string beforeHash = TestFileHash.Upper(path);
        var store = new AiKeyStore(Path.Combine(root, "key.bin"));
        var transport = new AiFixtureTransport();
        var oldStore = MainWindow.AiKeyStoreOverride; var oldTransport = MainWindow.AiTransportOverride;
        var oldPreview = MainWindow.AiPreviewOverride; var oldReview = MainWindow.AiReviewOverride; var oldMessages = ShellMessages.Presenter;
        var oldSettings = MainWindow.ShellSettingsPathOverride;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(root, "settings.json");
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        MainWindow.AiKeyStoreOverride = () => store; MainWindow.AiTransportOverride = () => transport;
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        var shell = new MainWindow { Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Width = 1024, Height = 768 };
        try
        {
            shell.Show(); PumpDispatcher(); shell.OpenProjectPath(path); WaitForReportFixture(shell);
            var control = ((Grid)shell.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
            string j45Text = ReportInterpretationInput.FromProject(control.fcf, manifest, AppVersionInfo.Current());
            Check("J46 the native command without a key sends nothing", !FinishReportCommand(shell.DraftInterpretationAsync()) && transport.Calls == 0);
            store.Save("fixture-key");
            MainWindow.AiPreviewOverride = (_, _) => false;
            Check("J46 declining the native preview sends nothing and changes nothing", !FinishReportCommand(shell.DraftInterpretationAsync())
                && transport.Calls == 0 && control.fcf.ReportDetails?.WrittenInterpretation == "Original interpretation");
            string preview = "";
            MainWindow.AiPreviewOverride = (input, _) => { preview = input.Text; return true; };
            MainWindow.AiReviewOverride = (_, _) => false;
            Check("J46 discarding the native response changes nothing", !FinishReportCommand(shell.DraftInterpretationAsync())
                && transport.Calls == 1 && control.fcf.ReportDetails?.WrittenInterpretation == "Original interpretation");
            MainWindow.AiReviewOverride = (_, _) => true;
            bool applied = FinishReportCommand(shell.DraftInterpretationAsync());
            var saved = control.fcf.ReportDetails;
            Check("J46 native accept saves labelled text and the exact preview fingerprint", applied && transport.Calls == 2
                && transport.Prompt == preview && saved?.WrittenInterpretation.StartsWith("AI draft (model ", StringComparison.Ordinal) == true
                && saved.AiInterpretation?.InputSha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(preview))));
            Check("J46d native transport sends J45 export bytes without modification", System.Text.Encoding.UTF8.GetBytes(transport.Prompt)
                .SequenceEqual(System.Text.Encoding.UTF8.GetBytes(j45Text)));
            control.ViewModel.Undo();
            Check("J46 native Undo restores the previous interpretation", control.fcf.ReportDetails?.WrittenInterpretation == "Original interpretation");
            var previous = control.fcf.ReportDetails!;
            MainWindow.AiReviewOverride = (_, _) => { control.fcf.ReportDetails = previous with { Title = "Changed while reviewing" }; return true; };
            Check("J46 a changed project prevents the native command from overwriting it", !FinishReportCommand(shell.DraftInterpretationAsync())
                && control.fcf.ReportDetails?.Title == "Changed while reviewing" && control.fcf.ReportDetails.WrittenInterpretation == "Original interpretation");
            control.fcf.ReportDetails = previous;
            var blocking = new AiBlockingTransport(); MainWindow.AiTransportOverride = () => blocking;
            MainWindow.AiReviewOverride = (_, _) => true;
            Task<bool> pending = shell.DraftInterpretationAsync();
            var timer = Stopwatch.StartNew();
            while (!blocking.Started && !pending.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(20)) { PumpDispatcher(); Thread.Sleep(10); }
            if (!pending.IsCompleted) shell.Operations.Cancel();
            bool cancelledResult = FinishReportCommand(pending);
            Check("J46 cancelling an in-flight native request leaves the project unchanged", blocking.Started
                && !cancelledResult && control.fcf.ReportDetails == previous);
            Check("J46 native draft never overwrites the project file", TestFileHash.Upper(path) == beforeHash);
        }
        finally
        {
            shell.Close(); MainWindow.AiKeyStoreOverride = oldStore; MainWindow.AiTransportOverride = oldTransport;
            MainWindow.AiPreviewOverride = oldPreview; MainWindow.AiReviewOverride = oldReview; ShellMessages.Presenter = oldMessages;
            MainWindow.ShellSettingsPathOverride = oldSettings;
            MainWindow.UnsavedProjectChoiceOverride = oldChoice;
        }
    }
}
