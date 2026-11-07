using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed partial class EvidenceGroupingSession
{
    private PanelEvidenceMatching? matchSnapshot;
    public PanelEvidenceMatchContext CreatePanelMatchContext(string familyKey)
    {
        EnsureCurrent();
        var panel = FamilyPanelValidation.Clone(selectedReviewPanel ?? reviewPanel
            ?? throw new InvalidOperationException("Import a family panel before matching its evidence names."));
        if (matchSnapshot == null || !matchSnapshot.IsCurrent(file)) matchSnapshot = new PanelEvidenceMatching(file);
        var snapshot = matchSnapshot;
        return new(snapshot, panel.Families.Single(family => family.Key == familyKey), panel.ConfirmedEvidenceMatches?.GetValueOrDefault(familyKey), () => snapshot.IsCurrent(file), panel);
    }
    public EvidenceReviewInput ConfirmPanelEvidenceMatches(string familyKey, PanelConfirmedEvidenceMatches matches)
    {
        EnsureCurrent();
        if (reviewPanel == null || Proposal == null) throw new InvalidOperationException("Build a panel proposal before confirming evidence matches.");
        var updatedBase = PanelEvidenceMatching.WithConfirmed(reviewPanel, familyKey, matches);
        var updatedSelection = PanelEvidenceMatching.WithConfirmed(selectedReviewPanel ?? reviewPanel, familyKey, matches);
        var input = EvidenceProposalPresentation.Present(Proposal, file, coherenceProfiles, updatedSelection);
        EnsureCurrent();
        reviewPanel = updatedBase; selectedReviewPanel = updatedSelection;
        return input;
    }
}
