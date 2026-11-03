using System;
using System.Threading.Tasks;
using FamilyQuant.Panels;

namespace FamilyQuant;

/// <summary>Runs subclass recomputation through the Families control's shared operation host and Undo session.
/// Call after the preceding operation has released its host lease. False means no labels were committed.</summary>
public static class SubclassAssignmentRunner
{
    public static Task<bool> RecomputeSubclassesAsync(ClusterGenerationControl host) =>
        (host ?? throw new ArgumentNullException(nameof(host))).RecomputeSubclassesCoreAsync();
}

public partial class ClusterGenerationControl
{
    internal Task<bool> RecomputeSubclassesCoreAsync() => RunCurationAsync("Recompute subclass labels", async operation =>
    {
        FamilyClusterFile document = fcf;
        operation.Report("subclasses", "Preparing subclass assignments");
        var candidate = await Task.Run(() => SubclassAssignment.ComputeCurrent(document, operation), operation.CancellationToken);
        operation.ThrowIfCancellationRequested();
        if (!ReferenceEquals(fcf, document)) throw new InvalidOperationException("The current document changed. Recompute subclasses again.");
        var result = SubclassAssignment.CommitCandidate(document, candidate, operation);
        undoStack.Add(result.Undo);
        if (undoStack.Count > UndoLimit) undoStack.RemoveAt(0);
        UpdateUndoButton();
        ViewModel.NotifyDocumentChanged();
        ShowCurationOperationResult("Subclass labels updated. Undo restores the previous labels.");
    });
}
