using FamilyQuant.Operations;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

/// <summary>Prepares exports beside their destination; cancellation never publishes a partial file.</summary>
public static class AppOperationFiles
{
    public static void WriteFile(string path, Action<string> write, OperationContext operation)
    {
        operation.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(full)!, ".fq-export-" + Guid.NewGuid().ToString("N") + Path.GetExtension(full));
        try
        {
            write(temporary);
            operation.Report("export", "Publishing the completed file", 1, 1);
            operation.Commit(() => File.Move(temporary, full, true));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void WriteFolder(string path, Action<string> write, OperationContext operation)
    {
        operation.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(full)!, ".fq-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            write(temporary);
            operation.Report("export", "Publishing the completed exports", 1, 1);
            operation.Commit(() =>
            {
                Directory.CreateDirectory(full);
                foreach (string source in Directory.GetFiles(temporary, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                {
                    string destination = Path.Combine(full, Path.GetRelativePath(temporary, source));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(source, destination, true);
                }
            });
        }
        finally { Directory.Delete(temporary, true); }
    }

    // WPF figure writers require an STA. Their charts are created and discarded on this owned thread.
    public static Task RunSta(Action work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { work(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
