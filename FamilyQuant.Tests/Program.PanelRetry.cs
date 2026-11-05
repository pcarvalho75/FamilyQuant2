using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static FamilyClusterFile RetryPanelFile()
    {
        var file = AtomicFetchFile();
        file.EvidenceSource!.Provider = "UniProt"; file.EvidenceSource.Release = "2026_03";
        file.MyExtendedFastaItems[0].Evidence!.Family = "fixture family";
        file.EvidenceSource.ReferencePanel = new ReferencePanel
        {
            Families = new() { "fixture family" }, MissingFamilies = new() { "fixture family" },
            FailureReason = "UniProt server error, HTTP 500", TotalBatches = 64, FailedBatches = 1,
        };
        return file;
    }

    private static void PanelRetryUiTests()
    {
        var file = RetryPanelFile();
        string evidenceBefore = JsonSerializer.Serialize(file.MyExtendedFastaItems[0].Evidence);
        var screen = new ClusterGenerationControl(file); using var handler = new EvidenceAndPanelHandler();
        screen.FetchDomainsHandlerOverride = handler;
        int changed = 0; screen.DocumentChanged += (_, _) => changed++;
        PumpDispatcher();
        Check("Z1e a reopened incomplete panel shows Retry panel and blocks the family-world step in plain words",
            ((System.Windows.Controls.Button)screen.FindName("ButtonRetryPanel")).Visibility == Visibility.Visible && screen.ViewModel.HasIncompletePanel
            && screen.ViewModel.EvidenceStepStatus.Contains("saved", StringComparison.Ordinal)
            && screen.ViewModel.FamilyWorldStepStatus.Contains("Retry panel", StringComparison.Ordinal));
        var task = screen.RetryPanelAsync(); FinishBusyTask(task); task.GetAwaiter().GetResult(); PumpDispatcher();
        Check("Z1e the real Retry panel action finishes the panel without requesting or changing protein evidence",
            handler.ProteinRequests == 0 && handler.PanelRequests == 2 && changed == 1
            && file.EvidenceSource?.ReferencePanel is { IsComplete: true, Entries.Count: 1 }
            && file.MyExtendedFastaItems[0].Domains == "Stored domain"
            && JsonSerializer.Serialize(file.MyExtendedFastaItems[0].Evidence) == evidenceBefore
            && ((System.Windows.Controls.Button)screen.FindName("ButtonRetryPanel")).Visibility == Visibility.Collapsed);
        {
            var cancelled = RetryPanelFile(); string before = cancelled.ToJsonText();
            using var stop = new CancellationTokenSource(); using var requests = new EvidenceAndPanelHandler();
            requests.DuringRequest = (_, token) => { stop.Cancel(); token.ThrowIfCancellationRequested(); };
            Check("Z1e cancelling Retry panel keeps every stored byte", Throws<OperationCanceledException>(() =>
                Task.Run(() => EvidenceFetchSession.RetryPanelAsync(cancelled, requests, cancellationToken: stop.Token)).GetAwaiter().GetResult())
                && cancelled.ToJsonText() == before && requests.ProteinRequests == 0);
        }
        {
            var stale = RetryPanelFile(); string before = stale.ToJsonText();
            using var requests = new EvidenceAndPanelHandler { PanelRelease = "2026_04" };
            Check("Z1e Retry panel refuses a release different from the saved protein evidence", Throws<IOException>(() =>
                Task.Run(() => EvidenceFetchSession.RetryPanelAsync(stale, requests)).GetAwaiter().GetResult()) && stale.ToJsonText() == before);
        }
        {
            var incomplete = RetryPanelFile(); using var refused = new EvidenceAndPanelHandler(failPanel: true);
            var outcome = Task.Run(() => EvidenceFetchSession.FetchAsync(incomplete, refused)).GetAwaiter().GetResult();
            string message = CurationViewModel.DescribeEvidenceFetch(outcome);
            Check("Z1e the result names the saved protein evidence, incomplete panel, HTTP reason and retry action",
                message.Contains("Protein evidence saved for 1 protein.", StringComparison.Ordinal)
                && message.Contains("1 family missing", StringComparison.Ordinal) && message.Contains("1 of 1 batch", StringComparison.Ordinal)
                && message.Contains("panel incomplete", StringComparison.Ordinal) && message.Contains("HTTP 400", StringComparison.Ordinal)
                && message.Contains("Retry panel", StringComparison.Ordinal) && !message.Contains('\n'));
        }
        {
            var local = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "local_entry", Sequence = "PEPTIDEK" } } };
            using var requests = new EvidenceAndPanelHandler();
            var outcome = Task.Run(() => EvidenceFetchSession.FetchAsync(local, requests)).GetAwaiter().GetResult();
            Check("Z1e a normal zero-accession fetch explains its unsupported database origin",
                CurationViewModel.DescribeEvidenceFetch(outcome).StartsWith("No protein in this file has a UniProt code.", StringComparison.Ordinal)
                && CurationViewModel.DescribeEvidenceFetch(outcome).Contains("stays unassigned", StringComparison.Ordinal)
                && !CurationViewModel.DescribeEvidenceFetch(outcome).Contains("Stored protein evidence kept", StringComparison.Ordinal));
            Check("Z1e a panel-only retry explicitly names retained protein evidence",
                CurationViewModel.DescribeEvidenceFetch(outcome, panelOnly: true).StartsWith("Stored protein evidence kept.", StringComparison.Ordinal));
        }
        {
            var edited = RetryPanelFile(); var expected = FamilyClusterFile.FromJsonText(edited.ToJsonText());
            expected.MyExtendedFastaItems[0].UserAnnotation = "User edit";
            using var requests = new EvidenceAndPanelHandler();
            requests.DuringRequest = (panel, _) => { if (panel) edited.MyExtendedFastaItems[0].UserAnnotation = "User edit"; };
            Check("Z1e a file change during Retry panel rejects fetched data and preserves the user's edit",
                Throws<InvalidDataException>(() => Task.Run(() => EvidenceFetchSession.RetryPanelAsync(edited, requests)).GetAwaiter().GetResult())
                && edited.ToJsonText() == expected.ToJsonText());
        }
        {
            var malformed = RetryPanelFile(); using var requests = new EvidenceAndPanelHandler { MalformedPanel = true };
            var outcome = Task.Run(() => EvidenceFetchSession.FetchAsync(malformed, requests)).GetAwaiter().GetResult();
            Check("Z1e malformed-panel fallback retains evidence and failed/total batch counts",
                malformed.MyExtendedFastaItems[0].Domains == "Supplied domain" && outcome.Panel is { IsComplete: false, TotalBatches: 1, FailedBatches: 1 }
                && CurationViewModel.DescribeEvidenceFetch(outcome).Contains("1 of 1 batch", StringComparison.Ordinal));
        }
    }

    private sealed class RetryOnceHandler(HttpMessageHandler inner, Func<Uri, bool> matches) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker invoker = new(inner, disposeHandler: false);
        private bool refused;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (!refused && matches(request.RequestUri!))
            {
                refused = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            return invoker.SendAsync(request, token);
        }
        protected override void Dispose(bool disposing) { if (disposing) invoker.Dispose(); base.Dispose(disposing); }
    }

    private static void InheritedRetryProgressTests()
    {
        foreach (string route in new[] { "taxonomy", "keywords", "metadata", "positions", "world" })
        {
            int waits = 0; var messages = new List<string>();
            var options = new UniProtFetchOptions { DelayAsync = (_, _) => { waits++; return Task.CompletedTask; } };
            if (route is "taxonomy" or "keywords")
            {
                using var inner = new TaskTaxonomyHandler();
                using var handler = new RetryOnceHandler(inner, uri => uri.AbsolutePath.Contains("/" + route + "/", StringComparison.Ordinal));
                EvidenceTaskDiscovery.DiscoverAsync(TaskFile(), handler, new InlineFetchProgress(p => messages.Add(p.Message)), options: options).GetAwaiter().GetResult();
            }
            else if (route is "metadata" or "positions")
            {
                using var inner = new PositionHandler();
                using var handler = new RetryOnceHandler(inner, uri => uri.Host == "www.ebi.ac.uk"
                    && uri.AbsolutePath.StartsWith(route == "metadata" ? "/interpro/api/protein/" : "/interpro/api/entry/", StringComparison.Ordinal));
                EvidenceFetcher.FetchAsync(PositionFile(), handler, new InlineFetchProgress(p => messages.Add(p.Message)),
                    positionAccessions: new[] { "P00001" }, options: options).GetAwaiter().GetResult();
            }
            else
            {
                using var inner = new WorldFetchHandler(); using var handler = new RetryOnceHandler(inner, _ => true);
                var file = TaskFile();
                using var taxonomy = new TaskTaxonomyHandler();
                var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, taxonomy).GetAwaiter().GetResult();
                var task = EvidenceTaskDiscovery.Confirm(file, proposal, new[] { "Leading scope" });
                var result = CoreOperation.RunAsync("Family world", operation => FamilyWorldFetcher.FetchAndStoreAsync(file, task,
                    operation, handler, options), progress: new FetchOperationProgress(p => messages.Add(p.Message))).GetAwaiter().GetResult();
                Check("Z1e family-world retry still completes", result.Status == OperationStatus.Succeeded);
            }
            string provider = route is "metadata" or "positions" ? "InterPro" : "UniProt";
            Check("Z1e " + route + " forwards its retry wait to progress without sleeping",
                waits == 1 && messages.Contains(provider + " busy, retrying in 5 s", StringComparer.Ordinal));
        }
    }
}
