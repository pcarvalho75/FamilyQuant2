using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

// Help > Check for updates runs only when the user picks it: one request through the shared busy host, then a plain
// answer. A newer release gets a small owned window whose button opens its page; nothing is downloaded or installed.
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
        string text = "FamilyQuant " + result.Tag + " is available. This copy is version " + current + ".\n\n" +
            "Nothing is downloaded or installed. The button opens the release page in your browser.";
        if (!ShellDialogs.IsDisplayed(this))
        {
            ShellMessages.Report(this, text + "\n" + result.ReleasePage.AbsoluteUri, title, showDialog: false);
            return true;
        }
        ShowUpdateAvailable(text, result.ReleasePage);
        return true;
    }

    private void ShowUpdateAvailable(string text, Uri page)
    {
        var window = new Window { Title = "Update available", Owner = this, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, MaxWidth = 520 };
        SecondaryWindowView.Theme(window);
        var open = new Button { Name = "ButtonOpenReleasePage", Content = "Open release page", IsDefault = true, Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Opens the release page in your browser. Nothing is downloaded or installed." };
        open.Click += (_, _) => (OpenReleasePageOverride ?? OpenInBrowser)(page);
        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
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
