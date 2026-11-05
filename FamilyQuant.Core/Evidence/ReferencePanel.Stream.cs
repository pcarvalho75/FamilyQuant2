using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence;

public static partial class ReferencePanelFetcher
{
    // Leave room for the request line and intermediaries' usual 8 KB limit. Measure the escaped URL,
    // including fields, rather than guessing a family count or depending on the organism's vocabulary.
    private const int MaximumStreamUrlLength = 7000;
    // UniProt also limits a combined query to 100 OR conditions, even below its URL limit.
    private const int MaximumFamiliesPerStream = 100;
    private static readonly string StreamEndpoint = Endpoint.Replace("/search?format=json&size=500", "/stream?format=json", StringComparison.Ordinal);
    private const string CountEndpoint = "https://rest.uniprot.org/uniprotkb/search?format=json&size=0&fields=accession&query=";

    private static async Task<ReferencePanel> FetchStreamsAsync(IReadOnlyList<string> families,
        HttpMessageHandler? handler, IProgress<FetchProgress>? progress, CancellationToken cancellationToken, string? expectedRelease,
        UniProtFetchOptions? options)
    {
        var asked = families.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal).ToList();
        var batches = StreamBatches(asked);
        var entries = new SortedDictionary<string, PanelEntry>(StringComparer.Ordinal);
        string? release = null;
        int received = 0, completed = 0;
        using HttpClient client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        var cache = batches.Count == 0 ? null : await UniProtBatchCache.OpenAsync(
            string.Join("\n", batches.Select(b => StreamEndpoint + Uri.EscapeDataString(StreamQuery(b)))),
            client, handler, options, message => Report(message), cancellationToken, expectedRelease).ConfigureAwait(false);
        release = cache?.Release;
        Report();
        foreach (List<string> batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string query = Uri.EscapeDataString(StreamQuery(batch));
            int announced;
            string url = StreamEndpoint + query;
            var saved = cache == null ? null : await cache.ReadAsync(url, cancellationToken).ConfigureAwait(false);
            string json;
            if (saved == null)
            {
                // Stream replies omit X-Total-Results. A zero-row search of the identical query supplies
                // the completeness count without downloading another copy of any protein evidence.
                using (HttpResponseMessage count = await GetWithRetry(client, CountEndpoint + query, cancellationToken, options,
                    message => Report(message)).ConfigureAwait(false))
                {
                    CheckRelease(count);
                    if (!count.Headers.TryGetValues("x-total-results", out IEnumerable<string>? totals)
                        || !int.TryParse(totals.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out announced)
                        || announced < 0)
                        throw new InvalidDataException("UniProt omitted the reviewed entry count. No panel was made.");
                }
                using HttpResponseMessage response = await GetWithRetry(client, url, cancellationToken, options,
                    message => Report(message)).ConfigureAwait(false);
                CheckRelease(response);
                if (NextPage(response) != null)
                    throw new InvalidDataException("UniProt returned an incomplete stream. No panel was made.");
                json = await ReadStreamJson(response.Content, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                announced = saved.Announced ?? throw new InvalidDataException("A saved panel batch has no completeness count.");
                json = saved.Json;
                Report("Resuming saved reference panel batches");
            }
            Dictionary<string, ProteinEvidence> parsed = EvidenceFetcher.Parse(json);
            var membership = batch.ToHashSet(StringComparer.OrdinalIgnoreCase);
            int delivered = 0;
            foreach ((string accession, string sequence) in Sequences(json))
            {
                cancellationToken.ThrowIfCancellationRequested();
                delivered++;
                if (!parsed.TryGetValue(accession, out ProteinEvidence? evidence) || evidence.Accession != accession)
                    throw new InvalidDataException("UniProt returned an entry it did not describe. No panel was made.");
                if (evidence.Reviewed != true)
                    throw new InvalidDataException("UniProt returned an unreviewed entry to a reviewed search. No panel was made.");
                if (!FamiliesOf(evidence).Any(membership.Contains)) continue;
                if (sequence.Length == 0 || entries.ContainsKey(accession)) continue;
                evidence.SimilarityAnnotations = null;
                entries.Add(accession, new PanelEntry { Accession = accession, Sequence = sequence, Evidence = evidence });
            }
            if (announced != delivered)
                throw new InvalidDataException("UniProt announced " + announced.ToString(CultureInfo.InvariantCulture)
                    + " reviewed entries and delivered " + delivered.ToString(CultureInfo.InvariantCulture) + ". No panel was made.");
            if (cache != null && saved == null) await cache.StoreAsync(url, json, announced, cancellationToken).ConfigureAwait(false);
            received += delivered;
            for (int i = 0; i < batch.Count; i++)
            {
                completed++;
                Report();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new ReferencePanel
        {
            Provider = "UniProt", Release = release ?? string.Empty, RetrievedAtUtc = cache?.RetrievedAtUtc ?? options?.UtcNow() ?? DateTimeOffset.UtcNow,
            Families = asked, Entries = entries.Values.ToList(),
        };

        void CheckRelease(HttpResponseMessage response)
        {
            string current = UniProtHttp.Release(response);
            if (current.Length == 0) throw new InvalidDataException("UniProt omitted its release. No panel was made.");
            if (expectedRelease != null && current != expectedRelease) throw new UniProtReleaseException();
            if (release != null && release != current)
                throw new UniProtReleaseException();
            release = current;
        }
        void Report(string message = "") => progress?.Report(new FetchProgress
        {
            CompletedBatches = completed, TotalBatches = asked.Count, Asked = received, Found = entries.Count, Message = message,
        });
    }

    private static async Task<string> ReadStreamJson(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentEncoding.Any(e => !e.Equals("gzip", StringComparison.OrdinalIgnoreCase)
            && !e.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("UniProt returned an unsupported stream encoding. No panel was made.");
        byte[] bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        int layers = 0;
        // Some stream replies are gzip files without Content-Encoding; a proxy can also wrap
        // an already compressed file in gzip transfer encoding. Decode the actual lossless layers.
        while (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            if (++layers > 4) throw new InvalidDataException("UniProt returned too many compression layers. No panel was made.");
            using var source = new MemoryStream(bytes, writable: false);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            await gzip.CopyToAsync(decoded, cancellationToken).ConfigureAwait(false);
            bytes = decoded.ToArray();
        }
        if (layers == 0)
        {
            if (content.Headers.ContentEncoding.Any(e => e.Equals("gzip", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("UniProt returned invalid compressed data. No panel was made.");
            return await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false), System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string StreamQuery(IEnumerable<string> families) => "(reviewed:true) AND ("
        + string.Join(" OR ", families.Select(f => "(family:\"" + f.Replace("\"", "") + "\")")) + ")";

    private static List<List<string>> StreamBatches(List<string> families)
    {
        var batches = new List<List<string>>();
        var current = new List<string>();
        foreach (string family in families)
        {
            current.Add(family);
            if (current.Count <= MaximumFamiliesPerStream
                && StreamEndpoint.Length + Uri.EscapeDataString(StreamQuery(current)).Length <= MaximumStreamUrlLength) continue;
            current.RemoveAt(current.Count - 1);
            if (current.Count > 0) batches.Add(current);
            current = new List<string> { family };
            if (StreamEndpoint.Length + Uri.EscapeDataString(StreamQuery(current)).Length > MaximumStreamUrlLength)
                throw new InvalidDataException("A family name exceeds the UniProt query URL limit. No panel was made.");
        }
        if (current.Count > 0) batches.Add(current);
        return batches;
    }
}
