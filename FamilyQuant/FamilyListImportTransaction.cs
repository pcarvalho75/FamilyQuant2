using FamilyQuant.Panels;
using System;
using System.Linq;

namespace FamilyQuant;

/// <summary>One existing panel transaction, bound to the captured project and its complete initial state.</summary>
public sealed class FamilyListImportTransaction : IDisposable
{
    private readonly CurationViewModel model;
    private readonly Func<bool> isCurrent;
    private readonly Action afterEdit;
    private string initialState;
    private string committedState = "";
    private GroupingSnapshot? undo;
    private long stateVersion, fingerprintVersion = -1;
    private string cachedFingerprint = "";
    private bool disposed;
    public FamilyClusterFile Project => model.Document;
    public bool Imported { get; private set; }
    public string PreviousPanel => model.Document.FamilyPanel is { } panel ? panel.Name + " (" + panel.Families.Count.ToString("N0")
        + (panel.Families.Count == 1 ? " family)" : " families)") : "None";
    public bool CanUndo => !disposed && Imported && undo != null && isCurrent()
        && model.UndoStack.LastOrDefault() == undo && Fingerprint() == committedState;

    public FamilyListImportTransaction(CurationViewModel model, Func<bool>? isCurrent = null, Action? afterEdit = null)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.isCurrent = isCurrent ?? (() => true); this.afterEdit = afterEdit ?? (() => { }); initialState = Fingerprint();
        model.DocumentChanged += DocumentChanged;
    }

    public bool Import(FamilyPanel panel, out string message)
    {
        if (Imported) { message = "This family list is already imported."; return false; }
        if (!CheckCurrent(out message)) return false;
        bool changed = model.ApplyFamilyPanel(panel);
        Imported = true;
        undo = changed ? model.UndoStack.LastOrDefault() : null;
        committedState = Fingerprint();
        message = changed ? "Family list imported." : "This family list is already the current panel.";
        afterEdit(); return true;
    }

    public bool CheckCurrent(out string message)
    {
        // Explicit publication boundaries still catch direct, unnotified edits to the mutable graph.
        // CanUndo only observes the cached digest for the current notified state.
        bool current = !disposed && isCurrent() && Fingerprint(refresh: true) == initialState;
        message = current ? "" : "The project changed. Review the import again.";
        return current;
    }

    public bool Undo(out string message)
    {
        if (!CanUndo || Fingerprint(refresh: true) != committedState)
        { message = "Use the project's Undo command to review later changes first."; return false; }
        model.Undo(); Imported = false; undo = null; committedState = ""; initialState = Fingerprint();
        afterEdit(); message = "Import undone."; return true;
    }

    private void DocumentChanged(object? sender, EventArgs args) => stateVersion++;

    private string Fingerprint(bool refresh = false)
    {
        if (refresh || fingerprintVersion != stateVersion)
        {
            cachedFingerprint = model.Document.JsonContentSha256();
            fingerprintVersion = stateVersion;
        }
        return cachedFingerprint;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        model.DocumentChanged -= DocumentChanged;
    }
}
