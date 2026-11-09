using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReferencePanelScopeRouteCommands() =>
    [new("--test-reference-panel-scope-route", 100, TestCommandPhase.Raw,
        args => args.Length == 1 && args[0] == "--test-reference-panel-scope-route", (_, _) =>
        {
            ReferencePanelScopeRouteTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReferencePanelScopeRouteTests()
    {
        EnsureApplication();
        Section("Z15a6 broad scope uses the full panel without duplicate response bytes");
        var options = new UniProtFetchOptions
        {
            DelayAsync = (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            UtcNow = () => DateTimeOffset.UnixEpoch,
        };
        var file = new FamilyClusterFile { EvidenceSource = new() { Provider = "UniProt", Release = "fixture-release" } };
        var catalogue = new List<(PanelEntry Entry, string Json)>();
        var families = Enumerable.Range(0, 6).Select(i => "Invented route " + i + " family").ToArray();
        for (int family = 0; family < families.Length; family++)
        {
            file.MyExtendedFastaItems.Add(new()
            {
                UserID = family, USearchID = family, SequenceIdentifier = "tr|Q6000" + family + "|invented",
                Sequence = NearestBase(160, (uint)(family + 700)), Description = "Invented route target",
                Evidence = new() { Family = families[family], Keywords = family == 0 ? new() { "Wide phase", "Narrow phase" }
                    : family < 4 ? new() { "Wide phase" } : new() },
            });
            for (int member = 0; member < 3; member++)
            {
                string accession = "P" + (60000 + family * 3 + member);
                string sequence = NearestBase(160, (uint)(family * 3 + member + 900));
                string keyword = family < 2 && member == 0 ? "Wide phase Narrow phase" : "Wide phase";
                string json = PanelEntryJson(accession, "Belongs to the " + families[family] + ".", sequence, keyword: keyword);
                var evidence = EvidenceFetcher.Parse(PanelPage(json))[accession]; evidence.SimilarityAnnotations = null;
                catalogue.Add((new() { Accession = accession, Sequence = sequence, Evidence = evidence }, json));
            }
        }
        string before = file.ToJsonText();
        using var fullHandler = new ScopedPanelHandler(catalogue);
        var full = ReferencePanelFetcher.FetchAsync(ReferencePanelFetcher.FamiliesFound(file), fullHandler, options: options).GetAwaiter().GetResult();
        using var narrowHandler = new ScopedPanelHandler(catalogue);
        var narrowMessages = new List<string>();
        var narrow = ReferencePanelFetcher.FetchForScopeAsync(file, new(new[] { "Narrow phase" }), narrowHandler,
            progress: new InlineFetchProgress(value => narrowMessages.Add(value.Message)), options: options).GetAwaiter().GetResult();
        string Query(IEnumerable<string> names, string suffix = "") => "(reviewed:true) AND ("
            + string.Join(" OR ", names.Select(name => "(family:\"" + name + "\")")) + ")" + suffix;
        var expectedQueries = new[] { Query(families.Take(1)), Query(families.Skip(1), " AND (\"NARROW PHASE\")"), Query(families.Skip(1).Take(1)) }
            .SelectMany(query => new[] { query, query });
        Check("Z15a6 narrow scope keeps the exact count and stream requests", narrowHandler.Queries.SequenceEqual(expectedQueries));
        Check("Z15a6 narrow scope keeps the exact stored entries", JsonSerializer.Serialize(narrow.Entries)
            == JsonSerializer.Serialize(catalogue.Take(6).Select(item => item.Entry).ToList()));
        Check("Z15a6 narrow scope keeps the recorded coverage", narrow.Coverage is { } coverage
            && coverage.ScopeTerms.SequenceEqual(new[] { "NARROW PHASE" }) && coverage.WholeFamilies.SequenceEqual(families.Take(2))
            && narrow.IsComplete && narrowHandler.Bytes < fullHandler.Bytes);
        using var broadHandler = new ScopedPanelHandler(catalogue);
        var broadMessages = new List<string>();
        var broad = ReferencePanelFetcher.FetchForScopeAsync(file, new(new[] { "Wide phase" }), broadHandler,
            progress: new InlineFetchProgress(value => broadMessages.Add(value.Message)), options: options).GetAwaiter().GetResult();
        Check("Z15a6 broad scope never moves more response bytes than a full fetch", broadHandler.Bytes <= fullHandler.Bytes,
            "broad=" + broadHandler.Bytes + ", full=" + fullHandler.Bytes);
        Check("Z15a6 broad scope takes the same requests as a full fetch", broadHandler.Queries.SequenceEqual(fullHandler.Queries));
        Check("Z15a6 broad route stores a byte-identical full panel", JsonSerializer.Serialize(broad) == JsonSerializer.Serialize(full));
        Check("Z15a6 broad route records complete full coverage", broad is { Coverage: null, IsComplete: true }
            && !Throws<InvalidDataException>(() => ReferencePanelFetcher.RequireFull(broad)));
        Check("Z15a6 route selection leaves the detached source unchanged", file.ToJsonText() == before);
        Check("Z15a6 known route messages give the route and family counts", narrowMessages.First() == "Scoped panel: the scope covers 1 of 6 families"
            && broadMessages.First() == "Full panel: the scope covers 4 of 6 families");
        Console.WriteLine($"Z15a6 full_bytes={fullHandler.Bytes} broad_bytes={broadHandler.Bytes} narrow_bytes={narrowHandler.Bytes} full_requests={fullHandler.Queries.Count} broad_requests={broadHandler.Queries.Count} narrow_requests={narrowHandler.Queries.Count}");

        var expandedSource = FamilyClusterFile.FromJsonText(before);
        expandedSource.EvidenceSource!.ReferencePanel = narrow;
        expandedSource.MyExtendedFastaItems[2].Evidence!.Keywords.Add("Stored phase");
        using var expandedHandler = new ScopedPanelHandler(catalogue);
        var expanded = ReferencePanelFetcher.FetchForScopeAsync(expandedSource, new(new[] { "Stored phase" }), expandedHandler, options: options).GetAwaiter().GetResult();
        Check("Z15a6 stored reference closure reaches the half-family fallback", expanded is { Coverage: null, IsComplete: true }
            && JsonSerializer.Serialize(expanded) == JsonSerializer.Serialize(full) && expandedHandler.Bytes <= fullHandler.Bytes);
        Check("Z15a6 automatic full promotion fetches only missing whole families", expandedHandler.Queries.SequenceEqual(
            Enumerable.Repeat(Query(families.Skip(2)), 2)));

        using var stagedHandler = new ScopedPanelHandler(catalogue);
        var stagedMessages = new List<string>();
        var staged = EvidenceFetchSession.RetryPanelAsync(file, stagedHandler,
            progress: new InlineFetchProgress(value => stagedMessages.Add(value.Message)), options: options,
            panelRequest: new(new[] { "Wide phase" })).GetAwaiter().GetResult();
        Check("Z15a6 staged caller publishes the full panel", staged.Panel is { Coverage: null, IsComplete: true }
            && JsonSerializer.Serialize(file.EvidenceSource!.ReferencePanel) == JsonSerializer.Serialize(full));
        Check("Z15a6 staged operation forwards the full-route reason", stagedMessages.Contains("Full panel: the scope covers 4 of 6 families", StringComparer.Ordinal));
        var session = new EvidenceGroupingSession(file, "route fixture");
        var input = session.Propose("\"Wide phase\"");
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, null);
        bool downloaded = false;
        var window = new SuperClusterPreviewWindow(preview, input, session.Propose, initialScope: "\"Wide phase\"", evidenceMode: true,
            fetchMissingPanel: (_, _) => { downloaded = true; return Task.CompletedTask; },
            panelCoverage: session.PanelCoverageStatus, panelCovered: session.PanelScopeCovered)
            { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.ShowAllOptions = true;
            window.Show(); PumpDispatcher();
            var model = (GroupingReviewViewModel)window.DataContext;
            Check("Z15a6 Fetch missing panel is disabled after the full route", !model.CanFetchMissingPanel
                && !((Button)window.FindName("FetchMissingPanelButton")).IsEnabled && model.PanelCoverageText == "Full reference panel stored.");
            model.ScopeText = "\"Future phase\""; PumpDispatcher();
            FinishReviewWork(window.FetchMissingPanelAsync);
            Check("Z15a6 full coverage also disables fetching for a later scope", !downloaded && !model.CanFetchMissingPanel
                && !((Button)window.FindName("FetchMissingPanelButton")).IsEnabled);
        }
        finally { window.Close(); PumpDispatcher(); }

        var (coldSource, coldCatalogue) = ScopedPanelDatabase(true);
        using var coldFullHandler = new ScopedPanelHandler(coldCatalogue);
        var coldFull = ReferencePanelFetcher.FetchAsync(ReferencePanelFetcher.FamiliesFound(coldSource), coldFullHandler, options: options).GetAwaiter().GetResult();
        using var coldBroadHandler = new ScopedPanelHandler(coldCatalogue);
        var coldMessages = new List<string>();
        var coldBroad = ReferencePanelFetcher.FetchForScopeAsync(coldSource, new(new[] { "Unrelated phase" }), coldBroadHandler,
            progress: new InlineFetchProgress(value => coldMessages.Add(value.Message)), options: options).GetAwaiter().GetResult();
        Check("Z15a6 cold reference-only broad scope never moves more bytes than full", coldBroadHandler.Bytes <= coldFullHandler.Bytes,
            "broad=" + coldBroadHandler.Bytes + ", full=" + coldFullHandler.Bytes);
        Check("Z15a6 cold reference-only broad scope stores the full panel", coldBroad.Coverage == null
            && JsonSerializer.Serialize(coldBroad) == JsonSerializer.Serialize(coldFull));
        Check("Z15a6 cold reference-only scope takes exactly the full requests", coldBroadHandler.Queries.SequenceEqual(coldFullHandler.Queries));
        Check("Z15a6 unknown closure logs the reason for the full route", coldMessages.First() == "Full panel: no local family matches this scope");
        Console.WriteLine($"Z15a6 cold_full_bytes={coldFullHandler.Bytes} cold_broad_bytes={coldBroadHandler.Bytes}");
    }
}
