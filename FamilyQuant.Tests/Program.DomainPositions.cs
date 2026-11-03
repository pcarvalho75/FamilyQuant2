using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string PositionUniProtReply = """
        {"results":[{"primaryAccession":"P00001","entryType":"UniProtKB reviewed (Swiss-Prot)",
        "sequence":{"value":"ACDEFGHIKL"},"features":[
          {"type":"Signal","location":{"start":{"value":1,"modifier":"EXACT"},"end":{"value":2,"modifier":"EXACT"}}},
          {"type":"Chain","description":"Source product","featureId":"PRO_fixture","location":{"start":{"value":3,"modifier":"EXACT"},"end":{"value":10,"modifier":"EXACT"}},"evidences":[{"evidenceCode":"ECO:0000269","source":"PubMed","id":"123"}]},
          {"type":"Peptide","location":{"start":{"modifier":"UNKNOWN"},"end":{"value":8,"modifier":"OUTSIDE"}}},
          {"type":"Propeptide","location":{"start":{"value":1,"modifier":"OUTSIDE"},"end":{"value":3,"modifier":"EXACT"}}},
          {"type":"Transmembrane"}],"uniProtKBCrossReferences":[{"database":"Pfam","id":"PF12345"}]}]}
        """;
    private const string PositionInterProReply = """
        {"count":1,"next":null,"results":[{"metadata":{"accession":"PF12345","name":"Supplied domain","source_database":"pfam"},
        "proteins":[{"accession":"p00001","protein_length":10,"entry_protein_locations":[
        {"model":"PF12345","fragments":[{"start":2,"end":4,"dc-status":"N_TERMINAL_DISC"},{"start":6,"end":7,"dc-status":"C_TERMINAL_DISC"}]},
        {"model":"PF12345","fragments":[{"start":8,"end":10,"dc-status":"CONTINUOUS"}]}]}]}]}
        """;

    private sealed class PositionHandler : HttpMessageHandler
    {
        public bool FailInterPro, CancelInterPro;
        public string? Next;
        public int PositionCalls;
        public string InterProReply = PositionInterProReply;
        public string UniProtReply = PositionUniProtReply;
        public string MetadataReply = """{"metadata":{"accession":"P00001","sequence":"ACDEFGHIKL"}}""";
        public Func<bool, int, string>? PositionRelease { get; set; }
        public Action? DuringPosition { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            bool interpro = request.RequestUri!.Host == "www.ebi.ac.uk";
            bool metadata = interpro && request.RequestUri.AbsolutePath.StartsWith("/interpro/api/protein/", StringComparison.Ordinal);
            if (interpro)
            {
                if (!metadata) PositionCalls++;
                DuringPosition?.Invoke();
                if (CancelInterPro) throw new OperationCanceledException(token);
            }
            var response = new HttpResponseMessage(interpro && FailInterPro ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(metadata ? MetadataReply : interpro ? InterProReply.Replace("\"next\":null", "\"next\":" + JsonSerializer.Serialize(PositionCalls == 1 ? Next : null), StringComparison.Ordinal)
                    .Replace("\"count\":1", "\"count\":" + (Next == null ? "1" : "2"), StringComparison.Ordinal) : UniProtReply),
            };
            response.Headers.Add(interpro ? "InterPro-Version" : "x-uniprot-release", interpro ? PositionRelease?.Invoke(metadata, PositionCalls) ?? "101.0" : "2026_03");
            return Task.FromResult(response);
        }
    }

    private static void DomainPositionTests(string workDir)
    {
        Directory.CreateDirectory(workDir);
        var parsed = EvidenceFetcher.Parse(PositionUniProtReply)["P00001"].Positions;
        Check("J4a: UniProt retains all four processing feature types and sequence identity", parsed is { SequenceLength: 10 }
            && parsed.Features.Count == 4 && parsed.SequenceSha256 == ProteinPositionFetcher.SequenceHash("ACDEFGHIKL"));
        var peptide = parsed?.Features.Single(f => f.Type == "Peptide");
        Check("J4a: unknown and uncertain boundaries are never made exact", peptide is { Start: null, End: 8, EndModifier: "OUTSIDE", StartModifier: "UNKNOWN" });
        Check("J4a: feature names, IDs and experimental references survive parsing", parsed?.Features.Single(f => f.Type == "Chain") is
            { Description: "Source product", FeatureId: "PRO_fixture" } product && product.Evidences.Single().Id == "123");
        var file = PositionFile();
        using var handler = new PositionHandler();
        EvidenceFetcher.FetchAsync(file, handler, positionAccessions: new[] { "P00001" }).GetAwaiter().GetResult();
        var positions = file.MyExtendedFastaItems[0].Evidence?.Positions;
        Check("J4a: repeated and discontinuous Pfam hits retain every fragment", positions?.Pfam.Count == 2
            && positions.Pfam[0] is { Start: 2, End: 7 } domain && domain.Fragments.Count == 2 && domain.Fragments[1].Start == 6
            && positions.Pfam[1].Start == 8);
        Check("J4a: positions carry both releases, UTC timestamps and reply hashes", positions is { DomainSource: { Release: "101.0" }, FeatureSource: { Release: "2026_03" } }
            && positions.DomainSource.Responses.Count == 2 && positions.DomainSource.Responses.All(r => r.Sha256.Length == 64) && positions.FeatureSource.Responses.Single().Sha256.Length == 64);
        Check("J4a: source sequence identity refuses equal-length substitutions", positions != null
            && ProteinPositionFetcher.MatchesSequence(positions, "acdefghikl") && !ProteinPositionFetcher.MatchesSequence(positions, "ACDEFGHIKA"));
        string path = Path.Combine(workDir, "positions.fqproj");
        ProjectStore.WriteFqproj(path, file, Array.Empty<QuantRow>(), new QuantManifest());
        var reopened = ProjectStore.ReadFqproj(path).fcf;
        Check("J4a: project reopens offline with byte-equal position evidence", JsonSerializer.Serialize(positions)
            == JsonSerializer.Serialize(reopened.MyExtendedFastaItems[0].Evidence?.Positions));
        Check("J4a: legacy evidence remains readable with unknown positions", EvidenceFetcher.Parse("""{"results":[{"primaryAccession":"P00001"}]}""")["P00001"].Positions == null);
        foreach (bool cancellation in new[] { false, true })
        {
            var unchanged = PositionFile(); string before = unchanged.ToJsonText();
            using var failing = new PositionHandler { FailInterPro = !cancellation, CancelInterPro = cancellation };
            bool refused = cancellation ? Throws<OperationCanceledException>(() => EvidenceFetcher.FetchAsync(unchanged, failing, positionAccessions: new[] { "P00001" }).GetAwaiter().GetResult())
                : Throws<HttpRequestException>(() => EvidenceFetcher.FetchAsync(unchanged, failing, positionAccessions: new[] { "P00001" }).GetAwaiter().GetResult());
            Check("J4a: " + (cancellation ? "cancellation" : "InterPro failure") + " publishes no partial evidence", refused && before == unchanged.ToJsonText());
        }
        using var paged = new PositionHandler { Next = "https://www.ebi.ac.uk/interpro/api/entry/pfam/protein/uniprot/P00001/?page=2" };
        var pages = PositionFile(); EvidenceFetcher.FetchAsync(pages, paged, positionAccessions: new[] { "P00001" }).GetAwaiter().GetResult();
        Check("J4a: InterPro paging is complete and keeps every response hash", paged.PositionCalls == 2
            && pages.MyExtendedFastaItems[0].Evidence?.Positions?.DomainSource?.Responses.Count == 3);
        using var invalidPage = new PositionHandler { Next = "https://other.example/positions" };
        Check("J4a: an off-provider next link is refused", Throws<InvalidDataException>(() => EvidenceFetcher.FetchAsync(PositionFile(), invalidPage, positionAccessions: new[] { "P00001" }).GetAwaiter().GetResult()));
        using var invalid = new PositionHandler { InterProReply = PositionInterProReply.Replace("\"end\":10", "\"end\":11", StringComparison.Ordinal) };
        Check("J4a: out-of-range coordinates are refused", Throws<InvalidDataException>(() => EvidenceFetcher.FetchAsync(PositionFile(), invalid, positionAccessions: new[] { "P00001" }).GetAwaiter().GetResult()));
        using var changedSequence = new PositionHandler { MetadataReply = """{"metadata":{"accession":"P00001","sequence":"ACDEFGHIKA"}}""" };
        Check("J4a: equal-length source sequence changes are refused", Throws<InvalidDataException>(() => EvidenceFetcher.FetchAsync(PositionFile(), changedSequence, positionAccessions: new[] { "P00001" }).GetAwaiter().GetResult()));
        using var unscoped = new PositionHandler(); var withoutScope = PositionFile();
        var boundedResult = EvidenceFetcher.FetchAsync(withoutScope, unscoped).GetAwaiter().GetResult();
        Check("J4a: the broad evidence fetch never sweeps InterPro without a position scope", unscoped.PositionCalls == 0
            && boundedResult.PositionAsked == 0 && withoutScope.MyExtendedFastaItems[0].Evidence?.Positions?.DomainSource == null);
        using var refresh = new PositionHandler();
        EvidenceFetcher.FetchAsync(file, refresh).GetAwaiter().GetResult();
        Check("J4a: a metadata refresh preserves valid older coordinates outside the requested scope", refresh.PositionCalls == 0
            && JsonSerializer.Serialize(positions?.Pfam) == JsonSerializer.Serialize(file.MyExtendedFastaItems[0].Evidence?.Positions?.Pfam));
        var scoped = PositionFile(); scoped.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "sp|P00002|OTHER", Sequence = "WWWWWWWWWW", UserID = 2 });
        var mapped = FamilyEnvelopeQuant.SearchCuration(new[] { new QuantRow { Side = DeNovo.NativeQuantFractions.SideIdentified, Sequence = "ACDEFGHIKL" } }, scoped);
        Check("J4a: scope follows production mapped proteins and explicit extra family selection",
            PositionFetchScope.FromMappedPeptides(mapped, scoped).SequenceEqual(new[] { "P00001" })
            && PositionFetchScope.FromMappedPeptides(mapped, scoped, new[] { 2 }).SequenceEqual(new[] { "P00001", "P00002" }));
        Check("J4a: the fetch summary includes position count and measured time", CurationViewModel.DescribeEvidenceFetch(new(
            EvidenceFetcher.FetchAsync(PositionFile(), new PositionHandler(), positionAccessions: new[] { "P00001" }).GetAwaiter().GetResult(), null, null))
            .Contains("1 of 1 requested proteins in", StringComparison.Ordinal));
        using var changedRelease = new PositionHandler { PositionRelease = (metadata, _) => metadata ? "101.0" : "102.0" };
        Check("J4a: an InterPro release change is refused", Throws<InvalidDataException>(() => EvidenceFetcher.FetchAsync(
            PositionFile(), changedRelease, positionAccessions: new[] { "P00001" }).GetAwaiter().GetResult()));
        var concurrentEdit = PositionFile();
        using var editHandler = new PositionHandler { DuringPosition = () => concurrentEdit.MyExtendedFastaItems[0].Sequence = "ACDEFGHIKA" };
        Check("J4a: a local sequence edit during the remote fetch refuses the stale commit", Throws<InvalidDataException>(() => EvidenceFetcher.FetchAsync(
            concurrentEdit, editHandler, positionAccessions: new[] { "P00001" }).GetAwaiter().GetResult()) && concurrentEdit.MyExtendedFastaItems[0].Evidence == null);
        EnsureApplication();
        var nativeFile = new FamilyClusterFile { MyExtendedFastaItems = new()
        { new() { SequenceIdentifier = "tr|A0A710|FIXTURE", Sequence = "PEPTIDEK", UserID = 71, USearchID = 71 } } };
        var control = new ClusterGenerationControl(nativeFile);
        using var nativeHandler = new EvidenceAndPanelHandler();
        control.FetchDomainsHandlerOverride = nativeHandler;
        ((System.Windows.Controls.CheckBox)control.FindName("CheckBoxIncludeFamilyPositions")).IsChecked = true;
        ((System.Windows.Controls.DataGrid)control.FindName("DataGridResultGrid")).SelectedItem = nativeFile.MyExtendedFastaItems[0];
        FinishBusyTask(control.FetchProteinEvidenceAsync());
        Check("J4a: the native explicit family choice fetches and stores its coordinates", nativeFile.MyExtendedFastaItems[0].Evidence?.Positions?.DomainSource != null);
    }

    private static FamilyClusterFile PositionFile() => new() { MyExtendedFastaItems = new()
    {
        new() { SequenceIdentifier = "sp|P00001|FIXTURE", Sequence = "ACDEFGHIKL", UserID = 1, USearchID = 1, Domains = "old" },
    }};
}
