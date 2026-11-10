using FamilyQuant.Evidence;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ReviewedHeaderCountTests()
    {
        EnsureApplication();
        var file = new FamilyClusterFile
        {
            EvidenceSource = new EvidenceProvenance { Provider = "UniProt", Release = "evidence-release" },
            MyExtendedFastaItems = new()
            {
                TaskProtein("sp|P00001|STABLE", 21, true), TaskProtein("tr|P00002|PROMOTED", 21, true),
                TaskProtein("sp|P00003|DEMOTED", 21, false), TaskProtein("tr|P00004|MERGED", 21, true),
                TaskProtein("contaminant_sp|P00998|IGNORED", 99, true), TaskProtein("Reverse_sp|P00999|IGNORED", 98, true),
            },
        };
        foreach (var protein in file.MyExtendedFastaItems) protein.Evidence!.Source!.Release = "evidence-release";
        file.MyExtendedFastaItems[3].Evidence!.Accession = "P00001";
        string before = file.ToJsonText();
        using var handler = new TaskTaxonomyHandler { Release = "taxonomy-release" };
        var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
        Check("Z12a2 prefix counts and the existing reviewed-accession count have independent definitions",
            proposal.ReviewedProteins == 2 && proposal.FastaSpEntries == 2
            && proposal.FastaTrNowReviewed!.SequenceEqual(new[] { "P00002" })
            && proposal.FastaSpNowUnreviewed!.SequenceEqual(new[] { "P00003" }) && file.ToJsonText() == before);
        Check("Z12a2 a merged unresolved tr header contributes to neither reviewed count or mismatch direction",
            !proposal.FastaTrNowReviewed!.Contains("P00004") && !proposal.FastaSpNowUnreviewed!.Contains("P00004")
            && proposal.ReviewedProteins == 2 && proposal.FastaSpEntries == 2);
        var session = new EvidenceGroupingSession(file, "fixture", discoverTask: (_, _) => Task.FromResult(proposal));
        var facts = session.DiscoverTaskAsync().GetAwaiter().GetResult();
        Check("Z12a2 the reviewed label uses stored evidence release rather than later taxonomy",
            facts.ReviewedCountText == "Reviewed in UniProt (release evidence-release): 2"
            && facts.FastaSpCountText == "Marked sp in the FASTA: 2");
        Check("Z12a2 tooltip lists accessions in both resolved status directions", facts.ReviewDifferenceTooltip
            == "tr now reviewed: P00002\nsp no longer reviewed: P00003");
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
        var window = new SuperClusterPreviewWindow(preview, null, session.Propose, evidenceMode: true, discoverTask: session.DiscoverTaskAsync);
        try
        {
            window.ShowAllOptions = true;
            window.Show(); PumpDispatcher();
            var reviewed = (TextBlock)window.FindName("UniProtReviewedCount");
            var marked = (TextBlock)window.FindName("FastaSpCount");
            Check("Z12a2 native review shows both counts beside their comparison tooltip", reviewed.Text == facts.ReviewedCountText
                && marked.Text == facts.FastaSpCountText && reviewed.ToolTip as string == facts.ReviewDifferenceTooltip
                && marked.ToolTip as string == facts.ReviewDifferenceTooltip);
        }
        finally { window.Close(); PumpDispatcher(); }
        var isoform = TaskProtein("sp|P00001-2|ISOFORM", 21, true);
        isoform.Evidence!.Accession = "P00001"; isoform.Evidence.Source!.Release = "evidence-release";
        var unresolved = TaskProtein("sp|P00007|UNRESOLVED", 21, false); unresolved.Evidence = null;
        file.MyExtendedFastaItems.Add(isoform); file.MyExtendedFastaItems.Add(unresolved);
        using var repeated = new TaskTaxonomyHandler { Release = "taxonomy-release" };
        var extra = EvidenceTaskDiscovery.DiscoverAsync(file, repeated).GetAwaiter().GetResult();
        Check("Z12a2 repeated isoform and unresolved sp headers remain explicit raw header counts",
            extra.ReviewedProteins == 2 && extra.FastaSpEntries == 4
            && extra.FastaSpUnresolved!.SequenceEqual(new[] { "P00007" }) && extra.RepeatedSpAccessions!.SequenceEqual(new[] { "P00001" }));
        Check("Z12a2 Help defines fetch status, FASTA headers and unresolved merged tr entries",
            new[] { false, true }.All(panel => EvidenceHelp.Build(panelMode: panel).Contains(EvidenceHelp.ReviewCountHelp, StringComparison.Ordinal)));
        string legacy = JsonSerializer.Serialize(new EvidenceTaskProposal());
        Check("Z12a2 absent legacy comparison metadata is omitted from saved task JSON",
            !legacy.Contains("FastaSpEntries", StringComparison.Ordinal) && !legacy.Contains("ReviewStatusReleases", StringComparison.Ordinal));
    }
}
