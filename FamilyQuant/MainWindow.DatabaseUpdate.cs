using FamilyQuant.DatabaseUpdate;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant;

/// <summary>
/// File > Update database (docs/design/DATABASE-UPDATE.md, sections 2, 7 and 9): read the newer FASTA, compare it
/// with the saved project, place the added proteins, show the preview, and apply it as one step. Reading,
/// comparing and placing run with progress and Cancel and change nothing; Apply is one commit. The project is
/// then unsaved, a fresh Families screen recomputes the share column over the new protein set (and starts with
/// an empty Undo), and the status line says what to do next.
/// </summary>
public partial class MainWindow
{
    private const string DatabaseUpdateTitle = "Update database";

    /// <summary>Tests choose the new database file here instead of the file dialog.</summary>
    public static Func<string?>? SelectUpdateDatabasePathOverride { get; set; }

    /// <summary>Tests answer the low-overlap warning here instead of a message box.</summary>
    public static Func<string, bool>? ConfirmDatabaseUpdateOverride { get; set; }

    /// <summary>Tests take the preview here instead of the modal window, and return the user's choice.</summary>
    public static Func<DatabaseUpdateViewModel, DatabaseUpdateOutcome>? DatabaseUpdatePreviewOverride { get; set; }

    private async void MenuItemUpdateDatabase_Click(object sender, RoutedEventArgs e) => await UpdateDatabaseAsync();

    public Task<bool> UpdateDatabaseAsync(string? databasePath = null) =>
        AppOperationHost.OnUi(this, () => UpdateDatabaseCoreAsync(databasePath));

    private async Task<bool> UpdateDatabaseCoreAsync(string? databasePath)
    {
        bool Refuse(string reason, MessageBoxImage image = MessageBoxImage.Information)
        {
            ShellMessages.Report(this, reason, DatabaseUpdateTitle, image: image, showDialog: false);
            return false;
        }
        if (DiagnosticBusy) return Refuse("Wait for the current operation to finish before updating the database.");
        var control = cgc;
        if (control == null) return Refuse("Open a project before updating its database.");
        FamilyClusterFile project = control.fcf;
        if (DocumentState.CheckForChanges(project))
            return Refuse("Save the project first (File > Save), then choose Update database again. The update starts from the saved project.");
        string? path = databasePath ?? ChooseUpdateDatabasePath();
        if (string.IsNullOrWhiteSpace(path)) return Refuse("No database was chosen. The project was kept as it was.");
        bool SameProject() => ReferenceEquals(cgc, control) && ReferenceEquals(control.fcf, project);

        DatabaseUpdateSource? source = null;
        IReadOnlyList<DatabaseUpdatePick> picks = Array.Empty<DatabaseUpdatePick>();
        IReadOnlyDictionary<int, bool>? nameChoices = null;
        bool overlapAccepted = false;
        DatabaseUpdateViewModel model;
        while (true)
        {
            DatabaseUpdatePreview preview;
            using (var operation = Operations.Begin(DatabaseUpdateTitle))
            {
                var context = operation.Context;
                try
                {
                    source ??= await Task.Run(() => DatabaseUpdateSource.Read(path, context));
                    var read = source;
                    var comparison = await Task.Run(() => DatabaseUpdateComparer.Compare(project, read, picks, context));
                    context.ThrowIfCancellationRequested();
                    if (!overlapAccepted && LowOverlap(comparison))
                    {
                        string warning = (comparison.Warnings.FirstOrDefault(w => w.Contains("Build families", StringComparison.Ordinal))
                            ?? "Fewer than half of the project's proteins are in the new file; Build families may be the better choice.")
                            + "\n\nContinue with the update anyway?";
                        bool go = ConfirmDatabaseUpdateOverride?.Invoke(warning)
                            ?? ShellMessages.Report(this, warning, DatabaseUpdateTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes;
                        if (!go) return Refuse("Update stopped before placing the new proteins. The project was kept as it was.");
                        overlapAccepted = true;
                    }
                    var placement = await Task.Run(() => DatabaseUpdatePlacer.Place(project, comparison, context));
                    context.ThrowIfCancellationRequested();
                    preview = DatabaseUpdatePlanner.Preview(project, comparison, placement);
                }
                catch (OperationCanceledException) when (context.IsCancellationRequested)
                {
                    return Refuse("Cancelled. The project was kept as it was.");
                }
                catch (Exception ex)
                {
                    Operations.LogFailure(ex);
                    return Refuse("The database update could not be prepared, and the project was kept as it was.\n\nReason given by the program: "
                        + ex.Message, MessageBoxImage.Error);
                }
            }
            if (!SameProject()) return Refuse("The open project changed while the update was prepared. Nothing was applied.");
            model = new DatabaseUpdateViewModel(preview, project.MyExtendedFastaItems.Select(p => p.SequenceIdentifier ?? "").ToArray(),
                Path.GetFileName(path), Path.GetFileName(lastFcfPath) ?? "the open project", nameChoices);
            DatabaseUpdateOutcome outcome = ShowDatabaseUpdatePreview(model);
            if (outcome == DatabaseUpdateOutcome.Cancel) return Refuse("Database update cancelled. Nothing changed.");
            if (outcome == DatabaseUpdateOutcome.Apply) break;
            picks = model.SelectedPicks;
            nameChoices = model.NameChoices;
        }

        if (!SameProject() || DiagnosticBusy) return Refuse("The open project changed before Apply. Nothing was applied.");
        DatabaseUpdateApplyResult result;
        using (var operation = Operations.Begin("Apply database update"))
        {
            try
            {
                result = DatabaseUpdateApply.Apply(project, model.Preview, model.KeptNames, AppVersionInfo.Current(), operation.Context);
            }
            catch (Exception ex) when (!operation.Context.HasCommitted)
            {
                Operations.LogFailure(ex);
                return Refuse("The database update was not applied, and the project was kept as it was.\n\nReason given by the program: " + ex.Message,
                    MessageBoxImage.Error);
            }
        }
        string message = DatabaseUpdateStatus(result.Record, project);
        var (updated, attribution) = AttachUpdatedProject(control, source!.Summary);
        updated.ReportDatabaseUpdate(message);
        // The share column is recomputed in the background and then shows its own summary; the update's sentence
        // is what the user needs next, so it is shown again once the share column is ready.
        try { await attribution; }
        catch (Exception ex) { Operations.LogFailure(ex); }
        if (ReferenceEquals(cgc, updated)) updated.ReportDatabaseUpdate(message);
        return true;
    }

    private string? ChooseUpdateDatabasePath()
    {
        if (SelectUpdateDatabasePathOverride != null) return SelectUpdateDatabasePathOverride();
        var dialog = new OpenFileDialog
        {
            Filter = "Protein database (*.fasta;*.fa;*.fas;*.T-R;*.T)|*.fasta;*.fa;*.fas;*.T-R;*.T|All files (*.*)|*.*",
            Title = "Choose the newer protein database, made the same way as the project's",
        };
        return ShellDialogs.ShowFile(this, "database-update.source", dialog) ? dialog.FileName : null;
    }

    private DatabaseUpdateOutcome ShowDatabaseUpdatePreview(DatabaseUpdateViewModel model)
    {
        if (DatabaseUpdatePreviewOverride != null) return DatabaseUpdatePreviewOverride(model);
        if (!ShellDialogs.IsDisplayed(this)) return DatabaseUpdateOutcome.Cancel;
        new DatabaseUpdateWindow(model) { Owner = this }.ShowDialog();
        return model.Outcome;
    }

    private static bool LowOverlap(DatabaseUpdateComparison comparison)
    {
        DatabaseUpdateCounts c = comparison.Counts;
        int kept = c.Unchanged + c.HeaderChanged + c.SequenceChanged + c.NewAccessionSameSequence + c.ReplacedByLonger;
        int before = kept + c.Removed + c.AmbiguousOldRows;
        return before > 0 && kept < DatabaseUpdateComparer.LowOverlapFraction * before;
    }

    /// <summary>The status sentence after Apply, in the user's words: what changed and what to do next.</summary>
    internal string DatabaseUpdateStatus(DatabaseUpdateRecord record, FamilyClusterFile project)
    {
        DatabaseUpdateCounts c = record.Counts;
        int added = c.Added + c.AmbiguousNewRecords, removed = c.Removed + c.AmbiguousOldRows, changed = c.SequenceChanged;
        string Count(int value, string one, string many) => value.ToString("N0", CultureInfo.CurrentCulture) + " " + (value == 1 ? one : many);
        string text = "Database updated: " + Count(added, "protein", "proteins") + " added, " + removed.ToString("N0", CultureInfo.CurrentCulture)
            + " removed, " + Count(changed, "sequence", "sequences") + " changed.";
        var targets = project.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier ?? "")).ToList();
        int withoutEvidence = targets.Count(p => p.Evidence == null);
        if (project.EvidenceSource != null || targets.Any(p => p.Evidence != null))
        {
            if (withoutEvidence > 0)
                text += " " + Count(withoutEvidence, "protein has", "proteins have") + " no evidence yet: Fetch evidence.";
        }
        if (loadedQuant.Count > 0 || loadedManifest != null) text += " Results are from the previous database: Quantify again.";
        return text;
    }

    /// <summary>Shows a fresh Families screen over the updated project, as Open does, keeping the saved path and the
    /// stored quantitation; the share column is recomputed against the new protein set and Undo starts empty.</summary>
    private (ClusterGenerationControl Screen, Task Attribution) AttachUpdatedProject(ClusterGenerationControl previous, DatabaseSourceSummary newDatabase)
    {
        FamilyClusterFile project = previous.fcf;
        var updated = new ClusterGenerationControl(project) { Name = previous.Name };
        GridApp.Children.Clear();
        cgc = updated;
        usearch = null; dnc = null; analytics = null; home = null;
        loadedCuration = project;
        openedDatabaseFileName = newDatabase.FileName;
        openedDatabaseFileSha256 = newDatabase.Sha256;
        GridApp.Children.Add(updated);
        updated.SetFullXicMappingProvenance(loadedManifest?.FamilyMapping);
        Task attribution = updated.SetFullXicQuantAsync(loadedQuant, loadedManifest?.PerRunTic);
        RefreshProjectCommandAvailability(resetSpectrum: true);
        if (!DocumentState.CheckForChanges(project)) DocumentState.MarkModified();
        TrackCuration(updated);
        UpdateGuide();
        return (updated, attribution);
    }
}

public partial class ClusterGenerationControl
{
    /// <summary>Shows the database update's closing sentence in this screen's status line.</summary>
    public void ReportDatabaseUpdate(string message) => ShowCurationOperationResult(message);
}
