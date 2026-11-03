using FamilyQuant.Operations;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public enum ReleaseUpdateStatus { Unavailable, Current, Newer }
public sealed record ReleaseUpdateResult(ReleaseUpdateStatus Status, string? Tag = null, Uri? ReleasePage = null);

/// <summary>
/// Help > Check for updates: one anonymous GET of a public repository's latest GitHub release, compared with the running
/// version. It downloads and installs nothing and sends nothing but the request. Offline, a timeout, a refusal (404, rate
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
        using var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
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
            return latest > current ? new ReleaseUpdateResult(ReleaseUpdateStatus.Newer, tag, page) : new ReleaseUpdateResult(ReleaseUpdateStatus.Current, tag);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException)
        {
            return unavailable;
        }
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
        && page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && page.AbsolutePath.StartsWith("/" + repository + "/releases/", StringComparison.OrdinalIgnoreCase);
}
