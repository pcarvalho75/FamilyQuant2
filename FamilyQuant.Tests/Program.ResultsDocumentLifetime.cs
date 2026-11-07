using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsDocumentLifetimeCommands() =>
    [new("--test-results-document-lifetime", 21118, TestCommandPhase.Raw,
        args => args is ["--test-results-document-lifetime", _], (args, _) =>
        {
            ResultsDocumentLifetimeTests(Path.GetFullPath(args[1]));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ResultsDocumentLifetimeTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var source = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "SYNTHETIC_LIFETIME", Description = "Synthetic lifetime fixture",
                Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 }
        } };
        string first = Path.Combine(output, "first.fcf"), second = Path.Combine(output, "second.fqproj");
        source.Save(first);
        var active = new CurationViewModel(source);
        active.RefreshDocument();
        active.Families.Single().IsLocked = true;
        Check("active family row still applies its lock through the callback", source.LockedUserIds.Contains(1) && active.CanUndo);
        active.Undo();
        Check("family row lock callback remains undoable", !source.LockedUserIds.Contains(1));
        ProjectStore.WriteFqproj(second, source, new[] { Qr("identified", "synthetic-lifetime", 1, 2, "PEPTIDEK", 100, 100) },
            new QuantManifest { PerRunTic = new() { ["synthetic-lifetime"] = 1000 } });
        var previousChoice = MainWindow.UnsavedProjectChoiceOverride;
        var previousMessages = ShellMessages.Presenter;
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        var shell = new MainWindow { Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
            Width = 1024, Height = 768, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            shell.Show(); PumpDispatcher();
            foreach (bool asynchronous in new[] { false, true })
            {
                var previous = ReplaceLifetimeDocument(shell, first, second, asynchronous);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    PumpDispatcher(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                }
                bool released = !previous.File.TryGetTarget(out var retained);
                Check("native replacement releases its previous document, async=" + asynchronous, released);
                if (retained != null) TraceResultsDocumentOwner(shell, retained);
                Check("native replacement releases its previous control, async=" + asynchronous, !previous.Control.TryGetTarget(out _));
            }
            Check("replacement keeps the current document and stored Results available",
                shell.NavigationModel.Document?.MyExtendedFastaItems.Count == 1 && shell.NavigationModel.HasStoredResults);
            var current = shell.NavigationModel.Document;
            string corrupt = Path.Combine(output, "corrupt.fqproj"); File.WriteAllText(corrupt, "invalid synthetic project");
            Check("failed synchronous open preserves the current document", !shell.OpenProjectPath(corrupt)
                && ReferenceEquals(current, shell.NavigationModel.Document));
            var failedOpen = shell.OpenProjectSafelyAsync(corrupt); FinishBusyTask(failedOpen);
            Check("failed asynchronous open preserves the current document", !failedOpen.Result
                && ReferenceEquals(current, shell.NavigationModel.Document));
        }
        finally
        {
            shell.Close(); PumpDispatcher(); MainWindow.UnsavedProjectChoiceOverride = previousChoice;
            ShellMessages.Presenter = previousMessages;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<FamilyClusterFile> File, WeakReference<ClusterGenerationControl> Control)
        ReplaceLifetimeDocument(MainWindow shell, string first, string second, bool asynchronous)
    {
        Check("lifetime fixture opens its first native document", shell.OpenProjectPath(first));
        var control = ((Grid)shell.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
        FinishBusyTask(control.FullXicReady); shell.UpdateLayout(); PumpDispatcher();
        var file = new WeakReference<FamilyClusterFile>(control.fcf);
        var view = new WeakReference<ClusterGenerationControl>(control);
        bool opened;
        if (asynchronous) { var task = shell.OpenProjectSafelyAsync(second); FinishBusyTask(task); opened = task.Result; }
        else opened = shell.OpenProjectPath(second);
        Check("lifetime fixture opens replacement stored Results", opened);
        var replacement = ((Grid)shell.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
        FinishBusyTask(replacement.FullXicReady); shell.UpdateLayout(); PumpDispatcher();
        return (file, view);
    }
}
