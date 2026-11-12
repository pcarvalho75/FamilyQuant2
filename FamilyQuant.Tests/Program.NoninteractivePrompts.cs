using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> NoninteractivePromptCommands() =>
    [new("--test-noninteractive-prompts", 100, TestCommandPhase.Raw,
        args => args is ["--test-noninteractive-prompts"], (_, _) =>
        {
            NoninteractivePromptTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void InstallTestShellMessagePresenter(Assembly assembly)
    {
        // Frozen comparison shells have separate statics and message record types.
        var property = assembly.GetType("FamilyQuant.ShellMessages")?.GetProperty("Presenter", BindingFlags.Public | BindingFlags.Static);
        if (property?.CanWrite != true || property.GetValue(null) != null) return;
        var answer = typeof(Program).GetMethod(nameof(AnswerUnansweredPrompt), BindingFlags.NonPublic | BindingFlags.Static)!;
        property.SetValue(null, Delegate.CreateDelegate(property.PropertyType, answer));
    }

    private static MessageBoxResult AnswerUnansweredPrompt(Window? owner, object request)
    {
        var type = request.GetType();
        string title = type.GetProperty("Title")?.GetValue(request) as string ?? "(untitled)";
        Console.WriteLine("UNANSWERED PROMPT: " + title.Replace('\r', ' ').Replace('\n', ' '));
        var answer = type.GetProperty("DefaultResult")?.GetValue(request) is MessageBoxResult result ? result : MessageBoxResult.None;
        if (answer != MessageBoxResult.None) return answer;
        var buttons = type.GetProperty("Buttons")?.GetValue(request) is MessageBoxButton value ? value : MessageBoxButton.OK;
        return buttons switch
        {
            MessageBoxButton.OK or MessageBoxButton.OKCancel => MessageBoxResult.OK,
            MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
            _ => MessageBoxResult.Cancel,
        };
    }

    private static void NoninteractivePromptTests()
    {
        Section("IM7c unattended prompts return defaults through the runner seam");
        EnsureApplication();
        var previous = ShellMessages.Presenter;
        Check("IM7c the runner installs its nonmodal responder before command dispatch", previous != null
            && previous.Method.Name == nameof(AnswerUnansweredPrompt));
        if (previous == null) return;
        var output = Console.Out;
        using var captured = new StringWriter();
        var host = new Window { Content = new TextBlock { Text = "Synthetic prompt host" }, Width = 300, Height = 200,
            Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        var answers = new List<MessageBoxResult>();
        MessageBoxResult explicitAnswer = MessageBoxResult.None;
        bool retained = false;
        try
        {
            host.Show(); PumpDispatcher();
            ShellMessages.Presenter = null;
            InstallTestShellMessagePresenter(typeof(MainWindow).Assembly);
            if (ShellMessages.Presenter == null) throw new InvalidOperationException("The test responder was not installed.");
            Console.SetOut(captured);
            foreach (var choice in new[] { MessageBoxResult.No, MessageBoxResult.Yes, MessageBoxResult.Cancel })
                answers.Add(ShellMessages.Report(host, "Synthetic confirmation.", "Default " + choice,
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question, choice));
            answers.Add(ShellMessages.Report(host, "Synthetic notice.", "Default OK"));
            answers.Add(ShellMessages.Report(host, "Synthetic confirmation.", "Default first button", MessageBoxButton.YesNo));
            Func<Window?, ShellMessage, MessageBoxResult> responder = (_, _) => MessageBoxResult.Yes;
            ShellMessages.Presenter = responder;
            InstallTestShellMessagePresenter(typeof(MainWindow).Assembly);
            retained = ReferenceEquals(ShellMessages.Presenter, responder);
            explicitAnswer = ShellMessages.Report(host, "Synthetic explicit response.", "Explicit response",
                MessageBoxButton.YesNo, defaultResult: MessageBoxResult.No);
        }
        finally
        {
            Console.SetOut(output);
            host.Close();
            ShellMessages.Presenter = previous;
        }
        Check("IM7c explicit defaults and platform first-button defaults return without a native box",
            answers.SequenceEqual(new[] { MessageBoxResult.No, MessageBoxResult.Yes, MessageBoxResult.Cancel, MessageBoxResult.OK, MessageBoxResult.Yes }));
        Check("IM7c each unanswered prompt produces one title-only diagnostic", captured.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .SequenceEqual(new[] { "Default No", "Default Yes", "Default Cancel", "Default OK", "Default first button" }.Select(title => "UNANSWERED PROMPT: " + title)));
        Check("IM7c an explicit test responder is retained and overrides the default", retained && explicitAnswer == MessageBoxResult.Yes);
    }
}
