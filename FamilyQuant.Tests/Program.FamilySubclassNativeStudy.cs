using FamilyQuant.Panels;
using FamilyQuant.Evidence;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FamilySubclassNativeStudyTests()
    {
        var curation = new FamilyClusterFile
        {
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "P0", UserID = 7, Sequence = "AAAAAAAKCCCCCCCK", Evidence = new() { Reviewed = true, Pfam = new() { new() { Accession = "domain-a" } } } },
                new() { SequenceIdentifier = "P1", UserID = 7, Sequence = "AAAAAAAKDDDDDDDK", Evidence = new() { Reviewed = true, Pfam = new() { new() { Accession = "domain-b" } } } },
                new() { SequenceIdentifier = "P2", UserID = 7, Sequence = "EEEEEEEK", Evidence = new() { Reviewed = true } },
            },
            ProteinSubclassLabels = new()
            {
                new() { ProteinIndex = 0, Identifier = "P0", FamilyUserId = 7, PanelFamilyKey = "F", SubclassKey = "a", Status = ProteinSubclassStatus.Assigned },
                new() { ProteinIndex = 1, Identifier = "P1", FamilyUserId = 7, PanelFamilyKey = "F", SubclassKey = "b", Status = ProteinSubclassStatus.Assigned },
                new() { ProteinIndex = 2, Identifier = "P2", FamilyUserId = 7, PanelFamilyKey = "F", Status = ProteinSubclassStatus.Unknown },
            },
            FamilyPanel = new() { Families = new() { new() { Key = "F", Subclasses = new()
                { new() { Key = "a", DisplayName = "Readable alpha", RequiredPfam = new() { "domain-a" } },
                  new() { Key = "b", RequiredPfam = new() { "domain-b" } } } } } },
        };
        curation.PanelProteinAssignments = curation.MyExtendedFastaItems.Select((p, i) => new PanelProteinAssignment
            { ProteinIndex = i, Identifier = p.SequenceIdentifier, FamilyUserId = p.UserID, PanelFamilyKey = "F", IsApplied = true }).ToList();
        MakePanelAssignmentsCurrent(curation, new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } });
        SubclassAssignment.CommitCandidate(curation, SubclassAssignment.ComputeCurrent(curation));
        var rows = new List<QuantRow>
        {
            Qr("identified", "run", 1, 2, "AAAAAAAK", 10, 10),
            Qr("identified", "run", 2, 2, "CCCCCCCK", 20, 20),
            Qr("identified", "run", 3, 2, "EEEEEEEK", 30, 30),
        };
        CurationHomologyHits hits = FamilyEnvelopeQuant.SearchCuration(rows, curation);
        PeptideFamilyAttribution map = FamilyEnvelopeQuant.AttributeHits(hits, curation);
        FamilyPeptideAttribution assigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), map);
        var manifest = new QuantManifest();
        bool computed = FamilyCompositionStudy.StoreSubclassesFromCuration(manifest, assigned, hits, curation);
        var parts = manifest.FamilySubclassParts?.Families.SingleOrDefault(f => f.FamilyUserId == 7);
        Check("production subclass adapter consumes every mapped protein and explicit unknown labels", computed
            && parts?.Parts.SingleOrDefault(p => p.PartKey == "a")?.Signal == 20
            && parts.Parts.Single(p => p.IsUnresolved).Signal == 40 && parts.FamilySignal == 60);
        Check("subclass labels shown in results come from the user's own panel",
            parts?.Parts.SingleOrDefault(p => p.PartKey == "a")?.Label == "Readable alpha");
        curation.ProteinSubclassLabels[0].ProteinIndex = 99;
        bool staleIndex = FamilyCompositionStudy.StoreSubclassesFromCuration(manifest, assigned, hits, curation);
        Check("stale protein-index labels cannot establish subclass specificity", !staleIndex && manifest.FamilySubclassParts == null);
        curation.ProteinSubclassLabels[0].ProteinIndex = 0;
        curation.ProteinSubclassLabels[0].FamilyUserId = 99;
        bool staleFamily = FamilyCompositionStudy.StoreSubclassesFromCuration(manifest, assigned, hits, curation);
        Check("stale family labels cannot establish subclass specificity", !staleFamily && manifest.FamilySubclassParts == null);
        curation.ProteinSubclassLabels[0].FamilyUserId = 7;
        curation.ProteinSubclassLabels[1].PanelFamilyKey = "another panel family";
        bool wrongPanel = FamilyCompositionStudy.StoreSubclassesFromCuration(manifest, assigned, hits, curation);
        Check("family-local subclass keys from incompatible panel families are never conflated", !wrongPanel && manifest.FamilySubclassParts == null);
        curation.ProteinSubclassLabels = null;
        manifest = new QuantManifest();
        Check("never-computed protein labels leave the optional subclass study absent",
            !FamilyCompositionStudy.StoreSubclassesFromCuration(manifest, assigned, hits, curation)
            && manifest.FamilySubclassParts == null);
    }
}
