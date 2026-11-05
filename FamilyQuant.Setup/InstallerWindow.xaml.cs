using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace FamilyQuant.Setup;

/// <summary>The interactive surface only. All machine changes belong to ISetupController.</summary>
public partial class InstallerWindow : Window
{
    internal enum SetupPage { Welcome, ConfirmOlder, Folder, Options, Progress, Done, Uninstall, Error }

    private readonly ISetupController _controller;
    private ExistingInstallation? _existing;
    private SetupPage _page;
    private CancellationTokenSource? _operationCancellation;
    private bool _busy, _dark = true, _startupError, _removed, _closeAfterCancel;
    private string _directory = "";
    public int ExitCode { get; private set; } = 1;
    private bool HasExisting => _existing is not null && !_controller.Options.Uninstall;
    private int VersionComparison => HasExisting ? SetupVersions.Compare(_existing!.Version, _controller.PayloadVersion) : -1;
    private bool IsUpgrade => HasExisting && VersionComparison < 0;
    private bool IsAlreadyInstalled => HasExisting && VersionComparison >= 0;
    private bool _allowOlderVersion;

    public InstallerWindow(ISetupController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        InitializeComponent();
        SetTheme(true);
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/FamilyQuant2-Setup;component/FamilyQuant-Setup.ico"));
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        VersionLabel.Text = "VERSION " + _controller.PayloadVersion;
        InitializeInstallation();
    }

    private void InitializeInstallation()
    {
        try
        {
            _startupError = false;
            _existing = _controller.DetectExisting();
            _directory = _controller.Options.Directory ?? _existing?.Directory ?? _controller.DefaultDirectory;
            FolderBox.Text = _directory;
            DesktopOption.IsChecked = _controller.Options.DesktopShortcut;
            FolderBox.IsReadOnly = HasExisting;
            ChangeFolderButton.IsEnabled = !HasExisting;
            FolderNote.Text = HasExisting ? "Your current installation is changed in place." : "Only your Windows account uses this installation.";
            if (_controller.Options.Uninstall && _existing is null && _controller.Options.Directory is null)
            {
                _startupError = true;
                ShowError("No installation found", "FamilyQuant could not be found for this Windows account.");
                return;
            }
            ShowPage(_controller.Options.Uninstall ? SetupPage.Uninstall : SetupPage.Welcome);
        }
        catch (Exception error)
        {
            _startupError = true;
            ShowError("Setup could not start", "The current installation could not be read.", error.Message);
        }
    }

    private void ShowPage(SetupPage page)
    {
        _page = page;
        foreach (var panel in new[] { WelcomePanel, FolderPanel, OptionsPanel, ProgressPanel, DonePanel, UninstallPanel, ErrorPanel })
            panel.Visibility = Visibility.Collapsed;
        PanelFor(page).Visibility = Visibility.Visible;
        BackButton.Visibility = page is SetupPage.ConfirmOlder or SetupPage.Folder or SetupPage.Options ||
            (page == SetupPage.Error && !_startupError && !_launchFailed) ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = page == SetupPage.Done ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Visibility = page == SetupPage.Progress ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.IsEnabled = true;
        CancelButton.IsEnabled = true;
        CancelButton.Content = page == SetupPage.Error ? "_Close" : "_Cancel";
        ExistingActionButton.Visibility = page == SetupPage.Welcome && IsAlreadyInstalled ? Visibility.Visible : Visibility.Collapsed;
        switch (page)
        {
            case SetupPage.Welcome:
                StepLabel.Text = "GET STARTED";
                PageTitle.Text = IsAlreadyInstalled ? "FamilyQuant is already installed" : IsUpgrade
                    ? $"Upgrade FamilyQuant {SetupVersions.Display(_existing!.Version)} to {SetupVersions.Display(_controller.PayloadVersion)}" : "Welcome to FamilyQuant";
                PageDescription.Text = IsAlreadyInstalled ? SetupVersions.AlreadyInstalled(_existing!.Version, _controller.PayloadVersion)
                    : IsUpgrade ? "Keep your workspace and bring the app up to date." : "Set up FamilyQuant for your Windows account.";
                WelcomeSummary.Text = IsAlreadyInstalled ? "" : IsUpgrade ? "Your projects, reports and settings stay." : "No administrator account is needed.";
                PrimaryButton.Content = IsAlreadyInstalled ? "_Open FamilyQuant" : "_Continue";
                ExistingActionButton.Content = VersionComparison > 0 ? "_Install this older version" : "_Repair";
                break;
            case SetupPage.ConfirmOlder:
                StepLabel.Text = "CONFIRM OLDER VERSION";
                PageTitle.Text = "Install this older version?";
                PageDescription.Text = $"Replace FamilyQuant {SetupVersions.Display(_existing!.Version)} with {SetupVersions.Display(_controller.PayloadVersion)}?";
                WelcomeSummary.Text = "";
                PrimaryButton.Content = "_Continue with older version";
                break;
            case SetupPage.Folder:
                StepLabel.Text = "INSTALL LOCATION";
                PageTitle.Text = "A place for FamilyQuant";
                PageDescription.Text = HasExisting ? "Setup uses your existing app folder." : "Choose where the app will be installed.";
                PrimaryButton.Content = "_Continue";
                break;
            case SetupPage.Options:
                StepLabel.Text = "YOUR PREFERENCES";
                PageTitle.Text = "Make it yours";
                PageDescription.Text = "Choose a shortcut and how to finish.";
                OptionsFolder.Text = _directory;
                PrimaryButton.Content = _allowOlderVersion ? "_Install older version" : IsAlreadyInstalled ? "_Repair" : IsUpgrade ? "_Upgrade" : "_Install";
                break;
            case SetupPage.Progress:
                StepLabel.Text = "IN PROGRESS";
                PageTitle.Text = _controller.Options.Uninstall ? "Removing FamilyQuant" : IsUpgrade ? "Upgrading FamilyQuant"
                    : IsAlreadyInstalled && !_allowOlderVersion ? "Repairing FamilyQuant" : "Installing FamilyQuant";
                PageDescription.Text = "Setup will let you know when it is finished.";
                break;
            case SetupPage.Done:
                StepLabel.Text = "ALL DONE";
                PageTitle.Text = _removed ? "FamilyQuant is uninstalled" : "FamilyQuant is ready";
                PageDescription.Text = _removed ? "Your projects, reports and settings stay." : "Your workspace is ready to open.";
                DoneSummary.Text = _removed ? "The app has been removed." : $"FamilyQuant {_controller.PayloadVersion} is installed.";
                DoneFolder.Text = _removed ? "You can install FamilyQuant again at any time." : _directory;
                PrimaryButton.Content = !_removed && LaunchOption.IsChecked == true ? "_Launch FamilyQuant" : "_Finish";
                break;
            case SetupPage.Uninstall:
                StepLabel.Text = "UNINSTALL";
                PageTitle.Text = "Remove FamilyQuant?";
                PageDescription.Text = "Remove the app for this Windows account.";
                UninstallFolder.Text = _directory;
                PrimaryButton.Content = "_Uninstall";
                break;
            case SetupPage.Error:
                StepLabel.Text = "NEEDS ATTENTION";
                PrimaryButton.Content = "_Try again";
                break;
        }
    }

    private FrameworkElement PanelFor(SetupPage page) => page switch
    {
        SetupPage.Welcome or SetupPage.ConfirmOlder => WelcomePanel, SetupPage.Folder => FolderPanel, SetupPage.Options => OptionsPanel,
        SetupPage.Progress => ProgressPanel, SetupPage.Done => DonePanel, SetupPage.Uninstall => UninstallPanel,
        _ => ErrorPanel
    };

    private void ShowError(string title, string message, string? details = null)
    {
        ShowPage(SetupPage.Error);
        PageTitle.Text = title;
        PageDescription.Text = "";
        ErrorMessage.Text = message;
        ErrorDetails.IsExpanded = false;
        ErrorDetails.Visibility = string.IsNullOrWhiteSpace(details) ? Visibility.Collapsed : Visibility.Visible;
        ErrorDetailsText.Text = details ?? "";
    }

    private async void PrimaryClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        switch (_page)
        {
            case SetupPage.Welcome:
                if (IsAlreadyInstalled) { OpenExisting(); return; }
                ShowPage(SetupPage.Folder); break;
            case SetupPage.ConfirmOlder:
                _allowOlderVersion = true;
                ShowPage(SetupPage.Folder); break;
            case SetupPage.Folder:
                if (TryReadFolder()) ShowPage(SetupPage.Options);
                break;
            case SetupPage.Options:
            case SetupPage.Uninstall: await ExecuteOperationAsync(); break;
            case SetupPage.Error:
                if (_startupError) InitializeInstallation();
                else await ExecuteOperationAsync();
                break;
            case SetupPage.Done:
                if (!_removed && LaunchOption.IsChecked == true)
                {
                    try { _controller.Launch(_directory); }
                    catch (Exception error)
                    {
                        _launchFailed = true;
                        ShowError("FamilyQuant could not open", "The app is installed. You can open it from the Start menu.", error.Message);
                        PrimaryButton.Content = "_Finish";
                        return;
                    }
                }
                Close();
                break;
        }
    }

    private bool _launchFailed;
    internal async Task ExecuteOperationAsync()
    {
        if (_busy) return;
        if (_launchFailed) { Close(); return; }
        try
        {
            if (_controller.IsAppRunning(_directory))
            {
                ExitCode = 3;
                ShowError("Close FamilyQuant to continue", "FamilyQuant is running, close it to continue.");
                return;
            }
            _busy = true;
            _operationCancellation = new CancellationTokenSource();
            ShowPage(SetupPage.Progress);
            ApplyProgress(new SetupProgress("Preparing", 0, 0));
            var progress = new Progress<SetupProgress>(update => { if (_busy) ApplyProgress(update); });
            var result = _controller.Options.Uninstall
                ? await _controller.UninstallAsync(_directory, progress, _operationCancellation.Token)
                : await _controller.InstallAsync(_directory, DesktopOption.IsChecked == true, progress, _operationCancellation.Token, _allowOlderVersion);
            ExitCode = result.ExitCode;
            if (result.Success)
            {
                _directory = result.Directory ?? _directory;
                _removed = _controller.Options.Uninstall;
                ShowPage(SetupPage.Done);
            }
            else ShowError(_operationCancellation.IsCancellationRequested ? "Setup was canceled" : "Setup could not finish", result.Message);
        }
        catch (OperationCanceledException)
        {
            ExitCode = 1;
            ShowError("Setup was canceled", "You can try again when you are ready.");
        }
        catch (Exception error)
        {
            ExitCode = 1;
            ShowError("Setup could not finish", "Try again, or close Setup and run it later.", error.Message);
        }
        finally
        {
            _busy = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            if (_closeAfterCancel) Close();
        }
    }

    private void ApplyProgress(SetupProgress update)
    {
        ProgressStage.Text = update.Message;
        InstallProgress.IsIndeterminate = update.Total <= 0;
        if (update.Total > 0)
        {
            var percent = Math.Clamp(100d * update.Completed / update.Total, 0, 100);
            InstallProgress.Value = percent;
            ProgressPercent.Text = percent.ToString("0", CultureInfo.CurrentCulture) + "%";
            ProgressDetail.Text = $"{update.Completed.ToString(CultureInfo.CurrentCulture)} of {update.Total.ToString(CultureInfo.CurrentCulture)}";
        }
        else ProgressPercent.Text = ProgressDetail.Text = "";
    }

    private bool TryReadFolder()
    {
        try
        {
            if (!Path.IsPathFullyQualified(FolderBox.Text.Trim())) throw new ArgumentException();
            _directory = Path.GetFullPath(FolderBox.Text.Trim());
            FolderBox.Text = _directory;
            FolderNote.Text = HasExisting ? "Your current installation is changed in place." : "Only your Windows account uses this installation.";
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            FolderNote.Text = "Choose a full folder path, such as the default install folder.";
            FolderBox.Focus();
            return false;
        }
    }

    private void ChangeFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the FamilyQuant install folder", Multiselect = false };
        if (Path.IsPathFullyQualified(FolderBox.Text)) dialog.InitialDirectory = FolderBox.Text;
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }
    private void ExistingActionClick(object sender, RoutedEventArgs e)
    {
        if (_page != SetupPage.Welcome || !IsAlreadyInstalled || _busy) return;
        ShowPage(VersionComparison > 0 ? SetupPage.ConfirmOlder : SetupPage.Folder);
    }
    private void OpenExisting()
    {
        try { _controller.Launch(_directory); ExitCode = 0; Close(); }
        catch (Exception error)
        {
            _launchFailed = true;
            ShowError("FamilyQuant could not open", "Open FamilyQuant from its shortcut.", error.Message);
            PrimaryButton.Content = "_Finish";
        }
    }
    private void BackClick(object sender, RoutedEventArgs e)
    {
        if (_page == SetupPage.Folder) _allowOlderVersion = false;
        ShowPage(_page switch
    {
        SetupPage.Options => SetupPage.Folder,
        SetupPage.Error => _controller.Options.Uninstall ? SetupPage.Uninstall : SetupPage.Options,
        _ => SetupPage.Welcome
        });
    }
    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_busy) RequestCancel(); else Close();
    }
    private void RequestCancel()
    {
        _operationCancellation?.Cancel();
        CancelButton.Content = "Canceling...";
        CancelButton.IsEnabled = false;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_busy) return;
        e.Cancel = true;
        _closeAfterCancel = true;
        RequestCancel();
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (_busy) RequestCancel(); else Close();
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ThemeClick(object sender, RoutedEventArgs e) => SetTheme(!_dark);

    private void SetTheme(bool dark)
    {
        _dark = dark;
        ThemeButton.Content = dark ? "Light" : "Dark";
        // Deliberately share the app's token values without loading its libraries into Setup.
        var values = new (string Key, string Dark, string Light)[]
        {
            ("CanvasBrush", "#202020", "#F3F3F3"), ("SurfaceBrush", "#282828", "#FFFFFF"),
            ("SubtleBrush", "#2D2D2D", "#F9F9F9"), ("InputBrush", "#333333", "#FFFFFF"),
            ("HoverBrush", "#383838", "#E9E9E9"), ("EdgeBrush", "#484848", "#D1D1D1"),
            ("TextBrush", "#F5F5F5", "#1A1A1A"), ("MutedBrush", "#BDBDBD", "#5C5C5C"),
            ("AccentBrush", "#60CDFF", "#005FB8"), ("AccentInkBrush", "#003E5E", "#FFFFFF"),
            ("SelectionBrush", "#154760", "#DCEBFA"), ("ErrorBrush", "#FF99A4", "#C42B1C")
        };
        foreach (var (key, darkValue, lightValue) in values)
        {
            var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(dark ? darkValue : lightValue));
            brush.Freeze();
            Resources[key] = brush;
        }
    }
}
