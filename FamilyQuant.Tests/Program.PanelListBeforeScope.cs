using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelListBeforeScopeCommands() =>
    [
        new("--test-panel-list-before-scope", 74, TestCommandPhase.Raw, a => a.Contains("--test-panel-list-before-scope", StringComparer.Ordinal), (_, _) =>
        {
            PanelListBeforeScopeTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    /// <summary>GA2 (D119): a row that names a UniProt family accepts, through a qualifying neighbour, a protein with no
    /// UniProt family of its own whose whole Pfam set it lists and which is its best row; a different own family is refused.</summary>
    private static void PanelListBeforeScopeTests()
    {
        static FastaItemExtended Protein(string id, string source, bool words, string sequence, params string[] domains) => new()
        {
            SequenceIdentifier = id, Sequence = sequence, UserID = 1, USearchID = 1,
            Evidence = new() { Accession = NearestReviewed.EntryAccession(id), Family = source, Reviewed = id.StartsWith("sp|", StringComparison.Ordinal),
                Keywords = words ? new() { "selected" } : new() { "other" }, Pfam = domains.Select(d => new EvidenceDomain { Accession = d }).ToList() },
        };
        const string near = "ACDEFGHIKLMNPQRSTVWY", far = "WWWWWWWWWWWWWWWWWWWW";
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
        var file = new FamilyClusterFile
        {
            FamilyPanel = new() { Families = new()
            {
                new() { Key = "named", DisplayName = "User named row", UniProtFamilies = new() { "Source alpha family" },
                    Pfam = new[] { "domain-a", "domain-b", "domain-c", "domain-d" }.Select(d => new FamilyPanelDomain { Accession = d }).ToList() },
                new() { Key = "single", DisplayName = "User single row", Pfam = new() { new() { Accession = "domain-c" } } },
            } },
            MyExtendedFastaItems = new()
            {
                Protein("sp|P53001|SYNTHETIC", "Source alpha family", true, near, "domain-a", "domain-b", "domain-c"),
                Protein("tr|P53002|SYNTHETIC", "", true, near, "domain-a", "domain-b", "domain-c"),
                Protein("tr|P53003|SYNTHETIC", "", false, near, "domain-a", "domain-b", "domain-c"),
                Protein("tr|P53004|SYNTHETIC", "Source beta family", false, near, "domain-a", "domain-b", "domain-c"),
                Protein("tr|P53005|SYNTHETIC", "", true, near, "domain-a", "domain-x"),
                Protein("tr|P53006|SYNTHETIC", "", true, near, "domain-c"),
                Protein("tr|P53007|SYNTHETIC", "", true, far, "domain-a", "domain-b", "domain-c"),
            },
            EvidenceRecipe = new() { Options = options },
        };
        var references = PanelEvidenceClassifier.References(file, options);
        var search = NearestReviewed.Search(NearestReviewed.Targets(file), references);
        string unchanged = file.ToJsonText();
        var proposal = PanelEvidenceClassifier.Propose(file, options, search, references);
        EvidenceProteinProposal At(int index) => proposal.Proteins.Single(r => r.ProteinIndex == index);
        bool Refused(int index) => At(index).Family == "" && At(index).Flags.Any(f => f.Code == "panel_source_family_required");
        Check("GA2 the reviewed member with the row's family joins the named row", At(0).Family == "named");
        Check("GA2 an unreviewed protein with the same Pfam set and no family of its own joins the named row", At(1)
            is { Family: "named", Tier: EvidenceTier.ReviewedNeighbour, SuggestedSelected: true }
            && At(1).Reason.Contains("this row lists every one of its Pfam domains", StringComparison.Ordinal));
        Check("GA2 the scope words stay: without them the unannotated neighbour route still decides", At(2)
            is { Family: "named", Tier: EvidenceTier.UnannotatedNeighbour, HasScopeAnnotation: false });
        Check("GA2 a protein whose own UniProt family differs is still refused", Refused(3));
        Check("GA2 a domain the row does not list keeps the protein out", Refused(4));
        Check("GA2 the named row must be the best row by the tie rule", Refused(5));
        Check("GA2 domains alone do not place a protein without a qualifying neighbour", At(6).Family == "" && At(6).Tier == EvidenceTier.Unassigned);
        Check("GA2 asks no new link question and leaves the project unchanged", proposal.PanelContext!.Links.All(l => !l.NeedsConfirmation)
            && file.ToJsonText() == unchanged);
        Check("GA2 the facts keep the neighbour and domain evidence of the accepted protein", proposal.PanelAssignments!.Single(f => f.ProteinIndex == 1)
            is { PanelFamilyKey: "named", NeighbourAccession: "P53001" } fact
            && fact.EvidenceKind.HasFlag(PanelAssignmentEvidenceKind.NearestReviewed) && fact.ObservedPfam.Count == 3);
    }
}
