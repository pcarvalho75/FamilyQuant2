using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FamilyQuant;

public enum CrashOrigin { Dispatcher, AppDomain, UnobservedTask }

public sealed record CrashNotice(string? ReportPath, string Directory)
{
    public string Message => ReportPath != null
        ? "FamilyQuant hit an unexpected error and must close. A report was saved: " + ReportPath + ". Please send it to the developers."
        : "FamilyQuant hit an unexpected error and must close. The report could not be saved. Please contact the developers.";
}

/// <summary>Diagnostic metadata only: never serializes a project, exception messages, Data or progress text.</summary>
public sealed class CrashReportStore
{
    public static string? DirectoryOverride { get; set; }
    public static Func<string, bool>? FolderOpenerOverride { get; set; }
    public static string DefaultDirectory => DirectoryOverride ?? Path.Combine(ApplicationPaths.LocalDataDirectory, "logs");
    public string Directory { get; }
    public CrashReportStore(string directory) => Directory = directory;

    public string? TryWrite(Exception? error, CrashOrigin origin, string? operation)
    {
        string? temporary = null;
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var now = DateTimeOffset.Now;
            string prefix = "crash-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            temporary = Path.Combine(Directory, "." + prefix + "-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
                writer.WriteLine("FamilyQuant crash report");
                writer.WriteLine("Time: " + now.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteLine("FamilyQuant version: " + AppVersionInfo.Current());
                writer.WriteLine("Windows version: " + Environment.OSVersion.VersionString);
                writer.WriteLine("Origin: " + origin);
                writer.WriteLine("Operation: " + SafeOperation(operation));
                writer.WriteLine("Exception messages and custom data are omitted to protect project data. Full available stacks follow.");
                WriteExceptions(writer, error);
                writer.Flush(); stream.Flush(flushToDisk: true);
            }
            for (int suffix = 0; suffix < 100; suffix++)
            {
                string path = Path.Combine(Directory, prefix + (suffix == 0 ? "" : "-" + suffix.ToString(CultureInfo.InvariantCulture)) + ".txt");
                try { File.Move(temporary!, path); temporary = null; return path; }
                catch (IOException) when (File.Exists(path)) { }
            }
        }
        catch { /* A diagnostic failure must never replace the original crash. */ }
        finally { try { if (temporary != null) File.Delete(temporary); } catch { } }
        return null;
    }

    private static void WriteExceptions(TextWriter writer, Exception? error)
    {
        if (error == null) { writer.WriteLine("Non-Exception failure object; payload omitted."); return; }
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(Exception Error, string Relation)>();
        pending.Push((error, "Root"));
        int index = 0;
        while (pending.Count > 0)
        {
            var item = pending.Pop();
            if (!seen.Add(item.Error)) { writer.WriteLine("Repeated exception reference omitted."); continue; }
            int current = index++;
            writer.WriteLine();
            writer.WriteLine("Exception " + current.ToString(CultureInfo.InvariantCulture) + " (" + item.Relation + ")");
            writer.WriteLine("Type: " + item.Error.GetType().FullName);
            writer.WriteLine("HResult: 0x" + item.Error.HResult.ToString("X8", CultureInfo.InvariantCulture));
            writer.WriteLine("Stack trace:");
            try { writer.WriteLine(item.Error.StackTrace ?? "No stack trace was captured."); }
            catch { writer.WriteLine("Stack trace unavailable."); }
            if (item.Error is AggregateException aggregate)
            {
                for (int child = aggregate.InnerExceptions.Count - 1; child >= 0; child--)
                    pending.Push((aggregate.InnerExceptions[child], "Inner " + child.ToString(CultureInfo.InvariantCulture) + " of " + current.ToString(CultureInfo.InvariantCulture)));
            }
            else if (item.Error.InnerException != null)
                pending.Push((item.Error.InnerException, "Inner of " + current.ToString(CultureInfo.InvariantCulture)));
        }
    }

    // Only fixed UI operation labels may enter the report. Arbitrary titles can contain file names.
    private static string SafeOperation(string? value) => value switch
    {
        "Group by evidence" or "Read database and scope" or "Read grouping task" or "Fetch evidence" or
        "Fetch family world" or "Retry panel" or "Quantify" or "Build families" or "Open project" or
        "Save project" or "Export families" or "Prepare results" or "Prepare report" or "Export report" or
        "Export results" or "Clustering diagnostic" or "Prepare spectrum comparison" or "Reannotate SEPro" or
        "Check for updates" or "Install update" or "Load and filter de novo" or "Reconcile de novo" or
        "Extract de novo XIC" or "Search peptide families" or "Build de novo partition" or "Attribute full XIC to families" => value,
        null or "" => "Not known",
        _ => "Other operation (name omitted for privacy)"
    };

    public static bool TryOpenFolder(string directory)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            if (FolderOpenerOverride != null) return FolderOpenerOverride(directory);
            using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(directory)) { UseShellExecute = true });
            return process != null;
        }
        catch { return false; }
    }
}

/// <summary>One fatal notice per registration; test seams avoid a real dialog or terminating the fixture.</summary>
public sealed class CrashHandling : IDisposable
{
    private readonly Application application;
    private readonly CrashReportStore store;
    private readonly Action<CrashNotice> notify;
    private readonly Action<int> terminate;
    private readonly Func<string?> operation;
    private int handling;
    public CrashHandling(Application application, CrashReportStore store, Action<CrashNotice>? notify = null,
        Action<int>? terminate = null, Func<string?>? operation = null)
    {
        this.application = application; this.store = store;
        this.notify = notify ?? CrashNoticeDialog.Show;
        this.terminate = terminate ?? Environment.Exit;
        this.operation = operation ?? (() => AppOperationHost.ActiveCrashOperation);
        application.DispatcherUnhandledException += OnDispatcher;
        AppDomain.CurrentDomain.UnhandledException += OnDomain;
        TaskScheduler.UnobservedTaskException += OnTask;
    }
    private void OnDispatcher(object sender, DispatcherUnhandledExceptionEventArgs args)
    { args.Handled = true; Handle(args.Exception, CrashOrigin.Dispatcher); }
    private void OnDomain(object sender, UnhandledExceptionEventArgs args) => Handle(args.ExceptionObject as Exception, CrashOrigin.AppDomain);
    private void OnTask(object? sender, UnobservedTaskExceptionEventArgs args)
    { args.SetObserved(); Handle(args.Exception, CrashOrigin.UnobservedTask); }
    public void Handle(Exception? error, CrashOrigin origin)
    {
        if (Interlocked.Exchange(ref handling, 1) != 0) return;
        try
        {
            string? title = null;
            try { title = operation(); } catch { }
            string? report = store.TryWrite(error, origin, title);
            try { notify(new(report, store.Directory)); } catch { }
        }
        catch { }
        finally { try { terminate(-1); } catch { } }
    }
    public void Dispose()
    {
        application.DispatcherUnhandledException -= OnDispatcher;
        AppDomain.CurrentDomain.UnhandledException -= OnDomain;
        TaskScheduler.UnobservedTaskException -= OnTask;
    }
}
