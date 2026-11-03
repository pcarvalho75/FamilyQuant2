using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Panels;

public enum PanelPfamCheckStatus { Match, NameMismatch, NotFound, CouldNotCheck }

public sealed class PanelPfamCheckRow
{
    public string FamilyKey { get; init; } = string.Empty;
    public string Accession { get; init; } = string.Empty;
    public string PanelName { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string ProviderShortName { get; init; } = string.Empty;
    public PanelPfamCheckStatus Status { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
}

public sealed class PanelPfamCheckReport
{
    public DateTimeOffset CheckedAtUtc { get; init; }
    public IReadOnlyList<PanelPfamCheckRow> Rows { get; init; } = Array.Empty<PanelPfamCheckRow>();
    public bool Complete => Rows.All(r => r.Status != PanelPfamCheckStatus.CouldNotCheck);
    public int Mismatches => Rows.Count(r => r.Status == PanelPfamCheckStatus.NameMismatch);
}

/// <summary>Checks each user accession and claimed name against InterPro. Returns every original
/// row, including unavailable checks; it never changes the panel or substitutes an accession.</summary>
public static class PanelPfamChecker
{
    private sealed record ProviderCheck(PanelPfamCheckStatus Status, string Name, string ShortName, string Reason, string Url);
    private static readonly Regex AccessionSyntax = new("^PF[0-9]{5}$", RegexOptions.CultureInvariant);

    public static async Task<PanelPfamCheckReport> CheckAsync(FamilyPanel panel, HttpMessageHandler? handler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(panel);
        cancellationToken.ThrowIfCancellationRequested();
        var input = FamilyPanelValidation.Clone(panel);
        var rows = input.Families.SelectMany(f => f.Pfam.Select(d => (Family: f.Key, Domain: d))).ToArray();
        string Key(string value) => value.Trim().ToUpperInvariant();
        string[] accessions = rows.Select(r => Key(r.Domain.Accession)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        using var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");
        var byAccession = new Dictionary<string, ProviderCheck>(StringComparer.Ordinal);
        for (int index = 0; index < accessions.Length; index++)
        {
            if (index > 0) await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            byAccession.Add(accessions[index], await FetchAsync(client, accessions[index], cancellationToken).ConfigureAwait(false));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PanelPfamCheckReport
        {
            CheckedAtUtc = DateTimeOffset.UtcNow,
            Rows = rows.Select(row =>
            {
                ProviderCheck check = byAccession[Key(row.Domain.Accession)];
                bool match = row.Domain.Name.Trim().Length > 0
                    && (string.Equals(row.Domain.Name.Trim(), check.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Domain.Name.Trim(), check.ShortName.Trim(), StringComparison.OrdinalIgnoreCase));
                var status = check.Status == PanelPfamCheckStatus.Match && !match ? PanelPfamCheckStatus.NameMismatch : check.Status;
                return new PanelPfamCheckRow
                {
                    FamilyKey = row.Family, Accession = row.Domain.Accession, PanelName = row.Domain.Name,
                    ProviderName = check.Name, ProviderShortName = check.ShortName, Status = status, SourceUrl = check.Url,
                    Reason = status == PanelPfamCheckStatus.NameMismatch
                        ? "The panel name matches neither the provider's full name nor its short name. No panel data were changed." : check.Reason,
                };
            }).ToArray(),
        };
    }

    private static async Task<ProviderCheck> FetchAsync(HttpClient client, string accession, CancellationToken cancellationToken)
    {
        string url = "https://www.ebi.ac.uk/interpro/api/entry/pfam/" + Uri.EscapeDataString(accession) + "/";
        ProviderCheck Unavailable(string reason) => new(PanelPfamCheckStatus.CouldNotCheck, "", "", "Could not check: " + reason, url);
        if (!AccessionSyntax.IsMatch(accession)) return new(PanelPfamCheckStatus.NotFound, "", "", "Invalid accession syntax; no request was made.", url);
        try
        {
            using var response = await GetWithRateLimitRetryAsync(client, url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
                return new(PanelPfamCheckStatus.NotFound, "", "", "The provider returned no entry for this accession.", url);
            if (!response.IsSuccessStatusCode) return Unavailable("provider HTTP status " + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                + (response.Headers.RetryAfter == null ? "" : " Retry-After: " + response.Headers.RetryAfter.ToString() + "."));
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!json.RootElement.TryGetProperty("metadata", out var metadata)
                || !metadata.TryGetProperty("accession", out var returned) || returned.GetString() != accession
                || !metadata.TryGetProperty("source_database", out var database) || !string.Equals(database.GetString(), "pfam", StringComparison.OrdinalIgnoreCase)
                || !metadata.TryGetProperty("name", out var names))
                return Unavailable("the reply did not identify the requested entry and database.");
            string name = names.TryGetProperty("name", out var full) ? full.GetString() ?? "" : "";
            string shortName = names.TryGetProperty("short", out var shortValue) ? shortValue.GetString() ?? "" : "";
            if (name.Length == 0 && shortName.Length == 0) return Unavailable("the reply contained no entry name.");
            return new(PanelPfamCheckStatus.Match, name, shortName, "Accession and name checked against provider metadata.", url);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return Unavailable(exception.Message);
        }
    }

    private static async Task<HttpResponseMessage> GetWithRateLimitRetryAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == 3) return response;
            TimeSpan wait = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is DateTimeOffset retryAt ? retryAt - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(5 * attempt));
            if (wait > TimeSpan.FromMinutes(1)) return response;
            response.Dispose();
            await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, cancellationToken).ConfigureAwait(false);
        }
    }

    public static string BuildCsv(PanelPfamCheckReport report)
    {
        var csv = new StringBuilder("Family key,Accession,Panel name,InterPro name,InterPro short name,Status,Reason,Source\r\n");
        foreach (var row in report.Rows)
            csv.Append(string.Join(",", new[] { row.FamilyKey, row.Accession, row.PanelName, row.ProviderName,
                row.ProviderShortName, row.Status.ToString(), row.Reason, row.SourceUrl }.Select(CsvFormatting.Guarded))).Append("\r\n");
        return csv.ToString();
    }
}
