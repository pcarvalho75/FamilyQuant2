using FamilyQuant.Ai;
using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace FamilyQuant;

public partial class MainWindow
{
    public static readonly RoutedUICommand AskAiHelpCommand = new("Ask AI Help", "AskAiHelp", typeof(MainWindow),
        new InputGestureCollection { new KeyGesture(Key.H, ModifierKeys.Control) });
    public static Func<IHelpAssistant>? HelpAssistantOverride { get; set; }
    public static string? HelpLogDirectoryOverride { get; set; }
    public AiHelpWindow? AiHelp { get; private set; }
    private Window? aiHelpOrigin;

    static MainWindow()
    {
        CommandManager.RegisterClassInputBinding(typeof(Window), new KeyBinding(AskAiHelpCommand, (KeyGesture)AskAiHelpCommand.InputGestures[0]));
        var binding = new CommandBinding(AskAiHelpCommand,
            (sender, e) => { FindHelpShell(sender as DependencyObject)?.OpenAiHelp(sender as Window); e.Handled = true; },
            (sender, e) => { e.CanExecute = FindHelpShell(sender as DependencyObject) != null; e.Handled = true; });
        binding.PreviewCanExecute += (sender, e) =>
        { e.CanExecute = FindHelpShell(sender as DependencyObject) != null; e.Handled = true; };
        CommandManager.RegisterClassCommandBinding(typeof(Window), binding);
    }

    private static MainWindow? FindHelpShell(DependencyObject? origin) => ShellDialogs.FindShell(origin)
        ?? Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();

    private void MenuItemAiHelp_Click(object sender, RoutedEventArgs e) => OpenAiHelp(this);

    public AiHelpWindow OpenAiHelp(Window? origin = null)
    {
        if (origin != null && origin != AiHelp) aiHelpOrigin = origin;
        if (AiHelp == null)
        {
            var window = new AiHelpWindow(HelpAssistantOverride?.Invoke() ?? new HelpAssistant(), HelpKey,
                CaptureHelpState, HelpOutcomeMessage, () => AiHelpWindow.CaptureWindow(aiHelpOrigin is { IsVisible: true } ? aiHelpOrigin : this));
            AiHelp = window;
            window.Owner = origin is { IsVisible: true } ? origin : this;
            window.Closed += (_, _) => AiHelp = null;
            window.Show();
        }
        if (AiHelp.WindowState == WindowState.Minimized) AiHelp.WindowState = WindowState.Normal;
        AiHelp.Activate();
        return AiHelp;
    }

    public HelpState CaptureHelpState()
    {
        string screen = IsQuantifyVisible ? WorkflowNames.Quantify : home != null ? WorkflowNames.Home
            : analytics != null ? WorkflowNames.Results : dnc != null ? WorkflowNames.DeNovoReview
            : cgc != null && GridApp.Children.Contains(cgc) ? WorkflowNames.CurateFamilies
            : usearch != null ? WorkflowNames.BuildFamilies : WorkflowNames.Home;
        var originHost = aiHelpOrigin == null ? null : AppOperationHost.GetScope(aiHelpOrigin);
        var entries = Operations.LogEntries.Concat(originHost == null || originHost == Operations
            ? Array.Empty<OperationLogEntry>() : originHost.LogEntries).OrderBy(e => e.Timestamp).ToArray();
        var error = entries.LastOrDefault(e => e.Stage == "operation-failure");
        string lastError = error?.Message ?? "";
        DateTimeOffset errorAt = error?.Timestamp ?? DateTimeOffset.MinValue;
        DateTimeOffset logAt = entries.LastOrDefault()?.Timestamp ?? DateTimeOffset.MinValue;
        var host = originHost != null && (originHost.LogEntries.LastOrDefault()?.Timestamp ?? DateTimeOffset.MinValue)
            > (Operations.LogEntries.LastOrDefault()?.Timestamp ?? DateTimeOffset.MinValue)
            ? originHost : Operations;
        string log = host.LogText;
        try
        {
            string operationDirectory = HelpLogDirectoryOverride ?? Path.Combine(ApplicationPaths.LocalDataDirectory, "operation-logs");
            if (Directory.Exists(operationDirectory))
                foreach (var saved in new DirectoryInfo(operationDirectory).EnumerateFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc))
                {
                    if (saved.LastWriteTimeUtc < errorAt.UtcDateTime && saved.LastWriteTimeUtc < logAt.UtcDateTime) break;
                    string[] tail = File.ReadLines(saved.FullName).TakeLast(40).ToArray();
                    if (tail.Length > 0 && saved.LastWriteTimeUtc > logAt.UtcDateTime)
                    { log = string.Join("\n", tail); logAt = saved.LastWriteTimeUtc; }
                    string? failure = File.ReadLines(saved.FullName).LastOrDefault(l => l.Contains("\toperation-failure\t", StringComparison.Ordinal));
                    string[] parts = failure?.Split('\t', 5) ?? Array.Empty<string>();
                    if (parts.Length == 5 && DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) && at > errorAt)
                    { lastError = parts[4]; errorAt = at; }
                }
            // Error logs contain diagnostic stacks, with exception messages and project payloads omitted by the writer.
            var file = Directory.Exists(CrashReportStore.DefaultDirectory)
                ? new DirectoryInfo(CrashReportStore.DefaultDirectory).EnumerateFiles("crash-*.txt").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
            if (file != null && file.LastWriteTimeUtc > errorAt.UtcDateTime)
            {
                string[] lines = File.ReadAllLines(file.FullName);
                lastError = string.Join("\n", lines.Where(l => l.StartsWith("Type:", StringComparison.Ordinal)
                    || l.StartsWith("Operation:", StringComparison.Ordinal) || l.StartsWith("Origin:", StringComparison.Ordinal)));
                if (file.LastWriteTimeUtc > logAt.UtcDateTime) log = string.Join("\n", lines);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new(typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "", screen, CurrentContextHelp(),
            cgc == null ? "No project open" : ProjectStatusText() + "\n" + StatusModel.Text,
            lastError, LastHelpLogLines(log));
    }

    public static string LastHelpLogLines(string log) => string.Join("\n", log.Replace("\r\n", "\n", StringComparison.Ordinal)
        .TrimEnd('\r', '\n').Split('\n').TakeLast(40));

    private string? HelpKey()
    {
        aiKeyOffer ??= new AiKeyOffer(AiKeyStoreOverride?.Invoke() ?? new AiKeyStore());
        string? key = aiKeyOffer.EnsureKey(lastFcfPath, () => ShellMessages.Report(AiHelp,
            "A key file is beside this project. Import it for this Windows user? The file will remain in place.",
            "AI assistant key", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
        if (!string.IsNullOrWhiteSpace(key)) return key;
        ShellMessages.Report(AiHelp, AiDraftMessage(AiWorkflowOutcome.MissingKey), "AI assistant key");
        new AiKeySettingsWindow(AiKeyStoreOverride?.Invoke() ?? new AiKeyStore()) { Owner = AiHelp }.ShowDialog();
        return null;
    }

    public static string HelpOutcomeMessage(AiOutcome outcome) => AiDraftMessage(
        Enum.TryParse<AiWorkflowOutcome>(outcome.ToString(), out var value) ? value : AiWorkflowOutcome.Unavailable);
}
