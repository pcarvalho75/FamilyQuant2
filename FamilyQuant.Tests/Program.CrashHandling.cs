using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string PrivateCrashPayload = "PRIVATE_CRASH_PAYLOAD C:\\private\\project.fqproj PRIVATE_PEPTIDE";

    [TestCommandTable]
    private static IEnumerable<TestCommand> CrashCommands() =>
    [new("--test-crash-handler", 10810, TestCommandPhase.Raw,
        args => args.Length == 1 && args[0] == "--test-crash-handler", (_, _) =>
        {
            EnsureApplication(); CrashHandlerTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    new("--crash-probe", 10811, TestCommandPhase.Raw,
        args => args.Length == 3 && args[0] == "--crash-probe", (args, _) => CrashProbe(args[1], args[2]))];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CrashInnerProbe() => throw new ArgumentException(PrivateCrashPayload);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CrashOuterProbe()
    {
        try { CrashInnerProbe(); }
        catch (Exception inner)
        {
            var error = new InvalidOperationException(PrivateCrashPayload, inner);
            error.Data["private"] = PrivateCrashPayload;
            throw error;
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MakeUnobservedCrashTask()
    {
        var task = Task.Run((Action)CrashOuterProbe);
        if (!SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(5))) throw new TimeoutException("Crash task fixture did not complete.");
        // Do not read Exception, await or Wait: those would observe the task's exception.
    }

    private static int CrashProbe(string mode, string root)
    {
        root = Path.GetFullPath(root);
        string prefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FamilyQuant-CrashTests-"));
        if (!root.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use the owned temporary crash fixture root.");
        Directory.CreateDirectory(root);
        EnsureApplication();
        if (mode == "native") return NativeCrashDialogProbe(root);
        using var hooks = new CrashHandling(Application.Current, new CrashReportStore(Path.Combine(root, "logs")),
            notify: notice => File.WriteAllText(Path.Combine(root, "notice.txt"), notice.Message),
            terminate: _ => Environment.Exit(19));
        using var operation = new AppOperationHost().Begin("Group by evidence");
        if (mode == "dispatcher")
        {
            Dispatcher.CurrentDispatcher.BeginInvoke((Action)CrashOuterProbe);
            Dispatcher.Run();
        }
        else if (mode == "domain")
        {
            new Thread(CrashOuterProbe).Start();
            Dispatcher.Run();
        }
        else if (mode == "task")
        {
            int notification = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> observer = (_, args) =>
                Volatile.Write(ref notification, args.Observed ? 1 : -1);
            TaskScheduler.UnobservedTaskException += observer;
            try
            {
                MakeUnobservedCrashTask();
                var deadline = Stopwatch.StartNew();
                while (Volatile.Read(ref notification) == 0 && deadline.Elapsed < TimeSpan.FromSeconds(10))
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    Thread.Sleep(20);
                }
                bool dispatcherRunning = false;
                Dispatcher.CurrentDispatcher.BeginInvoke((Action)(() => dispatcherRunning = true));
                PumpDispatcher();
                if (Volatile.Read(ref notification) != 1 || !dispatcherRunning) return 20;
                File.WriteAllText(Path.Combine(root, "task-alive.txt"), "Observed=True; DispatcherRunning=True");
                return 0;
            }
            finally { TaskScheduler.UnobservedTaskException -= observer; }
        }
        else throw new ArgumentException("Unknown crash fixture mode.");
        return 20; // Only the registered fatal handler may return the expected exit 19.
    }

    private static void CrashHandlerTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant-CrashTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (string mode in new[] { "dispatcher", "task", "domain" })
        {
            string output = Path.Combine(root, mode);
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "FamilyQuant.Tests.exe"))
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory,
                  RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "--crash-probe", mode, output }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Owned crash fixture did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            var watch = Stopwatch.StartNew();
            while (!process.WaitForExit(100))
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(25))
                { process.Kill(true); process.WaitForExit(); throw new TimeoutException("Owned crash fixture timed out: " + mode); }
            }
            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            bool nonfatal = mode == "task";
            Check(nonfatal ? "Z11b real task fault logs and keeps the app alive" : "Z11b real " + mode + " fault reaches the fatal handler and exits",
                process.ExitCode == (nonfatal ? 0 : 19),
                "exit=" + process.ExitCode + "; " + stderr.Result);
            string[] reports = Directory.Exists(Path.Combine(output, "logs"))
                ? Directory.GetFiles(Path.Combine(output, "logs"), "crash-*.txt") : Array.Empty<string>();
            bool hasNotice = File.Exists(Path.Combine(output, "notice.txt"));
            Check("Z11b " + mode + " writes one report with the appropriate notice policy", reports.Length == 1 && hasNotice == !nonfatal);
            if (reports.Length != 1) continue;
            string report = File.ReadAllText(reports[0]);
            Check("Z11b " + mode + " retains version Windows operation and complete exception stacks", report.Contains("Time: ", StringComparison.Ordinal)
                && report.Contains("FamilyQuant version: " + AppVersionInfo.Current(), StringComparison.Ordinal)
                && report.Contains("Windows version: ", StringComparison.Ordinal) && report.Contains("Operation: Group by evidence", StringComparison.Ordinal)
                && report.Contains("System.InvalidOperationException", StringComparison.Ordinal) && report.Contains("System.ArgumentException", StringComparison.Ordinal)
                && report.Contains(nameof(CrashOuterProbe), StringComparison.Ordinal) && report.Contains(nameof(CrashInnerProbe), StringComparison.Ordinal));
            Check("Z11b " + mode + " excludes exception message and Data payloads", !report.Contains("PRIVATE_", StringComparison.Ordinal)
                && !report.Contains(@"C:\private", StringComparison.Ordinal));
            if (nonfatal)
            {
                Check("Z11b unobserved task is marked nonfatal and observed before dispatcher work continues", report.Contains("Severity: Nonfatal", StringComparison.Ordinal)
                    && File.Exists(Path.Combine(output, "task-alive.txt"))
                    && File.ReadAllText(Path.Combine(output, "task-alive.txt")) == "Observed=True; DispatcherRunning=True");
            }
            else
            {
                string notice = hasNotice ? File.ReadAllText(Path.Combine(output, "notice.txt")) : "";
                Check("Z11b " + mode + " tells the user the saved fatal report path", report.Contains("Severity: Fatal", StringComparison.Ordinal)
                    && notice == new CrashNotice(reports[0], Path.GetDirectoryName(reports[0])!).Message
                    && !notice.Contains('\u2013') && !notice.Contains('\u2014'));
            }
        }

        string failure = Path.Combine(root, "not-a-folder"); File.WriteAllText(failure, "block directory creation");
        int notices = 0, exits = 0; CrashNotice? failedNotice = null;
        using (var hooks = new CrashHandling(Application.Current, new CrashReportStore(Path.Combine(failure, "logs")),
            notice => { notices++; failedNotice = notice; throw new InvalidOperationException("Broken presenter fixture"); },
            _ => { exits++; throw new InvalidOperationException("Broken exit fixture"); },
            () => throw new InvalidOperationException(PrivateCrashPayload)))
        {
            hooks.Handle(new Exception(PrivateCrashPayload), CrashOrigin.Dispatcher);
            hooks.Handle(new Exception(PrivateCrashPayload), CrashOrigin.UnobservedTask);
        }
        Check("Z11b writer provider presenter and exit failures never escape or duplicate the notice", notices == 1 && exits == 1
            && failedNotice is { ReportPath: null } && !failedNotice.Message.Contains("A report was saved", StringComparison.Ordinal));
        int quietNotices = 0, quietExits = 0;
        using (var hooks = new CrashHandling(Application.Current, new CrashReportStore(Path.Combine(failure, "logs")),
            _ => quietNotices++, _ => quietExits++, () => throw new InvalidOperationException(PrivateCrashPayload)))
            hooks.Handle(new Exception(PrivateCrashPayload), CrashOrigin.UnobservedTask);
        Check("Z11b a nonfatal report failure keeps the application running quietly", quietNotices == 0 && quietExits == 0);
        var store = new CrashReportStore(Path.Combine(root, "direct"));
        string? first = store.TryWrite(new Exception(PrivateCrashPayload), CrashOrigin.Dispatcher, PrivateCrashPayload);
        string? second = store.TryWrite(new Exception(PrivateCrashPayload), CrashOrigin.Dispatcher, null);
        Check("Z11b rapid reports cannot overwrite an earlier report", first != null && second != null && first != second && File.Exists(first) && File.Exists(second));
        Check("Z11b arbitrary operation titles cannot disclose user data", first != null && !File.ReadAllText(first).Contains("PRIVATE_", StringComparison.Ordinal));

        string mixedDirectory = Path.Combine(root, "nonfatal-then-fatal");
        int mixedNotices = 0, mixedExits = 0;
        using (var hooks = new CrashHandling(Application.Current, new CrashReportStore(mixedDirectory),
            _ => mixedNotices++, _ => mixedExits++))
        {
            hooks.Handle(new Exception(PrivateCrashPayload), CrashOrigin.UnobservedTask);
            hooks.Handle(new Exception(PrivateCrashPayload), CrashOrigin.UnobservedTask);
            Check("Z11b nonfatal reports keep notification and termination callbacks idle", mixedNotices == 0 && mixedExits == 0
                && Directory.GetFiles(mixedDirectory, "crash-*.txt").Length == 2);
            hooks.Handle(new Exception(PrivateCrashPayload), CrashOrigin.Dispatcher);
            hooks.Handle(new Exception(PrivateCrashPayload), CrashOrigin.AppDomain);
            var mixedReports = Directory.GetFiles(mixedDirectory, "crash-*.txt").Select(File.ReadAllText).ToArray();
            Check("Z11b task logging preserves the later single fatal notice and exit", mixedNotices == 1 && mixedExits == 1
                && mixedReports.Count(text => text.Contains("Severity: Nonfatal", StringComparison.Ordinal)) == 2
                && mixedReports.Count(text => text.Contains("Severity: Fatal", StringComparison.Ordinal)) == 1);
        }

        string? oldDirectory = CrashReportStore.DirectoryOverride;
        var oldOpener = CrashReportStore.FolderOpenerOverride;
        MainWindow? main = null;
        try
        {
            string logs = Path.Combine(root, "help-logs"); string? opened = null;
            CrashReportStore.DirectoryOverride = logs;
            CrashReportStore.FolderOpenerOverride = directory => { opened = directory; return true; };
            main = new MainWindow();
            var menu = (MenuItem)main.FindName("MenuItemOpenErrorLogs");
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check("Z11b Help opens the error logs folder without requiring an existing report", menu.Header as string == "Open error logs folder"
                && opened == logs && Directory.Exists(logs));
        }
        finally { main?.Close(); CrashReportStore.DirectoryOverride = oldDirectory; CrashReportStore.FolderOpenerOverride = oldOpener; }
        var native = typeof(App).Assembly.GetType("FamilyQuant.CrashNoticeDialog")!;
        Check("Z11b native dialog configuration matches the Windows packed layout", Marshal.SizeOf(native.GetNestedType("Config", BindingFlags.NonPublic)!) == (IntPtr.Size == 8 ? 160 : 96));
        string nativeRoot = Path.Combine(root, "native");
        var nativeStart = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "FamilyQuant.Tests.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "--crash-probe", "native", nativeRoot }) nativeStart.ArgumentList.Add(arg);
        using var nativeProcess = Process.Start(nativeStart) ?? throw new IOException("Owned native dialog probe did not start.");
        if (!nativeProcess.WaitForExit(15000)) { nativeProcess.Kill(true); nativeProcess.WaitForExit(); throw new TimeoutException("Owned native dialog probe timed out."); }
        Check("Z11b native Open folder button runs once and Close dismisses the same notice", nativeProcess.ExitCode == 19
            && File.ReadAllText(Path.Combine(nativeRoot, "native-proof.txt")) == "Shown=True; Opened=1");
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);
    private static int NativeCrashDialogProbe(string root)
    {
        int opened = 0;
        CrashReportStore.FolderOpenerOverride = _ => { opened++; return true; };
        var native = typeof(App).Assembly.GetType("FamilyQuant.CrashNoticeDialog")!;
        Action<IntPtr> created = window =>
        {
            SetWindowPos(window, IntPtr.Zero, -30000, -30000, 0, 0, 0x15);
            PostMessage(window, 0x466, (IntPtr)100, IntPtr.Zero); // TDM_CLICK_BUTTON: Open folder.
            PostMessage(window, 0x466, (IntPtr)8, IntPtr.Zero); // Close.
        };
        bool shown = (bool)native.GetMethod("TryShowNative", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { new CrashNotice(Path.Combine(root, "crash-synthetic.txt"), root), created })!;
        File.WriteAllText(Path.Combine(root, "native-proof.txt"), "Shown=" + shown + "; Opened=" + opened);
        return shown && opened == 1 ? 19 : 20;
    }
}
