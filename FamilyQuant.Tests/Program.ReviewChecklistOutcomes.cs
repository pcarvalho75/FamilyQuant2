using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReviewChecklistOutcomeCommands() =>
    [new("--test-review-checklist-outcomes", 74, TestCommandPhase.Raw, args => args.Contains("--test-review-checklist-outcomes", StringComparer.Ordinal), (_, _) =>
    {
        ReviewChecklistOutcomeTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];

    private static void ReviewChecklistOutcomeTests()
    {
        FamilyPanelFamily Family(string key, params string[] domains) => new() { Key = key, DisplayName = "User row " + key,
            Pfam = domains.Select(domain => new FamilyPanelDomain { Accession = domain }).ToList() };
        FastaItemExtended Protein(int index, bool scope, string domain) => new()
        {
            SequenceIdentifier = "sp|P6500" + index + "|EXAMPLE", Description = "Example protein " + index,
            Sequence = "ACDEFGHIK", UserID = index + 1, USearchID = index + 1,
            Evidence = new() { Accession = "P6500" + index, Family = scope ? "Example source family" : "", Reviewed = scope,
                Keywords = scope ? new() { "selected" } : new(), Pfam = new() { new() { Accession = domain } } },
        };
        var file = new FamilyClusterFile { FamilyPanel = new() { Families = new()
        {
            Family("outside", "PF90301"), Family("broad", "PF90302", "PF90303"), Family("complete", "PF90302"),
        } }, MyExtendedFastaItems = new()
        { Protein(0, false, "PF90301"), Protein(1, false, "PF90301"), Protein(2, true, "PF90302") } };
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
        string before = file.ToJsonText();
        var proposal = PanelEvidenceClassifier.Propose(file, options); string proposed = JsonSerializer.Serialize(proposal);
        var model = new EvidenceReviewViewModel(EvidenceProposalPresentation.Present(proposal, file));
        var outside = model.PanelChecklist.Single(row => row.Key == "outside");
        var losing = model.PanelChecklist.Single(row => row.Key == "broad");
        Check("RG6b outside-only Pfam matches name their scope and database count without a choice", outside.Matches == 2 && outside.Placed == 0
            && outside.State == PanelChecklistState.NoProteinInScope && outside.StateText == "No protein in the scope (2 in the database)"
            && !outside.NeedsLinkChoice);
        Check("RG6b losing row names the actual placement in the more complete row", losing.Matches == 1 && losing.Placed == 0
            && losing.State == PanelChecklistState.AssignedElsewhere && losing.AssignedElsewhere == 1
            && losing.Proteins.Single().ProposedFamily == "complete" && losing.StateText == "Proteins went to another row");
        Check("RG6b complete list totals do not invent a question while Apply is available", model.CanConfirm
            && proposal.PanelContext!.Links.All(link => !link.NeedsConfirmation)
            && model.PanelChecklistSummary == "Your list: 3 families, 1 with proteins, 0 waiting for a choice in Panel links, 0 with no Pfam code, 0 matching no database protein, 1 with no protein in the scope, 1 with proteins in another row, 0 with no proteins placed.");
        Check("RG6b reading state metadata leaves the proposal and source data unchanged", file.ToJsonText() == before && JsonSerializer.Serialize(proposal) == proposed);
        file.FamilyPanel!.Families.Single(row => row.Key == "broad").Pfam.RemoveAt(1);
        var tied = new EvidenceReviewViewModel(EvidenceProposalPresentation.Present(PanelEvidenceClassifier.Propose(file, options), file));
        var pending = tied.PanelChecklist.Single(row => row.Key == "broad"); tied.SelectedChecklistFamily = pending;
        Check("RG6b waiting requires a real pending link and names where to resolve it", pending.State == PanelChecklistState.NeedsChoice
            && pending.NeedsLinkChoice && pending.StateText == "Waiting for a choice in Panel links"
            && tied.ChecklistDetail.Contains("Resolve candidate links in Panel links", StringComparison.Ordinal) && !tied.CanConfirm);
    }
}
