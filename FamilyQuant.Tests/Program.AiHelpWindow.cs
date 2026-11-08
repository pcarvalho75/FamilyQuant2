using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FamilyQuant.Ai;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiHelpWindowCommands() =>
    [new("--test-ai-help-window", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-help-window", (args, _) =>
        {
            AiHelpWindowTests(args.Length == 3 && args[1] == "--screens" ? args[2] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private sealed class FakeHelpAssistant : IHelpAssistant
    {
        public List<HelpQuestion> Questions { get; } = new();
        public HelpAnswer Answer { get; set; } = new(AiOutcome.Success, "Use the displayed controls.",
            [new HelpSource("FamilyQuant/Example.cs", 10, 20)], true, null);
        public Task<HelpAnswer> AskAsync(string key, HelpQuestion question, CancellationToken cancellationToken)
        { Questions.Add(question); return Task.FromResult(Answer); }
    }

    private static readonly HelpState SyntheticHelpState = new("2.1.12.0", "Home", "Choose a step.", "No project open", "Synthetic error", "Synthetic log");

    private static void AiHelpWindowTests(string? screens = null)
    {
        EnsureApplication();
        var originalErrorDirectory = CrashReportStore.DirectoryOverride;
        var originalLogDirectory = MainWindow.HelpLogDirectoryOverride;
        CrashReportStore.DirectoryOverride = Path.Combine(Path.GetTempPath(), "fq-help-synthetic-" + Guid.NewGuid().ToString("N"));
        MainWindow.HelpLogDirectoryOverride = CrashReportStore.DirectoryOverride;
        var shell = new MainWindow();
        var secondary = new Window { Title = "Secondary", Width = 360, Height = 200, ShowInTaskbar = false };
        try
        {
            shell.Show(); PumpDispatcher();
            secondary.Owner = shell;
            MainWindow.AskAiHelpCommand.Execute(null, shell); PumpDispatcher();
            var first = shell.AiHelp;
            Check("H2a Ctrl+H command opens AI Help from the main window", first?.IsVisible == true);
            secondary.Show(); PumpDispatcher();
            var oldDirectory = CrashReportStore.DirectoryOverride;
            CrashReportStore.DirectoryOverride = Path.Combine(Path.GetTempPath(), "fq-help-synthetic-" + Guid.NewGuid().ToString("N"));
            try
            {
                for (int i = 0; i < 45; i++) shell.Operations.LogMessage("synthetic", "Synthetic line " + i);
                shell.Operations.LogFailure(new InvalidOperationException("Synthetic failure"));
                var state = shell.CaptureHelpState();
                Check("H2b shell state uses assembly version, live help, screen and no project", state.Version == typeof(MainWindow).Assembly.GetName().Version!.ToString()
                    && state.Screen == WorkflowNames.Home && state.ProjectSummary == "No project open" && state.ScreenHelp.Contains(WorkflowNames.Home));
                Check("H2b logged error and last 40 log lines are included", state.LastError.Contains("Synthetic failure")
                    && state.LogTail.Split('\n').Length == 40 && !state.LogTail.Contains("Synthetic line 0\n"));
            }
            finally { CrashReportStore.DirectoryOverride = oldDirectory; }
            MainWindow.AskAiHelpCommand.Execute(null, secondary); PumpDispatcher();
            Check("H2a secondary window command reuses one AI Help window", ReferenceEquals(first, shell.AiHelp)
                && Application.Current.Windows.OfType<AiHelpWindow>().Count() == 1);
            using (shell.Operations.Begin("Synthetic operation"))
                Check("H2d AI Help command remains available during a main window operation", MainWindow.AskAiHelpCommand.CanExecute(null, shell));
            first!.Close();
            MainWindow.AskAiHelpCommand.Execute(null, secondary); PumpDispatcher();
            Check("H2a secondary window command opens AI Help when closed", shell.AiHelp?.IsVisible == true);
            shell.AiHelp!.Close();
            var menu = (MenuItem)shell.FindName("MenuItemAiHelp");
            Check("H2a Help starts with the explained Ctrl+H command", ((MenuItem)menu.Parent).Items[0] == menu
                && menu.InputGestureText == "Ctrl+H" && menu.ToolTip != null);
            Check("H2a the class command uses the Ctrl+H gesture", MainWindow.AskAiHelpCommand.InputGestures
                .OfType<KeyGesture>().Any(b => b.Key == Key.H && b.Modifiers == ModifierKeys.Control));
        }
        finally { shell.AiHelp?.Close(); secondary.Close(); shell.Close(); CrashReportStore.DirectoryOverride = originalErrorDirectory; MainWindow.HelpLogDirectoryOverride = originalLogDirectory; }

        var pictureWindow = new Window { Width = 1900, Height = 300, Content = new TextBlock { Text = "Synthetic picture" }, ShowInTaskbar = false };
        pictureWindow.Show(); PumpDispatcher();
        byte[] picture = AiHelpWindow.CaptureWindow(pictureWindow)!;
        pictureWindow.Close();
        using (var stream = new MemoryStream(picture))
        {
            var bitmap = BitmapFrame.Create(stream);
            Check("H2b PNG capture caps the longer side at 1600 pixels", bitmap.PixelWidth <= 1600 && bitmap.PixelHeight <= 1600 && bitmap.PixelWidth > 0);
        }
        int captures = 0;
        var fake = new FakeHelpAssistant();
        var window = new AiHelpWindow(fake, () => "synthetic key", () => SyntheticHelpState, MainWindow.HelpOutcomeMessage,
            () => { captures++; return picture; });
        try
        {
            window.Question.Text = "Where do I begin?";
            window.AskAsync().GetAwaiter().GetResult();
            Check("H2a Ask sends the question and state", fake.Questions.Count == 1 && fake.Questions[0].Text == "Where do I begin?"
                && fake.Questions[0].State == SyntheticHelpState && fake.Questions[0].Earlier.Count == 0);
            window.Question.Text = "And then?"; window.AskAsync().GetAwaiter().GetResult();
            Check("H2a earlier turns go back oldest first", fake.Questions[1].Earlier.SequenceEqual(new[] { new HelpTurn("Where do I begin?", fake.Answer.Text) }));
            Check("H2b picture is off by default and never captured or sent", window.IncludePicture.IsChecked != true && captures == 0
                && fake.Questions.All(q => q.ScreenshotPng == null));
            window.RefreshState();
            Check("H2b preview shows every state field before sending", new[] { SyntheticHelpState.Version, SyntheticHelpState.Screen,
                SyntheticHelpState.ScreenHelp, SyntheticHelpState.ProjectSummary, SyntheticHelpState.LastError, SyntheticHelpState.LogTail }.All(window.StatePreview.Text.Contains));
            window.IncludePicture.IsChecked = true;
            Check("H2b opting in displays a small picture preview", captures == 1 && window.PicturePreview.Source != null
                && window.PicturePreview.Visibility == Visibility.Visible);
            window.AskAsync().GetAwaiter().GetResult();
            Check("H2b Ask takes a fresh picture only when ticked", captures == 2 && fake.Questions[^1].ScreenshotPng!.SequenceEqual(picture));
            window.IncludePicture.IsChecked = false; window.AskAsync().GetAwaiter().GetResult();
            Check("H2b unticking removes the preview and omits the picture", captures == 2 && fake.Questions[^1].ScreenshotPng == null && window.PicturePreview.Source == null);
            var reports = LogicalHelpText(window.Answers);
            Check("H2a answers and sources are selectable", reports.Contains("FamilyQuant/Example.cs, lines 10-20")
                && HelpBoxes(window.Answers).All(b => b.IsReadOnly));
            var method = typeof(MainWindow).GetMethod("AiDraftMessage", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var outcome in Enum.GetValues<AiOutcome>().Where(o => o != AiOutcome.Success))
            {
                fake.Answer = fake.Answer with { Outcome = outcome };
                window.Question.Text = "Synthetic question"; window.AskAsync().GetAwaiter().GetResult();
                var expected = (string)method.Invoke(null, new object[] { Enum.Parse<AiWorkflowOutcome>(outcome.ToString()) })!;
                Check("H2a " + outcome + " uses the AI draft message", LogicalHelpText(window.Answers).Contains(expected));
            }
            Check("H2d usage notice states account charges and shared GitHub limits", window.UsageNotice.Text == "Questions go to Claude with your own Anthropic key and count on your Anthropic account. AI Help reads the source through GitHub, which allows 10 code searches a minute and 5,000 file reads an hour for all FamilyQuant users together; when that limit is reached, AI Help says so and when it resets.");
            var reset = new DateTimeOffset(2030, 1, 2, 12, 34, 0, TimeSpan.Zero);
            fake.Answer = fake.Answer with { Outcome = AiOutcome.Success, SourceReadable = false, GitHubLimitResetsAt = reset };
            window.AskAsync().GetAwaiter().GetResult();
            Check("H2d earlier turns retain every displayed outcome", fake.Questions[^1].Earlier.Count == fake.Questions.Count - 1
                && fake.Questions[^1].Earlier[^1].Answer == MainWindow.HelpOutcomeMessage(AiOutcome.Incomplete));
            Check("H2d answer explains source unavailability", HelpNotices(window.Answers).Contains("This build cannot read the source on GitHub, so this answer relies on the screen and FamilyQuant's own help."));
            Check("H2d answer names the local GitHub reset time", HelpNotices(window.Answers).Contains("GitHub's limit was reached; reading resumes at " + reset.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) + "."));
            var keyWindow = new AiKeySettingsWindow(new AiKeyStore(Path.Combine(Path.GetTempPath(), "fq-help-unused-" + Guid.NewGuid().ToString("N"))));
            try
            {
                Check("H2d key window explains the shared key and shortcut", HelpNotices(keyWindow).Contains("The same key is used by AI Help (Help > Ask AI Help, or Ctrl+H)."));
                if (screens != null)
                {
                    Directory.CreateDirectory(screens); var previous = Themes.ThemeManager.Current;
                    try
                    {
                        keyWindow.Show(); PumpDispatcher();
                        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
                        { Themes.ThemeManager.Apply(theme); PumpDispatcher(); ScreenHarness.Save(keyWindow, Path.Combine(screens, "ai-key-" + theme + ".png"), 650, 390); }
                    }
                    finally { Themes.ThemeManager.Apply(previous); }
                }
            }
            finally { keyWindow.Close(); }
            string? copied = null, address = null, folder = null;
            string reportDirectory = Path.Combine(Path.GetTempPath(), "fq-help-report-" + Guid.NewGuid().ToString("N"));
            var reportQuestion = new HelpQuestion("Synthetic issue & detail", SyntheticHelpState, picture, Array.Empty<HelpTurn>());
            var report = new AiHelpReportWindow(reportQuestion, "Synthetic answer", text => copied = text,
                url => address = url, path => { folder = path; return true; }, reportDirectory);
            try
            {
                Check("H2d report states account ownership and public visibility", HelpNotices(report).Contains("You submit the issue from your own GitHub account, and issues there are public."));
                Check("H2c report starts with the question title and requested fields", report.ReportTitle.Text == reportQuestion.Text
                    && new[] { reportQuestion.Text, "Synthetic answer", SyntheticHelpState.Version, SyntheticHelpState.Screen,
                        SyntheticHelpState.LastError, SyntheticHelpState.LogTail }.All(report.ReportBody.Text.Contains)
                    && !report.ReportBody.IsReadOnly && !report.ReportTitle.IsReadOnly && window.ReportButton.IsEnabled);
                report.ReportTitle.Text = "Edited & issue #";
                report.ReportBody.Text += "\n" + string.Concat(Enumerable.Repeat("Synthetic & detail \u00e9 \U0001F9EA\n", 900));
                Check("H2c opening the issue copies the full edited report", report.OpenIssue() && copied == report.ReportTitle.Text + "\n\n" + report.ReportBody.Text);
                Check("H2c issue URL uses the releases repository and stays below 7000 characters", address != null && address.Length < 7000
                    && new Uri(address).Host == "github.com" && new Uri(address).AbsolutePath == "/pcarvalho75/FamilyQuant2-releases/issues/new"
                    && address.Contains("title=" + Uri.EscapeDataString(report.ReportTitle.Text))
                    && Uri.UnescapeDataString(address).EndsWith("The full report is on the clipboard.", StringComparison.Ordinal));
                Check("H2c picture is saved as PNG and its folder is opened", folder == reportDirectory && report.SavedPicturePath != null
                    && File.ReadAllBytes(report.SavedPicturePath).SequenceEqual(picture) && Path.GetExtension(report.SavedPicturePath) == ".png");
                int folderOpens = 0;
                var noPicture = new AiHelpReportWindow(reportQuestion with { ScreenshotPng = null }, "Synthetic answer", _ => { }, _ => { },
                    _ => { folderOpens++; return true; }, reportDirectory);
                try { Check("H2c no picture produces no image handoff", noPicture.OpenIssue() && noPicture.SavedPicturePath == null && folderOpens == 0); }
                finally { noPicture.Close(); }
                Check("H2c short issue text is URL encoded without truncation", Uri.UnescapeDataString(AiHelpReportWindow.BuildIssueAddress("A & B", "Synthetic + # detail")).EndsWith("&body=Synthetic + # detail", StringComparison.Ordinal));
                if (screens != null)
                {
                    Directory.CreateDirectory(screens); report.ReportBody.Text = AiHelpReportWindow.BuildBody(reportQuestion, "Synthetic answer");
                    var previous = Themes.ThemeManager.Current;
                    try
                    {
                        report.Show(); PumpDispatcher();
                        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
                        { Themes.ThemeManager.Apply(theme); PumpDispatcher(); ScreenHarness.Save(report, Path.Combine(screens, "ai-help-report-" + theme + ".png"), 800, 700); }
                    }
                    finally { Themes.ThemeManager.Apply(previous); }
                }
            }
            finally
            {
                report.Close();
                if (report.SavedPicturePath != null && File.Exists(report.SavedPicturePath)) File.Delete(report.SavedPicturePath);
                if (Directory.Exists(reportDirectory)) Directory.Delete(reportDirectory);
            }
            if (screens != null)
            {
                Directory.CreateDirectory(screens);
                var previous = Themes.ThemeManager.Current;
                try
                {
                    window.Answers.Children.Clear(); fake.Answer = fake.Answer with { Outcome = AiOutcome.Success };
                    window.Question.Text = "Where do I begin?"; window.AskAsync().GetAwaiter().GetResult();
                    window.Show(); PumpDispatcher();
                    foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
                    {
                        Themes.ThemeManager.Apply(theme); PumpDispatcher();
                        ScreenHarness.Save(window, Path.Combine(screens, "ai-help-" + theme + ".png"), 840, 760);
                        foreach (var details in HelpNodes(window).OfType<Expander>()) details.IsExpanded = true;
                        window.IncludePicture.IsChecked = true; PumpDispatcher();
                        var settle = Stopwatch.StartNew(); while (settle.ElapsedMilliseconds < 400) PumpDispatcher();
                        ScreenHarness.Save(window, Path.Combine(screens, "ai-help-details-" + theme + ".png"), 840, 760);
                        foreach (var details in HelpNodes(window).OfType<Expander>()) details.IsExpanded = false;
                        window.IncludePicture.IsChecked = false;
                    }
                }
                finally { Themes.ThemeManager.Apply(previous); }
            }
        }
        finally { window.Close(); }
    }

    private static IEnumerable<TextBox> HelpBoxes(DependencyObject parent)
    {
        if (parent is TextBox box) yield return box;
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject dependency)
                foreach (var found in HelpBoxes(dependency)) yield return found;
    }
    private static string LogicalHelpText(DependencyObject parent) => string.Join("\n", HelpBoxes(parent).Select(b => b.Text));
    private static IEnumerable<DependencyObject> HelpNodes(DependencyObject parent)
    {
        yield return parent;
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject dependency)
                foreach (var found in HelpNodes(dependency)) yield return found;
    }
    private static string HelpNotices(DependencyObject parent) => string.Join("\n", HelpNodes(parent).OfType<TextBlock>().Select(t => t.Text));
}
