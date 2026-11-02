namespace FamilyQuant;

public sealed partial class CurationQuantitationViewModel
{
    private string? fullXicError;
    public bool HasPatternLab => CurrentPLP != null;
    public bool HasQuantitation => HasFullXic || HasPatternLab;
    public bool HasFullXicError => fullXicError != null;
    public string FullXicFailureReason => fullXicError ?? string.Empty;
    public string FullXicStateLabel => IsPending ? "Preparing shares" : HasFullXic ? "Ready"
        : HasFullXicError ? "Unavailable" : "Not computed";
    private void NotifyPresentationState()
    {
        Changed(nameof(HasPatternLab)); Changed(nameof(HasQuantitation));
        Changed(nameof(HasFullXicError)); Changed(nameof(FullXicFailureReason));
        Changed(nameof(FullXicStateLabel));
    }
}
