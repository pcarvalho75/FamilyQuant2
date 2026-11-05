using FamilyQuant.Ai;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    private AiKeyOffer? aiKeyOffer;
    public static Func<AiKeyStore>? AiKeyStoreOverride { get; set; }
    public static Func<IAiTransport>? AiTransportOverride { get; set; }
    public static Func<AiInterpretationInput, string, bool>? AiPreviewOverride { get; set; }
    public static Func<AiInterpretationDraft, IReadOnlyList<string>, bool>? AiReviewOverride { get; set; }
    public Task<bool>? AiDraftTask { get; private set; }

    private async void MenuItemAiDraft_Click(object sender, RoutedEventArgs e)
    {
        AiDraftTask = DraftInterpretationAsync();
        await AiDraftTask;
    }

    public Task<bool> DraftInterpretationAsync() => AppOperationHost.OnUi(this, DraftInterpretationCoreAsync);

    private async Task<bool> DraftInterpretationCoreAsync()
    {
        if (cgc == null || loadedManifest == null || loadedQuant.Count == 0 || Operations.IsBusy || saveInProgress || reportExportInProgress)
        {
            ShellMessages.Report(this, "Open a project with stored Results and wait for the current operation before drafting an interpretation.", "AI interpretation");
            return false;
        }
        if (loadedManifest.FamilyRunShares == null)
        {
            ShellMessages.Report(this, "The project has no stored family shares. Use Quantify and save the project first.", "AI interpretation");
            return false;
        }
        var project = cgc;
        var manifest = loadedManifest;
        var rows = loadedQuant;
        try
        {
            aiKeyOffer ??= new AiKeyOffer(AiKeyStoreOverride?.Invoke() ?? new AiKeyStore());
            string? key = aiKeyOffer.EnsureKey(lastFcfPath, () => ShellMessages.Report(this,
                "A key file is beside this project. Import it for this Windows user? The file will remain in place.",
                "AI assistant key", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
            if (string.IsNullOrWhiteSpace(key))
            {
                ShellMessages.Report(this, "Add or import a key with Help > AI assistant key before requesting an AI draft.", "AI interpretation");
                return false;
            }
            if (!ShellDialogs.IsDisplayed(this))
            {
                ShellMessages.Report(this, "An AI draft needs a displayed application window for review and consent.", "AI interpretation", showDialog: false);
                return false;
            }
            using var operation = Operations.Begin("Draft interpretation with AI");
            await project.FullXicReady.WaitAsync(operation.Context.CancellationToken);
            operation.Context.Report("ai-input", "Preparing project information for your review", 0, 1);
            var input = await Task.Run(() => AiInterpretationInputBuilder.BuildProject(project.fcf, manifest), operation.Context.CancellationToken);
            operation.Context.Report("ai-input", "Project information ready for your review", 1, 1);
            bool Current()
            {
                if (!ReferenceEquals(cgc, project) || !ReferenceEquals(loadedManifest, manifest) || !ReferenceEquals(loadedQuant, rows)) return false;
                try { return AiInterpretationInputBuilder.BuildProject(project.fcf, manifest).Text == input.Text; }
                catch (Exception) { return false; }
            }
            var workflow = new AiInterpretationWorkflow(AiTransportOverride?.Invoke() ?? new AiTransport());
            var outcome = await workflow.RunAsync(key, input,
                (shown, model) => AiPreviewOverride?.Invoke(shown, model) ?? (new AiPreviewWindow(shown.Text, model) { Owner = this }.ShowDialog() == true),
                (draft, missing) => AiReviewOverride?.Invoke(draft, missing) ?? (new AiDraftReviewWindow(draft, missing) { Owner = this }.ShowDialog() == true),
                Current,
                draft => operation.Context.Commit(() => project.ApplyReportDetails(draft.Apply(project.fcf.ReportDetails ?? new ReportDetails()))),
                operation.Context.CancellationToken);
            if (outcome == AiWorkflowOutcome.Accepted)
            {
                StatusModel.ShowMessage("AI draft added to Report details. Undo restores the previous text; save the project to keep it.");
                return true;
            }
            if (outcome != AiWorkflowOutcome.Cancelled)
                ShellMessages.Report(this, AiDraftMessage(outcome), "AI interpretation");
            return false;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception) { ShellMessages.Report(this, "Could not prepare or complete the AI draft. Check your project and try again.", "AI interpretation"); return false; }
    }

    private static string AiDraftMessage(AiWorkflowOutcome outcome) => outcome switch
    {
        AiWorkflowOutcome.MissingKey => "Add or import a key with Help > AI assistant key.",
        AiWorkflowOutcome.WrongKey => "The key was not accepted. Check Help > AI assistant key.",
        AiWorkflowOutcome.Billing => "The account has a billing or credit problem.",
        AiWorkflowOutcome.RateLimited => "Rate limited. Try again later.",
        AiWorkflowOutcome.NoConnection => "Could not connect to the AI service.",
        AiWorkflowOutcome.Refusal => "The AI service declined this request; no draft was added.",
        AiWorkflowOutcome.Incomplete => "The response was incomplete; no draft was added.",
        AiWorkflowOutcome.Stale => "The project changed during the request; no draft was added. Try again.",
        _ => "The AI service could not complete the request; no draft was added.",
    };

    private void MenuItemAiKey_Click(object sender, RoutedEventArgs e)
    {
        var window = new AiKeySettingsWindow(new AiKeyStore()) { Owner = this };
        window.ShowDialog();
    }
}
