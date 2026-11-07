using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;

namespace FamilyQuant;

public sealed partial class EvidenceGroupingSession
{
    public EvidenceReviewInput ConfirmPanelEvidenceMatches(string familyKey, PanelConfirmedEvidenceMatches matches)
    {
        EnsureCurrent();
        if (reviewPanel == null || Proposal == null) throw new InvalidOperationException("Build a panel proposal before confirming evidence matches.");
        var previous = reviewPanel;
        reviewPanel = PanelEvidenceMatching.WithConfirmed(reviewPanel, familyKey, matches);
        try { return Propose(lastScope); }
        catch { reviewPanel = previous; throw; }
    }
}
