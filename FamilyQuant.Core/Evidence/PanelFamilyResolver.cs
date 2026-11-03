using System;
using FamilyQuant.Panels;

namespace FamilyQuant.Evidence;

/// <summary>A detached snapshot, verified against current assignment inputs when created.
/// Recheck IsCurrent before publishing work calculated asynchronously from this snapshot.</summary>
public sealed class PanelFamilyResolver
{
    private readonly PanelEvidenceClassifier.ClassificationContext context;
    private readonly string fingerprint;

    internal PanelFamilyResolver(FamilyClusterFile file)
    {
        PanelAssignmentEvidenceStore.RequireCurrent(file);
        var panel = FamilyPanelValidation.Clone(file.FamilyPanel!);
        var links = file.EvidenceRecipe?.PanelContext?.Links
            ?? throw new InvalidOperationException("Current panel-link provenance is unavailable. Rebuild the evidence grouping.");
        context = new(panel, EvidenceApply.Clone(links));
        fingerprint = file.PanelProteinAssignmentsFingerprint!;
    }

    /// <summary>Map any source record, including an external reviewed record. This resolves its
    /// family/architecture context; it does not replace scope, identity or coverage gates.</summary>
    public string? PanelFamilyFor(ProteinEvidence? evidence) => evidence == null ? null : context.Resolve(evidence).Family;

    public bool IsCurrent(FamilyClusterFile file) => file.PanelProteinAssignmentsFingerprint == fingerprint
        && PanelAssignmentEvidenceStore.Current(file) != null;
}

public static partial class PanelEvidenceClassifier
{
    public static PanelFamilyResolver CreateCurrentResolver(FamilyClusterFile file) => new(file);
}
