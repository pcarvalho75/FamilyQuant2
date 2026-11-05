using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class FetchFixtureScratch : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "fqr-" + Guid.NewGuid().ToString("N")[..10]);
        public FetchFixtureScratch() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            string target = Path.GetFullPath(Root);
            if (!string.Equals(Path.GetDirectoryName(target), temporary, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(target).StartsWith("fqr-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing fixture cleanup outside its owned temporary directory.");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Check("Z1c-path the fetch fixture removes its owned scratch directory", !Directory.Exists(target));
        }
    }

    internal sealed class ResumeUniProtHandler : HttpMessageHandler
    {
        internal string Release = "2026_03";
        internal string? FailMarker;
        internal HttpStatusCode FailureStatus = HttpStatusCode.BadRequest;
        internal int Probes;
        internal List<string> Downloads { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Uri uri = request.RequestUri!;
            string query = Uri.UnescapeDataString(uri.Query);
            if (query.EndsWith("query=*", StringComparison.Ordinal))
            {
                Probes++;
                return Task.FromResult(Reply("{\"results\":[]}", 0));
            }
            bool stream = uri.AbsolutePath.EndsWith("/stream", StringComparison.Ordinal);
            bool evidence = query.Contains("accessions=", StringComparison.Ordinal);
            if (FailMarker != null && query.Contains(FailMarker, StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(FailureStatus));
            var names = evidence ? query[(query.IndexOf("accessions=", StringComparison.Ordinal) + 11)..].Split(',')
                : Regex.Matches(query, "family:\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
            if (stream || evidence) Downloads.Add(query);
            string body = stream || evidence ? PanelPage(names.Select(name =>
                PanelEntryJson(evidence ? name : "P" + name[1..6],
                    "Belongs to the " + (evidence ? "fixture family" : name) + ".", "PEPTIDEK")).ToArray()) : "{\"results\":[]}";
            return Task.FromResult(Reply(body, names.Length));
        }
        private HttpResponseMessage Reply(string body, int count)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            response.Headers.Add("x-uniprot-release", Release);
            response.Headers.Add("x-total-results", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return response;
        }
    }

    private static void EvidenceResumeTests(string workDir)
    {
        using var scratch = new FetchFixtureScratch();
        string root = scratch.Root;
        UniProtFetchOptions Options(string name) => new()
        {
            ResumeDirectory = Path.Combine(root, name), UtcNow = () => DateTimeOffset.UnixEpoch,
            DelayAsync = (_, _) => Task.CompletedTask,
        };
        var families = Enumerable.Range(0, 2500).Select(i => "F" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + " family").ToArray();
        using var interrupted = new ResumeUniProtHandler { FailMarker = "F02400 family" };
        var options = Options("panel");
        bool stopped = false;
        var partial = ReferencePanelFetcher.FetchAsync(families, interrupted, options: options).GetAwaiter().GetResult();
        stopped = !partial.IsComplete && partial.MissingFamilies.SequenceEqual(new[] { "F02400 family" });
        Check("Z1c panel failure after batch 24 keeps the first 24 completed batches on disk",
            stopped && interrupted.Downloads.Count >= 24 && Directory.GetFiles(options.ResumeDirectory!, "*.json", SearchOption.AllDirectories).Length >= 25);
        interrupted.Downloads.Clear(); interrupted.FailMarker = null;
        var resumed = ReferencePanelFetcher.FetchAsync(families, interrupted, options: options).GetAwaiter().GetResult();
        using var uninterrupted = new ResumeUniProtHandler();
        var expected = ReferencePanelFetcher.FetchAsync(families, uninterrupted, options: Options("panel-full")).GetAwaiter().GetResult();
        Check("Z1c panel resume asks only batch 25 and equals the uninterrupted result byte for byte",
            interrupted.Probes == 2 && interrupted.Downloads.Count == 1 && interrupted.Downloads[0].Contains("F02400 family", StringComparison.Ordinal)
            && JsonSerializer.Serialize(resumed) == JsonSerializer.Serialize(expected));
        interrupted.Downloads.Clear(); interrupted.Release = "2026_04";
        var newer = ReferencePanelFetcher.FetchAsync(families, interrupted, options: options).GetAwaiter().GetResult();
        Check("Z1c a new release discards old panel batches before reuse",
            interrupted.Downloads.Count == 25 && newer.Release == "2026_04" && newer.Entries.Count == resumed.Entries.Count
            && Directory.GetFiles(options.ResumeDirectory!, "*.json", SearchOption.AllDirectories)
                .All(p => !File.ReadAllText(p).Contains("2026_03", StringComparison.Ordinal)));

        static FamilyClusterFile FileFixture() => new() { MyExtendedFastaItems = Enumerable.Range(1, 1001).Select(i =>
            new FastaItemExtended { SequenceIdentifier = "sp|P" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + "|FIXTURE", Sequence = "PEPTIDEK" }).ToList() };
        using var protein = new ResumeUniProtHandler { FailMarker = "P00501" };
        var proteinOptions = Options("proteins");
        var file = FileFixture(); string before = file.ToJsonText();
        stopped = false;
        try { EvidenceFetcher.FetchAsync(file, protein, options: proteinOptions).GetAwaiter().GetResult(); }
        catch (HttpRequestException) { stopped = true; }
        Check("Z1c evidence failure preserves the document while retaining the first finished batch",
            stopped && file.ToJsonText() == before && protein.Downloads.Count == 1);
        protein.Downloads.Clear(); protein.FailMarker = null;
        EvidenceFetcher.FetchAsync(file, protein, options: proteinOptions).GetAwaiter().GetResult();
        var fullFile = FileFixture(); using var fullProtein = new ResumeUniProtHandler();
        EvidenceFetcher.FetchAsync(fullFile, fullProtein, options: Options("proteins-full")).GetAwaiter().GetResult();
        Check("Z1c resumed protein evidence skips the completed request and preserves every stored byte",
            protein.Downloads.Count == 2 && protein.Probes == 2 && file.ToJsonText() == fullFile.ToJsonText());
        protein.Downloads.Clear();
        string batchPath = Directory.GetFiles(proteinOptions.ResumeDirectory!, "b-*.json", SearchOption.TopDirectoryOnly).First();
        System.IO.File.WriteAllText(batchPath, "{\"invalid\":true}");
        EvidenceFetcher.FetchAsync(FileFixture(), protein, options: proteinOptions).GetAwaiter().GetResult();
        Check("Z1c a corrupt saved reply is refetched instead of reused", protein.Downloads.Count == 1);
        foreach (string field in new[] { "Scope", "Query" })
        {
            string candidate = Directory.GetFiles(proteinOptions.ResumeDirectory!, "b-*.json").First();
            var record = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(candidate))!;
            record[field] = "A different full cache identity";
            System.IO.File.WriteAllText(candidate, record.ToJsonString());
            protein.Downloads.Clear();
            EvidenceFetcher.FetchAsync(FileFixture(), protein, options: proteinOptions).GetAwaiter().GetResult();
            Check("Z1c-path a shortened-name candidate with another full " + field + " is never reused", protein.Downloads.Count == 1);
        }
        {
            string blocked = Path.Combine(root, "blocked-cache");
            Directory.CreateDirectory(root);
            System.IO.File.WriteAllText(blocked, "A file blocks directory creation for this synthetic cache location.");
            var blockedOptions = new UniProtFetchOptions { ResumeDirectory = blocked, DelayAsync = (_, _) => Task.CompletedTask };
            using var requests = new ResumeUniProtHandler();
            var withoutCache = ReferencePanelFetcher.FetchAsync(new[] { "F00000 family" }, requests, options: blockedOptions).GetAwaiter().GetResult();
            var proteins = FileFixture();
            var fetched = EvidenceFetcher.FetchAsync(proteins, requests, options: blockedOptions).GetAwaiter().GetResult();
            Check("Z1e an unwritable cache location never fails panel or protein fetching",
                withoutCache.IsComplete && withoutCache.Entries.Count == 1 && fetched.Found == 1001 && requests.Downloads.Count == 4);
        }
        {
            string longRoot = Path.Combine(root, new string('p', 220 - root.Length - 1));
            using var requests = new ResumeUniProtHandler();
            var longOptions = new UniProtFetchOptions { ResumeDirectory = longRoot, DelayAsync = (_, _) => Task.CompletedTask };
            var longPanel = ReferencePanelFetcher.FetchAsync(new[] { "F00000 family" }, requests, options: longOptions).GetAwaiter().GetResult();
            string[] paths = Directory.GetFiles(longRoot, "*", SearchOption.AllDirectories);
            Check("Z1c-path a 220-character cache root uses flat files of at most 32 characters and paths below 260",
                longPanel.IsComplete && paths.Length == 2 && Directory.GetDirectories(longRoot).Length == 0
                && paths.All(p => Path.GetFileName(p).Length <= 32 && p.Length < 260)
                && Path.Combine(longRoot, new string('a', 64), new string('b', 64) + ".json").Length > 260);
            requests.Downloads.Clear();
            ReferencePanelFetcher.FetchAsync(new[] { "F00000 family" }, requests, options: longOptions).GetAwaiter().GetResult();
            Check("Z1c-path the long-root fetch really resumes rather than silently falling back", requests.Downloads.Count == 0);
        }
        {
            var legacyOptions = Options("legacy"); using var requests = new ResumeUniProtHandler();
            var single = new[] { "F00000 family" };
            var expectedLegacy = ReferencePanelFetcher.FetchAsync(single, requests, options: legacyOptions).GetAwaiter().GetResult();
            string cached = Directory.GetFiles(legacyOptions.ResumeDirectory!, "b-*.json").Single();
            using var reply = JsonDocument.Parse(System.IO.File.ReadAllText(cached));
            string json = reply.RootElement.GetProperty("Json").GetString()!;
            string endpoint = (string)typeof(ReferencePanelFetcher).GetField("StreamEndpoint", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            string query = endpoint + Uri.EscapeDataString((string)ReuseMethod(typeof(ReferencePanelFetcher), "StreamQuery").Invoke(null, new object[] { single })!);
            static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
            foreach (string path in Directory.GetFiles(legacyOptions.ResumeDirectory!, "*.json")) System.IO.File.Delete(path);
            string previous = Path.Combine(legacyOptions.ResumeDirectory!, Digest(query)); Directory.CreateDirectory(previous);
            System.IO.File.WriteAllText(Path.Combine(previous, "manifest.json"), JsonSerializer.Serialize(new { Release = "2026_03", RetrievedAtUtc = DateTimeOffset.UnixEpoch }));
            System.IO.File.WriteAllText(Path.Combine(previous, Digest(query) + ".json"), JsonSerializer.Serialize(new
                { Query = query, Release = "2026_03", Json = json, Announced = 1, Sha256 = Digest(json) }));
            requests.Downloads.Clear();
            var migrated = ReferencePanelFetcher.FetchAsync(single, requests, options: legacyOptions).GetAwaiter().GetResult();
            Check("Z1c-path valid older nested batches migrate without redownloading or changing result bytes", requests.Downloads.Count == 0
                && JsonSerializer.Serialize(migrated) == JsonSerializer.Serialize(expectedLegacy)
                && Directory.GetFiles(legacyOptions.ResumeDirectory!, "b-*.json").Length == 1);
        }
    }
}
