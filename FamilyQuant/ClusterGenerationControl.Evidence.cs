using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant;

public partial class ClusterGenerationControl
{
    private bool evidenceReviewRunning;
    private bool evidenceFetchRunning;
    public Func<IReadOnlyList<FastaItemExtended>, IReadOnlyList<NeighbourReference>, NeighbourSearch>? EvidenceSearchOverride { get; set; }
    public Func<EvidenceGroupingSession, EvidenceReviewSelection?>? ConfirmEvidenceReviewOverride { get; set; }
    public event EventHandler? DocumentChanged;

    private async void ButtonEvidenceGrouping_Click(object sender, RoutedEventArgs e) => await ReviewEvidenceAsync();

    public Task ReviewEvidenceAsync() => AppOperationHost.OnUi(this, ReviewEvidenceCoreAsync);

    private async Task ReviewEvidenceCoreAsync()
    {
        if (evidenceReviewRunning || superClusterRunning) return;
        evidenceReviewRunning = true;
        try
        {
            SuperClusterPreview? preview = null;
            bool prepared = await RunCurationAsync("Prepare evidence grouping review", async operation =>
            {
                var proteins = fcf.MyExtendedFastaItems.ToList();
                var locks = fcf.LockedUserIds.ToArray();
                operation.Report("review", "Preparing the stored protein review");
                preview = await Task.Run(() => new SuperClusterPreview(proteins, null, "", "", KeywordMatch.All, null, locks), operation.CancellationToken);
                operation.ThrowIfCancellationRequested();
            });
            if (prepared && preview != null && OpenGroupingReview(preview, true))
            {
                PerformClustering(preview);
            }
        }
        catch (Exception ex) { ShowCurationOperationResult("The grouping review could not finish: " + ex.Message); }
        finally { evidenceReviewRunning = false; }
    }

    private bool OpenGroupingReview(SuperClusterPreview preview, bool startWithEvidence)
    {
        var session = new EvidenceGroupingSession(fcf, AppVersionInfo.Current(), EvidenceSearchOverride);
        if (startWithEvidence && ConfirmEvidenceReviewOverride != null)
        {
            var choice = ConfirmEvidenceReviewOverride(session);
            if (choice != null) ApplyEvidenceChoice(session, choice);
            return false;
        }
        Window? owner = Window.GetWindow(this);
        if (owner == null) return false;
        var window = new SuperClusterPreviewWindow(preview, null, session.Propose, session.CountMotif,
            evidenceMode: startWithEvidence, discoverTask: session.DiscoverTaskAsync) { Owner = owner };
        window.ShowDialog();
        if (!window.Confirmed) return false;
        if (!window.IsEvidenceMode) return true;
        ApplyEvidenceChoice(session, window.EvidenceSelection!);
        return false;
    }

    private void ApplyEvidenceChoice(EvidenceGroupingSession session, EvidenceReviewSelection choice)
    {
        bool committed = false;
        BeginWait("Applying the reviewed grouping...");
        try
        {
            EvidenceApplyResult? result = null;
            CurationOperation.Commit(() => result = session.Apply(choice));
            if (result == null) throw new InvalidOperationException("The grouping produced no result.");
            if (!result.Changed) { ShowCurationOperationResult("The reviewed grouping is already applied."); return; }
            committed = true;
            if (result.Undo != null)
            {
                undoStack.Add(result.Undo);
                if (undoStack.Count > UndoLimit) undoStack.RemoveAt(0);
                UpdateUndoButton();
            }
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            AfterRegrouping();
            ShowCurationOperationResult(result.Recipe.Assignments.Count.ToString("N0") + " proteins grouped in "
                + result.Recipe.Assignments.Select(a => a.FamilyId).Distinct().Count().ToString("N0") + " families. Undo restores the previous grouping.");
        }
        catch (Exception ex)
        {
            ShowCurationOperationResult((committed ? "The grouping was applied, but the view could not refresh: " : "No grouping was applied: ") + ex.Message);
        }
        finally { EndWait(); }
    }

    private MessageBoxResult ShowCurationMessage(string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShellMessages.Report(this, message, title, buttons, image, defaultResult, SetCurationOperationStatus);

    private void ShowCurationOperationResult(string message) =>
        ShellMessages.Report(this, message, "Families", status: SetCurationOperationStatus, showDialog: false);

    private void SetCurationOperationStatus(string message)
    {
        statusTooltipBeforeSave ??= TextBlockStatusLine.ToolTip;
        saveResultStatus = message;
        TextBlockStatusLine.Text = message;
        TextBlockStatusLine.ToolTip = statusTooltipBeforeSave + "\n\n" + message;
        PublishShellStatus();
    }
}
