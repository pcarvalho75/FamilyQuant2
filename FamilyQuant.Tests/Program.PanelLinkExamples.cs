using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelLinkExampleCommands() =>
    [new("--test-panel-link-examples", 74, TestCommandPhase.Raw, args => args.Contains("--test-panel-link-examples", StringComparer.Ordinal), (_, _) =>
    {
        PanelLinkExampleTests(); Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];
    private static void PanelLinkExampleTests()
    {
        var proteins = Enumerable.Range(0, 3).Select(i => new FastaItemExtended { SequenceIdentifier = "sp|P1000" + i + "|ENTRY",
            Description = "Example protein " + i + " OS=hidden metadata", Sequence = "PEPTIDE", UserID = 1,
            Evidence = new ProteinEvidence { Family = "Example family", Reviewed = i > 0,
                Pfam = new() { new() { Accession = "PF12345" } }, Keywords = i == 2 ? new() { "Supplied annotation" } : new() } }).ToList();
        string architecture = PanelEvidenceClassifier.ArchitectureKey(proteins[0].Evidence);
        string key = PanelEvidenceClassifier.LinkKey("Example family", architecture);
        var proposal = new EvidenceProposal { SourceFingerprint = EvidenceClassifier.Fingerprint(proteins),
            Families = new[] { new EvidenceFamilyProposal { Family = "a", SuggestedName = "Family a", Proteins = 3 } },
            Proteins = proteins.Select((p, i) => new EvidenceProteinProposal { ProteinIndex = i, Identifier = p.SequenceIdentifier, Family = "a",
                Tier = i == 2 ? EvidenceTier.UniProtFamily : EvidenceTier.ReviewedNeighbour }).ToArray(),
            PanelContext = new() { Links = new[] { new PanelFamilyLinkProposal { Key = key, SourceFamily = "Example family", ArchitectureKey = architecture,
                SupportingAccessions = new[] { "P10001", "P10000", "P10002" }, CandidateFamilyKeys = new[] { "a", "b" } } } } };
        var input = EvidenceProposalPresentation.Present(proposal, proteins);
        var example = input.PanelLinkExamples![key];
        Check("RG3 reviewed evidence then annotation completeness wins independently of support order", example.Accession == "P10002" && example.Name == "Example protein 2"
            && example.UniProtFamily == "Example family");
        var row = new EvidenceReviewViewModel(input).PanelLinks.Single();
        Check("RG3 row exposes recognizable accession name and source family", row.ExampleProtein == "P10002 | Example protein 2"
            && row.ExampleUniProtFamily == "Example family" && row.Source.NeedsConfirmation && !row.HasUnappliedChoice);
        proteins[2].Evidence!.Keywords.Clear();
        var tied = EvidenceProposalPresentation.Present(new EvidenceProposal { SourceFingerprint = EvidenceClassifier.Fingerprint(proteins),
            Proteins = proposal.Proteins, Families = proposal.Families, PanelContext = proposal.PanelContext }, proteins).PanelLinkExamples![key];
        Check("RG3 equal completeness chooses ordinal accession", tied.Accession == "P10001");
        var missing = new PanelLinkReviewRow(new(), null, () => { });
        Check("RG3 missing evidence is named without inventing an example", missing.ExampleProtein == "No database example available" && missing.ExampleUniProtFamily == "Not recorded");
    }
}
