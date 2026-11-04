using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;
internal static partial class Program
{
    private const string UpdateRepository = "pcarvalho75/FamilyQuant2-releases";
    private const string SetupTag = "v99.0.1";
    private const string SetupName = "FamilyQuant2-99.0.1-Setup.exe";
    private const string SetupUrl = "https://github.com/pcarvalho75/FamilyQuant2-releases/releases/download/v99.0.1/" + SetupName;

    private static HttpResponseMessage SetupRelease(string? url = SetupUrl, string name = SetupName) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
        {
            tag_name = SetupTag,
            html_url = "https://github.com/" + UpdateRepository + "/releases/tag/" + SetupTag,
            assets = url == null ? Array.Empty<object>() : new object[] { new { name, browser_download_url = url, size = 3 } }
        }))
    };

    private sealed class UpdateProgress : IProgress<OperationProgress>
    {
        public readonly List<OperationProgress> Items = new();
        public Action? OnProgress;
        public void Report(OperationProgress value) { Items.Add(value); OnProgress?.Invoke(); }
    }

    private static void UpdateInstallTests()
    {
        foreach (var item in new[]
        {
            ("valid versioned asset", SetupUrl, SetupName, true),
            ("old release without setup", (string?)null, SetupName, false),
            ("wrong file name", SetupUrl, "FamilyQuant2-Setup.exe", false),
            ("wrong version", SetupUrl.Replace("99.0.1", "99.0.2", StringComparison.Ordinal), SetupName, false),
            ("wrong repo", SetupUrl.Replace("FamilyQuant2-releases", "another-repo", StringComparison.Ordinal), SetupName, false),
            ("other host", SetupUrl.Replace("github.com", "example.org", StringComparison.Ordinal), SetupName, false),
            ("host suffix attack", SetupUrl.Replace("github.com", "github.com.example.org", StringComparison.Ordinal), SetupName, false),
            ("insecure asset", SetupUrl.Replace("https:", "http:", StringComparison.Ordinal), SetupName, false),
            ("asset userinfo", SetupUrl.Replace("github.com", "user@github.com", StringComparison.Ordinal), SetupName, false),
            ("asset port", SetupUrl.Replace("github.com", "github.com:444", StringComparison.Ordinal), SetupName, false)
        })
        {
            using var http = new UpdateHttpHandler { Reply = _ => Task.FromResult(SetupRelease(item.Item2, item.Item3)) };
            var result = ReleaseUpdateChecker.CheckAsync(UpdateRepository, "2.0.0", new OperationContext(), http).GetAwaiter().GetResult();
            Check("M7b " + item.Item1 + " metadata", result.Status == ReleaseUpdateStatus.Newer
                && (result.Installer != null) == item.Item4 && http.Requests == 1);
        }
        var asset = new ReleaseInstallerAsset(SetupName, new Uri(SetupUrl), 3);
        var progress = new UpdateProgress();
        string output = Path.Combine(Path.GetTempPath(), "update-download-" + Guid.NewGuid().ToString("N"));
        using (var http = new UpdateHttpHandler { Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 77, 90, 1 }) }) })
        {
            string file = ReleaseInstallerDownload.DownloadAsync(asset, UpdateRepository, SetupTag, output, new OperationContext(progress: progress), http).GetAwaiter().GetResult();
            Check("M7b complete installer bytes are retained under the exact safe name", Path.GetFileName(file) == SetupName && File.ReadAllBytes(file).AsSpan().SequenceEqual(new byte[] { 77, 90, 1 }) && !File.Exists(file + ".download"));
            Check("M7b download reports byte progress and anonymous GET", progress.Items.Count >= 2 && progress.Items[^1].Completed == 3 && progress.Items[^1].Total == 3
                && http.Requests == 1 && !http.HasAuthorization && http.Method == "GET");
        }
        foreach (string host in new[] { "release-assets.githubusercontent.com", "objects.githubusercontent.com", "example.org", "release-assets.githubusercontent.com.example.org", "github.com" })
        {
            string folder = Path.Combine(Path.GetTempPath(), "update-redirect-" + Guid.NewGuid().ToString("N"));
            int requests = 0;
            using var http = new UpdateHttpHandler { Reply = _ => Task.FromResult(++requests == 1 ? Redirect("https://" + host + "/asset?ticket=fixture")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 77, 90, 1 }) }) };
            bool accepted = false;
            try { ReleaseInstallerDownload.DownloadAsync(asset, UpdateRepository, SetupTag, folder, new OperationContext(), http).GetAwaiter().GetResult(); accepted = true; }
            catch (InvalidDataException) { }
            bool expected = host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
            Check("M7b redirect exact host " + host, accepted == expected && requests == (expected ? 2 : 1)
                && (expected || !File.Exists(Path.Combine(folder, SetupName))));
        }
        foreach (string redirect in new[] { "http://objects.githubusercontent.com/file", "https://user@objects.githubusercontent.com/file", "https://objects.githubusercontent.com:444/file" })
        {
            int requests = 0;
            using var http = new UpdateHttpHandler { Reply = _ => { requests++; return Task.FromResult(Redirect(redirect)); } };
            bool rejected = false;
            try { ReleaseInstallerDownload.DownloadAsync(asset, UpdateRepository, SetupTag, Path.Combine(output, Guid.NewGuid().ToString("N")), new OperationContext(), http).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { rejected = true; }
            Check("M7b rejects insecure/credentialed/nondefault-port redirect before following", rejected && requests == 1);
        }
        foreach (string kind in new[] { "truncated", "digest", "cancelled", "offline", "untrusted", "loop" })
        {
            string folder = Path.Combine(output, kind);
            using var cancellation = new CancellationTokenSource();
            var watchProgress = new UpdateProgress { OnProgress = kind == "cancelled" ? cancellation.Cancel : null };
            using var http = new UpdateHttpHandler { Reply = _ =>
            {
                if (kind == "offline") throw new HttpRequestException("offline fixture");
                if (kind == "loop") return Task.FromResult(Redirect("https://objects.githubusercontent.com/loop"));
                var content = new ByteArrayContent(new byte[] { 77, 90, 1 });
                if (kind == "truncated") content.Headers.ContentLength = 4;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            } };
            var candidate = kind == "truncated" ? asset with { Size = 4 } : kind == "digest" ? asset with { Sha256 = "sha256:" + new string('0', 64) }
                : kind == "untrusted" ? asset with { DownloadUrl = new Uri("https://example.org/" + SetupName) } : asset;
            bool refused = false;
            try { ReleaseInstallerDownload.DownloadAsync(candidate, UpdateRepository, SetupTag, folder, new OperationContext(cancellation.Token, watchProgress), http).GetAwaiter().GetResult(); }
            catch (Exception error) when (error is InvalidDataException or OperationCanceledException or HttpRequestException) { refused = true; }
            Check("M7b " + kind + " leaves no runnable or partial installer", refused && !File.Exists(Path.Combine(folder, SetupName)) && !File.Exists(Path.Combine(folder, SetupName + ".download"))
                && (kind != "untrusted" || http.Requests == 0));
        }
    }

    private static HttpResponseMessage Redirect(string url)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(url);
        return response;
    }
}
