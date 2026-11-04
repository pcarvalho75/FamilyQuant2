using FamilyQuant.Operations;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

/// <summary>Explicit user action only. No execution here. Redirects are followed manually through exact GitHub hosts.</summary>
public static class ReleaseInstallerDownload
{
    public static async Task<string> DownloadAsync(ReleaseInstallerAsset asset, string repository, string tag,
        string outputFolder, OperationContext operation, HttpMessageHandler? handler = null)
    {
        operation.ThrowIfCancellationRequested();
        if (!ReleaseUpdateChecker.IsInstallerAsset(asset, repository, tag)) throw new InvalidDataException("The installer asset is not a trusted release download.");
        if (asset.Size is <= 0) throw new InvalidDataException("The installer size is invalid.");
        string target = Path.Combine(Path.GetFullPath(outputFolder), asset.Name);
        string partial = target + ".download";
        Directory.CreateDirectory(outputFolder);
        using var client = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler, disposeHandler: false);
        client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(operation.CancellationToken);
        limit.CancelAfter(TimeSpan.FromMinutes(15));
        Uri next = asset.DownloadUrl;
        bool created = false;
        try
        {
            for (int hop = 0; hop < 6; hop++)
            {
                using var response = await client.GetAsync(next, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    Uri? location = response.Headers.Location;
                    if (location == null) throw new InvalidDataException("The installer redirect has no location.");
                    next = location.IsAbsoluteUri ? location : new Uri(next, location);
                    if (!IsAllowedRedirect(next, asset)) throw new InvalidDataException("The installer redirect host is not trusted.");
                    continue;
                }
                response.EnsureSuccessStatusCode();
                long? total = asset.Size ?? response.Content.Headers.ContentLength;
                if (asset.Size.HasValue && response.Content.Headers.ContentLength is long contentLength && contentLength != asset.Size)
                    throw new InvalidDataException("The installer length does not match the release asset.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long count = 0;
                using (var destination = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    created = true;
                    using var source = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
                    byte[] buffer = new byte[81920];
                    operation.Report("update-download", "Downloading the FamilyQuant installer.", 0, total);
                    int read;
                    while ((read = await source.ReadAsync(buffer, limit.Token).ConfigureAwait(false)) > 0)
                    {
                        count += read;
                        if (total.HasValue && count > total) throw new InvalidDataException("The installer exceeds its declared length.");
                        hash.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read), limit.Token).ConfigureAwait(false);
                        operation.Report("update-download", "Downloading the FamilyQuant installer.", count, total);
                    }
                }
                if (count == 0 || total.HasValue && count != total) throw new InvalidDataException("The installer download is incomplete.");
                string actual = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (asset.Sha256 != null && !actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The installer checksum does not match the release asset.");
                operation.ThrowIfCancellationRequested();
                File.Move(partial, target); // Never expose an incomplete executable or replace an existing file.
                return target;
            }
            throw new InvalidDataException("The installer has too many redirects.");
        }
        finally
        {
            if (created && File.Exists(partial)) File.Delete(partial);
        }
    }

    private static bool IsAllowedRedirect(Uri uri, ReleaseInstallerAsset asset) => uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && (uri == asset.DownloadUrl || uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));
}
