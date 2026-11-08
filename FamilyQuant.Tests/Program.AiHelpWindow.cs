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
        finally { shell.AiHelp?.Close(); secondary.Close(); shell.Close(); }

        var fake = new FakeHelpAssistant();
        var window = new AiHelpWindow(fake, () => "synthetic key", () => SyntheticHelpState, MainWindow.HelpOutcomeMessage);
        try
        {
            window.Question.Text = "Where do I begin?";
            window.AskAsync().GetAwaiter().GetResult();
            Check("H2a Ask sends the question and state", fake.Questions.Count == 1 && fake.Questions[0].Text == "Where do I begin?"
                && fake.Questions[0].State == SyntheticHelpState && fake.Questions[0].Earlier.Count == 0);
            window.Question.Text = "And then?"; window.AskAsync().GetAwaiter().GetResult();
            Check("H2a earlier turns go back oldest first", fake.Questions[1].Earlier.SequenceEqual(new[] { new HelpTurn("Where do I begin?", fake.Answer.Text) }));
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
