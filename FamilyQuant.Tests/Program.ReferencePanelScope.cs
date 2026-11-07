using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReferencePanelScopeCommands() =>
    [new("--test-reference-panel-scope", 100, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-reference-panel-scope", (args, _) =>
        {
            ReferencePanelScopeTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.PanelScope"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
        })];

    private sealed class ScopedPanelHandler(List<(PanelEntry Entry, string Json)> catalogue) : HttpMessageHandler
    {
        internal readonly List<string> Queries = new();
        internal string Release = "fixture-release";
        internal string? RefusedFamily;
        internal long Bytes;
        internal Action<string>? OnQuery;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            string query = uri.Query.Contains("query=", StringComparison.Ordinal)
                ? Uri.UnescapeDataString(uri.Query.Split("query=", 2)[1])
                : "accession:" + Uri.UnescapeDataString(uri.Query.Split("accessions=", 2)[1].Split('&')[0]).Replace(",", " OR accession:", StringComparison.Ordinal);
            Queries.Add(query); OnQuery?.Invoke(query); token.ThrowIfCancellationRequested();
            var families = Regex.Matches(query, "family:\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (RefusedFamily != null && families.Contains(RefusedFamily))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            int clause = query.LastIndexOf(" AND (\"", StringComparison.Ordinal);
            var scope = clause < 0 ? null : Regex.Matches(query[clause..], "\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();
            var accessions = Regex.Matches(query, "accession:([A-Z0-9-]+)").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var chosen = catalogue.Where(item => (ReferencePanelFetcher.FamiliesOf(item.Entry.Evidence).Any(families.Contains) || accessions.Contains(item.Entry.Accession))
                && (scope == null || EvidenceClassifier.IsInScope(item.Entry.Evidence, scope))).ToArray();
            bool count = uri.Query.Contains("size=0", StringComparison.Ordinal);
            string body = count ? PanelPage() : PanelPage(chosen.Select(item => item.Json).ToArray()); Bytes += Encoding.UTF8.GetByteCount(body);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            response.Headers.Add("x-uniprot-release", Release);
            if (uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal)) response.Headers.Add("x-total-results", chosen.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Task.FromResult(response);
        }
    }

    private static (FamilyClusterFile File, List<(PanelEntry Entry, string Json)> Catalogue) ScopedPanelDatabase(bool fixture)
    {
        var file = fixture ? EvidenceMatchingFile() : new FamilyClusterFile();
        file.FamilyPanel = null; file.EvidenceSource = new() { Provider = "UniProt", Release = "fixture-release" };
        var catalogue = new List<(PanelEntry, string)>();
        uint seed = fixture ? 97u : 43u;
        string alpha = NearestBase(220, seed), beta = NearestBase(280, seed + 1), gamma = NearestBase(260, seed + 2);
        var domains = new[] { "Invented alpha family", "Invented beta family", "Invented gamma family" }
            .Select((family, index) => (family, domain: "PF0900" + (index + 1))).ToDictionary(pair => pair.family, pair => pair.domain);
        ProteinEvidence E(string family, string keyword, bool reviewed = false) => new()
        {
            Family = family, Reviewed = reviewed, Fragment = false, TransmembraneCount = 0,
            Keywords = keyword.Length == 0 ? new() : new() { keyword }, Pfam = new() { new() { Accession = domains.GetValueOrDefault(family, "PF09999"), Name = "Invented fold" } },
        };
        void Target(string family, string sequence, string keyword, bool reviewed = false)
        {
            int index = file.MyExtendedFastaItems.Count;
            file.MyExtendedFastaItems.Add(new() { UserID = index, USearchID = index, SequenceIdentifier = (reviewed ? "sp|P8" : "tr|Q8") + index.ToString("D4") + "|invented",
                Description = "Invented target", Sequence = sequence, Evidence = E(family, keyword, reviewed) });
        }
        void Reference(string family, string sequence, string keyword)
        {
            string accession = "P" + (50000 + catalogue.Count);
            string json = PanelEntryJson(accession, "Belongs to the " + family + ".", sequence, keyword: keyword).Replace("PF09999", domains.GetValueOrDefault(family, "PF09999"), StringComparison.Ordinal);
            var evidence = EvidenceFetcher.Parse(PanelPage(json))[accession];
            catalogue.Add((new PanelEntry { Accession = accession, Sequence = sequence, Evidence = evidence }, json));
        }
        Target("Invented alpha family", alpha, "Chosen phase", true);
        Target("Invented beta family", beta, "Later phase");
        Target("Invented gamma family", gamma + "CCCCCCCCCC", "");
        Target("", NearestMutate(gamma, 25), "");
        Reference("Invented alpha family", NearestMutate(alpha, 17), "Chosen phase");
        Reference("Invented beta family", NearestMutate(beta, 19), "Later phase");
        Reference("Invented gamma family", NearestMutate(gamma, 31), "Chosen phase");
        // A slice alone misses enough complete members to change the learned profile and default tick.
        for (int member = 0; member < 11; member++) Reference("Invented gamma family", gamma[..(100 + member)], "");
        int groups = fixture ? 35 : 110;
        for (int group = 0; group < groups; group++)
        {
            string family = "Invented background " + group + " family";
            Target(family, NearestBase(150, seed + (uint)group + 50), "");
            for (int member = 0; member < 18; member++) Reference(family, NearestBase(300 + member, seed + (uint)group * 31 + (uint)member + 1000), "Unrelated phase");
        }
        return (file, catalogue);
    }

    private static void ReferencePanelScopeTests(string output) => ReferencePanelScopeDatabaseTests(output, false, includeGuards: true);

    private static void ReferencePanelScopeDatabaseTests(string output, bool fixtureOnly, bool expansion = false, bool includeGuards = false)
    {
        Directory.CreateDirectory(output);
        var options = new UniProtFetchOptions { DelayAsync = (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, UtcNow = () => DateTimeOffset.UnixEpoch };
        var measurements = new List<object>();
        foreach (bool fixture in new[] { fixtureOnly })
        {
            var (file, catalogue) = ScopedPanelDatabase(fixture); string original = file.ToJsonText();
            using var fullHandler = new ScopedPanelHandler(catalogue); var timer = Stopwatch.StartNew();
            var full = ReferencePanelFetcher.FetchAsync(ReferencePanelFetcher.FamiliesFound(file), fullHandler, options: options).GetAwaiter().GetResult();
            double fullMs = timer.Elapsed.TotalMilliseconds;
            using var fastHandler = new ScopedPanelHandler(catalogue); timer.Restart();
            var fast = ReferencePanelFetcher.FetchForScopeAsync(file, new(new[] { "Chosen phase" }), fastHandler, options: options).GetAwaiter().GetResult();
            double fastMs = timer.Elapsed.TotalMilliseconds;
            Check("Z15a " + fixture + " scope records coverage and keeps needed families whole",
                fast is { IsComplete: true, Coverage: not null } && fast.Coverage.ScopeTerms.SequenceEqual(new[] { "CHOSEN PHASE" })
                && fast.Coverage.WholeFamilies.Contains("Invented gamma family", StringComparer.OrdinalIgnoreCase) && fast.Entries.Count < full.Entries.Count);
            EvidenceReviewInput Propose(ReferencePanel panel, out string proposal)
            {
                var copy = FamilyClusterFile.FromJsonText(original); copy.EvidenceSource!.ReferencePanel = panel;
                var session = new EvidenceGroupingSession(copy, "fixture"); var review = session.Propose("\"Chosen phase\"");
                proposal = JsonSerializer.Serialize(session.Proposal); return review;
            }
            var fullReview = Propose(full, out string fullJson); var fastReview = Propose(fast, out string fastJson);
            var naive = JsonSerializer.Deserialize<ReferencePanel>(JsonSerializer.Serialize(fast))!;
            naive.Entries = naive.Entries.Where(entry => ReferencePanelFetcher.FamiliesOf(entry.Evidence).Any(family => family == "Invented alpha family")
                || EvidenceClassifier.IsInScope(entry.Evidence, new[] { "Chosen phase" })).ToList();
            Propose(naive, out string naiveJson);
            Check("Z15a " + fixture + " the unclosed slice is an adversarial coherence regression", naiveJson != fullJson);
            Check("Z15a " + fixture + " native search proposal including every neighbour, tier, reason and flag is byte identical", fullJson == fastJson);
            var fullView = new EvidenceReviewViewModel(fullReview); var fastView = new EvidenceReviewViewModel(fastReview);
            Check("Z15a " + fixture + " default protein ticks are identical", fullView.Proteins.Select(row => row.Selected).SequenceEqual(fastView.Proteins.Select(row => row.Selected)));
            Check("Z15a " + fixture + " timed synthetic fetch transfers fewer response bytes", fastHandler.Bytes < fullHandler.Bytes && file.ToJsonText() == original);
            Console.WriteLine($"Z15a fixture={fixture} full_ms={fullMs:F3} scope_ms={fastMs:F3} full_bytes={fullHandler.Bytes} scope_bytes={fastHandler.Bytes} full_entries={full.Entries.Count} scope_entries={fast.Entries.Count} proposal_equal={fullJson == fastJson}");
            measurements.Add(new { fixture, fullMs, scopeMs = fastMs, fullBytes = fullHandler.Bytes, scopeBytes = fastHandler.Bytes, fullEntries = full.Entries.Count, scopeEntries = fast.Entries.Count, proposalEqual = fullJson == fastJson });
            if (expansion)
            {
                file.EvidenceSource!.ReferencePanel = fast;
                using var expand = new ScopedPanelHandler(catalogue);
                var expanded = ReferencePanelFetcher.FetchForScopeAsync(file, new(new[] { "Chosen phase", "Later phase" }), expand, options: options).GetAwaiter().GetResult();
                Check("Z15a " + fixture + " expansion fetches only new scope slices and newly whole families",
                    expand.Queries.All(query => !query.Contains("family:\"Invented alpha family\"", StringComparison.OrdinalIgnoreCase)
                        && !query.Contains("family:\"Invented gamma family\"", StringComparison.OrdinalIgnoreCase)
                        && !query.Contains("\"CHOSEN PHASE\"", StringComparison.Ordinal)) && expanded.Coverage!.ScopeTerms.Count == 2);
                file.EvidenceSource.ReferencePanel = expanded; using var reuse = new ScopedPanelHandler(catalogue);
                var reused = ReferencePanelFetcher.FetchForScopeAsync(file, new(new[] { "chosen-phase" }), reuse, options: options).GetAwaiter().GetResult();
                Check("Z15a " + fixture + " covered scope reuses stored entries without requests", reuse.Queries.Count == 0 && reused.Entries.Count == expanded.Entries.Count);
                Check("Z15a " + fixture + " unknown scope and full-only readers reject focused data",
                    Throws<InvalidDataException>(() => ReferencePanelFetcher.RequireScope(fast, new[] { "Later phase" }))
                    && Throws<InvalidDataException>(() => ReferencePanelFetcher.References(fast)));
                using var all = new ScopedPanelHandler(catalogue);
                var promoted = ReferencePanelFetcher.FetchForScopeAsync(file, new(Array.Empty<string>(), true), all, options: options).GetAwaiter().GetResult();
                Check("Z15a " + fixture + " Full panel promotes only missing families and reproduces every full entry",
                    promoted.Coverage == null && JsonSerializer.Serialize(promoted.Entries) == JsonSerializer.Serialize(full.Entries)
                    && all.Queries.All(query => !query.Contains("family:\"Invented alpha family\"", StringComparison.OrdinalIgnoreCase)));
            }
        }
        File.WriteAllText(Path.Combine(output, fixtureOnly ? "timings-second.json" : "timings-synthetic.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        if (!includeGuards) return;
        var (guardFile, guardCatalogue) = ScopedPanelDatabase(true); string before = guardFile.ToJsonText();
        using var released = new ScopedPanelHandler(guardCatalogue) { Release = "changed-release" };
        Check("Z15a changed release never mixes stored and fetched evidence", Throws<IOException>(() => EvidenceFetchSession.RetryPanelAsync(guardFile, released, options: options,
            panelRequest: new(new[] { "Chosen phase" })).GetAwaiter().GetResult()) && guardFile.ToJsonText() == before);
        using var cancelled = new CancellationTokenSource(); using var cancelHandler = new ScopedPanelHandler(guardCatalogue) { OnQuery = _ => cancelled.Cancel() };
        Check("Z15a cancellation never publishes a scope panel", Throws<OperationCanceledException>(() => EvidenceFetchSession.RetryPanelAsync(guardFile, cancelHandler,
            cancellationToken: cancelled.Token, options: options, panelRequest: new(new[] { "Chosen phase" })).GetAwaiter().GetResult()) && guardFile.ToJsonText() == before);
        using var refused = new ScopedPanelHandler(guardCatalogue) { RefusedFamily = "Invented gamma family" };
        var partial = EvidenceFetchSession.RetryPanelAsync(guardFile, refused, options: options, panelRequest: new(new[] { "Chosen phase" })).GetAwaiter().GetResult();
        Check("Z15a missing scope families remain explicitly incomplete", partial.Panel is { IsComplete: false, Coverage: not null }
            && partial.Panel.MissingFamilies.Contains("Invented gamma family", StringComparer.OrdinalIgnoreCase)
            && Throws<InvalidDataException>(() => ReferencePanelFetcher.RequireScope(partial.Panel, new[] { "Chosen phase" })));
        using var resumeHandler = new ScopedPanelHandler(guardCatalogue);
        var resumed = EvidenceFetchSession.RetryPanelAsync(guardFile, resumeHandler, options: options, panelRequest: new(new[] { "Chosen phase" })).GetAwaiter().GetResult();
        var clean = FamilyClusterFile.FromJsonText(before); using var uninterruptedHandler = new ScopedPanelHandler(guardCatalogue);
        var uninterrupted = EvidenceFetchSession.RetryPanelAsync(clean, uninterruptedHandler, options: options, panelRequest: new(new[] { "Chosen phase" })).GetAwaiter().GetResult();
        Check("Z15a resumed staged result is byte identical to uninterrupted scope fetch", JsonSerializer.Serialize(resumed.Panel) == JsonSerializer.Serialize(uninterrupted.Panel));
        string cacheRoot = Path.Combine(output, "resume-" + Guid.NewGuid().ToString("N"));
        var cacheOptions = new UniProtFetchOptions { ResumeDirectory = cacheRoot, DelayAsync = options.DelayAsync, UtcNow = options.UtcNow };
        var diskSource = FamilyClusterFile.FromJsonText(before); using var interrupted = new CancellationTokenSource();
        using (var diskHandler = new ScopedPanelHandler(guardCatalogue) { OnQuery = query => { if (query.Contains("\"CHOSEN PHASE\"", StringComparison.Ordinal)) interrupted.Cancel(); } })
            Check("Z15a cancellation leaves completed scope phases on disk without publishing", Throws<OperationCanceledException>(() => EvidenceFetchSession.RetryPanelAsync(diskSource, diskHandler,
                cancellationToken: interrupted.Token, options: cacheOptions, panelRequest: new(new[] { "Chosen phase" })).GetAwaiter().GetResult()) && diskSource.ToJsonText() == before
                && Directory.GetFiles(cacheRoot, "*.json", SearchOption.AllDirectories).Length > 0);
        using (var diskResume = new ScopedPanelHandler(guardCatalogue))
        {
            var result = EvidenceFetchSession.RetryPanelAsync(diskSource, diskResume, options: cacheOptions, panelRequest: new(new[] { "Chosen phase" })).GetAwaiter().GetResult();
            Check("Z15a disk resume reuses whole finished batches and is byte identical", diskResume.Queries.All(query => !query.Contains("family:\"Invented alpha family\"", StringComparison.OrdinalIgnoreCase))
                && JsonSerializer.Serialize(result.Panel) == JsonSerializer.Serialize(uninterrupted.Panel));
        }
        diskSource.EvidenceSource!.Release = "next-fixture-release";
        using (var changedRelease = new ScopedPanelHandler(guardCatalogue) { Release = "next-fixture-release" })
        {
            var result = EvidenceFetchSession.RetryPanelAsync(diskSource, changedRelease, options: cacheOptions, panelRequest: new(new[] { "Chosen phase" })).GetAwaiter().GetResult();
            Check("Z15a a new release discards recorded and disk scope coverage", result.Panel!.Release == "next-fixture-release"
                && changedRelease.Queries.Any(query => query.Contains("family:\"Invented alpha family\"", StringComparison.OrdinalIgnoreCase)));
        }

    }
}
