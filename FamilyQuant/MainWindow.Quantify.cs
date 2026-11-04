using System;

namespace FamilyQuant;

public partial class MainWindow
{
    private QuantifyControl? quantify;
    private string? openedDatabaseFileName;
    private string? openedDatabaseFileSha256;
    public static Func<QuantManifest?, FamilyClusterFile?, ComputeXicViewModel>? QuantifyViewModelFactoryOverride { get; set; }
    private bool IsQuantifyVisible => quantify != null && GridApp.Children.Contains(quantify);
    private bool IsQuantifyBusy => quantify?.ViewModel.IsBusy == true;

    private void PresentQuantifyScreen()
    {
        if (cgc == null || loadedCuration == null) return;
        var model = QuantifyViewModelFactoryOverride?.Invoke(loadedManifest, loadedCuration)
            ?? new ComputeXicViewModel(AppVersion(), loadedManifest, loadedCuration, SharedDeNovoFilters());
        model.SourceDatabaseFileName = openedDatabaseFileName;
        model.SourceDatabaseFileSha256 = openedDatabaseFileSha256;
        var control = new QuantifyControl(model);
        model.Operations = Operations;
        ClusterGenerationControl? heldProject = cgc;
        FamilyClusterFile? heldCuration = loadedCuration;
        var heldRows = loadedQuant;
        QuantManifest? heldManifest = loadedManifest;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ComputeXicViewModel.IsBusy) && model.IsBusy)
            {
                heldProject = cgc; heldCuration = loadedCuration; heldRows = loadedQuant; heldManifest = loadedManifest;
            }
            if (ReferenceEquals(quantify, control)) UpdateGuide();
        };
        control.Completed += result =>
        {
            if (!ReferenceEquals(control, quantify) || !ReferenceEquals(heldProject, cgc)
                || !ReferenceEquals(heldCuration, loadedCuration) || !ReferenceEquals(heldRows, loadedQuant)
                || !ReferenceEquals(heldManifest, loadedManifest))
            {
                StatusModel.ShowMessage("The project changed while Quantify was running. Its previous results were kept.");
                return;
            }
            loadedQuant = result.Rows;
            loadedManifest = result.Manifest;
            loadedCuration!.ReportInputProvenance = model.CompletedInputProvenance;
            lastSpectrumComparisonCsvPath = result.SpectrumComparisonCsvPath;
            RefreshProjectCommandAvailability(spectrumAvailable: result.SpectrumComparisonRows.Count > 0 || loadedQuant.Count > 0);
            cgc!.SetFullXicMappingProvenance(loadedManifest.FamilyMapping);
            _ = cgc.SetFullXicQuantAsync(loadedQuant, loadedManifest.PerRunTic, model.OperationStartedAtUtc, model.OperationLogPath);
            DocumentState.MarkModified();
            UpdateGuide();
            StatusModel.ShowMessage(model.RunSummary + "\n" + model.Details);
        };
        quantify = control;
        GridApp.Children.Clear();
        home = null; usearch = null; dnc = null; analytics = null;
        GridApp.Children.Add(control);
        UpdateGuide();
    }

    private string QuantifyHelpText() => quantify == null ? "" :
        "Quantify reads the selected RAW folder and SEPro package with the shared de novo filters.\n\n" +
        HomologyMigrationHelp.Text + "\n\n" +
        quantify.ViewModel.Basis + "\n\n" + quantify.ViewModel.BasisDetails + "\n\n" +
        quantify.ViewModel.RunSummary + "\n\n" + quantify.ViewModel.Details;
}
