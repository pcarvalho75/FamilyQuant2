using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReviewNamedDestinationCommands() =>
    [new("--test-review-named-destinations", 74, TestCommandPhase.Raw, args => args.Contains("--test-review-named-destinations", StringComparer.Ordinal), (_, _) =>
    {
        ReviewNamedDestinationTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];

    private static void ReviewNamedDestinationTests()
    {
        FastaItemExtended Protein(int index, string family, bool scope, string prefix = "") => new()
        {
            SequenceIdentifier = prefix + "tr|P6400" + index + "|EXAMPLE", Description = "Example protein " + index,
            Sequence = "ACDEFGHIK", UserID = index + 1, USearchID = index + 1,
            Evidence = new() { Accession = "P6400" + index, Family = family, Reviewed = false,
                Keywords = scope ? new() { "selected" } : new() },
        };
        var file = new FamilyClusterFile { FamilyPanel = new FamilyPanel { Families = new()
        {
            new() { Key = "N", DisplayName = "User named family", UniProtFamilies = new() { "Example source family" } },
        } }, MyExtendedFastaItems = new()
        {
            Protein(0, "Example source family", false), Protein(1, "Example source family", true),
            Protein(2, "", true), Protein(3, "Other source family", false),
            Protein(4, "Example source family", false, "contaminant_"), Protein(5, "Example source family", false, "Reverse_"),
        } };
        string before = file.ToJsonText();
        var proposal = PanelEvidenceClassifier.Propose(file, new() { ScopeTerms = new[] { "selected" } });
        var named = proposal.Proteins.Single(row => row.ProteinIndex == 0);
        Check("RG7-seam native exact-family admission retains the absence of scope words", named.Family == "N"
            && !named.HasScopeAnnotation && named.SuggestedSelected);
        var model = new EvidenceReviewViewModel(EvidenceProposalPresentation.Present(proposal, file));
        model.SelectedProtein = model.Proteins.Single(row => row.ProteinIndex == 0);
        Check("RG7-seam displayed scope-word result remains No for a placed named-row protein", model.SelectedProtein.ScopeWordsText == "No"
            && model.ProteinDetails.Contains("Scope words found: No", StringComparison.Ordinal)
            && model.SelectedProtein.Family == "N");
        Check("RG7-seam named placements count in the list and never outside scope", model.ProteinsInList == 2
            && model.InScopeProteinsOutsideList == 1 && model.ProteinsOutsideScope == 1
            && model.ProteinsInList + model.InScopeProteinsOutsideList + model.ProteinsOutsideScope == 4);
        Check("RG7-seam reading the native proposal changes no project data", file.ToJsonText() == before);
    }
}
