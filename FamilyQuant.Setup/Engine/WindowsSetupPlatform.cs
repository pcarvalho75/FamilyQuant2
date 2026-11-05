using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FamilyQuant.Setup.Engine;

/// <summary>Per-user Windows adapter. Never used by test mode. All registry operations use this
/// product's fixed HKCU key; shortcut operations touch only the two named user shortcuts.</summary>
public sealed class WindowsSetupPlatform : ISetupPlatform
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FamilyQuant2";
    private static readonly string StartShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FamilyQuant 2.lnk");
    private static readonly string DesktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FamilyQuant 2.lnk");
    public string UserDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SetupProduct.Id);
    private sealed record Snapshot(Dictionary<string, (object Value, RegistryValueKind Kind)>? Registry, byte[]? Start, byte[]? Desktop);

    public InstallRegistration? GetRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue("InstallLocation") is string path && key.GetValue("DisplayVersion") is string version
            ? new(path, version, Convert.ToInt64(key.GetValue("EstimatedSize", 0)) * 1024)
                { DesktopShortcut = Convert.ToInt32(key.GetValue("DesktopShortcut", 0)) == 1 } : null;
    }
    public object CaptureIntegration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return new Snapshot(key?.GetValueNames().ToDictionary(name => name, name => (key.GetValue(name)!, key.GetValueKind(name))),
            File.Exists(StartShortcut) ? File.ReadAllBytes(StartShortcut) : null,
            File.Exists(DesktopShortcut) ? File.ReadAllBytes(DesktopShortcut) : null);
    }
    public void RestoreIntegration(object snapshot)
    {
        var before = (Snapshot)snapshot;
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
        if (before.Registry != null)
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            foreach (var value in before.Registry) key.SetValue(value.Key, value.Value.Value, value.Value.Kind);
        }
        RestoreShortcut(StartShortcut, before.Start); RestoreShortcut(DesktopShortcut, before.Desktop);
    }
    public void Register(InstallRegistration registration, bool desktopShortcut)
    {
        string exe = Path.Combine(registration.Directory, SetupProduct.AppExe);
        string uninstall = Quote(Path.Combine(registration.Directory, SetupProduct.Uninstaller)) + " /uninstall /dir=" + Quote(registration.Directory);
        // Refuse to overwrite unrelated shortcuts that happen to use our display name.
        foreach (string path in new[] { StartShortcut, DesktopShortcut })
            if (File.Exists(path) && !ShortcutTargets(path, exe)) throw new IOException("A shortcut with this name points to a different program: " + path);
        CreateShortcut(StartShortcut, exe);
        if (desktopShortcut) CreateShortcut(DesktopShortcut, exe);
        else if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("DisplayName", "FamilyQuant 2"); key.SetValue("DisplayVersion", registration.Version);
        key.SetValue("Publisher", "Fundação Oswaldo Cruz (Fiocruz)"); key.SetValue("InstallLocation", registration.Directory);
        key.SetValue("DisplayIcon", Quote(exe) + ",0"); key.SetValue("UninstallString", uninstall);
        key.SetValue("QuietUninstallString", uninstall + " /silent");
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, (registration.SizeBytes + 1023) / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("DesktopShortcut", desktopShortcut ? 1 : 0, RegistryValueKind.DWord);
    }
    public void Unregister(string directory)
    {
        if (GetRegistration() is { } registration && !SetupPaths.Same(registration.Directory, directory))
            throw new IOException("The uninstall entry belongs to a different program folder.");
        string exe = Path.Combine(directory, SetupProduct.AppExe);
        foreach (string path in new[] { StartShortcut, DesktopShortcut })
            if (File.Exists(path) && ShortcutTargets(path, exe)) File.Delete(path);
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
    }
    public bool IsAppRunning(string directory)
    {
        string exe = Path.Combine(directory, SetupProduct.AppExe);
        foreach (var process in Process.GetProcessesByName("FamilyQuant2"))
            using (process)
                try { if (process.MainModule?.FileName is string path && SetupPaths.Same(path, exe)) return true; }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException) { if (!process.HasExited) return true; }
        return false;
    }
    public bool WaitForExit(int processId, TimeSpan timeout, CancellationToken token)
    {
        Process process;
        try { process = Process.GetProcessById(processId); } catch (ArgumentException) { return true; }
        using (process)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < timeout)
            {
                token.ThrowIfCancellationRequested();
                if (process.WaitForExit(100)) return true;
            }
            return process.HasExited;
        }
    }
    public Version? ReadPortableVersion(string appPath)
    {
        if (!File.Exists(appPath)) return null;
        var info = FileVersionInfo.GetVersionInfo(appPath);
        return info.ProductName == SetupProduct.Id && Version.TryParse(info.FileVersion, out var version) ? version : null;
    }
    public void Launch(string directory)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(directory, SetupProduct.AppExe))
            { WorkingDirectory = directory, UseShellExecute = true });
        if (process == null) throw new IOException("FamilyQuant could not be started.");
    }
    private static string Quote(string path) => "\"" + path + "\"";
    private static void RestoreShortcut(string path, byte[]? bytes)
    {
        if (bytes == null) { if (File.Exists(path)) File.Delete(path); }
        else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
    }
    private static bool ShortcutTargets(string path, string exe) => WithShortcut(path, shortcut => SetupPaths.Same((string)shortcut.TargetPath, exe));
    private static void CreateShortcut(string path, string exe)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WithShortcut(path, shortcut => { shortcut.TargetPath = exe; shortcut.WorkingDirectory = Path.GetDirectoryName(exe)!;
            shortcut.IconLocation = exe + ",0"; shortcut.Description = "FamilyQuant 2"; shortcut.Save(); return true; });
    }
    private static T WithShortcut<T>(string path, Func<dynamic, T> action)
    {
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcut support is unavailable."));
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            return action((dynamic)shortcut);
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
