using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class WorldFetchHandler : HttpMessageHandler
    {
        internal int Requests;
        internal bool FailClade, ChangedRelease, MissingTotal;
        internal bool PaginatedReviewed, DuplicatePage, TruncatedReviewed, MissingLineage, WrongClade;
        internal Action<bool, CancellationToken>? DuringRequest;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            bool reviewed = Uri.UnescapeDataString(request.RequestUri!.Query).Contains("reviewed:true", StringComparison.Ordinal);
            bool second = request.RequestUri.Query.Contains("page=2", StringComparison.Ordinal);
            DuringRequest?.Invoke(reviewed, token);
            string json = """
                {"results":[{"primaryAccession":"P00011","entryType":"UniProtKB reviewed (Swiss-Prot)","organism":{"taxonId":99},"lineages":[{"taxonId":1}],
                "sequence":{"value":"PEPTIDEK"},"keywords":[{"name":"Leading scope"}],
                "comments":[{"commentType":"SIMILARITY","texts":[{"value":"Belongs to the Fixture family."}]}]}]}
                """;
            if (!reviewed) json = json.Replace("P00011", "P00012").Replace("UniProtKB reviewed (Swiss-Prot)", "UniProtKB unreviewed (TrEMBL)").Replace("99", "21")
                .Replace("\"lineages\":[{\"taxonId\":1}]", "\"lineages\":[{\"taxonId\":1},{\"taxonId\":10}]");
            if (second && !DuplicatePage) json = json.Replace("P00011", "P00013");
            if (MissingLineage) json = json.Replace("\"lineages\":", "\"missingLineages\":");
            if (!reviewed && WrongClade) json = json.Replace("{\"taxonId\":1},{\"taxonId\":10}", "{\"taxonId\":1}");
            var response = new HttpResponseMessage(!reviewed && FailClade ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
                { Content = new StringContent(json) };
            response.Headers.Add("x-uniprot-release", !reviewed && ChangedRelease ? "changed" : "fixture");
            if (!MissingTotal) response.Headers.Add("x-total-results", reviewed && (PaginatedReviewed || TruncatedReviewed) ? "2" : "1");
            if (reviewed && PaginatedReviewed && !second)
                response.Headers.Add("Link", "<https://rest.uniprot.org/uniprotkb/search?query=%28reviewed%3Atrue%29&page=2>; rel=\"next\"");
            return Task.FromResult(response);
        }
    }

    private static void FamilyWorldFetchTests()
    {
        static EvidenceTaskChoice TaskChoice(FamilyClusterFile file)
        {
            using var handler = new TaskTaxonomyHandler();
            var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
            return EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
        }
        {
            var file = TaskFile(); var task = TaskChoice(file); using var handler = new WorldFetchHandler();
            var result = CoreOperation.RunAsync("Family world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, task, context, handler)).GetAwaiter().GetResult();
            Check("B4 stores reviewed global and unreviewed clade entries with confirmed scope and release", result.Status == OperationStatus.Succeeded
                && handler.Requests == 2 && file.EvidenceSource?.FamilyWorld is { } world && world.Entries.Count == 2
                && world.Entries.Any(e => e.Evidence.Reviewed == false && e.FromClade) && world.Release == "fixture"
                && world.Task.Proposal.Clade.TaxonId == 10 && world.Families.SequenceEqual(new[] { "Fixture family" }) && world.ContentSha256.Length == 64);
            if (result.Status == OperationStatus.Succeeded)
            {
                string bytes = file.ToJsonText();
                var reopened = FamilyClusterFile.FromJsonText(bytes);
                FamilyWorldFetcher.Stored(reopened);
                Check("B4 world is complete and byte-identical after offline reopen", reopened.ToJsonText() == bytes
                    && JsonSerializer.Serialize(reopened.EvidenceSource!.FamilyWorld) == JsonSerializer.Serialize(result.Value));
                string container = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fq-world-" + Guid.NewGuid().ToString("N") + ".fqproj");
                ProjectStore.WriteFqproj(container, reopened, Array.Empty<QuantRow>(), new QuantManifest { AppVersion = "fixture" });
                var project = ProjectStore.ReadFqproj(container);
                Check("B4 the project container preserves the entire stored world", JsonSerializer.Serialize(FamilyWorldFetcher.Stored(project.fcf))
                    == JsonSerializer.Serialize(result.Value));
                reopened.EvidenceSource!.FamilyWorld!.Entries[0].Sequence += "A";
                Check("B4 offline integrity rejects a tampered sequence", Throws<System.IO.InvalidDataException>(() => FamilyWorldFetcher.Stored(reopened)));
            }
        }
        {
            var file = TaskFile(); var task = TaskChoice(file); using var handler = new WorldFetchHandler { PaginatedReviewed = true };
            var result = CoreOperation.RunAsync("Family world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, task, context, handler)).GetAwaiter().GetResult();
            Check("B4 reads every announced page and retains taxonomy on every partition", result.Status == OperationStatus.Succeeded
                && handler.Requests == 3 && result.Value.Entries.Count == 3 && result.Value.Entries.All(e => e.TaxonId > 0 && e.LineageTaxonIds.Count > 0)
                && result.Value.Queries[0].Received == 2);
        }
        foreach (string failure in new[] { "cancel clade", "HTTP clade", "release", "total", "source edit", "duplicate page", "truncated", "lineage", "clade" })
        {
            var file = TaskFile(); var task = TaskChoice(file); string before = file.ToJsonText();
            using var cancellation = new CancellationTokenSource(); using var handler = new WorldFetchHandler();
            handler.FailClade = failure == "HTTP clade"; handler.ChangedRelease = failure == "release"; handler.MissingTotal = failure == "total";
            handler.PaginatedReviewed = failure == "duplicate page"; handler.DuplicatePage = failure == "duplicate page";
            handler.TruncatedReviewed = failure == "truncated"; handler.MissingLineage = failure == "lineage"; handler.WrongClade = failure == "clade";
            string expected = before;
            handler.DuringRequest = (reviewed, token) =>
            {
                if (reviewed) return;
                if (failure == "cancel clade") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                if (failure == "source edit")
                {
                    file.MyExtendedFastaItems[0].UserAnnotation = "User edit";
                    expected = file.ToJsonText();
                }
            };
            var result = CoreOperation.RunAsync("Family world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, task, context, handler), cancellation.Token).GetAwaiter().GetResult();
            Check("B4 " + failure + " leaves the original file without a partial world", result.Status != OperationStatus.Succeeded
                && handler.Requests == (failure is "total" or "truncated" or "lineage" ? 1 : 2)
                && file.EvidenceSource?.FamilyWorld == null && file.ToJsonText() == expected);
        }
        {
            var file = TaskFile(); var task = TaskChoice(file);
            var proposal = EvidenceClassifier.Propose(file.MyExtendedFastaItems, new EvidenceClassificationOptions { ScopeTerms = task.ScopeTerms });
            var applied = EvidenceApply.Apply(file, proposal, new EvidenceApplySelection { SourceFingerprint = proposal.SourceFingerprint,
                SelectedProteinIndices = proposal.Proteins.Where(p => p.SuggestedSelected).Select(p => p.ProteinIndex).ToArray() }, "fixture");
            string legacyRecipe = JsonSerializer.Serialize(applied.Recipe);
            using var handler = new WorldFetchHandler();
            var fetched = CoreOperation.RunAsync("Family world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, task, context, handler)).GetAwaiter().GetResult();
            bool unchanged = fetched.Status == OperationStatus.Succeeded && JsonSerializer.Serialize(file.EvidenceRecipe) == legacyRecipe;
            var replayed = EvidenceApply.ApplyRecipe(file, applied.Recipe);
            Check("B4 independent world metadata preserves existing recipe hash bytes and idempotent replay", unchanged && !replayed.Changed
                && file.EvidenceSource?.FamilyWorld != null && JsonSerializer.Serialize(file.EvidenceRecipe) == legacyRecipe);
        }
    }
}
