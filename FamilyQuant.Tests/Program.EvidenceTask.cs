using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
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
    private sealed class TaskTaxonomyHandler : HttpMessageHandler
    {
        internal int Requests;
        internal bool MissingRelease, ChangedRelease, WrongId;
        internal CancellationTokenSource? Cancel;
        internal Action? DuringRequest;
        internal int? DifferentFamilyTaxon;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            DuringRequest?.Invoke();
            if (request.RequestUri!.AbsolutePath == "/keywords/stream")
            {
                var vocabulary = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                    {"results":[{"keyword":{"name":"Leading scope","id":"KW-0001"},"category":{"name":"Molecular function","id":"KW-9992"}},
                    {"keyword":{"name":"Other","id":"KW-0002"},"category":{"name":"Technical term","id":"KW-9990"}}]}
                    """) };
                vocabulary.Headers.Add("x-uniprot-release", "fixture");
                return Task.FromResult(vocabulary);
            }
            int id = int.Parse(request.RequestUri!.Segments.Last(), CultureInfo.InvariantCulture);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    taxonId = WrongId ? id + 1 : id, scientificName = "Species " + id.ToString(CultureInfo.InvariantCulture), rank = "species", active = true,
                    lineage = new[] { new { taxonId = 1, scientificName = "Fixture root", rank = "superkingdom" },
                        new { taxonId = id == DifferentFamilyTaxon ? 12 : 10, scientificName = "Fixture clade", rank = "family" },
                        new { taxonId = id == DifferentFamilyTaxon ? 13 : 11, scientificName = "Fixture genus", rank = "genus" } },
                })),
            };
            if (!MissingRelease) response.Headers.Add("x-uniprot-release", ChangedRelease && Requests > 1 ? "other" : "fixture");
            Cancel?.Cancel();
            return Task.FromResult(response);
        }
    }

    private static FamilyClusterFile TaskFile() => new()
    {
        EvidenceSource = new EvidenceProvenance { Provider = "UniProt", Release = "fixture" },
        MyExtendedFastaItems = new List<FastaItemExtended>
        {
            TaskProtein("sp|P00001|ONE", 21, true, "Leading scope", "Other"),
            TaskProtein("tr|P00002|TWO", 22, true, "Leading scope"),
            TaskProtein("tr|P00003|THREE", 21, false, "Other"),
            TaskProtein("contaminant_unreadable", 0, true, "Contaminant keyword"),
            TaskProtein("Reverse_unreadable", 0, true, "Decoy keyword"),
        },
    };

    private static FastaItemExtended TaskProtein(string identifier, int taxon, bool reviewed, params string[] keywords) => new()
    {
        SequenceIdentifier = identifier, Sequence = "ACDEFGHIK", UserID = 1, USearchID = 1,
        Description = "Fixture OS=Species " + taxon.ToString(CultureInfo.InvariantCulture) + " OX=" + taxon.ToString(CultureInfo.InvariantCulture) + " PE=1 SV=1",
        Evidence = new ProteinEvidence { Accession = FamilyNaming.Accession(identifier), Reviewed = reviewed,
            Family = "Fixture family", Keywords = keywords.ToList(), Source = new EvidenceRecordSource { Provider = "UniProt", Release = "fixture" } },
    };

    private static void EvidenceTaskTests()
    {
        TaskOrderCoreChecks();
        static void Verify(string name, Action run)
        {
            try { run(); }
            catch (Exception ex) { Check(name, false, ex.GetBaseException().Message); }
        }
        foreach (int pairs in new[] { 2, 3 })
            Verify("M48 accepts " + pairs.ToString(CultureInfo.InvariantCulture) + " joined same-taxon headers", () =>
            {
                var file = TaskFile();
                file.MyExtendedFastaItems[0].Description += string.Concat(Enumerable.Range(1, pairs - 1)
                    .Select(index => " : tr|P00991|ALIAS Fixture OS=Alternate label " + index.ToString(CultureInfo.InvariantCulture) + " OX=21 PE=1 SV=1"));
                string before = file.ToJsonText(); using var handler = new TaskTaxonomyHandler();
                var result = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
                Check("M48 joined " + pairs.ToString(CultureInfo.InvariantCulture) + " same-taxon pairs remain one target and one taxon observation",
                    result.TargetProteins == 3 && result.ReviewedProteins == 2 && result.Taxa.Select(t => t.TaxonId).SequenceEqual(new[] { 21, 22 })
                    && result.Clade.TaxonId == 10 && handler.Requests == 3 && file.ToJsonText() == before);
            });
        Verify("B11 a joined target retains every parsed taxon", () =>
        {
            var file = TaskFile(); file.MyExtendedFastaItems[0].Description += " : tr|P00992|ALIAS Fixture OS=Other label OX=23 PE=1 SV=1";
            string before = file.ToJsonText();
            Check("B11 sample taxa retain the secondary taxon without counting decoys or contaminants",
                FamilyWorldAnalysis.SampleTaxa(file).SetEquals(new[] { "21", "22", "23" }));
            var joined = FamilyWorld.Collect(file).Single(protein => protein.Id == file.MyExtendedFastaItems[0].SequenceIdentifier);
            Check("B11 a joined world target belongs to each taxon for own-species removal",
                FamilyWorld.IsOfTaxa(joined, new[] { "21" }) && FamilyWorld.IsOfTaxa(joined, new[] { "23" })
                && !FamilyWorld.IsOfTaxa(joined, new[] { "24" }));
            using var handler = new TaskTaxonomyHandler { DifferentFamilyTaxon = 23 };
            var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
            Check("B11 joined different-taxon header is one target and all taxa determine the common clade",
                proposal.TargetProteins == 3 && proposal.ReviewedProteins == 2 && proposal.Taxa.Select(taxon => taxon.TaxonId).SequenceEqual(new[] { 21, 22, 23 })
                && proposal.Clade.TaxonId == 1 && proposal.ProposedScopeTerms.SequenceEqual(new[] { "Leading scope" })
                && proposal.Keywords[0].ReviewedProteins == 2 && handler.Requests == 4 && file.ToJsonText() == before);
            using var repeated = new TaskTaxonomyHandler { DifferentFamilyTaxon = 23 };
            Check("B11 repeated multi-taxon discovery is byte identical", JsonSerializer.Serialize(proposal)
                == JsonSerializer.Serialize(EvidenceTaskDiscovery.DiscoverAsync(file, repeated).GetAwaiter().GetResult()));
            var choice = EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
            var saved = JsonSerializer.Deserialize<EvidenceTaskChoice>(JsonSerializer.Serialize(choice))!;
            Check("B11 confirmed task roundtrip retains all taxa and clade without changing the source",
                saved.Proposal.Taxa.Select(taxon => taxon.TaxonId).SequenceEqual(new[] { 21, 22, 23 }) && saved.Proposal.Clade.TaxonId == 1
                && file.ToJsonText() == before);
        });
        Verify("B11 refuses a target with no parsable taxon before HTTP", () =>
        {
            var file = TaskFile(); file.MyExtendedFastaItems[0].Description = "Fixture OS=No numeric taxon OX=unknown";
            string before = file.ToJsonText(); using var handler = new TaskTaxonomyHandler(); bool refused = false;
            try { EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { refused = true; }
            Check("B11 no parsable taxon refuses the whole discovery without guessing or changing source",
                refused && handler.Requests == 0 && file.ToJsonText() == before);
        });
        Verify("M48 conflicting contaminant headers remain outside discovery", () =>
        {
            var file = TaskFile(); file.MyExtendedFastaItems[3].Description = "Fixture OS=First label OX=21 : Fixture OS=Second label OX=22";
            string before = file.ToJsonText(); using var handler = new TaskTaxonomyHandler();
            var result = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
            Check("M48 discovery still ignores a conflicting contaminant target", result.TargetProteins == 3 && result.IgnoredContaminants == 1
                && handler.Requests == 3 && file.ToJsonText() == before);
        });
        Verify("B3 derives a clade and the leading keyword without a name rule, ignoring contaminants and decoys", () =>
        {
            var file = TaskFile(); string before = file.ToJsonText(); using var handler = new TaskTaxonomyHandler();
            var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
            Check("B3 taxonomy and scope come from the file's data", handler.Requests == 3 && proposal.Clade.TaxonId == 10
                && proposal.Clade.ScientificName == "Fixture clade" && proposal.ProposedScopeTerms.SequenceEqual(new[] { "Leading scope" })
                && proposal.Keywords[0].ReviewedProteins == 2 && proposal.ReviewedProteins == 2 && proposal.TargetProteins == 3
                && proposal.IgnoredContaminants == 1 && proposal.Taxa.Select(t => t.TaxonId).SequenceEqual(new[] { 21, 22 }));
            Check("B3 discovery is read only", file.ToJsonText() == before);
            using var secondHandler = new TaskTaxonomyHandler();
            Check("B3 repeated discovery is byte identical", JsonSerializer.Serialize(proposal)
                == JsonSerializer.Serialize(EvidenceTaskDiscovery.DiscoverAsync(file, secondHandler).GetAwaiter().GetResult()));
            var choice = EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
            var grouping = EvidenceClassifier.Propose(file.MyExtendedFastaItems, new EvidenceClassificationOptions { ScopeTerms = choice.ScopeTerms });
            var selection = new EvidenceApplySelection
            {
                SourceFingerprint = grouping.SourceFingerprint, ConfirmedTask = choice,
                SelectedProteinIndices = grouping.Proteins.Where(p => p.SuggestedSelected).Select(p => p.ProteinIndex).ToArray(),
            };
            var applied = EvidenceApply.Apply(file, grouping, selection, "fixture");
            string exact = file.ToJsonText();
            var reopened = FamilyClusterFile.FromJsonText(exact);
            Check("B3 confirmation persists the clade, keyword counts and chosen scope in the recipe", reopened.EvidenceRecipe?.ConfirmedTask is { } saved
                && saved.Proposal.Clade.TaxonId == 10 && saved.Proposal.Keywords[0].ReviewedProteins == 2 && saved.ScopeTerms.SequenceEqual(choice.ScopeTerms));
            applied.Undo!.Restore(file);
            EvidenceApply.ApplyRecipe(file, applied.Recipe);
            Check("B3 confirmed recipe replays byte exactly without a taxonomy request", file.ToJsonText() == exact);
            applied.Undo.Restore(file);
            var altered = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(applied.Recipe))!;
            altered.ConfirmedTask!.Proposal.Clade.TaxonId++;
            bool tamperRefused = false;
            try { EvidenceApply.ApplyRecipe(file, altered); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { tamperRefused = true; }
            Check("B3 replay rejects altered confirmation without changing the file", tamperRefused && file.ToJsonText() == before);
            file.MyExtendedFastaItems[0].Description += " changed";
            bool staleRefused = false;
            try { EvidenceTaskDiscovery.Confirm(file, proposal, choice.ScopeTerms); } catch (InvalidOperationException) { staleRefused = true; }
            Check("B3 refuses a stale header at confirmation", staleRefused);
        });
        Verify("B3 deduplicates reviewed accessions and needs evidence before suggesting a scope", () =>
        {
            var file = TaskFile();
            file.MyExtendedFastaItems.Add(TaskProtein("sp|P00001|ALIAS", 21, true, "Leading scope", "Other"));
            using var handler = new TaskTaxonomyHandler();
            var result = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
            Check("B3 keyword counts use distinct reviewed accessions", result.ReviewedProteins == 2 && result.Keywords[0].ReviewedProteins == 2);
            file.MyExtendedFastaItems.Add(TaskProtein("sp|P00004|FOUR", 21, true, "Other"));
            file.MyExtendedFastaItems[1].Evidence!.Keywords.Add("Other");
            using var categoryHandler = new TaskTaxonomyHandler();
            result = EvidenceTaskDiscovery.DiscoverAsync(file, categoryHandler).GetAwaiter().GetResult();
            Check("B3 functional scope comes from the provider category even when a technical keyword is more frequent",
                result.Keywords[0].Keyword == "Other" && result.Keywords[0].ReviewedProteins == 3
                && result.ProposedScopeTerms.SequenceEqual(new[] { "Leading scope" }));
            foreach (var protein in file.MyExtendedFastaItems) protein.Evidence!.Keywords.Clear();
            using var emptyHandler = new TaskTaxonomyHandler();
            result = EvidenceTaskDiscovery.DiscoverAsync(file, emptyHandler).GetAwaiter().GetResult();
            Check("B3 absent keyword facts produce no invented scope", result.ProposedScopeTerms.Count == 0 && result.Keywords.Count == 0);
            using var oldCulture = new TaskCultureScope("pt-BR");
            using var localeHandler = new TaskTaxonomyHandler();
            Check("B3 discovery is culture independent", JsonSerializer.Serialize(result)
                == JsonSerializer.Serialize(EvidenceTaskDiscovery.DiscoverAsync(file, localeHandler).GetAwaiter().GetResult()));
        });
        Verify("B3 detects a changed source during discovery", () =>
        {
            var file = TaskFile(); using var handler = new TaskTaxonomyHandler { DuringRequest = () => file.MyExtendedFastaItems[0].Description += " changed" };
            bool refused = false;
            try { EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult(); } catch (InvalidDataException) { refused = true; }
            Check("B3 a changed header cannot acquire a stale task", refused);
        });
        foreach (string damage in new[] { "header", "accession", "release", "changed release", "wrong id", "cancel" })
            Verify("B3 refuses " + damage + " without changing the file", () =>
            {
                var file = TaskFile(); using var handler = new TaskTaxonomyHandler(); using var cancel = new CancellationTokenSource();
                if (damage == "header") file.MyExtendedFastaItems[0].Description = "No taxonomy here";
                if (damage == "accession") file.MyExtendedFastaItems[0].SequenceIdentifier = "local_entry";
                handler.MissingRelease = damage == "release"; handler.ChangedRelease = damage == "changed release";
                handler.WrongId = damage == "wrong id"; if (damage == "cancel") handler.Cancel = cancel;
                string before = file.ToJsonText(); bool refused = false;
                try { EvidenceTaskDiscovery.DiscoverAsync(file, handler, cancellationToken: cancel.Token).GetAwaiter().GetResult(); }
                catch (Exception ex) when (ex is InvalidDataException || ex is OperationCanceledException) { refused = true; }
                Check("B3 " + damage + " is an atomic refusal", refused && before == file.ToJsonText());
            });
    }

    private sealed class TaskCultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;
        internal TaskCultureScope(string culture) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        public void Dispose() => CultureInfo.CurrentCulture = previous;
    }

    private static int EvidenceTaskCheck(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new ArgumentException("Use --evidence-task-check <evidence.fcf> <new-report.json>.");
            if (File.Exists(args[2])) throw new IOException("Use a new report path.");
            var file = FamilyClusterFile.Load(args[1]);
            var result = EvidenceTaskDiscovery.DiscoverAsync(file).GetAwaiter().GetResult();
            string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(args[2], json);
            Console.WriteLine("Clade: " + result.Clade.ScientificName + " (" + result.Clade.TaxonId.ToString(CultureInfo.InvariantCulture)
                + "), release " + result.Release + ", taxa " + result.Taxa.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("Reviewed accession denominator: " + result.ReviewedProteins.ToString(CultureInfo.InvariantCulture)
                + "; ignored contaminants: " + result.IgnoredContaminants.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("Proposed functional scope: " + string.Join("; ", result.ProposedScopeTerms));
            foreach (var keyword in result.Keywords.Take(10)) Console.WriteLine(keyword.Keyword + ": " + keyword.ReviewedProteins.ToString(CultureInfo.InvariantCulture));
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex.GetBaseException()); return 1; }
    }
}
