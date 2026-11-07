using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;

namespace FamilyQuant;

public sealed partial class GroupingReviewViewModel
{
    private readonly Func<string, PanelConfirmedEvidenceMatches, EvidenceReviewInput>? confirmPanelMatches;
    public bool CanMatchPanelNames => confirmPanelMatches != null && !IsBusy && ScopeText == proposalScope
        && Evidence?.SelectedChecklistFamily is { } family && Evidence.Source.PanelNames?.ContainsKey(family.Key) == true
        && !Evidence.HasUnappliedPanelLinkChoices && !Evidence.HasInvalidPanelLinkNotes;
    public void ConfirmPanelMatches(string familyKey, PanelConfirmedEvidenceMatches matches)
    {
        if (!CanMatchPanelNames) throw new InvalidOperationException("Update the proposal and use any pending panel-link choices before confirming matches.");
        var previous = Evidence!.CaptureReviewState();
        if (previous.ManualChoices?.Count > 0) updateManualChoices?.Invoke(previous.ManualChoices);
        var input = confirmPanelMatches!(familyKey, matches);
        SetEvidence(input); Evidence!.RestoreReviewState(previous);
        manualFailure = ""; Status = "Matching labels confirmed in this review. Apply grouping and File > Save keep them.";
        NotifySummary();
    }
}
