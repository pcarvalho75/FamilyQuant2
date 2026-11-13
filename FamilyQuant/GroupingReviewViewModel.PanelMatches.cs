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
    public string MatchPanelNamesHint
    {
        get
        {
            if (IsBusy) return "Unavailable while the current review step is running. Wait for it to finish before matching stored names.";
            if (Evidence == null || ScopeText != proposalScope) return "Unavailable until the family proposal matches the current scope. Choose Update proposal before matching stored names.";
            if (Evidence.HasInvalidPanelLinkNotes) return "Unavailable because a panel link note needs correction. In Panel links, enter a short single-line note, then choose Use link choices before matching stored names.";
            if (Evidence.HasUnappliedPanelLinkChoices) return "Unavailable while panel link choices are waiting to update the proposal. In Panel links, choose Use link choices first, then match stored names.";
            if (confirmPanelMatches == null || Evidence.SelectedChecklistFamily is not { } family || Evidence.Source.PanelNames?.ContainsKey(family.Key) != true)
                return "Unavailable without a selected panel family that supports stored-name matching. Open Group by evidence with a panel, then select a family in Family checklist.";
            return "Open stored evidence-label suggestions for the selected panel family, then review and confirm labels used by the checklist. Disabled without an editable current panel proposal and selected family; Update proposal after scope edits, select a family and wait for active operations.";
        }
    }
    public bool CanUnconfirmPanelMatches => CanMatchPanelNames && Evidence!.SelectedChecklistFamily!.HasConfirmedMatches;
    public string UnconfirmPanelMatchesHint
    {
        get
        {
            if (IsBusy) return "Unavailable while the current review step is running. Wait for it to finish before un-confirming matches.";
            if (Evidence == null || ScopeText != proposalScope) return "Unavailable until the family proposal matches the current scope. Choose Update proposal before un-confirming matches.";
            if (Evidence.HasInvalidPanelLinkNotes || Evidence.HasUnappliedPanelLinkChoices)
                return "Unavailable while panel link choices are waiting. In Panel links, choose Use link choices first.";
            if (!CanUnconfirmPanelMatches)
                return "Unavailable without a selected family whose matches are confirmed. Select a Family checklist row with Confirmed match, such as one accepted from its sheet codes.";
            return "Removes the selected family's confirmed matches from this review; its Pfam codes stay in the list. Apply grouping keeps the change, Undo restores it, and closing the review keeps nothing.";
        }
    }
    public void UnconfirmPanelMatches()
    {
        if (!CanUnconfirmPanelMatches) throw new InvalidOperationException("Select a family with confirmed matches in a current proposal before un-confirming them.");
        string familyKey = Evidence!.SelectedChecklistFamily!.Key;
        var previous = Evidence.CaptureReviewState();
        if (previous.ManualChoices?.Count > 0) updateManualChoices?.Invoke(previous.ManualChoices);
        var input = confirmPanelMatches!(familyKey, new PanelConfirmedEvidenceMatches());
        SetEvidence(input); Evidence!.RestoreReviewState(previous);
        manualFailure = ""; Status = "Matches un-confirmed in this review. Apply grouping and File > Save keep the change.";
        NotifySummary();
    }
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
