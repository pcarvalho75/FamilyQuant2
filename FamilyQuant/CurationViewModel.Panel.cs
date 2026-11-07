using System;
using System.Text.Json;
using FamilyQuant.Panels;

namespace FamilyQuant;

public sealed partial class CurationViewModel
{
    /// <summary>Applies a validated detached panel as one undoable project edit.</summary>
    public bool ApplyFamilyPanel(FamilyPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var next = FamilyPanelValidation.Clone(panel);
        FamilyPanelValidation.Validate(next);
        if (JsonSerializer.Serialize(Document.FamilyPanel) == JsonSerializer.Serialize(next)) return false;
        var undo = GroupingSnapshot.CaptureFamilyPanel(Document);
        UndoStack.Add(undo);
        if (UndoStack.Count > 50) UndoStack.RemoveAt(0);
        Document.FamilyPanel = next;
        Changed(nameof(CanUndo));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
