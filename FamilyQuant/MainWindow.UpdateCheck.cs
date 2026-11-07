using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

// Manual checks use the shared busy host. Startup checks only update a dismissible banner.
public partial class MainWindow
{
    public HttpMessageHandler? UpdateCheckHandlerOverride { get; set; }
    public Action<Uri>? OpenReleasePageOverride { get; set; }

    private async void MenuItemCheckForUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    public Task<bool> CheckForUpdatesAsync() => AppOperationHost.OnUi(this, CheckForUpdatesCoreAsync);

    private async Task<bool> CheckForUpdatesCoreAsync()
    {
        const string title = "Check for updates";
        if (DiagnosticBusy)
        {
            ShellMessages.Report(this, "Wait for the current operation to finish before checking for updates.", title, showDialog: false);
            return false;
        }
        string current = AppVersionInfo.Current();
        ReleaseUpdateResult result;
        using (var operation = Operations.Begin("Check for updates"))
        {
            try
            {
                operation.Context.Report("update-check", "Asking GitHub for the latest FamilyQuant release.");
                result = await ReleaseUpdateChecker.CheckAsync(AppVersionInfo.ReleaseRepository, current, operation.Context, UpdateCheckHandlerOverride);
            }
            catch (OperationCanceledException)
            {
                operation.Dispose();
                ShellMessages.Report(this, "The update check was cancelled. Nothing was changed.", title);
                return false;
            }
        }
        if (result.Status == ReleaseUpdateStatus.Unavailable)
        {
            ShellMessages.Report(this, "The update check could not be done: the release page did not answer or its answer could not be read. " +
                "Check the internet connection and try again later. Nothing was changed.", title);
            return false;
        }
        if (result.Status == ReleaseUpdateStatus.Current || result.ReleasePage == null)
        {
            ShellMessages.Report(this, "This is the latest version of FamilyQuant (" + current + ").", title);
            return true;
        }
        string text = "FamilyQuant " + result.Tag + " is available (this copy is " + current + ").";
        if (!ShellDialogs.IsDisplayed(this))
        {
            ShellMessages.Report(this, text + "\n" + result.ReleasePage.AbsoluteUri, title, showDialog: false);
            return true;
        }
        ShowUpdateAvailable(text, result);
        return true;
    }

    private void ShowUpdateAvailable(string text, ReleaseUpdateResult result)
    {
        var window = new Window { Title = "Update available", Owner = this, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, MaxWidth = 520 };
        SecondaryWindowView.Theme(window);
        var open = new Button { Name = "ButtonOpenReleasePage", Content = "Open release page", IsDefault = true, Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Opens the release page in your browser. Nothing is downloaded or installed." };
        open.Click += (_, _) => (OpenReleasePageOverride ?? OpenInBrowser)(result.ReleasePage!);
        var close = new Button { Content = "Close", IsCancel = true,
            ToolTip = "Closes this release notice and keeps FamilyQuant open. The project stays unchanged." };
        close.Click += (_, _) => window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (result.Installer != null)
        {
            var install = new Button { Name = "ButtonInstallUpdate", Content = "Install update", Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Downloads this release's installer after Save, Discard or Cancel protects open edits. FamilyQuant closes to install it; project Undo does not revert installation." };
            install.SetResourceReference(StyleProperty, "PrimaryButton");
            install.Click += async (_, _) => { window.Close(); await InstallUpdateAsync(result); };
            buttons.Children.Add(install);
        }
        buttons.Children.Add(open);
        buttons.Children.Add(close);
        var layout = new StackPanel();
        layout.Children.Add(SecondaryWindowView.Text(text));
        layout.Children.Add(buttons);
        window.Content = SecondaryWindowView.Frame(layout);
        window.Show();
    }

    private static void OpenInBrowser(Uri page) => Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
}
