using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        /// <summary>UniProt's search, answering from a list of pages, each with its own headers.</summary>
        private sealed class CannedPanelUniProt : HttpMessageHandler
        {
            private readonly Queue<(HttpStatusCode Status, string Body, string Release, string? Next)> pages;
            public List<string> Asked { get; } = new List<string>();

            /// <summary>The x-total-results the first page of a family announces, when set.</summary>
            public Queue<int?> Totals { get; } = new Queue<int?>();

            public CannedPanelUniProt(params (HttpStatusCode, string, string, string?)[] pages) =>
                this.pages = new Queue<(HttpStatusCode, string, string, string?)>(pages);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Asked.Add(Uri.UnescapeDataString(request.RequestUri!.ToString()));
                (HttpStatusCode status, string body, string release, string? next) = pages.Dequeue();
                var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
                if (release.Length > 0) response.Headers.Add("x-uniprot-release", release);
                if (next != null) response.Headers.Add("Link", "<" + next + ">; rel=\"next\"");
                if (Totals.Count > 0 && Totals.Dequeue() is int total)
                    response.Headers.Add("x-total-results", total.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return Task.FromResult(response);
            }
        }

        private static string PanelEntryJson(string accession, string similarity, string sequence, bool reviewed = true,
            string keyword = "Chosen") =>
            "{\"primaryAccession\":\"" + accession + "\",\"entryType\":\"UniProtKB " +
            (reviewed ? "reviewed (Swiss-Prot)" : "unreviewed (TrEMBL)") + "\"," +
            (keyword.Length > 0 ? "\"keywords\":[{\"name\":\"" + keyword + "\"}]," : "") +
            "\"comments\":[{\"commentType\":\"SIMILARITY\",\"texts\":[{\"value\":\"" + similarity + "\"}]}," +
            "{\"commentType\":\"TISSUE SPECIFICITY\",\"texts\":[{\"value\":\"A long paragraph of prose.\"}]}]," +
            "\"uniProtKBCrossReferences\":[{\"database\":\"Pfam\",\"id\":\"PF09999\",\"properties\":[{\"key\":\"EntryName\",\"value\":\"Testdomain\"}]}]," +
            "\"sequence\":{\"value\":\"" + sequence + "\"}}";

        private static string PanelPage(params string[] entries) => "{\"results\":[" + string.Join(",", entries) + "]}";

        private static void ReferencePanelTests(string workDir)
        {
            string alphaBase = NearestBase(190, 71);
            string gammaBase = NearestBase(170, 83);
            var file = new FamilyClusterFile();
            file.MyExtendedFastaItems.AddRange(new[]
            {
                NearestItem("tr|A0A101|A0A101_TEST", NearestMutate(alphaBase, 8), false, "Alpha family"),
                NearestItem("tr|A0A102|A0A102_TEST", gammaBase, false, "gamma family"),
                NearestItem("tr|A0A103|A0A103_TEST", NearestBase(150, 97), false, "Outside family"),
                NearestItem("sp|P20001|IN_FILE_TEST", NearestMutate(gammaBase, 9), true, "Gamma family"),
                NearestItem("Reverse_tr|A0A104|A0A104_TEST", NearestBase(150, 101), false, "Decoy family"),
            });
            file.MyExtendedFastaItems[0].Evidence!.Keywords = new List<string> { "Chosen" };
            file.MyExtendedFastaItems[1].Evidence!.Keywords = new List<string> { "Chosen" };
            file.MyExtendedFastaItems[3].Evidence!.Keywords = new List<string> { "Chosen" };
            Func<ProteinEvidence, bool> scope = e => e.Keywords.Contains("Chosen");

            List<string> families = ReferencePanelFetcher.FamiliesFound(file, scope);
            Check("the families asked are those the scoped targets name, case folded and sorted, decoys never",
                families.SequenceEqual(new[] { "Alpha family", "gamma family" }), string.Join(" | ", families));
            Check("without a scope every target's family is asked",
                ReferencePanelFetcher.FamiliesFound(file).Count == 3);

            // A real next link carries the field list, commas included.
            const string next = "https://rest.uniprot.org/uniprotkb/search?format=json&fields=accession,reviewed,sequence&cursor=page2&size=500";
            var handler = new CannedPanelUniProt(
                (HttpStatusCode.OK, PanelPage(
                    PanelEntryJson("P20003", "Belongs to the alpha family.", alphaBase),
                    PanelEntryJson("P20009", "Belongs to the alphabet soup family.", NearestBase(120, 7))), "2026_03", next),
                (HttpStatusCode.OK, PanelPage(
                    PanelEntryJson("P20002", "Belongs to the alpha family. Beta subfamily.", NearestMutate(alphaBase, 4, 1, 1))), "2026_03", null),
                (HttpStatusCode.OK, PanelPage(
                    PanelEntryJson("P20001", "Belongs to the gamma family.", NearestMutate(gammaBase, 9)),
                    PanelEntryJson("P20003", "Belongs to the alpha family.", alphaBase),
                    PanelEntryJson("P20004", "In the N-terminal section; belongs to the gamma family. In the C-terminal section; belongs to the delta family.", NearestMutate(gammaBase, 5))), "2026_03", null));
            foreach (int? total in new int?[] { 3, null, 3 }) handler.Totals.Enqueue(total);
            ReferencePanel panel = ReferencePanelFetcher.FetchAndStoreAsync(file, scope, handler).GetAwaiter().GetResult();
            Check("each family is asked as a reviewed search, and the next page is followed",
                handler.Asked.Count == 3 && handler.Asked[0].Contains("(reviewed:true) AND (family:\"Alpha family\")")
                && handler.Asked[1] == next && handler.Asked[2].Contains("family:\"gamma family\""),
                string.Join(" ; ", handler.Asked));
            Check("an entry the text search matched but that is not a member is dropped, a repeat is kept once",
                panel.Entries.Select(e => e.Accession).SequenceEqual(new[] { "P20001", "P20002", "P20003", "P20004" }),
                string.Join(",", panel.Entries.Select(e => e.Accession)));
            Check("the panel records its release, its families and when it was fetched",
                panel.Release == "2026_03" && panel.Families.SequenceEqual(families) && panel.RetrievedAtUtc != null
                && file.EvidenceSource?.ReferencePanel == panel);
            PanelEntry two = panel.Entries.Single(e => e.Accession == "P20004");
            Check("panel families are parsed as the file's are: two memberships leave no single family",
                two.Evidence.Family == "" && two.Evidence.FamilyMemberships?.Count == 2
                && panel.Entries.Single(e => e.Accession == "P20002").Evidence.Subfamily == "Beta subfamily");
            Check("a panel entry keeps its domains and its tissue, and drops the family text",
                two.Evidence.Pfam.Single().Name == "Testdomain"
                && two.Evidence.TissueSpecificity.SequenceEqual(new[] { "A long paragraph of prose." })
                && two.Evidence.SimilarityAnnotations == null && two.Sequence.Length == 170);

            List<NeighbourReference> references = NearestReviewed.References(file, scope);
            Check("the file's own reviewed protein answers for itself; the panel adds only what it lacks",
                references.Select(r => r.Accession).SequenceEqual(new[] { "P20001", "P20002", "P20003", "P20004" })
                && references[0].Identifier == "sp|P20001|IN_FILE_TEST" && references.Skip(1).All(r => r.Identifier == ""),
                string.Join(",", references.Select(r => r.Accession + ":" + r.Identifier)));

            PanelRefusalTests(scope);

            NearestReviewed.ForgetMemo();
            NeighbourSearch search;
            try { search = NearestReviewed.SearchAndStore(file, scope); }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  reference panel search - USEARCH could not run here (" +
                    ex.GetBaseException().Message.Split('\n')[0] + ")");
                skipped++;
                return;
            }
            NearestNeighbour? alpha = search.ByQuery().GetValueOrDefault("tr|A0A101|A0A101_TEST");
            Check("a protein with no reviewed relative in the file finds one in the panel",
                alpha?.Accession == "P20003" && alpha.Reference == "" && alpha.Family == "alpha family",
                alpha == null ? "none" : alpha.Accession + " " + alpha.Family);
            Check("and the panel answers for that neighbour's evidence",
                NearestReviewed.ReferenceEvidence(file)(alpha!)?.Pfam.Single().Name == "Testdomain");

            // A header without its accession: identical to P20003, so it takes P20003 unless told it is P20003.
            var stripped = new List<FastaItemExtended> { NearestItem("custom_entry_1", alphaBase, null) };
            List<NeighbourReference> panelOnly = ReferencePanelFetcher.References(panel);
            NearestNeighbour? found = NearestReviewed.Search(stripped, panelOnly).Neighbours.SingleOrDefault();
            Check("an entry with no accession still gets a neighbour from the panel (itself, unannounced)",
                found?.Accession == "P20003" && found.IdentityPercent == 100.0, found?.Accession);
            var own = new Dictionary<string, string> { ["custom_entry_1"] = "P20003" };
            NearestNeighbour? leftOut = NearestReviewed.Search(stripped, panelOnly, ownAccessions: own).Neighbours.SingleOrDefault();
            Check("a leave-one-out that names the entry never lets it take itself",
                leftOut?.Accession == "P20002", leftOut?.Accession);

            string path = System.IO.Path.Combine(workDir, "reference-panel.fcf");
            file.Save(path);
            FamilyClusterFile reopened = FamilyClusterFile.Load(path);
            NearestReviewed.ForgetMemo();
            long runs = NearestReviewed.SearchRunCount;
            NeighbourSearch offline = NearestReviewed.Search(NearestReviewed.Targets(reopened), NearestReviewed.References(reopened, scope));
            Check("the panel survives save and reopen, and the offline rerun is byte-identical",
                reopened.ToJsonText() == file.ToJsonText() && NearestReviewed.SearchRunCount == runs + 1
                && JsonSerializer.Serialize(offline) == JsonSerializer.Serialize(search));
            Check("the stored search is current for the reopened file's references, panel included",
                NearestReviewed.IsCurrent(reopened.EvidenceSource?.NearestReviewed,
                    NearestReviewed.Targets(reopened), NearestReviewed.References(reopened, scope)));
        }

        private static CannedPanelUniProt Announcing(CannedPanelUniProt handler, int total)
        {
            handler.Totals.Enqueue(total);
            return handler;
        }

        private static void PanelRefusalTests(Func<ProteinEvidence, bool> scope)
        {
            string body = PanelPage(PanelEntryJson("P30001", "Belongs to the alpha family.", "MKTAYIAKQRQISFVKSHFSRQ"));
            (string Name, CannedPanelUniProt Handler)[] cases =
            {
                ("a release that changes between pages", new CannedPanelUniProt(
                    (HttpStatusCode.OK, body, "2026_03", "https://rest.uniprot.org/next"), (HttpStatusCode.OK, body, "2026_04", null))),
                ("a page without a release", new CannedPanelUniProt((HttpStatusCode.OK, body, "", null))),
                ("a refused page", new CannedPanelUniProt((HttpStatusCode.BadRequest, "{}", "2026_03", null))),
                ("fewer entries than UniProt announced (a page left unread)", Announcing(new CannedPanelUniProt(
                    (HttpStatusCode.OK, body, "2026_03", null)), 2)),
                ("an unreviewed entry in a reviewed search", new CannedPanelUniProt((HttpStatusCode.OK,
                    PanelPage(PanelEntryJson("P30002", "Belongs to the alpha family.", "MKTAYIAK", reviewed: false)), "2026_03", null))),
            };
            foreach ((string name, CannedPanelUniProt handler) in cases)
            {
                var file = new FamilyClusterFile();
                file.MyExtendedFastaItems.Add(NearestItem("tr|A0A201|A0A201_TEST", "MKTAYIAKQRQISFVKSHFSRQ", false, "alpha family"));
                file.MyExtendedFastaItems[0].Evidence!.Keywords = new List<string> { "Chosen" };
                string before = file.ToJsonText();
                bool threw = false;
                try { ReferencePanelFetcher.FetchAndStoreAsync(file, scope, handler).GetAwaiter().GetResult(); }
                catch (Exception) { threw = true; }
                Check("no panel is made from " + name + ", and the file is unchanged",
                    threw && file.ToJsonText() == before);
            }
        }
    }
}
