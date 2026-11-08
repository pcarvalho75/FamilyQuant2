using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationDispatcherReclaimCommands() =>
    [new("--test-curation-dispatcher-reclaim", 21120, TestCommandPhase.Raw,
        args => args is ["--test-curation-dispatcher-reclaim", _] or ["--test-curation-dispatcher-reclaim", _, "--worker"], (args, _) =>
        {
            int result = CurationDispatcherReclaimTests(Path.GetFullPath(args[1]), args.Length == 3);
            if (args.Length == 3) return result;
            Check("large dispatcher opens complete with a dispatcher-dependent finalizer", result == 0);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static int CurationDispatcherReclaimTests(string output, bool worker = false)
    {
        if (worker)
        {
            if (!NativeProofDesktop.IsPrivate) throw new InvalidOperationException("The finalizer worker requires its owned private desktop.");
            return CurationDispatcherReclaimWorker(output);
        }
        if (Directory.Exists(output)) throw new IOException("Choose a fresh dispatcher reclaim fixture folder.");
        Directory.CreateDirectory(output);
        try
        {
            return NativeProofDesktop.Run(new[] { "--test-curation-dispatcher-reclaim", output, "--worker" },
                Path.Combine(output, "native-dispatcher-reclaim.log"), 120_000);
        }
        catch (TimeoutException error)
        {
            string log = Path.Combine(output, "native-dispatcher-reclaim.log");
            if (File.Exists(log)) Console.Write(File.ReadAllText(log));
            Console.WriteLine(error.Message); return 124;
        }
        finally
        {
            // Only this fresh fixture folder's exact generated input is eligible for cleanup.
            string source = Path.Combine(output, "large-dispatcher.fcf");
            if (File.Exists(source)) File.Delete(source);
        }
    }

    private sealed class DispatcherDependentFinalizer(Dispatcher dispatcher, ManualResetEventSlim entered,
        ManualResetEventSlim finished)
    {
        ~DispatcherDependentFinalizer()
        {
            entered.Set();
            dispatcher.Invoke(() => finished.Set());
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ArmDispatcherFinalizer(Dispatcher dispatcher, ManualResetEventSlim entered,
        ManualResetEventSlim finished) => _ = new DispatcherDependentFinalizer(dispatcher, entered, finished);

    private static int CurationDispatcherReclaimWorker(string output)
    {
        using var log = new StreamWriter(Path.Combine(output, "native-dispatcher-reclaim.log")) { AutoFlush = true };
        var previousOut = Console.Out; var previousError = Console.Error;
        Console.SetOut(log); Console.SetError(log);
        var previousChoice = MainWindow.UnsavedProjectChoiceOverride;
        var previousMessages = ShellMessages.Presenter;
        MainWindow? shell = null;
        try
        {
            string source = Path.Combine(output, "large-dispatcher.fcf");
            GenerateCurationProof(source, 1024, 65536);
            EnsureApplication();
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
            shell = new MainWindow { Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
            shell.Show(); PumpDispatcher();
            foreach (bool asynchronous in new[] { false, true })
            {
                using var entered = new ManualResetEventSlim();
                using var finished = new ManualResetEventSlim();
                using var returned = new ManualResetEventSlim();
                var watchdog = new Thread(() =>
                {
                    if (entered.Wait(TimeSpan.FromSeconds(60)) && !returned.Wait(TimeSpan.FromSeconds(30)))
                    {
                        Console.WriteLine("FAIL dispatcher open did not return after the finalizer requested the dispatcher.");
                        // Exit performs finalizer shutdown too. Terminate only this explicitly marked child.
                        Process.GetCurrentProcess().Kill();
                    }
                }) { IsBackground = true };
                watchdog.Start();
                ArmDispatcherFinalizer(shell.Dispatcher, entered, finished);
                Console.WriteLine("OPEN on dispatcher, async=" + asynchronous);
                long before = GC.GetAllocatedBytesForCurrentThread();
                bool opened;
                if (asynchronous)
                {
                    var pending = shell.OpenProjectSafelyAsync(source);
                    var wait = Stopwatch.StartNew();
                    while (!pending.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(60)) { PumpDispatcher(); Thread.Sleep(1); }
                    opened = pending.IsCompleted && pending.GetAwaiter().GetResult();
                }
                else opened = shell.OpenProjectPath(source);
                returned.Set();
                var drain = Stopwatch.StartNew();
                while (!finished.IsSet && drain.Elapsed < TimeSpan.FromSeconds(30)) { PumpDispatcher(); Thread.Sleep(1); }
                Check("native large open returns on the dispatcher, async=" + asynchronous, opened && shell.Dispatcher.CheckAccess());
                Check("dispatcher-dependent finalizer finishes after open, async=" + asynchronous, entered.IsSet && finished.IsSet);
                if (!asynchronous) Check("native open crosses the 128 MiB allocation gate", GC.GetAllocatedBytesForCurrentThread() - before >= 128L * 1024 * 1024);
                Check("large native input retains all synthetic reference entries", shell.NavigationModel.Document?.EvidenceSource?.ReferencePanel?.Entries.Count == 1024);
                watchdog.Join(1000);
            }
        }
        catch (Exception error) { Check("dispatcher reclaim fixture finishes", false, error.ToString()); }
        finally
        {
            shell?.Close(); PumpDispatcher();
            MainWindow.UnsavedProjectChoiceOverride = previousChoice; ShellMessages.Presenter = previousMessages;
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            Console.SetOut(previousOut); Console.SetError(previousError);
        }
        return failed == 0 ? 0 : 1;
    }
}
