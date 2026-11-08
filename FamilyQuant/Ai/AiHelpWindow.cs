using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Ai;

public sealed class AiHelpWindow : Window
{
    private readonly IHelpAssistant assistant;
    private readonly Func<string?> key;
    private readonly Func<HelpState> state;
    private readonly Func<AiOutcome, string> outcomeMessage;
    private readonly Func<byte[]?> screenshot;
    private readonly List<HelpTurn> earlier = new();
    private CancellationTokenSource? asking;
    private bool closed;
    public TextBox Question { get; } = new();
    public Button AskButton { get; } = new();
    public Button CancelButton { get; } = new();
    public StackPanel Answers { get; } = new();
    public TextBlock Status { get; } = SecondaryWindowView.Text("", true);
    public Task? AskTask { get; private set; }
    public CheckBox IncludePicture { get; } = new();
    public Image PicturePreview { get; } = new();
    public TextBox StatePreview { get; } = SecondaryWindowView.Report("", true, "The program information included with your next question.");
    public HelpQuestion? LastQuestion { get; private set; }
    public string LastAnswer { get; private set; } = "";

    public AiHelpWindow(IHelpAssistant assistant, Func<string?> key, Func<HelpState> state,
        Func<AiOutcome, string> outcomeMessage, Func<byte[]?>? screenshot = null)
    {
        this.assistant = assistant; this.key = key; this.state = state; this.outcomeMessage = outcomeMessage;
        this.screenshot = screenshot ?? (() => null);
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
        StatePreview.MaxHeight = 150;
        var details = new Expander { Header = "What AI Help sends", Content = StatePreview, Margin = new Thickness(0, 10, 0, 8),
            ToolTip = "Review the version, screen, project summary and recent diagnostics before asking." };
        details.Expanded += (_, _) => RefreshState(); top.Children.Add(details);
        IncludePicture.Content = "Include a picture of the FamilyQuant window";
        IncludePicture.ToolTip = "Adds a PNG of the window from which you opened AI Help; review its preview before asking.";
        top.Children.Add(IncludePicture);
        PicturePreview.MaxWidth = 180; PicturePreview.MaxHeight = 100; PicturePreview.HorizontalAlignment = HorizontalAlignment.Left;
        PicturePreview.Margin = new Thickness(0, 8, 0, 0); PicturePreview.Stretch = Stretch.Uniform;
        PicturePreview.ToolTip = "Preview of the optional picture; a fresh picture is taken when you ask.";
        PicturePreview.Visibility = Visibility.Collapsed; top.Children.Add(PicturePreview);
        IncludePicture.Checked += (_, _) => RefreshPicture();
        IncludePicture.Unchecked += (_, _) => { PicturePreview.Source = null; PicturePreview.Visibility = Visibility.Collapsed; };
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
        Loaded += (_, _) => { RefreshState(); Question.Focus(); };
        Activated += (_, _) => RefreshState();
    }

    public void RefreshState() => StatePreview.Text = FormatState(state());

    public static string FormatState(HelpState value) => $"Version: {value.Version}\nScreen: {value.Screen}\n\nScreen help:\n{value.ScreenHelp}"
        + $"\n\nProject:\n{value.ProjectSummary}\n\nLast error:\n{value.LastError}\n\nLog tail:\n{value.LogTail}";

    private byte[]? RefreshPicture()
    {
        byte[]? png = screenshot();
        if (png == null) { PicturePreview.Source = null; PicturePreview.Visibility = Visibility.Collapsed; return null; }
        using var stream = new MemoryStream(png);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); PicturePreview.Source = bitmap;
        PicturePreview.Visibility = Visibility.Visible;
        return png;
    }

    public static byte[]? CaptureWindow(Window? window)
    {
        if (window == null || !window.IsVisible || window.ActualWidth <= 0 || window.ActualHeight <= 0) return null;
        window.UpdateLayout();
        double scale = Math.Min(1, 1600 / Math.Max(window.ActualWidth, window.ActualHeight));
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Floor(window.ActualWidth * scale)),
            Math.Max(1, (int)Math.Floor(window.ActualHeight * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
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
            var currentState = state(); StatePreview.Text = FormatState(currentState);
            var question = new HelpQuestion(Question.Text.Trim(), currentState,
                IncludePicture.IsChecked == true ? RefreshPicture() : null, earlier.ToArray());
            Status.Text = "Asking Claude...";
            var answer = await assistant.AskAsync(apiKey, question, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (closed) return;
            string text = answer.Outcome == AiOutcome.Success ? answer.Text : outcomeMessage(answer.Outcome);
            LastQuestion = question; LastAnswer = text;
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
