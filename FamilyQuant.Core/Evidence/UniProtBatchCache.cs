using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence;

/// <summary>Only complete, validated replies are stored. A fresh release probe precedes every reuse.</summary>
internal sealed class UniProtBatchCache
{
    private const string ReleaseUrl = "https://rest.uniprot.org/uniprotkb/search?format=json&size=0&fields=accession&query=*";
    private sealed record Manifest(string? Scope, string Release, DateTimeOffset RetrievedAtUtc, List<string>? SplitQueries = null);
    internal sealed record Batch(string Query, string Release, string Json, int? Announced, string Sha256, string? Scope = null);
    private readonly string directory;
    private Manifest manifest;
    private readonly string? legacyDirectory;
    private bool disabled;
    internal string Release => manifest.Release;
    internal DateTimeOffset RetrievedAtUtc => manifest.RetrievedAtUtc;

    internal bool WasSplit(string query) => manifest.SplitQueries?.Contains(query, StringComparer.Ordinal) == true;

    internal async Task MarkSplitAsync(string query, CancellationToken token)
    {
        if (disabled || WasSplit(query)) return;
        manifest = manifest with { SplitQueries = (manifest.SplitQueries ?? new List<string>()).Append(query).OrderBy(value => value, StringComparer.Ordinal).ToList() };
        try { await WriteAsync(Path.Combine(directory, "m-" + manifest.Scope![..24] + ".json"), manifest, token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { disabled = true; }
    }

    private UniProtBatchCache(string directory, Manifest manifest, string? legacyDirectory)
    { this.directory = directory; this.manifest = manifest; this.legacyDirectory = legacyDirectory; }

    internal static async Task<UniProtBatchCache?> OpenAsync(string query, HttpClient client, HttpMessageHandler? handler,
        UniProtFetchOptions? options, Action<string> progress, CancellationToken token, string? expectedRelease = null)
    {
        string? root = options?.ResumeDirectory ?? (handler == null ? ApplicationPaths.EvidenceFetchCacheDirectory : null);
        if (root == null) return null;
        using var response = await UniProtHttp.GetWithRetryAsync(client, ReleaseUrl, 5,
            status => "UniProt refused the release check (HTTP " + status.ToString(System.Globalization.CultureInfo.InvariantCulture) + ").",
            token, options, progress).ConfigureAwait(false);
        string release = UniProtHttp.Release(response);
        if (release.Length == 0) throw new InvalidDataException("UniProt omitted its release. Saved batches were not used.");
        if (expectedRelease != null && expectedRelease != release) throw new UniProtReleaseException();
        try
        {
            string directory = Path.GetFullPath(root);
            Directory.CreateDirectory(directory);
            string scope = Hash(query);
            string path = Path.Combine(directory, "m-" + scope[..24] + ".json");
            Manifest? manifest = await ReadJsonAsync<Manifest>(path, token).ConfigureAwait(false);
            string legacy = Path.Combine(directory, scope);
            Manifest? old = null;
            try { old = await ReadJsonAsync<Manifest>(Path.Combine(legacy, "manifest.json"), token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (manifest == null || manifest.Scope != scope || manifest.Release != release || manifest.RetrievedAtUtc == default)
            {
                // Short flat names identify a cache candidate; full scope/query/release and reply hashes
                // still validate it. Even a shortened-name collision cannot supply another query's data.
                foreach (string saved in Directory.EnumerateFiles(directory, "b-" + scope[..12] + "-*.json", SearchOption.TopDirectoryOnly))
                {
                    var batch = await ReadJsonAsync<Batch>(saved, token).ConfigureAwait(false);
                    if (batch?.Scope == scope) File.Delete(saved);
                }
                if (old != null && old.Release != release)
                    foreach (string saved in Directory.EnumerateFiles(legacy, "*.json", SearchOption.TopDirectoryOnly)) File.Delete(saved);
                manifest = new Manifest(scope, release, old != null && old.Release == release && old.RetrievedAtUtc != default
                    ? old.RetrievedAtUtc : options?.UtcNow() ?? DateTimeOffset.UtcNow);
                await WriteAsync(path, manifest, token).ConfigureAwait(false);
            }
            return new UniProtBatchCache(directory, manifest!, old?.Release == release ? legacy : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progress("Saved batches are unavailable; fetching from UniProt");
            return null;
        }
    }

    internal async Task<Batch?> ReadAsync(string query, CancellationToken token)
    {
        if (disabled) return null;
        try
        {
            string path = BatchPath(query);
            var batch = await ReadJsonAsync<Batch>(path, token).ConfigureAwait(false);
            if (Valid(batch, query) && batch!.Scope == manifest.Scope) return batch;
            if (File.Exists(path)) File.Delete(path);
            if (legacyDirectory != null)
            {
                string previous = Path.Combine(legacyDirectory, Hash(query) + ".json");
                batch = await ReadJsonAsync<Batch>(previous, token).ConfigureAwait(false);
                if (Valid(batch, query))
                {
                    await StoreAsync(query, batch!.Json, batch.Announced, token).ConfigureAwait(false);
                    if (!disabled) File.Delete(previous);
                    return batch;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { disabled = true; }
        return null;
    }

    internal async Task StoreAsync(string query, string json, int? announced, CancellationToken token)
    {
        if (disabled) return;
        try { await WriteAsync(BatchPath(query), new Batch(query, Release, json, announced, Hash(json), manifest.Scope), token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { disabled = true; }
    }

    private static async Task WriteAsync<T>(string path, T value, CancellationToken token)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, "~" + Guid.NewGuid().ToString("N")[..12] + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                await JsonSerializer.SerializeAsync(stream, value, cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private string BatchPath(string query) => Path.Combine(directory, "b-" + manifest.Scope![..12] + "-" + Hash(Release + "\n" + query)[..12] + ".json");

    private bool Valid(Batch? batch, string query) => batch != null && batch.Query == query && batch.Release == Release
        && batch.Json != null && batch.Sha256 == Hash(batch.Json);

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken token) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false)); }
        catch (JsonException) { return null; }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
