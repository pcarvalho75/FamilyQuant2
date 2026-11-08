using System;
using System.Collections.Generic;
using System.Linq;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;

namespace FamilyQuant;

public sealed partial class GroupingSnapshot
{
    private FamilyClusterFile? panelOnlyFile;
    private FastaItemExtended[]? panelOnlyRows;
    private FamilyPanel? previousFamilyPanel;
    private EvidenceRecipe? previousPanelRecipe;
    private List<ProteinSubclassLabel>? previousPanelSubclassLabels;
    private string? previousPanelSubclassPanelFingerprint;
    private string? previousPanelSubclassFingerprint;

    /// <summary>A panel edit retains its detached definitions and dependent subclass/recipe state,
    /// including a combined recompute, without copying unrelated reference evidence.</summary>
    public static GroupingSnapshot CaptureFamilyPanel(FamilyClusterFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var snapshot = new GroupingSnapshot(new(), new(), null, false, null)
        {
            panelOnlyFile = file,
            panelOnlyRows = file.MyExtendedFastaItems.ToArray(),
            previousFamilyPanel = file.FamilyPanel == null ? null : FamilyPanelValidation.Clone(file.FamilyPanel),
            previousPanelRecipe = file.EvidenceRecipe == null ? null : EvidenceApply.Clone(file.EvidenceRecipe),
            previousPanelSubclassLabels = file.ProteinSubclassLabels == null ? null : EvidenceApply.Clone(file.ProteinSubclassLabels),
            previousPanelSubclassPanelFingerprint = file.ProteinSubclassLabelsPanelFingerprint,
            previousPanelSubclassFingerprint = file.ProteinSubclassLabelsFingerprint,
        };
        return snapshot;
    }

    private bool RestoreFamilyPanel(FamilyClusterFile file)
    {
        if (panelOnlyRows == null) return false;
        if (!ReferenceEquals(file, panelOnlyFile) || file.MyExtendedFastaItems.Count != panelOnlyRows.Length
            || panelOnlyRows.Where((protein, index) => !ReferenceEquals(protein, file.MyExtendedFastaItems[index])).Any())
            throw new InvalidOperationException("The proteins changed since the panel edit; its undo cannot be restored.");
        // Prepare first: failed storage or deserialization must leave both document and snapshot intact.
        var restored = previousFamilyPanel == null ? null : FamilyPanelValidation.Clone(previousFamilyPanel);
        var restoredRecipe = previousPanelRecipe == null ? null : EvidenceApply.Clone(previousPanelRecipe);
        var restoredLabels = previousPanelSubclassLabels == null ? null : EvidenceApply.Clone(previousPanelSubclassLabels);
        file.FamilyPanel = restored;
        file.EvidenceRecipe = restoredRecipe;
        file.ProteinSubclassLabels = restoredLabels;
        file.ProteinSubclassLabelsPanelFingerprint = previousPanelSubclassPanelFingerprint;
        file.ProteinSubclassLabelsFingerprint = previousPanelSubclassFingerprint;
        return true;
    }
}
