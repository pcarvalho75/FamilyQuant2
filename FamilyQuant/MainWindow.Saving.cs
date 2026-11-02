using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace FamilyQuant;

public partial class MainWindow
{
    public ShellDocumentState DocumentState { get; } = new();
    public static Func<UnsavedProjectChoice>? UnsavedProjectChoiceOverride { get; set; }
    private string cleanWindowTitle = "";
    private bool approvedClose, leavePromptRunning;
    private ClusterGenerationControl? trackedCuration;
    private EventInfo? documentChangedEvent;
    private EventHandler? documentChangedHandler;
    private bool dirtyCheckQueued;

    private void InitializeDocumentLifecycle()
    {
        cleanWindowTitle = Title;
        DocumentState.PropertyChanged += (_, _) =>
        {
            Title = cleanWindowTitle + (DocumentState.IsModified ? " *" : "");
            UpdateGuide();
        };
        Closing += WindowClosing;
    }

    private void TrackCuration(ClusterGenerationControl control)
    {
        if (ReferenceEquals(trackedCuration, control)) return;
        if (trackedCuration != null && documentChangedHandler != null)
            documentChangedEvent?.RemoveEventHandler(trackedCuration, documentChangedHandler);
        trackedCuration = control;
        documentChangedEvent = control.GetType().GetEvent("DocumentChanged");
        documentChangedHandler = (_, _) => DocumentState.MarkCurationChanged();
        documentChangedEvent?.AddEventHandler(control, documentChangedHandler);
        // Compatibility until the curation change event lands. Only committed cell edits and
        // mutating commands schedule a check; search, filters and sorting do not serialize the file.
        control.DataGridResultGrid.CellEditEnding += (_, args) =>
        {
            if (args.EditAction == DataGridEditAction.Commit) QueueDirtyCheck(control);
        };
        control.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((sender, args) =>
        {
            if (args.OriginalSource is not Button button) return;
            if (button.Name is "ButtonUndo" or "ButtonManageLocks" or "ButtonSuperCluster" or
                "ButtonWordCluster" or "ButtonApplyToSelected" or "ButtonFetchDomains" or
                "ButtonLoadQuantData" or "ButtonRecalculateQuantitation") QueueDirtyCheck(control);
        }));
    }

    private void QueueDirtyCheck(ClusterGenerationControl control)
    {
        if (dirtyCheckQueued) return;
        dirtyCheckQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            dirtyCheckQueued = false;
            if (ReferenceEquals(cgc, control)) DocumentState.CheckForChanges(control.fcf);
        }));
    }

    public Task<bool> OpenProjectSafelyAsync(string path) => AppOperationHost.OnUi(this, () => OpenProjectSafelyCoreAsync(path));
    private async Task<bool> OpenProjectSafelyCoreAsync(string path)
    {
        if (!await ConfirmCanReplaceAsync()) return false;
        return await documentCommands.OpenProjectPathAsync(path);
    }

    private async Task<bool> ConfirmCanReplaceAsync()
    {
        if (leavePromptRunning || saveInProgress || IsQuantifyBusy || Operations.IsBusy) return false;
        if (cgc == null || !DocumentState.CheckForChanges(cgc.fcf)) return true;
        leavePromptRunning = true;
        try
        {
            UnsavedProjectChoice choice;
            if (UnsavedProjectChoiceOverride != null) choice = UnsavedProjectChoiceOverride();
            else if (PresentationSource.FromVisual(this) == null) return false;
            else
            {
                MessageBoxResult answer = MessageBox.Show(this, "Save changes to the open project before continuing?",
                    "Unsaved project", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                choice = answer == MessageBoxResult.Yes ? UnsavedProjectChoice.Save
                    : answer == MessageBoxResult.No ? UnsavedProjectChoice.Discard : UnsavedProjectChoice.Cancel;
            }
            if (choice == UnsavedProjectChoice.Cancel) return false;
            if (choice == UnsavedProjectChoice.Discard) return true;
            await SaveProjectAsync(saveAs: false);
            return !DocumentState.IsModified;
        }
        finally { leavePromptRunning = false; }
    }

    private async void WindowClosing(object? sender, CancelEventArgs args)
    {
        if (Operations.IsBusy)
        {
            args.Cancel = true;
            Operations.Cancel();
            StatusModel.ShowMessage("Cancelling the current operation. Close again after it finishes.");
            return;
        }
        if (IsQuantifyBusy)
        {
            args.Cancel = true;
            quantify!.ViewModel.CancelCommand.Execute(null);
            StatusModel.ShowMessage("Cancelling Quantify. Close again after the current phase finishes.");
            return;
        }
        if (approvedClose || PresentationSource.FromVisual(this) == null && UnsavedProjectChoiceOverride == null) return;
        Task<bool> decision = ConfirmCanReplaceAsync();
        if (decision.IsCompletedSuccessfully)
        {
            args.Cancel = !decision.Result;
            return;
        }
        args.Cancel = true;
        if (!await decision) return;
        approvedClose = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
