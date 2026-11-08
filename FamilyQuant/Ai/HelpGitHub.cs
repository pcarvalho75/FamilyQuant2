using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Ai;

/// <summary>
/// The read-only GitHub token built into this program, or null when the build had none. Key and Scrambled come from
/// the GenerateHelpTokenSource target in FamilyQuant.App.csproj; the scrambling only keeps secret scanners from
/// recognising the token, it does not protect it.
/// </summary>
internal static partial class HelpGitHubToken
{
    public static string? Read()
    {
        byte[] bytes = Convert.FromBase64String(Scrambled);
        for (int i = 0; i < bytes.Length; i++) bytes[i] ^= (byte)Key[i % Key.Length];
        string token = Encoding.UTF8.GetString(bytes);
        return token.Length == 0 ? null : token;
    }
}

/// <summary>
/// GitHub stopped the reading for this question. A null <see cref="LimitResetsAt"/> means it refused the built-in token;
/// otherwise its rate limit was reached and it said when it would answer again.
/// </summary>
internal sealed class HelpGitHubStopException(DateTimeOffset? limitResetsAt) : Exception("GitHub stopped answering.")
{
    public DateTimeOffset? LimitResetsAt { get; } = limitResetsAt;
}

/// <summary>
/// Read-only access to the FamilyQuant source on GitHub for the help assistant's three tools. Only the paths and search
/// terms Claude chooses are sent, never anything about the user. The tree and each file are fetched once per instance;
/// the program keeps one instance per process. A failed read is not cached.
/// </summary>
internal sealed class HelpGitHub
{
    public const string Repository = "pcarvalho75/FamilyQuant2";
    public const string Branch = "master";
    public const int MaxReadLines = 400;
    private const int MaxListedPaths = 300;
    private const int MaxSearchResults = 15;
    private const int MaxFragment = 240;
    private readonly HttpClient http;
    private readonly string token;
    private readonly string userAgent;
    private readonly ConcurrentDictionary<string, string[]> files = new(StringComparer.Ordinal);
    private string[]? tree;

    public HelpGitHub(HttpClient http, string token, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        this.http = http;
        this.token = token;
        userAgent = "FamilyQuant/" + version;
    }

    /// <summary>The repository paths that start with <paramref name="prefix"/>, one per line.</summary>
    public async Task<string> ListFilesAsync(string prefix, CancellationToken cancellationToken)
    {
        string[] paths = tree ??= await LoadTreeAsync(cancellationToken).ConfigureAwait(false);
        string start = (prefix ?? "").Replace('\\', '/').TrimStart('/');
        var matches = paths.Where(path => path.StartsWith(start, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) return "No file path starts with \"" + start + "\".";
        var text = new StringBuilder();
        foreach (string path in matches.Take(MaxListedPaths)) text.Append(path).Append('\n');
        if (matches.Count > MaxListedPaths)
            text.Append("... and ").Append(matches.Count - MaxListedPaths).Append(" more; ask for a longer prefix.\n");
        return text.ToString();
    }

    /// <summary>
    /// Lines <paramref name="fromLine"/> to <paramref name="toLine"/> (1-based, inclusive, at most
    /// <see cref="MaxReadLines"/>) of one file, each prefixed with its number, and the range actually returned.
    /// </summary>
    public async Task<(string Text, HelpSource? Source)> ReadFileAsync(string path, int fromLine, int toLine,
        CancellationToken cancellationToken)
    {
        string clean = (path ?? "").Replace('\\', '/').Trim().TrimStart('/');
        string[] segments = clean.Split('/');
        if (clean.Length == 0 || segments.Any(segment => segment is "" or "." or ".."))
            return ("\"" + path + "\" is not a repository file path.", null);
        if (!files.TryGetValue(clean, out string[]? lines))
        {
            string relative = "/repos/" + Repository + "/contents/" + string.Join("/", segments.Select(Uri.EscapeDataString))
                + "?ref=" + Branch;
            string? body = await GetAsync(relative, "application/vnd.github.raw", cancellationToken).ConfigureAwait(false);
            if (body == null) return ("The file \"" + clean + "\" was not found on " + Branch + ".", null);
            lines = body.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
            if (lines.Length > 1 && lines[^1].Length == 0) lines = lines[..^1];
            files[clean] = lines;
        }
        int from = Math.Max(1, fromLine);
        if (from > lines.Length)
            return ("\"" + clean + "\" has " + lines.Length.ToString(CultureInfo.InvariantCulture) + " lines.", null);
        int to = Math.Min(lines.Length, Math.Min(Math.Max(from, toLine), from + MaxReadLines - 1));
        var text = new StringBuilder();
        text.Append(clean).Append(" lines ").Append(from.ToString(CultureInfo.InvariantCulture)).Append('-')
            .Append(to.ToString(CultureInfo.InvariantCulture)).Append(" of ")
            .Append(lines.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
        for (int line = from; line <= to; line++)
            text.Append(line.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(lines[line - 1]).Append('\n');
        return (text.ToString(), new HelpSource(clean, from, to));
    }

    /// <summary>GitHub code search in the repository: matching paths with a short matching fragment each.</summary>
    public async Task<string> SearchCodeAsync(string query, CancellationToken cancellationToken)
    {
        string terms = (query ?? "").Trim();
        if (terms.Length == 0) return "The search needs at least one term.";
        string relative = "/search/code?q=" + Uri.EscapeDataString(terms) + "+repo:" + Repository
            + "&per_page=" + MaxSearchResults.ToString(CultureInfo.InvariantCulture);
        string? body = await GetAsync(relative, "application/vnd.github.text-match+json", cancellationToken).ConfigureAwait(false);
        if (body == null) return "The search found nothing.";
        using var document = JsonDocument.Parse(body);
        var text = new StringBuilder();
        int count = 0;
        if (document.RootElement.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("path", out JsonElement path) || path.ValueKind != JsonValueKind.String) continue;
                text.Append(path.GetString()).Append('\n');
                if (item.TryGetProperty("text_matches", out JsonElement matches) && matches.ValueKind == JsonValueKind.Array
                    && matches.GetArrayLength() > 0 && matches[0].TryGetProperty("fragment", out JsonElement fragment)
                    && fragment.ValueKind == JsonValueKind.String)
                {
                    string compact = string.Join(" ", (fragment.GetString() ?? "")
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    if (compact.Length > MaxFragment) compact = compact[..MaxFragment] + "...";
                    text.Append("  ").Append(compact).Append('\n');
                }
                if (++count == MaxSearchResults) break;
            }
        return count == 0 ? "The search found nothing." : text.ToString();
    }

    private async Task<string[]> LoadTreeAsync(CancellationToken cancellationToken)
    {
        string? body = await GetAsync("/repos/" + Repository + "/git/trees/" + Branch + "?recursive=1",
            "application/vnd.github+json", cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException("The repository tree was not found.");
        using var document = JsonDocument.Parse(body);
        var paths = new List<string>();
        if (document.RootElement.TryGetProperty("tree", out JsonElement entries) && entries.ValueKind == JsonValueKind.Array)
            foreach (JsonElement entry in entries.EnumerateArray())
                if (entry.TryGetProperty("type", out JsonElement type) && type.GetString() == "blob"
                    && entry.TryGetProperty("path", out JsonElement path) && path.GetString() is { Length: > 0 } value)
                    paths.Add(value);
        return paths.ToArray();
    }

    /// <summary>The response body, or null for 404. A rate limit or a refused token stops the reading; any other
    /// failure is an <see cref="HttpRequestException"/>.</summary>
    private async Task<string?> GetAsync(string relative, string accept, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com" + relative);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (LimitResetsAt(response, body) is { } resetsAt) throw new HelpGitHubStopException(resetsAt);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new HelpGitHubStopException(null);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("GitHub answered " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ".");
        return body;
    }

    /// <summary>When a 403 or 429 is GitHub's primary limit (remaining 0) or a secondary limit, the time it resets.</summary>
    internal static DateTimeOffset? LimitResetsAt(HttpResponseMessage response, string body)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)) return null;
        string? remaining = Header(response, "x-ratelimit-remaining");
        bool secondary = response.Headers.RetryAfter != null
            || body.Contains("secondary rate limit", StringComparison.OrdinalIgnoreCase);
        if (response.StatusCode == HttpStatusCode.Forbidden && remaining != "0" && !secondary) return null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (response.Headers.RetryAfter?.Delta is { } delta) return now + delta;
        if (response.Headers.RetryAfter?.Date is { } date) return date;
        if (long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        return now.AddMinutes(1);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault()?.Trim() : null;
}
