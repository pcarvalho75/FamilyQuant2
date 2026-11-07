using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void TaxaEntryCountTests()
    {
        var file = new FamilyClusterFile
        {
            MyExtendedFastaItems = new List<FastaItemExtended>
            {
                TaskProtein("sp|P00001|FIRST", 41, false),
                TaskProtein("tr|P00002|SECOND", 41, true),
                TaskProtein("tr|P00003|THIRD", 41, true),
                TaskProtein("sp|P00004|FOURTH", 21, false),
                TaskProtein("tr|P00005|FIFTH", 21, true),
                TaskProtein("tr|P00006|SIXTH", 31, true),
                TaskProtein("contaminant_sp|P00998|IGNORED", 99, true),
                TaskProtein("Reverse_sp|P00999|IGNORED", 98, true),
            },
        };
        string before = file.ToJsonText();
        using var handler = new TaskTaxonomyHandler();
        var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
        var largest = proposal.Taxa.Single(t => t.TaxonId == 41);
        var smaller = proposal.Taxa.Single(t => t.TaxonId == 21);
        Check("Z12b1 counts target FASTA records by OX and prefix independently of fetched reviewed status",
            largest.Entries == 3 && largest.MarkedSp == 1 && largest.MarkedTr == 2
            && smaller.Entries == 2 && smaller.MarkedSp == 1 && smaller.MarkedTr == 1
            && proposal.Taxa.Single(t => t.TaxonId == 31) is { Entries: 1, MarkedSp: 0, MarkedTr: 1 }
            && proposal.TargetProteins == 6 && proposal.ReviewedProteins == 4 && proposal.IgnoredContaminants == 1
            && proposal.Taxa.Count == 3 && file.ToJsonText() == before);
        var session = new EvidenceGroupingSession(file, "fixture", discoverTask: (_, _) => Task.FromResult(proposal));
        var facts = session.DiscoverTaskAsync().GetAwaiter().GetResult();
        Check("Z12b1 presentation sorts taxa by entries and supplies matching row totals",
            facts.Taxa.Select(t => t.TaxonomyId).SequenceEqual(new[] { 41, 21, 31 })
            && facts.Taxa[0] is { Entries: 3, MarkedSp: 1, MarkedTr: 2 }
            && facts.TaxaTotals == "Total entries 6 | FASTA sp 2 | FASTA tr 4");
        file.MyExtendedFastaItems[3].Description += " : tr|P00888|ALIAS Fixture OS=Joined label OX=31 : tr|P00889|ALIAS Fixture OS=Repeated label OX=21";
        using var joinedHandler = new TaskTaxonomyHandler();
        var joined = EvidenceTaskDiscovery.DiscoverAsync(file, joinedHandler).GetAwaiter().GetResult();
        Check("Z12b1 joined headers count a record once per distinct taxon using its FASTA prefix",
            joined.Taxa.Single(t => t.TaxonId == 21) is { Entries: 2, MarkedSp: 1, MarkedTr: 1 }
            && joined.Taxa.Single(t => t.TaxonId == 31) is { Entries: 2, MarkedSp: 1, MarkedTr: 1 }
            && joined.TargetProteins == 6 && joined.Taxa.Sum(t => t.Entries) == 7);
        const string legacy = "{\"TaxonId\":21,\"ScientificName\":\"Fixture label\",\"Rank\":\"species\"}";
        var old = JsonSerializer.Deserialize<EvidenceTaxon>(legacy)!;
        Check("Z12b1 old taxon JSON retains its exact representation and leaves unavailable counts empty",
            old.Entries == null && old.MarkedSp == null && old.MarkedTr == null && JsonSerializer.Serialize(old) == legacy);
        var oldFacts = facts with { Taxa = new[] { new ReviewTaskTaxon("Fixture label", 21) } };
        Check("Z12b1 older supplied facts identify unavailable totals", oldFacts.TaxaTotals == "Taxa totals unavailable");
    }
}
