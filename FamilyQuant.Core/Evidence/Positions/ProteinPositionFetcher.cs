using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence.Positions;

public static class ProteinPositionFetcher
{
    public static string SequenceHash(string sequence) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sequence.ToUpperInvariant())));

    public static bool MatchesSequence(ProteinPositionEvidence positions, string sequence) => positions.SequenceLength == sequence.Length
        && positions.SequenceSha256.Length > 0 && string.Equals(positions.SequenceSha256, SequenceHash(sequence), StringComparison.OrdinalIgnoreCase);

    internal static PositionEvidenceSource Source(string provider, string release, string url, string body) => new()
    {
        Provider = provider, Release = release, RetrievedAtUtc = DateTimeOffset.UtcNow,
        Responses = new() { new() { Url = url, Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))) } },
    };

    internal static async Task FetchDomainsAsync(ProteinPositionEvidence positions, HttpClient client, CancellationToken token,
        UniProtFetchOptions? options = null, Action<string>? retryProgress = null)
    {
        string prefix = "https://www.ebi.ac.uk/interpro/api/entry/pfam/protein/uniprot/" + Uri.EscapeDataString(positions.Accession) + "/";
        string? next = prefix + "?page_size=200";
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var domains = new List<PfamDomainPosition>();
        string metadataUrl = "https://www.ebi.ac.uk/interpro/api/protein/uniprot/" + Uri.EscapeDataString(positions.Accession) + "/";
        using var metadataReply = await UniProtHttp.GetWithRetryAsync(client, metadataUrl, 3,
            status => "InterPro refused the source sequence (HTTP " + status.ToString(CultureInfo.InvariantCulture) + ").", token, options, retryProgress).ConfigureAwait(false);
        string metadataBody = await metadataReply.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        string metadataRelease = Release(metadataReply);
        PositionEvidenceSource source = Source("InterPro/Pfam", metadataRelease, metadataUrl, metadataBody);
        using (var metadataJson = JsonDocument.Parse(metadataBody))
        {
            var metadata = metadataJson.RootElement.GetProperty("metadata");
            if (!string.Equals(ProteinPositionParser.Text(metadata, "accession"), positions.Accession, StringComparison.OrdinalIgnoreCase)
                || !MatchesSequence(positions, ProteinPositionParser.Text(metadata, "sequence")))
                throw new InvalidDataException("InterPro and UniProt source sequences differ. No evidence was applied.");
        }
        int received = 0;
        int? expected = null;
        while (next != null)
        {
            token.ThrowIfCancellationRequested();
            if (!next.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(next, UriKind.Absolute, out _)
                || !visited.Add(next) || visited.Count > 10000)
                throw new InvalidDataException("InterPro supplied an invalid or repeated next-page URL. No evidence was applied.");
            using var response = await UniProtHttp.GetWithRetryAsync(client, next, 3,
                status => "InterPro refused protein positions (HTTP " + status.ToString(CultureInfo.InvariantCulture) + ").", token, options, retryProgress).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            string release = Release(response);
            var pageSource = Source("InterPro/Pfam", release, next, body);
            if (source.Release != release) throw new InvalidDataException("The InterPro release changed between pages. No evidence was applied.");
            source.Responses.AddRange(pageSource.Responses);
            if (response.StatusCode == HttpStatusCode.NoContent) { next = null; break; }
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            int count = root.GetProperty("count").GetInt32();
            if (count < 0 || expected.HasValue && expected != count) throw new InvalidDataException("InterPro changed its hit count between pages.");
            expected = count;
            foreach (var entry in root.GetProperty("results").EnumerateArray())
            {
                received++;
                var metadata = entry.GetProperty("metadata");
                if (!string.Equals(ProteinPositionParser.Text(metadata, "source_database"), "pfam", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("InterPro returned a non-Pfam entry for a Pfam query.");
                bool matched = false;
                foreach (var protein in entry.GetProperty("proteins").EnumerateArray())
                {
                    if (!string.Equals(ProteinPositionParser.Text(protein, "accession"), positions.Accession, StringComparison.OrdinalIgnoreCase)) continue;
                    matched = true;
                    if (protein.GetProperty("protein_length").GetInt32() != positions.SequenceLength)
                        throw new InvalidDataException("InterPro and UniProt source sequence lengths differ. No evidence was applied.");
                    foreach (var location in protein.GetProperty("entry_protein_locations").EnumerateArray())
                    {
                        var domain = new PfamDomainPosition
                        {
                            Accession = metadata.GetProperty("accession").GetString() ?? string.Empty,
                            Name = ProteinPositionParser.Text(metadata, "name"), Model = ProteinPositionParser.Text(location, "model"),
                        };
                        foreach (var fragment in location.GetProperty("fragments").EnumerateArray())
                        {
                            int start = fragment.GetProperty("start").GetInt32(), end = fragment.GetProperty("end").GetInt32();
                            if (start < 1 || end < start || end > positions.SequenceLength) throw new InvalidDataException("InterPro supplied out-of-range domain coordinates.");
                            domain.Fragments.Add(new() { Start = start, End = end, Status = ProteinPositionParser.Text(fragment, "dc-status") });
                        }
                        if (domain.Fragments.Count == 0 || domain.Accession.Length == 0) throw new InvalidDataException("InterPro supplied an empty domain hit.");
                        domain.Start = domain.Fragments.Min(f => f.Start); domain.End = domain.Fragments.Max(f => f.End);
                        domains.Add(domain);
                    }
                }
                if (!matched) throw new InvalidDataException("InterPro returned positions for a different protein.");
            }
            next = root.TryGetProperty("next", out var link) && link.ValueKind == JsonValueKind.String ? link.GetString() : null;
            if (next != null && Uri.TryCreate(new Uri(prefix), next, out var absolute)) next = absolute.AbsoluteUri;
        }
        if (expected.HasValue && received != expected) throw new InvalidDataException("InterPro omitted domain entries from the paginated reply.");
        positions.Pfam = domains;
        positions.DomainSource = source;
    }

    private static string Release(HttpResponseMessage response)
    {
        string release = response.Headers.TryGetValues("InterPro-Version", out var versions) ? versions.FirstOrDefault() ?? string.Empty : string.Empty;
        if (release.Length == 0) throw new InvalidDataException("InterPro omitted its release. No evidence was applied.");
        return release;
    }
}
