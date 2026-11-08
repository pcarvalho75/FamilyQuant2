using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Ai;

public sealed class AiHelpWindow : Window
{
    private readonly IHelpAssistant assistant;
    private readonly Func<string?> key;
    private readonly Func<HelpState> state;
    private readonly Func<AiOutcome, string> outcomeMessage;
    private readonly List<HelpTurn> earlier = new();
    private CancellationTokenSource? asking;
    private bool closed;
    public TextBox Question { get; } = new();
    public Button AskButton { get; } = new();
    public Button CancelButton { get; } = new();
    public StackPanel Answers { get; } = new();
    public TextBlock Status { get; } = SecondaryWindowView.Text("", true);
    public Task? AskTask { get; private set; }

    public AiHelpWindow(IHelpAssistant assistant, Func<string?> key, Func<HelpState> state,
        Func<AiOutcome, string> outcomeMessage)
    {
        this.assistant = assistant; this.key = key; this.state = state; this.outcomeMessage = outcomeMessage;
        Title = "AI Help"; Width = 840; Height = 760; MinWidth = 560; MinHeight = 500;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SecondaryWindowView.Theme(this);
        var layout = new DockPanel();
        var top = new StackPanel();
        var title = SecondaryWindowView.Text(Title); title.SetResourceReference(StyleProperty, "PageTitle"); top.Children.Add(title);
        top.Children.Add(SecondaryWindowView.Text("Ask about FamilyQuant. AI Help reads the FamilyQuant source code on GitHub and what the program shows.", true));
        top.Children.Add(new Label { Content = "Your question", Target = Question, ToolTip = "Enter what you want to understand about the program." });
        Question.AcceptsReturn = true; Question.TextWrapping = TextWrapping.Wrap; Question.Height = 86;
        Question.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Question.ToolTip = "Type a question about FamilyQuant; earlier answers in this window provide context.";
        top.Children.Add(Question);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        AskButton.Content = "Ask"; AskButton.ToolTip = "Sends your question and the displayed program information to Claude.";
        AskButton.SetResourceReference(StyleProperty, "PrimaryButton"); AskButton.Margin = new Thickness(0, 0, 8, 0);
        CancelButton.Content = "Cancel"; CancelButton.ToolTip = "Stops waiting for the current answer."; CancelButton.Visibility = Visibility.Collapsed;
        actions.Children.Add(AskButton); actions.Children.Add(CancelButton); top.Children.Add(actions); top.Children.Add(Status);
        DockPanel.SetDock(top, Dock.Top); layout.Children.Add(top);
        var scroll = new ScrollViewer { Content = Answers, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Read and select the answers and their source references." };
        layout.Children.Add(scroll);
        Content = SecondaryWindowView.Frame(layout);
        AskButton.Click += async (_, _) => { AskTask = AskAsync(); await AskTask; };
        CancelButton.Click += (_, _) => asking?.Cancel();
        Closed += (_, _) => { closed = true; asking?.Cancel(); };
        Loaded += (_, _) => Question.Focus();
    }

    public async Task AskAsync()
    {
        if (asking != null || string.IsNullOrWhiteSpace(Question.Text)) return;
        using var cancellation = new CancellationTokenSource(); asking = cancellation;
        AskButton.IsEnabled = false; Question.IsReadOnly = true; CancelButton.Visibility = Visibility.Visible;
        try
        {
            string? apiKey = key();
            if (string.IsNullOrWhiteSpace(apiKey)) return;
            var question = new HelpQuestion(Question.Text.Trim(), state(), null, earlier.ToArray());
            Status.Text = "Asking Claude...";
            var answer = await assistant.AskAsync(apiKey, question, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (closed) return;
            string text = answer.Outcome == AiOutcome.Success ? answer.Text : outcomeMessage(answer.Outcome);
            var turn = new StackPanel { Margin = new Thickness(0, 8, 0, 12) };
            turn.Children.Add(SecondaryWindowView.Report(question.Text, true, "The question sent for this answer."));
            turn.Children.Add(SecondaryWindowView.Report(text, true, "Select and copy any part of this answer."));
            if (answer.Sources.Count > 0)
                turn.Children.Add(SecondaryWindowView.Report(string.Join("\n", answer.Sources.Select(s => $"{s.Path}, lines {s.FromLine}-{s.ToLine}")),
                    true, "Repository paths and inclusive line ranges read for this answer."));
            Answers.Children.Add(turn);
            if (answer.Outcome == AiOutcome.Success) earlier.Add(new(question.Text, answer.Text));
            Status.Text = "";
        }
        catch (OperationCanceledException) { if (!closed) Status.Text = "Cancelled."; }
        catch (Exception) { if (!closed) Status.Text = outcomeMessage(AiOutcome.Unavailable); }
        finally
        {
            asking = null;
            if (!closed) { AskButton.IsEnabled = true; Question.IsReadOnly = false; CancelButton.Visibility = Visibility.Collapsed; }
        }
    }
}
