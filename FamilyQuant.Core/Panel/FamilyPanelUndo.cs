using System;
using System.Linq;
using FamilyQuant.Panels;

namespace FamilyQuant;

public sealed partial class GroupingSnapshot
{
    private FamilyClusterFile? panelOnlyFile;
    private FastaItemExtended[]? panelOnlyRows;
    private FamilyPanel? previousFamilyPanel;

    /// <summary>A panel-only edit retains a detached panel, without copying unrelated reference evidence.</summary>
    public static GroupingSnapshot CaptureFamilyPanel(FamilyClusterFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var snapshot = new GroupingSnapshot(new(), new(), null, false, null)
        {
            panelOnlyFile = file,
            panelOnlyRows = file.MyExtendedFastaItems.ToArray(),
            previousFamilyPanel = file.FamilyPanel == null ? null : FamilyPanelValidation.Clone(file.FamilyPanel),
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
        file.FamilyPanel = restored;
        return true;
    }
}
