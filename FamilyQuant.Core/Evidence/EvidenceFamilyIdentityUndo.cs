using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed partial class GroupingSnapshot
{
    private bool restoreUnlockIdentities;
    private List<Evidence.EvidenceFamilyIdentity>? unlockIdentities;

    /// <summary>Capture the grouping and only the identity metadata the explicit unlock changes.
    /// Large reference panels and stored evidence remain untouched by this lock-only snapshot.</summary>
    public static GroupingSnapshot CaptureEvidenceIdentityUnlock(FamilyClusterFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var snapshot = Capture(file);
        snapshot.restoreUnlockIdentities = file.EvidenceRecipe != null;
        snapshot.unlockIdentities = CopyUnlockIdentities(file.EvidenceRecipe?.FamilyIdentities);
        return snapshot;
    }

    private static List<Evidence.EvidenceFamilyIdentity>? CopyUnlockIdentities(List<Evidence.EvidenceFamilyIdentity>? identities) =>
        identities?.Select(identity => identity with { Identifiers = identity.Identifiers.ToArray() }).ToList();

    private void RestoreEvidenceIdentityUnlock(FamilyClusterFile file)
    {
        if (!restoreUnlockIdentities) return;
        if (file.EvidenceRecipe == null) throw new InvalidOperationException("The saved evidence grouping is no longer available. Undo its later change before restoring these locks.");
        file.EvidenceRecipe.FamilyIdentities = CopyUnlockIdentities(unlockIdentities);
    }
}
