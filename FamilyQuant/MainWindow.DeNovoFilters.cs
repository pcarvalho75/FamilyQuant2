namespace FamilyQuant;

public partial class MainWindow
{
    private DeNovoFilterViewModel? projectDeNovoFilters;
    private FamilyClusterFile? filterDocument;
    private QuantManifest? filterManifest;

    // C10's narrow host seam: both views receive one filter state for this document/result pair.
    private DeNovoFilterViewModel SharedDeNovoFilters()
    {
        if (projectDeNovoFilters == null || !ReferenceEquals(filterDocument, loadedCuration)
            || !ReferenceEquals(filterManifest, loadedManifest))
        {
            projectDeNovoFilters = new DeNovoFilterViewModel(loadedManifest);
            filterDocument = loadedCuration;
            filterManifest = loadedManifest;
        }
        return projectDeNovoFilters;
    }
}
