using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed partial class EvidenceGroupingSession
{
    public EvidenceReviewInput UnlockEvidenceFamilies(string scope, Action<IReadOnlyList<int>> unlock)
    {
        EnsureCurrent();
        if (Proposal == null || scope != lastScope) throw new InvalidOperationException("Update the proposal before unlocking its families.");
        var rows = EvidenceProposalPresentation.Present(Proposal, file, coherenceProfiles, selectedReviewPanel).Proteins
            .Where(row => row.Family.Length > 0).ToArray();
        if (rows.Length == 0 || rows.Any(row => !row.IsLocked))
            throw new InvalidOperationException("Use this action only when every proposed protein belongs to a locked family.");
        int[] families = rows.Select(row => file.MyExtendedFastaItems[row.ProteinIndex].UserID).Distinct().Order().ToArray();
        var expected = new HashSet<int>(sourceLocks); expected.ExceptWith(families);
        unlock(families);
        if (!expected.SetEquals(file.LockedUserIds)) throw new InvalidOperationException("The locks changed unexpectedly. Undo the lock edit and reopen the review.");
        sourceLocks.ExceptWith(families);
        EnsureCurrent();
        // Locks affect review eligibility, not the already calculated classification.
        return EvidenceProposalPresentation.Present(Proposal, file, coherenceProfiles, selectedReviewPanel);
    }
}
