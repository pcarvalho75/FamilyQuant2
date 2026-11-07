using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class RetryUniProtHandler : HttpMessageHandler
    {
        internal int Calls, Failures = 3;
        internal HttpStatusCode Status = HttpStatusCode.InternalServerError;
        internal bool Timeout;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            if (Calls <= Failures)
            {
                if (Timeout) throw new TaskCanceledException("Fixture timeout");
                return Task.FromResult(new HttpResponseMessage(Status));
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(PanelPage(PanelEntryJson("P00001", "Belongs to the fixture family.", "PEPTIDEK"))),
            };
            response.Headers.Add("x-uniprot-release", "2026_03");
            response.Headers.Add("x-total-results", "1");
            return Task.FromResult(response);
        }
    }

    private static void UniProtRetryTests()
    {
        foreach (bool panel in new[] { false, true })
        foreach (string failure in new[] { "500", "503", "429", "timeout" })
        {
            using var handler = new RetryUniProtHandler
            {
                Timeout = failure == "timeout",
                Failures = failure == "500" ? 1 : 3,
                Status = failure == "timeout" ? HttpStatusCode.OK : (HttpStatusCode)int.Parse(failure, System.Globalization.CultureInfo.InvariantCulture),
            };
            var waits = new List<double>(); var reports = new List<FetchProgress>();
            var options = new UniProtFetchOptions { DelayAsync = (wait, _) => { waits.Add(wait.TotalSeconds); return Task.CompletedTask; } };
            var progress = new InlineFetchProgress(reports.Add);
            if (panel) ReferencePanelFetcher.FetchAsync(new[] { "fixture family" }, handler, progress, options: options).GetAwaiter().GetResult();
            else
            {
                var file = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "sp|P00001|FIXTURE", Sequence = "PEPTIDEK" } } };
                EvidenceFetcher.FetchAsync(file, handler, progress, options: options).GetAwaiter().GetResult();
            }
            double[] expectedWaits = failure == "500" ? new double[] { 0 } : failure == "timeout" ? new double[] { 1, 1, 1 } : new double[] { 5, 15, 45 };
            Check("Z13a " + (panel ? "panel" : "protein") + " recovers from " + failure + " with the matching reported wait policy",
                handler.Calls == handler.Failures + (panel ? 2 : 1) && waits.SequenceEqual(expectedWaits)
                && reports.Where(p => p.Message.Length > 0).Select(p => p.Message)
                    .SequenceEqual(expectedWaits.Select(wait => "UniProt busy, retrying in " + wait.ToString(System.Globalization.CultureInfo.CurrentCulture) + " s")));
        }
        foreach (bool badRequest in new[] { true, false })
        {
            using var handler = new RetryUniProtHandler { Failures = 10, Status = badRequest ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError };
            var waits = new List<double>();
            var options = new UniProtFetchOptions { DelayAsync = (wait, _) => { waits.Add(wait.TotalSeconds); return Task.CompletedTask; } };
            bool refused = false;
            var incomplete = ReferencePanelFetcher.FetchAsync(new[] { "fixture family" }, handler, options: options).GetAwaiter().GetResult();
            refused = !incomplete.IsComplete && incomplete.FailureReason.Contains("HTTP " + ((int)handler.Status).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Check("Z13a " + (badRequest ? "400 fails immediately" : "repeated 500 stops after two quick attempts"),
                refused && handler.Calls == (badRequest ? 1 : 2)
                && waits.SequenceEqual(badRequest ? Array.Empty<double>() : new double[] { 0 }));
        }
        {
            using var cancel = new CancellationTokenSource();
            using var handler = new RetryUniProtHandler { Status = HttpStatusCode.ServiceUnavailable };
            var reports = new List<OperationProgress>();
            bool waited = false;
            var options = new UniProtFetchOptions { DelayAsync = (_, token) =>
            {
                waited = true; cancel.Cancel(); return Task.FromCanceled(token);
            } };
            var file = AtomicFetchFile(); string before = file.ToJsonText();
            var result = CoreOperation.RunAsync("Retry panel", operation => ReferencePanelFetcher.FetchAndStoreAsync(file,
                operation, handler: handler, options: options), cancel.Token, new FetchOperationProgress(reports.Add)).GetAwaiter().GetResult();
            Check("Z1b cancelling the injected wait stops requests, preserves the file and reports the wait through the operation",
                waited && result.Status == OperationStatus.Cancelled && handler.Calls == 1 && file.ToJsonText() == before
                && reports.Any(p => p.Message == "UniProt busy, retrying in 5 s"));
        }
        CoreReuseContractTests();
    }
}
