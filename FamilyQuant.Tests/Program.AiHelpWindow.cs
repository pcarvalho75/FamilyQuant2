using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
}
