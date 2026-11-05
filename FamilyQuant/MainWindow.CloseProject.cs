using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    private async void MenuItemCloseProject_Click(object sender, RoutedEventArgs e) => await CloseProjectAsync();

    public Task<bool> CloseProjectAsync() => AppOperationHost.OnUi(this, CloseProjectCoreAsync);

    private async Task<bool> CloseProjectCoreAsync()
    {
        var closing = cgc;
        if (closing == null || !await ConfirmCanReplaceAsync() || !ReferenceEquals(closing, cgc)) return false;
        if (trackedCuration != null && documentChangedHandler != null)
            documentChangedEvent?.RemoveEventHandler(trackedCuration, documentChangedHandler);
        trackedCuration = null;
        documentChangedEvent = null;
        documentChangedHandler = null;
        AttachStatusSource(null);
        GridApp.Children.Clear();
        cgc = null;
        loadedCuration = null;
        loadedManifest = null;
        loadedQuant = new List<QuantRow>();
        lastFcfPath = null;
        lastSpectrumComparisonCsvPath = null;
        openedDatabaseFileName = null;
        openedDatabaseFileSha256 = null;
        latestHomologyResult = null;
        quantify = null;
        usearch = null;
        dnc = null;
        analytics = null;
        home = null;
        DocumentState.Clear();
        ShowHome();
        RefreshProjectCommandAvailability(resetSpectrum: true);
        return true;
    }
}
