using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class ResumeUniProtHandler : HttpMessageHandler
    {
        internal string Release = "2026_03";
        internal string? FailMarker;
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
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
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
        string root = Path.Combine(workDir, "resume-" + Guid.NewGuid().ToString("N"));
        UniProtFetchOptions Options(string name) => new()
        {
            ResumeDirectory = Path.Combine(root, name), UtcNow = () => DateTimeOffset.UnixEpoch,
            DelayAsync = (_, _) => Task.CompletedTask,
        };
        var families = Enumerable.Range(0, 2500).Select(i => "F" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + " family").ToArray();
        using var interrupted = new ResumeUniProtHandler { FailMarker = "F02400 family" };
        var options = Options("panel");
        bool stopped = false;
        try { ReferencePanelFetcher.FetchAsync(families, interrupted, options: options).GetAwaiter().GetResult(); }
        catch (HttpRequestException) { stopped = true; }
        Check("Z1c panel failure after batch 24 keeps the first 24 completed batches on disk",
            stopped && interrupted.Downloads.Count == 24 && Directory.GetFiles(options.ResumeDirectory!, "*.json", SearchOption.AllDirectories).Length == 25);
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
        string batchPath = Directory.GetFiles(proteinOptions.ResumeDirectory!, "*.json", SearchOption.AllDirectories)
            .First(p => Path.GetFileName(p) != "manifest.json");
        System.IO.File.WriteAllText(batchPath, "{\"invalid\":true}");
        EvidenceFetcher.FetchAsync(FileFixture(), protein, options: proteinOptions).GetAwaiter().GetResult();
        Check("Z1c a corrupt saved reply is refetched instead of reused", protein.Downloads.Count == 1);
    }
}
