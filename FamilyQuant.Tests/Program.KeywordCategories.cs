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
    }
}
