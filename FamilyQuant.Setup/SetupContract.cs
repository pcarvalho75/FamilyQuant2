namespace FamilyQuant.Setup;

/// <summary>Shared SH engine / LK window seam. No machine changes are made by these types.</summary>
public sealed record SetupOptions
{
    public string? Directory { get; init; }
    public bool Silent { get; init; }
    public bool Uninstall { get; init; }
    public bool DesktopShortcut { get; init; }
    public bool TestMode { get; init; }
    public string? TestRoot { get; init; }
    public int? WaitPid { get; init; }
    public int? ParentPid { get; init; }
    public string? ResultFile { get; init; }
}

public sealed record SetupProgress(string Message, int Completed, int Total);
public sealed record ExistingInstallation(string Directory, string Version, bool Registered, bool HasManifest);
public sealed record SetupResult(bool Success, int ExitCode, string Message, string? Directory = null);

/// <summary>LK owns InstallerWindow(ISetupController). SH owns this interface and its implementation.
/// Progress is reported on the caller-supplied IProgress context. Call Launch only after success and
/// the person's explicit finish-screen choice. Quiet mode uses no window and never launches the app.</summary>
public interface ISetupController
{
    SetupOptions Options { get; }
    string DefaultDirectory { get; }
    string PayloadVersion { get; }
    ExistingInstallation? DetectExisting();
    bool IsAppRunning(string directory);
    Task<SetupResult> InstallAsync(string directory, bool desktopShortcut, IProgress<SetupProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<SetupResult> UninstallAsync(string directory, IProgress<SetupProgress>? progress = null,
        CancellationToken cancellationToken = default);
    void Launch(string directory);
}
