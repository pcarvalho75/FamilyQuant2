using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    public HttpMessageHandler? UpdateDownloadHandlerOverride { get; set; }
    public Action<ProcessStartInfo>? StartUpdateInstallerOverride { get; set; }
    public Action? CloseForUpdateOverride { get; set; }
    public string? UpdateDownloadFolderOverride { get; set; }

    private async void MenuItemInstallUpdate_Click(object sender, RoutedEventArgs e) => await InstallUpdateAsync();
    public Task<bool> InstallUpdateAsync(ReleaseUpdateResult? offered = null) => AppOperationHost.OnUi(this, () => InstallUpdateCoreAsync(offered));

    private async Task<bool> InstallUpdateCoreAsync(ReleaseUpdateResult? offered)
    {
        const string title = "Install update";
        if (DiagnosticBusy)
        {
            ShellMessages.Report(this, "Wait for the current operation to finish before installing an update.", title, showDialog: false);
            return false;
        }
        try
        {
            ReleaseUpdateResult result;
            using (var check = Operations.Begin(title))
            {
                check.Context.Report("update-check", "Asking GitHub for the latest FamilyQuant release.");
                result = offered ?? await ReleaseUpdateChecker.CheckAsync(AppVersionInfo.ReleaseRepository, AppVersionInfo.Current(), check.Context, UpdateCheckHandlerOverride);
            }
            if (result.Status == ReleaseUpdateStatus.Unavailable)
            {
                ShellMessages.Report(this, "The update check could not be done. Check the internet connection and try again later.", title);
                return false;
            }
            if (result.Status != ReleaseUpdateStatus.Newer || result.ReleasePage == null || result.Tag == null)
            {
                ShellMessages.Report(this, "This copy of FamilyQuant is up to date (" + AppVersionInfo.Current() + ").", title);
                return true;
            }
            if (result.Installer == null)
            {
                string text = "FamilyQuant " + result.Tag + " is available. This release has no installer.";
                if (ShellDialogs.IsDisplayed(this)) ShowUpdateAvailable(text, result);
                else ShellMessages.Report(this, text + "\n" + result.ReleasePage.AbsoluteUri, title, showDialog: false);
                return true;
            }
            // Keep the ordinary Save/Discard/Cancel contract before downloading and before approving exit.
            if (!await ConfirmCanReplaceAsync()) return false;
            string installer;
            using (var download = Operations.Begin(title))
            {
                string folder = UpdateDownloadFolderOverride ?? Path.Combine(ApplicationPaths.ScratchDirectory, "updates", Guid.NewGuid().ToString("N"));
                installer = await ReleaseInstallerDownload.DownloadAsync(result.Installer, AppVersionInfo.ReleaseRepository, result.Tag,
                    folder, download.Context, UpdateDownloadHandlerOverride);
                download.Context.ThrowIfCancellationRequested();
            }
            var start = CreateUpdateInstallerStartInfo(installer, AppContext.BaseDirectory, Environment.ProcessId);
            if (StartUpdateInstallerOverride != null) StartUpdateInstallerOverride(start);
            else using (var process = Process.Start(start))
                if (process == null) throw new InvalidOperationException("The installer could not be started.");
            approvedClose = true;
            if (CloseForUpdateOverride != null) CloseForUpdateOverride();
            else Close();
            return true;
        }
        catch (OperationCanceledException)
        {
            ShellMessages.Report(this, "The update was cancelled. Nothing was installed.", title);
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or Win32Exception)
        {
            ShellMessages.Report(this, "The update could not be installed: " + error.Message + "\nFamilyQuant remains open.", title);
            return false;
        }
    }

    public static ProcessStartInfo CreateUpdateInstallerStartInfo(string installer, string installFolder, int processId)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(installer)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(installer))!, CreateNoWindow = true };
        start.ArgumentList.Add("/silent");
        start.ArgumentList.Add("/dir=" + Path.TrimEndingDirectorySeparator(Path.GetFullPath(installFolder)));
        start.ArgumentList.Add("/waitpid=" + processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return start;
    }
}
