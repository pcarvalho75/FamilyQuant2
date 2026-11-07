using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class OrganismEvidenceHandler : HttpMessageHandler
    {
        internal List<Uri> Requests { get; } = new();
        internal static string Entry => PanelEntryJson("P00001", "Belongs to the fixture family.", "ACDEFGHIK")[..^1]
            + ",\"organism\":{\"scientificName\":\"Fixture organism\",\"taxonId\":21}}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!);
            bool count = Uri.UnescapeDataString(request.RequestUri!.Query).Contains("size=0", StringComparison.Ordinal);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(count ? "{\"results\":[]}" : PanelPage(Entry)) };
            response.Headers.Add("x-uniprot-release", "fixture"); response.Headers.Add("x-total-results", "1");
            return Task.FromResult(response);
        }
    }

    private static void OrganismEvidenceTests()
    {
        var parsed = EvidenceFetcher.Parse(PanelPage(OrganismEvidenceHandler.Entry))["P00001"];
        Check("Z12g5 protein evidence reads optional organism name and taxonomy identifier",
            parsed.OrganismName == "Fixture organism" && parsed.TaxonId == 21);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new() { TaskProtein("sp|P00001|FIXTURE", 21, true) } };
        using var proteins = new OrganismEvidenceHandler();
        EvidenceFetcher.FetchAsync(file, proteins).GetAwaiter().GetResult();
        Check("Z12g5 evidence request asks for both organism fields", proteins.Requests.All(uri =>
            Uri.UnescapeDataString(uri.Query).Contains("organism_name,organism_id", StringComparison.Ordinal))
            && file.MyExtendedFastaItems[0].Evidence is { OrganismName: "Fixture organism", TaxonId: 21 });
        using var references = new OrganismEvidenceHandler();
        var panel = ReferencePanelFetcher.FetchAsync(new[] { "fixture family" }, references).GetAwaiter().GetResult();
        Check("Z12g5 streamed panel requests and retains the same organism metadata", panel.IsComplete
            && panel.Entries.Single() is { OrganismName: "Fixture organism", TaxonId: 21 }
            && references.Requests.Where(uri => uri.AbsolutePath.EndsWith("/stream", StringComparison.Ordinal)).All(uri =>
                Uri.UnescapeDataString(uri.Query).Contains("organism_name,organism_id", StringComparison.Ordinal)));
        using var paged = new OrganismEvidenceHandler();
        var comparison = ReferencePanelFetcher.FetchAsync(new[] { "fixture family" }, paged, usePagedComparison: true).GetAwaiter().GetResult();
        Check("Z12g5 paged comparison route retains organism metadata", comparison.Entries.Single() is { OrganismName: "Fixture organism", TaxonId: 21 });
        file.EvidenceSource!.ReferencePanel = panel;
        var reopened = FamilyClusterFile.FromJsonText(file.ToJsonText());
        Check("Z12g5 project roundtrip retains protein and panel organism fields", reopened.MyExtendedFastaItems[0].Evidence!.OrganismName == "Fixture organism"
            && reopened.EvidenceSource!.ReferencePanel!.Entries.Single() is { OrganismName: "Fixture organism", TaxonId: 21 });
        var legacyEvidence = EvidenceFetcher.Parse(PanelPage(PanelEntryJson("P00002", "Belongs to the fixture family.", "ACDEFGHIK")))["P00002"];
        var legacyPanel = new PanelEntry { Accession = "P00002", Evidence = legacyEvidence };
        string legacy = JsonSerializer.Serialize(legacyPanel);
        Check("Z12g5 legacy metadata stays absent in serialized protein and panel records", legacyEvidence.OrganismName == null && legacyEvidence.TaxonId == null
            && !legacy.Contains("OrganismName", StringComparison.Ordinal) && !legacy.Contains("TaxonId", StringComparison.Ordinal)
            && JsonSerializer.Serialize(JsonSerializer.Deserialize<PanelEntry>(legacy)) == legacy);
        var missing = EvidenceFetcher.Parse(PanelPage(OrganismEvidenceHandler.Entry.Replace("\"scientificName\":\"Fixture organism\",\"taxonId\":21",
            "\"scientificName\":\" \",\"taxonId\":0", StringComparison.Ordinal)))["P00001"];
        Check("Z12g5 missing or invalid optional organism values stay unavailable", missing.OrganismName == null && missing.TaxonId == null);
    }
}
