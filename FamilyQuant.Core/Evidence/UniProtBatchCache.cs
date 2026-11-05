using System;
using System.IO;
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
    private sealed record Manifest(string Release, DateTimeOffset RetrievedAtUtc);
    internal sealed record Batch(string Query, string Release, string Json, int? Announced, string Sha256);
    private readonly string directory;
    private readonly Manifest manifest;
    private bool disabled;
    internal string Release => manifest.Release;
    internal DateTimeOffset RetrievedAtUtc => manifest.RetrievedAtUtc;

    private UniProtBatchCache(string directory, Manifest manifest) { this.directory = directory; this.manifest = manifest; }

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
            string directory = Path.Combine(Path.GetFullPath(root), Hash(query));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "manifest.json");
            Manifest? manifest = null;
            if (File.Exists(path))
            {
                try { manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false)); }
                catch (JsonException) { }
            }
            if (manifest?.Release != release)
            {
                // Only this exact query's flat JSON cache is discarded. No recursive deletion or source paths.
                foreach (string saved in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)) File.Delete(saved);
                manifest = new Manifest(release, options?.UtcNow() ?? DateTimeOffset.UtcNow);
                await WriteAsync(path, manifest, token).ConfigureAwait(false);
            }
            return new UniProtBatchCache(directory, manifest!);
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
            string path = Path.Combine(directory, Hash(query) + ".json");
            if (!File.Exists(path)) return null;
            try
            {
                var batch = JsonSerializer.Deserialize<Batch>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
                if (batch != null && batch.Query == query && batch.Release == Release && batch.Json != null && batch.Sha256 == Hash(batch.Json)) return batch;
            }
            catch (JsonException) { }
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { disabled = true; }
        return null;
    }

    internal async Task StoreAsync(string query, string json, int? announced, CancellationToken token)
    {
        if (disabled) return;
        try { await WriteAsync(Path.Combine(directory, Hash(query) + ".json"), new Batch(query, Release, json, announced, Hash(json)), token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { disabled = true; }
    }

    private static async Task WriteAsync<T>(string path, T value, CancellationToken token)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
