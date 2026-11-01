namespace FamilyQuant;

public partial class MainWindow
{
    private DeNovoHomology? latestHomologyResult;

    // C11's narrow bridge retains a completed standalone snapshot independently of quantitation.
    private void KeepHomologyResult(DeNovoHomology result)
    {
        latestHomologyResult = result;
        RefreshNavigationState();
        RefreshShellMenuState();
        StatusModel.ShowMessage("The homology snapshot and its CSV export are available in Results.");
    }
}
