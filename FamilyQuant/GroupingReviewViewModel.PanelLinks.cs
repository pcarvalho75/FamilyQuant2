using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

public sealed partial class GroupingReviewViewModel
{
    private bool onlyPanelLinksNeedingChoice;
    public bool OnlyPanelLinksNeedingChoice
    {
        get => onlyPanelLinksNeedingChoice;
        set { if (Set(ref onlyPanelLinksNeedingChoice, value)) Changed(nameof(VisiblePanelLinks)); }
    }
    public int PanelLinksNeedingChoiceCount => Evidence?.PanelLinks.Count(row => row.Source.NeedsConfirmation) ?? 0;
    public bool IsWaitingForPanelLinks => ModeIndex == 1 && PanelLinksNeedingChoiceCount > 0;
    public string PanelLinksWaitText => "Waiting for " + PanelLinksNeedingChoiceCount.ToString("N0", CultureInfo.CurrentCulture)
        + (PanelLinksNeedingChoiceCount == 1 ? " choice in Panel links" : " choices in Panel links");
    public string PanelLinksChoiceFilterLabel => "Needs a choice (" + PanelLinksNeedingChoiceCount.ToString("N0", CultureInfo.CurrentCulture) + ")";
    public IReadOnlyList<PanelLinkReviewRow> VisiblePanelLinks => (Evidence?.PanelLinks ?? Array.Empty<PanelLinkReviewRow>())
        .Where(row => !OnlyPanelLinksNeedingChoice || row.Source.NeedsConfirmation)
        .OrderByDescending(row => row.Source.NeedsConfirmation).ToArray();

    private void NotifyPanelLinkList()
    {
        Changed(nameof(VisiblePanelLinks));
        Changed(nameof(PanelLinksNeedingChoiceCount));
        Changed(nameof(PanelLinksChoiceFilterLabel));
        Changed(nameof(IsWaitingForPanelLinks)); Changed(nameof(PanelLinksWaitText));
    }
}
