using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static FamilyClusterFile ManualChoiceFile() => new()
    {
        SourceSha256 = "invented-manual-source", SourceSequenceCount = 3,
        MyExtendedFastaItems = new List<FastaItemExtended>
        {
            new() { SequenceIdentifier = "tr|P10001|invented_one", Sequence = "ACDEFGHIK", UserID = 1, USearchID = 1,
                Evidence = new ProteinEvidence { Family = "Invented family", Reviewed = false, Fragment = false, Keywords = new List<string> { "scope" } } },
            new() { SequenceIdentifier = "tr|P10002|invented_two", Sequence = "ACDEFGHIL", UserID = 2, USearchID = 2,
                Evidence = new ProteinEvidence { Family = "Invented family", Reviewed = false, Fragment = false, Keywords = new List<string> { "scope" } } },
            new() { SequenceIdentifier = "tr|P10003|invented_unassigned", Sequence = "KLMNPQRST", UserID = 3, USearchID = 3,
                Evidence = new ProteinEvidence { Reviewed = false, Fragment = false } }
        }
    };

    private static void ManualFamilyChoiceTests()
    {
        Section("Hand-added families - explicit choices, proposal updates and atomic replay");
        var file = ManualChoiceFile(); string before = file.ToJsonText();
        var session = new EvidenceGroupingSession(file, "manual fixture");
        var automatic = session.Propose("scope");
        string untouched = JsonSerializer.Serialize(session.Proposal!.Proteins[0]);
        var existing = session.AddToFamily(new EvidenceManualRequest(new[] { 2 }, "Invented family", "Invented family", false, "Curator's supplied note"));
        var added = existing.Proteins.Single(p => p.ProteinIndex == 2);
        Check("an unassigned row can explicitly join a proposed family with its note and tick", added.Family == "Invented family" && added.Tier == "Added by hand"
            && added.Reason.Contains("Curator's supplied note", StringComparison.Ordinal) && added.SuggestedSelected && added.OriginalEvidence.Contains("No family proposed", StringComparison.Ordinal));
        Check("hand overlay preserves every untouched automatic row and the file", JsonSerializer.Serialize(session.Proposal!.Proteins[0]) == untouched && file.ToJsonText() == before);
        var updated = session.Propose("scope");
        Check("Update proposal keeps the explicit choice and note", updated.ManualChoices?.Count == 1 && updated.Proteins.Single(p => p.ProteinIndex == 2).Tier == "Added by hand"
            && updated.Proteins.Single(p => p.ProteinIndex == 2).Reason.Contains("Curator's supplied note", StringComparison.Ordinal));
        var removed = session.RemoveFromFamily(new[] { 2 });
        Check("Remove restores the original automatic unassigned row", removed.Proteins.Single(p => p.ProteinIndex == 2).Family.Length == 0
            && removed.Proteins.Single(p => p.ProteinIndex == 2).Tier == "Unassigned" && file.ToJsonText() == before);
        var fresh = session.AddToFamily(new EvidenceManualRequest(new[] { 2 }, "", "Invented hand family", true, "New family note"));
        string newKey = fresh.ManualChoices!.Single().Family;
        fresh = session.AddToFamily(new EvidenceManualRequest(new[] { 0 }, newKey, "Invented hand family", false, "Second supplied note"));
        string motif = session.CountMotif("K");
        Check("manual motif output shows the user family name rather than an internal key", motif.Contains("Invented hand family\t2\t2", StringComparison.Ordinal)
            && !motif.Contains(newKey, StringComparison.Ordinal));
        var choice = new EvidenceReviewViewModel(fresh).CaptureSelection();
        var result = session.Apply(choice); string after = file.ToJsonText();
        Check("new hand family groups selected rows and records explicit tier/note/original proof", result.Changed
            && file.MyExtendedFastaItems[0].UserID == file.MyExtendedFastaItems[2].UserID && file.MyExtendedFastaItems[0].UserID != file.MyExtendedFastaItems[1].UserID
            && result.Recipe.ManualChoices?.Count == 2 && result.Recipe.Assignments.Count(a => a.Tier == EvidenceTier.AddedByHand && a.AutomaticEvidence != null) == 2
            && result.Recipe.Assignments.Any(a => a.Reason.Contains("New family note", StringComparison.Ordinal)));
        var methods = FamilyQuant.Report.ReportMethods.Build(file, new QuantManifest()).Blocks.OfType<FamilyQuant.Report.ReportTable>()
            .Single(t => t.Id == "methods-hand-families");
        Check("Methods records user choices and notes without claiming automatic evidence", methods.Rows.Count == 2
            && methods.Rows.All(r => r[3] == "Applied") && methods.Caption.Contains("not automatic evidence", StringComparison.Ordinal)
            && methods.Rows.Any(r => r[2] == "New family note") && file.ToJsonText() == after);
        var shifted = FamilyClusterFile.FromJsonText(after); shifted.MyExtendedFastaItems.RemoveAt(0);
        var stale = FamilyQuant.Report.ReportMethods.Build(shifted, new QuantManifest()).Blocks.OfType<FamilyQuant.Report.ReportTable>().Single(t => t.Id == "methods-hand-families");
        Check("Methods guards stored indices after database rows shift", stale.Rows.All(r => r[3] == "Source changed"));
        var reopened = new EvidenceGroupingSession(file, "manual fixture").Propose("scope");
        Check("saved choices reopen as protected hand choices without reticking locked members", reopened.ManualChoices?.Count == 2
            && reopened.Proteins.Where(p => p.ProteinIndex == 0 || p.ProteinIndex == 2).All(p => p.Tier == "Added by hand" && p.IsLocked && !p.SuggestedSelected));
        var recipe = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(result.Recipe))!;
        result.Undo!.Restore(file);
        Check("one Undo restores every pre-review file byte", file.ToJsonText() == before);
        EvidenceApply.ApplyRecipe(file, recipe);
        Check("saved manual recipe round trip replays byte for byte", file.ToJsonText() == after);
        var badRecipe = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(recipe))!;
        badRecipe.ManualChoices![0] = badRecipe.ManualChoices[0] with { SequenceSha256 = "wrong-sequence" };
        var untouchedFile = FamilyClusterFile.FromJsonText(before); bool staleRefused = false;
        try { EvidenceApply.ApplyRecipe(untouchedFile, badRecipe); } catch (ArgumentException) { staleRefused = true; }
        Check("manual replay refuses a different bound sequence before publication", staleRefused && untouchedFile.ToJsonText() == before);

        var locked = ManualChoiceFile(); locked.LockedUserIds.Add(3);
        var lockedSession = new EvidenceGroupingSession(locked, "manual fixture"); lockedSession.Propose("scope");
        string lockedBefore = locked.ToJsonText(), proposalBefore = JsonSerializer.Serialize(lockedSession.Proposal);
        bool refused = false;
        try { lockedSession.AddToFamily(new EvidenceManualRequest(new[] { 0, 2 }, "Invented family", "Invented family", false)); }
        catch (InvalidOperationException error) { refused = error.Message.Contains("Locked", StringComparison.Ordinal) && error.Message.Contains("Unlock", StringComparison.Ordinal); }
        Check("a mixed locked selection is refused atomically with a reason", refused && locked.ToJsonText() == lockedBefore
            && JsonSerializer.Serialize(lockedSession.Proposal) == proposalBefore);
        Check("unused manual fields preserve old JSON and enum values", !JsonSerializer.Serialize(new EvidenceRecipe()).Contains("ManualChoices", StringComparison.Ordinal)
            && !JsonSerializer.Serialize(new EvidenceProteinProposal()).Contains("AutomaticEvidence", StringComparison.Ordinal)
            && (int)EvidenceTier.UnannotatedNeighbour == 4 && (int)PanelAssignmentEvidenceKind.ConfirmedLink == 8);

        var panelFile = ManualChoiceFile();
        panelFile.FamilyPanel = new FamilyPanel { Name = "Invented panel", Families = new List<FamilyPanelFamily>
            { new() { Key = "invented-panel", DisplayName = "Invented panel family", Decision = FamilyPanelDecision.Quantify,
                Pfam = new List<FamilyPanelDomain> { new() { Accession = "PF-invented" } } } } };
        string panelBefore = panelFile.ToJsonText();
        var panelSession = new EvidenceGroupingSession(panelFile, "manual fixture"); panelSession.Propose("scope");
        var panelInput = panelSession.AddToFamily(new EvidenceManualRequest(new[] { 2 }, "", "Invented manual panel family", true, "Explicit panel note"));
        var appliedPanel = panelSession.Apply(new EvidenceReviewViewModel(panelInput).CaptureSelection());
        string panelAfter = panelFile.ToJsonText();
        Check("panel manual family is a declared quantified user definition, not inferred evidence", panelFile.FamilyPanel.Families.Any(f => f.DisplayName == "Invented manual panel family"
            && f.Decision == FamilyPanelDecision.Quantify && f.Pfam.Count == 0)
            && PanelAssignmentEvidenceStore.Current(panelFile)?.Single(r => r.ProteinIndex == 2).EvidenceKind == PanelAssignmentEvidenceKind.AddedByHand);
        appliedPanel.Undo!.Restore(panelFile);
        Check("panel Undo restores definitions as well as grouping", panelFile.ToJsonText() == panelBefore);
        EvidenceApply.ApplyRecipe(panelFile, JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(appliedPanel.Recipe))!);
        Check("panel manual recipe reproduces its declared definitions and facts", panelFile.ToJsonText() == panelAfter);
    }
}
