using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence
{
    public sealed class FetchProgress
    {
        public int CompletedBatches { get; init; }
        public int TotalBatches { get; init; }
        public int Asked { get; init; }
        public int Found { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class EvidenceFetchResult
    {
        public int Asked { get; internal set; }
        public int Found { get; internal set; }
        public int WithDomains { get; internal set; }
        public int PositionAsked { get; internal set; }
        public int PositionFound { get; internal set; }
        public double PositionElapsedSeconds { get; internal set; }
        public int Missing => Asked - Found;
        public EvidenceProvenance Provenance { get; internal set; } = new EvidenceProvenance();
        public string Source => Provenance.Provider + (Provenance.Release.Length == 0 ? "" : " " + Provenance.Release);
    }

    public static class EvidenceFetcher
    {
        private const string Endpoint = "https://rest.uniprot.org/uniprotkb/accessions?format=json&size=500&fields="
            + "accession,reviewed,organism_name,organism_id,protein_families,keyword,cc_tissue_specificity,xref_pfam,xref_panther,ft_transmem,fragment,sequence,ft_chain,ft_peptide,ft_propep,ft_signal&accessions=";
        private const int Attempts = 5;
        private static readonly Regex MembershipStart = new Regex(@"\bbelongs\s+to\s+(?:the\s+)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HierarchyBreak = new Regex(@"(?<=family)[.;,]\s*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static Dictionary<string, ProteinEvidence> Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            var entries = new Dictionary<string, ProteinEvidence>(StringComparer.Ordinal);
            try
            {
                foreach (JsonElement item in document.RootElement.GetProperty("results").EnumerateArray())
                {
                    ProteinEvidence evidence = ReadEntry(item);
                    entries.Add(evidence.Accession, evidence);
                    if (item.TryGetProperty("secondaryAccessions", out JsonElement secondary))
                        foreach (JsonElement alias in secondary.EnumerateArray())
                            entries.TryAdd(alias.GetString() ?? string.Empty, evidence);
                }
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex is InvalidOperationException || ex is ArgumentException)
            {
                throw new JsonException("The reply was not a valid UniProt evidence list.", ex);
            }
            return entries;
        }

        public static string DomainNames(ProteinEvidence evidence) => string.Join(ProteinDomains.Separator,
            evidence.Pfam.Select(d => d.Name.Length == 0 ? d.Accession : d.Name));

        private static ProteinEvidence ReadEntry(JsonElement item)
        {
            string accession = item.GetProperty("primaryAccession").GetString() ?? string.Empty;
            if (accession.Length == 0) throw new JsonException("An evidence entry has no accession.");
            var evidence = new ProteinEvidence { Accession = accession, TransmembraneCount = 0, Fragment = false };
            if (item.TryGetProperty("organism", out var organism) && organism.ValueKind == JsonValueKind.Object)
            {
                if (organism.TryGetProperty("scientificName", out var name) && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(name.GetString())) evidence.OrganismName = name.GetString();
                if (organism.TryGetProperty("taxonId", out var taxon) && taxon.ValueKind == JsonValueKind.Number
                    && taxon.TryGetInt32(out int taxonId) && taxonId > 0) evidence.TaxonId = taxonId;
            }
            if (item.TryGetProperty("entryType", out JsonElement entryType))
                evidence.Reviewed = entryType.GetString() switch
                {
                    "UniProtKB reviewed (Swiss-Prot)" => true,
                    "UniProtKB unreviewed (TrEMBL)" => false,
                    _ => null,
                };
            if (item.TryGetProperty("proteinDescription", out JsonElement description)
                && description.TryGetProperty("flag", out JsonElement flag))
                evidence.Fragment = (flag.GetString() ?? string.Empty).IndexOf("fragment", StringComparison.OrdinalIgnoreCase) >= 0;
            if (item.TryGetProperty("keywords", out JsonElement keywords))
            {
                evidence.Keywords = keywords.EnumerateArray().Select(k => k.GetProperty("name").GetString() ?? string.Empty)
                    .Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                var categories = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (JsonElement keyword in keywords.EnumerateArray())
                {
                    string name = keyword.GetProperty("name").GetString() ?? string.Empty;
                    if (name.Length == 0 || !keyword.TryGetProperty("category", out var category)
                        || category.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(category.GetString())) continue;
                    string value = category.GetString()!;
                    if (categories.TryGetValue(name, out string? previous) && previous != value)
                        throw new JsonException("UniProt returned conflicting keyword categories.");
                    categories[name] = value;
                }
                if (categories.Count > 0) evidence.KeywordCategories = categories.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }
            if (item.TryGetProperty("features", out JsonElement features))
                evidence.TransmembraneCount = features.EnumerateArray()
                    .Count(f => f.GetProperty("type").GetString() == "Transmembrane");
            if (item.TryGetProperty("comments", out JsonElement comments))
            {
                var familyTexts = new List<string>();
                foreach (JsonElement comment in comments.EnumerateArray())
                {
                    string? type = comment.GetProperty("commentType").GetString();
                    if (type != "SIMILARITY" && type != "TISSUE SPECIFICITY") continue;
                    if (!comment.TryGetProperty("texts", out JsonElement texts)) continue;
                    var values = texts.EnumerateArray().Select(t => t.GetProperty("value").GetString() ?? string.Empty)
                        .Where(t => t.Length > 0);
                    if (type == "SIMILARITY") familyTexts.AddRange(values);
                    else evidence.TissueSpecificity.AddRange(values);
                }
                ApplyFamilyAnnotations(evidence, familyTexts);
            }
            if (item.TryGetProperty("uniProtKBCrossReferences", out JsonElement references))
                foreach (JsonElement reference in references.EnumerateArray())
                {
                    string? database = reference.GetProperty("database").GetString();
                    if (database != "Pfam" && database != "PANTHER") continue;
                    var domain = new EvidenceDomain { Accession = reference.GetProperty("id").GetString() ?? string.Empty };
                    if (reference.TryGetProperty("properties", out JsonElement properties))
                        domain.Name = properties.EnumerateArray().Where(p => p.GetProperty("key").GetString() == "EntryName")
                            .Select(p => p.GetProperty("value").GetString()).FirstOrDefault() ?? string.Empty;
                    (database == "Pfam" ? evidence.Pfam : evidence.Panther).Add(domain);
                }
            evidence.Positions = Positions.ProteinPositionParser.ReadUniProt(item);
            return evidence;
        }

        private static void ApplyFamilyAnnotations(ProteinEvidence evidence, List<string> annotations)
        {
            evidence.SimilarityAnnotations = annotations.Count == 0 ? null : annotations;
            var memberships = new List<EvidenceFamilyMembership>();
            foreach (string annotation in annotations)
            {
                MatchCollection starts = MembershipStart.Matches(annotation);
                for (int i = 0; i < starts.Count; i++)
                {
                    int begin = starts[i].Index + starts[i].Length;
                    int end = i + 1 < starts.Count ? starts[i + 1].Index : annotation.Length;
                    List<string> hierarchy = HierarchyBreak.Split(annotation.Substring(begin, end - begin))
                        .Select(s => s.Trim().TrimEnd('.', ';', ',').Trim()).Where(IsRanked).ToList();
                    if (hierarchy.Count == 0) continue;
                    memberships.Add(new EvidenceFamilyMembership
                    {
                        Family = hierarchy.LastOrDefault(h => h.EndsWith(" family", StringComparison.OrdinalIgnoreCase)) ?? hierarchy[0],
                        Subfamily = hierarchy.FirstOrDefault(h => h.EndsWith(" subfamily", StringComparison.OrdinalIgnoreCase)) ?? string.Empty,
                        Hierarchy = string.Join(", ", hierarchy),
                        Context = annotation.Substring(0, starts[i].Index).Trim().TrimEnd(';').Trim(),
                    });
                }
            }
            evidence.FamilyMemberships = memberships.Count == 0 ? null : memberships;
            evidence.FamilyAnnotation = string.Join("; ", memberships.Select(m => m.Hierarchy));
            List<string> families = memberships.Select(m => m.Family).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (families.Count == 1)
            {
                evidence.Family = families[0];
                List<string> subfamilies = memberships.Select(m => m.Subfamily).Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (subfamilies.Count == 1) evidence.Subfamily = subfamilies[0];
            }

            static bool IsRanked(string value)
            {
                int space = value.LastIndexOf(' ');
                return space >= 0 && value.Substring(space + 1).EndsWith("family", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static async Task<EvidenceFetchResult> FetchAsync(FamilyClusterFile file,
            HttpMessageHandler? handler = null, IProgress<FetchProgress>? progress = null,
            CancellationToken cancellationToken = default, IReadOnlyCollection<string>? positionAccessions = null,
            UniProtFetchOptions? options = null)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            var positionScope = (positionAccessions ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            var originals = file.MyExtendedFastaItems.Select(i =>
                (Item: i, Identifier: i.SequenceIdentifier, Sequence: i.Sequence, Domains: i.Domains, Evidence: i.Evidence)).ToArray();
            List<List<string>> batches = ProteinDomains.Batches(originals.Select(i => i.Item));
            if (batches.Count == 0) return new EvidenceFetchResult
            {
                Provenance = file.EvidenceSource ?? new EvidenceProvenance { Provider = "UniProt" },
            };
            var found = new Dictionary<string, ProteinEvidence>(StringComparer.Ordinal);
            var failures = new Dictionary<string, string>(StringComparer.Ordinal);
            var result = new EvidenceFetchResult { Asked = batches.Sum(b => b.Count) };
            string? release = null;
            string originalDomainSource = file.DomainSource;
            EvidenceProvenance? originalEvidenceSource = file.EvidenceSource;
            using HttpClient client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = TimeSpan.FromSeconds(100);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");
            var cache = await UniProtBatchCache.OpenAsync(string.Join("\n", batches.Select(b => Endpoint + string.Join(",", b))),
                client, handler, options, message => Report(0, message), cancellationToken).ConfigureAwait(false);
            release = cache?.Release;
            Report(0);
            for (int index = 0; index < batches.Count; index++)
            {
                await FetchBatch(batches[index], index).ConfigureAwait(false);
                Report(index + 1);
            }
            if (found.Count == 0 && failures.Count > 0)
                throw new HttpRequestException("UniProt could not return any requested accession. " + string.Join("; ", failures.Select(pair => pair.Key + ": " + pair.Value)),
                    null, System.Net.HttpStatusCode.InternalServerError);

            async Task FetchBatch(List<string> batch, int index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string url = Endpoint + string.Join(",", batch);
                if (batch.Count > 1 && cache?.WasSplit(url) == true)
                {
                    Report(index, "Resuming saved protein evidence groups");
                    await FetchSplit(batch, index).ConfigureAwait(false);
                    return;
                }
                try { await FetchCompleteBatch(batch, index).ConfigureAwait(false); }
                catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (batch.Count == 1)
                    {
                        failures[batch[0]] = error.Message;
                        Report(index, "UniProt could not return accession " + batch[0] + " (HTTP 500)");
                        return;
                    }
                    if (cache != null) await cache.MarkSplitAsync(url, cancellationToken).ConfigureAwait(false);
                    Report(index, "UniProt refused a batch; trying smaller accession groups");
                    await FetchSplit(batch, index).ConfigureAwait(false);
                }
            }

            async Task FetchSplit(List<string> batch, int index)
            {
                int half = batch.Count / 2;
                await FetchBatch(batch.GetRange(0, half), index).ConfigureAwait(false);
                await FetchBatch(batch.GetRange(half, batch.Count - half), index).ConfigureAwait(false);
            }

            async Task FetchCompleteBatch(List<string> batch, int index)
            {
                string url = Endpoint + string.Join(",", batch);
                var saved = cache == null ? null : await cache.ReadAsync(url, cancellationToken).ConfigureAwait(false);
                string body, currentRelease;
                if (saved == null)
                {
                    using HttpResponseMessage response = await GetWithRetry(client, url, cancellationToken,
                        options, message => Report(index, message)).ConfigureAwait(false);
                    body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    currentRelease = UniProtHttp.Release(response);
                }
                else
                {
                    body = saved.Json; currentRelease = saved.Release;
                    Report(index, "Resuming saved protein evidence batches");
                }
                Dictionary<string, ProteinEvidence> parsed = Parse(body);
                int matched = 0;
                foreach (string accession in batch)
                    if (parsed.TryGetValue(accession, out ProteinEvidence? evidence))
                    {
                        if (evidence.Reviewed == null)
                            throw new InvalidDataException("UniProt omitted the requested review status. No evidence was applied.");
                        found.Add(accession, evidence);
                        matched++;
                    }
                if (matched == 0) throw new InvalidDataException("UniProt answered about none of the accessions in a requested batch.");
                if (currentRelease.Length == 0)
                    throw new InvalidDataException("UniProt omitted its release. No evidence was applied.");
                if (release != null && release != currentRelease)
                    throw new InvalidDataException("The UniProt release changed between batches. No evidence was applied.");
                release = currentRelease;
                if (cache != null && saved == null) await cache.StoreAsync(url, body, null, cancellationToken).ConfigureAwait(false);
                var featureSource = Positions.ProteinPositionFetcher.Source("UniProt", currentRelease,
                    Endpoint + string.Join(",", batch), body);
                featureSource.RetrievedAtUtc = cache?.RetrievedAtUtc ?? options?.UtcNow() ?? featureSource.RetrievedAtUtc;
                foreach (var evidence in parsed.Values.Distinct())
                    if (evidence.Positions != null) evidence.Positions.FeatureSource = featureSource;
            }
            // Fetch onto detached evidence objects. A failed page or cancellation cannot publish a
            // half-enriched protein, nor any of the otherwise successful UniProt batches.
            var requestedEvidence = positionScope.Where(found.ContainsKey).Select(a => found[a]).ToHashSet();
            var previousPositions = originals.Select(p => p.Evidence?.Positions).Where(p => p != null)
                .GroupBy(p => (p!.Accession, p.SequenceSha256)).ToDictionary(g => g.Key, g => g.First()!);
            var positionWatch = Stopwatch.StartNew();
            result.PositionAsked = positionScope.Count;
            foreach (var evidence in found.Values.Distinct())
            {
                if (!requestedEvidence.Contains(evidence) && evidence.Positions != null)
                {
                    if (previousPositions.TryGetValue((evidence.Accession, evidence.Positions.SequenceSha256), out var previous))
                    {
                        evidence.Positions.Pfam = EvidenceApply.Clone(previous.Pfam)!;
                        evidence.Positions.DomainSource = EvidenceApply.Clone(previous.DomainSource);
                    }
                }
            }
            await Parallel.ForEachAsync(requestedEvidence.Where(e => e.Positions != null), new ParallelOptions
            {
                MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken,
            }, async (evidence, token) => await Positions.ProteinPositionFetcher.FetchDomainsAsync(evidence.Positions!, client, token,
                options, message => Report(batches.Count, message)).ConfigureAwait(false)).ConfigureAwait(false);
            result.PositionFound = requestedEvidence.Count(e => e.Positions?.DomainSource != null);
            if (requestedEvidence.Select(e => e.Positions?.DomainSource?.Release).Where(r => r != null).Distinct(StringComparer.Ordinal).Count() > 1)
                throw new InvalidDataException("The InterPro release changed between proteins. No evidence was applied.");
            result.PositionElapsedSeconds = positionWatch.Elapsed.TotalSeconds;
            result.Found = found.Count;
            result.WithDomains = found.Values.Count(e => e.Pfam.Count > 0);
            result.Provenance = new EvidenceProvenance
            {
                Provider = "UniProt", Release = release ?? string.Empty, RetrievedAtUtc = cache?.RetrievedAtUtc ?? options?.UtcNow() ?? DateTimeOffset.UtcNow,
                UnresolvedAccessions = result.Missing == 0 ? null : batches.SelectMany(b => b).Where(a => !found.ContainsKey(a)).ToList(),
                UnresolvedReasons = result.Missing == 0 ? null : batches.SelectMany(b => b).Where(a => !found.ContainsKey(a))
                    .ToDictionary(accession => accession, accession => failures.TryGetValue(accession, out string? reason)
                        ? reason : "UniProt returned no matching accession.", StringComparer.Ordinal),
            };
            var source = new EvidenceRecordSource
            {
                Provider = result.Provenance.Provider, Release = result.Provenance.Release,
                RetrievedAtUtc = result.Provenance.RetrievedAtUtc.Value,
            };
            foreach (ProteinEvidence evidence in found.Values) evidence.Source = source;
            var staged = originals.Select(i => (i.Item, Accession: FamilyNaming.Accession(i.Identifier).Split('-')[0]))
                .Where(i => found.ContainsKey(i.Accession))
                .Select(i => (i.Item, Evidence: found[i.Accession], Domains: DomainNames(found[i.Accession]))).ToArray();

            cancellationToken.ThrowIfCancellationRequested();
            if (file.MyExtendedFastaItems.Count != originals.Length || file.DomainSource != originalDomainSource
                || !ReferenceEquals(file.EvidenceSource, originalEvidenceSource))
                throw new InvalidDataException("The file changed while its evidence was being fetched. No evidence was applied.");
            for (int i = 0; i < originals.Length; i++)
                if (!ReferenceEquals(file.MyExtendedFastaItems[i], originals[i].Item)
                    || originals[i].Item.SequenceIdentifier != originals[i].Identifier
                    || originals[i].Item.Sequence != originals[i].Sequence
                    || originals[i].Item.Domains != originals[i].Domains
                    || !ReferenceEquals(originals[i].Item.Evidence, originals[i].Evidence))
                    throw new InvalidDataException("A protein changed while evidence was being fetched. No evidence was applied.");
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var value in staged)
            {
                value.Item.Evidence = value.Evidence;
                value.Item.Domains = value.Domains;
            }
            file.EvidenceSource = result.Provenance;
            file.DomainSource = result.Source;
            return result;

            void Report(int completed, string message = "") => progress?.Report(new FetchProgress
            {
                CompletedBatches = completed, TotalBatches = batches.Count, Asked = result.Asked, Found = found.Count, Message = message,
            });
        }

        private static Task<HttpResponseMessage> GetWithRetry(HttpClient client, string url, CancellationToken cancellationToken,
            UniProtFetchOptions? options = null, Action<string>? retryProgress = null) =>
            UniProtHttp.GetWithRetryAsync(client, url, Attempts, status => "UniProt refused the evidence batch (HTTP " + status.ToString(System.Globalization.CultureInfo.InvariantCulture) + ").", cancellationToken, options, retryProgress);
    }
}
