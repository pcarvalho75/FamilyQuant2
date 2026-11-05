using FamilyQuant.Operations;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    private readonly CancellationTokenSource updateWindowLifetime = new();
    private bool startupUpdateScheduled;
    private ReleaseUpdateResult? availableUpdate;
    public TimeSpan UpdateCheckStartupDelay { get; set; } = TimeSpan.FromSeconds(3);
    public Task StartupUpdateCheckTask { get; private set; } = Task.CompletedTask;

    private void InitializeUpdateChecks()
    {
        MenuItemAutomaticUpdateChecks.IsChecked = shellState.CheckForUpdatesAtStartup;
        ContentRendered += (_, _) =>
        {
            if (startupUpdateScheduled) return;
            startupUpdateScheduled = true;
            if (System.Reflection.Assembly.GetEntryAssembly() == typeof(MainWindow).Assembly)
                ShowUpdateCompletionNotice(AppContext.BaseDirectory, AppVersionInfo.Current());
            // Harness-created windows cannot make live requests, even when testing saved preferences.
            if (System.Reflection.Assembly.GetEntryAssembly() != typeof(MainWindow).Assembly && UpdateCheckHandlerOverride == null) return;
            StartupUpdateCheckTask = CheckForUpdatesAtStartupAsync();
        };
        Closed += (_, _) => updateWindowLifetime.Cancel();
    }

    public bool ShowUpdateCompletionNotice(string directory, string currentVersion)
    {
        string? message = Deployment.UpdateCompletionMarker.Consume(directory, currentVersion);
        if (message == null) return false;
        StatusModel.ShowMessage(message);
        return true;
    }

    private async Task CheckForUpdatesAtStartupAsync()
    {
        if (!shellState.CheckForUpdatesAtStartup) return;
        try
        {
            await Task.Delay(UpdateCheckStartupDelay, updateWindowLifetime.Token);
            if (!shellState.CheckForUpdatesAtStartup) return;
            var result = await ReleaseUpdateChecker.CheckAsync(AppVersionInfo.ReleaseRepository, AppVersionInfo.Current(),
                new OperationContext(updateWindowLifetime.Token), UpdateCheckHandlerOverride);
            if (!updateWindowLifetime.IsCancellationRequested && shellState.CheckForUpdatesAtStartup && result.Status == ReleaseUpdateStatus.Newer)
                ShowUpdateBanner(result);
            await LogStartupUpdateAsync(result.Status.ToString());
        }
        catch (OperationCanceledException) when (updateWindowLifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            // Never turn a background check into a dialog or an unhandled async event exception.
            await LogStartupUpdateAsync("Unavailable (" + error.GetType().Name + ")");
        }
    }

    private async Task LogStartupUpdateAsync(string outcome)
    {
        string line = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\tStartup update check: " + outcome + Environment.NewLine;
        Trace.WriteLine(line);
        if (!persistShellSettings) return;
        try
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(shellSettingsPath))!;
            Directory.CreateDirectory(folder);
            await File.AppendAllTextAsync(Path.Combine(folder, "update-check.log"), line).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Trace.WriteLine(error.Message); }
    }

    private void ShowUpdateBanner(ReleaseUpdateResult result)
    {
        availableUpdate = result;
        TextUpdateAvailable.Text = "FamilyQuant " + result.Tag + " is available.";
        ButtonInstallAvailableUpdate.Visibility = result.Installer == null ? Visibility.Collapsed : Visibility.Visible;
        UpdateAvailableBanner.Visibility = Visibility.Visible;
    }

    private void ButtonDismissUpdate_Click(object sender, RoutedEventArgs e) => UpdateAvailableBanner.Visibility = Visibility.Collapsed;
    private async void ButtonInstallAvailableUpdate_Click(object sender, RoutedEventArgs e) => await InstallUpdateAsync(availableUpdate);
    private void ButtonOpenAvailableRelease_Click(object sender, RoutedEventArgs e)
    {
        if (availableUpdate?.ReleasePage is Uri page) (OpenReleasePageOverride ?? OpenInBrowser)(page);
    }
    private void MenuItemAutomaticUpdateChecks_Click(object sender, RoutedEventArgs e)
    {
        shellState.CheckForUpdatesAtStartup = MenuItemAutomaticUpdateChecks.IsChecked;
        if (!shellState.CheckForUpdatesAtStartup) UpdateAvailableBanner.Visibility = Visibility.Collapsed;
        string? error = persistShellSettings ? shellState.Save(shellSettingsPath) : null;
        if (error != null) StatusModel.ShowMessage(error);
    }
}
