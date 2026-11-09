using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence;

public static partial class EvidenceTaskDiscovery
{
    // One search page holds a whole batch (at most MaximumTaxaPerBatch results), and ReadTaxonomyBatch
    // refuses a reply that omits a requested taxon. The stream endpoint failed with 502 on 2026-11-09.
    private const string TaxonomySearchEndpoint = "https://rest.uniprot.org/taxonomy/search?format=json&size=500&query=";
    private const int MaximumTaxonomyUrlLength = 7000;
    private const int MaximumTaxaPerBatch = 100;

    private static async Task<(Dictionary<int, List<EvidenceTaxon>> Paths, string Release)> FetchTaxonomyAsync(
        List<int> taxa, HttpClient client, HttpMessageHandler? handler, IProgress<FetchProgress>? progress,
        CancellationToken token, UniProtFetchOptions? options, bool perTaxonComparison)
    {
        var batches = TaxonomyBatches(taxa, perTaxonComparison);
        var paths = new Dictionary<int, List<EvidenceTaxon>>();
        // The comparison route retains the old single-entry payload for proposal parity checks.
        var cache = perTaxonComparison ? null : await UniProtBatchCache.OpenAsync(
            string.Join("\n", batches.Select(b => TaxonomyUrl(b, false))), client, handler, options,
            Report, token).ConfigureAwait(false);
        string? release = cache?.Release;
        foreach (List<int> batch in batches)
        {
            token.ThrowIfCancellationRequested();
            string url = TaxonomyUrl(batch, perTaxonComparison);
            var saved = cache == null ? null : await cache.ReadAsync(url, token).ConfigureAwait(false);
            string json;
            if (saved != null) json = saved.Json;
            else
            {
                using var response = await UniProtHttp.GetWithRetryAsync(client, url, 5,
                    status => "UniProt refused the taxonomy request (HTTP " + status.ToString(CultureInfo.InvariantCulture)
                        + "). No task was proposed.", token, options, Report).ConfigureAwait(false);
                string current = UniProtHttp.Release(response);
                if (current.Length == 0 || (release != null && release != current))
                    throw new InvalidDataException("UniProt omitted its release or changed releases during taxonomy discovery. No task was proposed.");
                release = current;
                json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }
            // Validate the entire reply before caching or combining it. A partial batch cannot
            // produce a proposal, and response order cannot change the selected common ancestor.
            var fetched = ReadTaxonomyBatch(json, batch, perTaxonComparison);
            token.ThrowIfCancellationRequested();
            if (saved == null && cache != null) await cache.StoreAsync(url, json, batch.Count, token).ConfigureAwait(false);
            foreach (var pair in fetched) paths.Add(pair.Key, pair.Value);
            Report(saved == null ? string.Empty : "Using saved taxonomy batches");
        }
        return (paths, release ?? string.Empty);

        void Report(string message) => progress?.Report(new FetchProgress
        {
            // Task discovery's existing busy labels and operation logs count taxa, not requests.
            CompletedBatches = paths.Count, TotalBatches = taxa.Count, Asked = taxa.Count, Found = paths.Count, Message = message,
        });
    }

    private static Dictionary<int, List<EvidenceTaxon>> ReadTaxonomyBatch(string json, List<int> requested, bool single)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var entries = new List<JsonElement>();
        if (single) entries.Add(root);
        else
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("UniProt omitted the taxonomy results. No task was proposed.");
            entries.AddRange(results.EnumerateArray());
        }
        var wanted = requested.ToHashSet();
        var paths = new Dictionary<int, List<EvidenceTaxon>>();
        foreach (JsonElement entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("UniProt returned an incomplete taxon. No task was proposed.");
            EvidenceTaxon taxon = ReadTaxon(entry);
            if (!wanted.Contains(taxon.TaxonId) || paths.ContainsKey(taxon.TaxonId)
                || !entry.TryGetProperty("active", out var active) || active.ValueKind != JsonValueKind.True)
                throw new InvalidDataException("UniProt did not return the active taxonomy entries requested. No task was proposed.");
            if (!entry.TryGetProperty("lineage", out var lineage) || lineage.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("UniProt omitted a taxonomic lineage. No task was proposed.");
            var path = lineage.EnumerateArray().Select(ReadTaxon).Append(taxon).ToList();
            if (path.Select(t => t.TaxonId).Distinct().Count() != path.Count)
                throw new InvalidDataException("UniProt returned a repeated taxon in a lineage. No task was proposed.");
            paths.Add(taxon.TaxonId, path);
        }
        if (paths.Count != wanted.Count)
            throw new InvalidDataException("UniProt omitted a requested taxonomic lineage. No task was proposed.");
        return paths;
    }

    private static List<List<int>> TaxonomyBatches(List<int> taxa, bool single)
    {
        var batches = new List<List<int>>();
        var batch = new List<int>();
        foreach (int id in taxa)
        {
            if (batch.Count > 0 && (batch.Count >= (single ? 1 : MaximumTaxaPerBatch)
                || TaxonomyUrl(batch.Append(id), false).Length > MaximumTaxonomyUrlLength))
            {
                batches.Add(batch);
                batch = new List<int>();
            }
            batch.Add(id);
        }
        if (batch.Count > 0) batches.Add(batch);
        return batches;
    }

    private static string TaxonomyUrl(IEnumerable<int> taxa, bool single) => single
        ? "https://rest.uniprot.org/taxonomy/" + taxa.Single().ToString(CultureInfo.InvariantCulture)
        : TaxonomySearchEndpoint + Uri.EscapeDataString(string.Join(" OR ", taxa.Select(id => "(tax_id:" + id.ToString(CultureInfo.InvariantCulture) + ")")));
}
