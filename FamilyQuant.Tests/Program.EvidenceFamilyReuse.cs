using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static EvidenceApplyResult ApplyReuseMembers(FamilyClusterFile file, int[] indices, string? name = null)
    {
        var session = new EvidenceGroupingSession(file, "family identity fixture");
        var input = session.Propose("scope");
        var selection = new EvidenceReviewViewModel(input).CaptureSelection() with { ProteinIndices = indices };
        if (name != null) selection = selection with { Names = input.Families.ToDictionary(family => family.Family, _ => name) };
        return session.Apply(selection);
    }

    private static void EvidenceFamilyReuseTests()
    {
        Section("Z14c4 retained evidence family numbers through later Apply and replay");
        var file = ManualChoiceFile();
        ApplyReuseMembers(file, new[] { 0 });
        int retained = file.MyExtendedFastaItems[0].UserID;
        string anchor = JsonSerializer.Serialize(file.MyExtendedFastaItems[0]);
        file.MyExtendedFastaItems.Add(new FastaItemExtended
        {
            SequenceIdentifier = "tr|P10004|invented_later", Sequence = "ACDEFGHIM", UserID = 9, USearchID = 9,
            Evidence = new ProteinEvidence { Family = "Invented family", Reviewed = false, Keywords = new List<string> { "scope" } },
        });
        string before = file.ToJsonText();
        var later = ApplyReuseMembers(file, new[] { 1, 3 });
        string after = file.ToJsonText();
        Check("Z14c4 later database members join the stored evidence family number and keep its lock",
            later.Changed && file.MyExtendedFastaItems[1].UserID == retained && file.MyExtendedFastaItems[3].UserID == retained
            && file.LockedUserIds.Contains(retained) && JsonSerializer.Serialize(file.MyExtendedFastaItems[0]) == anchor
            && file.MyExtendedFastaItems[1].USearchID == 2 && file.MyExtendedFastaItems[3].USearchID == 9);
        var recipe = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(later.Recipe))!;
        later.Undo!.Restore(file);
        Check("Z14c4 one Undo restores the earlier family and recipe exactly", file.ToJsonText() == before);
        EvidenceApply.ApplyRecipe(file, recipe);
        Check("Z14c4 saved reuse recipe replays byte for byte", file.ToJsonText() == after);
        Check("Z14c4 an already applied reuse recipe is idempotent", !EvidenceApply.ApplyRecipe(file, recipe).Changed && file.ToJsonText() == after);
        var changedRecipe = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(recipe))!;
        changedRecipe.Assignments[0].FamilyId = 3;
        var unchanged = FamilyClusterFile.FromJsonText(before);
        bool refused = false;
        try { EvidenceApply.ApplyRecipe(unchanged, changedRecipe); } catch (ArgumentException) { refused = true; }
        Check("Z14c4 replay refuses an unrelated existing number atomically", refused && unchanged.ToJsonText() == before);
        changedRecipe.Assignments[0].FamilyId = 0;
        refused = false;
        try { EvidenceApply.ApplyRecipe(unchanged, changedRecipe); }
        catch (ArgumentException error) { refused = error.Message.Contains("incompatible with its source", StringComparison.Ordinal); }
        Check("Z14c4 the missing-identity sentinel cannot become a replay family number", refused && unchanged.ToJsonText() == before);
        var metadataFile = FamilyClusterFile.FromJsonText(before);
        string originalAssignments = JsonSerializer.Serialize(recipe.Assignments);
        string storedAssignments = JsonSerializer.Serialize(metadataFile.EvidenceRecipe!.Assignments);
        string editedMetadataState = "";
        var metadataProgress = new InlineOperationProgress(progress =>
        {
            if (progress.Stage != "evidence-apply" || progress.Completed != 1) return;
            var identities = metadataFile.EvidenceRecipe!.FamilyIdentities!;
            identities.Add(identities[0] with { Family = "Different supplied identity",
                GroupKey = JsonSerializer.Serialize(new[] { "Different supplied identity", "" }) });
            editedMetadataState = metadataFile.ToJsonText();
        });
        var metadataOperation = new Operations.OperationContext(progress: metadataProgress);
        refused = false;
        try { EvidenceApply.ApplyRecipe(metadataFile, recipe, metadataOperation); }
        catch (ArgumentException error) { refused = error.Message.Contains("saved family identities", StringComparison.Ordinal); }
        Check("Z14c4 metadata-only edit reaches the family identity publication guard without changing assignments",
            refused && !metadataOperation.HasCommitted && metadataFile.ToJsonText() == editedMetadataState
            && JsonSerializer.Serialize(recipe.Assignments) == originalAssignments
            && JsonSerializer.Serialize(metadataFile.EvidenceRecipe!.Assignments) == storedAssignments);

        var coincident = ManualChoiceFile();
        ApplyReuseMembers(coincident, new[] { 0 }, "Shared supplied name");
        int first = coincident.MyExtendedFastaItems[0].UserID;
        coincident.MyExtendedFastaItems[1].Evidence!.Family = "Different invented identity";
        ApplyReuseMembers(coincident, new[] { 1 }, "Shared supplied name");
        Check("Z14c4 coincident display names cannot join different evidence identities",
            coincident.MyExtendedFastaItems[1].UserID > first && coincident.MyExtendedFastaItems[0].UserID == first
            && coincident.MyExtendedFastaItems[1].UserAnnotation == coincident.MyExtendedFastaItems[0].UserAnnotation);
        var renamed = ManualChoiceFile();
        ApplyReuseMembers(renamed, new[] { 0 }, "First supplied name");
        int renamedId = renamed.MyExtendedFastaItems[0].UserID;
        ApplyReuseMembers(renamed, new[] { 1 }, "Another supplied name");
        Check("Z14c4 matching stored identity reuses its number independently of edited names",
            renamed.MyExtendedFastaItems[1].UserID == renamedId && renamed.MyExtendedFastaItems[0].UserAnnotation == "First supplied name"
            && renamed.MyExtendedFastaItems[1].UserAnnotation == "Another supplied name");

        var manual = ManualChoiceFile();
        ApplyReuseMembers(manual, new[] { 0 });
        int manualId = manual.MyExtendedFastaItems[0].UserID;
        var manualSession = new EvidenceGroupingSession(manual, "family identity fixture");
        manualSession.Propose("scope");
        var manualInput = manualSession.AddToFamily(new EvidenceManualRequest(new[] { 2 }, "Invented family", "Invented family", false, "Supplied hand choice"));
        var manualResult = manualSession.Apply(new EvidenceReviewViewModel(manualInput).CaptureSelection() with { ProteinIndices = new[] { 2 } });
        Check("Z14c4 AddedByHand joins the same retained evidence identity with explicit proof and lock",
            manual.MyExtendedFastaItems[2].UserID == manualId && manual.LockedUserIds.Contains(manualId)
            && manualResult.Recipe.Assignments.Single() is { Tier: EvidenceTier.AddedByHand, AutomaticEvidence: not null }
            && manualResult.Recipe.ManualChoices?.Single().Note == "Supplied hand choice");
        string manualAfter = manual.ToJsonText();
        manualResult.Undo!.Restore(manual);
        EvidenceApply.ApplyRecipe(manual, manualResult.Recipe);
        Check("Z14c4 hand-added reuse recipe replays its grouping and proof exactly", manual.ToJsonText() == manualAfter);

        var history = ManualChoiceFile();
        ApplyReuseMembers(history, new[] { 0 });
        int historyId = history.MyExtendedFastaItems[0].UserID;
        history.MyExtendedFastaItems[1].Evidence!.Family = "Different invented identity";
        ApplyReuseMembers(history, new[] { 1 });
        history = FamilyClusterFile.FromJsonText(history.ToJsonText());
        history.MyExtendedFastaItems[2].Evidence!.Family = "Invented family";
        history.MyExtendedFastaItems[2].Evidence!.Keywords = new List<string> { "scope" };
        ApplyReuseMembers(history, new[] { 2 });
        Check("Z14c4 an unrelated intervening Apply and project reopen retain the earlier identity",
            history.MyExtendedFastaItems[2].UserID == historyId);

        var legacy = ManualChoiceFile();
        ApplyReuseMembers(legacy, new[] { 0 });
        int legacyId = legacy.MyExtendedFastaItems[0].UserID;
        var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(legacy.ToJsonText())!;
        legacyJson["EvidenceRecipe"]!.AsObject().Remove("FamilyIdentities");
        legacy = FamilyClusterFile.FromJsonText(legacyJson.ToJsonString());
        ApplyReuseMembers(legacy, new[] { 1 });
        Check("Z14c4 legacy saved assignments supply family identity without a new snapshot",
            legacy.MyExtendedFastaItems[1].UserID == legacyId);

        var stale = ManualChoiceFile();
        ApplyReuseMembers(stale, new[] { 0 });
        int staleId = stale.MyExtendedFastaItems[0].UserID;
        stale.MyExtendedFastaItems[0].UserID = 8;
        stale.MyExtendedFastaItems[2].UserID = staleId;
        ApplyReuseMembers(stale, new[] { 1 });
        Check("Z14c4 stale identity without a retained source member cannot claim an existing number",
            stale.MyExtendedFastaItems[1].UserID > 8 && stale.MyExtendedFastaItems[2].UserID == staleId);
        Check("Z14c4 old empty recipes omit the optional identity snapshot",
            !JsonSerializer.Serialize(new EvidenceRecipe()).Contains("FamilyIdentities", StringComparison.Ordinal));

        var ambiguous = ManualChoiceFile();
        ApplyReuseMembers(ambiguous, new[] { 0 });
        var identities = ambiguous.EvidenceRecipe!.FamilyIdentities!;
        var identity = identities.Single();
        ambiguous.MyExtendedFastaItems[1].UserID = 7; ambiguous.LockedUserIds.Add(7);
        identities.Add(identity with { FamilyId = 7, Identifiers = new[] { ambiguous.MyExtendedFastaItems[1].SequenceIdentifier } });
        ambiguous.MyExtendedFastaItems[2].Evidence!.Family = "Invented family";
        ambiguous.MyExtendedFastaItems[2].Evidence!.Keywords = new List<string> { "scope" };
        ApplyReuseMembers(ambiguous, new[] { 2 });
        Check("Z14c4 an ambiguous identity in two retained families gets a fresh number",
            ambiguous.MyExtendedFastaItems[2].UserID > 7 && ambiguous.LockedUserIds.Contains(identity.FamilyId) && ambiguous.LockedUserIds.Contains(7));

        var unlocked = ManualChoiceFile();
        ApplyReuseMembers(unlocked, new[] { 0 });
        int unlockedId = unlocked.MyExtendedFastaItems[0].UserID;
        unlocked.LockedUserIds.Remove(unlockedId);
        ApplyReuseMembers(unlocked, new[] { 1 });
        Check("Z14c4 an unlocked grouping keeps the existing fresh-allocation behavior", unlocked.MyExtendedFastaItems[1].UserID > unlockedId);

        var named = ManualChoiceFile();
        foreach (int index in new[] { 0, 1 })
        {
            var namedSession = new EvidenceGroupingSession(named, "named override fixture");
            var namedInput = namedSession.Propose("scope");
            namedSession.Apply(new EvidenceReviewViewModel(namedInput).CaptureSelection() with
            {
                ProteinIndices = new[] { index },
                ProteinOverrides = new[] { new EvidenceProteinOverride { ProteinIndex = index, Family = "Invented family", Name = "Named override" } },
            });
        }
        Check("Z14c4 a named single-protein override never reuses a number from its display name",
            named.MyExtendedFastaItems[1].UserID > named.MyExtendedFastaItems[0].UserID);

        var panel = ManualChoiceFile();
        ApplyReuseMembers(panel, new[] { 0 });
        int ordinaryId = panel.MyExtendedFastaItems[0].UserID;
        panel.MyExtendedFastaItems[0].Evidence!.Reviewed = true;
        panel.MyExtendedFastaItems[0].Evidence!.Accession = "P10001";
        panel.MyExtendedFastaItems[0].Evidence!.Pfam = new List<EvidenceDomain> { new() { Accession = "PF-invented" } };
        panel.FamilyPanel = new FamilyQuant.Panels.FamilyPanel
        {
            Name = "Supplied panel", Families = new List<FamilyQuant.Panels.FamilyPanelFamily>
            {
                new() { Key = "Invented family", DisplayName = "Supplied panel family", Decision = FamilyQuant.Panels.FamilyPanelDecision.Quantify,
                    Pfam = new List<FamilyQuant.Panels.FamilyPanelDomain> { new() { Accession = "PF-invented" } } },
            },
        };
        int panelId = 0;
        foreach (int index in new[] { 2, 1 })
        {
            var panelSession = new EvidenceGroupingSession(panel, "panel identity fixture");
            panelSession.Propose("scope");
            var panelInput = panelSession.AddToFamily(new EvidenceManualRequest(new[] { index }, "Invented family", "Supplied panel family", false));
            var panelResult = panelSession.Apply(new EvidenceReviewViewModel(panelInput).CaptureSelection() with { ProteinIndices = new[] { index } });
            if (index == 2)
            {
                panelId = panel.MyExtendedFastaItems[index].UserID;
                Check("Z14c4 panel and ordinary evidence identities have separate namespaces", panelId > ordinaryId);
            }
            else
            {
                Check("Z14c4 AddedByHand panel member reuses its retained panel family number",
                    panel.MyExtendedFastaItems[index].UserID == panelId && panel.LockedUserIds.Contains(panelId)
                    && panelResult.Recipe.Assignments.Single().Tier == EvidenceTier.AddedByHand);
                string panelAfter = panel.ToJsonText();
                panelResult.Undo!.Restore(panel);
                EvidenceApply.ApplyRecipe(panel, panelResult.Recipe);
                Check("Z14c4 retained panel identity and manual facts replay byte for byte", panel.ToJsonText() == panelAfter);
            }
        }
    }
}
