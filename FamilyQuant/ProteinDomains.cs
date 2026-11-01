using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant
{
    /// <summary>
    /// Each protein's Pfam domains, read from UniProt's own annotation. What a protein contains settles what
    /// its name cannot: among Ana's 31,777 Viperidae entries, 202 of the 844 proteins gathered by her words had
    /// no metalloproteinase domain, and 29 true SVMPs carried none of her words. It takes no list of names
    /// and no class-specific rule, so it works the same for every protein class.
    ///
    /// Fetched once and saved in the .fcf with the UniProt release, so a file gives the same answer offline
    /// and next year. A protein UniProt does not know, and one too short for Pfam to call (the short
    /// disintegrins, 41 to 51 residues), simply has no domains.
    /// </summary>
    public static class ProteinDomains
    {
        public const string Separator = "; ";

        private const int BatchSize = 500;
        private const int Attempts = 3;
        private const string Endpoint =
            "https://rest.uniprot.org/uniprotkb/accessions?format=json&fields=xref_pfam&size=500&accessions=";

        /// <summary>What a fetch found, for the message and the file.</summary>
        public sealed class FetchResult
        {
            public int Asked { get; internal set; }
            public int Found { get; internal set; }
            public int WithDomains { get; internal set; }

            /// <summary>"UniProt 2026_03", or "UniProt" when the service names no release.</summary>
            public string Source { get; internal set; } = "UniProt";
        }

        /// <summary>The domain names of a stored value.</summary>
        public static List<string> Names(string? domains) =>
            (domains ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim()).Where(d => d.Length > 0).ToList();

        /// <summary>The distinct UniProt accessions of these proteins, in batches the service accepts. It
        /// refuses a whole request over one malformed accession, so only well-formed ones are asked.</summary>
        public static List<List<string>> Batches(IEnumerable<FastaItemExtended> items)
        {
            List<string> accessions = items.Select(i => FamilyNaming.Accession(i.SequenceIdentifier))
                .Where(FamilyNaming.IsUniProtAccession).Select(a => a.Split('-')[0])
                .Distinct(StringComparer.Ordinal).ToList();

            var batches = new List<List<string>>();
            for (int start = 0; start < accessions.Count; start += BatchSize)
                batches.Add(accessions.Skip(start).Take(BatchSize).ToList());
            return batches;
        }

        /// <summary>Accession to domain names from one UniProt reply. An entry with no Pfam is present with
        /// an empty value, because "looked up, has none" differs from "never asked".</summary>
        public static Dictionary<string, string> Parse(string json)
        {
            var domains = new Dictionary<string, string>(StringComparer.Ordinal);
            using JsonDocument document = JsonDocument.Parse(json);

            try
            {
                ReadEntries(document.RootElement, domains);
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex is InvalidOperationException)
            {
                throw new JsonException("The reply was not the list of proteins UniProt sends.", ex);
            }
            return domains;
        }

        private static void ReadEntries(JsonElement root, Dictionary<string, string> domains)
        {
            foreach (JsonElement entry in root.GetProperty("results").EnumerateArray())
            {
                string accession = entry.GetProperty("primaryAccession").GetString() ?? string.Empty;
                var names = new List<string>();
                if (entry.TryGetProperty("uniProtKBCrossReferences", out JsonElement references))
                {
                    foreach (JsonElement reference in references.EnumerateArray())
                    {
                        if (reference.GetProperty("database").GetString() != "Pfam") continue;
                        string? name = reference.GetProperty("properties").EnumerateArray()
                            .Where(p => p.GetProperty("key").GetString() == "EntryName")
                            .Select(p => p.GetProperty("value").GetString()).FirstOrDefault();
                        names.Add(string.IsNullOrEmpty(name) ? reference.GetProperty("id").GetString() ?? string.Empty : name);
                    }
                }
                domains[accession] = string.Join(Separator, names);
            }
        }

        /// <summary>
        /// Fills <see cref="FastaItemExtended.Domains"/> from UniProt. All or nothing: the proteins are
        /// written only after every batch has answered, so a dropped connection cannot leave a file with
        /// domains for half of it, reading as if the rest had none. A reply naming none of the proteins asked
        /// is a failure, not news that none has domains. One that misses a few leaves those as they were,
        /// and the caller reports how many: a withdrawn entry is unknown, which is not the same as empty.
        /// </summary>
        /// <param name="handler">The suite's seam: no test reaches the network.</param>
        public static async Task<FetchResult> FetchAsync(IReadOnlyCollection<FastaItemExtended> items,
                                                         HttpMessageHandler? handler = null)
        {
            using HttpClient client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = TimeSpan.FromSeconds(100);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");

            var result = new FetchResult();
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (List<string> batch in Batches(items))
            {
                result.Asked += batch.Count;
                using HttpResponseMessage response = await GetWithRetry(client, Endpoint + string.Join(",", batch));
                foreach (KeyValuePair<string, string> entry in Parse(await response.Content.ReadAsStringAsync()))
                    found[entry.Key] = entry.Value;
                if (response.Headers.TryGetValues("x-uniprot-release", out IEnumerable<string>? release))
                    result.Source = "UniProt " + release.First();
            }

            if (result.Asked > 0 && found.Count == 0)
                throw new InvalidDataException("UniProt answered about none of the proteins it was asked about.");

            foreach (FastaItemExtended item in items)
            {
                string accession = FamilyNaming.Accession(item.SequenceIdentifier).Split('-')[0];
                if (found.TryGetValue(accession, out string? domains)) item.Domains = domains;
            }
            result.Found = found.Count;
            result.WithDomains = found.Values.Count(v => v.Length > 0);
            return result;
        }

        private static async Task<HttpResponseMessage> GetWithRetry(HttpClient client, string url)
        {
            for (int attempt = 1; ; attempt++)
            {
                HttpResponseMessage? response = null;
                try { response = await client.GetAsync(url); }
                catch (HttpRequestException) when (attempt < Attempts) { }
                catch (TaskCanceledException) when (attempt < Attempts) { }

                if (response != null)
                {
                    if (attempt == Attempts || !WorthRetrying((int)response.StatusCode)) return response.EnsureSuccessStatusCode();
                    response.Dispose();
                }
                await Task.Delay(TimeSpan.FromSeconds(attempt));
            }
        }

        /// <summary>A busy or restarting service, worth asking again; anything else will not change.</summary>
        private static bool WorthRetrying(int status) => status == 429 || status >= 500;
    }
}
