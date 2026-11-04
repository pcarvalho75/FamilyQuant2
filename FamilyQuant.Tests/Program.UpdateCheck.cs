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
using System.Windows.Threading;
using FamilyQuant.Operations;

namespace FamilyQuant.Tests;
internal static partial class Program
{
    private sealed class UpdateHttpHandler : HttpMessageHandler
    {
        internal Func<CancellationToken, Task<HttpResponseMessage>> Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        internal int Requests;
        internal string? Url, UserAgent, Method;
        internal bool HasBody, HasAuthorization, CancellationObserved;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests); Url = request.RequestUri?.AbsoluteUri;
            UserAgent = request.Headers.UserAgent.ToString(); Method = request.Method.Method;
            HasBody = request.Content != null; HasAuthorization = request.Headers.Authorization != null;
            try { return await Reply(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { CancellationObserved = token.IsCancellationRequested; throw; }
        }
    }
    private static HttpResponseMessage UpdateResponse(string tag, string? page = null) => new(HttpStatusCode.OK)
    { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { tag_name = tag,
        html_url = page ?? "https://github.com/pcarvalho75/FamilyQuant2-releases/releases/tag/" + tag })) };

    private static void UpdateCheckTests()
    {
        const string repository = "pcarvalho75/FamilyQuant2-releases";
        void Case(string name, Func<CancellationToken, Task<HttpResponseMessage>> reply, ReleaseUpdateStatus expected)
        {
            using var http = new UpdateHttpHandler { Reply = reply };
            var result = ReleaseUpdateChecker.CheckAsync(repository, "2.0.0.0", new OperationContext(), http).GetAwaiter().GetResult();
            Check("Y10 " + name, result.Status == expected && http.Requests == 1);
            Check("Y10 " + name + " sends only anonymous GET with User-Agent", http.Requests == 1
                && http.Url == "https://api.github.com/repos/pcarvalho75/FamilyQuant2-releases/releases/latest"
                && http.Method == "GET" && !string.IsNullOrWhiteSpace(http.UserAgent) && !http.HasBody && !http.HasAuthorization);
            Check("Y10 " + name + " offers a page only for a newer release", expected == ReleaseUpdateStatus.Newer
                ? result.Tag == "v2.0.1" && result.ReleasePage?.AbsoluteUri == "https://github.com/pcarvalho75/FamilyQuant2-releases/releases/tag/v2.0.1"
                : result.ReleasePage == null);
        }
        Case("newer release", _ => Task.FromResult(UpdateResponse("v2.0.1")), ReleaseUpdateStatus.Newer);
        Case("same release ignores omitted zero revision", _ => Task.FromResult(UpdateResponse("v2.0.0")), ReleaseUpdateStatus.Current);
        Case("older release", _ => Task.FromResult(UpdateResponse("v1.9.9")), ReleaseUpdateStatus.Current);
        Case("malformed JSON", _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{broken") }), ReleaseUpdateStatus.Unavailable);
        Case("404", _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)), ReleaseUpdateStatus.Unavailable);
        Case("rate limit", _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)), ReleaseUpdateStatus.Unavailable);
        Case("offline", _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("owned offline fixture")), ReleaseUpdateStatus.Unavailable);
        Case("bad tag", _ => Task.FromResult(UpdateResponse("latest")), ReleaseUpdateStatus.Unavailable);
        Case("bad release page", _ => Task.FromResult(UpdateResponse("v2.0.1", "file:///C:/owned.exe")), ReleaseUpdateStatus.Unavailable);
        using var timeout = new UpdateHttpHandler { Reply = async token => { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); return UpdateResponse("v2.0.1"); } };
        var watch = Stopwatch.StartNew();
        var timed = ReleaseUpdateChecker.CheckAsync(repository, "2.0.0.0", new OperationContext(), timeout).GetAwaiter().GetResult();
        Check("Y10 actual ten-second HTTP timeout returns plain failure", timed.Status == ReleaseUpdateStatus.Unavailable
            && timeout.Requests == 1 && timeout.CancellationObserved && watch.Elapsed >= TimeSpan.FromSeconds(9) && watch.Elapsed < TimeSpan.FromSeconds(15));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var untouched = new UpdateHttpHandler(); bool stopped = false;
        try { ReleaseUpdateChecker.CheckAsync(repository, "2.0.0.0", new OperationContext(cancelled.Token), untouched).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { stopped = true; }
        Check("Y10 pre-cancelled check sends nothing", stopped && untouched.Requests == 0);
        Check("Y10 release repository is one AppVersionInfo constant", typeof(AppVersionInfo).GetField("ReleaseRepository")?.GetRawConstantValue() as string == repository);
        using var installerHttp = new UpdateHttpHandler { Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"tag_name\":\"v2.0.1\",\"html_url\":\"https://github.com/pcarvalho75/FamilyQuant2-releases/releases/tag/v2.0.1\",\"assets\":[{\"name\":\"FamilyQuant2-2.0.1-Setup.exe\",\"browser_download_url\":\"https://github.com/pcarvalho75/FamilyQuant2-releases/releases/download/v2.0.1/FamilyQuant2-2.0.1-Setup.exe\",\"size\":3}]}" ) }) };
        var installable = ReleaseUpdateChecker.CheckAsync(repository, "2.0.0", new OperationContext(), installerHttp).GetAwaiter().GetResult();
        Check("M7b newer release retains the versioned installer asset", typeof(ReleaseUpdateResult).GetProperty("Installer")?.GetValue(installable) != null);
        UpdateInstallTests();
    }

    private static int UpdateCheckCommand(string[] args, string workDir)
    {
        string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        string log = Path.Combine(output, "proofs.log");
        if (!NativeProofDesktop.IsPrivate) return NativeProofDesktop.Run(args, log, 90_000);
        var outer = Console.Out; using var writer = new StreamWriter(log) { AutoFlush = true }; Console.SetOut(writer);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            UpdateCheckTests(); EnsureApplication();
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            { if (sender is Window window) OffscreenProofWindow(window); }), true);
            UpdateCheckNativeTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }
        finally { foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray()) window.Close(); Console.SetOut(outer); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void UpdateCheckNativeTests()
    {
        using var http = new UpdateHttpHandler { Reply = _ => Task.FromResult(UpdateResponse("v99.0.1")) };
        var main = OffscreenProofWindow(new MainWindow());
        var injection = typeof(MainWindow).GetProperty("UpdateCheckHandlerOverride");
        injection?.SetValue(main, http);
        main.Show(); PumpDispatcher();
        var item = main.FindName("MenuItemCheckForUpdates") as MenuItem;
        Check("Y10 real Help menu exposes manual Check for updates", item != null && injection != null);
        Check("Y10 shell startup sends no update request", http.Requests == 0);
        if (item == null || injection == null) { main.Close(); return; }
        void Click() => typeof(MenuItem).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(item, null);
        Click(); FinishBusyOperation(main.Operations); PumpDispatcher();
        var result = Application.Current.Windows.Cast<Window>().SingleOrDefault(window => window != main && window.Owner == main && window.Title == "Update available");
        Check("Y10 native Help click requests one release and opens owned result", http.Requests == 1 && result != null && HasProofWindowPlacement(result)
            && WalkTree(result).OfType<TextBlock>().Any(text => text.Text.Contains("v99.0.1", StringComparison.Ordinal)));
        var opener = typeof(MainWindow).GetProperty("OpenReleasePageOverride"); var opened = new List<Uri>();
        opener?.SetValue(main, (Action<Uri>)(uri => opened.Add(uri)));
        Check("Y10 result never opens browser automatically", opened.Count == 0 && opener != null);
        if (result != null)
        {
            var button = WalkTree(result).OfType<Button>().SingleOrDefault(value => value.Name == "ButtonOpenReleasePage");
            Check("Y10 result has an explicit release-page button", button?.Content as string == "Open release page");
            if (button != null) SecondaryClick(button);
            Check("Y10 native button opens only the release page", opened.Count == 1 && opened[0].AbsoluteUri == "https://github.com/pcarvalho75/FamilyQuant2-releases/releases/tag/v99.0.1");
            result.Close();
        }
        using var hold = new UpdateHttpHandler { Reply = async token => { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); return UpdateResponse("v99.0.1"); } };
        injection.SetValue(main, hold); string? message = null;
        var previous = ShellMessages.Presenter; ShellMessages.Presenter = (_, value) => { message = value.Text; return MessageBoxResult.OK; };
        try
        {
            Click(); PumpDispatcher();
            bool busy = main.Operations.IsBusy && !((Menu)main.FindName("MainMenu")).IsEnabled && main.Operations.CancelCommand.CanExecute(null);
            main.Operations.CancelCommand.Execute(null); FinishBusyOperation(main.Operations); PumpDispatcher();
            Check("Y10 shared busy host Cancel reaches HTTP and returns controls", busy && hold.Requests == 1 && hold.CancellationObserved
                && !main.Operations.IsBusy && ((Menu)main.FindName("MainMenu")).IsEnabled && message?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true);
            using var fail = new UpdateHttpHandler { Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)) };
            injection.SetValue(main, fail); message = null; Click(); FinishBusyOperation(main.Operations); PumpDispatcher();
            Check("Y10 native failure gives plain message and no result window", fail.Requests == 1 && message?.Contains("could not", StringComparison.OrdinalIgnoreCase) == true
                && Application.Current.Windows.Cast<Window>().All(window => window.Owner != main || window.Title != "Update available") && opened.Count == 1);
        }
        finally { ShellMessages.Presenter = previous; main.Close(); }
    }
}
