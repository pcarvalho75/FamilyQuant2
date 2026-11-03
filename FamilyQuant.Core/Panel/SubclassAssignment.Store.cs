using System;
using System.Linq;
using System.Text.Json;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;

namespace FamilyQuant.Panels;

public sealed record SubclassAssignmentApplied(GroupingSnapshot Undo);

public static partial class SubclassAssignment
{
    /// <summary>Compute only from family assignments whose panel, protein evidence and provenance are current.</summary>
    public static SubclassAssignmentCandidate ComputeCurrent(FamilyClusterFile file, OperationContext? operation = null)
    {
        PanelAssignmentEvidenceStore.RequireCurrent(file);
        var resolver = PanelEvidenceClassifier.CreateCurrentResolver(file);
        var candidate = BuildCandidateCore(file, file.EvidenceRecipe!.Options, operation, null, resolver);
        VerifyCandidate(file, candidate);
        if (!resolver.IsCurrent(file)) throw new InvalidOperationException("Panel-family evidence changed during subclass computation.");
        return candidate;
    }

    /// <summary>Publish an unchanged candidate as one undoable project and recipe update.</summary>
    public static SubclassAssignmentApplied CommitCandidate(FamilyClusterFile file, SubclassAssignmentCandidate candidate) =>
        CommitCandidate(file, candidate, null);

    public static SubclassAssignmentApplied CommitCandidate(FamilyClusterFile file, SubclassAssignmentCandidate candidate,
        OperationContext? operation)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(candidate);
        VerifyCandidate(file, candidate);
        var labels = EvidenceApply.Clone(candidate.Labels.ToList());
        string fingerprint = LabelFingerprint(candidate.SourceFingerprint, labels);
        GroupingSnapshot? undo = null;
        void Publish()
        {
            VerifyCandidate(file, candidate);
            // Preserve non-scientific recipe edits made while the candidate was being prepared.
            var recipe = EvidenceApply.Clone(file.EvidenceRecipe!);
            // The panel editor may have changed subclass rules or report text since family grouping.
            recipe.FamilyPanel = FamilyPanelValidation.Clone(file.FamilyPanel!);
            recipe.ProteinSubclassLabels = EvidenceApply.Clone(labels);
            recipe.ProteinSubclassLabelsPanelFingerprint = candidate.PanelFingerprint;
            recipe.ProteinSubclassLabelsFingerprint = fingerprint;
            undo = GroupingSnapshot.CaptureEvidence(file);
            file.ProteinSubclassLabels = labels;
            file.ProteinSubclassLabelsPanelFingerprint = candidate.PanelFingerprint;
            file.ProteinSubclassLabelsFingerprint = fingerprint;
            file.EvidenceRecipe = recipe;
        }
        if (operation == null) Publish(); else operation.Commit(Publish);
        return new SubclassAssignmentApplied(undo!);
    }

    /// <summary>False when panel, evidence, family assignments, recipe or saved label contents have changed.</summary>
    public static bool IsCurrent(FamilyClusterFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (PanelAssignmentEvidenceStore.Current(file) == null || file.FamilyPanel == null || file.EvidenceRecipe == null
            || file.ProteinSubclassLabels == null || file.EvidenceRecipe.ProteinSubclassLabels == null) return false;
        string panelFingerprint = PanelDerivedDataCurrentness.Fingerprint(file.FamilyPanel);
        if (file.ProteinSubclassLabelsPanelFingerprint != panelFingerprint
            || file.EvidenceRecipe.ProteinSubclassLabelsPanelFingerprint != panelFingerprint
            || file.ProteinSubclassLabelsFingerprint == null
            || file.EvidenceRecipe.ProteinSubclassLabelsFingerprint != file.ProteinSubclassLabelsFingerprint
            || JsonSerializer.Serialize(file.ProteinSubclassLabels) != JsonSerializer.Serialize(file.EvidenceRecipe.ProteinSubclassLabels)) return false;
        string source = SourceFingerprint(file, file.EvidenceRecipe.Options, panelFingerprint);
        return LabelFingerprint(source, file.ProteinSubclassLabels) == file.ProteinSubclassLabelsFingerprint;
    }

    private static void VerifyCandidate(FamilyClusterFile file, SubclassAssignmentCandidate candidate)
    {
        PanelAssignmentEvidenceStore.RequireCurrent(file);
        string panel = PanelDerivedDataCurrentness.Fingerprint(file.FamilyPanel!);
        string source = SourceFingerprint(file, file.EvidenceRecipe!.Options, panel);
        if (!candidate.UsesCurrentResolver || candidate.PanelFingerprint != panel || candidate.SourceFingerprint != source
            || candidate.ContentFingerprint != LabelFingerprint(candidate.SourceFingerprint, candidate.Labels))
            throw new InvalidOperationException("Subclass evidence changed. Rebuild the subclass candidate.");
    }
}
