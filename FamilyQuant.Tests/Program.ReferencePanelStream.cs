using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReferencePanelStreamCommands() =>
    [
        new("--test-reference-panel-stream", 100, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-reference-panel-stream", (_, _) =>
            {
                ReferencePanelStreamTests();
                string output = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "reference-panel-stream");
                Directory.CreateDirectory(output);
                ReferencePanelTests(output);
                EvidenceOperationTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private sealed class StreamPanelHandler : HttpMessageHandler
    {
        internal List<Uri> Requests { get; } = new();
        internal Func<Uri, (string Body, string Release, int? Count)> Reply = _ =>
            (PanelPage(PanelEntryJson("P00002", "Belongs to the beta family.", "PEPTIDEK"),
                PanelEntryJson("P00001", "Belongs to the alpha family.", "OTHERPEPTIDE"),
                PanelEntryJson("P00003", "Belongs to the alphabet family.", "UNRELATED")), "2026_03", 3);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Uri uri = request.RequestUri!;
            Requests.Add(uri);
            var (body, release, count) = Reply(uri);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            if (release.Length > 0) response.Headers.Add("x-uniprot-release", release);
            // Real stream replies do not announce a count. The search count request does.
            if (uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal) && count is int total)
                response.Headers.Add("x-total-results", total.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Task.FromResult(response);
        }
    }

    private static void ReferencePanelStreamTests()
    {
        using var handler = new StreamPanelHandler();
        ReferencePanel panel = ReferencePanelFetcher.FetchAsync(new[] { "beta family", "Alpha family", "alpha family", " " }, handler)
            .GetAwaiter().GetResult();
        Check("J17 default uses one OR-combined stream and one zero-row completeness request",
            handler.Requests.Count == 2 && handler.Requests.Count(u => u.AbsolutePath.EndsWith("/stream", StringComparison.Ordinal)) == 1
            && handler.Requests.Any(u => u.Query.Contains("size=0", StringComparison.Ordinal))
            && handler.Requests.All(u => Uri.UnescapeDataString(u.Query).Contains(" OR ", StringComparison.Ordinal)));
        Check("J17 stream keeps only exact requested memberships, sorted and deduplicated",
            panel.Families.SequenceEqual(new[] { "Alpha family", "beta family" })
            && panel.Entries.Select(e => e.Accession).SequenceEqual(new[] { "P00001", "P00002" })
            && panel.Entries.All(e => e.Evidence.SimilarityAnnotations == null));
        using var paged = new StreamPanelHandler();
        var old = ReferencePanelFetcher.FetchAsync(panel.Families, paged, usePagedComparison: true).GetAwaiter().GetResult();
        Check("J17 streamed and old paged routes retain identical entries and panel provenance facts",
            JsonSerializer.Serialize(panel.Entries) == JsonSerializer.Serialize(old.Entries)
            && JsonSerializer.Serialize(panel.Families) == JsonSerializer.Serialize(old.Families)
            && panel.Provider == old.Provider && panel.Release == old.Release
            && panel.RetrievedAtUtc != null && old.RetrievedAtUtc != null);

        using var empty = new StreamPanelHandler();
        var none = ReferencePanelFetcher.FetchAsync(Array.Empty<string>(), empty).GetAwaiter().GetResult();
        Check("J17 empty family selection does not request the whole reviewed database", empty.Requests.Count == 0 && none.Entries.Count == 0);
        using var longQuery = new StreamPanelHandler { Reply = _ => (PanelPage(), "2026_03", 0) };
        var longFamilies = Enumerable.Range(0, 150).Select(i => "User-é-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)
            + new string('x', 100) + " family").ToArray();
        var progress = new List<FetchProgress>();
        ReferencePanelFetcher.FetchAsync(longFamilies, longQuery, new InlineFetchProgress(progress.Add)).GetAwaiter().GetResult();
        Check("J17 batches by full escaped URL length and combines as many clauses as fit",
            longQuery.Requests.All(u => u.AbsoluteUri.Length <= 7000)
            && longQuery.Requests.Count < longFamilies.Length / 4 && longQuery.Requests.Count > 2);
        Check("J17 family progress and cancellation checkpoints remain ordered through batching",
            progress.Select(p => p.CompletedBatches).SequenceEqual(Enumerable.Range(0, longFamilies.Length + 1))
            && progress.All(p => p.TotalBatches == longFamilies.Length));
        using var shortQuery = new StreamPanelHandler { Reply = _ => (PanelPage(), "2026_03", 0) };
        ReferencePanelFetcher.FetchAsync(Enumerable.Range(0, 201).Select(i => "F" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)
            + " family").ToArray(), shortQuery).GetAwaiter().GetResult();
        Check("J17 respects UniProt's 100-condition limit even when more fit in the URL",
            shortQuery.Requests.Count == 6 && shortQuery.Requests.All(u =>
                System.Text.RegularExpressions.Regex.Matches(Uri.UnescapeDataString(u.Query), @"\(family:").Count <= 100));

        foreach (string failure in new[] { "missing count", "wrong count", "count release", "stream release", "changed release", "malformed stream", "unreviewed entry" })
        {
            using var bad = new StreamPanelHandler { Reply = uri =>
            {
                bool stream = uri.AbsolutePath.EndsWith("/stream", StringComparison.Ordinal);
                return (failure == "malformed stream" && stream ? "{\"results\":[" :
                    PanelPage(PanelEntryJson("P00001", "Belongs to the alpha family.", "PEPTIDEK", failure != "unreviewed entry")),
                    failure == "count release" && !stream || failure == "stream release" && stream ? "" :
                    failure == "changed release" && stream ? "2026_04" : "2026_03",
                    failure == "missing count" ? null : failure == "wrong count" ? 2 : 1);
            }};
            var file = AtomicFetchFile(); string before = file.ToJsonText();
            bool refused = false;
            try { ReferencePanelFetcher.FetchAndStoreAsync(file, handler: bad).GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { refused = true; }
            Check("J17 " + failure + " preserves the entire existing panel and file", refused && before == file.ToJsonText());
        }
        foreach (bool afterBatch in new[] { false, true })
        {
            using var stop = new CancellationTokenSource();
            using var cancelled = new StreamPanelHandler();
            var file = AtomicFetchFile(); string before = file.ToJsonText();
            cancelled.Reply = uri =>
            {
                if (!afterBatch && uri.AbsolutePath.EndsWith("/stream", StringComparison.Ordinal)) stop.Cancel();
                return (PanelPage(), "2026_03", 0);
            };
            bool refused = false;
            try { ReferencePanelFetcher.FetchAndStoreAsync(file, handler: cancelled, cancellationToken: stop.Token,
                progress: new InlineFetchProgress(p => { if (afterBatch && p.CompletedBatches > 0) stop.Cancel(); })).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { refused = true; }
            Check("J17 cancellation " + (afterBatch ? "at completed batch" : "during stream") + " preserves all stored bytes", refused && before == file.ToJsonText());
        }
        PanelComparisonCacheTests();
    }

    private sealed class InlineFetchProgress(Action<FetchProgress> report) : IProgress<FetchProgress>
    {
        public void Report(FetchProgress value) => report(value);
    }
}
