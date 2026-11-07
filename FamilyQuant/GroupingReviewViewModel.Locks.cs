using System;
using System.Linq;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class GroupingReviewViewModel
{
    private readonly Func<string, EvidenceReviewInput>? unlockEvidenceFamilies;
    public bool AllProposedProteinsLocked => Evidence != null && Evidence.Proteins.Any(row => row.Family.Length > 0)
        && Evidence.Proteins.Where(row => row.Family.Length > 0).All(row => row.Source.IsLocked);
    public bool ShowUnlockEvidenceFamilies => ModeIndex == 1 && unlockEvidenceFamilies != null && AllProposedProteinsLocked;
    public bool CanUnlockEvidenceFamilies => ShowUnlockEvidenceFamilies && !IsBusy && ScopeText == proposalScope
        && Evidence is { HasUnappliedPanelLinkChoices: false, HasInvalidPanelLinkNotes: false }
        && !Evidence.PanelLinks.Any(row => row.Source.NeedsConfirmation);
    public string UnlockEvidenceFamiliesHint => "Removes only the locks on current families represented by this proposal as one change Undo reverses, keeping memberships and quantities unchanged. "
        + (IsBusy ? "Wait for the current operation to finish."
            : ScopeText != proposalScope ? "Use Update proposal for the edited scope to enable unlocking."
            : Evidence?.HasInvalidPanelLinkNotes == true ? "Complete each edited panel-link note to enable unlocking."
            : Evidence?.HasUnappliedPanelLinkChoices == true ? "Use link choices to update the proposal before unlocking."
            : Evidence?.PanelLinks.Any(row => row.Source.NeedsConfirmation) == true ? "Resolve each panel link to enable unlocking."
            : "Review the newly eligible ticks, then Apply grouping; File > Save keeps the lock change.");

    public Task UnlockEvidenceFamiliesAsync()
    {
        if (!CanUnlockEvidenceFamilies) return Task.CompletedTask;
        try
        {
            IsBusy = true;
            var previous = Evidence!.CaptureReviewState();
            int[] defaults = Evidence.Proteins.Where(row => row.Source.IsLocked && row.Family.Length > 0).Select(row => row.ProteinIndex).ToArray();
            var input = unlockEvidenceFamilies!(ScopeText);
            SetEvidence(input);
            Evidence!.RestoreReviewState(previous, useDefaults: defaults);
            Status = "Evidence families unlocked. Undo restores their locks; review the updated ticks, then Apply grouping.";
        }
        catch (Exception error) { Status = "The evidence families could not be unlocked: " + error.Message; }
        finally { IsBusy = false; }
        return Task.CompletedTask;
    }

    private void NotifyEvidenceLocks()
    {
        Changed(nameof(AllProposedProteinsLocked)); Changed(nameof(ShowUnlockEvidenceFamilies)); Changed(nameof(CanUnlockEvidenceFamilies));
        Changed(nameof(UnlockEvidenceFamiliesHint));
    }
}
