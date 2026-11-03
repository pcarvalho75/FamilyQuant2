using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReferencePanelStreamLiveCommands() =>
    [
        new("--reference-panel-parity-live", 100, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--reference-panel-parity-live", (args, _) => ReferencePanelParityLive(args)),
    ];

    // This command reads only its supplied input. Each actual response has a URL, release, body SHA
    // and elapsed time in the packet manifest. No running application project or cache is consulted.
    private sealed class PanelPacketHandler(string path, string? cache = null, HttpMessageHandler? source = null)
        : DelegatingHandler(source ?? new HttpClientHandler())
    {
        internal int Requests { get; private set; }
        internal int CacheHits { get; private set; }
        private DateTimeOffset notBefore;
        private sealed record CachedReply(string Url, string Sha256, Dictionary<string, string[]> Headers);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            string key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.RequestUri!.AbsoluteUri)));
            string? metadata = cache == null ? null : Path.Combine(cache, key + ".json");
            string? payload = cache == null ? null : Path.Combine(cache, key + ".bin");
            token.ThrowIfCancellationRequested();
            if (metadata != null && File.Exists(metadata) && File.Exists(payload))
            {
                CachedReply saved = JsonSerializer.Deserialize<CachedReply>(File.ReadAllText(metadata))!;
                byte[] body = await File.ReadAllBytesAsync(payload!, token).ConfigureAwait(false);
                if (saved.Url != request.RequestUri.AbsoluteUri || saved.Sha256 != Convert.ToHexString(SHA256.HashData(body)))
                    throw new InvalidDataException("A cached comparison reply changed. No comparison was made.");
                var replay = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
                replay.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                foreach (var header in saved.Headers) replay.Headers.TryAddWithoutValidation(header.Key, header.Value);
                CacheHits++;
                return replay;
            }
            TimeSpan wait = notBefore - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token).ConfigureAwait(false);
            HttpResponseMessage response = await base.SendAsync(request, token).ConfigureAwait(false);
            try
            {
                byte[] body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                if ((int)response.StatusCode == 429 && cache != null)
                {
                    // This is comparison transport only. The production three-attempt retry policy is
                    // unchanged; wait until the provider permits another request before that retry.
                    var retry = response.Headers.RetryAfter;
                    notBefore = retry?.Date ?? DateTimeOffset.UtcNow + (retry?.Delta ?? TimeSpan.FromSeconds(30));
                }
                if (metadata != null && response.IsSuccessStatusCode)
                {
                    Directory.CreateDirectory(cache!);
                    await File.WriteAllBytesAsync(payload!, body, token).ConfigureAwait(false);
                    var saved = new CachedReply(request.RequestUri.AbsoluteUri, Convert.ToHexString(SHA256.HashData(body)),
                        response.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
                    File.WriteAllText(metadata, JsonSerializer.Serialize(saved));
                }
                File.AppendAllText(path, JsonSerializer.Serialize(new
                {
                    Url = request.RequestUri!.AbsoluteUri, RetrievedAtUtc = DateTimeOffset.UtcNow,
                    Status = (int)response.StatusCode, ElapsedSeconds = watch.Elapsed.TotalSeconds,
                    Release = response.Headers.TryGetValues("x-uniprot-release", out var release) ? release.FirstOrDefault() : null,
                    Total = response.Headers.TryGetValues("x-total-results", out var total) ? total.FirstOrDefault() : null,
                    RetryAfter = response.Headers.RetryAfter?.ToString(),
                    Bytes = body.Length, Sha256 = Convert.ToHexString(SHA256.HashData(body)),
                }) + "\n");
                Requests++;
                return response;
            }
            catch { response.Dispose(); throw; }
        }
    }

    private sealed class PanelLiveProgress(string phase) : IProgress<FetchProgress>
    {
        private int last = -1;
        public void Report(FetchProgress value)
        {
            // Bounded logs while preserving checkpoints for an operator watching a long legacy run.
            if (value.CompletedBatches != value.TotalBatches && value.CompletedBatches < last + 50 && last >= 0) return;
            last = value.CompletedBatches;
            Console.WriteLine(phase + " " + value.CompletedBatches.ToString(CultureInfo.InvariantCulture) + "/"
                + value.TotalBatches.ToString(CultureInfo.InvariantCulture) + "; " + value.Found.ToString(CultureInfo.InvariantCulture) + " entries");
        }
    }

    private static int ReferencePanelParityLive(string[] args)
    {
        bool resume = args.Length == 4 && args[3] == "--resume";
        if (args.Length != 3 && !resume) throw new ArgumentException("Supply an input FCF and a new output directory, or add --resume for its saved comparison.");
        string input = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if ((!resume && Directory.Exists(output)) || (resume && !Directory.Exists(output))
            || output.StartsWith(Path.GetDirectoryName(input)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use a new output directory outside the input folder.");
        string inputHash = TestFileHash.Upper(input);
        Directory.CreateDirectory(output);
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var original = FamilyClusterFile.Load(input);
            var file = resume ? ProjectStore.ReadFqproj(Path.Combine(output, "evidence.fqproj")).fcf : original;
            var watch = Stopwatch.StartNew();
            using var evidencePackets = new PanelPacketHandler(Path.Combine(output, "evidence-packets.jsonl"));
            double evidenceSeconds;
            double priorPagedSeconds = 0;
            int priorPagedRequests = 0;
            JsonElement prior = default;
            if (resume)
            {
                using var checkpoint = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "validation.json")));
                prior = checkpoint.RootElement.Clone();
                evidenceSeconds = Number(prior, "EvidenceSeconds");
                priorPagedSeconds = Number(prior, "PagedActiveSeconds");
                priorPagedRequests = (int)Number(prior, "PagedRequests");
                if (original.SourceSha256 != file.SourceSha256 || original.SourceSequenceCount != file.SourceSequenceCount
                    || !original.MyExtendedFastaItems.Select(p => (p.SequenceIdentifier, p.Sequence, p.Description, p.UserID, p.USearchID))
                        .SequenceEqual(file.MyExtendedFastaItems.Select(p => (p.SequenceIdentifier, p.Sequence, p.Description, p.UserID, p.USearchID))))
                    throw new InvalidDataException("The evidence checkpoint belongs to a different source database.");
                if (Text(prior, "InputSha256") is string recorded && recorded != inputHash)
                    throw new InvalidDataException("The source input changed since the comparison checkpoint.");
            }
            else
            {
                EvidenceFetcher.FetchAsync(file, evidencePackets, new PanelLiveProgress("Evidence"), stop.Token).GetAwaiter().GetResult();
                evidenceSeconds = watch.Elapsed.TotalSeconds;
                ProjectStore.WriteFqproj(Path.Combine(output, "evidence.fqproj"), file, Array.Empty<QuantRow>(), new QuantManifest());
                WriteStage(new { Phase = "evidence complete", InputSha256 = inputHash, evidenceSeconds, file.EvidenceSource!.Release });
            }
            var families = ReferencePanelFetcher.FamiliesFound(file);
            string fetchedEvidenceHash = HashEvidence(file);
            if (Text(prior, "EvidenceSha256") is string savedEvidenceHash && savedEvidenceHash != fetchedEvidenceHash)
                throw new InvalidDataException("The evidence checkpoint changed.");

            using var streamPackets = new PanelPacketHandler(Path.Combine(output, "stream-packets.jsonl"));
            watch.Restart();
            ReferencePanel streamed;
            double streamSeconds;
            int streamRequests;
            if (resume)
            {
                using FileStream panel = File.OpenRead(Path.Combine(output, "stream-panel.json"));
                streamed = JsonSerializer.Deserialize<ReferencePanel>(panel)!;
                streamSeconds = Number(prior, "StreamSeconds");
                streamRequests = (int)Number(prior, "StreamRequests", "Requests");
            }
            else
            {
                streamed = ReferencePanelFetcher.FetchAsync(families, streamPackets, new PanelLiveProgress("Stream"), stop.Token).GetAwaiter().GetResult();
                streamSeconds = watch.Elapsed.TotalSeconds;
                streamRequests = streamPackets.Requests;
                using FileStream panel = File.Create(Path.Combine(output, "stream-panel.json"));
                JsonSerializer.Serialize(panel, streamed);
            }
            Check("J17 live stream release and families equal the protein evidence checkpoint",
                streamed.Release == file.EvidenceSource!.Release && streamed.Families.SequenceEqual(families));
            if (failed > 0) return 1;
            string streamHash = TestFileHash.Upper(Path.Combine(output, "stream-panel.json"));
            if (Text(prior, "StreamPanelSha256") is string savedStreamHash && savedStreamHash != streamHash)
                throw new InvalidDataException("The streamed panel checkpoint changed.");
            WritePagedStage("stream complete; paged comparison running", priorPagedSeconds, priorPagedRequests);

            using var pagedPackets = new PanelPacketHandler(Path.Combine(output, "paged-comparison-packets.jsonl"), Path.Combine(output, "paged-replies"));
            watch.Restart();
            ReferencePanel paged;
            try
            {
                paged = ReferencePanelFetcher.FetchAsync(families, pagedPackets, new PanelLiveProgress("Paged"), stop.Token,
                    usePagedComparison: true).GetAwaiter().GetResult();
            }
            catch
            {
                WritePagedStage("paged comparison interrupted; --resume can continue", priorPagedSeconds + watch.Elapsed.TotalSeconds,
                    priorPagedRequests + pagedPackets.Requests);
                throw;
            }
            double pagedSeconds = priorPagedSeconds + watch.Elapsed.TotalSeconds;
            using (FileStream panel = File.Create(Path.Combine(output, "paged-panel.json"))) JsonSerializer.Serialize(panel, paged);
            Check("J17 live panel provider, release and requested family order equal the old route",
                streamed.Provider == paged.Provider && streamed.Release == paged.Release && streamed.Families.SequenceEqual(paged.Families));
            var oldEntries = paged.Entries.ToDictionary(e => e.Accession, StringComparer.Ordinal);
            var newEntries = streamed.Entries.ToDictionary(e => e.Accession, StringComparer.Ordinal);
            int mismatches = 0;
            using (var csv = new StreamWriter(Path.Combine(output, "entry-parity.csv")))
            {
                csv.WriteLine("accession,paged_sha256,stream_sha256,equal");
                foreach (string accession in oldEntries.Keys.Concat(newEntries.Keys).Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal))
                {
                    string a = oldEntries.TryGetValue(accession, out var old) ? HashEntry(old) : "missing";
                    string b = newEntries.TryGetValue(accession, out var current) ? HashEntry(current) : "missing";
                    bool equal = a == b;
                    if (!equal) mismatches++;
                    csv.WriteLine(accession + "," + a + "," + b + "," + (equal ? "true" : "false"));
                }
            }
            Check("J17 live every sorted panel entry, sequence and complete evidence equals the old route", mismatches == 0);
            Check("J17 live both routes leave the fetched protein evidence and provenance unchanged", fetchedEvidenceHash == HashEvidence(file));
            if (failed == 0)
            {
                file.EvidenceSource!.ReferencePanel = streamed;
                string project = Path.Combine(output, "streamed.fqproj");
                ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
                var reopened = ProjectStore.ReadFqproj(project).fcf;
                Check("J17 live saved panel and protein evidence reopen offline exactly",
                    JsonSerializer.Serialize(reopened.EvidenceSource?.ReferencePanel) == JsonSerializer.Serialize(streamed)
                    && fetchedEvidenceHash == HashEvidence(reopened));
            }
            Check("J17 live original input remains byte-identical", inputHash == TestFileHash.Upper(input));
            WriteStage(new
            {
                Phase = failed == 0 ? "complete" : "parity failed", Input = input, InputSha256 = inputHash, EvidenceSha256 = fetchedEvidenceHash,
                Families = families.Count, StreamEntries = streamed.Entries.Count, PagedEntries = paged.Entries.Count, Mismatches = mismatches,
                EvidenceSeconds = evidenceSeconds, StreamSeconds = streamSeconds, PagedSeconds = pagedSeconds,
                EvidenceRequests = File.ReadLines(Path.Combine(output, "evidence-packets.jsonl")).Count(), StreamRequests = streamRequests,
                PagedRequests = priorPagedRequests + pagedPackets.Requests, pagedPackets.CacheHits,
                StreamPanelSha256 = streamHash,
                streamed.Provider, streamed.Release, StreamRetrievedAtUtc = streamed.RetrievedAtUtc, PagedRetrievedAtUtc = paged.RetrievedAtUtc,
                TimestampComparison = "Independent retrieval timestamps are recorded separately. Every other panel field is compared exactly.",
                passed, failed, skipped,
            });
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;

            void WritePagedStage(string phase, double seconds, int requests) => WriteStage(new
            {
                Phase = phase, InputSha256 = inputHash, EvidenceSha256 = fetchedEvidenceHash, StreamPanelSha256 = streamHash,
                EvidenceSeconds = evidenceSeconds, StreamSeconds = streamSeconds, StreamRequests = streamRequests,
                PagedActiveSeconds = seconds, PagedRequests = requests,
                Families = families.Count, Entries = streamed.Entries.Count, streamed.Release,
            });
        }
        finally { Console.CancelKeyPress -= cancel; }

        void WriteStage<T>(T value) => File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        static double Number(JsonElement item, string name, string? alternative = null) => item.ValueKind == JsonValueKind.Object
            ? item.EnumerateObject().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || alternative != null && p.Name.Equals(alternative, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value.GetDouble()).FirstOrDefault() : 0;
        static string? Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object
            ? item.EnumerateObject().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value.GetString()).FirstOrDefault() : null;
        static string HashEntry(PanelEntry entry) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entry)));
        static string HashEvidence(FamilyClusterFile file)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            // The panel is a separate phase; compare the exact evidence and provenance fetched once,
            // with only that optional panel slot excluded, including the original retrieval timestamps.
            foreach (var item in file.MyExtendedFastaItems)
                hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { item.SequenceIdentifier, item.Domains, item.Evidence }));
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { file.DomainSource, file.EvidenceSource?.Provider,
                file.EvidenceSource?.Release, file.EvidenceSource?.RetrievedAtUtc, file.EvidenceSource?.UnresolvedAccessions }));
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }

    private static void PanelComparisonCacheTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "panel-reply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string cache = Path.Combine(root, "replies");
        const string url = "https://rest.uniprot.org/uniprotkb/search?query=fixture";
        const string next = "https://rest.uniprot.org/uniprotkb/search?fields=accession,sequence&cursor=fixture";
        string body = PanelPage(PanelEntryJson("P00001", "Belongs to the fixture family.", "PEPTIDEK"));
        using var firstSource = Announcing(new CannedPanelUniProt((System.Net.HttpStatusCode.OK, body, "2026_03", next)), 2);
        using (var packets = new PanelPacketHandler(Path.Combine(root, "first.jsonl"), cache, firstSource))
        using (var client = new HttpClient(packets, disposeHandler: false))
        using (var response = client.GetAsync(url).GetAwaiter().GetResult())
            Check("J17 comparison captures exact successful response bytes", response.Content.ReadAsStringAsync().GetAwaiter().GetResult() == body
                && packets.Requests == 1 && packets.CacheHits == 0);
        using var unused = new CannedPanelUniProt();
        using (var packets = new PanelPacketHandler(Path.Combine(root, "replay.jsonl"), cache, unused))
        using (var client = new HttpClient(packets, disposeHandler: false))
        {
            using var replay = client.GetAsync(url).GetAwaiter().GetResult();
            Check("J17 comparison replay preserves body, release, count and next-page header without network",
                replay.Content.ReadAsStringAsync().GetAwaiter().GetResult() == body && replay.Headers.GetValues("x-uniprot-release").Single() == "2026_03"
                && replay.Headers.GetValues("x-total-results").Single() == "2" && replay.Headers.GetValues("Link").Single() == "<" + next + ">; rel=\"next\""
                && packets.Requests == 0 && packets.CacheHits == 1 && unused.Asked.Count == 0);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Check("J17 comparison replay honors cancellation", Throws<OperationCanceledException>(() =>
                client.GetAsync(url, cancelled.Token).GetAwaiter().GetResult()) && packets.Requests == 0);
            File.WriteAllText(Directory.GetFiles(cache, "*.bin").Single(), "changed fixture bytes");
            Check("J17 comparison refuses a modified reply before reusing it", Throws<InvalidDataException>(() =>
                client.GetAsync(url).GetAwaiter().GetResult()) && packets.Requests == 0);
        }
    }
}
