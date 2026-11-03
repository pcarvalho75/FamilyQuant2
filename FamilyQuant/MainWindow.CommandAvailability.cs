using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class MainWindow
{
    private static bool ExplicitAvailability(MenuItem item) =>
        item.ReadLocalValue(UIElement.IsEnabledProperty) is bool enabled ? enabled : item.IsEnabled;

    private void RefreshProjectCommandAvailability(bool? spectrumAvailable = null, bool resetSpectrum = false)
    {
        bool project = cgc != null;
        bool familyOrHome = cgc != null && GridApp.Children.Contains(cgc) || home != null && GridApp.Children.Contains(home);
        bool build = usearch != null && GridApp.Children.Contains(usearch);
        bool spectrum = spectrumAvailable ?? (resetSpectrum
            ? (loadedManifest?.SpectrumComparisonRows?.Count ?? 0) > 0 || loadedQuant.Count > 0
            : build ? false : ExplicitAvailability(MenuItemSpectrumComparison));
        // Quantify retains the previous view's reannotation policy, as before.
        bool reannotate = IsQuantifyVisible ? ExplicitAvailability(MenuItemReanotateSEPro) : project && familyOrHome;
        MenuItemSaveFCF.IsEnabled = MenuItemSaveProject.IsEnabled = project;
        MenuItemFamilyPanel.IsEnabled = project && !Operations.IsBusy && !saveInProgress;
        MenuItemReportDetails.IsEnabled = project && !Operations.IsBusy && !saveInProgress;
        MenuItemExportReport.IsEnabled = !Operations.IsBusy && !saveInProgress && !reportExportInProgress;
        MenuItemComputeXic.IsEnabled = project && familyOrHome;
        MenuItemClusteringDiagnostic.IsEnabled = CanRunClusteringDiagnostic;
        MenuItemSpectrumComparison.IsEnabled = spectrum;
        MenuItemReanotateSEPro.IsEnabled = reannotate;
    }
}
