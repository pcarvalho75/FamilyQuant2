using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FamilyQuant.Operations;

namespace FamilyQuant.Evidence
{
    /// <summary>One reviewed protein from outside the file. Its evidence is parsed by
    /// <see cref="EvidenceFetcher.Parse"/>, so its family reads exactly as the file's own proteins do.</summary>
    public sealed class PanelEntry
    {
        public string Accession { get; set; } = string.Empty;
        public string Sequence { get; set; } = string.Empty;
        public ProteinEvidence Evidence { get; set; } = new ProteinEvidence();
    }

    /// <summary>The reviewed proteins of the families found in the file, from every taxon, kept with the
    /// file so the neighbour search answers offline.</summary>
    public sealed class ReferencePanel
    {
        public string Provider { get; set; } = string.Empty;
        public string Release { get; set; } = string.Empty;
        public DateTimeOffset? RetrievedAtUtc { get; set; }

        /// <summary>The families asked for, sorted.</summary>
        public List<string> Families { get; set; } = new List<string>();

        /// <summary>The entries, sorted by accession.</summary>
        public List<PanelEntry> Entries { get; set; } = new List<PanelEntry>();

        public List<string> MissingFamilies { get; set; } = new();
        public string FailureReason { get; set; } = string.Empty;
        [JsonIgnore]
        public bool IsComplete => MissingFamilies.Count == 0 && FailureReason.Length == 0;
    }

    public sealed partial class EvidenceProvenance
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ReferencePanel? ReferencePanel { get; set; }
    }

    public static partial class ReferencePanelFetcher
    {
        private const string Endpoint = "https://rest.uniprot.org/uniprotkb/search?format=json&size=500&fields="
            + "accession,reviewed,protein_families,keyword,cc_tissue_specificity,xref_pfam,xref_panther,ft_transmem,fragment,sequence&query=";
        private const int Attempts = 5;

        /// <summary>Every family named by a target's evidence, optionally only those of targets in a scope
        /// the caller chose; case folded, sorted. Read from the data, so the program holds no family list.</summary>
        public static List<string> FamiliesFound(FamilyClusterFile file, Func<ProteinEvidence, bool>? inScope = null)
        {
            var families = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FastaItemExtended item in NearestReviewed.Targets(file))
            {
                ProteinEvidence? evidence = item.Evidence;
                if (evidence == null || (inScope != null && !inScope(evidence))) continue;
                foreach (string family in FamiliesOf(evidence)) families.Add(family);
            }
            return families.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal).ToList();
        }

        /// <summary>Every family a protein belongs to: each membership, or the single family.</summary>
        public static IEnumerable<string> FamiliesOf(ProteinEvidence evidence)
        {
            if (evidence.FamilyMemberships != null && evidence.FamilyMemberships.Count > 0)
                return evidence.FamilyMemberships.Select(m => m.Family).Where(f => f.Length > 0);
            return evidence.Family.Length > 0 ? new[] { evidence.Family } : Array.Empty<string>();
        }

        /// <summary>
        /// The reviewed proteins of each family, every taxon. UniProt's family search is a text search, so
        /// an entry is kept only when one of its memberships is the family asked for. All or nothing: a
        /// refused page, a missing release or a release that changes between pages throws and returns no
        /// panel. Progress counts families done. The paged comparison switch retains the previous
        /// route for the native parity checker; ordinary callers use bounded, OR-combined streams.
        /// </summary>
        public static async Task<ReferencePanel> FetchAsync(IReadOnlyList<string> families,
            HttpMessageHandler? handler = null, IProgress<FetchProgress>? progress = null,
            CancellationToken cancellationToken = default, bool usePagedComparison = false, string? expectedRelease = null,
            UniProtFetchOptions? options = null)
        {
            if (!usePagedComparison)
                return await FetchStreamsAsync(families, handler, progress, cancellationToken, expectedRelease, options).ConfigureAwait(false);
            List<string> asked = families.Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal).ToList();
            var entries = new SortedDictionary<string, PanelEntry>(StringComparer.Ordinal);
            string? release = null;
            int received = 0;
            using HttpClient client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = TimeSpan.FromSeconds(100);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");
            Report(0);
            for (int index = 0; index < asked.Count; index++)
            {
                string family = asked[index];
                string? url = Endpoint + Uri.EscapeDataString("(reviewed:true) AND (family:\"" + family.Replace("\"", "") + "\")");
                int? announced = null;
                int delivered = 0;
                while (url != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using HttpResponseMessage response = await GetWithRetry(client, url, cancellationToken, options,
                        message => Report(index, message)).ConfigureAwait(false);
                    string currentRelease = UniProtHttp.Release(response);
                    if (currentRelease.Length == 0)
                        throw new InvalidDataException("UniProt omitted its release. No panel was made.");
                    if (expectedRelease != null && currentRelease != expectedRelease)
                        throw new UniProtReleaseException();
                    if (release != null && release != currentRelease)
                        throw new UniProtReleaseException();
                    release = currentRelease;
                    if (announced == null && response.Headers.TryGetValues("x-total-results", out IEnumerable<string>? totals)
                        && int.TryParse(totals.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int total))
                        announced = total;

                    string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    Dictionary<string, ProteinEvidence> parsed = EvidenceFetcher.Parse(json);
                    foreach ((string accession, string sequence) in Sequences(json))
                    {
                        received++;
                        delivered++;
                        if (!parsed.TryGetValue(accession, out ProteinEvidence? evidence) || evidence.Accession != accession)
                            throw new InvalidDataException("UniProt returned an entry it did not describe. No panel was made.");
                        if (evidence.Reviewed != true)
                            throw new InvalidDataException("UniProt returned an unreviewed entry to a reviewed search. No panel was made.");
                        if (!FamiliesOf(evidence).Contains(family, StringComparer.OrdinalIgnoreCase)) continue;
                        if (sequence.Length == 0 || entries.ContainsKey(accession)) continue;
                        // The family text is dropped; the tissue stays, because the scope words are matched in it
                        // when a reference decides whether a neighbour is in scope.
                        evidence.SimilarityAnnotations = null;
                        entries.Add(accession, new PanelEntry { Accession = accession, Sequence = sequence, Evidence = evidence });
                    }
                    url = NextPage(response);
                }
                // A page left unread is a silent hole in the panel, so the count UniProt announced must arrive.
                if (announced != null && delivered != announced)
                    throw new InvalidDataException("UniProt announced " + announced.Value.ToString(CultureInfo.InvariantCulture)
                        + " reviewed entries for \"" + family + "\" and delivered "
                        + delivered.ToString(CultureInfo.InvariantCulture) + ". No panel was made.");
                Report(index + 1);
            }

            return new ReferencePanel
            {
                Provider = "UniProt",
                Release = release ?? string.Empty,
                RetrievedAtUtc = DateTimeOffset.UtcNow,
                Families = asked,
                Entries = entries.Values.ToList(),
            };

            void Report(int done, string message = "") => progress?.Report(new FetchProgress
            {
                CompletedBatches = done, TotalBatches = asked.Count, Asked = received, Found = entries.Count, Message = message,
            });
        }

        /// <summary>Fetches the panel for the families found in the file (in scope when given) and keeps it
        /// with the file's evidence. Nothing changes in the file unless the whole panel arrives.</summary>
        public static async Task<ReferencePanel> FetchAndStoreAsync(FamilyClusterFile file,
            Func<ProteinEvidence, bool>? inScope = null, HttpMessageHandler? handler = null,
            IProgress<FetchProgress>? progress = null, CancellationToken cancellationToken = default,
            bool usePagedComparison = false, string? expectedRelease = null, UniProtFetchOptions? options = null)
        {
            ReferencePanel panel = await FetchAsync(FamiliesFound(file, inScope), handler, progress, cancellationToken, usePagedComparison, expectedRelease, options)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            file.EvidenceSource ??= new EvidenceProvenance();
            file.EvidenceSource.ReferencePanel = panel;
            return panel;
        }

        /// <summary>The stage name the panel fetch reports its progress under.</summary>
        public const string ProgressStage = "Reference panel";

        /// <summary>The panel fetch on the operation path: progress after each family, Cancel through the
        /// context, and the panel kept with the file only by the final commit, so a cancel or a failure leaves
        /// the file as it was. Run it through CoreOperation.RunAsync.</summary>
        public static async Task<ReferencePanel> FetchAndStoreAsync(FamilyClusterFile file, OperationContext operation,
            Func<ProteinEvidence, bool>? inScope = null, HttpMessageHandler? handler = null, UniProtFetchOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(operation);
            List<string> families = FamiliesFound(file, inScope);
            ReferencePanel panel = await FetchAsync(families, handler, new OperationFetchProgress(operation),
                operation.CancellationToken, options: options).ConfigureAwait(false);
            operation.Commit(() =>
            {
                file.EvidenceSource ??= new EvidenceProvenance();
                file.EvidenceSource.ReferencePanel = panel;
            });
            return panel;
        }

        /// <summary>Reports at once, on the fetching thread, so a cancel stops the fetch at the next family.</summary>
        private sealed class OperationFetchProgress : IProgress<FetchProgress>
        {
            private readonly OperationContext operation;
            public OperationFetchProgress(OperationContext operation) => this.operation = operation;

            public void Report(FetchProgress value) => operation.Report(ProgressStage,
                value.Message.Length > 0 ? value.Message : "Fetched " + value.CompletedBatches.ToString(CultureInfo.InvariantCulture) + " of "
                + value.TotalBatches.ToString(CultureInfo.InvariantCulture) + " families, "
                + value.Found.ToString("N0", CultureInfo.InvariantCulture) + " reviewed proteins kept",
                value.CompletedBatches, value.TotalBatches);
        }

        /// <summary>The panel's entries as neighbour references, leaving out any accession already among
        /// <paramref name="present"/> (the file's own reviewed proteins answer for themselves).</summary>
        public static List<NeighbourReference> References(ReferencePanel? panel, IEnumerable<string>? present = null)
        {
            if (panel == null || !panel.IsComplete) return new List<NeighbourReference>();
            var skip = new HashSet<string>(present ?? Array.Empty<string>(), StringComparer.Ordinal);
            return panel.Entries
                .Where(e => e.Sequence.Length > 0 && !skip.Contains(e.Accession))
                .Select(e => new NeighbourReference
                {
                    Accession = e.Accession,
                    Family = e.Evidence.Family ?? string.Empty,
                    Sequence = e.Sequence,
                    Evidence = e.Evidence,
                })
                .ToList();
        }

        private static IEnumerable<(string, string)> Sequences(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            var found = new List<(string, string)>();
            foreach (JsonElement item in document.RootElement.GetProperty("results").EnumerateArray())
            {
                string accession = item.GetProperty("primaryAccession").GetString() ?? string.Empty;
                string sequence = item.TryGetProperty("sequence", out JsonElement s) && s.TryGetProperty("value", out JsonElement v)
                    ? v.GetString() ?? string.Empty : string.Empty;
                found.Add((accession, sequence));
            }
            return found;
        }

        /// <summary>The next page from UniProt's <c>Link: &lt;url&gt;; rel="next"</c> header, or null. The
        /// URL is read between its brackets, never split at commas: it carries the comma-separated fields.</summary>
        internal static string? NextPage(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? links)) return null;
            Match next = NextLink.Match(string.Join(",", links));
            return next.Success ? next.Groups[1].Value : null;
        }

        private static readonly Regex NextLink = new Regex("<([^>]*)>\\s*;\\s*rel=\"next\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static Task<HttpResponseMessage> GetWithRetry(HttpClient client, string url, CancellationToken cancellationToken,
            UniProtFetchOptions? options = null, Action<string>? retryProgress = null) =>
            UniProtHttp.GetWithRetryAsync(client, url, Attempts, status => "UniProt refused a panel page (HTTP " + status.ToString(CultureInfo.InvariantCulture) + "). No panel was made.", cancellationToken, options, retryProgress);
    }
}
