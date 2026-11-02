using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence;

internal static class UniProtHttp
{
    internal static string Release(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-uniprot-release", out IEnumerable<string>? values)
            ? values.FirstOrDefault() ?? string.Empty : string.Empty;

    internal static async Task<HttpResponseMessage> GetWithRetryAsync(HttpClient client, string url,
        int attempts, Func<int, string> refusalMessage, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try { response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false); }
            catch (HttpRequestException) when (attempt < attempts) { }
            catch (TaskCanceledException) when (attempt < attempts && !cancellationToken.IsCancellationRequested) { }
            if (response != null)
            {
                if (response.IsSuccessStatusCode) return response;
                int status = (int)response.StatusCode;
                if (attempt == attempts || (status != 429 && status < 500))
                {
                    response.Dispose();
                    throw new HttpRequestException(refusalMessage(status), null, response.StatusCode);
                }
                response.Dispose();
            }
            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
        }
    }
}
