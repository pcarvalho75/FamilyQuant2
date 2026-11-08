using FamilyQuant.Ai;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace FamilyQuant;

public partial class MainWindow
{
    public static readonly RoutedUICommand AskAiHelpCommand = new("Ask AI Help", "AskAiHelp", typeof(MainWindow),
        new InputGestureCollection { new KeyGesture(Key.H, ModifierKeys.Control) });
    public static Func<IHelpAssistant>? HelpAssistantOverride { get; set; }
    public AiHelpWindow? AiHelp { get; private set; }

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
        if (AiHelp == null)
        {
            var window = new AiHelpWindow(HelpAssistantOverride?.Invoke() ?? new HelpAssistant(), HelpKey,
                () => new HelpState(typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "", WorkflowNames.Home,
                    CurrentContextHelp(), ProjectStatusText(), "", ""), HelpOutcomeMessage);
            AiHelp = window;
            window.Owner = origin is { IsVisible: true } ? origin : this;
            window.Closed += (_, _) => AiHelp = null;
            window.Show();
        }
        if (AiHelp.WindowState == WindowState.Minimized) AiHelp.WindowState = WindowState.Normal;
        AiHelp.Activate();
        return AiHelp;
    }

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
