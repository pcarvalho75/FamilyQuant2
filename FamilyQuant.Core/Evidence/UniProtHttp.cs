using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence;

internal sealed class UniProtReleaseException() : System.IO.IOException(
    "The UniProt release changed between fetched batches. No evidence was applied.");

internal static class UniProtHttp
{
    internal static string Release(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-uniprot-release", out IEnumerable<string>? values)
            ? values.FirstOrDefault() ?? string.Empty : string.Empty;

    internal static async Task<HttpResponseMessage> GetWithRetryAsync(HttpClient client, string url,
        int attempts, Func<int, string> refusalMessage, CancellationToken cancellationToken,
        UniProtFetchOptions? options = null, Action<string>? retryProgress = null)
    {
        int[] waits = [5, 15, 45, 120];
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage? response = null;
            try { response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false); }
            catch (HttpRequestException) when (attempt < attempts) { }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= attempts) throw new HttpRequestException("UniProt timed out after repeated requests.", ex);
            }
            if (response != null)
            {
                if (response.IsSuccessStatusCode) return response;
                int status = (int)response.StatusCode;
                if (attempt >= attempts || (status != 429 && status is not (>= 500 and <= 599)))
                {
                    response.Dispose();
                    throw new HttpRequestException(refusalMessage(status), null, response.StatusCode);
                }
                response.Dispose();
            }
            int seconds = waits[Math.Min(attempt - 1, waits.Length - 1)];
            string provider = new Uri(url).Host.Equals("www.ebi.ac.uk", StringComparison.OrdinalIgnoreCase) ? "InterPro" : "UniProt";
            retryProgress?.Invoke(provider + " busy, retrying in " + seconds.ToString(System.Globalization.CultureInfo.CurrentCulture) + " s");
            cancellationToken.ThrowIfCancellationRequested();
            await (options?.DelayAsync ?? Task.Delay)(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
