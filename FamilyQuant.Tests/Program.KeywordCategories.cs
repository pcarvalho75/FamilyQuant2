using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void KeywordCategoryTests()
    {
        const string reply = """
            {"results":[{"primaryAccession":"P00001","entryType":"UniProtKB reviewed (Swiss-Prot)","keywords":[
            {"id":"KW-0001","name":"Fixture activity","category":"Molecular function"},
            {"id":"KW-0002","name":"Fixture annotation","category":"Technical term"},
            {"id":"KW-0003","name":"Stored category only","category":"Molecular function"},
            {"id":"KW-0004","name":"Legacy missing category"}]}]}
            """;
        var evidence = EvidenceFetcher.Parse(reply)["P00001"];
        Check("Z12c1 evidence parsing retains UniProt keyword categories for the next saved project",
            evidence.KeywordCategories?.Count == 3 && evidence.KeywordCategories["Fixture activity"] == "Molecular function"
            && evidence.KeywordCategories["Fixture annotation"] == "Technical term"
            && evidence.Keywords.Contains("Legacy missing category", StringComparer.Ordinal));
        var file = new FamilyClusterFile { MyExtendedFastaItems = new() { TaskProtein("sp|P00001|FIXTURE", 21, true) } };
        file.MyExtendedFastaItems[0].Evidence = evidence;
        string before = file.ToJsonText();
        var saved = FamilyClusterFile.FromJsonText(before);
        Check("Z12c1 project roundtrip keeps categories", saved.MyExtendedFastaItems[0].Evidence!.KeywordCategories!["Fixture activity"] == "Molecular function");
        using var handler = new TaskTaxonomyHandler { VocabularyJson = """
            {"results":[
            {"keyword":{"name":"Fixture activity","id":"KW-0001"},"category":{"name":"Molecular function","id":"KW-9992"}},
            {"keyword":{"name":"Fixture annotation","id":"KW-0002"},"category":{"name":"Technical term","id":"KW-9990"}}]}
            """ };
        var task = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
        var session = new EvidenceGroupingSession(file, "fixture", discoverTask: (_, _) => Task.FromResult(task));
        var input = session.DiscoverTaskAsync().GetAwaiter().GetResult();
        Check("Z12c1 table projection retains vocabulary category names and identifiers",
            input.Keywords.Single(k => k.Keyword == "Fixture activity") is { CategoryName: "Molecular function", CategoryId: "KW-9992" }
            && input.Keywords.Single(k => k.Keyword == "Fixture annotation").CategoryDisplay == "Technical term");
        Check("Z12c1 stored categories fill vocabulary gaps without changing the category-ID suggestion rule",
            input.Keywords.Single(k => k.Keyword == "Stored category only").CategoryDisplay == "Molecular function"
            && task.ProposedScopeTerms.SequenceEqual(new[] { "Fixture activity" }) && file.ToJsonText() == before);
        Check("Z12c1 older missing categories are stated explicitly",
            input.Keywords.Single(k => k.Keyword == "Legacy missing category").CategoryDisplay == "Unavailable (fetch evidence)"
            && new ReviewTaskKeyword("Old term", 1).CategoryDisplay == "Unavailable (fetch evidence)");
        var legacy = new ProteinEvidence { Keywords = new() { "Old term" } };
        string oldJson = JsonSerializer.Serialize(legacy);
        Check("Z12c1 optional category metadata preserves legacy evidence JSON", !oldJson.Contains("KeywordCategories", StringComparison.Ordinal)
            && JsonSerializer.Serialize(JsonSerializer.Deserialize<ProteinEvidence>(oldJson)) == oldJson);
        bool refused = false;
        try { EvidenceFetcher.Parse(reply.Replace("{\"id\":\"KW-0004\",\"name\":\"Legacy missing category\"}",
            "{\"id\":\"KW-0004\",\"name\":\"Fixture activity\",\"category\":\"Technical term\"}", StringComparison.Ordinal)); }
        catch (JsonException) { refused = true; }
        Check("Z12c1 conflicting provider categories cannot silently choose an arbitrary value", refused);
        VocabularyRouteTests();
    }

    // 2.1.13: UniProt's keyword and taxonomy streams answered 502 on 2026-11-09 while its search pages worked.
    private static void VocabularyRouteTests()
    {
        static FamilyClusterFile File(params string[] keywords) => new()
        {
            EvidenceSource = new EvidenceProvenance { Provider = "UniProt", Release = "fixture" },
            MyExtendedFastaItems = new() { TaskProtein("sp|P00001|ONE", 21, true, keywords) },
        };
        var stored = File("Fixture activity", "Fixture annotation");
        stored.MyExtendedFastaItems[0].Evidence!.KeywordCategories = new() { ["Fixture activity"] = "Molecular function", ["Fixture annotation"] = "Technical term" };
        using (var inner = new TaskTaxonomyHandler())
        {
            var proposal = EvidenceTaskDiscovery.DiscoverAsync(stored, inner).GetAwaiter().GetResult();
            var taxonomy = inner.Urls.Where(u => new Uri(u).AbsolutePath.StartsWith("/taxonomy/", StringComparison.Ordinal)).ToList();
            Check("2.1.13 stored keyword categories need no vocabulary request",
                !inner.Urls.Any(u => new Uri(u).AbsolutePath.StartsWith("/keywords/", StringComparison.Ordinal))
                && proposal.ProposedScopeTerms.SequenceEqual(new[] { "Fixture activity" })
                && proposal.Keywords.Single(k => k.Keyword == "Fixture activity").CategoryId == EvidenceTaskDiscovery.FunctionalScopeCategoryId
                && proposal.Keywords.Single(k => k.Keyword == "Fixture annotation").CategoryId == "KW-9990",
                "urls=" + string.Join(" ", inner.Urls));
            Check("2.1.13 taxonomy is read from UniProt's search pages, one page per batch", taxonomy.Count > 0
                && taxonomy.All(u => u.StartsWith("https://rest.uniprot.org/taxonomy/search?format=json&size=500&query=", StringComparison.Ordinal)),
                "taxonomy=" + string.Join(" ", taxonomy));
        }
        var legacy = File("Fixture annotation", "Legacy missing category");
        using (var inner = new TaskTaxonomyHandler())
        using (var paged = new PagedVocabularyHandler(inner))
        {
            var proposal = EvidenceTaskDiscovery.DiscoverAsync(legacy, paged).GetAwaiter().GetResult();
            Check("2.1.13 keywords without a stored category are read page by page until UniProt gives no next page",
                paged.Pages.Count == 2 && paged.Pages[0].Contains("/keywords/search?", StringComparison.Ordinal)
                && paged.Pages[0].Contains("size=500", StringComparison.Ordinal) && paged.Pages[1].Contains("cursor=page2", StringComparison.Ordinal)
                && proposal.Keywords.Single(k => k.Keyword == "Legacy missing category").CategoryId == EvidenceTaskDiscovery.FunctionalScopeCategoryId
                && proposal.ProposedScopeTerms.SequenceEqual(new[] { "Legacy missing category" }),
                "pages=" + string.Join(" ", paged.Pages));
        }
        var mixed = File("Fixture activity", "Legacy missing category");
        mixed.MyExtendedFastaItems.Add(TaskProtein("sp|P00002|TWO", 21, true, "Fixture activity"));
        foreach (var protein in mixed.MyExtendedFastaItems)
            protein.Evidence!.KeywordCategories = new() { ["Fixture activity"] = "Molecular function" };
        using (var inner = new TaskTaxonomyHandler())
        using (var paged = new PagedVocabularyHandler(inner))
        {
            var proposal = EvidenceTaskDiscovery.DiscoverAsync(mixed, paged).GetAwaiter().GetResult();
            Check("2.1.13 partly stored categories read the vocabulary for the gap and keep the stored molecular-function suggestion",
                paged.Pages.Count == 2 && proposal.ProposedScopeTerms.SequenceEqual(new[] { "Fixture activity" })
                && proposal.Keywords.Single(k => k.Keyword == "Fixture activity") is { ReviewedProteins: 2, CategoryId: EvidenceTaskDiscovery.FunctionalScopeCategoryId }
                && proposal.Keywords.Single(k => k.Keyword == "Legacy missing category").CategoryId == EvidenceTaskDiscovery.FunctionalScopeCategoryId,
                "pages=" + paged.Pages.Count + " scope=" + string.Join(",", proposal.ProposedScopeTerms));
        }
        using (var inner = new TaskTaxonomyHandler())
        using (var paged = new PagedVocabularyHandler(inner) { RepeatFirstPage = true })
        {
            bool refusedLoop = false;
            try { EvidenceTaskDiscovery.DiscoverAsync(legacy, paged).GetAwaiter().GetResult(); }
            catch (System.IO.InvalidDataException) { refusedLoop = true; }
            Check("2.1.13 a repeated next-page link is refused instead of looping", refusedLoop && paged.Pages.Count == 2);
        }
        using (var inner = new TaskTaxonomyHandler())
        using (var paged = new PagedVocabularyHandler(inner) { SecondRelease = "other" })
        {
            bool refusedRelease = false;
            try { EvidenceTaskDiscovery.DiscoverAsync(legacy, paged).GetAwaiter().GetResult(); }
            catch (System.IO.InvalidDataException) { refusedRelease = true; }
            Check("2.1.13 a vocabulary page from another UniProt release is refused", refusedRelease && paged.Pages.Count == 2);
        }
    }

    private sealed class PagedVocabularyHandler(System.Net.Http.HttpMessageHandler inner) : System.Net.Http.DelegatingHandler(inner)
    {
        internal readonly List<string> Pages = new();
        internal string SecondRelease = "fixture";
        internal bool RepeatFirstPage;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath != "/keywords/search") return base.SendAsync(request, token);
            Pages.Add(request.RequestUri.AbsoluteUri);
            bool first = !request.RequestUri.Query.Contains("cursor=", StringComparison.Ordinal);
            var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(first
                    ? """{"results":[{"keyword":{"name":"Fixture annotation","id":"KW-0002"},"category":{"name":"Technical term","id":"KW-9990"}}]}"""
                    : """{"results":[{"keyword":{"name":"Legacy missing category","id":"KW-0004"},"category":{"name":"Molecular function","id":"KW-9992"}}]}"""),
            };
            response.Headers.Add("x-uniprot-release", first ? "fixture" : SecondRelease);
            if (first || RepeatFirstPage) response.Headers.TryAddWithoutValidation("Link",
                "<https://rest.uniprot.org/keywords/search?query=*&format=json&size=500&cursor=page2>; rel=\"next\"");
            return Task.FromResult(response);
        }
    }
}
