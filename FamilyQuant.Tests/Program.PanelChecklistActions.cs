using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelChecklistActionsCommandTable() =>
    [
        new("--test-panel-checklist-actions", 74, TestCommandPhase.Raw, args => args.Contains("--test-panel-checklist-actions", StringComparer.Ordinal), (args, _) =>
        {
            PanelChecklistActionsTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.PanelChecklistActions"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static FamilyClusterFile ChecklistActionFile()
    {
        var file = ChecklistFile();
        file.FamilyPanel!.Families.RemoveAll(family => family.Key == "narrow");
        return file;
    }

    private static void PanelChecklistActionsTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        ChecklistPreviewLayoutWaitTests();
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (1366, 768), (1920, 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var file = ChecklistActionFile(); string before = file.ToJsonText();
                var session = new EvidenceGroupingSession(file, "checklist action fixture");
                var input = session.Propose("selected"); int updates = 0;
                var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
                var window = new SuperClusterPreviewWindow(preview, input, scope => { updates++; return session.Propose(scope); },
                    initialScope: "selected", evidenceMode: true, previewScope: session.PreviewScopeAsync,
                    addToFamily: session.AddToFamily, removeFromFamily: session.RemoveFromFamily, updateManualChoices: session.UpdateManualChoices)
                    { Width = size.Item1, Height = size.Item2, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    ((TabItem)window.FindName("PanelChecklistTab")).IsSelected = true;
                    model.Evidence!.SelectedChecklistFamily = model.Evidence.PanelChecklist.Single(row => row.Key == "missing");
                    window.UpdateLayout(); PumpDispatcher();
                    var add = (Button)window.FindName("ChecklistAddScopeButton");
                    var hand = (Button)window.FindName("ChecklistPlaceHandButton");
                    Check("Z12n2 " + theme + " " + size.Item1 + " missing family exposes its stored phrase and explicit hand action",
                        model.Evidence.SelectedChecklistPhrase is { Text: "alternate activity", Targets: 2 } && model.CanAddChecklistPhrase && model.CanPlaceChecklistMatches
                        && add.IsVisible && hand.IsVisible);
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    FinishReviewWork(() => model.ScopePreviewCompletion); window.UpdateLayout(); PumpDispatcher();
                    Check("Z12n2 " + theme + " " + size.Item1 + " scope action previews stored evidence and requires explicit proposal update",
                        WordCluster.ParseKeywords(model.ScopeText).Contains("alternate activity") && model.ScopeChangePreview?.Proteins == 2
                        && updates == 0 && !model.CanConfirm && !model.CanPlaceChecklistMatches && file.ToJsonText() == before);
                    var inlinePreview = (TextBox)window.FindName("ChecklistScopePreview");
                    WaitForChecklistPreviewLayout(window, inlinePreview, "Z12n2 " + theme + " " + size.Item1, TimeSpan.FromSeconds(10));
                    Check("Z12n2 " + theme + " " + size.Item1 + " scope preview is readable next to checklist actions",
                        inlinePreview.IsVisible && inlinePreview.Text.Contains(model.ScopePreviewSummary, StringComparison.Ordinal)
                        && inlinePreview.ActualHeight >= 48 && inlinePreview.TransformToAncestor(window).Transform(new Point(0, inlinePreview.ActualHeight)).Y <= window.ActualHeight);
                    ScreenHarness.Save(window, Path.Combine(output, "scope-action-" + theme + "-" + size.Item1 + ".png"), size.Item1, size.Item2);
                    FinishReviewWork(model.UpdateProposalAsync); window.UpdateLayout(); PumpDispatcher();
                    Check("Z12n2 " + theme + " " + size.Item1 + " Update keeps the selected checklist family and enables current hand choices",
                        updates == 1 && model.Evidence!.SelectedChecklistFamily?.Key == "missing" && model.CanPlaceChecklistMatches);
                    int dialogs = 0;
                    window.ManualFamilyDialogOverride = editor => { dialogs++; return false; };
                    hand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check("Z12n2 " + theme + " " + size.Item1 + " cancelling confirmation preserves the proposal and source",
                        dialogs == 1 && session.Proposal!.ManualChoices?.Count is not > 0 && file.ToJsonText() == before);
                    bool rightTarget = false;
                    window.ManualFamilyDialogOverride = editor =>
                    {
                        dialogs++; rightTarget = editor.SelectedOption?.Key == "missing" && editor.Proteins.Select(protein => protein.ProteinIndex).SequenceEqual(new[] { 3, 5 });
                        editor.Note = "Supplied checklist decision"; return true;
                    };
                    hand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout(); PumpDispatcher();
                    var selected = model.Evidence!.SelectedChecklistFamily;
                    Check("Z12n2 " + theme + " " + size.Item1 + " confirmation adds missing stored panel family through hand contract without applying",
                        rightTarget && dialogs == 2 && selected is { Key: "missing", Placed: 2, Outside: 0 }
                        && session.Proposal!.ManualChoices?.Count == 2 && model.Evidence.Proteins.Where(row => row.ProteinIndex is 3 or 5).All(row => row.Tier == "Added by hand" && row.Selected)
                        && file.ToJsonText() == before);
                    ScreenHarness.Save(window, Path.Combine(output, "hand-action-" + theme + "-" + size.Item1 + ".png"), size.Item1, size.Item2);
                    var applied = session.Apply(model.Evidence.CaptureSelection() with { ProteinIndices = new[] { 3, 5 } });
                    string after = file.ToJsonText();
                    Check("Z12n2 " + theme + " " + size.Item1 + " normal Apply records hand proof, source binding and note",
                        applied.Changed && applied.Recipe.Assignments.All(assignment => assignment.Family == "missing" && assignment.Tier == EvidenceTier.AddedByHand && assignment.AutomaticEvidence != null)
                        && applied.Recipe.ManualChoices!.All(choice => choice.Note == "Supplied checklist decision"));
                    applied.Undo!.Restore(file);
                    bool exactUndo = file.ToJsonText() == before;
                    EvidenceApply.ApplyRecipe(file, applied.Recipe);
                    Check("Z12n2 " + theme + " " + size.Item1 + " Undo and saved checklist hand recipe are byte exact", exactUndo && file.ToJsonText() == after);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }

        var locked = ChecklistActionFile(); locked.LockedUserIds.Add(1);
        var lockedSession = new EvidenceGroupingSession(locked, "locked checklist fixture");
        var lockedInput = lockedSession.Propose("selected");
        var lockedModel = new GroupingReviewViewModel(new SuperClusterPreview(locked.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, locked.LockedUserIds),
            lockedInput, lockedSession.Propose, initialScope: "selected", evidenceMode: true,
            addToFamily: lockedSession.AddToFamily, removeFromFamily: lockedSession.RemoveFromFamily);
        lockedModel.Evidence!.SelectedChecklistFamily = lockedModel.Evidence.PanelChecklist.Single(row => row.Key == "missing");
        string lockedBefore = locked.ToJsonText(); bool refused = false;
        try { lockedSession.AddToFamily(new EvidenceManualRequest(new[] { 3, 5 }, "missing", "Source group missing", false)); }
        catch (InvalidOperationException) { refused = true; }
        Check("Z12n2 mixed locked matches refuse hand placement atomically and explain unlocking", refused && !lockedModel.CanPlaceChecklistMatches
            && lockedModel.ChecklistHandHint.Contains("Unlock", StringComparison.Ordinal) && locked.ToJsonText() == lockedBefore);
        lockedModel.Evidence.SelectedChecklistFamily = lockedModel.Evidence.PanelChecklist.Single(row => row.Key == "excluded");
        Check("Z12n2 excluded and unmatched families offer no implicit assignment", !lockedModel.CanPlaceChecklistMatches && !lockedModel.CanAddChecklistPhrase);
        Check("Z12n2 Help explains consensus import, stable keys, scope preview and explicit hand choices",
            GroupingReviewHelp.Text.Contains("File > Import family list", StringComparison.Ordinal)
            && GroupingReviewHelp.Text.Contains("Added by hand", StringComparison.Ordinal) && GroupingReviewHelp.Text.Contains("Add phrase to scope", StringComparison.Ordinal));
        var guarded = ChecklistActionFile();
        var guardedSession = new EvidenceGroupingSession(guarded, "guarded panel choice fixture"); guardedSession.Propose("selected");
        foreach (string key in new[] { "excluded", "neither-proposed-nor-stored" })
        {
            string sourceBefore = guarded.ToJsonText(), proposalBefore = JsonSerializer.Serialize(guardedSession.Proposal);
            refused = false;
            try { guardedSession.AddToFamily(new EvidenceManualRequest(new[] { 3, 5 }, key, "Supplied target", false)); }
            catch (ArgumentException) { refused = true; }
            Check("Z12n2-fix session refuses " + key + " without changing file or proposal", refused && guarded.ToJsonText() == sourceBefore
                && JsonSerializer.Serialize(guardedSession.Proposal) == proposalBefore);
        }
        var punctuation = ChecklistActionFile();
        foreach (int index in new[] { 3, 5 }) punctuation.MyExtendedFastaItems[index].Evidence!.Keywords = new() { "alternate, activity" };
        var punctuationSession = new EvidenceGroupingSession(punctuation, "punctuation fixture");
        var punctuationInput = punctuationSession.Propose("selected");
        var punctuationModel = new GroupingReviewViewModel(new SuperClusterPreview(punctuation.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, punctuation.LockedUserIds),
            punctuationInput, punctuationSession.Propose, initialScope: "selected", evidenceMode: true);
        punctuationModel.Evidence!.SelectedChecklistFamily = punctuationModel.Evidence.PanelChecklist.Single(row => row.Key == "missing");
        punctuationModel.Evidence.SelectedChecklistPhrase = punctuationModel.Evidence.ChecklistPhrases.Single(phrase => phrase.Text == "alternate, activity");
        bool initiallyAvailable = punctuationModel.CanAddChecklistPhrase;
        punctuationModel.AddChecklistPhrase(); string scopeAfter = punctuationModel.ScopeText;
        punctuationModel.AddChecklistPhrase();
        Check("Z12n2-fix punctuated phrase compares parsed forms and disables repeat Add", initiallyAvailable && !punctuationModel.CanAddChecklistPhrase
            && WordCluster.ParseKeywords(scopeAfter).Contains("alternate activity") && punctuationModel.ScopeText == scopeAfter);
    }

    private static int WaitForChecklistPreviewLayout(Window window, TextBox preview, string context, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        window.Dispatcher.VerifyAccess();
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var deadline = new DispatcherTimer(DispatcherPriority.Send, window.Dispatcher) { Interval = timeout };
        DispatcherOperation? pendingRead = null;
        ChecklistPreviewLayout? previous = null;
        ChecklistPreviewLayout last = default;
        Exception? measurementError = null;
        TimeSpan? lastRenderTime = null;
        int samples = 0;
        bool settled = false, readQueued = false;
        void StopAtDeadline(object? sender, EventArgs args) => frame.Continue = false;
        void ReadAfterRender(object? sender, EventArgs args)
        {
            if (readQueued || settled) return;
            if (args is RenderingEventArgs rendering)
            {
                if (lastRenderTime == rendering.RenderingTime) return;
                lastRenderTime = rendering.RenderingTime;
            }
            readQueued = true;
            pendingRead = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    window.UpdateLayout();
                    last = new ChecklistPreviewLayout(preview.IsVisible, preview.Text, preview.ActualWidth, preview.ActualHeight,
                        preview.TransformToAncestor(window).Transform(new Point(0, preview.ActualHeight)).Y,
                        window.ActualWidth, window.ActualHeight,
                        window.IsLoaded && preview.IsLoaded && window.IsMeasureValid && window.IsArrangeValid
                            && preview.IsMeasureValid && preview.IsArrangeValid);
                    samples++;
                    // Separate render/idle passes must agree; a stable unreadable size still reaches the original assertion.
                    settled = last.Visible && last.LayoutValid && previous == last;
                    previous = last;
                    if (settled) frame.Continue = false;
                }
                catch (Exception error) { measurementError = error; frame.Continue = false; }
                finally { readQueued = false; }
            }));
        }
        try
        {
            CompositionTarget.Rendering += ReadAfterRender;
            deadline.Tick += StopAtDeadline; deadline.Start();
            window.InvalidateVisual(); Dispatcher.PushFrame(frame);
        }
        finally
        {
            deadline.Stop(); deadline.Tick -= StopAtDeadline;
            CompositionTarget.Rendering -= ReadAfterRender;
            pendingRead?.Abort(); elapsed.Stop();
        }
        if (measurementError != null) throw new InvalidOperationException(context + " layout measurement failed for " + preview.Name, measurementError);
        if (!settled)
        {
            string diagnostic = FormattableString.Invariant($"visible={last.Visible}, layoutValid={last.LayoutValid}, textLength={last.Text?.Length ?? 0}, ");
            diagnostic += FormattableString.Invariant($"size={last.Width:R}x{last.Height:R}, bottom={last.Bottom:R}, window={last.WindowWidth:R}x{last.WindowHeight:R}.");
            throw new TimeoutException(FormattableString.Invariant(
                $"{context}: {preview.Name} layout did not settle after {elapsed.Elapsed.TotalMilliseconds:F0} ms; samples={samples}, ") + diagnostic);
        }
        Console.WriteLine(FormattableString.Invariant($"  {context}: layout settled after {samples} render/idle reads in {elapsed.Elapsed.TotalMilliseconds:F0} ms."));
        return samples;
    }

    private readonly record struct ChecklistPreviewLayout(bool Visible, string Text, double Width, double Height,
        double Bottom, double WindowWidth, double WindowHeight, bool LayoutValid);

    private static void ChecklistPreviewLayoutWaitTests()
    {
        var preview = new TextBox { Name = "DelayedChecklistPreview", Text = "Synthetic delayed preview", Height = 72,
            Visibility = Visibility.Hidden };
        var window = new Window { Content = preview, Width = 360, Height = 180, ShowActivated = false, ShowInTaskbar = false };
        bool revealed = false;
        var reveal = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        reveal.Tick += (_, _) => { reveal.Stop(); revealed = true; preview.Visibility = Visibility.Visible; };
        try
        {
            window.Show(); reveal.Start();
            int samples = WaitForChecklistPreviewLayout(window, preview, "Z12n2b delayed preview", TimeSpan.FromSeconds(10));
            Check("Z12n2b delayed preview waits for two matching visible layout reads", revealed && preview.IsVisible
                && preview.ActualHeight == 72 && samples >= 2);
            preview.Name = "NeverReadyChecklistPreview"; preview.Visibility = Visibility.Hidden;
            bool failedLoudly = false;
            try { WaitForChecklistPreviewLayout(window, preview, "Z12n2b never-ready preview", TimeSpan.FromMilliseconds(200)); }
            catch (TimeoutException error)
            {
                failedLoudly = error.Message.Contains("NeverReadyChecklistPreview", StringComparison.Ordinal)
                    && error.Message.Contains("did not settle", StringComparison.Ordinal)
                    && error.Message.Contains("samples=", StringComparison.Ordinal);
            }
            Check("Z12n2b a never-ready preview fails with bounded layout diagnostics", failedLoudly);
        }
        finally { reveal.Stop(); window.Close(); PumpDispatcher(); }
    }
}
