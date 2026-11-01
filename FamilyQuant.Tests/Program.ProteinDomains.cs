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
        private static void EvidenceArchitectureTests()
        {
            var proteins = new List<FastaItemExtended>
            {
                ArchitectureItem("P00001", "First family", true, ("catalytic", "Catalytic domain"), ("binding", "Binding domain")),
                ArchitectureItem("P00002", "First family", true, ("catalytic", "Catalytic domain")),
                ArchitectureItem("P00003", "First family", false, ("binding", "Binding domain")),
                ArchitectureItem("P00004", "Second family", true, ("other", "Other domain")),
                ArchitectureItem("P00005", "Second family", false, ("other", "Other domain")),
            };
            var options = new Evidence.EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
            var proposal = Evidence.EvidenceClassifier.Propose(proteins, options);
            string before = new FamilyClusterFile { MyExtendedFastaItems = proteins }.ToJsonText();
            var suggested = Evidence.EvidenceArchitecture.Suggest(proteins, proposal);
            var first = suggested.FirstOrDefault(p => p.Family == "First family");
            Check("architecture groups distinguish a free domain from a multi-domain protein without a code rule",
                first?.CanSplit == true && first.Groups.Count == 3
                && first.Groups.Any(g => g.Domains.SequenceEqual(new[] { "binding" }) && g.SuggestedName == "Binding domain"));
            Check("a shared architecture stays whole under the same grouping code",
                suggested.FirstOrDefault(p => p.Family == "Second family") is { CanSplit: false } second && second.Groups.Count == 1);
            Check("the dominant domain is learned from reviewed family members",
                first?.Groups.FirstOrDefault(g => g.Domains.Count == 2)?.DominantDomainAccession == "catalytic");
            Check("architecture suggestions change no stored protein or family value",
                new FamilyClusterFile { MyExtendedFastaItems = proteins }.ToJsonText() == before);
            proteins[0].Evidence!.Pfam.Reverse();
            var reversed = Evidence.EvidenceArchitecture.Suggest(proteins, Evidence.EvidenceClassifier.Propose(proteins, options));
            Check("domain input order does not change the combination key",
                first != null && reversed.First(p => p.Family == "First family").Groups.Select(g => g.Key).SequenceEqual(first.Groups.Select(g => g.Key)));
            if (first != null)
            {
                Evidence.EvidenceArchitectureGroup binding = first.Groups.Single(g => g.Domains.SequenceEqual(new[] { "binding" }));
                var stored = new FamilyClusterFile
                {
                    MyExtendedFastaItems = proteins,
                    EvidenceRecipe = new Evidence.EvidenceRecipe
                    {
                        ArchitectureChoices = new List<Evidence.EvidenceArchitectureChoice>
                        {
                            new Evidence.EvidenceArchitectureChoice
                            {
                                Family = first.Family, ArchitectureKey = binding.Key, Name = binding.SuggestedName, Split = true,
                            },
                        },
                    },
                };
                var reopenedChoice = FamilyClusterFile.FromJsonText(stored.ToJsonText()).EvidenceRecipe!.ArchitectureChoices.Single();
                Check("the user's split and learned name are stored in the project recipe",
                    reopenedChoice.Split && reopenedChoice.ArchitectureKey == binding.Key && reopenedChoice.Name == "Binding domain");
            }

            static FastaItemExtended ArchitectureItem(string accession, string family, bool reviewed, params (string Id, string Name)[] domains) =>
                new FastaItemExtended
                {
                    SequenceIdentifier = "sp|" + accession + "|TEST", Sequence = "ACDEFGHIK", UserID = 31, USearchID = 31,
                    Evidence = new Evidence.ProteinEvidence
                    {
                        Accession = accession, Family = family, Reviewed = reviewed, Keywords = new List<string> { "selected" },
                        Pfam = domains.Select(d => new Evidence.EvidenceDomain { Accession = d.Id, Name = d.Name }).ToList(),
                    },
                };
        }
        private static void EvidenceClassifierTests()
        {
            string[] suppliedNames = { "Alpha family", "Beta family", "unseen example family" };
            Check("evidence guard rejects a planted family literal from supplied data",
                EvidenceRuleGuard.Violations("Family = \"unseen example family\";", suppliedNames).Count > 0);
            Check("evidence guard rejects planted domain and family database codes",
                EvidenceRuleGuard.Violations("var id = \"PF00001\";", suppliedNames).Count > 0
                && EvidenceRuleGuard.Violations("var id = \"PTHR00001:SF2\";", suppliedNames).Count > 0);
            Check("evidence guard rejects a planted fixed scope word",
                EvidenceRuleGuard.Violations("scope.Contains(\"venom\")", suppliedNames).Count > 0);
            string root = GoldenNumbers.FindRepository();
            string engine = Directory.Exists(Path.Combine(root, "FamilyQuant.Core", "Evidence"))
                ? Path.Combine(root, "FamilyQuant.Core", "Evidence") : Path.Combine(root, "FamilyQuant", "Evidence");
            var sourceViolations = Directory.GetFiles(engine, "*.cs", SearchOption.AllDirectories)
                .SelectMany(path => EvidenceRuleGuard.Violations(File.ReadAllText(path), suppliedNames).Select(v => Path.GetFileName(path) + ": " + v)).ToList();
            Check("evidence engine has no fixed biological codes, supplied family labels or scope word",
                sourceViolations.Count == 0, string.Join("; ", sourceViolations));
            var options = new Evidence.EvidenceClassificationOptions { ScopeTerms = new[] { "chosen tissue", "selected" } };
            FastaItemExtended alpha = ClassifiedItem("P00001", "Alpha family", "domain-a", true);
            FastaItemExtended beta = ClassifiedItem("P00002", "Beta family", "domain-b", true);
            FastaItemExtended query = ClassifiedItem("P00003", "", "domain-a", true);
            var rows = new List<FastaItemExtended> { alpha, beta, query };
            var hits = new Dictionary<string, Evidence.NearestNeighbour>(StringComparer.Ordinal)
            {
                [query.SequenceIdentifier] = Hit(query, alpha, 60),
                [alpha.SequenceIdentifier] = Hit(alpha, beta, 99),
            };
            string before = new FamilyClusterFile { MyExtendedFastaItems = rows }.ToJsonText();
            Evidence.EvidenceProposal proposal = Evidence.EvidenceClassifier.Propose(rows, options, hits);
            Check("classification uses the source family before a conflicting neighbour",
                proposal.Proteins[0].Tier == Evidence.EvidenceTier.UniProtFamily && proposal.Proteins[0].Family == "Alpha family");
            Check("an annotated protein accepts a reviewed neighbour exactly at sixty percent",
                proposal.Proteins[2].Tier == Evidence.EvidenceTier.ReviewedNeighbour && proposal.Proteins[2].Family == "Alpha family");
            Check("building an evidence proposal changes no stored protein or family value",
                new FamilyClusterFile { MyExtendedFastaItems = rows }.ToJsonText() == before);
            Check("scope matches complete words and phrases in keywords or tissue only",
                Evidence.EvidenceClassifier.IsInScope(new Evidence.ProteinEvidence { TissueSpecificity = new List<string> { "The chosen tissue is expressed here" } }, options.ScopeTerms)
                && !Evidence.EvidenceClassifier.IsInScope(new Evidence.ProteinEvidence { Keywords = new List<string> { "unselected" } }, options.ScopeTerms));
            Check("an empty scope is refused rather than implicitly selecting every protein",
                Throws<ArgumentException>(() => Evidence.EvidenceClassifier.Propose(rows, new Evidence.EvidenceClassificationOptions())));

            var markerRows = Enumerable.Range(1, 6).Select(i => ClassifiedItem("Q" + i.ToString("D5", CultureInfo.InvariantCulture),
                i <= 4 ? "Alpha family" : i == 5 ? "Beta family" : "", "domain-a", true)).ToList();
            var markerProposal = Evidence.EvidenceClassifier.Propose(markerRows, options);
            Check("one domain vote per carrier learns a family exactly at eighty percent",
                markerProposal.Markers.Count == 1 && markerProposal.Markers[0].FamilyCarriers == 4
                && markerProposal.Markers[0].LabelledCarriers == 5 && markerProposal.Markers[0].AllCarriers == 6
                && markerProposal.Proteins[5].Tier == Evidence.EvidenceTier.LearnedMarker);
            var aboveVote = Evidence.EvidenceClassifier.Propose(markerRows,
                new Evidence.EvidenceClassificationOptions { ScopeTerms = options.ScopeTerms, MarkerVoteFraction = 0.801 });
            Check("a vote below the selected purity stays unassigned", aboveVote.Proteins[5].Tier == Evidence.EvidenceTier.Unassigned);
            markerRows[0].Evidence!.Pfam.Add(new Evidence.EvidenceDomain { Accession = "domain-a", Name = "Duplicate source reference" });
            Check("duplicate domain references do not count as extra carrier votes",
                Evidence.EvidenceClassifier.Propose(markerRows, options).Markers.Single().AllCarriers == 6);
            var heldOut = Evidence.EvidenceClassifier.Propose(markerRows, options, maskedLabels: new HashSet<int> { 0 });
            Check("a held-out label is absent from direct assignment and marker training",
                heldOut.Proteins[0].Tier == Evidence.EvidenceTier.Unassigned && heldOut.Markers.Count == 0);

            query.Evidence!.Keywords.Clear();
            query.Evidence.TissueSpecificity.Clear();
            hits[query.SequenceIdentifier] = Hit(query, alpha, 70);
            proposal = Evidence.EvidenceClassifier.Propose(rows, options, hits);
            Check("an unannotated protein needs seventy percent and compatible domains",
                proposal.Proteins[2].Tier == Evidence.EvidenceTier.UnannotatedNeighbour && proposal.Proteins[2].Family == "Alpha family");
            hits[query.SequenceIdentifier] = Hit(query, alpha, 69.99);
            Check("an unannotated hit below seventy percent remains unassigned",
                Evidence.EvidenceClassifier.Propose(rows, options, hits).Proteins[2].Tier == Evidence.EvidenceTier.Unassigned);
            hits[query.SequenceIdentifier] = Hit(query, alpha, 90);
            query.Evidence.Pfam = new List<Evidence.EvidenceDomain> { new Evidence.EvidenceDomain { Accession = "domain-z", Name = "Domain Z" } };
            Check("high identity does not override incompatible observed domains",
                Evidence.EvidenceClassifier.Propose(rows, options, hits).Proteins[2].Tier == Evidence.EvidenceTier.Unassigned);

            query.Evidence.TissueSpecificity.Add("Expressed in the chosen tissue.");
            query.Evidence.Pfam.Clear();
            hits[query.SequenceIdentifier] = Hit(query, alpha, 90, aligned: 69);
            Check("query coverage below seventy percent is not a usable neighbour",
                Evidence.EvidenceClassifier.Propose(rows, options, hits).Proteins[2].Tier == Evidence.EvidenceTier.Unassigned);
            hits[query.SequenceIdentifier] = Hit(query, query, 100);
            Check("the classifier never accepts a self-labelled neighbour",
                Evidence.EvidenceClassifier.Propose(rows, options, hits).Proteins[2].Tier == Evidence.EvidenceTier.Unassigned);

            query.Evidence.FamilyMemberships = new List<Evidence.EvidenceFamilyMembership>
            {
                new Evidence.EvidenceFamilyMembership { Family = "Alpha family", Context = "first section" },
                new Evidence.EvidenceFamilyMembership { Family = "Beta family", Context = "second section" },
            };
            hits[query.SequenceIdentifier] = Hit(query, alpha, 95);
            var ambiguous = Evidence.EvidenceClassifier.Propose(rows, options, hits).Proteins[2];
            Check("multiple source families stay flagged for user choice instead of choosing a neighbour",
                ambiguous.Tier == Evidence.EvidenceTier.Unassigned && ambiguous.Flags.Count > 0 && !ambiguous.SuggestedSelected);
            query.Evidence.FamilyMemberships = null;
            var tied = Hit(query, alpha, 95);
            tied.TiedFamilies.Add("Beta family");
            hits[query.SequenceIdentifier] = tied;
            var crossFamilyTie = Evidence.EvidenceClassifier.Propose(rows, options, hits).Proteins[2];
            Check("a tie across reviewed families remains flagged and unassigned",
                crossFamilyTie.Tier == Evidence.EvidenceTier.Unassigned && crossFamilyTie.Flags.Count > 0);
            var decoy = ClassifiedItem("P00004", "Alpha family", "domain-a", true);
            decoy.SequenceIdentifier = SequenceLabels.DecoyPrefix + decoy.SequenceIdentifier;
            rows.Add(decoy);
            Check("decoys receive no classification proposal", Evidence.EvidenceClassifier.Propose(rows, options, hits).Proteins.Count == 3);

            static FastaItemExtended ClassifiedItem(string accession, string family, string domain, bool annotated) => new FastaItemExtended
            {
                SequenceIdentifier = "sp|" + accession + "|TEST", Sequence = "ACDEFGHIK", Description = "selected in description only",
                UserID = 31, USearchID = 31,
                Evidence = new Evidence.ProteinEvidence
                {
                    Accession = accession, Reviewed = true, Family = family,
                    Keywords = annotated ? new List<string> { "selected" } : new List<string>(),
                    Pfam = new List<Evidence.EvidenceDomain> { new Evidence.EvidenceDomain { Accession = domain, Name = "Domain from source" } },
                },
            };
            static Evidence.NearestNeighbour Hit(FastaItemExtended target, FastaItemExtended reference, double identity, int aligned = 70) =>
                new Evidence.NearestNeighbour
                {
                    Query = target.SequenceIdentifier, Reference = reference.SequenceIdentifier,
                    Accession = reference.Evidence!.Accession, Family = reference.Evidence.Family,
                    IdentityPercent = identity, AlignmentLength = aligned, QueryLength = 100, ReferenceLength = 100,
                };
        }
        private static Task<Evidence.EvidenceFetchResult> FetchEvidenceForTest(IReadOnlyCollection<FastaItemExtended> items,
            System.Net.Http.HttpMessageHandler handler) => Evidence.EvidenceFetcher.FetchAsync(
                new FamilyClusterFile { MyExtendedFastaItems = items.ToList() }, handler);

        private static Dictionary<string, string> ParseDomainsForTest(string json) => Evidence.EvidenceFetcher.Parse(json)
            .ToDictionary(p => p.Key, p => Evidence.EvidenceFetcher.DomainNames(p.Value), StringComparer.Ordinal);
        private const string RichEvidenceReply = """
            {"results":[{"primaryAccession":"P00001","entryType":"UniProtKB reviewed (Swiss-Prot)",
              "proteinDescription":{"flag":"Fragment"},
              "comments":[{"commentType":"SIMILARITY","texts":[{"value":"Belongs to the example family. Narrow subfamily."}]},
                          {"commentType":"TISSUE SPECIFICITY","texts":[{"value":"Expressed in a source tissue"}]}],
              "keywords":[{"id":"KW-0001","name":"example keyword"}],
              "features":[{"type":"Transmembrane"},{"type":"Transmembrane"},{"type":"Domain"}],
              "uniProtKBCrossReferences":[
                {"database":"Pfam","id":"PF00001","properties":[{"key":"EntryName","value":"Example domain"}]},
                {"database":"PANTHER","id":"PTHR00001","properties":[{"key":"EntryName","value":"Example reference"}]}]}]}
            """;

        private static void EvidenceFetchTests()
        {
            const string compositeFamilyReply = """
                {"results":[{"primaryAccession":"P00001","comments":[
                  {"commentType":"SIMILARITY","texts":[{"value":"In the first section; belongs to the alpha family"}]},
                  {"commentType":"SIMILARITY","texts":[{"value":"In the second section; belongs to the beta superfamily. Specific beta family"}]}]}]}
                """;
            Evidence.ProteinEvidence composite = Evidence.EvidenceFetcher.Parse(compositeFamilyReply)["P00001"];
            Check("regional memberships retain separate hierarchies and do not invent one family label",
                composite.FamilyAnnotation == "alpha family; beta superfamily, Specific beta family" && composite.Family.Length == 0
                && composite.FamilyMemberships?.Count == 2 && composite.SimilarityAnnotations?.Count == 2
                && composite.FamilyMemberships[0].Context == "In the first section");
            Evidence.ProteinEvidence unranked = Evidence.EvidenceFetcher.Parse("""
                {"results":[{"primaryAccession":"P00001","comments":[
                  {"commentType":"SIMILARITY","texts":[{"value":"Belongs to the unranked example group"}]}]}]}
                """)["P00001"];
            Check("an unranked similarity statement is retained without being called a family annotation",
                unranked.FamilyAnnotation.Length == 0 && unranked.Family.Length == 0
                && unranked.SimilarityAnnotations?.Single() == "Belongs to the unranked example group");
            var protein = new FastaItemExtended { SequenceIdentifier = "sp|P00001|TEST", UserID = 17, USearchID = 17 };
            var handler = new CannedUniProt((System.Net.HttpStatusCode.OK, RichEvidenceReply));
            FetchEvidenceForTest(new[] { protein }, handler).GetAwaiter().GetResult();
            Check("fetch reads all evidence fields while keeping the Domains column",
                protein.Evidence is { Reviewed: true, Fragment: true, TransmembraneCount: 2 } evidence
                && evidence.Family == "example family" && evidence.Subfamily == "Narrow subfamily"
                && evidence.Keywords.SequenceEqual(new[] { "example keyword" })
                && evidence.TissueSpecificity.SequenceEqual(new[] { "Expressed in a source tissue" })
                && evidence.Pfam.Count == 1 && evidence.Pfam[0].Accession == "PF00001"
                && evidence.Panther.Count == 1 && evidence.Panther[0].Name == "Example reference"
                && protein.Domains == "Example domain" && protein.UserID == 17 && protein.USearchID == 17);

            var progress = new List<Evidence.FetchProgress>();
            FamilyClusterFile complete = Fixture(1);
            complete.MyExtendedFastaItems.Add(new FastaItemExtended
            {
                SequenceIdentifier = "Reverse_sp|P00001|TEST", Domains = "decoy annotation", UserID = 22,
            });
            Evidence.EvidenceFetcher.FetchAsync(complete,
                new CannedUniProt((System.Net.HttpStatusCode.OK, RichEvidenceReply)),
                new EvidenceProgress(p => progress.Add(p))).GetAwaiter().GetResult();
            Check("file-level fetch records the release, record source and progress without changing decoys",
                complete.EvidenceSource?.Release == "2026_03"
                && complete.MyExtendedFastaItems[0].Evidence?.Source?.Release == "2026_03"
                && complete.MyExtendedFastaItems[1].Domains == "decoy annotation"
                && complete.MyExtendedFastaItems[1].Evidence == null
                && progress.Count == 2 && progress[0].CompletedBatches == 0 && progress[1].CompletedBatches == 1);

            FamilyClusterFile missing = Fixture(2);
            var older = new Evidence.ProteinEvidence
            {
                Family = "older family", Source = new Evidence.EvidenceRecordSource { Provider = "UniProt", Release = "earlier" },
            };
            missing.MyExtendedFastaItems[1].Evidence = older;
            Evidence.EvidenceFetchResult partial = Evidence.EvidenceFetcher.FetchAsync(missing,
                new CannedUniProt((System.Net.HttpStatusCode.OK, RichEvidenceReply))).GetAwaiter().GetResult();
            Check("a missing entry keeps its annotations and earlier record source, with its accession recorded",
                partial.Asked == 2 && partial.Found == 1 && partial.Missing == 1
                && ReferenceEquals(missing.MyExtendedFastaItems[1].Evidence, older)
                && missing.MyExtendedFastaItems[1].Domains == "before"
                && missing.MyExtendedFastaItems[1].Evidence!.Source!.Release == "earlier"
                && missing.EvidenceSource!.UnresolvedAccessions!.SequenceEqual(new[] { "P00002" }));

            FamilyClusterFile lateRefusal = Fixture(501);
            string before = lateRefusal.ToJsonText();
            var refused = new CannedUniProt((System.Net.HttpStatusCode.OK, Reply(lateRefusal.MyExtendedFastaItems.Take(500))),
                (System.Net.HttpStatusCode.BadRequest, "refused"));
            Check("a refused later batch applies no protein, domain or provenance changes and is not retried",
                Throws<System.Net.Http.HttpRequestException>(() => Evidence.EvidenceFetcher.FetchAsync(lateRefusal, refused).GetAwaiter().GetResult())
                && lateRefusal.ToJsonText() == before && refused.Asked.Count == 2);

            FamilyClusterFile cancelled = Fixture(501);
            before = cancelled.ToJsonText();
            using var cancel = new CancellationTokenSource();
            var cancelHandler = new CannedUniProt((System.Net.HttpStatusCode.OK, Reply(cancelled.MyExtendedFastaItems.Take(500))));
            Check("cancellation after a successful batch applies nothing and sends no later request",
                Throws<OperationCanceledException>(() => Evidence.EvidenceFetcher.FetchAsync(cancelled, cancelHandler,
                    new EvidenceProgress(p => { if (p.CompletedBatches == 1) cancel.Cancel(); }), cancel.Token).GetAwaiter().GetResult())
                && cancelled.ToJsonText() == before && cancelHandler.Asked.Count == 1);

            FamilyClusterFile mixedRelease = Fixture(501);
            before = mixedRelease.ToJsonText();
            var mixedHandler = new CannedUniProt((System.Net.HttpStatusCode.OK, Reply(mixedRelease.MyExtendedFastaItems.Take(500))),
                (System.Net.HttpStatusCode.OK, Reply(mixedRelease.MyExtendedFastaItems.Skip(500))))
            {
                ReleaseForRequest = n => n == 1 ? "first-release" : "second-release",
            };
            Check("a release change between batches applies nothing",
                Throws<InvalidDataException>(() => Evidence.EvidenceFetcher.FetchAsync(mixedRelease, mixedHandler).GetAwaiter().GetResult())
                && mixedRelease.ToJsonText() == before);

            FamilyClusterFile noRelease = Fixture(1);
            before = noRelease.ToJsonText();
            var noReleaseHandler = new CannedUniProt((System.Net.HttpStatusCode.OK, RichEvidenceReply)) { ReleaseForRequest = _ => "" };
            Check("a reply with no release is refused without recording unsupported provenance",
                Throws<InvalidDataException>(() => Evidence.EvidenceFetcher.FetchAsync(noRelease, noReleaseHandler).GetAwaiter().GetResult())
                && noRelease.ToJsonText() == before);

            FamilyClusterFile noStatus = Fixture(1);
            before = noStatus.ToJsonText();
            Check("a fetched record without the requested review status is refused",
                Throws<InvalidDataException>(() => Evidence.EvidenceFetcher.FetchAsync(noStatus,
                    new CannedUniProt((System.Net.HttpStatusCode.OK, """{"results":[{"primaryAccession":"P00001"}]}"""))).GetAwaiter().GetResult())
                && noStatus.ToJsonText() == before);

            FamilyClusterFile noCodes = Fixture(1);
            noCodes.MyExtendedFastaItems[0].SequenceIdentifier = "local_sequence";
            before = noCodes.ToJsonText();
            var unusedHandler = new CannedUniProt();
            Evidence.EvidenceFetchResult nothing = Evidence.EvidenceFetcher.FetchAsync(noCodes, unusedHandler).GetAwaiter().GetResult();
            Check("a file without accession codes is unchanged and sends no request",
                nothing.Asked == 0 && unusedHandler.Asked.Count == 0 && noCodes.ToJsonText() == before);

            FamilyClusterFile changed = Fixture(1);
            Check("a protein changed during fetch cannot receive evidence for its previous identifier",
                Throws<InvalidDataException>(() => Evidence.EvidenceFetcher.FetchAsync(changed,
                    new CannedUniProt((System.Net.HttpStatusCode.OK, RichEvidenceReply)),
                    new EvidenceProgress(p => { if (p.CompletedBatches == 1) changed.MyExtendedFastaItems[0].SequenceIdentifier = "local_changed"; }))
                    .GetAwaiter().GetResult()) && changed.MyExtendedFastaItems[0].Evidence == null
                && changed.MyExtendedFastaItems[0].Domains == "before" && changed.DomainSource == "before");

            var rateLimited = new CannedUniProt((System.Net.HttpStatusCode.TooManyRequests, "busy"),
                (System.Net.HttpStatusCode.OK, RichEvidenceReply));
            Check("an HTTP 429 batch is retried before applying complete evidence",
                Evidence.EvidenceFetcher.FetchAsync(Fixture(1), rateLimited).GetAwaiter().GetResult().Found == 1
                && rateLimited.Asked.Count == 2);

            static FamilyClusterFile Fixture(int count) => new FamilyClusterFile
            {
                DomainSource = "before", LockedUserIds = new HashSet<int> { 17 }, SourceSha256 = "unchanged-source",
                MyExtendedFastaItems = Enumerable.Range(1, count).Select(i => new FastaItemExtended
                {
                    SequenceIdentifier = "sp|P" + i.ToString("D5", CultureInfo.InvariantCulture) + "|TEST",
                    Sequence = "ACDEFGHIK", Domains = "before", UserID = i + 20, USearchID = i + 20,
                }).ToList(),
            };
            static string Reply(IEnumerable<FastaItemExtended> items) => JsonSerializer.Serialize(new
            {
                results = items.Select(i => new
                {
                    primaryAccession = FamilyNaming.Accession(i.SequenceIdentifier), entryType = "UniProtKB unreviewed (TrEMBL)",
                }).ToArray(),
            });
        }

        private sealed class EvidenceProgress : IProgress<Evidence.FetchProgress>
        {
            private readonly Action<Evidence.FetchProgress> report;
            public EvidenceProgress(Action<Evidence.FetchProgress> report) => this.report = report;
            public void Report(Evidence.FetchProgress value) => report(value);
        }
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
            public Func<int, string>? ReleaseForRequest { get; set; }

            public CannedUniProt(params (System.Net.HttpStatusCode Status, string Body)[] replies) =>
                this.replies = new Queue<(System.Net.HttpStatusCode, string)>(replies);

            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Asked.Add(request.RequestUri!.ToString());
                (System.Net.HttpStatusCode status, string body) = replies.Dequeue();
                var response = new System.Net.Http.HttpResponseMessage(status) { Content = new System.Net.Http.StringContent(body) };
                string release = ReleaseForRequest?.Invoke(Asked.Count) ?? "2026_03";
                if (release.Length > 0) response.Headers.Add("x-uniprot-release", release);
                return Task.FromResult(response);
            }
        }

        private const string UniProtReply = """
            {"results":[
              {"primaryAccession":"P0DM77","entryType":"UniProtKB unreviewed (TrEMBL)","uniProtKBCrossReferences":[]},
              {"primaryAccession":"O93517","entryType":"UniProtKB unreviewed (TrEMBL)","uniProtKBCrossReferences":[
                {"database":"Pfam","id":"PF00200","properties":[{"key":"EntryName","value":"Disintegrin"},{"key":"MatchStatus","value":"1"}]},
                {"database":"Pfam","id":"PF01421","properties":[{"key":"EntryName","value":"Reprolysin"},{"key":"MatchStatus","value":"1"}]}]},
              {"primaryAccession":"P83044","entryType":"UniProtKB unreviewed (TrEMBL)"}
            ]}
            """;

        /// <summary>Pfam from UniProt: read the reply, ask only what the service accepts, and never leave half a file.</summary>
        private static void ProteinDomainsTests(string workDir)
        {
            Dictionary<string, string> parsed = ParseDomainsForTest(UniProtReply);
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
            Evidence.EvidenceFetchResult result = FetchEvidenceForTest(items, canned).GetAwaiter().GetResult();
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
                FetchEvidenceForTest(kept, refused).GetAwaiter().GetResult();
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
                Refused(() => ParseDomainsForTest("{}")) && Refused(() => ParseDomainsForTest("[]")) && Refused(() => ParseDomainsForTest("not json"))
                && Refused(() => ParseDomainsForTest("""{"results":[{"nothing":1}]}""")));
            Check("and so is a reply naming nobody, which would otherwise wipe every domain",
                Refused(() => FetchEvidenceForTest(kept, new CannedUniProt((System.Net.HttpStatusCode.OK, """{"results":[]}"""))).GetAwaiter().GetResult())
                && kept[0].Domains == "Reprolysin");
            List<FastaItemExtended> partly = Disintegrins();
            partly[0].Domains = "from an earlier release";
            Evidence.EvidenceFetchResult partlyFound = FetchEvidenceForTest(partly, new CannedUniProt((System.Net.HttpStatusCode.OK, UniProtReply))).GetAwaiter().GetResult();
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
            FetchEvidenceForTest(second, busy).GetAwaiter().GetResult();
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
