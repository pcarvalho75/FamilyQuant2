using FamilyQuant.Setup.Engine;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;

namespace FamilyQuant.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool silent = args.Contains("/silent", StringComparer.OrdinalIgnoreCase);
        try
        {
            SetupToolTipDefaults.Apply();
            // LK's inert preview path is dispatched before any real platform/controller is constructed.
            if (args.Length == 1 && args[0].StartsWith("/screens=", StringComparison.OrdinalIgnoreCase))
                return InstallerWindowPreview.Run(args[0][9..]);
            var options = SetupCommandLine.Parse(args);
            var controller = new SetupController(options);
            string directory = controller.DefaultDirectory;
            string self = Environment.ProcessPath ?? throw new IOException("Setup cannot find its executable.");
            if (options.Uninstall && SetupPaths.Within(directory, self)) return RelocateUninstaller(options with { Directory = directory }, self);
            if (options.ParentPid is int parent && !WaitForOriginalUninstaller(parent, directory))
                return Finish(options, new(false, 3, "The previous uninstaller did not close. Try again.", directory));
            if (options.Silent)
            {
                var result = options.Uninstall ? controller.UninstallAsync(directory).GetAwaiter().GetResult()
                    : controller.InstallAsync(directory, options.DesktopShortcut).GetAwaiter().GetResult();
                return Finish(options, result);
            }
            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new InstallerWindow(controller);
            application.Run(window);
            return window.ExitCode;
        }
        catch (Exception error)
        {
            if (!silent) MessageBox.Show(error.Message, "FamilyQuant Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            return error is ArgumentException ? 2 : 1;
        }
    }

    private static int Finish(SetupOptions options, SetupResult result)
    {
        if (options.TestMode && options.ResultFile != null)
        {
            // Parse has confined this output to the test root. Revalidate after the operation too.
            _ = SetupCommandLine.Parse(SetupCommandLine.Arguments(options).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(options.ResultFile)!);
            string temporary = options.ResultFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(new { result.Success, result.ExitCode, result.Message, result.Directory,
                    ProcessId = Environment.ProcessId }, SetupProduct.Json));
                File.Move(temporary, options.ResultFile, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        else if (options.Silent)
        {
            Console.Error.WriteLine(result.Message);
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "FamilyQuant2-Setup-" + Environment.ProcessId + ".log"), result.Message); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return result.ExitCode;
    }

    private static int RelocateUninstaller(SetupOptions options, string self)
    {
        string parent = options.TestMode ? options.TestRoot! : Path.GetTempPath();
        SetupPaths.CheckAncestors(parent);
        string folder = Path.Combine(parent, "FamilyQuant2-Uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string worker = Path.Combine(folder, "FamilyQuant2-Setup.exe");
        File.Copy(self, worker);
        var start = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = options.Silent, WorkingDirectory = folder };
        foreach (string argument in SetupCommandLine.Arguments(options with { ParentPid = Environment.ProcessId })) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The uninstall helper could not start.");
        // The helper waits for THIS executable to exit before removing installed files. Its temporary
        // copy is deliberately left for ordinary Windows Temp cleanup; no shell deletion command runs.
        return 0;
    }
    private static bool WaitForOriginalUninstaller(int processId, string directory)
    {
        Process process;
        try { process = Process.GetProcessById(processId); } catch (ArgumentException) { return true; }
        using (process)
        {
            if (process.MainModule?.FileName is not string path || !SetupPaths.Same(path, Path.Combine(directory, SetupProduct.Uninstaller))) return false;
            return process.WaitForExit(60000);
        }
    }
}
