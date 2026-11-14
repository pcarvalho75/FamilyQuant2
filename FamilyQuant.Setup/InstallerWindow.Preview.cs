using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Setup;

/// <summary>Off-screen UI proof. This entry constructs only an inert controller, never the real engine.</summary>
public static partial class InstallerWindowPreview
{
    /// <summary>Safe focused tests for the ordinary suite; STA thread, fake controller only, no files written.</summary>
    public static IReadOnlyList<string> CheckInteractions()
    {
        SetupToolTipDefaults.Apply();
        var previousContext = SynchronizationContext.Current;
        var app = Application.Current ?? new Application();
        var previousShutdown = app.ShutdownMode;
        try
        {
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var checks = new List<string>();
            CheckInteractions(checks);
            return checks;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            app.ShutdownMode = previousShutdown;
        }
    }

    public static int Run(string outputDirectory)
    {
        SetupToolTipDefaults.Apply();
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        var previousContext = SynchronizationContext.Current;
        var app = Application.Current ?? new Application();
        var previousShutdown = app.ShutdownMode;
        try
        {
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            CheckInteractions(checks);
            foreach (var dark in new[] { true, false })
            foreach (var screen in Screens)
            {
                var controller = new PreviewController
                {
                    Existing = screen is "older" or "confirm-older" ? new(PreviewController.Folder, "2.1.19.0", true, true)
                        : screen == "equal" ? new(PreviewController.Folder, "2.1.4.0", true, true)
                        : screen.StartsWith("upgrade", StringComparison.Ordinal) || screen.StartsWith("uninstall", StringComparison.Ordinal)
                        ? new ExistingInstallation(PreviewController.Folder, "2.1.2", true, true) : null,
                    PayloadVersion = screen is "older" or "equal" or "confirm-older" ? "2.1.4.0" : "2.1.3",
                    Options = new SetupOptions { Uninstall = screen.StartsWith("uninstall", StringComparison.Ordinal) }
                };
                var window = new InstallerWindow(controller);
                window.ShowPreview(screen, dark);
                var surface = (FrameworkElement)window.Content;
                surface.Measure(new Size(680, 600));
                surface.Arrange(new Rect(0, 0, 680, 600));
                surface.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                var bitmap = new RenderTargetBitmap(1020, 900, 144, 144, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output, screen + (dark ? "-dark.png" : "-light.png")))) encoder.Save(stream);
                Require(controller.Installs == 0 && controller.Uninstalls == 0 && controller.Launches == 0, "render is inert");
                window.Close();
            }
            File.WriteAllLines(Path.Combine(output, "ui-checks.txt"), checks.Concat(new[] { $"PASS: {checks.Count} interaction checks; {Screens.Length * 2} renders; fake controllers only." }));
            Console.WriteLine($"PASS M7j: {checks.Count} interaction checks, {Screens.Length * 2} off-screen renders. No machine integration.");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "ui-failure.txt"), error.ToString());
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            app.ShutdownMode = previousShutdown;
        }
    }

    private static readonly string[] Screens =
    {
        "welcome", "folder", "options", "progress", "done", "running-app", "error",
        "uninstall", "uninstall-progress", "uninstall-done", "upgrade", "upgrade-folder", "upgrade-progress", "canceled",
        "older", "equal", "confirm-older"
    };
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("M7j UI check failed: " + description);
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string Tip(FrameworkElement element)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        return element.ToolTip as string ?? "";
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("M7j fake UI operation did not complete.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(1);
        }
    }
    private static void CheckInteractions(List<string> checks)
    {
        void Check(bool condition, string description) { Require(condition, description); checks.Add("PASS " + description); }
        var fresh = new PreviewController();
        var window = new InstallerWindow(fresh);
        Check(window.Icon is BitmapSource { PixelWidth: > 0 }, "Setup assembly icon loads independently of its executable host");
        Check(window.Title == "FamilyQuant Setup", "Setup window identifies itself in the Windows title");
        Check(window.TitleBrand.Source is BitmapSource { PixelWidth: > 0 }, "Setup assembly title brand image loads independently of its host");
        Check(window.WelcomeBrand.Source is BitmapSource { PixelWidth: > 0 }, "Setup assembly welcome brand image loads independently of its host");
        Check(fresh.Installs == 0 && fresh.Uninstalls == 0 && fresh.Launches == 0, "construction never installs, uninstalls or launches");
        Check(window.FolderBox.Text == fresh.DefaultDirectory, "new install uses the controller default folder");
        Check(window.ThemeButton.Content.Equals("Light"), "dark is the default and light is available");
        Check(new FrameworkElement[] { window.PrimaryButton, window.FolderBox, window.DesktopOption, window.ErrorDetails }
            .All(element => ToolTipService.GetInitialShowDelay(element) == SetupToolTipDefaults.InitialDelayMilliseconds
                && ToolTipService.GetShowDuration(element) == SetupToolTipDefaults.ShowDurationMilliseconds
                && ToolTipService.GetShowOnDisabled(element)), "Setup tooltip defaults are 500 ms, 20 seconds and visible on disabled controls");
        foreach (bool dark in new[] { true, false })
        {
            window.ShowPreview("welcome", dark);
            var style = (Style)window.FindResource(typeof(ToolTip));
            var tip = new ToolTip { Style = style, Resources = window.Resources };
            var template = (DataTemplate)style.Resources[new DataTemplateKey(typeof(string))];
            Check(tip.MaxWidth == SetupToolTipDefaults.MaximumWidth && tip.FontSize == 12
                && ReferenceEquals(tip.Background, window.FindResource("SurfaceBrush"))
                && ReferenceEquals(tip.Foreground, window.FindResource("TextBrush"))
                && ReferenceEquals(tip.BorderBrush, window.FindResource("EdgeBrush"))
                && template.LoadContent() is TextBlock { TextWrapping: TextWrapping.Wrap },
                "Setup tooltip style wraps at 380 px and follows the current appearance tokens, dark=" + dark);
        }
        window.ShowPreview("welcome", true);
        Check(Tip(window.PrimaryButton).Contains("folder page", StringComparison.Ordinal), "welcome tooltip describes the next page before any installation");
        Click(window.PrimaryButton);
        Check(Tip(window.FolderBox).Contains(fresh.DefaultDirectory, StringComparison.Ordinal)
            && Tip(window.PrimaryButton).Contains("full folder path", StringComparison.Ordinal), "folder tooltips give the actual default path and current action");
        window.FolderBox.Text = "relative-folder";
        Click(window.PrimaryButton);
        Check(window.FolderPanel.Visibility == Visibility.Visible && fresh.Installs == 0, "relative folder is refused before engine call");
        window.FolderBox.Text = @"D:\Apps\FamilyQuant2";
        Click(window.PrimaryButton);
        Check(window.OptionsPanel.Visibility == Visibility.Visible, "folder continues to options");
        Check(Tip(window.PrimaryButton).Contains("Windows registration", StringComparison.Ordinal)
            && Tip(window.PrimaryButton).Contains("Undo", StringComparison.Ordinal), "options tooltip explains installation effects and Undo scope");
        Check(window.FolderNote.Text == "Only your Windows account uses this installation.", "valid folder clears its earlier validation warning");
        window.DesktopOption.IsChecked = true;
        var install = window.ExecuteOperationAsync();
        PumpUntil(() => install.IsCompleted);
        install.GetAwaiter().GetResult();
        Check(fresh.Installs == 1 && fresh.LastDirectory == @"D:\Apps\FamilyQuant2" && fresh.Desktop, "chosen folder and desktop option reach the controller");
        Check(window.DonePanel.Visibility == Visibility.Visible && fresh.Launches == 0, "success shows done without automatic launch");
        Check(Tip(window.PrimaryButton).StartsWith("Opens FamilyQuant", StringComparison.Ordinal), "completion tooltip follows the final launch action");
        Click(window.PrimaryButton);
        Check(fresh.Launches == 1 && window.ExitCode == 0, "done launches only after explicit choice");

        var running = new PreviewController { Running = true };
        window = new InstallerWindow(running);
        var refusal = window.ExecuteOperationAsync();
        refusal.GetAwaiter().GetResult();
        Check(running.Installs == 0 && window.ExitCode == 3 && window.ErrorMessage.Text == "FamilyQuant is running, close it to continue.", "running app is refused before any install call");
        running.Running = false;
        var retry = window.ExecuteOperationAsync(); PumpUntil(() => retry.IsCompleted); retry.GetAwaiter().GetResult();
        Check(running.Installs == 1 && window.DonePanel.Visibility == Visibility.Visible, "running-app retry works after app closes");
        window.LaunchOption.IsChecked = false; Click(window.PrimaryButton);
        Check(running.Launches == 0, "disabled launch option never launches");

        var upgrade = new PreviewController { Existing = new ExistingInstallation(PreviewController.Folder, "2.1.2", true, true) };
        window = new InstallerWindow(upgrade);
        Check(window.PageTitle.Text == "Upgrade FamilyQuant 2.1.2 to 2.1.3" && window.FolderBox.IsReadOnly && !window.ChangeFolderButton.IsEnabled, "upgrade names both versions and keeps the existing folder");
        Check(Tip(window.ChangeFolderButton).Contains("Unavailable", StringComparison.Ordinal)
            && Tip(window.ChangeFolderButton).Contains("fresh installation", StringComparison.Ordinal), "disabled folder tooltip explains the reason and enabling condition");
        window.Close();
        CheckVersionChoices(checks);
        CheckFailuresAndCancellation(checks);
    }

    private static void CheckFailuresAndCancellation(List<string> checks)
    {
        void Check(bool condition, string description) { Require(condition, description); checks.Add("PASS " + description); }
        var failed = new PreviewController { Fail = true };
        var window = new InstallerWindow(failed);
        var operation = window.ExecuteOperationAsync(); PumpUntil(() => operation.IsCompleted); operation.GetAwaiter().GetResult();
        Check(window.ErrorPanel.Visibility == Visibility.Visible && window.ErrorMessage.Text == "No files were changed. Try a different folder.", "engine failure message remains clear and visible");
        Check(failed.Launches == 0, "failed install cannot launch");
        Click(window.BackButton);
        Check(window.OptionsPanel.Visibility == Visibility.Visible, "failure returns to options so the folder can be changed");
        Click(window.BackButton);
        Check(window.FolderPanel.Visibility == Visibility.Visible, "failure recovery can reach the folder chooser");
        window.Close();

        var remove = new PreviewController
        {
            Existing = new ExistingInstallation(PreviewController.Folder, "2.1.2", true, true),
            Options = new SetupOptions { Uninstall = true }
        };
        window = new InstallerWindow(remove);
        Check(window.UninstallPanel.Visibility == Visibility.Visible && remove.Uninstalls == 0, "uninstall requires an explicit confirmation action");
        Check(Tip(window.PrimaryButton).StartsWith("Removes the installed app", StringComparison.Ordinal), "uninstall tooltip describes removal before confirmation");
        operation = window.ExecuteOperationAsync(); PumpUntil(() => operation.IsCompleted); operation.GetAwaiter().GetResult();
        Check(remove.Uninstalls == 1 && remove.Installs == 0 && window.PageTitle.Text == "FamilyQuant is uninstalled", "uninstall calls only its controller operation and shows completion");
        Click(window.PrimaryButton);
        Check(remove.Launches == 0, "uninstall completion never launches");

        var held = new PreviewController { Hold = true };
        window = new InstallerWindow(held);
        operation = window.ExecuteOperationAsync();
        var duplicate = window.ExecuteOperationAsync();
        Check(held.Installs == 1 && duplicate.IsCompleted, "duplicate install gestures cannot start a second operation");
        PumpUntil(() => window.InstallProgress.Value == 50);
        Check(window.ProgressStage.Text == "Extracting app files" && window.ProgressPercent.Text == "50%", "controller callback drives stage and progress");
        Check(Tip(window.CancelButton).StartsWith("Requests cancellation", StringComparison.Ordinal)
            && Tip(window.CloseButton).Contains("already committed", StringComparison.Ordinal), "active operation tooltips describe cancellation and a completed change");
        Click(window.CancelButton); PumpUntil(() => operation.IsCompleted); operation.GetAwaiter().GetResult();
        Check(held.Canceled && window.PageTitle.Text == "Setup was canceled", "cancel reaches the engine token and exposes retry");
        Check(Tip(window.PrimaryButton).StartsWith("Retries", StringComparison.Ordinal)
            && Tip(window.CancelButton).StartsWith("Closes", StringComparison.Ordinal), "cancellation refreshes retry and close tooltips after the operation stops");
        window.Close();

        held = new PreviewController { Hold = true, ReturnCanceledResult = true };
        window = new InstallerWindow(held);
        operation = window.ExecuteOperationAsync(); Click(window.CancelButton);
        PumpUntil(() => operation.IsCompleted); operation.GetAwaiter().GetResult();
        Check(window.PageTitle.Text == "Setup was canceled" && window.ErrorMessage.Text == "Setup cancelled. Previous program files were kept.", "controller canceled result keeps its message and canceled title");
        window.Close();

        held = new PreviewController { Hold = true };
        window = new InstallerWindow(held);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        operation = window.ExecuteOperationAsync(); window.Close();
        PumpUntil(() => operation.IsCompleted); operation.GetAwaiter().GetResult();
        Check(held.Canceled && closed, "closing an active operation waits for its cancellation");

        var launchFailure = new PreviewController { LaunchThrows = true };
        window = new InstallerWindow(launchFailure);
        operation = window.ExecuteOperationAsync(); PumpUntil(() => operation.IsCompleted); operation.GetAwaiter().GetResult();
        Click(window.PrimaryButton);
        Check(window.PageTitle.Text == "FamilyQuant could not open" && launchFailure.Installs == 1, "launch failure preserves successful installation");
        Click(window.PrimaryButton);
        Check(launchFailure.Installs == 1 && launchFailure.Launches == 1, "finish after launch failure does not reinstall or relaunch");
    }

    private sealed class PreviewController : ISetupController
    {
        public const string Folder = @"C:\Users\User\AppData\Local\Programs\FamilyQuant2";
        public SetupOptions Options { get; init; } = new();
        public string DefaultDirectory => Folder;
        public string PayloadVersion { get; init; } = "2.1.3";
        public ExistingInstallation? Existing { get; init; }
        public ExistingInstallation? DetectExisting() => Existing;
        public bool Running { get; set; }
        public bool Hold { get; init; }
        public bool Fail { get; init; }
        public bool LaunchThrows { get; init; }
        public bool ReturnCanceledResult { get; init; }
        public bool Canceled { get; private set; }
        public int Installs { get; private set; }
        public int Uninstalls { get; private set; }
        public int Launches { get; private set; }
        public string? LastDirectory { get; private set; }
        public bool Desktop { get; private set; }
        public bool LastAllowOlderVersion { get; private set; }
        public bool IsAppRunning(string directory) => Running;
        public async Task<SetupResult> InstallAsync(string directory, bool desktopShortcut, IProgress<SetupProgress>? progress = null,
            CancellationToken cancellationToken = default, bool allowOlderVersion = false)
        {
            Installs++; LastDirectory = directory; Desktop = desktopShortcut; LastAllowOlderVersion = allowOlderVersion;
            return await Finish(directory, progress, cancellationToken);
        }
        public async Task<SetupResult> UninstallAsync(string directory, IProgress<SetupProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Uninstalls++; LastDirectory = directory;
            return await Finish(directory, progress, cancellationToken);
        }
        private async Task<SetupResult> Finish(string directory, IProgress<SetupProgress>? progress, CancellationToken token)
        {
            progress?.Report(new SetupProgress("Extracting app files", 1, 2));
            try
            {
                if (Hold) await Task.Delay(Timeout.Infinite, token);
                else await Task.Yield();
                return Fail ? new SetupResult(false, 1, "No files were changed. Try a different folder.") : new SetupResult(true, 0, "Complete", directory);
            }
            catch (OperationCanceledException)
            {
                Canceled = true;
                if (ReturnCanceledResult) return new SetupResult(false, 1, "Setup cancelled. Previous program files were kept.", directory);
                throw;
            }
        }
        public void Launch(string directory)
        {
            Launches++;
            if (LaunchThrows) throw new InvalidOperationException("Preview launch failure.");
        }
    }
}

public partial class InstallerWindow
{
    internal void ShowPreview(string screen, bool dark)
    {
        SetTheme(dark);
        ShowPage(screen switch
        {
            "welcome" or "upgrade" or "older" or "equal" => SetupPage.Welcome,
            "confirm-older" => SetupPage.ConfirmOlder,
            "folder" or "upgrade-folder" => SetupPage.Folder,
            "options" => SetupPage.Options,
            "progress" or "upgrade-progress" or "uninstall-progress" => SetupPage.Progress,
            "done" or "uninstall-done" => SetupPage.Done,
            "uninstall" => SetupPage.Uninstall,
            _ => SetupPage.Error
        });
        if (screen.EndsWith("progress", StringComparison.Ordinal))
            ApplyProgress(new SetupProgress(screen == "uninstall-progress" ? "Removing app files" : "Extracting app files", 148, 240));
        if (screen == "running-app") ShowError("Close FamilyQuant to continue", "FamilyQuant is running, close it to continue.");
        if (screen == "error") ShowError("Setup could not finish", "The install folder cannot be written. Choose a different folder and try again.");
        if (screen == "canceled") ShowError("Setup was canceled", "You can try again when you are ready.");
        if (screen == "uninstall-done") { _removed = true; ShowPage(SetupPage.Done); }
    }
}
