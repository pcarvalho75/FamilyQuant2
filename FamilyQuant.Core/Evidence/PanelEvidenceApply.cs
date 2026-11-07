using FamilyQuant.Panels;
using System;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Evidence;

public static partial class EvidenceApply
{
    private static void ValidatePanelOverlay(FamilyPanel? source, FamilyPanel selected, System.Collections.Generic.IReadOnlyList<EvidenceManualChoice>? manual = null)
    {
        if (source == null) throw new InvalidOperationException("Store the panel before reviewing its evidence links.");
        var before = FamilyPanelValidation.Clone(source);
        if (manual?.Count > 0) before = ManualFamilyChoices.ExtendPanel(before, manual);
        var after = FamilyPanelValidation.Clone(selected);
        before.EvidenceLinks.Clear(); after.EvidenceLinks.Clear();
        before.ConfirmedEvidenceMatches = null; after.ConfirmedEvidenceMatches = null;
        foreach (var family in before.Families) family.Subclasses.Clear();
        foreach (var family in after.Families) family.Subclasses.Clear();
        if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
            throw new InvalidOperationException("The panel definitions changed during link review. Rebuild the proposal.");
    }

    private static void ValidatePanelProposal(FamilyClusterFile file, EvidenceProposal proposal,
        EvidenceApplySelection selection, FamilyPanel? selected)
    {
        if (proposal.PanelContext == null) return;
        if (selected == null || file.FamilyPanel == null
            || proposal.PanelContext.SourcePanelFingerprint != PanelDerivedDataCurrentness.FamilyFingerprint(file.FamilyPanel)
            || proposal.PanelContext.AppliedPanelFingerprint != PanelDerivedDataCurrentness.FamilyFingerprint(selected))
            throw new InvalidOperationException("The panel proposal does not belong to these source and selected panels.");
        if (selection.ProteinOverrides.Count > 0 || selection.ArchitectureChoices.Count > 0)
            throw new ArgumentException("Panel grouping uses confirmed links. Edit its definitions to change family or subclass rules.", nameof(selection));
        ValidatePanelOverlay(file.FamilyPanel, selected, proposal.ManualChoices);
        var regenerated = PanelEvidenceClassifier.Propose(file, proposal.Options,
            selection.SessionSearch, selection.SessionReferences, selected);
        regenerated = ManualFamilyChoices.Overlay(regenerated, proposal.ManualChoices ?? Array.Empty<EvidenceManualChoice>(), file);
        if (JsonSerializer.Serialize(regenerated) != JsonSerializer.Serialize(proposal))
            throw new InvalidOperationException("The panel choices no longer reproduce this evidence proposal.");
        foreach (var choice in selected.EvidenceLinks)
        {
            bool unchanged = file.FamilyPanel!.EvidenceLinks.Any(old => JsonSerializer.Serialize(old) == JsonSerializer.Serialize(choice));
            if (unchanged) continue;
            var link = regenerated.PanelContext!.Links.FirstOrDefault(l => l.Key == PanelEvidenceClassifier.LinkKey(choice.SourceFamily, choice.ArchitectureKey));
            if (link?.IsConfirmed != true || link.SelectedFamilyKey != choice.PanelFamilyKey)
                throw new ArgumentException("A new link choice lacks current reviewed support.", nameof(selection));
        }
    }

    private static void ValidatePanelRecipe(FamilyClusterFile file, EvidenceRecipe recipe)
    {
        if (recipe.PanelContext == null) return;
        var panel = recipe.FamilyPanel ?? throw new ArgumentException("The panel recipe has no panel snapshot.", nameof(recipe));
        ValidatePanelOverlay(file.FamilyPanel, panel, recipe.ManualChoices);
        var actual = PanelEvidenceClassifier.Propose(file, recipe.Options, recipe.SessionSearch, recipe.SessionReferences, panel);
        actual = ManualFamilyChoices.Overlay(actual, recipe.ManualChoices ?? new(), file);
        if (JsonSerializer.Serialize(actual.PanelContext) != JsonSerializer.Serialize(recipe.PanelContext))
            throw new ArgumentException("The saved panel links no longer have the recorded reviewed support.", nameof(recipe));
        var rows = actual.Proteins.ToDictionary(p => p.ProteinIndex);
        foreach (var assignment in recipe.Assignments)
            if (!rows.TryGetValue(assignment.ProteinIndex, out var row) || row.Family != assignment.Family
                || row.Tier != assignment.Tier || row.Reason != assignment.Reason || !row.SuggestedSelected)
                throw new ArgumentException("A saved panel assignment is not reproduced by the recorded evidence rule.", nameof(recipe));
        var facts = PanelAssignmentEvidenceStore.Stage(actual, recipe.Assignments);
        if (JsonSerializer.Serialize(facts) != JsonSerializer.Serialize(recipe.PanelProteinAssignments)
            || PanelAssignmentEvidenceStore.Fingerprint(file, facts, recipe, SessionSource(file, recipe.SessionSearch), recipe.Assignments)
                != recipe.PanelProteinAssignmentsFingerprint)
            throw new ArgumentException("The saved per-target evidence facts do not reproduce the panel rule.", nameof(recipe));
    }
}
