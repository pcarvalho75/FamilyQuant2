using FamilyQuant.Examples;
using FamilyQuant.Operations;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportPreparationCommands() =>
    [new("--test-import-preparation", 100, TestCommandPhase.Raw,
        args => args.Contains("--test-import-preparation", StringComparer.Ordinal), (args, _) =>
        {
            ImportPreparationTests(args.Length == 2 ? args[1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReadyImportPreparation(FamilyListImportViewModel model, string workbook)
    {
        FinishReviewWork(() => model.LoadAsync(workbook));
        FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(_ => true); });
        for (int attempt = 0; attempt < 100 && model.SpecialRows.FirstOrDefault(r => r.Blocking) is { } row; attempt++)
        { model.SelectedSpecialRow = row; row.Action = row.SeveralNames ? FamilyPanelRowAction.Split : FamilyPanelRowAction.Import; model.UseRowChoice(); }
        if (!model.CanNext) throw new InvalidOperationException("The preparation fixture did not reach a resolved special-row page.");
    }

    private sealed class PreparationMatcher(FamilyClusterFile source) : IFamilyListImportMatches
    {
        private readonly FamilyListStoredEvidenceMatches inner = new(source);
        public int Calls;
        public string SourceStatus => inner.SourceStatus;
        public string ChangeNotice => inner.ChangeNotice;
        public IReadOnlyList<ImportMatchFamily> Suggest(FamilyClusterFile project, FamilyPanel panel)
        { Calls++; return inner.Suggest(project, panel); }
        public string ConfirmationWarning(IReadOnlyList<ImportEvidenceMatch> choices) => inner.ConfirmationWarning(choices);
        public FamilyPanel Accept(FamilyPanel panel, IReadOnlyList<ImportMatchFamily> choices) => inner.Accept(panel, choices);
    }

    private static void ImportPreparationTests(string? output = null)
    {
        Section("Z12p5f6 slow suggestion copies keep the wizard responsive and cancellation atomic");
        EnsureApplication();
        using var dialogs = new ImportFixtureDialogs();
        string work = Path.Combine(Path.GetTempPath(), "fq-import-preparation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work); string workbook = Path.Combine(work, "example.xlsx"); ExampleFamilyList.Write(workbook);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var file = ManualChoiceFile(); var curation = new CurationViewModel(file); var matcher = new PreparationMatcher(file);
                using var gate = new ManualResetEventSlim(); int copyThread = 0, copies = 0;
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(curation), matcher: matcher, copyProject: (source, context) =>
                { copies++; copyThread = Environment.CurrentManagedThreadId; started.TrySetResult(); gate.Wait(); return source.DeepCopy(); });
                var window = new FamilyListImportWindow(model)
                { Width = 1366, Height = 768, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                Task? fallback = null; Task<bool>? next = null;
                try
                {
                    window.Show(); ReadyImportPreparation(model, workbook);
                    string draft = JsonSerializer.Serialize(model.DraftPanel), sourceBefore = file.ToJsonText();
                    int uiThread = Environment.CurrentManagedThreadId; bool tick = false;
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => tick = true));
                    fallback = Task.Run(async () => { await Task.Delay(2000); if (Volatile.Read(ref copyThread) == uiThread) gate.Set(); });
                    var previousContext = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                    var watch = Stopwatch.StartNew();
                    try { next = model.NextAsync(); }
                    finally { watch.Stop(); SynchronizationContext.SetSynchronizationContext(previousContext); }
                    PumpDispatcher();
                    Check(theme + " slow preparation returns promptly with busy state and the dispatcher available", watch.ElapsedMilliseconds < 500
                        && !next.IsCompleted && model.IsBusy && tick && !model.CanNext && !model.CanBack && !model.CanEdit);
                    FinishReviewWork(async () => { await started.Task; });
                    Check(theme + " the real copy runs away from the dispatcher", copyThread != uiThread && copies == 1);
                    if (output != null && model.IsBusy)
                        CaptureFamilyListPage(window, output, theme, 1366, 768, "04-preparing-matches");
                    Task cancelled = model.IsBusy ? model.CancelReadAsync() : Task.CompletedTask;
                    window.Close(); PumpDispatcher(); gate.Set();
                    FinishReviewWork(async () => { await cancelled; await next; }); PumpDispatcher();
                    Check(theme + " Cancel waits for preparation and refuses its late result", !window.IsVisible && !model.IsBusy && !next.Result
                        && model.Page == 3 && model.MatchFamilies.Count == 0 && matcher.Calls == 0);
                    Check(theme + " Cancel preserves every draft choice and the full project", JsonSerializer.Serialize(model.DraftPanel) == draft
                        && file.ToJsonText() == sourceBefore && curation.UndoStack.Count == 0 && !model.Imported);
                }
                finally
                {
                    gate.Set();
                    if (next != null) FinishReviewWork(async () => { await next; });
                    if (fallback != null) FinishReviewWork(() => fallback);
                    if (window.IsVisible) window.Close(); PumpDispatcher();
                }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }

        var successful = ManualChoiceFile(); int completedCopies = 0; bool copyEqual = false;
        using var complete = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(successful)), copyProject: (source, context) =>
        {
            var clone = source.DeepCopy(context); completedCopies++;
            copyEqual = !ReferenceEquals(source, clone) && clone.ToJsonText() == FamilyClusterFile.FromJsonText(source.ToJsonText()).ToJsonText();
            return clone;
        });
        ReadyImportPreparation(complete, workbook);
        var expected = new FamilyListStoredEvidenceMatches(successful).Suggest(FamilyClusterFile.FromJsonText(successful.ToJsonText()), complete.DraftPanel!);
        FinishReviewWork(async () => { await complete.NextAsync(); });
        Check("completed streamed preparation equals the old copy and shared suggestions", copyEqual && completedCopies == 1 && complete.Page == 4
            && complete.MatchFamilies.Select(f => f.Key).SequenceEqual(expected.Select(f => f.Key)));
        Check("every successful suggestion retains exact values counts and initial ticks", complete.MatchFamilies.All(f =>
            f.Suggestions.Select(s => (s.Key, s.Text, s.Proteins, s.Reviewed, s.Use)).SequenceEqual(expected.Single(e => e.Key == f.Key).Suggestions.Select(s => (s.Key, s.Text, s.Proteins, s.Reviewed, s.Use)))));
        complete.Back(); FinishReviewWork(async () => { await complete.NextAsync(); });
        Check("Back and Next reuse prepared matching without another copy", completedCopies == 1 && complete.Page == 4);

        int attempts = 0;
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var retry = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())), copyProject: (source, context) =>
        {
            int attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            { retryStarted.TrySetResult(); context.CancellationToken.WaitHandle.WaitOne(); context.ThrowIfCancellationRequested(); }
            if (attempt == 2) throw new IOException("Supplied preparation failure.");
            return source.DeepCopy(context);
        });
        ReadyImportPreparation(retry, workbook); string beforeRetry = JsonSerializer.Serialize(retry.DraftPanel); bool first = true;
        FinishReviewWork(async () =>
        {
            var pending = retry.NextAsync(); await retryStarted.Task;
            await retry.CancelReadAsync(); first = await pending;
        });
        Check("a cancelled preparation leaves the draft and retry step intact", !first && retry.Page == 3 && !retry.IsBusy
            && retry.MatchFamilies.Count == 0 && JsonSerializer.Serialize(retry.DraftPanel) == beforeRetry);
        bool second = true; FinishReviewWork(async () => { second = await retry.NextAsync(); });
        Check("a failed copy is reported without consuming the matching preparation", !second && retry.Page == 3 && !retry.IsBusy
            && retry.MatchFamilies.Count == 0 && retry.Status.Contains("Supplied preparation failure", StringComparison.Ordinal));
        FinishReviewWork(async () => { await retry.NextAsync(); });
        Check("retry after cancellation and failure performs a fresh copy and publishes suggestions", attempts == 3 && retry.Page == 4
            && retry.MatchFamilies.Count > 0 && !retry.Imported);
        ImportPreparationBroadMatchTests(output);
    }
}
