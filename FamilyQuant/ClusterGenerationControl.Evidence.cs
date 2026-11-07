using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class ClusterGenerationControl
{
    private bool evidenceReviewRunning;
    private bool evidenceFetchRunning;
    public Func<IReadOnlyList<FastaItemExtended>, IReadOnlyList<NeighbourReference>, NeighbourSearch>? EvidenceSearchOverride { get; set; }
    public Func<EvidenceGroupingSession, EvidenceReviewSelection?>? ConfirmEvidenceReviewOverride { get; set; }
    public event EventHandler? DocumentChanged;

    private async void ButtonEvidenceGrouping_Click(object sender, RoutedEventArgs e)
    {
        try { await ReviewEvidenceWithFetchOfferAsync(); }
        catch (OperationCanceledException) { ShowCurationOperationResult("Cancelled. The previous document was kept."); }
        catch (Exception error) { ShowCurationOperationResult("The grouping review could not finish: " + error.Message); }
    }

    private async Task ReviewEvidenceWithFetchOfferAsync()
    {
        if (evidenceReviewRunning || evidenceFetchRunning || superClusterRunning || Operations.IsBusy) return;
        if (!HasStoredProteinEvidence())
        {
            if (!ConfirmGroupingPrerequisite("Group by evidence",
                "This project has no UniProt evidence yet. Download it now?", "Download and continue")) return;
            await FetchProteinEvidenceAsync();
            if (!HasStoredProteinEvidence()) return;
        }
        await ReviewEvidenceAsync();
    }

    private bool HasStoredProteinEvidence() => NearestReviewed.Targets(fcf)
        .Any(p => !SequenceLabels.IsContaminant(p.SequenceIdentifier) && p.Evidence != null);

    private bool ConfirmGroupingPrerequisite(string title, string message, string action)
    {
        var owner = Window.GetWindow(this);
        if (owner == null) return false;
        var dialog = new Window { Title = title, Owner = owner, Width = 560, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        SecondaryWindowView.Theme(dialog);
        var proceed = new Button { Content = action, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        proceed.SetResourceReference(StyleProperty, "PrimaryButton");
        proceed.Click += (_, _) => dialog.DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(proceed); buttons.Children.Add(cancel);
        var panel = new StackPanel();
        panel.Children.Add(SecondaryWindowView.Text(message)); panel.Children.Add(buttons);
        dialog.Content = SecondaryWindowView.Frame(panel);
        return dialog.ShowDialog() == true;
    }

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
            if (prepared && preview != null && await OpenGroupingReview(preview, true))
            {
                PerformClustering(preview);
            }
        }
        catch (OperationCanceledException) { ShowCurationOperationResult("Cancelled. The previous document was kept."); }
        catch (Exception ex) { ShowCurationOperationResult("The grouping review could not finish: " + ex.Message); }
        finally { evidenceReviewRunning = false; }
    }

    private async Task<bool> OpenGroupingReview(SuperClusterPreview preview, bool startWithEvidence)
    {
        var session = new EvidenceGroupingSession(fcf, AppVersionInfo.Current(), EvidenceSearchOverride);
        if (startWithEvidence && ConfirmEvidenceReviewOverride != null)
        {
            var choice = ConfirmEvidenceReviewOverride(session);
            if (choice != null) await ApplyEvidenceChoice(session, choice);
            return false;
        }
        Window? owner = Window.GetWindow(this);
        if (owner == null) return false;
        var window = new SuperClusterPreviewWindow(preview, null, session.Propose, session.CountMotif,
            evidenceMode: startWithEvidence, confirmPanelLinks: session.ConfirmPanelLinks,
            discoverTaskWithProgress: session.DiscoverTaskAsync, previewScope: session.PreviewScopeAsync, addToFamily: session.AddToFamily,
            removeFromFamily: session.RemoveFromFamily, updateManualChoices: session.UpdateManualChoices,
            createMatchContext: session.CreatePanelMatchContext, confirmPanelMatches: session.ConfirmPanelEvidenceMatches) { Owner = owner };
        window.AlignmentSource = highlighted => FamilyQuant.Evidence.Alignment.AlignmentCandidates.Find(fcf, session.Proposal, highlighted);
        if (importedFamilyChecklistRequested) window.RequestFamilyChecklist();
        window.ShowDialog();
        if (!window.Confirmed) return false;
        if (!window.IsEvidenceMode) return true;
        await ApplyEvidenceChoice(session, window.EvidenceSelection!);
        return false;
    }

    private async Task ApplyEvidenceChoice(EvidenceGroupingSession session, EvidenceReviewSelection choice)
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
            ShowGroupingCompletion(result.Recipe.Assignments.Count.ToString("N0") + " proteins grouped in "
                + result.Recipe.Assignments.Select(a => a.FamilyId).Distinct().Count().ToString("N0") + " families. Undo restores the previous grouping.");
        }
        catch (Exception ex)
        {
            ShowCurationOperationResult((committed ? "The grouping was applied, but the view could not refresh: " : "No grouping was applied: ") + ex.Message);
        }
        finally { EndWait(); }
        if (committed && session.Proposal?.PanelContext != null)
        {
            bool labelled = await SubclassAssignmentRunner.RecomputeSubclassesAsync(this, combineWithPreviousUndo: true);
            if (!labelled) ShowCurationOperationResult("Family assignments were applied; subclass recomputation did not finish. Undo restores the whole grouping.");
        }
    }

    private MessageBoxResult ShowCurationMessage(string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShellMessages.Report(this, message, title, buttons, image, defaultResult, SetCurationOperationStatus);

    private void ShowCurationOperationResult(string message) =>
        ShellMessages.Report(this, message, WorkflowNames.CurateFamilies, status: SetCurationOperationStatus, showDialog: false);

    private void SetCurationOperationStatus(string message)
    {
        statusTooltipBeforeSave ??= TextBlockStatusLine.ToolTip;
        saveResultStatus = message;
        TextBlockStatusLine.Text = message;
        TextBlockStatusLine.ToolTip = statusTooltipBeforeSave + "\n\n" + message;
        PublishShellStatus();
    }
}
