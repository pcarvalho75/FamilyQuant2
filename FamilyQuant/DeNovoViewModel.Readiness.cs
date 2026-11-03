using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant;

public sealed partial class DeNovoViewModel
{
    private string observedMode = "";
    protected override void ChildChanged(string name)
    {
        if (observedMode != Filters.ModeTag)
        {
            observedMode = Filters.ModeTag;
            if (LastImport != null && !changingImportSource) SourceChanged();
            SourcePathChanged();
        }
        base.ChildChanged(name);
    }
    public bool CanReconcile => !IsBusy && FilteredRows.Count > 0;
    public bool CanSearch => CanReconcile;
    public bool CanExportHomology => !IsBusy && LastHomology != null;
    public bool CanExtract => !IsBusy && LastReconciliation != null && KeptRunNames().Count > 0
        && RawMatches.Count(m => m.Found) == KeptRunNames().Count;
    private bool EveryRunExtracted => LastExtraction != null && KeptRunNames().Count > 0
        && KeptRunNames().All(r => LastExtraction.ByRun.ContainsKey(r));
    public bool CanPartition => !IsBusy && EveryRunExtracted && (!string.IsNullOrWhiteSpace(PartitionPlp) || !string.IsNullOrWhiteSpace(PartitionXic2));
    public string ReconcileState => ReportView.LayerState(CanReconcile, CanReconcile ? null : "no rows imported; use Load and filter in Step 1 first");
    public string ExtractState => ReportView.LayerState(CanExtract, LastReconciliation == null ? "no reconciliation yet"
        : KeptRunNames().Count == 0 ? "no rows survived the reconciliation" : null);
    public string HomologyState => ReportView.LayerState(CanSearch, CanSearch ? null : "no rows imported; use Load and filter in Step 1 first");
    public string PartitionState => ReportView.LayerState(CanPartition, EveryRunExtracted ? null : "no extraction yet");
    public void SourcePathChanged()
    {
        if (Directory.Exists(ImportDirectory))
        {
            try { LabelFilesFound = "Files found: " + DeNovoScreenOperations.CountSelectedExports(
                ImportDirectory, Filters.Sources).ToString("N0", CultureInfo.CurrentCulture); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Changed(null);
    }
    public void RawDirectoryChanged()
    {
        bool dropped = LastExtraction != null || LastPartition != null;
        if (dropped) ClearExtraction();
        RefreshRawMatchPreview();
        Changed(null);
        if (dropped) ShowStatus("The folder holding the acquisitions was changed, so the extraction and any " +
            "partition built on it were dropped: they measured the RAW files of the previous folder. " +
            "Check every matched run before extracting again.");
    }
}
