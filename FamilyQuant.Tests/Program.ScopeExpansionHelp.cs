using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ScopeExpansionHelpTests()
    {
        foreach (bool panel in new[] { false, true })
        {
            string help = EvidenceHelp.Build(panelMode: panel);
            Check("Z12c4 " + (panel ? "panel" : "evidence") + " Help explains scope expansion and retained families",
                help.Contains(EvidenceHelp.ScopeExpansionHelp, StringComparison.Ordinal)
                && help.Contains("Add to scope", StringComparison.Ordinal) && help.Contains("Update proposal", StringComparison.Ordinal)
                && help.Contains("Group by words >", StringComparison.Ordinal) && help.Contains("Group by keywords", StringComparison.Ordinal)
                && help.Contains("Quantify uses those current", StringComparison.Ordinal));
        }
        var chosen = TaskProtein("sp|P00011|CHOSEN", 21, true, "Chosen scope");
        chosen.UserID = chosen.USearchID = 11; chosen.Sequence = "ACDEFGHIK"; chosen.Evidence!.Family = "Fixture chosen family";
        var outside = TaskProtein("tr|P00012|OUTSIDE", 21, false);
        outside.UserID = outside.USearchID = 22; outside.Sequence = "LMNPQRSTV"; outside.Evidence!.Family = "Fixture other family";
        var file = new FamilyClusterFile
        {
            MyExtendedFastaItems = new() { chosen, outside },
            EvidenceSource = new EvidenceProvenance { Provider = "UniProt", Release = "fixture" },
        };
        var proposal = EvidenceClassifier.Propose(file.MyExtendedFastaItems, new EvidenceClassificationOptions { ScopeTerms = new[] { "chosen scope" } });
        var applied = EvidenceApply.Apply(file, proposal, new EvidenceApplySelection
        { SourceFingerprint = proposal.SourceFingerprint, SelectedProteinIndices = new[] { 0 } }, "fixture");
        Check("Z12c4 a protein left out of evidence Apply keeps its step 1 family", applied.Changed
            && chosen.UserID != 11 && outside.UserID == 22 && outside.USearchID == 22);
        var rows = new List<QuantRow> { Qr("identified", "fixture-run", 1, 2, "LMNPQRSTV", 100, 200) };
        var attribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(rows, file);
        Check("Z12c4 Quantify attribution includes the retained family of a left-out protein",
            attribution.IdentifiedBySequence.Values.Any(families => families.SequenceEqual(new[] { 22 })));
        var manual = new List<FastaItemExtended>
        {
            new() { SequenceIdentifier = "sp|P00021|MARKER", Description = "Manual phrase", Sequence = "ACDEFGHIK", UserID = 3, USearchID = 3 },
            new() { SequenceIdentifier = "sp|P00022|OTHER", Description = "Other wording", Sequence = "LMNPQRSTV", UserID = 3, USearchID = 3 },
        };
        var review = new SuperClusterPreview(manual, null, "\"Manual phrase\"", "", KeywordMatch.All, null, null);
        review.UntickWithoutKeyword();
        bool available = review.CanCreate;
        review.Join(manual);
        Check("Z12c4 Group by keywords can form a family from reviewed partial-family ticks", available
            && manual[0].UserID != 3 && manual[1].UserID == 3 && manual.All(p => p.USearchID == 3));
    }
}
