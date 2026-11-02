using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence;

public sealed class EvidenceTaxon
{
    public int TaxonId { get; set; }
    public string ScientificName { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
}

public sealed class EvidenceKeywordCount
{
    public string Keyword { get; set; } = string.Empty;
    public string KeywordId { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int ReviewedProteins { get; set; }
}

public sealed class EvidenceTaskProposal
{
    public string SourceFingerprint { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Release { get; set; } = string.Empty;
    public List<string> KeywordEvidenceReleases { get; set; } = new();
    public int TargetProteins { get; set; }
    public int IgnoredContaminants { get; set; }
    public int ReviewedProteins { get; set; }
    public List<EvidenceTaxon> Taxa { get; set; } = new();
    public EvidenceTaxon Clade { get; set; } = new();
    public List<EvidenceKeywordCount> Keywords { get; set; } = new();
    public List<string> ProposedScopeTerms { get; set; } = new();
}

public sealed class EvidenceTaskChoice
{
    public EvidenceTaskProposal Proposal { get; set; } = new();
    public List<string> ScopeTerms { get; set; } = new();
}

public static class EvidenceTaskDiscovery
{
    // UniProt's ontology category identifier for molecular function, not a list of biological terms.
    // Functional scope is suggested independently of location, structure and technical annotation.
    public const string FunctionalScopeCategoryId = "KW-9992";
    private static readonly Regex Organism = new(@"(?:^|\s)OS=(?<name>.+?)\sOX=(?<id>[1-9][0-9]*)(?=\s|$)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Read-only discovery. The nearest common taxonomic family is preferred; otherwise the
    /// deepest common ancestor is offered. Keyword counts use distinct reviewed accessions in the file,
    /// each counted once per keyword. The most frequent keyword in UniProt's molecular-function
    /// category is offered as functional scope. No scope is applied until a person confirms it.</summary>
    public static async Task<EvidenceTaskProposal> DiscoverAsync(FamilyClusterFile file, HttpMessageHandler? handler = null,
        IProgress<FetchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        string fingerprint = SourceFingerprint(file);
        var targets = file.MyExtendedFastaItems.Where(p => !NearestReviewed.IsDecoy(p)).ToList();
        var proteins = targets.Where(p => !IsContaminant(p)).ToList();
        if (proteins.Count == 0) throw new InvalidDataException("There are no non-contaminant target proteins to read.");
        var headers = proteins.Select(Header).ToList();
        var taxa = headers.GroupBy(h => h.TaxonId).OrderBy(g => g.Key).Select(g => new EvidenceTaxon
        {
            TaxonId = g.Key, ScientificName = string.Join("; ", g.Select(h => h.ScientificName).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal)),
        }).ToList();
        var lineages = new List<List<EvidenceTaxon>>();
        string? release = null;
        using HttpClient client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");
        for (int i = 0; i < taxa.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = "https://rest.uniprot.org/taxonomy/" + taxa[i].TaxonId.ToString(CultureInfo.InvariantCulture);
            using HttpResponseMessage response = await UniProtHttp.GetWithRetryAsync(client, url, 3,
                status => "UniProt refused the taxonomy request (HTTP " + status.ToString(CultureInfo.InvariantCulture) + "). No task was proposed.", cancellationToken).ConfigureAwait(false);
            string current = UniProtHttp.Release(response);
            if (current.Length == 0 || (release != null && release != current))
                throw new InvalidDataException("UniProt omitted its release or changed releases during taxonomy discovery. No task was proposed.");
            release = current;
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            EvidenceTaxon taxon = ReadTaxon(root);
            if (taxon.TaxonId != taxa[i].TaxonId || !root.TryGetProperty("active", out var active) || active.ValueKind != JsonValueKind.True)
                throw new InvalidDataException("UniProt did not return the active taxonomy entry requested. No task was proposed.");
            if (!root.TryGetProperty("lineage", out var lineage) || lineage.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("UniProt omitted a taxonomic lineage. No task was proposed.");
            var path = lineage.EnumerateArray().Select(ReadTaxon).Append(taxon).ToList();
            if (path.Select(t => t.TaxonId).Distinct().Count() != path.Count)
                throw new InvalidDataException("UniProt returned a repeated taxon in a lineage. No task was proposed.");
            lineages.Add(path);
            taxa[i].Rank = taxon.Rank;
            progress?.Report(new FetchProgress { CompletedBatches = i + 1, TotalBatches = taxa.Count, Asked = taxa.Count, Found = i + 1 });
        }
        var common = lineages[0].Where(t => lineages.All(path => path.Any(p => p.TaxonId == t.TaxonId))).ToList();
        EvidenceTaxon clade = common.LastOrDefault(t => t.Rank == "family") ?? common.LastOrDefault()
            ?? throw new InvalidDataException("The file's taxa have no shared lineage in UniProt. No clade was guessed.");
        var reviewed = proteins.Where(p => p.Evidence?.Reviewed == true
                && p.Evidence.Accession == FamilyNaming.Accession(p.SequenceIdentifier).Split('-')[0])
            .GroupBy(p => p.Evidence!.Accession, StringComparer.Ordinal).Select(g => g.First()).ToList();
        // Duplicate accessions cannot silently select contradictory keyword observations.
        foreach (var group in proteins.Where(p => p.Evidence?.Reviewed == true).GroupBy(p => p.Evidence!.Accession, StringComparer.Ordinal))
            if (group.Select(p => JsonSerializer.Serialize(p.Evidence!.Keywords.OrderBy(k => k, StringComparer.Ordinal))).Distinct().Count() > 1)
                throw new InvalidDataException("Duplicate reviewed accessions have conflicting keyword evidence. Fetch evidence again.");
        var keywords = reviewed.SelectMany(p => p.Evidence!.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal))
            .GroupBy(k => k, StringComparer.Ordinal).Select(g => new EvidenceKeywordCount { Keyword = g.Key, ReviewedProteins = g.Count() })
            .OrderByDescending(k => k.ReviewedProteins).ThenBy(k => k.Keyword, StringComparer.Ordinal).ToList();
        if (keywords.Count > 0)
        {
            using HttpResponseMessage response = await UniProtHttp.GetWithRetryAsync(client,
                "https://rest.uniprot.org/keywords/stream?query=*&format=json", 3,
                status => "UniProt refused the keyword vocabulary (HTTP " + status.ToString(CultureInfo.InvariantCulture) + "). No task was proposed.", cancellationToken).ConfigureAwait(false);
            if (UniProtHttp.Release(response) != release)
                throw new InvalidDataException("The UniProt keyword vocabulary and taxonomy releases differ. No task was proposed.");
            using JsonDocument vocabulary = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var byName = keywords.ToDictionary(k => k.Keyword, StringComparer.Ordinal);
            foreach (var entry in vocabulary.RootElement.GetProperty("results").EnumerateArray())
            {
                var term = entry.GetProperty("keyword");
                string name = term.GetProperty("name").GetString() ?? string.Empty;
                if (!byName.TryGetValue(name, out var count)) continue;
                count.KeywordId = term.GetProperty("id").GetString() ?? string.Empty;
                if (!entry.TryGetProperty("category", out var category)) continue;
                count.CategoryId = category.GetProperty("id").GetString() ?? string.Empty;
                count.CategoryName = category.GetProperty("name").GetString() ?? string.Empty;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (fingerprint != SourceFingerprint(file)) throw new InvalidDataException("The file changed during task discovery. No task was proposed.");
        return new EvidenceTaskProposal
        {
            SourceFingerprint = fingerprint, Provider = "UniProt", Release = release ?? string.Empty,
            KeywordEvidenceReleases = reviewed.Select(p => p.Evidence!.Source?.Release ?? file.EvidenceSource?.Release ?? string.Empty)
                .Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList(),
            TargetProteins = proteins.Count, IgnoredContaminants = targets.Count - proteins.Count,
            ReviewedProteins = reviewed.Count, Taxa = taxa, Clade = clade, Keywords = keywords,
            ProposedScopeTerms = keywords.Where(k => k.CategoryId == FunctionalScopeCategoryId).Take(1).Select(k => k.Keyword).ToList(),
        };
    }

    public static EvidenceTaskChoice Confirm(FamilyClusterFile file, EvidenceTaskProposal proposal, IReadOnlyList<string> scopeTerms)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(scopeTerms);
        var choice = new EvidenceTaskChoice { Proposal = EvidenceApply.Clone(proposal),
            ScopeTerms = scopeTerms.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList() };
        Validate(file, choice, choice.ScopeTerms);
        return choice;
    }

    internal static void Validate(FamilyClusterFile file, EvidenceTaskChoice choice, IReadOnlyList<string> scope)
    {
        if (choice.Proposal.SourceFingerprint != SourceFingerprint(file) || choice.Proposal.Provider != "UniProt"
            || choice.Proposal.Release.Length == 0 || choice.Proposal.Clade.TaxonId <= 0 || choice.Proposal.Clade.ScientificName.Length == 0
            || choice.ScopeTerms.Count == 0 || choice.ScopeTerms.Any(string.IsNullOrWhiteSpace)
            || !choice.ScopeTerms.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(scope.OrderBy(s => s, StringComparer.Ordinal)))
            throw new InvalidOperationException("The confirmed task does not match this file and the reviewed scope. Discover and confirm the task again.");
    }

    // Group numbers and annotations are intentionally absent: applying a grouping must not invalidate
    // the source task. Descriptions are present because OS/OX can change independently of the sequence.
    private static string SourceFingerprint(FamilyClusterFile file)
    {
        using var hash = new LengthPrefixedSha256();
        foreach (var protein in file.MyExtendedFastaItems)
        {
            hash.Append(protein.SequenceIdentifier ?? string.Empty); hash.Append(protein.Description ?? string.Empty);
            hash.Append(protein.Sequence ?? string.Empty); hash.Append(JsonSerializer.Serialize(protein.Evidence));
        }
        return hash.Finish();
    }

    private static bool IsContaminant(FastaItemExtended protein) =>
        SequenceLabels.IsContaminant(protein.SequenceIdentifier);

    private static EvidenceTaxon Header(FastaItemExtended protein)
    {
        string identifier = protein.SequenceIdentifier ?? string.Empty;
        MatchCollection matches = Organism.Matches(protein.Description ?? string.Empty);
        if (!(identifier.StartsWith("sp|", StringComparison.Ordinal) || identifier.StartsWith("tr|", StringComparison.Ordinal))
            || !FamilyNaming.IsUniProtAccession(FamilyNaming.Accession(identifier)) || matches.Count == 0
            || !int.TryParse(matches[0].Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id)
            || matches.Cast<Match>().Any(match => !int.TryParse(match.Groups["id"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out int other) || other != id))
            throw new InvalidDataException("Cannot read a UniProt OS= and OX= header for '" + identifier + "'. No task was guessed; use UniProt headers or choose the scope manually.");
        return new EvidenceTaxon { TaxonId = id, ScientificName = matches[0].Groups["name"].Value.Trim() };
    }

    private static EvidenceTaxon ReadTaxon(JsonElement item)
    {
        if (!item.TryGetProperty("taxonId", out var id) || !id.TryGetInt32(out int number) || number <= 0
            || !item.TryGetProperty("scientificName", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString())
            || !item.TryGetProperty("rank", out var rank) || rank.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("UniProt returned an incomplete taxon. No task was proposed.");
        return new EvidenceTaxon { TaxonId = number, ScientificName = name.GetString()!, Rank = rank.GetString()! };
    }
}
