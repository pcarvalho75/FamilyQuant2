using FamilyQuant.Operations;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public enum ReleaseUpdateStatus { Unavailable, Current, Newer }
public sealed record ReleaseInstallerAsset(string Name, Uri DownloadUrl, long? Size = null, string? Sha256 = null);
public sealed record ReleaseUpdateResult(ReleaseUpdateStatus Status, string? Tag = null, Uri? ReleasePage = null,
    ReleaseInstallerAsset? Installer = null);

/// <summary>
/// One anonymous GET of a public repository's latest GitHub release, compared with the running
/// version. Checking never downloads an asset. Offline, a timeout, a refusal (404, rate
/// limit) or unreadable data all give Unavailable; only the user's own cancel propagates.
/// </summary>
public static class ReleaseUpdateChecker
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task<ReleaseUpdateResult> CheckAsync(string repository, string currentVersion,
        OperationContext operation, HttpMessageHandler? handler = null)
    {
        operation.ThrowIfCancellationRequested();
        var unavailable = new ReleaseUpdateResult(ReleaseUpdateStatus.Unavailable);
        if (!TryParseVersion(currentVersion, out Version current)) return unavailable;
        using var client = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler, disposeHandler: false);
        client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(operation.CancellationToken);
        limit.CancelAfter(Timeout);
        try
        {
            using var response = await client.GetAsync("https://api.github.com/repos/" + repository + "/releases/latest",
                HttpCompletionOption.ResponseContentRead, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return unavailable;
            string json = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String
                || !document.RootElement.TryGetProperty("html_url", out var pageElement) || pageElement.ValueKind != JsonValueKind.String)
                return unavailable;
            string tag = tagElement.GetString()!.Trim();
            if (!TryParseVersion(tag, out Version latest)
                || !Uri.TryCreate(pageElement.GetString(), UriKind.Absolute, out Uri? page) || !IsReleasePage(page, repository))
                return unavailable;
            return latest > current ? new ReleaseUpdateResult(ReleaseUpdateStatus.Newer, tag, page,
                ReadInstaller(document.RootElement, repository, tag)) : new ReleaseUpdateResult(ReleaseUpdateStatus.Current, tag);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException)
        {
            return unavailable;
        }
    }

    public static string InstallerName(string tag) => "FamilyQuant2-" + tag.TrimStart('v', 'V') + "-Setup.exe";

    // Rechecked immediately before downloading. The file name and repository/tag path must agree.
    public static bool IsInstallerAsset(ReleaseInstallerAsset asset, string repository, string tag) =>
        TryParseVersion(tag, out _) && asset.Name == InstallerName(tag)
        && asset.DownloadUrl.Scheme == Uri.UriSchemeHttps && asset.DownloadUrl.IsDefaultPort
        && asset.DownloadUrl.UserInfo.Length == 0 && asset.DownloadUrl.Query.Length == 0 && asset.DownloadUrl.Fragment.Length == 0
        && asset.DownloadUrl.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && asset.DownloadUrl.AbsolutePath == "/" + repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + asset.Name;

    private static ReleaseInstallerAsset? ReadInstaller(JsonElement root, string repository, string tag)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        ReleaseInstallerAsset? found = null;
        foreach (var item in assets.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || name.GetString() != InstallerName(tag) || !item.TryGetProperty("browser_download_url", out var url)
                || url.ValueKind != JsonValueKind.String || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)) continue;
            long? size = item.TryGetProperty("size", out var bytes) && bytes.TryGetInt64(out long length) ? length : null;
            string? digest = item.TryGetProperty("digest", out var hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
            var asset = new ReleaseInstallerAsset(name.GetString()!, uri, size, digest);
            if (!IsInstallerAsset(asset, repository, tag) || found != null) return null;
            found = asset;
        }
        return found;
    }

    /// <summary>A tag such as v2.0.1 or a version such as 2.0.0.0; omitted parts count as zero.</summary>
    internal static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        string value = (text ?? "").Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        string[] parts = value.Split('.');
        if (parts.Length is < 1 or > 4) return false;
        var numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    private static bool IsReleasePage(Uri page, string repository) => page.Scheme == Uri.UriSchemeHttps
        && page.IsDefaultPort && page.UserInfo.Length == 0
        && page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && page.AbsolutePath.StartsWith("/" + repository + "/releases/", StringComparison.OrdinalIgnoreCase);
}
