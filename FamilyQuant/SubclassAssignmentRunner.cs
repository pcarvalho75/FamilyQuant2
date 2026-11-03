using System;
using System.Threading.Tasks;
using FamilyQuant.Panels;

namespace FamilyQuant;

/// <summary>Runs subclass recomputation through the Families control's shared operation host and Undo session.
/// Call after the preceding operation has released its host lease. False means no labels were committed.</summary>
public static class SubclassAssignmentRunner
{
    public static Task<bool> RecomputeSubclassesAsync(ClusterGenerationControl host) =>
        RecomputeSubclassesAsync(host, false);

    /// <summary>When called as part of a preceding action, fold the result into that action's existing Undo.</summary>
    public static Task<bool> RecomputeSubclassesAsync(ClusterGenerationControl host, bool combineWithPreviousUndo) =>
        (host ?? throw new ArgumentNullException(nameof(host))).RecomputeSubclassesCoreAsync(combineWithPreviousUndo);
}

public partial class ClusterGenerationControl
{
    internal Task<bool> RecomputeSubclassesCoreAsync(bool combineWithPreviousUndo) => RunCurationAsync("Recompute subclass labels", async operation =>
    {
        FamilyClusterFile document = fcf;
        GroupingSnapshot? previousUndo = combineWithPreviousUndo && undoStack.Count > 0 ? undoStack[^1] : null;
        if (combineWithPreviousUndo && previousUndo == null)
            throw new InvalidOperationException("There is no preceding action to combine with subclass recomputation.");
        operation.Report("subclasses", "Preparing subclass assignments");
        var candidate = await Task.Run(() => SubclassAssignment.ComputeCurrent(document, operation), operation.CancellationToken);
        operation.ThrowIfCancellationRequested();
        if (!ReferenceEquals(fcf, document)) throw new InvalidOperationException("The current document changed. Recompute subclasses again.");
        if (combineWithPreviousUndo && (undoStack.Count == 0 || !ReferenceEquals(undoStack[^1], previousUndo)))
            throw new InvalidOperationException("The preceding Undo changed. Recompute subclasses again.");
        var result = SubclassAssignment.CommitCandidate(document, candidate, operation);
        if (!combineWithPreviousUndo)
        {
            undoStack.Add(result.Undo);
            if (undoStack.Count > UndoLimit) undoStack.RemoveAt(0);
        }
        UpdateUndoButton();
        ViewModel.NotifyDocumentChanged();
        ShowCurationOperationResult("Subclass labels updated. Undo restores the previous labels.");
    });
}
