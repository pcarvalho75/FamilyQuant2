using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void CoherenceSourceDetailsTests()
    {
        Section("Coherence warnings - numbers and their file/panel sources");
        ProteinEvidence Evidence(string accession, bool fragment = false, int? helices = 0) => new()
        {
            Accession = accession, Reviewed = true, Family = "Invented family", Fragment = fragment,
            TransmembraneCount = helices, Pfam = new List<EvidenceDomain> { new() { Accession = "PF-invented", Name = "Invented domain" } }
        };
        string Sequence(int length, int cysteines) => new string('C', cysteines) + new string('A', length - cysteines);
        var file = new FamilyClusterFile();
        for (int i = 0; i < 3; i++) file.MyExtendedFastaItems.Add(new FastaItemExtended
        {
            SequenceIdentifier = "sp|P1000" + (i + 1) + "|invented_file", Sequence = Sequence(100 + i, 8 + i),
            Description = "Invented reviewed protein OS=Invented species " + (i % 2 == 0 ? "A" : "B") + " OX=" + (100 + i % 2),
            Evidence = Evidence("P1000" + (i + 1), helices: i == 2 ? null : 0)
        });
        file.MyExtendedFastaItems.Add(new FastaItemExtended { SequenceIdentifier = "sp|P10001-2|invented_isoform",
            Sequence = Sequence(900, 90), Evidence = Evidence("P10001-2") });
        var panel = new ReferencePanel();
        for (int i = 0; i < 7; i++) panel.Entries.Add(new PanelEntry { Accession = "P2000" + i,
            Sequence = Sequence(100 + i, 8 + i % 5), Evidence = Evidence("P2000" + i, helices: i >= 5 ? null : 0) });
        panel.Entries.Add(new PanelEntry { Accession = "P29999", Sequence = Sequence(20, 1), Evidence = Evidence("P29999", fragment: true) });
        panel.Entries.Add(new PanelEntry { Accession = "P10001", Sequence = Sequence(800, 80), Evidence = Evidence("P10001") });
        file.EvidenceSource = new EvidenceProvenance { ReferencePanel = panel };
        FamilyProfile profile = CoherenceCheck.Learn(file)["Invented family"];
        int Source(string property, string member) => (int?)(typeof(FamilyProfile).GetProperty(property)?.GetValue(profile)?.GetType()
            .GetProperty(member)?.GetValue(typeof(FamilyProfile).GetProperty(property)?.GetValue(profile))) ?? 0;
        Check("source tagging preserves file-first accession/isoform deduplication and learned ranges", profile.Members == 11 && profile.CompleteMembers == 10
            && profile.MinLength == 100 && profile.MaxLength == 106 && profile.MinCysteines == 8 && profile.MaxCysteines == 12);
        Check("complete range sources are three file and seven panel members", Source("CompleteSources", "InFile") == 3 && Source("CompleteSources", "ReferencePanel") == 7);
        Check("other populations have their own exact source splits", Source("ReviewedSources", "InFile") == 3 && Source("ReviewedSources", "ReferencePanel") == 8
            && Source("TransmembraneSources", "InFile") == 2 && Source("TransmembraneSources", "ReferencePanel") == 5
            && Source("DomainCallSources", "InFile") == 3 && Source("DomainCallSources", "ReferencePanel") == 7);
        Check("reusable roster matches profile counts and keeps panel species unknown", profile.ReviewedMembers.Count == 11
            && profile.ReviewedMembers.Count(m => m.IsComplete) == 10
            && profile.ReviewedMembers.Count(m => m.Source == CoherenceMemberSource.File) == 3
            && profile.ReviewedMembers.Count(m => m.Source == CoherenceMemberSource.ReferencePanel) == 8
            && profile.ReviewedMembers.First(m => m.Accession == "P10001").Species == "Invented species A"
            && profile.ReviewedMembers.First(m => m.Accession == "P10001").TaxonomyId == 100
            && profile.ReviewedMembers.Where(m => m.Source == CoherenceMemberSource.ReferencePanel).All(m => m.Species == null));
        var high = new FastaItemExtended { SequenceIdentifier = "invented_query", Sequence = Sequence(130, 18), Evidence = Evidence("invented_query", helices: 2) };
        high.Evidence.Pfam.Add(new EvidenceDomain { Accession = "PF-new", Name = "New invented domain" });
        var low = new FastaItemExtended { SequenceIdentifier = "invented_short", Sequence = Sequence(50, 2), Evidence = Evidence("invented_short") };
        var flags = CoherenceCheck.Check(profile, high, minimumMembers: 1);
        var lower = CoherenceCheck.Check(profile, low, minimumMembers: 1);
        Check("every coherence flag names file and all-species panel sources", flags.Concat(lower).Count() == 6
            && flags.Concat(lower).All(f => f.Message.Contains("in your file", StringComparison.Ordinal)
                && f.Message.Contains("from the reference panel", StringComparison.Ordinal) && f.Message.Contains("all species", StringComparison.Ordinal)));
        string cysteines = flags.Single(f => f.Code == CoherenceCheck.CysteinesAbove).Message;
        Check("cysteine warning gives the measured value, complete range and correct source counts", cysteines.Contains("18 cysteines", StringComparison.Ordinal)
            && cysteines.Contains("8 to 12", StringComparison.Ordinal) && cysteines.Contains("3 in your file, 7 from the reference panel", StringComparison.Ordinal));
        Check("helix and domain warnings explain their different populations", flags.Single(f => f.Code == CoherenceCheck.Transmembrane).Message.Contains("2 in your file, 5 from the reference panel", StringComparison.Ordinal)
            && flags.Single(f => f.Code == CoherenceCheck.Domain).Message.Contains("3 in your file, 8 from the reference panel", StringComparison.Ordinal));
        var input = ReviewLayoutFixture();
        var row = input.Proteins[0] with { Flags = new[] { CoherenceCheck.CysteinesAbove + ": " + cysteines } };
        var view = new EvidenceReviewViewModel(input with { Proteins = input.Proteins.Select(p => p.ProteinIndex == 0 ? row : p).ToArray() });
        Check("the same source numbers reach the full warning tooltip and Help defines them", view.Proteins[0].FlagsHelp.Contains(cysteines, StringComparison.Ordinal)
            && EvidenceReviewTerms.Text.Contains("each accession once", StringComparison.Ordinal));
        CoherenceCheckTests();
    }
}
