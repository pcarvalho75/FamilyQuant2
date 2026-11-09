using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class TaxonomyRefusalHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal Func<Uri, bool> Refuse = _ => false;
        internal HttpStatusCode Status = HttpStatusCode.TooManyRequests;
        internal RetryConditionHeaderValue? RetryAfter;
        internal string? RawRetryAfter;
        internal int FailuresRemaining = 1, Failures;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (FailuresRemaining > 0 && Refuse(request.RequestUri!))
            {
                FailuresRemaining--; Failures++;
                var response = new HttpResponseMessage(Status);
                response.Headers.RetryAfter = RetryAfter;
                if (RawRetryAfter != null) response.Headers.TryAddWithoutValidation("Retry-After", RawRetryAfter);
                return Task.FromResult(response);
            }
            return base.SendAsync(request, token);
        }
    }

    private static FamilyClusterFile ManyTaxaFile(int count = 300, int firstId = 21) => new()
    {
        EvidenceSource = new EvidenceProvenance { Provider = "UniProt", Release = "fixture" },
        MyExtendedFastaItems = Enumerable.Range(0, count).Reverse().Select(i =>
            TaskProtein("sp|P" + i.ToString("D5", CultureInfo.InvariantCulture) + "|FIXTURE", firstId + i, true,
                i % 3 == 0 ? "Other" : "Leading scope")).ToList(),
    };

    private static int StoredTaxonomyBatchCount(string directory)
    {
        // Batch records have query, reply and integrity fields. Manifests do not.
        // Inspect contents so both flat and legacy nested cache names are supported.
        int count = 0;
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement record = document.RootElement;
            if (record.ValueKind == JsonValueKind.Object
                && record.TryGetProperty("Query", out var query) && query.ValueKind == JsonValueKind.String
                && record.TryGetProperty("Json", out var json) && json.ValueKind == JsonValueKind.String
                && record.TryGetProperty("Sha256", out var hash) && hash.ValueKind == JsonValueKind.String
                && record.TryGetProperty("Release", out var release) && release.ValueKind == JsonValueKind.String)
                count++;
        }
        return count;
    }

    private static void TaxonomyBatchTests(string workDir)
    {
        string root = Path.GetFullPath(Path.Combine(workDir, "taxonomy-batches-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var instant = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        static bool IsTaxonomy(Uri uri) => uri.AbsolutePath.StartsWith("/taxonomy/", StringComparison.Ordinal);
        static string Proposal(FamilyClusterFile file, TaskTaxonomyHandler handler, bool single = false) =>
            JsonSerializer.Serialize(EvidenceTaskDiscovery.DiscoverAsync(file, handler, usePerTaxonComparison: single).GetAwaiter().GetResult());
        static string[] BatchUrls(TaskTaxonomyHandler handler) => handler.Urls.Where(u => new Uri(u).AbsolutePath == "/taxonomy/search").ToArray();
        void Verify(string name, Action run)
        {
            try { run(); }
            catch (Exception ex) { Check("Z1m " + name, false, ex.GetBaseException().Message); }
        }
        try
        {
            foreach (int count in new[] { 87, 300 })
            foreach (bool retryVocabulary in new[] { false, true })
                Verify("taxa busy units", () =>
                {
                    var file = ManyTaxaFile(count);
                    using var inner = new TaskTaxonomyHandler();
                    using var handler = new TaxonomyRefusalHandler(inner) { Refuse = uri => retryVocabulary
                        ? uri.AbsolutePath == "/keywords/search" : IsTaxonomy(uri)
                            && (count == 87 || Uri.UnescapeDataString(uri.Query).Contains("tax_id:121)", StringComparison.Ordinal)) };
                    var values = new List<FetchProgress>();
                    var groupingReports = new List<OperationProgress>(); var familyReports = new List<OperationProgress>();
                    static IProgress<FetchProgress> Adapter(Type owner, string name, List<OperationProgress> reports) =>
                        (IProgress<FetchProgress>)Activator.CreateInstance(owner.GetNestedType(name, BindingFlags.NonPublic)!,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                            new object[] { new OperationContext(progress: new FetchOperationProgress(reports.Add)) }, CultureInfo.InvariantCulture)!;
                    var grouping = Adapter(typeof(SuperClusterPreviewWindow), "GroupingTaskProgress", groupingReports);
                    var family = Adapter(typeof(ClusterGenerationControl), "FamilyWorldTaskProgress", familyReports);
                    var progress = new InlineFetchProgress(value => { values.Add(value); grouping.Report(value); family.Report(value); });
                    var options = new UniProtFetchOptions { DelayAsync = (_, _) => Task.CompletedTask };
                    EvidenceTaskDiscovery.DiscoverAsync(file, handler, progress, options: options).GetAwaiter().GetResult();
                    string scenario = count.ToString(CultureInfo.InvariantCulture) + (retryVocabulary ? " vocabulary retry" : " taxonomy retry");
                    Check("Z1m-fix2 " + scenario + " reports finished lineages and total taxa consistently",
                        handler.Failures == 1 && values.Count > 1 && values.All(p => p.TotalBatches == count && p.Asked == count
                            && p.CompletedBatches == p.Found && p.CompletedBatches <= count)
                        && values[^1].CompletedBatches == count
                        && values.Where(p => p.Message.Length == 0).Select(p => p.CompletedBatches)
                            .SequenceEqual(count == 87 ? new[] { 87 } : new[] { 100, 200, 300 }));
                    Check("Z1m-fix2 " + scenario + " keeps both busy labels and logged numbers in taxa",
                        groupingReports.Count == values.Count && familyReports.Count == values.Count
                        && values.Select((value, i) =>
                        {
                            string completed = value.CompletedBatches.ToString(CultureInfo.CurrentCulture);
                            string total = count.ToString(CultureInfo.CurrentCulture);
                            return groupingReports[i].Completed == value.Found && groupingReports[i].Total == count
                                && familyReports[i].Completed == value.Found && familyReports[i].Total == count
                                && groupingReports[i].Message == (value.Message.Length > 0 ? value.Message
                                    : "Asking UniProt about taxon " + completed + " of " + total + "\nNeeds an internet connection")
                                && familyReports[i].Message == (value.Message.Length > 0 ? value.Message
                                    : "Reading database task: " + completed + " of " + total + " taxa.");
                        }).All(correct => correct));
                });
            foreach (string culture in new[] { "en-US", "pt-BR" })
            foreach (bool sharedFamily in new[] { true, false })
                Verify("300-taxon proposal parity", () =>
                {
                    using var locale = new TaskCultureScope(culture);
                    var file = ManyTaxaFile();
                    file.MyExtendedFastaItems.Add(TaskProtein("tr|P99999|ALIAS", 21, false));
                    file.MyExtendedFastaItems[^1].Description = "Fixture OS=Header alias OX=21";
                    string before = file.ToJsonText();
                    using var batched = new TaskTaxonomyHandler { DifferentFamilyTaxon = sharedFamily ? null : 23 };
                    using var single = new TaskTaxonomyHandler { DifferentFamilyTaxon = batched.DifferentFamilyTaxon };
                    string actual = Proposal(file, batched);
                    Check("Z1m batch and individual proposals are byte identical in " + culture + (sharedFamily ? " with family" : " with ancestor"),
                        actual == Proposal(file, single, true) && file.ToJsonText() == before);
                    string[] urls = BatchUrls(batched);
                    Check("Z1m 300 distinct taxa use three bounded requests plus vocabulary", batched.Requests == 4 && urls.Length == 3
                        && single.Requests == 301 && urls.All(u => u.Length <= 7000
                            && System.Text.RegularExpressions.Regex.Matches(Uri.UnescapeDataString(u), "tax_id:").Count <= 100));
                });
            Verify("large taxon IDs", () =>
            {
                var file = ManyTaxaFile(firstId: int.MaxValue - 299);
                using var batch = new TaskTaxonomyHandler(); using var single = new TaskTaxonomyHandler();
                Check("Z1m ten-digit IDs retain exact proposals and bounded URLs", Proposal(file, batch) == Proposal(file, single, true)
                    && BatchUrls(batch).Length == 3 && BatchUrls(batch).All(u => u.Length <= 7000));
            });
            foreach (string retry in new[] { "delta", "date", "past", "short", "missing", "invalid", "503" })
                Verify("Retry-After " + retry, () =>
                {
                    var file = TaskFile(); string before = file.ToJsonText();
                    using var inner = new TaskTaxonomyHandler();
                    using var handler = new TaxonomyRefusalHandler(inner)
                    {
                        Refuse = IsTaxonomy, Status = retry == "503" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.TooManyRequests,
                        RetryAfter = retry switch
                        {
                            "delta" => new RetryConditionHeaderValue(TimeSpan.FromSeconds(22)),
                            "date" => new RetryConditionHeaderValue(instant.AddSeconds(37)),
                            "past" => new RetryConditionHeaderValue(instant.AddSeconds(-10)),
                            "short" => new RetryConditionHeaderValue(TimeSpan.FromSeconds(1)),
                            _ => null,
                        },
                        RawRetryAfter = retry == "invalid" ? "invalid fixture value" : null,
                    };
                    var waits = new List<double>(); var messages = new List<string>();
                    double expected = retry == "delta" ? 22 : retry == "date" ? 37 : 5;
                    var options = new UniProtFetchOptions { UtcNow = () => instant,
                        DelayAsync = (delay, _) => { waits.Add(delay.TotalSeconds); return Task.CompletedTask; } };
                    var result = EvidenceTaskDiscovery.DiscoverAsync(file, handler, new InlineFetchProgress(p => messages.Add(p.Message)), options: options).GetAwaiter().GetResult();
                    using var baseline = new TaskTaxonomyHandler();
                    Check("Z1m " + retry + " waits are injected and reported before the same proposal succeeds",
                        waits.SequenceEqual(new[] { expected }) && messages.Contains("UniProt busy, retrying in " + expected.ToString(CultureInfo.CurrentCulture) + " s")
                        && handler.Failures == 1 && JsonSerializer.Serialize(result) == Proposal(file, baseline) && file.ToJsonText() == before);
                });
            Verify("cancel Retry-After", () =>
            {
                var file = TaskFile(); string before = file.ToJsonText();
                using var inner = new TaskTaxonomyHandler();
                using var handler = new TaxonomyRefusalHandler(inner)
                { Refuse = IsTaxonomy, RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(22)) };
                using var cancel = new CancellationTokenSource(); bool refused = false, waited = false;
                var options = new UniProtFetchOptions { DelayAsync = (delay, token) =>
                {
                    waited = delay == TimeSpan.FromSeconds(22) && token == cancel.Token;
                    cancel.Cancel(); return Task.FromCanceled(token);
                } };
                try { EvidenceTaskDiscovery.DiscoverAsync(file, handler, cancellationToken: cancel.Token, options: options).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { refused = true; }
                Check("Z1m Retry-After uses the caller's cancellation token and sends no request after cancellation",
                    waited && refused && handler.Failures == 1 && inner.Requests == 0 && file.ToJsonText() == before);
            });
            foreach (string damage in new[] { "missing", "duplicate", "extra", "inactive", "no lineage", "repeated ancestor", "incomplete ancestor", "text id", "non-object ancestor" })
                Verify("refuse " + damage, () =>
                {
                    var file = TaskFile(); string before = file.ToJsonText();
                    using var handler = new TaskTaxonomyHandler { TransformTaxonomyJson = json =>
                    {
                        var node = JsonNode.Parse(json)!; var results = node["results"]!.AsArray();
                        switch (damage)
                        {
                            case "missing": results.RemoveAt(0); break;
                            case "duplicate": results.Add(results[0]!.DeepClone()); break;
                            case "extra": var extra = results[0]!.DeepClone(); extra["taxonId"] = 999; results.Add(extra); break;
                            case "inactive": results[0]!["active"] = false; break;
                            case "no lineage": results[0]!.AsObject().Remove("lineage"); break;
                            case "repeated ancestor": var path = results[0]!["lineage"]!.AsArray(); path.Add(path[0]!.DeepClone()); break;
                            case "incomplete ancestor": results[0]!["lineage"]![0]!.AsObject().Remove("rank"); break;
                            case "text id": results[0]!["taxonId"] = "21"; break;
                            case "non-object ancestor": results[0]!["lineage"]![0] = "invalid fixture value"; break;
                        }
                        return node.ToJsonString();
                    } };
                    bool refused = false;
                    string directory = Path.Combine(root, damage);
                    try { EvidenceTaskDiscovery.DiscoverAsync(file, handler, options: new UniProtFetchOptions { ResumeDirectory = directory }).GetAwaiter().GetResult(); }
                    catch (InvalidDataException) { refused = true; }
                    Check("Z1m " + damage + " cannot produce a proposal, save an invalid batch or change the file",
                        refused && StoredTaxonomyBatchCount(directory) == 0 && file.ToJsonText() == before);
                });
            foreach (string interruption in new[] { "429", "503", "cancel", "keywords" })
                Verify("resume " + interruption, () =>
                {
                    string directory = Path.Combine(root, interruption);
                    var file = ManyTaxaFile(); string before = file.ToJsonText();
                    using var inner = new TaskTaxonomyHandler();
                    using var handler = new TaxonomyRefusalHandler(inner)
                    {
                        Refuse = uri => interruption == "keywords" ? uri.AbsolutePath == "/keywords/search"
                            : IsTaxonomy(uri) && Uri.UnescapeDataString(uri.Query).Contains("tax_id:121)", StringComparison.Ordinal),
                        FailuresRemaining = 10, Status = interruption == "503" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.TooManyRequests,
                    };
                    using var cancel = new CancellationTokenSource(); var waits = new List<double>();
                    var options = new UniProtFetchOptions { ResumeDirectory = directory, DelayAsync = (delay, token) =>
                    {
                        waits.Add(delay.TotalSeconds);
                        if (interruption == "cancel") { cancel.Cancel(); return Task.FromCanceled(token); }
                        return Task.CompletedTask;
                    } };
                    bool refused = false;
                    try { EvidenceTaskDiscovery.DiscoverAsync(file, handler, cancellationToken: cancel.Token, options: options).GetAwaiter().GetResult(); }
                    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { refused = true; }
                    int stored = StoredTaxonomyBatchCount(directory);
                    var reports = new List<FetchProgress>(); using var retry = new TaskTaxonomyHandler();
                    var result = EvidenceTaskDiscovery.DiscoverAsync(file, retry, new InlineFetchProgress(reports.Add), options: options).GetAwaiter().GetResult();
                    using var baseline = new TaskTaxonomyHandler();
                    Check("Z1m " + interruption + " preserves finished batches and resumes to the uninterrupted proposal",
                        refused && stored == (interruption == "keywords" ? 3 : 1) && BatchUrls(retry).Length == (interruption == "keywords" ? 0 : 2)
                        && reports.Any(p => p.Message == "Using saved taxonomy batches") && JsonSerializer.Serialize(result) == Proposal(file, baseline)
                        && reports.All(p => p.TotalBatches == 300 && p.CompletedBatches == p.Found && p.Asked == 300)
                        && file.ToJsonText() == before);
                    Check("Z1m " + interruption + " patient waits stay bounded and cancellation stops immediately",
                        waits.SequenceEqual(interruption == "cancel" ? new double[] { 5 } : new double[] { 5, 15, 45, 120 }));
                    using var newRelease = new TaskTaxonomyHandler { Release = "next" };
                    var replaced = EvidenceTaskDiscovery.DiscoverAsync(file, newRelease, options: options).GetAwaiter().GetResult();
                    Check("Z1m a new release discards all saved lineages", BatchUrls(newRelease).Length == 3 && replaced.Release == "next"
                        && StoredTaxonomyBatchCount(directory) == 3);
                });
            Verify("mid-fetch release guard", () =>
            {
                var file = ManyTaxaFile(); string before = file.ToJsonText(); bool refused = false;
                using var handler = new TaskTaxonomyHandler { ChangedRelease = true };
                try { EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult(); }
                catch (InvalidDataException) { refused = true; }
                Check("Z1m release drift between batches cannot produce a task", refused && BatchUrls(handler).Length == 2 && file.ToJsonText() == before);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
        Check("Z1m synthetic taxonomy caches clean up after checks", !Directory.Exists(root));
    }
}
