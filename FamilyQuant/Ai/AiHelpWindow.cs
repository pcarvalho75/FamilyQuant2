using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Globalization;
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
    public Button ReportButton { get; } = new();
    public StackPanel Answers { get; } = new();
    public TextBlock Status { get; } = SecondaryWindowView.Text("", true);
    public Task? AskTask { get; private set; }
    public CheckBox IncludePicture { get; } = new();
    public Image PicturePreview { get; } = new();
    public TextBox StatePreview { get; } = SecondaryWindowView.Report("", true, "The program information included with your next question.");
    public HelpQuestion? LastQuestion { get; private set; }
    public string LastAnswer { get; private set; } = "";
    public TextBlock UsageNotice { get; } = SecondaryWindowView.Text("Questions go to Claude with your own Anthropic key and count on your Anthropic account. AI Help reads the source through GitHub, which allows 10 code searches a minute and 5,000 file reads an hour for all FamilyQuant users together; when that limit is reached, AI Help says so and when it resets.", true);

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
        IncludePicture.Checked += (_, _) => { RefreshPicture(); Dispatcher.BeginInvoke(() => PicturePreview.BringIntoView()); };
        IncludePicture.Unchecked += (_, _) => { PicturePreview.Source = null; PicturePreview.Visibility = Visibility.Collapsed; };
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        AskButton.Content = "Ask"; AskButton.ToolTip = "Sends your question and the displayed program information to Claude.";
        AskButton.SetResourceReference(StyleProperty, "PrimaryButton"); AskButton.Margin = new Thickness(0, 0, 8, 0);
        CancelButton.Content = "Cancel"; CancelButton.ToolTip = "Stops waiting for the current answer."; CancelButton.Visibility = Visibility.Collapsed;
        ReportButton.Content = "Report this as a bug..."; ReportButton.IsEnabled = false;
        ReportButton.ToolTip = "Review and edit a public GitHub issue about the last question and answer before submitting it yourself.";
        ReportButton.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(AskButton); actions.Children.Add(CancelButton); actions.Children.Add(ReportButton);
        var topScroll = new ScrollViewer { Content = top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, ToolTip = "Review the question, program information and optional picture before asking." };
        SizeChanged += (_, e) => topScroll.MaxHeight = Math.Max(140, e.NewSize.Height - 340);
        var heading = new StackPanel(); heading.Children.Add(topScroll); heading.Children.Add(actions); heading.Children.Add(Status);
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        UsageNotice.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(UsageNotice, Dock.Bottom); layout.Children.Add(UsageNotice);
        var scroll = new ScrollViewer { Content = Answers, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Read and select the answers and their source references." };
        layout.Children.Add(scroll);
        Content = SecondaryWindowView.Frame(layout);
        AskButton.Click += async (_, _) => { AskTask = AskAsync(); await AskTask; };
        CancelButton.Click += (_, _) => asking?.Cancel();
        ReportButton.Click += (_, _) =>
        { if (LastQuestion != null) new AiHelpReportWindow(LastQuestion, LastAnswer) { Owner = this }.ShowDialog(); };
        Closed += (_, _) => { closed = true; asking?.Cancel(); };
        Loaded += (_, _) => { RefreshState(); Question.Focus(); };
        Activated += (_, _) => RefreshState();
    }

    public void RefreshState() { StatePreview.Text = FormatState(state()); StatePreview.ScrollToHome(); }

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
        AskButton.IsEnabled = false; ReportButton.IsEnabled = false; Question.IsReadOnly = true; CancelButton.Visibility = Visibility.Visible;
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
            if (answer.Outcome == AiOutcome.Success && !answer.SourceReadable)
                turn.Children.Add(SecondaryWindowView.Text("This build cannot read the source on GitHub, so this answer relies on the screen and FamilyQuant's own help.", true));
            if (answer.GitHubLimitResetsAt is DateTimeOffset reset)
                turn.Children.Add(SecondaryWindowView.Text("GitHub's limit was reached; reading resumes at "
                    + reset.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) + ".", true));
            Answers.Children.Add(turn);
            // Only Claude's answers go back as earlier turns; an outcome message is the program's text, not Claude's.
            if (answer.Outcome == AiOutcome.Success) earlier.Add(new(question.Text, answer.Text));
            Status.Text = "";
        }
        catch (OperationCanceledException) { if (!closed) Status.Text = "Cancelled."; }
        catch (Exception) { if (!closed) Status.Text = outcomeMessage(AiOutcome.Unavailable); }
        finally
        {
            asking = null;
            if (!closed) { AskButton.IsEnabled = true; ReportButton.IsEnabled = LastQuestion != null; Question.IsReadOnly = false; CancelButton.Visibility = Visibility.Collapsed; }
        }
    }
}

public sealed class AiHelpReportWindow : Window
{
    private const string IssueBase = "https://github.com/pcarvalho75/FamilyQuant2-releases/issues/new?title=";
    private const string ClipboardClosing = "\n\nThe full report is on the clipboard.";
    private readonly byte[]? picture;
    private readonly Action<string> copyReport;
    private readonly Action<string> openAddress;
    private readonly Func<string, bool> openFolder;
    private readonly string reportDirectory;
    public TextBox ReportTitle { get; } = new();
    public TextBox ReportBody { get; } = new();
    public Button OpenButton { get; } = new();
    public TextBlock Status { get; } = SecondaryWindowView.Text("", true);
    public string? SavedPicturePath { get; private set; }

    public AiHelpReportWindow(HelpQuestion question, string answer, Action<string>? copyReport = null,
        Action<string>? openAddress = null, Func<string, bool>? openFolder = null, string? reportDirectory = null)
    {
        this.picture = question.ScreenshotPng;
        this.copyReport = copyReport ?? Clipboard.SetText;
        this.openAddress = openAddress ?? (address => { using var process = Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); });
        this.openFolder = openFolder ?? CrashReportStore.TryOpenFolder;
        this.reportDirectory = reportDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamilyQuant", "AI Help reports");
        Title = "Report this as a bug"; Width = 800; Height = 700; MinWidth = 520; MinHeight = 420;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner; SecondaryWindowView.Theme(this);
        var layout = new DockPanel();
        var heading = new StackPanel();
        var title = SecondaryWindowView.Text(Title); title.SetResourceReference(StyleProperty, "PageTitle"); heading.Children.Add(title);
        heading.Children.Add(SecondaryWindowView.Text("You submit the issue from your own GitHub account, and issues there are public.", true));
        heading.Children.Add(new Label { Content = "Issue title", Target = ReportTitle, ToolTip = "Edit the short title that appears on the public issue." });
        ReportTitle.Text = SafePrefix(question.Text.Replace('\r', ' ').Replace('\n', ' ').Trim(), 200);
        ReportTitle.MaxLength = 200; ReportTitle.ToolTip = "A short description of the problem, initially taken from your question.";
        heading.Children.Add(ReportTitle);
        heading.Children.Add(new Label { Content = "Report", Target = ReportBody, ToolTip = "Review and edit the information you want to share publicly." });
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var footer = new StackPanel();
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        OpenButton.Content = "Open on GitHub"; OpenButton.SetResourceReference(StyleProperty, "PrimaryButton");
        OpenButton.ToolTip = "Copies the full report and opens a filled issue draft in your browser; you submit it yourself.";
        var close = new Button { Content = "Close", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Closes this editable report without submitting an issue." };
        close.Click += (_, _) => Close();
        actions.Children.Add(OpenButton); actions.Children.Add(close); footer.Children.Add(actions); footer.Children.Add(Status);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        ReportBody.Text = BuildBody(question, answer); ReportBody.AcceptsReturn = true; ReportBody.TextWrapping = TextWrapping.Wrap;
        ReportBody.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        ReportBody.ToolTip = "The question, answer and diagnostic information to review before opening the issue draft.";
        layout.Children.Add(ReportBody); Content = SecondaryWindowView.Frame(layout);
        OpenButton.Click += (_, _) => OpenIssue();
    }

    public static string BuildBody(HelpQuestion question, string answer) => $"Question:\n{question.Text}\n\nAnswer:\n{answer}"
        + $"\n\nVersion: {question.State.Version}\nScreen: {question.State.Screen}\n\nLast error:\n{question.State.LastError}\n\nLog tail:\n{question.State.LogTail}";

    private static string SafePrefix(string text, int length)
    {
        length = Math.Min(length, text.Length);
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length];
    }

    public static string BuildIssueAddress(string title, string body)
    {
        string prefix = IssueBase + Uri.EscapeDataString(SafePrefix(title, 200)) + "&body=";
        if (prefix.Length + Uri.EscapeDataString(body).Length < 7000) return prefix + Uri.EscapeDataString(body);
        int low = 0, high = body.Length;
        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;
            if (prefix.Length + Uri.EscapeDataString(SafePrefix(body, middle) + ClipboardClosing).Length < 7000) low = middle;
            else high = middle - 1;
        }
        return prefix + Uri.EscapeDataString(SafePrefix(body, low) + ClipboardClosing);
    }

    public bool OpenIssue()
    {
        try
        {
            copyReport(ReportTitle.Text + "\n\n" + ReportBody.Text);
            if (picture != null)
            {
                Directory.CreateDirectory(reportDirectory);
                SavedPicturePath = Path.Combine(reportDirectory, "report-" + Guid.NewGuid().ToString("N") + ".png");
                File.WriteAllBytes(SavedPicturePath, picture);
                if (!openFolder(reportDirectory)) { Status.Text = "The picture was saved, but its folder could not be opened: " + reportDirectory; return false; }
            }
            openAddress(BuildIssueAddress(ReportTitle.Text, ReportBody.Text));
            Status.Text = picture == null ? "Report copied. Finish and submit the issue in your browser."
                : "Report copied. Drag the saved picture into the issue in your browser, then submit it.";
            return true;
        }
        catch (Exception) { Status.Text = "Could not prepare or open the issue. Try again."; return false; }
    }
}
