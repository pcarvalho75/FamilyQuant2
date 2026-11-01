using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void EvidenceStorageTests(string workDir)
        {
            const string evidenceJson = """
                {"Accession":"Q00001","Reviewed":true,"Family":"Family from source","Subfamily":"Subfamily from source",
                 "FamilyAnnotation":"Complete family annotation","Keywords":["first keyword","second keyword"],
                 "TissueSpecificity":["first tissue","second tissue"],
                 "Pfam":[{"Accession":"PF00001","Name":"Domain from source"}],
                 "Panther":[{"Accession":"PTHR00001","Name":"Reference from source"}],
                 "TransmembraneCount":2,"Fragment":false}
                """;
            const string sourceJson = """
                {"Provider":"UniProt","Release":"test-release","RetrievedAtUtc":"2026-01-01T00:00:00+00:00"}
                """;
            var legacy = BuildSyntheticFcf();
            string legacyText = legacy.ToJsonText();
            var root = System.Text.Json.Nodes.JsonNode.Parse(legacyText)!;
            root["EvidenceSource"] = System.Text.Json.Nodes.JsonNode.Parse(sourceJson);
            root["MyExtendedFastaItems"]![0]!["Evidence"] = System.Text.Json.Nodes.JsonNode.Parse(evidenceJson);
            FamilyClusterFile loaded = FamilyClusterFile.FromJsonText(root.ToJsonString());
            var written = System.Text.Json.Nodes.JsonNode.Parse(loaded.ToJsonText())!;
            Check("per-protein evidence survives the file serializer offline",
                System.Text.Json.Nodes.JsonNode.DeepEquals(root["MyExtendedFastaItems"]![0]!["Evidence"],
                    written["MyExtendedFastaItems"]![0]!["Evidence"]));
            Check("the file retains its evidence provider, release and retrieval time",
                System.Text.Json.Nodes.JsonNode.DeepEquals(root["EvidenceSource"], written["EvidenceSource"]));
            string file = Path.Combine(workDir, "evidence-storage.fcf");
            loaded.Save(file);
            Check("evidence survives an actual file save and reopen",
                FamilyClusterFile.Load(file).ToJsonText() == loaded.ToJsonText());
            string project = Path.Combine(workDir, "evidence-storage.fqproj");
            ProjectStore.WriteFqproj(project, loaded, new List<QuantRow>(), new QuantManifest());
            Check("a project container retains exactly the same evidence-bearing curation",
                ProjectStore.ReadFqproj(project).fcf.ToJsonText() == loaded.ToJsonText());
            Check("an old file gains no empty evidence properties or curation changes",
                FamilyClusterFile.FromJsonText(legacyText).ToJsonText() == legacyText
                && !legacyText.Contains("\"Evidence\"") && !legacyText.Contains("\"EvidenceSource\""));
        }

        /// <summary>UniProt, answering from a list of replies, so no test reaches the network.</summary>
        private sealed class CannedUniProt : System.Net.Http.HttpMessageHandler
        {
            private readonly Queue<(System.Net.HttpStatusCode Status, string Body)> replies;
            public List<string> Asked { get; } = new List<string>();

            public CannedUniProt(params (System.Net.HttpStatusCode Status, string Body)[] replies) =>
                this.replies = new Queue<(System.Net.HttpStatusCode, string)>(replies);

            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Asked.Add(request.RequestUri!.ToString());
                (System.Net.HttpStatusCode status, string body) = replies.Dequeue();
                var response = new System.Net.Http.HttpResponseMessage(status) { Content = new System.Net.Http.StringContent(body) };
                response.Headers.Add("x-uniprot-release", "2026_03");
                return Task.FromResult(response);
            }
        }

        private const string UniProtReply = """
            {"results":[
              {"primaryAccession":"P0DM77","uniProtKBCrossReferences":[]},
              {"primaryAccession":"O93517","uniProtKBCrossReferences":[
                {"database":"Pfam","id":"PF00200","properties":[{"key":"EntryName","value":"Disintegrin"},{"key":"MatchStatus","value":"1"}]},
                {"database":"Pfam","id":"PF01421","properties":[{"key":"EntryName","value":"Reprolysin"},{"key":"MatchStatus","value":"1"}]}]},
              {"primaryAccession":"P83044"}
            ]}
            """;

        /// <summary>Pfam from UniProt: read the reply, ask only what the service accepts, and never leave half a file.</summary>
        private static void ProteinDomainsTests(string workDir)
        {
            Dictionary<string, string> parsed = ProteinDomains.Parse(UniProtReply);
            Check("a reply gives each protein's domains by name", parsed["O93517"] == "Disintegrin; Reprolysin", parsed["O93517"]);
            Check("and a protein looked up with none is there, empty", parsed["P0DM77"] == "" && parsed["P83044"] == "");
            Check("the stored value reads back as names, tolerating spaces and nothing at all",
                ProteinDomains.Names(" Disintegrin ;Reprolysin; ").SequenceEqual(new[] { "Disintegrin", "Reprolysin" }) && ProteinDomains.Names(null).Count == 0);

            var mixed = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|P31982|VM2I_CROCC" },
                new FastaItemExtended { SequenceIdentifier = "tr|P31982-2|X" },
                new FastaItemExtended { SequenceIdentifier = "Reverse_sp|P31987|VM2I_CROVV" },
                new FastaItemExtended { SequenceIdentifier = "contaminant_P05784|K1C18_MOUSE" },
                new FastaItemExtended { SequenceIdentifier = "Venom_contig_12" },
            };
            List<List<string>> asked = ProteinDomains.Batches(mixed);
            Check("only well-formed accessions are asked, once each: one bad one and UniProt refuses the whole request",
                asked.Count == 1 && asked[0].SequenceEqual(new[] { "P31982", "P05784" }), string.Join(",", asked.SelectMany(b => b)));
            List<FastaItemExtended> many = Enumerable.Range(0, 1201).Select(i => new FastaItemExtended
            {
                SequenceIdentifier = "sp|P" + (i % 10) + (char)('A' + i / 676 % 26) + (char)('A' + i / 26 % 26) + (char)('A' + i % 26) + (i % 10) + "|X",
            }).ToList();
            List<List<string>> batches = ProteinDomains.Batches(many);
            Check("a long list is split into requests the service accepts", batches.Count == 3 && batches.All(b => b.Count <= 500), batches.Count.ToString());

            List<FastaItemExtended> items = Disintegrins();
            items.Add(new FastaItemExtended { SequenceIdentifier = "Reverse_sp|O93517|VM3S4_GLOBR", Description = "", UserID = 1, USearchID = 1 });
            var canned = new CannedUniProt((System.Net.HttpStatusCode.OK, UniProtReply));
            ProteinDomains.FetchResult result = ProteinDomains.FetchAsync(items, canned).GetAwaiter().GetResult();
            Check("a fetch fills the proteins and records the release",
                items.First(i => i.SequenceIdentifier == "sp|O93517|VM3S4_GLOBR").Domains == "Disintegrin; Reprolysin"
                && result.Source == "UniProt 2026_03" && result.WithDomains == 1 && result.Asked == 7, result.Source + " " + result.Asked);
            Check("and a decoy gets none, because it is no protein of UniProt's",
                items.Last().Domains == "" && !canned.Asked[0].Contains("Reverse", StringComparison.Ordinal));
            Check("the reply's accessions are asked for by code, not by the whole identifier",
                canned.Asked[0].Contains("accessions=Q7T1T3,O93517", StringComparison.Ordinal), canned.Asked[0]);

            List<FastaItemExtended> kept = Disintegrins();
            kept[0].Domains = "Reprolysin";
            bool failed = false;
            try
            {
                var refused = new CannedUniProt((System.Net.HttpStatusCode.BadRequest, ""));
                ProteinDomains.FetchAsync(kept, refused).GetAwaiter().GetResult();
            }
            catch (System.Net.Http.HttpRequestException) { failed = true; }
            Check("a refused request is an error, not a file full of empty domains, and what was there stays", failed && kept[0].Domains == "Reprolysin");

            static bool Refused(Action run)
            {
                try { run(); return false; }
                catch (System.Text.Json.JsonException) { return true; }
                catch (InvalidDataException) { return true; }
            }
            Check("a reply that is not UniProt's list is refused, not a crash",
                Refused(() => ProteinDomains.Parse("{}")) && Refused(() => ProteinDomains.Parse("[]")) && Refused(() => ProteinDomains.Parse("not json"))
                && Refused(() => ProteinDomains.Parse("""{"results":[{"nothing":1}]}""")));
            Check("and so is a reply naming nobody, which would otherwise wipe every domain",
                Refused(() => ProteinDomains.FetchAsync(kept, new CannedUniProt((System.Net.HttpStatusCode.OK, """{"results":[]}"""))).GetAwaiter().GetResult())
                && kept[0].Domains == "Reprolysin");
            List<FastaItemExtended> partly = Disintegrins();
            partly[0].Domains = "from an earlier release";
            ProteinDomains.FetchResult partlyFound = ProteinDomains.FetchAsync(partly, new CannedUniProt((System.Net.HttpStatusCode.OK, UniProtReply))).GetAwaiter().GetResult();
            Check("a protein the reply does not name is counted, and left as it was: unknown is not empty",
                partly[0].Domains == "from an earlier release" && partlyFound.Found == 3 && partlyFound.Asked == 7, partlyFound.Found + " of " + partlyFound.Asked);
            List<FastaItemExtended> sortable = Disintegrins();
            sortable[0].Domains = "Reprolysin";
            sortable[1].Domains = "ADAM_CR";
            sortable[2].Domains = "Disintegrin";
            Check("the Domains column sorts on domains, not on the family",
                ReportView.Sort(sortable, "Domains", true).First().Domains == "Reprolysin"
                && ReportView.Sort(sortable, "Domains", false).Last().Domains == "Reprolysin");

            List<FastaItemExtended> second = Disintegrins();
            var busy = new CannedUniProt((System.Net.HttpStatusCode.ServiceUnavailable, ""), (System.Net.HttpStatusCode.OK, UniProtReply));
            ProteinDomains.FetchAsync(second, busy).GetAwaiter().GetResult();
            Check("a busy service is asked again", busy.Asked.Count == 2 && second[1].Domains.Length > 0, busy.Asked.Count.ToString());

            string fcfPath = Path.Combine(workDir, "domains.fcf");
            var saved = new FamilyClusterFile { ClusteringIdentity = 70, DomainSource = "UniProt 2026_03", MyExtendedFastaItems = second };
            saved.Save(fcfPath);
            FamilyClusterFile reopened = FamilyClusterFile.Load(fcfPath);
            Check("the domains and their release are saved in the file",
                reopened.DomainSource == "UniProt 2026_03" && reopened.MyExtendedFastaItems[1].Domains == "Disintegrin; Reprolysin");
            Check("the Search box finds a domain, however the protein is called",
                FamilyFilter.Apply(second, "reprolysin", false, false, false).Single().SequenceIdentifier == "sp|O93517|VM3S4_GLOBR");

            var control = new ClusterGenerationControl(fcfPath);
            foreach (FastaItemExtended item in control.fcf.MyExtendedFastaItems) item.Domains = "";
            var button = typeof(ClusterGenerationControl).GetMethod("ButtonFetchDomains_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                control.FetchDomainsHandlerOverride = new CannedUniProt((System.Net.HttpStatusCode.OK, UniProtReply));
                button.Invoke(control, new object?[] { null, null });
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!control.IsEnabled || control.fcf.DomainSource.Length == 0 || control.fcf.MyExtendedFastaItems[1].Domains.Length == 0)
                {
                    if (watch.Elapsed > TimeSpan.FromSeconds(10)) break;
                    PumpDispatcher();
                    Thread.Sleep(5);
                }
                Check("the Fetch Domains button fills the table, and the family numbers do not move",
                    control.fcf.MyExtendedFastaItems[1].Domains == "Disintegrin; Reprolysin" && control.fcf.MyExtendedFastaItems[1].UserID == 1181);

                string before = control.fcf.DomainSource;
                control.fcf.MyExtendedFastaItems[1].Domains = "kept";
                control.FetchDomainsHandlerOverride = new CannedUniProt((System.Net.HttpStatusCode.BadRequest, ""));
                button.Invoke(control, new object?[] { null, null });
                watch.Restart();
                while (!control.IsEnabled && watch.Elapsed < TimeSpan.FromSeconds(10)) { PumpDispatcher(); Thread.Sleep(5); }
                PumpDispatcher();
                Thread.Sleep(50);
                PumpDispatcher();
                Check("and when UniProt refuses, the file is as it was and the screen is usable again",
                    control.fcf.MyExtendedFastaItems[1].Domains == "kept" && control.fcf.DomainSource == before && control.IsEnabled);
            }
            finally
            {
                control.FetchDomainsHandlerOverride = null;
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
    }
}
