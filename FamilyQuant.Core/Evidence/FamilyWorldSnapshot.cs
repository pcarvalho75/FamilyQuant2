using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FamilyQuant.Operations;

namespace FamilyQuant.Evidence;

public sealed class FamilyWorldEntry
{
    public string Accession { get; set; } = string.Empty;
    public string Sequence { get; set; } = string.Empty;
    public int TaxonId { get; set; }
    public List<int> LineageTaxonIds { get; set; } = new();
    public ProteinEvidence Evidence { get; set; } = new();
    public bool FromReviewedPanel { get; set; }
    public bool FromClade { get; set; }
}

public sealed class FamilyWorldQuery
{
    public string Family { get; set; } = string.Empty;
    public string Partition { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public int Received { get; set; }
    public int Kept { get; set; }
}

public sealed class FamilyWorldSnapshot
{
    public string Provider { get; set; } = string.Empty;
    public string Release { get; set; } = string.Empty;
    public DateTimeOffset? RetrievedAtUtc { get; set; }
    public EvidenceTaskChoice Task { get; set; } = new();
    public List<string> Families { get; set; } = new();
    public List<FamilyWorldEntry> Entries { get; set; } = new();
    public List<FamilyWorldQuery> Queries { get; set; } = new();
    public string ContentSha256 { get; set; } = string.Empty;
}

public sealed partial class EvidenceProvenance
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FamilyWorldSnapshot? FamilyWorld { get; set; }
}

public static class FamilyWorldFetcher
{
    private const string Endpoint = "https://rest.uniprot.org/uniprotkb/search?format=json&size=500&fields="
        + "accession,reviewed,protein_families,keyword,cc_tissue_specificity,xref_pfam,xref_panther,ft_transmem,fragment,sequence,organism_id,lineage_ids&query=";

    public static async Task<FamilyWorldSnapshot> FetchAndStoreAsync(FamilyClusterFile file, EvidenceTaskChoice task,
        OperationContext operation, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EvidenceTaskDiscovery.Validate(file, task, task.ScopeTerms);
        task = EvidenceApply.Clone(task);
        var originals = file.MyExtendedFastaItems.ToArray();
        string initial = EvidenceClassifier.Fingerprint(originals);
        string source = JsonSerializer.Serialize(file.EvidenceSource);
        string domainSource = file.DomainSource;
        var families = NearestReviewed.Targets(file).Where(p => !(p.SequenceIdentifier ?? string.Empty).StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.Ordinal))
            .Select(p => p.Evidence).Where(e => e != null && EvidenceClassifier.IsInScope(e, task.ScopeTerms))
            .SelectMany(e => ReferencePanelFetcher.FamiliesOf(e!)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal).ToList();
        if (families.Count == 0) throw new InvalidDataException("No annotated family is in the confirmed scope. No world was fetched.");
        var entries = new SortedDictionary<string, FamilyWorldEntry>(StringComparer.Ordinal);
        var receipts = new List<FamilyWorldQuery>();
        string? release = null;
        using var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");
        foreach (bool reviewed in new[] { true, false })
            foreach (string family in families)
            {
                string query = (reviewed ? "(reviewed:true)" : "(taxonomy_id:" + task.Proposal.Clade.TaxonId.ToString(CultureInfo.InvariantCulture) + ")")
                    + " AND (family:\"" + family.Replace("\"", string.Empty) + "\")";
                var receipt = new FamilyWorldQuery { Family = family, Partition = reviewed ? "reviewed" : "clade", Query = query };
                string? url = Endpoint + Uri.EscapeDataString(query);
                int? announced = null;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (url != null)
                {
                    operation.ThrowIfCancellationRequested();
                    using var response = await UniProtHttp.GetWithRetryAsync(client, url, 3,
                        status => "UniProt refused the family-world page (HTTP " + status.ToString(CultureInfo.InvariantCulture) + "). No world was attached.",
                        operation.CancellationToken).ConfigureAwait(false);
                    string current = UniProtHttp.Release(response);
                    if (current.Length == 0 || (release != null && release != current) || current != task.Proposal.Release)
                        throw new InvalidDataException("UniProt omitted or changed the release after task confirmation. No world was attached.");
                    release = current;
                    if (!response.Headers.TryGetValues("x-total-results", out var totals)
                        || !int.TryParse(totals.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out int total) || total < 0
                        || (announced.HasValue && announced != total))
                        throw new InvalidDataException("UniProt omitted or changed the family-world result count. No world was attached.");
                    announced = total;
                    string json = await response.Content.ReadAsStringAsync(operation.CancellationToken).ConfigureAwait(false);
                    var parsed = EvidenceFetcher.Parse(json);
                    using var document = JsonDocument.Parse(json);
                    foreach (var item in document.RootElement.GetProperty("results").EnumerateArray())
                    {
                        string accession = item.GetProperty("primaryAccession").GetString() ?? string.Empty;
                        if (!seen.Add(accession)) throw new InvalidDataException("A family-world query repeated an accession. No world was attached.");
                        receipt.Received++;
                        var evidence = parsed[accession];
                        if (evidence.Reviewed == null || (reviewed && evidence.Reviewed != true))
                            throw new InvalidDataException("UniProt did not supply the requested review status. No world was attached.");
                        if (!ReferencePanelFetcher.FamiliesOf(evidence).Contains(family, StringComparer.OrdinalIgnoreCase)) continue;
                        string sequence = item.GetProperty("sequence").GetProperty("value").GetString() ?? string.Empty;
                        int taxon = item.GetProperty("organism").GetProperty("taxonId").GetInt32();
                        var lineage = item.GetProperty("lineages").EnumerateArray().Select(t => t.GetProperty("taxonId").GetInt32()).ToList();
                        if (sequence.Length == 0 || taxon <= 0 || lineage.Count == 0 || lineage.Any(id => id <= 0)
                            || lineage.Distinct().Count() != lineage.Count || (!reviewed && taxon != task.Proposal.Clade.TaxonId && !lineage.Contains(task.Proposal.Clade.TaxonId)))
                            throw new InvalidDataException("UniProt omitted a sequence/taxonomy lineage or returned an entry outside the confirmed clade. No world was attached.");
                        receipt.Kept++;
                        if (entries.TryGetValue(accession, out var prior))
                        {
                            if (prior.Sequence != sequence || prior.TaxonId != taxon || !prior.LineageTaxonIds.SequenceEqual(lineage)
                                || JsonSerializer.Serialize(prior.Evidence) != JsonSerializer.Serialize(evidence))
                                throw new InvalidDataException("An entry changed between family-world queries. No world was attached.");
                            prior.FromReviewedPanel |= reviewed; prior.FromClade |= !reviewed;
                        }
                        else entries.Add(accession, new FamilyWorldEntry { Accession = accession, Sequence = sequence, TaxonId = taxon,
                            LineageTaxonIds = lineage, Evidence = evidence, FromReviewedPanel = reviewed, FromClade = !reviewed });
                    }
                    url = ReferencePanelFetcher.NextPage(response);
                }
                if (receipt.Received != announced) throw new InvalidDataException("A family-world query ended before all announced entries arrived. No world was attached.");
                receipts.Add(receipt);
                operation.Report("Family world", receipts.Count.ToString(CultureInfo.InvariantCulture) + " of "
                    + (families.Count * 2).ToString(CultureInfo.InvariantCulture) + " family queries; " + entries.Count.ToString(CultureInfo.InvariantCulture) + " entries kept",
                    receipts.Count, families.Count * 2);
            }
        var world = new FamilyWorldSnapshot { Provider = "UniProt", Release = release ?? string.Empty, RetrievedAtUtc = DateTimeOffset.UtcNow,
            Task = task, Families = families, Entries = entries.Values.ToList(), Queries = receipts };
        world.ContentSha256 = ContentHash(world);
        Verify(world);
        var attached = JsonSerializer.Deserialize<EvidenceProvenance>(source) ?? new EvidenceProvenance();
        attached.FamilyWorld = world;
        operation.Report("Family world", "Publishing the complete family world", families.Count * 2, families.Count * 2);
        operation.Commit(() =>
        {
            if (file.MyExtendedFastaItems.Count != originals.Length || originals.Where((p, index) => !ReferenceEquals(p, file.MyExtendedFastaItems[index])).Any()
                || EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems) != initial || JsonSerializer.Serialize(file.EvidenceSource) != source || file.DomainSource != domainSource)
                throw new InvalidDataException("The file changed during the family-world fetch. No world was attached.");
            operation.ThrowIfCancellationRequested();
            file.EvidenceSource = attached;
        });
        return world;
    }

    public static FamilyWorldSnapshot Stored(FamilyClusterFile file)
    {
        var world = file.EvidenceSource?.FamilyWorld ?? throw new InvalidDataException("This file has no stored family world.");
        Verify(world);
        EvidenceTaskDiscovery.Validate(file, world.Task, world.Task.ScopeTerms);
        return world;
    }

    public static void Verify(FamilyWorldSnapshot world)
    {
        if (world.Provider != "UniProt" || world.Release.Length == 0 || world.Families.Count == 0 || world.Task.Proposal.Clade.TaxonId <= 0
            || world.Queries.Count != world.Families.Count * 2 || world.Entries.Select(e => e.Accession).Distinct(StringComparer.Ordinal).Count() != world.Entries.Count
            || world.Entries.Any(e => e.Sequence.Length == 0 || e.TaxonId <= 0 || e.LineageTaxonIds.Count == 0 || e.Evidence.Reviewed == null
                || (!e.FromReviewedPanel && !e.FromClade) || (e.FromReviewedPanel && e.Evidence.Reviewed != true)
                || !ReferencePanelFetcher.FamiliesOf(e.Evidence).Intersect(world.Families, StringComparer.OrdinalIgnoreCase).Any())
            || world.ContentSha256 != ContentHash(world))
            throw new InvalidDataException("The stored family world is incomplete or its content hash differs.");
    }

    public static string ContentHash(FamilyWorldSnapshot world) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    { world.Provider, world.Release, world.Task, world.Families, world.Entries, world.Queries }))));
}
