using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;
internal static partial class Program
{
    private static void UpdateStartupAndInstallNativeTests(string proofOutput)
    {
        string output = Path.Combine(Path.GetTempPath(), "update-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        string settings = Path.Combine(output, "shell.json");
        string? previousSettings = MainWindow.ShellSettingsPathOverride;
        var previousPresenter = ShellMessages.Presenter;
        var messages = new List<string>();
        ShellMessages.Presenter = (_, value) => { messages.Add(value.Text); return MessageBoxResult.OK; };
        MainWindow.ShellSettingsPathOverride = settings;
        bool Finish(Task task)
        {
            FinishBusyTask(task);
            task.GetAwaiter().GetResult();
            PumpDispatcher();
            return task.IsCompletedSuccessfully;
        }
        void Click(MenuItem item) => typeof(MenuItem).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(item, null);
        MainWindow Open(UpdateHttpHandler http)
        {
            var window = OffscreenProofWindow(new MainWindow { UpdateCheckHandlerOverride = http, UpdateCheckStartupDelay = TimeSpan.Zero });
            window.Show(); PumpDispatcher(); Finish(window.StartupUpdateCheckTask);
            return window;
        }
        try
        {
            Check("M7b automatic checks default on for old and new preferences", new ShellSessionState().CheckForUpdatesAtStartup
                && ShellSessionState.Load(Path.Combine(output, "missing.json")).CheckForUpdatesAtStartup);
            File.WriteAllText(settings, "{}");
            Check("M7b old preference JSON enables startup check", ShellSessionState.Load(settings).CheckForUpdatesAtStartup);
            var rootPlan = MainWindow.CreateUpdateInstallerStartInfo(Path.Combine(output, SetupName), "C:\\", 123);
            var spacePlan = MainWindow.CreateUpdateInstallerStartInfo(Path.Combine(output, SetupName), "C:\\An install\\FamilyQuant\\", 123);
            Check("M7b launch arguments preserve drive roots and folders with spaces", rootPlan.ArgumentList[1] == "/dir=C:\\"
                && spacePlan.ArgumentList[1] == "/dir=C:\\An install\\FamilyQuant" && rootPlan.ArgumentList.Count == 3 && spacePlan.ArgumentList.Count == 3);
            using var http = new UpdateHttpHandler { Reply = _ => Task.FromResult(SetupRelease()) };
            int launched = 0, closed = 0, downloads = 0, browser = 0;
            for (int start = 0; start < 2; start++)
            {
                var main = Open(http);
                main.StartUpdateInstallerOverride = _ => launched++;
                main.CloseForUpdateOverride = () => closed++;
                main.OpenReleasePageOverride = _ => browser++;
                using var data = new UpdateHttpHandler { Reply = _ => { downloads++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 77, 90, 1 }) }); } };
                main.UpdateDownloadHandlerOverride = data;
                Check("M7b every start checks once and shows only a nonblocking banner", http.Requests == start + 1
                    && ((FrameworkElement)main.FindName("UpdateAvailableBanner")).Visibility == Visibility.Visible
                    && !main.Operations.IsBusy && ((Menu)main.FindName("MainMenu")).IsEnabled
                    && Application.Current.Windows.Cast<Window>().All(window => window == main || window.Owner != main));
                Check("M7b startup never downloads, launches or opens browser", downloads == 0 && launched == 0 && closed == 0 && browser == 0 && messages.Count == 0);
                if (start == 0) ScreenHarness.Save(main, Path.Combine(proofOutput, "startup-banner.png"), 1024, 768);
                SecondaryClick((Button)main.FindName("ButtonDismissUpdate"));
                Check("M7b notice can be dismissed", ((FrameworkElement)main.FindName("UpdateAvailableBanner")).Visibility == Visibility.Collapsed);
                main.Close();
            }
            foreach (string state in new[] { "current", "older", "offline", "failure", "no-installer" })
            {
                using var response = new UpdateHttpHandler { Reply = _ => state switch
                {
                    "current" => Task.FromResult(UpdateResponse("v" + AppVersionInfo.Current())),
                    "older" => Task.FromResult(UpdateResponse("v1.0.0")),
                    "offline" => Task.FromException<HttpResponseMessage>(new HttpRequestException("offline fixture")),
                    "failure" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)),
                    _ => Task.FromResult(SetupRelease(null))
                } };
                var main = Open(response);
                bool visible = ((FrameworkElement)main.FindName("UpdateAvailableBanner")).Visibility == Visibility.Visible;
                Check("M7b quiet startup " + state, response.Requests == 1 && visible == (state == "no-installer") && messages.Count == 0 && !main.Operations.IsBusy);
                if (state == "no-installer")
                {
                    Check("M7b old release banner offers page and no install button", ((Button)main.FindName("ButtonInstallAvailableUpdate")).Visibility == Visibility.Collapsed
                        && ((Button)main.FindName("ButtonOpenAvailableRelease")).Visibility == Visibility.Visible);
                }
                main.Close();
            }
            new ShellSessionState { CheckForUpdatesAtStartup = false }.Save(settings);
            using (var disabled = new UpdateHttpHandler { Reply = _ => Task.FromResult(SetupRelease()) })
            {
                var main = Open(disabled);
                var setting = (MenuItem)main.FindName("MenuItemAutomaticUpdateChecks");
                Check("M7b off setting prevents startup requests and is reflected in Help", disabled.Requests == 0 && !setting.IsChecked);
                var manual = main.CheckForUpdatesAsync(); Finish(manual);
                Check("M7b manual Check still works when startup is off", disabled.Requests == 1 && manual.Result);
                foreach (Window child in Application.Current.Windows.Cast<Window>().Where(window => window.Owner == main).ToArray()) child.Close();
                Click(setting);
                PumpDispatcher();
                Check("M7b Help toggle persists the per-user preference", ShellSessionState.Load(settings).CheckForUpdatesAtStartup && setting.IsChecked);
                main.Close();
            }
            using (var pending = new UpdateHttpHandler { Reply = async token => { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); return SetupRelease(); } })
            {
                var main = OffscreenProofWindow(new MainWindow { UpdateCheckHandlerOverride = pending, UpdateCheckStartupDelay = TimeSpan.Zero });
                main.Show(); PumpDispatcher();
                Check("M7b pending startup leaves menus and operations available", pending.Requests == 1 && !main.Operations.IsBusy && ((Menu)main.FindName("MainMenu")).IsEnabled);
                main.Close(); Finish(main.StartupUpdateCheckTask);
                Check("M7b closing cancels the background request silently", pending.CancellationObserved && messages.Count == 0);
            }
            using (var release = new UpdateHttpHandler { Reply = _ => Task.FromResult(SetupRelease()) })
            using (var bytes = new UpdateHttpHandler { Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 77, 90, 1 }) }) })
            {
                var main = Open(release);
                main.UpdateDownloadHandlerOverride = bytes;
                main.UpdateDownloadFolderOverride = Path.Combine(output, "download");
                ProcessStartInfo? plan = null;
                main.StartUpdateInstallerOverride = value => { plan = value; launched++; };
                main.CloseForUpdateOverride = () => closed++;
                Check("M7b Help exposes Install update beside Check", main.FindName("MenuItemInstallUpdate") is MenuItem { Header: "Install update" });
                Click((MenuItem)main.FindName("MenuItemInstallUpdate")); PumpDispatcher(); FinishBusyOperation(main.Operations);
                Check("M7b explicit Help click downloads then launches once and requests close", release.Requests == 2 && bytes.Requests == 1 && launched == 1 && closed == 1
                    && plan != null && File.Exists(plan.FileName) && Path.GetFileName(plan.FileName) == SetupName);
                Check("M7b quiet installer arguments preserve the install folder and own PID", plan != null && !plan.UseShellExecute
                    && plan.ArgumentList.SequenceEqual(new[] { "/silent", "/dir=" + Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), "/waitpid=" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
                main.Close();
            }
            using (var current = new UpdateHttpHandler { Reply = _ => Task.FromResult(UpdateResponse("v" + AppVersionInfo.Current())) })
            {
                var main = Open(current); messages.Clear();
                var install = main.InstallUpdateAsync(); Finish(install);
                Check("M7b manual Install says up to date without downloading", install.Result && messages.Single().Contains("up to date", StringComparison.Ordinal) && launched == 1 && closed == 1);
                main.Close(); messages.Clear();
            }
            using (var release = new UpdateHttpHandler { Reply = _ => Task.FromResult(SetupRelease()) })
            {
                string source = Path.Combine(output, "unsaved.fcf");
                new FamilyClusterFile { MyExtendedFastaItems = new() { new FastaItemExtended { SequenceIdentifier = "update_fixture", Description = "Synthetic fixture",
                    Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 } } }.Save(source);
                var main = Open(release);
                main.OpenProjectPath(source);
                main.NavigationModel.Document!.MyExtendedFastaItems[0].UserAnnotation = "Edited fixture";
                main.DocumentState.CheckForChanges(main.NavigationModel.Document);
                int attemptedLaunch = 0;
                main.StartUpdateInstallerOverride = _ => attemptedLaunch++;
                main.CloseForUpdateOverride = () => closed++;
                using var data = new UpdateHttpHandler(); main.UpdateDownloadHandlerOverride = data;
                var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
                try
                {
                    MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Cancel;
                    var refused = main.InstallUpdateAsync(); Finish(refused);
                    Check("M7b unsaved-project Cancel keeps edits and sends no download", !refused.Result && main.DocumentState.IsModified
                        && main.NavigationModel.Document.MyExtendedFastaItems[0].UserAnnotation == "Edited fixture" && data.Requests == 0 && attemptedLaunch == 0 && closed == 1);
                }
                finally { MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard; main.Close(); MainWindow.UnsavedProjectChoiceOverride = oldChoice; }
            }
            foreach (string mode in new[] { "offline", "launch-failure", "no-installer", "cancelled" })
            {
                using var release = new UpdateHttpHandler { Reply = _ => Task.FromResult(SetupRelease(mode == "no-installer" ? null : SetupUrl)) };
                using var bytes = new UpdateHttpHandler { Reply = async token =>
                {
                    if (mode == "offline") throw new HttpRequestException("offline fixture");
                    if (mode == "cancelled") await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 77, 90, 1 }) };
                } };
                var main = Open(release);
                main.UpdateDownloadHandlerOverride = bytes;
                main.UpdateDownloadFolderOverride = Path.Combine(output, mode);
                main.StartUpdateInstallerOverride = _ => throw new System.ComponentModel.Win32Exception("owned fake launch refusal");
                main.CloseForUpdateOverride = () => closed++;
                var task = main.InstallUpdateAsync();
                if (mode == "cancelled") { PumpDispatcher(); main.Operations.Cancel(); }
                Finish(task);
                Check("M7b " + mode + " never closes or runs installer", closed == 1 && (mode == "no-installer" ? task.Result && bytes.Requests == 0 : !task.Result && messages.Count == 1));
                foreach (Window child in Application.Current.Windows.Cast<Window>().Where(window => window.Owner == main).ToArray())
                {
                    Check("M7b no-setup manual result has only release-page action", !WalkTree(child).OfType<Button>().Any(button => button.Name == "ButtonInstallUpdate"));
                    child.Close();
                }
                main.Close(); messages.Clear();
            }
            Check("M7b quiet background outcomes are recorded in per-user log", File.ReadAllText(Path.Combine(output, "update-check.log")).Contains("Startup update check: Unavailable", StringComparison.Ordinal));
        }
        finally
        {
            foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray()) window.Close();
            ShellMessages.Presenter = previousPresenter;
            MainWindow.ShellSettingsPathOverride = previousSettings;
        }
    }
}
