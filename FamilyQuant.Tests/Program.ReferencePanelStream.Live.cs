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
    private sealed class PanelPacketHandler(string path) : DelegatingHandler(new HttpClientHandler())
    {
        internal int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            HttpResponseMessage response = await base.SendAsync(request, token).ConfigureAwait(false);
            try
            {
                byte[] body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                File.AppendAllText(path, JsonSerializer.Serialize(new
                {
                    Url = request.RequestUri!.AbsoluteUri, RetrievedAtUtc = DateTimeOffset.UtcNow,
                    Status = (int)response.StatusCode, ElapsedSeconds = watch.Elapsed.TotalSeconds,
                    Release = response.Headers.TryGetValues("x-uniprot-release", out var release) ? release.FirstOrDefault() : null,
                    Total = response.Headers.TryGetValues("x-total-results", out var total) ? total.FirstOrDefault() : null,
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
        if (args.Length != 3) throw new ArgumentException("Supply an input FCF and a new output directory outside the input folder.");
        string input = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) || output.StartsWith(Path.GetDirectoryName(input)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use a new output directory outside the input folder.");
        string inputHash = TestFileHash.Upper(input);
        Directory.CreateDirectory(output);
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var file = FamilyClusterFile.Load(input);
            var watch = Stopwatch.StartNew();
            using var evidencePackets = new PanelPacketHandler(Path.Combine(output, "evidence-packets.jsonl"));
            var evidence = EvidenceFetcher.FetchAsync(file, evidencePackets, new PanelLiveProgress("Evidence"), stop.Token).GetAwaiter().GetResult();
            double evidenceSeconds = watch.Elapsed.TotalSeconds;
            var families = ReferencePanelFetcher.FamiliesFound(file);
            string fetchedEvidenceHash = HashEvidence(file);
            ProjectStore.WriteFqproj(Path.Combine(output, "evidence.fqproj"), file, Array.Empty<QuantRow>(), new QuantManifest());
            WriteStage(new { Phase = "evidence complete", evidenceSeconds, Families = families.Count, evidence.Found, evidence.Provenance.Release });

            using var streamPackets = new PanelPacketHandler(Path.Combine(output, "stream-packets.jsonl"));
            watch.Restart();
            ReferencePanel streamed = ReferencePanelFetcher.FetchAsync(families, streamPackets, new PanelLiveProgress("Stream"), stop.Token).GetAwaiter().GetResult();
            double streamSeconds = watch.Elapsed.TotalSeconds;
            Check("J17 live stream release equals the protein evidence release", streamed.Release == evidence.Provenance.Release);
            if (streamed.Release != evidence.Provenance.Release) return 1;
            using (FileStream panel = File.Create(Path.Combine(output, "stream-panel.json"))) JsonSerializer.Serialize(panel, streamed);
            WriteStage(new { Phase = "stream complete; paged comparison running", evidenceSeconds, streamSeconds,
                Families = families.Count, Entries = streamed.Entries.Count, Requests = streamPackets.Requests, streamed.Release });

            using var pagedPackets = new PanelPacketHandler(Path.Combine(output, "paged-packets.jsonl"));
            watch.Restart();
            ReferencePanel paged = ReferencePanelFetcher.FetchAsync(families, pagedPackets, new PanelLiveProgress("Paged"), stop.Token,
                usePagedComparison: true).GetAwaiter().GetResult();
            double pagedSeconds = watch.Elapsed.TotalSeconds;
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
                EvidenceRequests = evidencePackets.Requests, StreamRequests = streamPackets.Requests, PagedRequests = pagedPackets.Requests,
                streamed.Provider, streamed.Release, StreamRetrievedAtUtc = streamed.RetrievedAtUtc, PagedRetrievedAtUtc = paged.RetrievedAtUtc,
                TimestampComparison = "Independent retrieval timestamps are recorded separately. Every other panel field is compared exactly.",
                passed, failed, skipped,
            });
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }
        finally { Console.CancelKeyPress -= cancel; }

        void WriteStage<T>(T value) => File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
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
}
