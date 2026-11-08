using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class BadAccessionHandler : HttpMessageHandler
    {
        internal string Release = "fixture";
        internal string? BadAccession = "P00003";
        internal HttpStatusCode FailureStatus = HttpStatusCode.InternalServerError;
        internal bool ChangeLastRelease;
        internal List<string[]> Downloads { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            if (!query.Contains("accessions=", StringComparison.Ordinal)) return Task.FromResult(Reply("{\"results\":[]}", Release));
            string[] accessions = query[(query.IndexOf("accessions=", StringComparison.Ordinal) + 11)..].Split(',');
            Downloads.Add(accessions);
            if (BadAccession != null && accessions.Contains(BadAccession, StringComparer.Ordinal))
                return Task.FromResult(new HttpResponseMessage(FailureStatus));
            return Task.FromResult(Reply(PanelPage(accessions.Select(accession => PanelEntryJson(accession,
                "Belongs to the fixture family.", "ACDEFGHIK")).ToArray()), ChangeLastRelease && accessions.Contains("P00005") ? "other" : Release));
        }
        private static HttpResponseMessage Reply(string body, string release)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            response.Headers.Add("x-uniprot-release", release);
            return response;
        }
    }

    private static FamilyClusterFile SmartFetchFile() => new()
    {
        MyExtendedFastaItems = Enumerable.Range(1, 5).Select(index =>
        {
            var protein = TaskProtein("sp|P" + index.ToString("D5") + "|FIXTURE", 21, true);
            protein.Domains = "Previous domain";
            return protein;
        }).ToList(),
    };

    private static void SmartFetchRetryTests()
    {
        using var scratch = new FetchFixtureScratch();
        var instant = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        UniProtFetchOptions Options(string folder, List<double> waits) => new()
        {
            ResumeDirectory = Path.Combine(scratch.Root, folder), UtcNow = () => instant,
            DelayAsync = (delay, _) => { waits.Add(delay.TotalSeconds); return Task.CompletedTask; },
        };
        var file = SmartFetchFile(); var prior = file.MyExtendedFastaItems[2].Evidence;
        var waits = new List<double>(); var reports = new List<FetchProgress>();
        using var bad = new BadAccessionHandler();
        var result = EvidenceFetcher.FetchAsync(file, bad, new InlineFetchProgress(reports.Add), options: Options("resume", waits)).GetAwaiter().GetResult();
        Check("Z13a repeated 500 isolates the bad accession while completing every other entry", result.Asked == 5 && result.Found == 4
            && result.Provenance.UnresolvedAccessions!.SequenceEqual(new[] { "P00003" })
            && bad.Downloads.Where(group => group.Contains("P00003")).GroupBy(group => string.Join(",", group)).All(group => group.Count() == 2));
        Check("Z13a 500 isolation uses no patient waits and reports the rejected accession", waits.All(wait => wait == 0)
            && reports.Any(progress => progress.Message.Contains("P00003 (HTTP 500)", StringComparison.Ordinal)));
        Check("Z13a failed entries retain previous evidence and a persisted failure reason", ReferenceEquals(file.MyExtendedFastaItems[2].Evidence, prior)
            && file.MyExtendedFastaItems[2].Domains == "Previous domain" && result.Provenance.UnresolvedReasons!["P00003"].Contains("HTTP 500", StringComparison.Ordinal)
            && FamilyClusterFile.FromJsonText(file.ToJsonText()).EvidenceSource!.UnresolvedReasons!["P00003"].Contains("HTTP 500", StringComparison.Ordinal));
        string expected = file.ToJsonText();
        using var retry = new BadAccessionHandler(); var again = SmartFetchFile();
        EvidenceFetcher.FetchAsync(again, retry, options: Options("resume", new())).GetAwaiter().GetResult();
        Check("Z13a retry reuses completed split groups and retries only the failed accession", retry.Downloads.Count == 2
            && retry.Downloads.All(group => group.SequenceEqual(new[] { "P00003" })) && again.ToJsonText() == expected);
        using var changed = new BadAccessionHandler { Release = "next", BadAccession = null };
        var fresh = SmartFetchFile();
        EvidenceFetcher.FetchAsync(fresh, changed, options: Options("resume", new())).GetAwaiter().GetResult();
        Check("Z13a a new release invalidates saved split plans and successful replies", changed.Downloads.Count == 1
            && changed.Downloads[0].Length == 5 && fresh.EvidenceSource!.Release == "next" && fresh.EvidenceSource.UnresolvedAccessions == null);
        using (var cancel = new CancellationTokenSource())
        {
            var cancelled = SmartFetchFile(); string before = cancelled.ToJsonText(); int delays = 0; bool stopped = false;
            using var handler = new BadAccessionHandler();
            var options = new UniProtFetchOptions
            {
                ResumeDirectory = Path.Combine(scratch.Root, "cancel"), UtcNow = () => instant,
                DelayAsync = (_, token) => { if (++delays == 2) { cancel.Cancel(); return Task.FromCanceled(token); } return Task.CompletedTask; },
            };
            try { EvidenceFetcher.FetchAsync(cancelled, handler, cancellationToken: cancel.Token, options: options).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { stopped = true; }
            Check("Z13a cancellation after a successful split group preserves the open file", stopped && cancelled.ToJsonText() == before);
            using var resumed = new BadAccessionHandler();
            EvidenceFetcher.FetchAsync(cancelled, resumed, options: Options("cancel", new())).GetAwaiter().GetResult();
            Check("Z13a cancelled retries keep finished groups on disk", !resumed.Downloads.Any(group => group.Contains("P00001") || group.Contains("P00002"))
                && cancelled.ToJsonText() == expected);
        }
        foreach (bool releaseDrift in new[] { false, true })
        {
            var source = SmartFetchFile(); string before = source.ToJsonText(); bool refused = false;
            using var handler = new BadAccessionHandler { ChangeLastRelease = releaseDrift, FailureStatus = releaseDrift ? HttpStatusCode.InternalServerError : HttpStatusCode.Forbidden };
            try { EvidenceFetcher.FetchAsync(source, handler, options: Options(releaseDrift ? "drift" : "forbidden", new())).GetAwaiter().GetResult(); }
            catch (Exception error) when (error is HttpRequestException or InvalidDataException) { refused = true; }
            Check("Z13a " + (releaseDrift ? "release drift" : "non-retryable refusal") + " cannot publish partial annotations", refused
                && source.ToJsonText() == before && (releaseDrift || handler.Downloads.Count == 1));
        }
        foreach (HttpStatusCode status in new[] { HttpStatusCode.BadGateway, HttpStatusCode.GatewayTimeout })
        {
            using var handler = new RetryUniProtHandler { Status = status };
            var shortWaits = new List<double>();
            EvidenceFetcher.FetchAsync(new FamilyClusterFile { MyExtendedFastaItems = new() { TaskProtein("sp|P00001|FIXTURE", 21, true) } }, handler,
                options: new UniProtFetchOptions { DelayAsync = (delay, _) => { shortWaits.Add(delay.TotalSeconds); return Task.CompletedTask; } }).GetAwaiter().GetResult();
            Check("Z13a " + (int)status + " uses only short cancellable waits", shortWaits.SequenceEqual(new double[] { 1, 1, 1 }));
        }
    }
}
