using System;
using System.Threading.Tasks;

namespace FamilyQuant;

/// <summary>Runs subclass recomputation through the Families control's shared operation host and Undo session.
/// Call after the preceding operation has released its host lease. False means no labels were committed.</summary>
public static class SubclassAssignmentRunner
{
    public static Task<bool> RecomputeSubclassesAsync(ClusterGenerationControl host) =>
        throw new NotImplementedException("Subclass recomputation is not yet available.");
}
