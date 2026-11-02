using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

/// <summary>Native dialog boundary. A disconnected visual never opens a native dialog.</summary>
public static class ShellDialogs
{
    public static Func<Window?, string, FileDialog, bool?>? FilePresenter { get; set; }
    public static Func<Window?, string, OpenFolderDialog, bool?>? FolderPresenter { get; set; }

    public static MainWindow? FindShell(DependencyObject? origin)
    {
        Window? window = origin as Window ?? (origin == null ? null : Window.GetWindow(origin));
        for (; window != null; window = window.Owner)
            if (window is MainWindow shell) return shell;
        return null;
    }

    internal static bool IsDisplayed(DependencyObject? origin) => origin is Visual visual && PresentationSource.FromVisual(visual) != null;
    private static string SettingsPath => MainWindow.ShellSettingsPathOverride ?? Path.Combine(ApplicationPaths.LocalDataDirectory, "shell.json");
    private static bool PersistStandalone => MainWindow.ShellSettingsPathOverride != null ||
        System.Reflection.Assembly.GetEntryAssembly() == typeof(MainWindow).Assembly;
    private static string? PreferredFolder(DependencyObject origin)
    {
        if (FindShell(origin) is MainWindow shell) return shell.PreferredFolder;
        return PersistStandalone ? ShellSessionState.Load(SettingsPath).LastFolder : null;
    }

    public static bool ShowFile(DependencyObject origin, string id, FileDialog dialog)
    {
        string? folder = PreferredFolder(origin);
        if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (!IsDisplayed(origin)) return false;
        Window? owner = origin as Window ?? Window.GetWindow(origin);
        bool accepted = (FilePresenter != null ? FilePresenter(owner, id, dialog)
            : owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true;
        if (accepted) Remember(origin, dialog.FileName);
        return accepted;
    }

    public static bool ShowFolder(DependencyObject origin, string id, OpenFolderDialog dialog)
    {
        string? folder = PreferredFolder(origin);
        if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (!IsDisplayed(origin)) return false;
        Window? owner = origin as Window ?? Window.GetWindow(origin);
        bool accepted = (FolderPresenter != null ? FolderPresenter(owner, id, dialog)
            : owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true;
        if (accepted) Remember(origin, dialog.FolderName);
        return accepted;
    }

    private static void Remember(DependencyObject origin, string path)
    {
        if (FindShell(origin) is MainWindow shell) { shell.RememberFolder(path); return; }
        if (!PersistStandalone || string.IsNullOrWhiteSpace(path)) return;
        string? folder = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path));
        if (!Directory.Exists(folder)) return;
        var state = ShellSessionState.Load(SettingsPath);
        state.LastFolder = folder;
        string? error = state.Save(SettingsPath);
        if (error != null) ShellMessages.Report(origin, error, "Application preferences", showDialog: false);
    }
}

public sealed record ShellMessage(string Text, string Title, MessageBoxButton Buttons, MessageBoxImage Image, MessageBoxResult DefaultResult);

/// <summary>One message enters the status/help record before any optional modal presentation.</summary>
public static class ShellMessages
{
    public static Func<Window?, ShellMessage, MessageBoxResult>? Presenter { get; set; }
    public static event Action<ShellMessage>? Reported;
    public static MessageBoxResult Report(DependencyObject? origin, string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information,
        MessageBoxResult defaultResult = MessageBoxResult.None, Action<string>? status = null, bool showDialog = true)
    {
        status?.Invoke(message);
        ShellDialogs.FindShell(origin)?.StatusModel.ShowMessage(message);
        var request = new ShellMessage(message, title, buttons, image, defaultResult);
        Reported?.Invoke(request);
        if (!showDialog || !ShellDialogs.IsDisplayed(origin))
            return buttons == MessageBoxButton.OK ? MessageBoxResult.OK
                : buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
        Window? owner = origin as Window ?? Window.GetWindow(origin!);
        if (Presenter != null) return Presenter(owner, request);
        return owner == null ? MessageBox.Show(message, title, buttons, image, defaultResult)
            : MessageBox.Show(owner, message, title, buttons, image, defaultResult);
    }
}
